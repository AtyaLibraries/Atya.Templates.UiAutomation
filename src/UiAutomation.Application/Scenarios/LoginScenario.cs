using UiAutomation.Application.Flows;
using UiAutomation.Application.Pages;

namespace UiAutomation.Application.Scenarios;

// ─────────────────────────────────────────────────────────────────────────────
//  EXAMPLE — replace with your feature's typed scenario.
//
//  A typed scenario scope (ADR-004) contains only the Pages, Components, and Flows a feature
//  needs — never a global service-locator bag. Actors are NOT scenario members; they are
//  declared in the ActorPlan and acquired by the framework before this graph is composed.
//  The object graph is built by dependency injection.
// ─────────────────────────────────────────────────────────────────────────────
public sealed class LoginScenario(AuthFlow auth, DashboardPage dashboard)
{
    public AuthFlow Auth { get; } = auth;

    public DashboardPage Dashboard { get; } = dashboard;
}
