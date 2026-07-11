using System.Runtime.ExceptionServices;

namespace UiAutomation.Framework.Composition;

/// <summary>Explicit lifecycle state machine for a scenario execution (Section 9.2).</summary>
public enum ScenarioState
{
    Created,
    Running,
    TearingDown,
    Completed,
}

/// <summary>The three terminal outcomes of a scenario execution.</summary>
public enum ScenarioOutcome
{
    Passed,
    BodyFailed,
    TeardownFailed,
}

/// <summary>
/// The internal result the wrapper produces; it drives final failure selection (Section 9.2).
/// </summary>
public sealed record ScenarioExecutionResult
{
    public ExceptionDispatchInfo? BodyFailure { get; init; }

    public IReadOnlyList<TeardownFailure> TeardownFailures { get; init; } = [];

    public ScenarioOutcome Outcome { get; init; }
}

/// <summary>
/// The process-local concurrency permit (Section 6.4). Acquired by the factory during
/// setup, released exactly once in the centralized teardown sequence.
/// </summary>
public interface IConcurrencyPermit
{
    void Release();
}

/// <summary>
/// Best-effort failure diagnostics captured while contexts and leases are still live
/// (Section 9.2). A diagnostics failure can never block cleanup, actor release, or disposal.
/// </summary>
public interface IFailureDiagnostics
{
    ValueTask CaptureFailureAsync(Exception bodyException, CancellationToken cancellationToken);
}

/// <summary>
/// Stops tracing / finalizes video per the capture policy (Section 6.4 / Section 8). Runs
/// while the framework-created context is still open, before contexts close.
/// </summary>
public interface ICaptureController
{
    ValueTask FinalizeAsync(bool testFailed, CancellationToken cancellationToken);
}

/// <summary>
/// The framework-created actor contexts and pages. Closed <em>before</em> actor leases are
/// released (the load-bearing Section 8 ordering rule).
/// </summary>
public interface IActorContextSet
{
    ValueTask CloseAllAsync();
}

/// <summary>Releases all actor leases held by the scenario (Section 15.6 outcomes).</summary>
public interface IActorReleaser
{
    ValueTask ReleaseAllAsync();
}
