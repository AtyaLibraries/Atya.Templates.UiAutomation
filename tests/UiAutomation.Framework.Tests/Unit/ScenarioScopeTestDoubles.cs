using UiAutomation.Framework.Cleanup;
using UiAutomation.Framework.Composition;

namespace UiAutomation.Framework.Tests.Unit;

/// <summary>Shared ordered log so ordering guarantees can be asserted across stages.</summary>
internal sealed class OrderLog
{
    private readonly List<string> _entries = [];
    private readonly Lock _gate = new();

    public void Record(string stage)
    {
        lock (_gate)
        {
            _entries.Add(stage);
        }
    }

    public IReadOnlyList<string> Entries
    {
        get
        {
            lock (_gate)
            {
                return _entries.ToArray();
            }
        }
    }

    public int IndexOf(string stage) => Entries.ToList().IndexOf(stage);
}

internal sealed class FakeFailureDiagnostics(OrderLog log, Exception? throws = null) : IFailureDiagnostics
{
    public int Calls { get; private set; }

    public ValueTask CaptureFailureAsync(Exception bodyException, CancellationToken cancellationToken)
    {
        Calls++;
        log.Record("diagnostics");
        return throws is null ? ValueTask.CompletedTask : ValueTask.FromException(throws);
    }
}

internal sealed class FakeCaptureController(OrderLog log, Exception? throws = null) : ICaptureController
{
    public bool? LastTestFailed { get; private set; }

    public ValueTask FinalizeAsync(bool testFailed, CancellationToken cancellationToken)
    {
        LastTestFailed = testFailed;
        log.Record("capture");
        return throws is null ? ValueTask.CompletedTask : ValueTask.FromException(throws);
    }
}

internal sealed class FakeCleanupCoordinator(OrderLog log, Exception? throws = null) : ITestCleanupCoordinator
{
    public int Executions { get; private set; }

    public void Register(string name, Func<CancellationToken, ValueTask> cleanup, CleanupOptions options)
    {
    }

    public ValueTask ExecuteAsync(CancellationToken teardownToken)
    {
        Executions++;
        log.Record("cleanup");
        return throws is null ? ValueTask.CompletedTask : ValueTask.FromException(throws);
    }
}

internal sealed class FakeActorContextSet(OrderLog log, Exception? throws = null) : IActorContextSet
{
    public bool Closed { get; private set; }

    public ValueTask CloseAllAsync()
    {
        Closed = true;
        log.Record("contexts");
        return throws is null ? ValueTask.CompletedTask : ValueTask.FromException(throws);
    }
}

internal sealed class FakeActorReleaser(OrderLog log, Exception? throws = null) : IActorReleaser
{
    public bool Released { get; private set; }

    public ValueTask ReleaseAllAsync()
    {
        Released = true;
        log.Record("actor-release");
        return throws is null ? ValueTask.CompletedTask : ValueTask.FromException(throws);
    }
}

internal sealed class FakeServiceScope(OrderLog log, Exception? throws = null) : IAsyncDisposable
{
    public bool Disposed { get; private set; }

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        log.Record("scope-disposal");
        return throws is null ? ValueTask.CompletedTask : ValueTask.FromException(throws);
    }
}

internal sealed class FakePermit(OrderLog log) : IConcurrencyPermit
{
    public int Releases { get; private set; }

    public void Release()
    {
        Releases++;
        log.Record("permit-release");
    }
}

internal sealed class FakeReporter(Exception? throws = null) : ITeardownReporter
{
    public List<string> Warnings { get; } = [];

    public void ReportWarning(string message)
    {
        Warnings.Add(message);
        if (throws is not null)
        {
            throw throws;
        }
    }
}

/// <summary>Fluent builder for a <see cref="ScenarioScope"/> wired with fault-injectable doubles.</summary>
internal sealed class ScenarioScopeHarness
{
    public OrderLog Log { get; } = new();

    public Exception? DiagnosticsThrows { get; set; }

    public Exception? CaptureThrows { get; set; }

    public Exception? CleanupThrows { get; set; }

    public Exception? ContextsThrows { get; set; }

    public Exception? ActorReleaseThrows { get; set; }

    public Exception? ScopeDisposalThrows { get; set; }

    public Exception? ReporterThrows { get; set; }

    public FakeFailureDiagnostics Diagnostics { get; private set; } = null!;

    public FakeCaptureController Capture { get; private set; } = null!;

    public FakeCleanupCoordinator Cleanup { get; private set; } = null!;

    public FakeActorContextSet Contexts { get; private set; } = null!;

    public FakeActorReleaser Actors { get; private set; } = null!;

    public FakeServiceScope ServiceScope { get; private set; } = null!;

    public FakePermit Permit { get; private set; } = null!;

    public FakeReporter Reporter { get; private set; } = null!;

    public ScenarioScope Build()
    {
        Diagnostics = new FakeFailureDiagnostics(Log, DiagnosticsThrows);
        Capture = new FakeCaptureController(Log, CaptureThrows);
        Cleanup = new FakeCleanupCoordinator(Log, CleanupThrows);
        Contexts = new FakeActorContextSet(Log, ContextsThrows);
        Actors = new FakeActorReleaser(Log, ActorReleaseThrows);
        ServiceScope = new FakeServiceScope(Log, ScopeDisposalThrows);
        Permit = new FakePermit(Log);
        Reporter = new FakeReporter(ReporterThrows);

        return new ScenarioScope(
            Diagnostics,
            Capture,
            Cleanup,
            Contexts,
            Actors,
            ServiceScope,
            Permit,
            Reporter,
            CancellationToken.None);
    }
}

/// <summary>Marker exception so tests can assert the exact original instance is preserved.</summary>
internal sealed class BodyException(string message) : Exception(message);
