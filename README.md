# Atya.Templates.UiAutomation

The `dotnet new` **UI automation** solution template for the Atya fleet — .NET 10 + Playwright + xUnit v3 + Microsoft Testing Platform. It distributes the UI Automation Framework architecture (`docs/architecture.md`, Section 31) as generated source, not as a runtime package.

> This repository **is** the template. The tree under source control is what gets laid down by `dotnet new atya-ui-automation`; `Atya.Templates.UiAutomation.csproj` packs it as a `Template` NuGet package with no runtime dependencies.

## Use it

```bash
dotnet new install Atya.Templates.UiAutomation
dotnet new atya-ui-automation --name Contoso.Shop.UiAutomation
```

`--name` becomes the generated solution's root namespace. `Atya` appears only on this template package.

## Repository map

| Path | Role |
| --- | --- |
| `.template.config/template.json` | Template definition (`sourceName: UiAutomation`). |
| `Atya.Templates.UiAutomation.csproj` | Packs the tree as the template package (excluded from generated content). |
| `src/`, `tests/`, `tools/`, `config/`, `build/` | The generated solution content. |
| `docs/architecture.md` | The governing architecture spec — single source of truth (not shipped into generated repos). |
| `.github/workflows/` | Template-repo CI: build/test the template, `test-template` validation, and publish. |

## Contributing / validating

`test-template.yml` runs the full validation on every PR: pack the template, install the nupkg, instantiate it, then in the generated solution restore (locked), build, and run the framework unit + permutation + chromium mechanics + spike suites.

## Release

Versioning starts at tag `v1.0.0` through the standard fleet chain (development → master → `v*` tag → central publisher → nuget.org via OIDC, with the production gate approved manually). NuGet versions are immutable; nothing unverified ships.

## Governance

Catalogued in `platform/CONSTITUTION.md` as `Atya.Templates.UiAutomation` (Templates infra, BUILD). Repo custom property: `atya-managed=true` (template repos carry `atya-managed` because their checks come from the managed reusable CI — the documented exception in the constitution's `requirements`).

## License

MIT — see [LICENSE](LICENSE).
