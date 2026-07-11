using Microsoft.Playwright;
using UiAutomation.Framework.Composition;
using UiAutomation.Framework.Secrets;

namespace UiAutomation.Application.Pages;

// ─────────────────────────────────────────────────────────────────────────────
//  EXAMPLE — replace with your application's login page.
//
//  A Page Object may use Playwright directly (ADR-001) but must not expose raw selector
//  strings; exposed ILocators are assertion targets, not action surfaces (Section 10). It
//  takes the non-disposable PageHandle so IPage is never a DI service (Section 9.3).
// ─────────────────────────────────────────────────────────────────────────────
public sealed class LoginPage(PageHandle page)
{
    private IPage Page => page.Page;

    private ILocator UsernameInput => Page.GetByTestId("login-username");

    private ILocator PasswordInput => Page.GetByTestId("login-password");

    private ILocator SubmitButton => Page.GetByTestId("login-submit");

    public ILocator ValidationMessage => Page.GetByTestId("login-validation-message");

    public Task OpenAsync() => Page.GotoAsync("/login");

    public async Task SubmitAsync(LoginCredentials credentials)
    {
        await UsernameInput.FillAsync(credentials.Username);
        await PasswordInput.FillAsync(credentials.Password);
        await SubmitButton.ClickAsync();
    }
}
