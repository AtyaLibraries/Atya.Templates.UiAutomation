namespace UiAutomation.Framework.Configuration;

/// <summary>
/// The validated, resolved configuration for one test process: exactly one environment and
/// one execution profile (Section 6.5), plus the content root all paths resolve against.
/// </summary>
public sealed record TestConfiguration
{
    public required EnvironmentSettings Environment { get; init; }

    public required ExecutionProfile Profile { get; init; }

    public required string ContentRoot { get; init; }
}
