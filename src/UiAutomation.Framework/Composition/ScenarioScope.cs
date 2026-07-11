using System.Runtime.ExceptionServices;
using UiAutomation.Framework.Cleanup;

namespace UiAutomation.Framework.Composition;

/// <summary>
/// Owns the deterministic teardown sequence and final failure selection for one scenario
/// execution (Section 9.2). Every collaborator is injected, so the mandatory permutation
/// suite (Section 26.1) exercises the full ordering, failure-preservation, idempotency, and
/// reporting guarantees against the <em>actual</em> orchestration — no browser required.
///
/// This is the reference implementation the doc's non-normative sketch defers to; the
/// "known deltas" listed in Section 9.2 are closed here (contexts close before actor
/// release; a real <see cref="ScenarioExecutionResult"/> drives selection; the emergency
/// disposal path never races the primary path).
/// </summary>
public sealed class ScenarioScope : IAsyncDisposable
{
    private readonly IFailureDiagnostics _diagnostics;
    private readonly ICaptureController _capture;
    private readonly ITestCleanupCoordinator _cleanup;
    private readonly IActorContextSet _contexts;
    private readonly IActorReleaser _actors;
    private readonly IAsyncDisposable _serviceScope;
    private readonly IConcurrencyPermit _permit;
    private readonly ITeardownReporter _reporter;
    private readonly CancellationToken _teardownToken;

    private int _state = (int)ScenarioState.Created;
    private volatile bool _completed;
    private int _disposed;

    public ScenarioScope(
        IFailureDiagnostics diagnostics,
        ICaptureController capture,
        ITestCleanupCoordinator cleanup,
        IActorContextSet contexts,
        IActorReleaser actors,
        IAsyncDisposable serviceScope,
        IConcurrencyPermit permit,
        ITeardownReporter reporter,
        CancellationToken teardownToken)
    {
        _diagnostics = diagnostics;
        _capture = capture;
        _cleanup = cleanup;
        _contexts = contexts;
        _actors = actors;
        _serviceScope = serviceScope;
        _permit = permit;
        _reporter = reporter;
        _teardownToken = teardownToken;
    }

    public ScenarioState State => (ScenarioState)Volatile.Read(ref _state);

    /// <summary>The result produced by the last (single) <see cref="RunAsync"/> call, for inspection in tests.</summary>
    public ScenarioExecutionResult? Result { get; private set; }

    public async ValueTask RunAsync(Func<ValueTask> body)
    {
        ArgumentNullException.ThrowIfNull(body);

        // Exactly one caller may move Created -> Running. Concurrent or repeated calls fail
        // deterministically instead of double-running teardown.
        var previous = (ScenarioState)Interlocked.CompareExchange(
            ref _state,
            (int)ScenarioState.Running,
            (int)ScenarioState.Created);

        if (previous != ScenarioState.Created)
        {
            throw new InvalidOperationException($"RunAsync is invalid in state {previous}.");
        }

        var teardown = new TeardownFailureAccumulator();
        ExceptionDispatchInfo? bodyFailure = null;

        try
        {
            try
            {
                await body().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                bodyFailure = ExceptionDispatchInfo.Capture(exception);
            }

            Volatile.Write(ref _state, (int)ScenarioState.TearingDown);

            // Diagnostics first, while contexts and leases are still live — best-effort.
            if (bodyFailure is not null)
            {
                await teardown.RunAsync(
                    "diagnostics",
                    () => _diagnostics.CaptureFailureAsync(bodyFailure.SourceException, _teardownToken)).ConfigureAwait(false);
            }

            // Authenticated application cleanup runs while the lease is still held.
            await teardown.RunAsync(
                "cleanup",
                () => _cleanup.ExecuteAsync(_teardownToken)).ConfigureAwait(false);

            // Stop tracing / finalize video while the framework-created context is still open.
            await teardown.RunAsync(
                "capture",
                () => _capture.FinalizeAsync(bodyFailure is not null, _teardownToken)).ConfigureAwait(false);

            // Contexts close BEFORE leases release: an exclusive user must never return to
            // the pool while a browser session authenticated as that user is still alive.
            await teardown.RunAsync(
                "contexts",
                () => _contexts.CloseAllAsync()).ConfigureAwait(false);

            await teardown.RunAsync(
                "actor-release",
                () => _actors.ReleaseAllAsync()).ConfigureAwait(false);

            // Scope disposal is part of PRIMARY teardown, so a disposal failure after a
            // passing body fails the test instead of degrading to a warning.
            await teardown.RunAsync(
                "scope-disposal",
                () => _serviceScope.DisposeAsync()).ConfigureAwait(false);
        }
        finally
        {
            // The concurrency permit is ALWAYS released, even if a stage above threw
            // unexpectedly (stages are guarded, so this is belt-and-braces).
            try
            {
                _permit.Release();
            }
            catch (Exception permitFailure)
            {
                teardown.Add("permit-release", permitFailure);
            }
        }

        _completed = true;
        Volatile.Write(ref _state, (int)ScenarioState.Completed);

        // Publishing is itself best-effort and never blocks final failure selection.
        teardown.PublishSafely(_reporter);

        var outcome = bodyFailure is not null
            ? ScenarioOutcome.BodyFailed
            : teardown.HasFailures ? ScenarioOutcome.TeardownFailed : ScenarioOutcome.Passed;

        Result = new ScenarioExecutionResult
        {
            BodyFailure = bodyFailure,
            TeardownFailures = teardown.Failures,
            Outcome = outcome,
        };

        if (bodyFailure is not null)
        {
            // Original failure, unchanged; teardown failures ride along as metadata only.
            bodyFailure.Throw();
        }

        if (teardown.HasFailures)
        {
            throw teardown.ToTeardownException();
        }
    }

    /// <summary>
    /// EMERGENCY fallback only. The primary path (<see cref="RunAsync"/>) has already
    /// disposed everything and set <c>_completed</c>; this then does nothing. It runs only
    /// if the framework failed before/without <see cref="RunAsync"/> — an exception is
    /// already unwinding — so it never throws and reports itself as a
    /// framework-initialization failure, never a silent pass.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        if (_completed)
        {
            return;
        }

        SafeReport(
            "ScenarioScope disposed without RunAsync completing; emergency cleanup and lease " +
            "release executed. This indicates a framework-initialization failure.");

        await SafeAsync("emergency-cleanup", () => _cleanup.ExecuteAsync(_teardownToken)).ConfigureAwait(false);
        await SafeAsync("emergency-contexts", () => _contexts.CloseAllAsync()).ConfigureAwait(false);
        await SafeAsync("emergency-actor-release", () => _actors.ReleaseAllAsync()).ConfigureAwait(false);
        await SafeAsync("emergency-scope-disposal", () => _serviceScope.DisposeAsync()).ConfigureAwait(false);

        try
        {
            _permit.Release();
        }
        catch (Exception exception)
        {
            SafeReport($"Emergency permit release failed: {exception.Message}");
        }
    }

    private async ValueTask SafeAsync(string stage, Func<ValueTask> action)
    {
        try
        {
            await action().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            SafeReport($"Emergency teardown stage '{stage}' failed: {exception}");
        }
    }

    private void SafeReport(string message)
    {
        try
        {
            _reporter.ReportWarning(message);
        }
        catch (Exception reportFailure)
        {
            Console.Error.WriteLine($"[emergency-reporting-fallback] {message} ({reportFailure.Message})");
        }
    }
}
