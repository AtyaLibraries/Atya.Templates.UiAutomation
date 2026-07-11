namespace UiAutomation.Framework.Reporting;

/// <summary>
/// The separated identity dimensions for a test execution (Section 22.2). Case ID is the
/// stable logical case; Execution ID is unique per execution so pipeline reruns never
/// overwrite each other's artifacts.
/// </summary>
public sealed record TestIdentity
{
    public required string CaseId { get; init; }

    public required string ExecutionId { get; init; }

    public required string TestName { get; init; }

    public required string Environment { get; init; }

    public required string Profile { get; init; }

    public required string Browser { get; init; }

    /// <summary>The ownership prefix stamped on test-created resources (Section 21): <c>e2e-{run}-{case}</c>.</summary>
    public string OwnershipId => $"e2e-{ExecutionId}-{Sanitize(CaseId)}";

    public static string Sanitize(string value)
    {
        var chars = value.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '-').ToArray();
        return new string(chars);
    }
}
