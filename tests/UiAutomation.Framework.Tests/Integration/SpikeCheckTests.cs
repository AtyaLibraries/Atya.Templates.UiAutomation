using Microsoft.Playwright.Xunit.v3;
using Xunit;

namespace UiAutomation.Framework.Tests.Integration;

/// <summary>
/// Executable Phase 1 compatibility-spike checks (docs/architecture.md Section 29). Where a
/// spike result contradicts the document, STOP and amend the document — spike results are
/// authoritative. These run on chromium in the framework stage (no application required).
/// </summary>
[Trait("Category", "Spike")]
public sealed class SpikeCheckTests : BrowserTest
{
    // Spike #2: the browser/profile mechanism actually honored under MTP. With no BROWSER
    // override, the Playwright xUnit base launches chromium — the Phase 1 default.
    [Fact]
    public void Default_browser_engine_is_chromium()
    {
        Assert.Equal("chromium", Browser.BrowserType.Name);
    }

    // Spike #1: a BrowserTest-derived class compiles and runs against the pinned packages and
    // exposes a live worker-scoped browser.
    [Fact]
    public void Playwright_browser_is_available_and_connected()
    {
        Assert.NotNull(Browser);
        Assert.True(Browser.IsConnected);
    }

    // Spike #3: TestContext attachment/warning APIs are accepted during a running test.
    [Fact]
    public void TestContext_accepts_warnings_and_attachments()
    {
        var context = TestContext.Current;
        Assert.NotNull(context);
        context.AddWarning("spike: warnings are accepted mid-test");
        context.AddAttachment("spike-metadata", "spike: attachments are accepted mid-test");
    }
}
