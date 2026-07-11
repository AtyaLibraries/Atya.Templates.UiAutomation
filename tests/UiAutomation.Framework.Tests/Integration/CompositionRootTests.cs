using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright.Xunit.v3;
using UiAutomation.Framework.Composition;
using Xunit;

namespace UiAutomation.Framework.Tests.Integration;

/// <summary>
/// Mandatory proof (Section 9.3 / ADR-008 / Section 29 gate 5): disposing the DI scope must
/// NOT close a Playwright-owned page, because <see cref="Microsoft.Playwright.IPage"/> is never
/// a DI service — Page Objects take the non-disposable <see cref="PageHandle"/> instead.
/// </summary>
[Trait("Category", "Mechanics")]
public sealed class CompositionRootTests : BrowserTest
{
    [Fact]
    public async Task Disposing_the_di_scope_leaves_the_playwright_page_open()
    {
        var context = await Browser.NewContextAsync();
        var page = await context.NewPageAsync();

        var services = new ServiceCollection();
        services.AddScoped<PageHandle>();
        await using var provider = services.BuildServiceProvider();

        await using (var scope = provider.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<PageHandle>().Set(page);
        }

        // The scope has been disposed; the Playwright-owned page must remain open.
        Assert.False(page.IsClosed);

        await context.CloseAsync();
        Assert.True(page.IsClosed);
    }
}
