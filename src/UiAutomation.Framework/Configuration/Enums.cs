namespace UiAutomation.Framework.Configuration;

/// <summary>The supported test environments (docs/architecture.md Section 6.1).</summary>
public enum EnvironmentName
{
    Local,
    Dev,
    Qa,
    Uat,
}

/// <summary>The Playwright browser engines (Section 6.4).</summary>
public enum BrowserKind
{
    Chromium,
    Firefox,
    WebKit,
}

/// <summary>Trace/video capture policy (Section 6.4).</summary>
public enum CapturePolicy
{
    Off,
    RetainOnFailure,
    Always,
}
