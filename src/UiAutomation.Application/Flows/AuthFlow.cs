using UiAutomation.Application.Pages;
using UiAutomation.Framework.Composition;
using UiAutomation.Framework.Secrets;

namespace UiAutomation.Application.Flows;

// ─────────────────────────────────────────────────────────────────────────────
//  EXAMPLE — replace with your application's authentication journey.
//
//  A Flow represents reusable business behavior (Section 11). It must not contain selectors,
//  resolve users, or hide the final scenario assertion. Authentication takes the actor handle;
//  credential resolution happens INSIDE the flow via the framework credential orchestrator, so
//  the test never touches plaintext credentials (Section 9.4).
// ─────────────────────────────────────────────────────────────────────────────
public sealed class AuthFlow(LoginPage loginPage, ICredentialResolver credentialResolver)
{
    public async Task LoginAsAsync(TestActor actor, CancellationToken cancellationToken = default)
    {
        var credentials = await credentialResolver.ResolveAsync(actor.User, cancellationToken);
        await loginPage.OpenAsync();
        await loginPage.SubmitAsync(credentials);
    }
}
