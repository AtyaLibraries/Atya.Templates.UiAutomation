using Microsoft.Playwright;
using UiAutomation.Application.Models;
using UiAutomation.Application.Scenarios;
using UiAutomation.Framework.Composition;
using UiAutomation.Framework.Users;
using Xunit;

namespace UiAutomation.Tests.Smoke;

// ─────────────────────────────────────────────────────────────────────────────
//  EXAMPLE — the canonical login smoke test (docs/architecture.md Section 12).
//
//  This is an APPLICATION-DEPENDENT test: it needs TEST_ENV, TEST_PROFILE, and a reachable
//  application, so it is marked RequiresApplication=true and runs only in the UI test stage
//  of the pipeline — never in the framework stage (Section 26.2). Replace with your own
//  smoke tests once the example Pages/Flows/Scenario are pointed at your app.
// ─────────────────────────────────────────────────────────────────────────────
public sealed class LoginSmokeTests(HostFixture host) : UiTestBase(host)
{
    [Fact]
    [Trait("Category", "Smoke")]
    [Trait("Role", "Admin")]
    [Trait("RequiresApplication", "true")]
    public async Task Admin_can_log_in()
    {
        await RunScenarioAsync<LoginScenario>(
            new UserLeaseRequest { Role = Roles.Admin },
            async execution =>
            {
                await execution.Scenario.Auth.LoginAsAsync(execution.Actor, execution.CancellationToken);

                await Assertions.Expect(execution.Scenario.Dashboard.Root).ToBeVisibleAsync();
            });
    }
}
