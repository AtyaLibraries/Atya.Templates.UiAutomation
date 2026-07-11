namespace UiAutomation.Framework.Configuration;

/// <summary>
/// Browser execution configuration (Section 6.4 / ADR-003). Locale and time zone are
/// execution-matrix dimensions and live here; base URL does not (it is environment-owned).
/// </summary>
public sealed record ExecutionProfile
{
    public required string Name { get; init; }

    public BrowserKind Browser { get; init; } = BrowserKind.Chromium;

    public bool Headless { get; init; } = true;

    public int ViewportWidth { get; init; } = 1440;

    public int ViewportHeight { get; init; } = 900;

    public TimeSpan ActionTimeout { get; init; } = TimeSpan.FromSeconds(15);

    public TimeSpan NavigationTimeout { get; init; } = TimeSpan.FromSeconds(30);

    public TimeSpan AssertionTimeout { get; init; } = TimeSpan.FromSeconds(10);

    public string Locale { get; init; } = "en-US";

    public string TimeZoneId { get; init; } = "UTC";

    public CapturePolicy Trace { get; init; } = CapturePolicy.RetainOnFailure;

    // Baseline: Off. Reliable default-context video needs framework-owned context
    // close ordering; enabled only under that design (Section 6.4, ADR-002).
    public CapturePolicy Video { get; init; } = CapturePolicy.Off;

    public int MaxParallelTests { get; init; } = 4;
}
