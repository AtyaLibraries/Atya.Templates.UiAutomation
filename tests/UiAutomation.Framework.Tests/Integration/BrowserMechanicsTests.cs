using Microsoft.Playwright;
using Microsoft.Playwright.Xunit.v3;
using Xunit;

namespace UiAutomation.Framework.Tests.Integration;

/// <summary>
/// Mechanics tests (Section 26.2): need a real browser but no application environment. They
/// derive from the Playwright <see cref="BrowserTest"/> directly (not UiTestBase) because they
/// exercise browser primitives, not the scenario wrapper. Chromium-only for Phase 1.
/// </summary>
[Trait("Category", "Mechanics")]
public sealed class BrowserMechanicsTests : BrowserTest
{
    [Fact]
    public async Task Creates_context_page_and_captures_a_screenshot()
    {
        var context = await Browser.NewContextAsync();
        var page = await context.NewPageAsync();
        await page.SetContentAsync("<h1 data-testid='hello'>hello</h1>");

        var bytes = await page.ScreenshotAsync();

        Assert.NotEmpty(bytes);
        await context.CloseAsync();
    }

    [Fact]
    public async Task Starts_and_exports_a_trace_zip()
    {
        var context = await Browser.NewContextAsync();
        await context.Tracing.StartAsync(new TracingStartOptions { Screenshots = true, Snapshots = true });
        var page = await context.NewPageAsync();
        await page.SetContentAsync("<p>trace me</p>");

        var zip = Path.Combine(Path.GetTempPath(), $"uia-trace-{Guid.NewGuid():N}.zip");
        await context.Tracing.StopAsync(new TracingStopOptions { Path = zip });

        Assert.True(File.Exists(zip));
        File.Delete(zip);
        await context.CloseAsync();
    }

    [Fact]
    public async Task Creates_multiple_isolated_contexts()
    {
        var first = await Browser.NewContextAsync();
        var second = await Browser.NewContextAsync();

        var firstPage = await first.NewPageAsync();
        var secondPage = await second.NewPageAsync();
        await firstPage.SetContentAsync("<b>one</b>");
        await secondPage.SetContentAsync("<b>two</b>");

        Assert.NotSame(first, second);
        await first.CloseAsync();
        await second.CloseAsync();
    }
}
