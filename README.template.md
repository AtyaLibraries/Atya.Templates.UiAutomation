# UiAutomation

A .NET 10 Playwright + xUnit v3 UI-automation solution generated from the **Atya UI Automation** template.

## Layout

```
src/UiAutomation.Framework/       Reusable framework: configuration, users/leasing, secrets,
                                  reporting/capture, cleanup, and the scenario lifecycle.
src/UiAutomation.Application/     YOUR app: Pages, Components, Flows, Scenarios (examples included).
tests/UiAutomation.Tests/         Application UI tests (Smoke/Regression/...). RequiresApplication=true.
tests/UiAutomation.Framework.Tests/  Framework unit + permutation suite + browser mechanics + spike checks.
tools/UiAutomation.Preflight/     Environment health-check console tool.
config/                           appsettings.{env}.json + execution-profiles.json.
build/azure-pipelines.yml         Chromium smoke pipeline.
```

## Run

```bash
# 1. Restore + build
dotnet restore --locked-mode
dotnet build

# 2. Install the browser (chromium for Phase 1)
pwsh tests/UiAutomation.Tests/bin/Debug/net10.0/playwright.ps1 install chromium

# 3. Framework tests (no app required): unit permutation suite + browser mechanics + spike checks
dotnet test tests/UiAutomation.Framework.Tests --filter-not-trait "RequiresApplication=true"

# 4. Application UI tests (need a reachable app)
$env:TEST_ENV = "qa"; $env:TEST_PROFILE = "chromium-headless"
dotnet test tests/UiAutomation.Tests --filter-trait "Category=Smoke"
```

## Configuration

* `TEST_ENV` selects the environment: `local` | `dev` | `qa` | `uat` (config/appsettings.{env}.json).
* `TEST_PROFILE` selects the execution profile (config/execution-profiles.json).
* Secrets are read from environment variables by name (see `test-assets/users/users.json`). Never commit secrets.

## Writing tests

Derive from `UiTestBase` and run the whole body through `RunScenarioAsync<TScenario>()`. Declare actors in the lease request; never acquire users, resolve secrets, or touch `IPage` for application actions inside a test. See the example `LoginSmokeTests` and the framework rules in the architecture doc.

## Extending

Replace the example `Pages/`, `Flows/`, `Scenarios/`, and `Models/Roles.cs` with your application's own. The framework project is yours to evolve — there is no runtime NuGet package to wait on.
