using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Microsoft.Playwright.Xunit.v3;
using UiAutomation.Framework.Cleanup;
using UiAutomation.Framework.Configuration;
using UiAutomation.Framework.Reporting;
using UiAutomation.Framework.Users;
using Xunit;

namespace UiAutomation.Framework.Composition;

/// <summary>
/// The framework test base (Section 8.2). Derives from the official Playwright xUnit v3
/// <see cref="BrowserTest"/> so the framework owns actor-context creation (ADR-002), and
/// exposes the single public execution entry point <see cref="RunScenarioAsync{TScenario}(ActorPlan,Func{ScenarioExecution{TScenario},ValueTask})"/>.
///
/// Tests never see a lifecycle object: scope creation/disposal, actor acquisition/release,
/// context creation/close, diagnostics, cleanup, concurrency-permit release, and final failure
/// selection are all framework-ordered inside <see cref="ScenarioScope"/>.
/// </summary>
public abstract class UiTestBase(HostFixture host) : BrowserTest
{
    private int _wrapperCalls;

    /// <summary>Single-actor convenience form (Section 9.2).</summary>
    protected ValueTask RunScenarioAsync<TScenario>(
        UserLeaseRequest actor,
        Func<ScenarioExecution<TScenario>, ValueTask> body)
        where TScenario : notnull
        => RunScenarioAsync(new ActorPlan(actor), body);

    /// <summary>General form: named actor slots (Section 9.2).</summary>
    protected async ValueTask RunScenarioAsync<TScenario>(
        ActorPlan plan,
        Func<ScenarioExecution<TScenario>, ValueTask> body)
        where TScenario : notnull
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(body);

        // One-shot per test: a second call fails deterministically (Section 9.2).
        if (Interlocked.Exchange(ref _wrapperCalls, 1) == 1)
        {
            throw new InvalidOperationException("RunScenarioAsync may be called only once per test.");
        }

        var config = host.Configuration;
        var testToken = TestContext.Current?.CancellationToken ?? CancellationToken.None;

        // Capability gating runs before any setup cost is incurred but still counts as a
        // wrapper call, so a capability skip and the zero-call guard never conflict.
        ApplyCapabilityGating(config);

        await host.ConcurrencyGate.WaitAsync(testToken).ConfigureAwait(false);
        var permit = new SemaphorePermit(host.ConcurrencyGate);

        AsyncServiceScope scope = default;
        TestActorManager? actors = null;
        var createdContexts = new List<(IBrowserContext Context, IPage Page)>();
        var scopeOwnsTeardown = false;

        try
        {
            scope = host.Services.CreateAsyncScope();
            var services = scope.ServiceProvider;

            var identity = BuildIdentity(config);
            services.GetRequiredService<TestIdentityAccessor>().Set(identity);
            var paths = ArtifactPaths.For(config.ContentRoot, identity);

            // Actor-first: acquire the declared slot set BEFORE composing any scenario graph.
            actors = services.GetRequiredService<TestActorManager>();
            await actors.AcquireAsync(plan, testToken).ConfigureAwait(false);

            // Create the (single, Phase 1) actor's context and page from the worker browser.
            var context = await Browser.NewContextAsync(BuildContextOptions(config)).ConfigureAwait(false);
            var page = await context.NewPageAsync().ConfigureAwait(false);
            page.SetDefaultTimeout((float)config.Profile.ActionTimeout.TotalMilliseconds);
            page.SetDefaultNavigationTimeout((float)config.Profile.NavigationTimeout.TotalMilliseconds);
            createdContexts.Add((context, page));
            services.GetRequiredService<PageHandle>().Set(page);

            var capture = new PlaywrightCaptureController(context, config.Profile, paths);
            await capture.StartAsync().ConfigureAwait(false);

            var diagnostics = new PlaywrightFailureDiagnostics(page, paths);
            var contextSet = new ActorContextSet(createdContexts);
            var cleanup = services.GetRequiredService<ITestCleanupCoordinator>();

            var scenario = services.GetRequiredService<TScenario>();
            var execution = new ScenarioExecution<TScenario>(scenario, actors, testToken);

            var scenarioScope = new ScenarioScope(
                diagnostics,
                capture,
                cleanup,
                contextSet,
                actors,
                scope,
                permit,
                new TestContextReporter(),
                teardownToken: CancellationToken.None);

            // From here the scenario scope owns every resource and the whole teardown sequence.
            scopeOwnsTeardown = true;
            await scenarioScope.RunAsync(() => body(execution)).ConfigureAwait(false);
        }
        catch when (!scopeOwnsTeardown)
        {
            // Transactional rollback (Section 9.3): setup failed before the scenario scope took
            // ownership, so unwind everything already acquired in reverse order.
            foreach (var (context, _) in createdContexts)
            {
                try
                {
                    await context.CloseAsync().ConfigureAwait(false);
                }
                catch (Exception)
                {
                    // Best-effort during rollback.
                }
            }

            if (actors is not null)
            {
                try
                {
                    await actors.ReleaseAllAsync().ConfigureAwait(false);
                }
                catch (Exception)
                {
                    // Best-effort during rollback.
                }
            }

            try
            {
                await scope.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception)
            {
                // Best-effort during rollback.
            }

            permit.Release();
            throw;
        }
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        GC.SuppressFinalize(this);

        // Post-test guard (Section 9.2): a UiTestBase test must run its body through the wrapper.
        if (Volatile.Read(ref _wrapperCalls) == 0)
        {
            throw new InvalidOperationException(
                "RunScenarioAsync was never called. A UiTestBase test must run its entire body through the wrapper.");
        }
    }

    private static void ApplyCapabilityGating(TestConfiguration config)
    {
        var traits = TestContext.Current?.Test?.Traits;
        if (traits is null)
        {
            return;
        }

        if (traits.TryGetValue("RequiresCapability", out var capabilities))
        {
            foreach (var capability in capabilities)
            {
                if (!config.Environment.Capabilities.Contains(capability))
                {
                    Assert.Skip($"Environment '{config.Environment.Name}' lacks capability '{capability}'.");
                }
            }
        }

        if (traits.TryGetValue("RequiresFeature", out var features))
        {
            foreach (var feature in features)
            {
                if (!(config.Environment.Features.TryGetValue(feature, out var enabled) && enabled))
                {
                    Assert.Skip($"Environment '{config.Environment.Name}' lacks feature '{feature}'.");
                }
            }
        }
    }

    private static BrowserNewContextOptions BuildContextOptions(TestConfiguration config)
    {
        var profile = config.Profile;
        return new BrowserNewContextOptions
        {
            // Base URL is environment-owned, never a profile value (ADR-003 / Section 8.2).
            BaseURL = config.Environment.BaseUrl.ToString(),
            ViewportSize = new ViewportSize { Width = profile.ViewportWidth, Height = profile.ViewportHeight },
            Locale = profile.Locale,
            TimezoneId = profile.TimeZoneId,
        };
    }

    private static TestIdentity BuildIdentity(TestConfiguration config)
    {
        var caseId = TestContext.Current?.Test?.TestDisplayName ?? "unknown-case";
        return new TestIdentity
        {
            CaseId = caseId,
            ExecutionId = Guid.NewGuid().ToString("N")[..12],
            TestName = caseId,
            Environment = config.Environment.Name.ToString(),
            Profile = config.Profile.Name,
            Browser = config.Profile.Browser.ToString(),
        };
    }
}
