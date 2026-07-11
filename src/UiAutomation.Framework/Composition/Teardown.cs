using System.Text;

namespace UiAutomation.Framework.Composition;

/// <summary>A single teardown-stage failure, tagged with the stage name that produced it.</summary>
public sealed record TeardownFailure(string Stage, Exception Exception);

/// <summary>
/// Thrown when the test body passed but one or more teardown stages failed. Per Section 9.2
/// / rule 26, a teardown failure after a passing body always fails the test.
/// </summary>
public sealed class TeardownException : Exception
{
    public TeardownException(IReadOnlyList<TeardownFailure> failures)
        : base(BuildMessage(failures))
    {
        Failures = failures;
    }

    public IReadOnlyList<TeardownFailure> Failures { get; }

    private static string BuildMessage(IReadOnlyList<TeardownFailure> failures)
    {
        var builder = new StringBuilder("Test body passed but ")
            .Append(failures.Count)
            .Append(failures.Count == 1 ? " teardown stage failed: " : " teardown stages failed: ");

        for (var i = 0; i < failures.Count; i++)
        {
            if (i > 0)
            {
                builder.Append("; ");
            }

            builder.Append(failures[i].Stage).Append(" -> ").Append(failures[i].Exception.GetType().Name);
        }

        return builder.ToString();
    }
}

/// <summary>
/// Sink for teardown reporting (warnings, attachments, metadata). Abstracted so publication
/// is best-effort and unit-testable: a reporting failure is recorded, never allowed to block
/// cleanup or replace the original test exception (Section 9.2).
/// </summary>
public interface ITeardownReporter
{
    void ReportWarning(string message);
}

/// <summary>
/// Accumulates teardown-stage failures. Each stage is attempted through
/// <see cref="RunAsync"/>; a failure is captured, never thrown, and never blocks a later
/// stage. Publication via <see cref="PublishSafely"/> is itself guarded with a bounded
/// stderr fallback.
/// </summary>
public sealed class TeardownFailureAccumulator
{
    private readonly List<TeardownFailure> _failures = [];

    public bool HasFailures => _failures.Count > 0;

    public IReadOnlyList<TeardownFailure> Failures => _failures;

    public async ValueTask RunAsync(string stage, Func<ValueTask> action)
    {
        try
        {
            await action().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _failures.Add(new TeardownFailure(stage, exception));
        }
    }

    public void Add(string stage, Exception exception) => _failures.Add(new TeardownFailure(stage, exception));

    public TeardownException ToTeardownException() => new(_failures.ToArray());

    public void PublishSafely(ITeardownReporter reporter)
    {
        foreach (var failure in _failures)
        {
            try
            {
                reporter.ReportWarning($"Teardown stage '{failure.Stage}' failed: {failure.Exception}");
            }
            catch (Exception publishFailure)
            {
                // Bounded stderr fallback — reporting must never block teardown or the
                // final failure selection.
                Console.Error.WriteLine(
                    $"[teardown-reporting-fallback] stage '{failure.Stage}' could not be published: {publishFailure.Message}");
            }
        }
    }
}
