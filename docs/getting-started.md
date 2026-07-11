# Getting started

This solution was generated from the **Atya UI Automation** template. It is a Phase 1 vertical
slice of the UI Automation Framework architecture: one complete, reliable path from validated
configuration to a published CI result.

## The one rule that matters most

Every test derives from `UiTestBase` and runs its **entire body** through the single entry point:

```csharp
await RunScenarioAsync<LoginScenario>(
    new UserLeaseRequest { Role = Roles.Admin },
    async execution =>
    {
        await execution.Scenario.Auth.LoginAsAsync(execution.Actor, execution.CancellationToken);
        await Assertions.Expect(execution.Scenario.Dashboard.Root).ToBeVisibleAsync();
    });
```

The framework — not the test — owns scope creation/disposal, actor acquisition/release, browser
context close, diagnostics, cleanup, concurrency-permit release, and final failure selection.
Tests never see a lifecycle object.

## What tests must and must not do

* **Do**: declare actors in the lease request; use Flows and named Page Object locators; assert
  the business outcome in the test with Playwright `Expect`.
* **Don't**: put selectors in tests; call `IPage` for application actions; resolve secrets or
  acquire users yourself; call the wrapper more than once; write multi-user UI tests (not part of
  the Phase 1 baseline).

## Layout

* `src/*.Framework` — the reusable lifecycle. Yours to evolve; there is no runtime NuGet package.
* `src/*.Application` — replace the example `Pages`/`Flows`/`Scenarios`/`Models` with your app's.
* `tests/*.Framework.Tests` — framework unit + the mandatory permutation suite + browser mechanics
  + Phase 1 spike checks. Runs without an application.
* `tests/*.Tests` — your application UI tests (`RequiresApplication=true`).
* `tools/*.Preflight` — environment health-check console tool.

## Configuration

* `TEST_ENV` → `local` | `dev` | `qa` | `uat` (selects `config/appsettings.{env}.json`).
* `TEST_PROFILE` → an entry in `config/execution-profiles.json` (Phase 1 ships chromium profiles).
* Secrets are resolved from environment variables named in `test-assets/users/users.json`.

## Next phases

Reliability (full cleanup coordinator, API setup/cleanup, Key Vault), parallel scale (distributed
leasing, browser matrix), data/auth optimization (Excel, storage-state), and AI-assisted
generation are staged in the architecture roadmap. Keep the permutation suite green as you extend
the lifecycle — it is the confidence gate, not code review.
