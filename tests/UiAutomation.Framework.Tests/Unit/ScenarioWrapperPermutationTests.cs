using UiAutomation.Framework.Composition;
using Xunit;

namespace UiAutomation.Framework.Tests.Unit;

/// <summary>
/// The mandatory execution-wrapper permutation suite (docs/architecture.md Section 26.1).
/// Per the doc, "the architecture is not production-safe until these pass against the actual
/// wrapper" — a written review is not the confidence gate, this suite is. Every case runs
/// against the real <see cref="ScenarioScope"/> with fault-injected collaborators.
/// </summary>
[Trait("Category", "Permutation")]
public sealed class ScenarioWrapperPermutationTests
{
    private static Func<ValueTask> Passing() => () => ValueTask.CompletedTask;

    private static Func<ValueTask> Failing(Exception failure) => () => ValueTask.FromException(failure);

    // ---- Failure preservation: the original body exception is always primary ----

    [Fact]
    public async Task Body_fails_diagnostics_succeed_original_is_primary()
    {
        var harness = new ScenarioScopeHarness();
        var scope = harness.Build();
        var body = new BodyException("boom");

        var thrown = await Assert.ThrowsAsync<BodyException>(() => scope.RunAsync(Failing(body)).AsTask());

        Assert.Same(body, thrown);
        Assert.Equal(1, harness.Diagnostics.Calls);
        Assert.Equal(ScenarioOutcome.BodyFailed, scope.Result!.Outcome);
    }

    [Fact]
    public async Task Body_fails_diagnostics_fail_original_is_primary()
    {
        var harness = new ScenarioScopeHarness { DiagnosticsThrows = new InvalidOperationException("diag") };
        var scope = harness.Build();
        var body = new BodyException("boom");

        var thrown = await Assert.ThrowsAsync<BodyException>(() => scope.RunAsync(Failing(body)).AsTask());

        Assert.Same(body, thrown);
        // Cleanup still ran despite the diagnostics failure.
        Assert.Equal(1, harness.Cleanup.Executions);
    }

    [Fact]
    public async Task Body_fails_cleanup_fails_original_is_primary()
    {
        var harness = new ScenarioScopeHarness { CleanupThrows = new InvalidOperationException("cleanup") };
        var scope = harness.Build();
        var body = new BodyException("boom");

        var thrown = await Assert.ThrowsAsync<BodyException>(() => scope.RunAsync(Failing(body)).AsTask());

        Assert.Same(body, thrown);
        Assert.True(harness.Actors.Released, "actor release must still run after cleanup fails");
    }

    [Fact]
    public async Task Body_fails_actor_release_fails_original_is_primary()
    {
        var harness = new ScenarioScopeHarness { ActorReleaseThrows = new InvalidOperationException("release") };
        var scope = harness.Build();
        var body = new BodyException("boom");

        var thrown = await Assert.ThrowsAsync<BodyException>(() => scope.RunAsync(Failing(body)).AsTask());

        Assert.Same(body, thrown);
        Assert.True(harness.ServiceScope.Disposed, "scope disposal must still run after actor release fails");
    }

    [Fact]
    public async Task Body_fails_di_disposal_fails_original_is_primary()
    {
        var harness = new ScenarioScopeHarness { ScopeDisposalThrows = new InvalidOperationException("dispose") };
        var scope = harness.Build();
        var body = new BodyException("boom");

        var thrown = await Assert.ThrowsAsync<BodyException>(() => scope.RunAsync(Failing(body)).AsTask());

        Assert.Same(body, thrown);
        Assert.Equal(1, harness.Permit.Releases);
    }

    [Fact]
    public async Task Body_fails_every_teardown_stage_fails_original_is_primary_and_all_recorded()
    {
        var harness = new ScenarioScopeHarness
        {
            DiagnosticsThrows = new InvalidOperationException("diag"),
            CleanupThrows = new InvalidOperationException("cleanup"),
            CaptureThrows = new InvalidOperationException("capture"),
            ContextsThrows = new InvalidOperationException("contexts"),
            ActorReleaseThrows = new InvalidOperationException("release"),
            ScopeDisposalThrows = new InvalidOperationException("dispose"),
        };
        var scope = harness.Build();
        var body = new BodyException("boom");

        var thrown = await Assert.ThrowsAsync<BodyException>(() => scope.RunAsync(Failing(body)).AsTask());

        Assert.Same(body, thrown);
        // Every stage was attempted and its failure recorded as metadata.
        Assert.Equal(ScenarioOutcome.BodyFailed, scope.Result!.Outcome);
        Assert.Equal(6, scope.Result.TeardownFailures.Count);
        Assert.Equal(1, harness.Permit.Releases);
    }

    // ---- Passing body with a teardown failure: the test fails, never a silent pass ----

    [Fact]
    public async Task Body_passes_cleanup_fails_fails_as_teardown()
    {
        var harness = new ScenarioScopeHarness { CleanupThrows = new InvalidOperationException("cleanup") };
        var scope = harness.Build();

        var thrown = await Assert.ThrowsAsync<TeardownException>(() => scope.RunAsync(Passing()).AsTask());

        Assert.Contains(thrown.Failures, f => f.Stage == "cleanup");
        Assert.Equal(ScenarioOutcome.TeardownFailed, scope.Result!.Outcome);
    }

    [Fact]
    public async Task Body_passes_actor_release_fails_fails_as_teardown()
    {
        var harness = new ScenarioScopeHarness { ActorReleaseThrows = new InvalidOperationException("release") };
        var scope = harness.Build();

        var thrown = await Assert.ThrowsAsync<TeardownException>(() => scope.RunAsync(Passing()).AsTask());

        Assert.Contains(thrown.Failures, f => f.Stage == "actor-release");
    }

    [Fact]
    public async Task Body_passes_scope_disposal_fails_fails_as_teardown_never_silent_pass()
    {
        var harness = new ScenarioScopeHarness { ScopeDisposalThrows = new InvalidOperationException("dispose") };
        var scope = harness.Build();

        var thrown = await Assert.ThrowsAsync<TeardownException>(() => scope.RunAsync(Passing()).AsTask());

        Assert.Contains(thrown.Failures, f => f.Stage == "scope-disposal");
    }

    // ---- Ordering ----

    [Fact]
    public async Task Ordering_is_diagnostics_cleanup_capture_contexts_actorRelease_scopeDisposal_permit()
    {
        var harness = new ScenarioScopeHarness();
        var scope = harness.Build();

        await Assert.ThrowsAsync<BodyException>(() => scope.RunAsync(Failing(new BodyException("x"))).AsTask());

        var log = harness.Log;
        Assert.True(log.IndexOf("diagnostics") < log.IndexOf("cleanup"), "diagnostics before cleanup");
        Assert.True(log.IndexOf("cleanup") < log.IndexOf("contexts"), "auth-dependent cleanup before contexts");
        Assert.True(log.IndexOf("contexts") < log.IndexOf("actor-release"), "contexts close before actor release");
        Assert.True(log.IndexOf("actor-release") < log.IndexOf("scope-disposal"), "DI scope disposed after scoped work");
        Assert.True(log.IndexOf("scope-disposal") < log.IndexOf("permit-release"), "permit released last");
    }

    [Fact]
    public async Task Concurrency_permit_is_always_released_even_when_all_stages_fail()
    {
        var harness = new ScenarioScopeHarness
        {
            CleanupThrows = new InvalidOperationException("cleanup"),
            ContextsThrows = new InvalidOperationException("contexts"),
            ActorReleaseThrows = new InvalidOperationException("release"),
            ScopeDisposalThrows = new InvalidOperationException("dispose"),
        };
        var scope = harness.Build();

        await Assert.ThrowsAsync<TeardownException>(() => scope.RunAsync(Passing()).AsTask());

        Assert.Equal(1, harness.Permit.Releases);
    }

    [Fact]
    public async Task Capture_is_told_whether_the_test_failed()
    {
        var harness = new ScenarioScopeHarness();
        var scope = harness.Build();

        await scope.RunAsync(Passing());

        Assert.Equal(false, harness.Capture.LastTestFailed);
    }

    // ---- Idempotency and concurrency ----

    [Fact]
    public async Task Double_execution_is_rejected_deterministically()
    {
        var harness = new ScenarioScopeHarness();
        var scope = harness.Build();

        await scope.RunAsync(Passing());

        await Assert.ThrowsAsync<InvalidOperationException>(() => scope.RunAsync(Passing()).AsTask());
    }

    [Fact]
    public async Task Concurrent_execution_is_rejected_deterministically()
    {
        var harness = new ScenarioScopeHarness();
        var scope = harness.Build();
        var gate = new TaskCompletionSource();

        // First call blocks inside the body until released; the second must be rejected
        // by the Created->Running CAS, not double-run teardown.
        var first = scope.RunAsync(async () => await gate.Task).AsTask();

        await Assert.ThrowsAsync<InvalidOperationException>(() => scope.RunAsync(Passing()).AsTask());

        gate.SetResult();
        await first;
        Assert.Equal(1, harness.Permit.Releases);
    }

    [Fact]
    public async Task Repeated_disposal_is_safe()
    {
        var harness = new ScenarioScopeHarness();
        var scope = harness.Build();

        await scope.DisposeAsync();
        await scope.DisposeAsync();
    }

    [Fact]
    public async Task Disposal_after_completed_run_is_a_no_op()
    {
        var harness = new ScenarioScopeHarness();
        var scope = harness.Build();

        await scope.RunAsync(Passing());
        var releasesAfterRun = harness.Permit.Releases;

        await scope.DisposeAsync();

        // The completed primary path already released everything; disposal does nothing.
        Assert.Equal(releasesAfterRun, harness.Permit.Releases);
    }

    [Fact]
    public async Task Disposal_without_run_executes_emergency_cleanup_and_reports_framework_init_failure()
    {
        var harness = new ScenarioScopeHarness();
        var scope = harness.Build();

        await scope.DisposeAsync();

        Assert.True(harness.Cleanup.Executions >= 1, "emergency cleanup ran");
        Assert.True(harness.Actors.Released, "emergency actor release ran");
        Assert.True(harness.ServiceScope.Disposed, "emergency scope disposal ran");
        Assert.Contains(harness.Reporter.Warnings, w => w.Contains("framework-initialization failure"));
    }

    // ---- Reporting failures are best-effort ----

    [Fact]
    public async Task Reporter_failure_does_not_block_cleanup_or_replace_original_exception()
    {
        var harness = new ScenarioScopeHarness { ReporterThrows = new InvalidOperationException("report") };
        var scope = harness.Build();
        var body = new BodyException("boom");

        var thrown = await Assert.ThrowsAsync<BodyException>(() => scope.RunAsync(Failing(body)).AsTask());

        Assert.Same(body, thrown);
        Assert.Equal(1, harness.Cleanup.Executions);
        Assert.True(harness.Actors.Released);
        Assert.Equal(1, harness.Permit.Releases);
    }
}
