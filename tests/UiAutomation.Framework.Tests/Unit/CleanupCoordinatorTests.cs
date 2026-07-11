using UiAutomation.Framework.Cleanup;
using Xunit;

namespace UiAutomation.Framework.Tests.Unit;

/// <summary>
/// Mandatory cleanup-coordinator state coverage (docs/architecture.md Section 26.1):
/// LIFO order, resume-after-partial, completed steps never repeat, remaining steps run
/// after a failure, and multiple failures collected together.
/// </summary>
[Trait("Category", "Permutation")]
public sealed class CleanupCoordinatorTests
{
    [Fact]
    public async Task Executes_in_lifo_order()
    {
        var order = new List<string>();
        var coordinator = new TestCleanupCoordinator();
        coordinator.Register("first", _ => { order.Add("first"); return ValueTask.CompletedTask; }, CleanupOptions.Default);
        coordinator.Register("second", _ => { order.Add("second"); return ValueTask.CompletedTask; }, CleanupOptions.Default);

        await coordinator.ExecuteAsync(CancellationToken.None);

        Assert.Equal(["second", "first"], order);
    }

    [Fact]
    public async Task Remaining_steps_run_after_earlier_step_fails_and_failures_are_collected()
    {
        var ran = new List<string>();
        var coordinator = new TestCleanupCoordinator();
        coordinator.Register("a", _ => { ran.Add("a"); return ValueTask.CompletedTask; }, CleanupOptions.Default);
        coordinator.Register("b", _ => throw new InvalidOperationException("b-failed"), CleanupOptions.Default);
        coordinator.Register("c", _ => throw new InvalidOperationException("c-failed"), CleanupOptions.Default);

        var thrown = await Assert.ThrowsAsync<CleanupException>(
            () => coordinator.ExecuteAsync(CancellationToken.None).AsTask());

        // c and b are registered last, so LIFO tries them first; a still runs afterward.
        Assert.Contains("a", ran);
        Assert.Equal(2, thrown.Failures.Count);
        Assert.Equal(CleanupStepState.Completed, coordinator.StateOf("a"));
        Assert.Equal(CleanupStepState.Failed, coordinator.StateOf("b"));
        Assert.Equal(CleanupStepState.Failed, coordinator.StateOf("c"));
    }

    [Fact]
    public async Task Completed_steps_are_not_executed_twice()
    {
        var counts = new Dictionary<string, int>();
        var coordinator = new TestCleanupCoordinator();
        coordinator.Register(
            "once",
            _ => { counts["once"] = counts.GetValueOrDefault("once") + 1; return ValueTask.CompletedTask; },
            CleanupOptions.Default);

        await coordinator.ExecuteAsync(CancellationToken.None);
        await coordinator.ExecuteAsync(CancellationToken.None);

        Assert.Equal(1, counts["once"]);
    }

    [Fact]
    public async Task Resume_after_partial_reruns_only_retry_safe_failed_steps()
    {
        var attempts = new Dictionary<string, int>();
        var failFirstTime = true;
        var coordinator = new TestCleanupCoordinator();

        coordinator.Register(
            "retry-safe",
            _ =>
            {
                attempts["retry-safe"] = attempts.GetValueOrDefault("retry-safe") + 1;
                if (failFirstTime)
                {
                    failFirstTime = false;
                    throw new InvalidOperationException("transient");
                }

                return ValueTask.CompletedTask;
            },
            CleanupOptions.Default with { IsRetrySafe = true });

        coordinator.Register(
            "not-retry-safe",
            _ =>
            {
                attempts["not-retry-safe"] = attempts.GetValueOrDefault("not-retry-safe") + 1;
                throw new InvalidOperationException("permanent");
            },
            CleanupOptions.Default);

        await Assert.ThrowsAsync<CleanupException>(() => coordinator.ExecuteAsync(CancellationToken.None).AsTask());

        // Second pass resumes incomplete work only: the retry-safe step re-runs and
        // succeeds; the non-retry-safe failed step is skipped (its failure was already
        // reported by the first pass), so this pass produces no new failures.
        await coordinator.ExecuteAsync(CancellationToken.None);

        Assert.Equal(2, attempts["retry-safe"]);
        Assert.Equal(1, attempts["not-retry-safe"]);
        Assert.Equal(CleanupStepState.Completed, coordinator.StateOf("retry-safe"));
        Assert.Equal(CleanupStepState.Failed, coordinator.StateOf("not-retry-safe"));
    }

    [Fact]
    public async Task Repeated_execution_after_all_complete_is_safe_and_noop()
    {
        var runs = 0;
        var coordinator = new TestCleanupCoordinator();
        coordinator.Register("x", _ => { runs++; return ValueTask.CompletedTask; }, CleanupOptions.Default);

        await coordinator.ExecuteAsync(CancellationToken.None);
        await coordinator.ExecuteAsync(CancellationToken.None);
        await coordinator.ExecuteAsync(CancellationToken.None);

        Assert.Equal(1, runs);
    }
}
