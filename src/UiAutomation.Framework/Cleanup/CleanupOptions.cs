namespace UiAutomation.Framework.Cleanup;

/// <summary>
/// Per-step cleanup options (Section 21.1). A step is retried only if it explicitly
/// declares itself idempotent/retry-safe; timeouts are per-step.
/// </summary>
public sealed record CleanupOptions
{
    public static CleanupOptions Default { get; } = new();

    /// <summary>Per-step timeout. The step receives a token linked to the teardown token and this deadline.</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Only retry-safe (idempotent) steps may be retried after a failure.</summary>
    public bool IsRetrySafe { get; init; }
}
