# Atya UI Automation template

A `dotnet new` solution template for **.NET 10 + Playwright + xUnit v3 + Microsoft Testing Platform** UI automation, distributing the Atya UI Automation Framework architecture.

## Install & use

```bash
dotnet new install Atya.Templates.UiAutomation
dotnet new atya-ui-automation --name Contoso.Shop.UiAutomation
```

`--name` sets the generated solution's root namespace (e.g. `Contoso.Shop.UiAutomation`). `Atya` appears only on the template package itself.

## What you get (Phase 1 vertical slice)

* Actor-first scenario lifecycle: `RunScenarioAsync<TScenario>()` is the single execution entry point — the framework owns scope creation/disposal, actor acquisition/release, context close, diagnostics, cleanup, and final failure selection.
* Deterministic teardown with a **mandatory execution-wrapper permutation suite** proving the failure-preservation, ordering, idempotency, and reporting guarantees.
* Environment vs. execution-profile configuration separation, fail-fast validation, user leasing, environment secret provider, and a diagnostics capture policy with a private raw-artifact directory.
* Example `LoginPage` / `DashboardPage` / `AuthFlow` / `LoginScenario` (clearly marked replaceable), a `Preflight` health-check tool, and a chromium-only Azure DevOps pipeline.

## Dependencies

The template package has **no runtime dependencies**. Generated projects use plain `Microsoft.NET.Sdk` and take exactly one Atya dependency by default — `Atya.Governance.CodeQuality` (analyzers, `PrivateAssets="all"`). The test stack (Playwright, xUnit v3, MTP) is pinned via Central Package Management.

See `docs/architecture.md` in the generated solution's source repository for the full architecture.
