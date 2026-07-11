namespace UiAutomation.Framework.Cleanup;

/// <summary>Thrown when one or more cleanup steps fail; carries every step failure (Section 21.1).</summary>
public sealed class CleanupException : Exception
{
    public CleanupException(IReadOnlyList<Exception> failures)
        : base($"{failures.Count} cleanup step(s) failed.")
    {
        Failures = failures;
    }

    public IReadOnlyList<Exception> Failures { get; }
}

/// <summary>Per-step lifecycle state (Section 21.1).</summary>
public enum CleanupStepState
{
    Pending,
    Running,
    Completed,
    Failed,
}

/// <summary>
/// Minimal Phase 1 cleanup coordinator: registration, LIFO execution, and explicit
/// per-step state tracking (Section 29 lists this as required because the wrapper
/// permutation suite exercises cleanup behavior). Per-step timeouts, linked-token drain,
/// and retry-safe re-execution are Phase 2 (Section 21.1) — this class keeps the state
/// model and ordering guarantees the suite depends on.
/// </summary>
public sealed class TestCleanupCoordinator : ITestCleanupCoordinator
{
    private sealed class Step(string name, Func<CancellationToken, ValueTask> cleanup, CleanupOptions options)
    {
        public string Name { get; } = name;

        public Func<CancellationToken, ValueTask> Cleanup { get; } = cleanup;

        public CleanupOptions Options { get; } = options;

        public CleanupStepState State { get; set; } = CleanupStepState.Pending;
    }

    private readonly List<Step> _steps = [];
    private readonly TimeProvider _timeProvider;
    private readonly Lock _gate = new();

    public TestCleanupCoordinator()
        : this(TimeProvider.System)
    {
    }

    public TestCleanupCoordinator(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
    }

    public void Register(string name, Func<CancellationToken, ValueTask> cleanup, CleanupOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(cleanup);
        ArgumentNullException.ThrowIfNull(options);

        lock (_gate)
        {
            _steps.Add(new Step(name, cleanup, options));
        }
    }

    /// <summary>Test-visible snapshot of a step's state (used by the mandatory state-coverage tests).</summary>
    public CleanupStepState StateOf(string name)
    {
        lock (_gate)
        {
            return _steps.First(s => s.Name == name).State;
        }
    }

    public async ValueTask ExecuteAsync(CancellationToken teardownToken)
    {
        List<Step> snapshot;
        lock (_gate)
        {
            // LIFO: last created, first cleaned.
            snapshot = Enumerable.Reverse(_steps).ToList();
        }

        var failures = new List<Exception>();

        foreach (var step in snapshot)
        {
            // Resume-after-partial: a completed step is never executed twice. A step that
            // failed and is not retry-safe is not re-attempted.
            if (step.State == CleanupStepState.Completed)
            {
                continue;
            }

            if (step.State == CleanupStepState.Failed && !step.Options.IsRetrySafe)
            {
                continue;
            }

            step.State = CleanupStepState.Running;

            // Each step gets its own linked token: the shared teardown token plus a
            // per-step timeout. A step timeout never cancels the teardown token for
            // other steps (Section 21.1).
            using var timeoutCts = new CancellationTokenSource(step.Options.Timeout, _timeProvider);
            using var stepCts = CancellationTokenSource.CreateLinkedTokenSource(teardownToken, timeoutCts.Token);

            try
            {
                await step.Cleanup(stepCts.Token).ConfigureAwait(false);
                step.State = CleanupStepState.Completed;
            }
            catch (Exception exception)
            {
                // A step failure never blocks the remaining steps; the original failure is retained.
                step.State = CleanupStepState.Failed;
                failures.Add(exception);
            }
        }

        if (failures.Count > 0)
        {
            throw new CleanupException(failures);
        }
    }
}
