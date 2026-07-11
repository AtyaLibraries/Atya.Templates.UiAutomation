using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using UiAutomation.Framework.Configuration;
using Xunit;

namespace UiAutomation.Framework.Composition;

/// <summary>
/// The xUnit v3 assembly fixture holding immutable, thread-safe shared services (Section 9.1):
/// validated configuration, the DI root, the process concurrency gate, and the process-global
/// Playwright assertion timeout. It must not store per-test state (current page, user, or id).
///
/// Register it in the test assembly with:
/// <c>[assembly: AssemblyFixture(typeof(HostFixture))]</c>.
/// </summary>
public sealed class HostFixture : IAsyncLifetime
{
    internal IServiceProvider Services { get; private set; } = null!;

    internal TestConfiguration Configuration { get; private set; } = null!;

    internal SemaphoreSlim ConcurrencyGate { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        Services = await Bootstrapper.BuildAsync();
        Configuration = Services.GetRequiredService<TestConfiguration>();

        // The Playwright xUnit base selects its worker browser from the BROWSER environment
        // variable. One profile runs per process (Section 6.5), so translating the active
        // profile's browser here — before any worker browser launches — is the Phase 1
        // adapter the compatibility spike validates for the wider matrix.
        Environment.SetEnvironmentVariable("BROWSER", Configuration.Profile.Browser switch
        {
            BrowserKind.Firefox => "firefox",
            BrowserKind.WebKit => "webkit",
            _ => "chromium",
        });

        // The process-local throttle sized from the active profile (Section 6.4).
        ConcurrencyGate = new SemaphoreSlim(Configuration.Profile.MaxParallelTests, Configuration.Profile.MaxParallelTests);

        // Playwright's assertion timeout is process-global and must not be changed concurrently
        // mid-run, so it is set once here (Section 13).
        Assertions.SetDefaultExpectTimeout((float)Configuration.Profile.AssertionTimeout.TotalMilliseconds);
    }

    public async ValueTask DisposeAsync()
    {
        ConcurrencyGate?.Dispose();
        if (Services is IAsyncDisposable disposable)
        {
            await disposable.DisposeAsync();
        }
    }
}
