using Microsoft.Playwright;
using UiAutomation.Framework.Composition;

namespace UiAutomation.Application.Pages;

// ─────────────────────────────────────────────────────────────────────────────
//  EXAMPLE — replace with your application's dashboard/landing page.
//  Page Objects expose named locators for assertions (Section 10.3).
// ─────────────────────────────────────────────────────────────────────────────
public sealed class DashboardPage(PageHandle page)
{
    private IPage Page => page.Page;

    public ILocator Root => Page.GetByTestId("dashboard");

    public ILocator CurrentUser => Page.GetByTestId("current-user");
}
