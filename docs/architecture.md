# UI Automation Framework Architecture

**Stack:** Playwright for .NET + .NET 10 + xUnit v3
**Purpose:** Scalable, maintainable UI automation platform with clean architecture, multi-environment support, role- and permission-based user coverage, safe parallel execution, Azure DevOps integration, and AI-assisted test generation.

---

## 1. Overview

This document defines the architecture and implementation strategy for a UI automation testing platform.

The framework is designed to:

* Scale across large automated test suites
* Keep tests readable and business-focused
* Support Local, DEV, QA, and UAT environments
* Support users with different roles, permissions, tenants, and account states
* Run tests safely in parallel
* Integrate with Azure DevOps
* Produce useful failure diagnostics
* Protect credentials and sensitive test data
* Support JSON- and Excel-based test data
* Support AI-assisted test generation
* Preserve Playwright’s native capabilities instead of hiding them behind weak wrappers

The framework uses a small number of clear architectural boundaries rather than many unnecessary projects and abstractions.

---

## 2. Technology Stack

| Area                         | Technology                                      |
| ---------------------------- | ----------------------------------------------- |
| Language                     | C#                                              |
| Runtime                      | .NET 10 LTS                                     |
| Test Framework               | xUnit v3                                        |
| Test Platform                | Microsoft Testing Platform                      |
| TRX Reporting                | `Microsoft.Testing.Extensions.TrxReport`        |
| UI Automation                | Playwright for .NET                             |
| Playwright xUnit Integration | `Microsoft.Playwright.Xunit.v3`                 |
| UI Assertions                | Playwright `Assertions.Expect`                  |
| Domain Assertions            | xUnit assertions                                |
| Primary Test Data            | JSON                                            |
| Spreadsheet Adapter          | ClosedXML                                       |
| Configuration                | JSON + environment variables                    |
| Secrets                      | Azure Key Vault or secure environment variables |
| Logging                      | `Microsoft.Extensions.Logging`                  |
| CI/CD                        | Azure DevOps                                    |
| Package Management           | Central Package Management                      |
| SDK Selection                | `global.json`                                   |

.NET 10 is an LTS release, and the SDK can select Microsoft Testing Platform as the repository-wide test runner through `global.json`.

### 2.1 FluentAssertions Licensing Decision

FluentAssertions 8 and later require a commercial license for commercial use. Therefore, this framework does not mandate FluentAssertions.

The baseline uses:

* Playwright `Expect` assertions for browser state
* xUnit assertions for domain and configuration state

FluentAssertions may be introduced only after an explicit licensing decision and approved package version.

---

## 3. Scope

The framework owns:

* Environment and execution configuration
* User definitions and user allocation
* Credential resolution
* Playwright test integration
* Page Objects
* Component Objects
* Business Flows
* Test-data loading and validation
* Test setup and cleanup
* Failure diagnostics
* CI/CD integration
* AI test-generation constraints

The framework does not attempt to:

* Replace Playwright with a custom browser API
* Create a generic UI scripting language
* Hide all assertions inside framework classes
* Retry every failure automatically
* Share browser contexts between tests
* Treat Excel as executable test code
* Solve application-specific data provisioning generically

---

## 4. Architecture Decisions

### ADR-001: Use Playwright Directly in Page and Component Objects

**Decision**

Page Objects and Component Objects may use `IPage` and `ILocator` directly.

Tests and Flows must not contain selectors.

**Reason**

A generic wrapper such as:

```csharp
public interface IBrowserDriver
{
    Task ClickAsync(string selector);
    Task FillAsync(string selector, string value);
}
```

would hide important Playwright functionality:

* Locator auto-waiting
* Accessibility locators
* Test-ID locators
* Frames
* Popups
* Downloads
* Network interception
* Browser contexts
* Tracing
* Playwright assertions

**Consequences**

* The framework remains Playwright-specific
* Page Objects can use the complete Playwright API
* Migration to another browser library would require replacing the Page and Component layers
* Tests remain insulated from selectors and low-level browser behavior

---

### ADR-002: Use the Official Playwright xUnit v3 Integration

**Status:** Proposed — presumptive choice pending the Phase 1 executable spike.

**Decision**

Use `Microsoft.Playwright.Xunit.v3` and its supplied test base classes rather than manually managing Playwright and browser lifecycle.

**`BrowserTest` is the presumptive base class**, not `PageTest`. The stated requirements — actor-specific storage state applied at context creation, one context per actor, throttling *before* context creation, reliable video finalization, and deterministic close ordering — all require framework-owned context creation, which `BrowserTest` provides while still using the official integration. The spike's job is to *validate* `BrowserTest` (lifecycle, parallel behavior, configuration surface), with `PageTest` as the fallback only if validation fails; if the fallback is taken, **storage-state reuse, multi-user sessions, reliable video, and pre-context throttling are explicitly removed from the architecture's scope** — not deferred, removed.

Browser reuse is **worker-scoped**, not global: the integration uses worker-scoped browser services, and concurrent workers may hold multiple browser processes. Resource planning uses the worker/concurrency high-water mark, never "one shared browser process."

**Consequences**

* The framework does not invent a fake “worker” concept
* Under `BrowserTest`, the framework creates and owns actor contexts and closes them before lease release
* The custom framework must not dispose objects owned by the Playwright base class
* Additional contexts created for multi-user scenarios have separate ownership

---

### ADR-003: Separate Environment Configuration from Execution Configuration

**Decision**

Environment configuration and browser execution configuration are separate models.

**Environment configuration includes:**

* Base URL
* API URL
* Feature flags
* Secret provider
* User source
* Test-data source
* Environment capabilities

**Execution configuration includes:**

* Browser
* Headless mode
* Viewport
* Timeouts
* Tracing
* Video
* Maximum concurrency

**Reason**

Browser type is an execution-matrix dimension, not an intrinsic property of QA, DEV, or UAT.

---

### ADR-004: Use Typed Scenario Scopes for Per-Test Composition

**Decision**

Each test creates a typed scenario scope.

A scenario scope contains only the Pages, Flows, cleanup services, and test-specific infrastructure required by that feature.

**Reason**

A single global `UiTestContext` with dozens of Page and Flow properties becomes an unstructured service locator.

**Example typed scenario:**

```csharp
public sealed class LoginScenario
{
    public LoginScenario(
        AuthFlow auth,
        DashboardPage dashboard)
    {
        Auth = auth;
        Dashboard = dashboard;
    }

    public AuthFlow Auth { get; }

    public DashboardPage Dashboard { get; }
}
```

Scenarios contain only application composition (Pages, Components, Flows). Actors are declared through the `ActorPlan`/lease-request parameter of `RunScenarioAsync` and acquired by the framework before the scenario graph is composed (Section 9.2/9.4) — never a scenario member, never acquired inside the body.

A scenario class is justified only when it defines a meaningful feature boundary. A scenario that is a property bag around one or two injected services should be replaced by a single feature facade or direct `ActivatorUtilities` composition — one-scenario-class-per-test-area is not mandatory, and authoring ergonomics are measured before the pattern is enforced.

**Consequences**

* The object graph is built by dependency injection
* Test classes do not manually construct Page Objects
* The framework does not expose a global `Pages` or `Flows` bag
* Each feature has an explicit composition boundary

---

### ADR-005: Lease Users Instead of Selecting Them

**Decision**

Tests acquire user leases based on role, permissions, and metadata.

The framework must not expose a convenience operation such as:

```csharp
GetUserByRole(Roles.Admin)
```

for stateful parallel tests.

**Reason**

Simple user selection can assign the same account to multiple conflicting tests.

---

### ADR-006: Use Domain-Oriented Test Data

**Decision**

JSON and Excel contain business inputs and expected outcomes.

They must not contain low-level UI commands such as:

```text
Click
Fill
Wait
Select
AssertText
```

**Reason**

A generic Excel command interpreter creates a second programming language that is difficult to compile, refactor, review, and maintain.

---

### ADR-007: No Automatic Retries in the Baseline

**Decision**

Framework version 1 does not automatically retry failed UI tests.

**Reason**

xUnit retry implementation changes:

* Test discovery
* TRX output
* Artifact structure
* User lease lifecycle
* Cleanup behavior
* Failure visibility

Introducing retries without selecting and testing one concrete mechanism would create misleading results.

**Baseline policy**

* Default retries: `0`
* Known unstable tests receive the `FlakyCandidate` trait
* Flaky tests run in a separate pipeline stage
* Original failures remain visible
* Automatic reruns require a separate architecture decision

---

### ADR-008: One Owner for Every Disposable Resource

**Decision**

Every disposable resource has exactly one owner.

| Resource                            | Owner                                    |
| ----------------------------------- | ---------------------------------------- |
| Default test `IPage`                | Playwright xUnit base class              |
| Default test `IBrowserContext`      | Playwright xUnit base class              |
| Dependency-injection scope          | Scenario scope                           |
| User lease                          | ScenarioScope, via the scope-owned TestActorManager |
| Concurrency permit                  | ScenarioScope (acquired by the factory, released in centralized teardown) |
| Additional browser context          | Multi-user scenario scope                |
| Cleanup coordinator                 | Scenario scope                           |
| Authentication state temporary file | Authentication-state provider            |

No two framework objects may both attempt to dispose the same browser context.

---

## 5. Recommended Solution Structure

Start with four projects.

```text
UiAutomationSolution/
├── src/
│   ├── UiAutomation.Framework/
│   │   ├── Configuration/
│   │   ├── Users/
│   │   ├── Secrets/
│   │   ├── Browser/
│   │   ├── TestData/
│   │   ├── Reporting/
│   │   ├── Cleanup/
│   │   └── Composition/
│   │
│   └── UiAutomation.Application/
│       ├── Components/
│       ├── Pages/
│       ├── Flows/
│       ├── Scenarios/
│       └── Models/
│
├── tests/
│   ├── UiAutomation.Tests/
│   │   ├── Smoke/
│   │   ├── Regression/
│   │   ├── Permissions/
│   │   ├── CriticalPath/
│   │   └── RoleBased/
│   │
│   └── UiAutomation.Framework.Tests/
│       ├── Unit/
│       └── Integration/
│
├── config/
│   ├── appsettings.json
│   ├── appsettings.local.json
│   ├── appsettings.dev.json
│   ├── appsettings.qa.json
│   ├── appsettings.uat.json
│   └── execution-profiles.json
│
├── test-assets/
│   ├── users/
│   └── data/
│
├── tools/
│   └── UiAutomation.Preflight/
│
├── build/
│   └── azure-pipelines.yml
│
├── docs/
│   ├── architecture.md
│   ├── test-authoring-guide.md
│   ├── selector-strategy.md
│   ├── user-allocation-strategy.md
│   └── ai-generation-rules.md
│
├── global.json
├── Directory.Build.props
├── Directory.Packages.props
└── UiAutomationSolution.sln
```

### 5.1 Project-Splitting Rules

Create additional projects only when at least one condition is true:

* A component needs independent versioning
* A component is reused by multiple solutions
* Separate teams own the components
* A dependency must be isolated
* Build time has become a measurable problem
* Security boundaries require separation

Folders are organizational boundaries. They do not automatically need to become projects.

The framework project is named `UiAutomation.Framework`, not `UiAutomation.Playwright`, to avoid namespace ambiguity with `Microsoft.Playwright`.

---

## 6. Configuration Architecture

### 6.1 Environment Selection

Use:

```text
TEST_ENV
```

Supported values:

```text
local
dev
qa
uat
```

Examples:

```powershell
$env:TEST_ENV = "qa"
dotnet test
```

CI must provide `TEST_ENV` explicitly.

Local execution may default to `local`.

### 6.2 Execution Profile Selection

Use:

```text
TEST_PROFILE
```

Example values:

```text
chromium-headless
chromium-debug
firefox-headless
webkit-headless
```

This supports browser-matrix execution without modifying environment configuration.

Execution profiles are defined in `config/execution-profiles.json` and validated at startup like environment settings.

`TEST_PROFILE` is a framework-level input. A framework adapter translates the selected profile into the configuration surface supported by the pinned `Microsoft.Playwright.Xunit.v3` integration (for example, Playwright runner options and `ContextOptions()` overrides). The exact translation mechanism is runner-specific and is validated by the Phase 1 compatibility spike.

### 6.3 Environment Settings

```csharp
public enum EnvironmentName
{
    Local,
    Dev,
    Qa,
    Uat
}
```

```csharp
public sealed record EnvironmentSettings
{
    public required EnvironmentName Name { get; init; }

    public required Uri BaseUrl { get; init; }

    public Uri? ApiBaseUrl { get; init; }

    public required string UserFile { get; init; }

    public required string TestDataRoot { get; init; }

    public required SecretProviderSettings Secrets { get; init; }

    public IReadOnlyDictionary<string, bool> Features { get; init; } =
        new Dictionary<string, bool>();

    public IReadOnlySet<string> Capabilities { get; init; } =
        new HashSet<string>();
}
```

#### Capability Gating

`Features` and `Capabilities` are consumed by capability gating:

```csharp
[Trait("RequiresCapability", "PaymentSandbox")]
[Trait("RequiresFeature", "NewLogin")]
```

Before the test body runs, the framework base class compares required traits against the selected environment and skips the test dynamically (xUnit v3 dynamic skip) with an explicit reason, for example: `Environment 'dev' lacks capability 'PaymentSandbox'`.

A test skipped for a missing capability is reported as skipped, never as passed or failed.

Capability semantics are **mandatory vs. optional**:

* A missing *optional* capability skips the affected tests
* A capability *required by the selected suite contract* (e.g., the smoke suite) fails preflight instead — critical coverage must never silently become skips
* Dynamic skip does not avoid setup cost: the test pipeline (class creation, potentially browser resources) still runs. Broad environment incompatibility belongs in discovery/pipeline filtering; dynamic skip is for unavoidable runtime decisions
* Where possible, capability/feature state is queried from the running application and its source/version recorded — a static config file drifts from the deployed application

### 6.4 Execution Profile

```csharp
public enum BrowserKind
{
    Chromium,
    Firefox,
    WebKit
}
```

```csharp
public sealed record ExecutionProfile
{
    public required string Name { get; init; }

    public BrowserKind Browser { get; init; } =
        BrowserKind.Chromium;

    public bool Headless { get; init; } = true;

    public int ViewportWidth { get; init; } = 1440;

    public int ViewportHeight { get; init; } = 900;

    public TimeSpan ActionTimeout { get; init; } =
        TimeSpan.FromSeconds(15);

    public TimeSpan NavigationTimeout { get; init; } =
        TimeSpan.FromSeconds(30);

    public TimeSpan AssertionTimeout { get; init; } =
        TimeSpan.FromSeconds(10);

    public string Locale { get; init; } = "en-US";

    public string TimeZoneId { get; init; } = "UTC";

    public CapturePolicy Trace { get; init; } =
        CapturePolicy.RetainOnFailure;

    public CapturePolicy Video { get; init; } =
        CapturePolicy.Off;   // baseline: off (see note)

    public int MaxParallelTests { get; init; } = 4;
}
```

```csharp
public enum CapturePolicy
{
    Off,
    RetainOnFailure,
    Always
}
```

Capture policies carry size caps and environment-specific retention; trace start/stop ownership is defined for both success (stop and discard under `RetainOnFailure`) and failure (stop and retain). Locale and time zone are execution-matrix dimensions and live here; **base URL is exclusively environment configuration** and is read from `EnvironmentSettings` when contexts are created.

Startup validation rejects a profile whose `MaxParallelTests` exceeds the static `maxParallelThreads` ceiling in `testconfig.json` — the profile drives the runtime semaphore, the ceiling bounds it, and no third control exists.

`MaxParallelTests` is a process-local throttle: the assembly fixture exposes a semaphore sized from the active profile, and the framework acquires it during scenario setup. A single local semaphore does **not** enforce the full effective-concurrency minimum on its own — that is enforced across distinct layers:

* xUnit scheduler (`maxParallelThreads` in `testconfig.json`): assembly-level ceiling
* Process execution gate (this semaphore): local context/resource ceiling
* User lease manager: role- and account-specific capacity
* Distributed coordinator: multi-agent environment capacity
* Azure DevOps jobs: agent-level parallelism

The effective concurrency is the minimum these layers permit; no single component computes or enforces it alone.

Under the default Playwright `PageTest` base class, this local gate has a known limitation: the isolated browser context and page are created by the base class *before* framework setup runs, so the semaphore throttles scenario execution but not context creation. Whether the gate can bound context/browser resource use depends on the base-class decision in Section 8.2 (a `BrowserTest`-based design lets the framework create contexts after acquiring the gate). This is resolved by the Phase 1 spike.

The video policy is **`Off` in the baseline**. Playwright finalizes a video only when its browser context closes, and a `PageTest`-owned default context closes after the framework wrapper has already returned — so reliable default-context video cannot be guaranteed. Video is enabled only under a design where the framework owns and closes the context before finishing teardown (the presumptive `BrowserTest` direction, ADR-002), and its availability is confirmed by the spike.

### 6.5 Fail-Fast Validation

Before tests execute, validate:

* `TEST_ENV`
* `TEST_PROFILE`
* Environment file existence
* Base URL validity
* User-file existence
* Test-data path existence
* Secret-provider settings
* Browser value
* Timeout ranges
* Required capabilities
* Configuration schema

Invalid infrastructure configuration must fail the run before hundreds of UI tests begin.

Operational rules:

* Precedence is explicit and fixed: base JSON → environment overlay → execution profile → environment-variable overrides
* Unknown configuration keys are rejected, not ignored
* Paths resolve against a stable repository/content root, never the current working directory
* One environment and one profile per test process
* A sanitized effective-configuration artifact is emitted with each run for diagnosis and reproducibility

---

## 7. Repository-Wide Test Platform Configuration

The repository explicitly selects Microsoft Testing Platform.

```json
{
  "sdk": {
    "version": "10.0.100",
    "rollForward": "latestPatch"
  },
  "test": {
    "runner": "Microsoft.Testing.Platform"
  }
}
```

`rollForward` is `latestPatch`, not `latestFeature`: for runner- and MTP-sensitive infrastructure, reproducibility matters more than automatically adopting later feature bands. Moving to a later band is an intentional, tested policy change.

Exact package versions are pinned through Central Package Management for at least `Microsoft.Playwright`, `Microsoft.Playwright.Xunit.v3`, `xunit.v3`, `Microsoft.Testing.Platform`, `Microsoft.Testing.Extensions.TrxReport`, `Microsoft.Extensions.DependencyInjection`, and `ClosedXML`. A compatibility matrix (`docs/compatibility-matrix.md`) records the verified combination and is updated when any of these changes.

When MTP is selected in `global.json`, every test project in the repository must use an MTP-compatible runner.

Test-project runner configuration belongs in:

```text
testconfig.json
```

Example:

```json
{
  "xUnit": {
    "parallelizeTestCollections": true,
    "maxParallelThreads": 4,
    "parallelAlgorithm": "conservative"
  }
}
```

xUnit v3 supports `testconfig.json` when running through Microsoft Testing Platform.

---

## 8. Runtime Lifecycle

The default lifecycle is:

```text
xUnit creates test instance
    ↓
Playwright xUnit base class provides isolated Page and BrowserContext
    ↓
Framework base class applies the execution profile and starts tracing
    ↓
Test calls RunScenarioAsync<TScenario>(actorPlan, body)
    ↓
Framework acquires the local concurrency permit
    ↓
Framework creates the per-test DI scope
    ↓
Declared actor slot set is acquired atomically (one slot in the baseline)
    ↓
Actor browser context is created (storage state applied here when enabled)
    ↓
Actor page and test identity are published through scoped accessors
    ↓
Per-actor Scenario object, Pages, Components, and Flows are resolved
    ↓
Test body executes
    ↓
On failure: original body exception is captured first
    ↓
Best-effort diagnostics are captured while contexts and leases are still live
    ↓
Authenticated application cleanup runs (lease still held)
    ↓
Tracing is stopped / video finalized per capture policy
    ↓
Actor pages and framework-created contexts are closed
    ↓
If a context close failed: the actor is quarantined, not released
    ↓
Actor leases are released (or quarantined)
    ↓
DI scope is disposed
    ↓
Concurrency permit is released
    ↓
Teardown report is attached
    ↓
Final failure selection: original body exception, else teardown exception, else pass
    ↓
Playwright base class disposes the default Page and BrowserContext
```

Two ordering rules are load-bearing:

* **Contexts close before leases release.** An exclusive user must never return to the pool while a browser session authenticated as that user is still alive.
* **Late-failure diagnostics.** If the body passed but a teardown stage fails, diagnostics are captured at that point — while the context and lease still exist — not skipped because the body succeeded. On a fully successful test, tracing is stopped and discarded per the capture policy.

Every stage from diagnostics onward is attempted regardless of earlier teardown failures. This ordering is owned entirely by `RunScenarioAsync` (Section 9.2); the test author has no lifecycle responsibilities.

### 8.1 Playwright Ownership

The official Playwright xUnit v3 base class owns:

* Default `IPage`
* Default `IBrowserContext`
* Shared Playwright instance
* Shared browser process

The custom scenario scope must not dispose the default Page or BrowserContext.

### 8.2 Framework Test Base Class

Tests derive from a framework-owned base class (`UiTestBase`) that itself derives from the official Playwright xUnit v3 test base.

The framework base class is the glue between the execution profile and the Playwright-owned lifecycle. It:

* Overrides `ContextOptions()` to apply context-level options: viewport, locale, and time zone from the active `ExecutionProfile`, base URL from `EnvironmentSettings` (base URL is environment-owned, never a profile value). Storage state is **not** a profile value either — it is user/actor-specific and can only be applied to a context created *after* the actor is known (see Section 17)
* Applies action, navigation, and assertion timeouts to the created page
* Starts Playwright tracing per test according to the trace `CapturePolicy`, exports the trace on failure, and stops/discards it on success under `RetainOnFailure`
* Performs capability gating before the test body runs
* Exposes the assembly fixture services and the single public execution entry point `RunScenarioAsync<TScenario>()` to test classes

Browser selection is not hardcoded to any single mechanism. The framework adapter translates `TEST_PROFILE` into the runner configuration actually supported by the pinned Playwright xUnit v3 package; the concrete mechanism is confirmed by the Phase 1 compatibility spike.

Failure detection does not rely on reading the final xUnit test state during ordinary teardown, because the final test state may not be published before object disposal. Failure handling belongs to `RunScenarioAsync` (Section 9.2).

---

## 9. Composition Root

### 9.1 Assembly Fixture

Use an xUnit v3 assembly fixture for immutable, thread-safe shared services:

* Validated configuration
* User definitions
* Secret-provider factory
* Distributed lease client
* Test-data-loader registry
* Dependency-injection root container

xUnit v3 assembly fixtures are shared across the assembly without disabling test parallelism, so all fixture services must be thread-safe.

```csharp
[assembly: AssemblyFixture(typeof(UiAutomationHostFixture))]
```

```csharp
public sealed class UiAutomationHostFixture :
    IAsyncLifetime
{
    // Internal: exposing the root provider publicly would let tests
    // resolve lifecycle internals and bypass RunScenarioAsync.
    internal IServiceProvider Services { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        Services = await UiAutomationBootstrapper.BuildAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (Services is IAsyncDisposable asyncDisposable)
        {
            await asyncDisposable.DisposeAsync();
        }
    }
}
```

The assembly fixture must not store:

* Current test Page
* Current browser context
* Current user
* Current test ID
* Mutable per-test collections

### 9.2 Scenario Execution

**Public contract — actor-first from the baseline.** The only test-facing execution API is:

```csharp
// General form: named actor slots.
protected ValueTask RunScenarioAsync<TScenario>(
    ActorPlan plan,
    Func<ScenarioExecution<TScenario>, ValueTask> body)
    where TScenario : notnull;

// Single-actor convenience: wraps a one-slot plan.
protected ValueTask RunScenarioAsync<TScenario>(
    UserLeaseRequest actor,
    Func<ScenarioExecution<TScenario>, ValueTask> body)
    where TScenario : notnull;
```

defined on `UiTestBase`. Actors are **declared before the body and acquired before any scenario graph is composed** — never acquired inside the body. The lifecycle is actor-first even with a single actor:

```text
1. Atomically acquire the declared actor slot set (one slot in the baseline)
2. Create the actor's browser context (storage state applied here when enabled)
3. Compose the actor's Scenario/session graph against that context's page
4. Execute the body
```

This ordering exists in Phase 1 with exactly one actor. Multi-user support then *is* additive — more named slots, more `ActorSession`s — with no lifecycle redesign, because nothing in the baseline ever composed a scenario graph before its actor existed.

`ScenarioExecution<TScenario>` exposes `Scenario`, the actor (`Actor` for the single-slot form, `Session("name")` for named slots), and the test cancellation token — nothing disposable. Tests never receive, dispose, or otherwise manage a lifecycle object; the framework owns scope creation, scope disposal, teardown accumulation, context disposal, actor release, DI disposal, concurrency-permit release, and final failure selection.

**Contract status: candidate, confirmed by the Phase 1 spike — not finalized.** No contract is called stable while a scheduled spike can invalidate it.

The wrapper is **one-shot per test**: a test that calls it zero times fails a post-test guard in `UiTestBase`; a second call fails deterministically.

**Deterministic outcome contract.** Internally the wrapper produces:

```csharp
public sealed record ScenarioExecutionResult
{
    public ExceptionDispatchInfo? BodyFailure { get; init; }

    public IReadOnlyList<TeardownFailure> TeardownFailures { get; init; }

    public ScenarioOutcome Outcome { get; init; }
}
```

and the final decision is fixed:

```text
Body failure exists
    → rethrow the original body exception unchanged
    → attach teardown failures as metadata

No body failure, teardown failure exists
    → throw one infrastructure/teardown exception carrying all teardown failures

No failures
    → pass
```

Warnings alone are never the only record of a teardown failure after a passing body — that case always fails the test.

**Non-normative pseudocode.** The sketch below is illustrative only. It is not compiling reference code, it assumes the Playwright-provided default page, and it knowingly does **not** implement everything the normative text requires. The normative artifacts are: the Section 8 ordering, the guarantees list below, and the mandatory permutation suite (Section 26.1) — the reference implementation is correct when that suite passes, not when it resembles this sketch.

Known deltas the reference implementation must close (all covered by the permutation suite):

* Teardown order per Section 8: contexts close **before** actor release
* `ScenarioExecutionResult` is actually produced and drives final failure selection
* Framework-created contexts are tracked by the scope and disposed by it
* The body delegate receives `ScenarioExecution<TScenario>`, not the bare scenario
* Actor acquisition happens **before** scenario composition per the `ActorPlan` (the sketch predates actor-first ordering and wrongly exposes an `Actors` manager for in-body acquisition)
* **No separate `DisposeAsync` path.** The sketch's `IAsyncDisposable` has a real race: disposal during an active run sees `_completed == false` and runs emergency cleanup concurrently with the live body. The real scope has exactly one wrapper-owned completion path (or, if disposal is retained, CAS state ownership plus a completion `TaskCompletionSource` that disposal must await)
* Emergency-path warning calls are themselves guarded
* Reporting failures enter the accumulator before publication, with a bounded stderr fallback

```csharp
internal sealed class ScenarioScope<TScenario> :
    IAsyncDisposable
    where TScenario : notnull
{
    private readonly AsyncServiceScope _serviceScope;
    private readonly TestCleanupCoordinator _cleanup;
    private readonly IFailureDiagnostics _diagnostics;
    private readonly TestActorManager _actors;
    private readonly CancellationToken _teardownToken;
    private int _state = (int)ScenarioState.Created;
    private bool _completed;
    private int _disposed;

    public ScenarioScope(
        AsyncServiceScope serviceScope,
        TScenario scenario,
        TestCleanupCoordinator cleanup,
        IFailureDiagnostics diagnostics,
        TestActorManager actors,
        CancellationToken teardownToken)
    {
        _serviceScope = serviceScope;
        Scenario = scenario;
        _cleanup = cleanup;
        _diagnostics = diagnostics;
        _actors = actors;
        _teardownToken = teardownToken;
    }

    public TScenario Scenario { get; }

    public TestActorManager Actors => _actors;

    public async ValueTask RunAsync(
        Func<TScenario, ValueTask> body)
    {
        // Thread-safe transition: exactly one caller may move
        // Created -> Running. Concurrent or repeated calls fail
        // deterministically instead of double-running teardown.
        var previous = (ScenarioState)Interlocked.CompareExchange(
            ref _state,
            (int)ScenarioState.Running,
            (int)ScenarioState.Created);

        if (previous != ScenarioState.Created)
        {
            throw new InvalidOperationException(
                $"RunAsync is invalid in state {previous}.");
        }

        var teardown = new TeardownFailureAccumulator();
        ExceptionDispatchInfo? bodyFailure = null;

        try
        {
            await body(Scenario);
        }
        catch (Exception exception)
        {
            bodyFailure = ExceptionDispatchInfo.Capture(exception);
        }

        Volatile.Write(ref _state, (int)ScenarioState.TearingDown);

        // Every teardown stage is attempted. A failure in any stage is
        // captured into the accumulator, never thrown, and never blocks a
        // later stage. Diagnostics are best-effort and cannot stop cleanup.
        if (bodyFailure is not null)
        {
            await teardown.RunAsync(
                "diagnostics",
                () => _diagnostics.CaptureFailureAsync(
                    bodyFailure.SourceException));
        }

        await teardown.RunAsync(
            "cleanup",
            () => _cleanup.ExecuteAsync(_teardownToken));

        await teardown.RunAsync(
            "actor-release",
            () => _actors.ReleaseAllAsync());

        // DI scope disposal is part of the PRIMARY teardown sequence, so a
        // disposal failure after a passing body fails the test instead of
        // degrading to a warning.
        await teardown.RunAsync(
            "scope-disposal",
            () => _serviceScope.DisposeAsync());

        _completed = true;
        Volatile.Write(ref _state, (int)ScenarioState.Completed);

        // Publishing is itself best-effort: a failure while adding warnings,
        // attachments, or metadata is recorded to a bounded stderr fallback
        // and never blocks the final failure selection.
        teardown.PublishSafely(TestContext.Current);

        if (bodyFailure is not null)
        {
            bodyFailure.Throw();                    // original failure, unchanged
        }

        if (teardown.HasFailures)
        {
            throw teardown.ToTeardownException();   // body passed, teardown failed
        }
    }

    // EMERGENCY fallback only. The primary path (RunAsync) has already
    // disposed everything and set _completed; this body then does nothing.
    // It runs only if the framework failed before/without RunAsync, in which
    // case an exception is already unwinding — so this method never throws,
    // and the situation is reported as a framework-initialization failure,
    // never as a silent pass.
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        if (!_completed)
        {
            TestContext.Current.AddWarning(
                "ScenarioScope disposed without RunAsync completing; " +
                "emergency cleanup and lease release executed. " +
                "This indicates a framework-initialization failure.");

            await SafeAsync(
                "emergency-cleanup",
                () => _cleanup.ExecuteAsync(_teardownToken));

            await SafeAsync(
                "emergency-actor-release",
                () => _actors.ReleaseAllAsync());

            await SafeAsync(
                "emergency-scope-disposal",
                () => _serviceScope.DisposeAsync());
        }
    }

    private static async ValueTask SafeAsync(
        string stage,
        Func<ValueTask> action)
    {
        try
        {
            await action();
        }
        catch (Exception exception)
        {
            TestContext.Current.AddWarning(
                $"Teardown stage '{stage}' failed: {exception}");
        }
    }
}
```

`ScenarioState` is an explicit lifecycle state machine (`Created → Running → TearingDown → Completed`) with thread-safe transitions:

* Exactly one caller may transition `Created → Running` (`Interlocked.CompareExchange`)
* Concurrent or repeated `RunAsync` calls fail deterministically
* `DisposeAsync` cannot race `RunAsync` into double-releasing leases (idempotent via `Interlocked.Exchange`, and a no-op once `RunAsync` completed)
* Completion state is published only after every teardown stage has been attempted
* These properties are verified by dedicated concurrency tests (Section 26.1), not assumed from the state names

The teardown split is explicit:

* **Primary managed teardown** (`RunAsync`): owns failure reporting. Body failed → the body exception is preserved and rethrown. Body passed but any teardown stage failed (including DI-scope disposal) → the test fails with one infrastructure/teardown exception. No silent pass is possible on this path.
* **Emergency fallback disposal** (`DisposeAsync`): reached only when the framework failed before or without `RunAsync` — an exception is already unwinding, so this path never throws, attempts every remaining stage, and reports itself as a framework-initialization failure. Because tests cannot create scopes (the scope is internal and only `RunScenarioAsync` constructs it), this path cannot be reached by test-author mistake.

`_cleanup.ExecuteAsync(_teardownToken)` uses a dedicated teardown cancellation token, not the (possibly already-cancelled) test-timeout token, so cleanup still runs after a test times out.

Wrapper guarantees:

* Failure diagnostics are captured while user leases are still held, but are best-effort: a diagnostics failure cannot prevent cleanup, actor release, or scope disposal
* Cleanup runs before lease release, and actor contexts close before lease release (the Section 8 ordering); every teardown stage is attempted even if an earlier stage fails
* The original test exception is rethrown unchanged through `ExceptionDispatchInfo.Capture(...).Throw()`; no teardown stage — diagnostics, cleanup, actor release, or disposal — can replace it
* A teardown failure after a *failed* test is reported as metadata and never replaces the original failure
* Any teardown failure after a *passed* test — including scope disposal — fails the test as an infrastructure/teardown failure; warnings alone are never the only record
* Multiple teardown failures are accumulated and published together via `ScenarioExecutionResult`
* Reporting operations (warnings, attachments, metadata serialization, artifact writes) are themselves best-effort: a reporting failure is captured into the accumulator with a bounded stderr fallback and never prevents cleanup, actor release, context disposal, DI disposal, or permit release

Failures that occur before the body starts (scope creation, configuration, fixture initialization) use a minimal fallback diagnostic path: the framework base class records the exception with test identity where available and, when a page exists, captures a best-effort screenshot. Such failures are classified as framework-initialization or infrastructure failures, not application-test failures.

Code executed before the wrapper is minimized. Outside `RunScenarioAsync` there is:

* No user acquisition
* No application navigation
* No test-data setup
* No secret resolution
* No product-specific logic

Test metadata (traits) and the `RunScenarioAsync` call itself are the only things that legitimately live outside the wrapper.

### 9.3 Scenario Factory

The factory is internal infrastructure invoked only by `RunScenarioAsync`; tests never call it.

```csharp
internal interface IScenarioScopeFactory
{
    ValueTask<ScenarioScope<TScenario>> CreateAsync<TScenario>(
        IPage page,
        TestIdentity test,
        CancellationToken cancellationToken = default)
        where TScenario : notnull;
}
```

The factory:

1. Acquires the parallelism gate (Section 6.4)
2. Creates a child dependency-injection scope
3. Assigns the current test’s `IPage` to a scoped page accessor
4. Assigns test identity and artifact context to scoped accessors
5. Creates a cleanup coordinator and the scope-owned `TestActorManager`
6. Resolves the typed scenario
7. Returns the scenario scope

**Construction is transactional.** The factory acquires the concurrency permit, creates the DI scope, sets accessors, and resolves the scenario — and if any intermediate step throws, everything already acquired is rolled back in reverse order in `catch`/`finally`. The permit has one owner (the scenario scope; see ADR-008) and is released in the same centralized teardown sequence as every other resource. A factory failure never leaks a permit or a DI scope.

Microsoft.Extensions.DependencyInjection does not support adding registrations to an already-created scope. Per-test values therefore flow through mutable scoped accessors registered at the root:

```csharp
public sealed class PageHandle   // deliberately NOT IDisposable
{
    public IPage Page { get; private set; } = null!;

    internal void Set(IPage page) => Page = page;
}
```

**`IPage` is never registered as a DI service.** MEDI disposes disposable instances it resolves — including factory-returned ones — so a scoped `IPage` registration would let scope disposal close a Playwright-owned page, violating ADR-008. Page Objects instead take the non-disposable `PageHandle`, or are constructed via `ActivatorUtilities` with the page passed as an explicit argument. A mandatory unit test proves that disposing the DI scope leaves the Playwright-owned page open.

### 9.4 Actors

An actor represents one identity acting inside a scenario.

Ownership model:

```text
ScenarioScope
    └── TestActorManager
            └── tracks TestActor lease handles
```

* `TestActor` is a lease-backed actor handle; it is not independently disposable
* `TestActorManager` is internal machinery: it acquires the declared slot set before scenario composition and registers the resulting `TestActor` handles. Tests never call it — they declare actors in the `ActorPlan` and receive them via `execution.Actor` / `execution.Session("name")`
* `ScenarioScope` is the sole lifecycle owner of all actor leases (ADR-008)
* Tests do not dispose actors, do not dispose leases, and do not access `IUserLeaseManager` directly

This ensures the cleanup-before-release ordering cannot be bypassed.

```csharp
public sealed class TestActorManager
{
    public ValueTask<TestActor> CreateAsync(
        UserLeaseRequest request,
        CancellationToken cancellationToken = default);

    public ValueTask<IReadOnlyList<TestActor>> CreateManyAsync(
        IReadOnlyList<UserLeaseRequest> requests,
        CancellationToken cancellationToken = default);
}
```

```csharp
public sealed class TestActor
{
    public TestUser User { get; }

    // No credential access on the public surface. Authentication flows
    // accept the actor handle and resolve credentials internally through
    // the framework's credential orchestrator.
}
```

Tests never see plaintext credentials. The application authentication Flow takes the actor:

```csharp
await execution.Scenario.Auth.LoginAsAsync(admin);
```

`LoginAsAsync(actor)` keeps authentication an explicit, visible test step while the credential resolution and consumption happen inside the Flow via the framework orchestrator. Secret-bearing state is scenario-scoped and becomes **inactive when the execution completes**: an actor handle stored in a test field is invalidated after the wrapper returns, so escaped handles cannot retain usable credentials or lease authority.

Actor rules:

* `CreateManyAsync` is the only public multi-actor acquisition path; it delegates to `IUserLeaseManager.AcquireSetAsync`, acquires the complete actor set or none, and never loops over individual `AcquireAsync` calls
* All actor leases are released by the scenario-scope completion sequence after application-data cleanup; tests never use per-actor `await using`
* Resolved credentials are cached inside the framework's credential orchestrator for the scenario lifetime only; the cached value is memory-only and deactivated when the execution completes. This is the single credential-caching policy — there is no run-wide plaintext secret cache (see Section 16), and no plaintext credential type appears on the test-facing API
* The actor provides credential resolution but does not know how the application performs login; it never calls `AuthFlow` automatically
* Authentication-state reuse (Section 17) may later be represented through the actor, but it remains an explicit test or scenario decision

The application-specific authentication Flow remains an explicit, visible step in the test — authentication is never a hidden side effect of scope or actor creation.

---

## 10. Page and Component Design

### 10.1 Locator Strategy

Locator choice is context-dependent, not a rigid hierarchy. Prefer accessible role-and-name locators when the accessible contract is itself part of the behavior being validated (this keeps tests aligned with what assistive technologies rely on). Prefer a configured test ID when a stable application-specific automation contract is needed — for example when accessible names are dynamic, localized, or shared across many same-role elements.

A reasonable default preference order for enterprise apps with dynamic markup:

1. Configured test ID (stable automation contract) or accessible role and name (accessibility contract) — chosen per the guidance above
2. Associated label (form fields)
3. Placeholder
4. Stable visible text, only when text is genuinely part of the requirement
5. Stable semantic CSS, only when no better contract exists
6. XPath only as a last resort for exceptional legacy markup

Do not treat test-ID-first as an absolute rule that discourages accessibility-oriented locators.

Avoid:

* Generated CSS classes
* Deep DOM paths
* `nth-child`
* Styling attributes
* Random IDs
* Selectors copied directly from browser developer tools without review

### 10.2 Component Object Example

```csharp
public sealed class NavigationMenu
{
    private readonly IPage _page;

    public NavigationMenu(IPage page)
    {
        _page = page;
    }

    private ILocator RequestsLink =>
        _page.GetByRole(
            AriaRole.Link,
            new()
            {
                Name = "Requests"
            });

    public Task OpenRequestsAsync() =>
        RequestsLink.ClickAsync();
}
```

### 10.3 Page Object Example

```csharp
public sealed class LoginPage
{
    private readonly IPage _page;

    public LoginPage(IPage page)
    {
        _page = page;
    }

    private ILocator UsernameInput =>
        _page.GetByTestId("login-username");

    private ILocator PasswordInput =>
        _page.GetByTestId("login-password");

    private ILocator SubmitButton =>
        _page.GetByTestId("login-submit");

    public ILocator ValidationMessage =>
        _page.GetByTestId("login-validation-message");

    public Task OpenAsync() =>
        _page.GotoAsync("/login");

    public async Task SubmitAsync(
        LoginCredentials credentials)
    {
        await UsernameInput.FillAsync(credentials.Username);
        await PasswordInput.FillAsync(credentials.Password);
        await SubmitButton.ClickAsync();
    }
}
```

```csharp
public sealed class DashboardPage
{
    private readonly IPage _page;

    public DashboardPage(IPage page)
    {
        _page = page;
    }

    public ILocator Root =>
        _page.GetByTestId("dashboard");

    public ILocator CurrentUser =>
        _page.GetByTestId("current-user");
}
```

Page Objects may expose named locators for assertions.

They must not expose raw selector strings.

Authoring rules refined:

* **Exposed `ILocator` is an assertion target, not an action surface.** A public locator technically allows `ClickAsync`/`FillAsync` from a test, so the boundary is enforced by an analyzer permitting exposed locators only inside Playwright assertion calls — or the Page Object exposes semantic assertion methods (`Dashboard.ShouldBeVisibleAsync()`) instead. "Final assertion visible" means the assertion *intent* is visible in the test, not that every test must call `Assertions.Expect` directly
* **Component Objects are root-scoped.** A component receives its root `ILocator` and resolves child locators relative to it, so repeated components (two navigation regions, grid rows) never collide via page-wide queries
* **The test-ID contract is governed**: the attribute is set through Playwright configuration; frontend ownership, naming rules, uniqueness, review expectations, and change/removal policy are documented in `docs/selector-strategy.md`

---

## 11. Flow Design

Flows represent reusable business behavior.

```csharp
public sealed class AuthFlow
{
    private readonly LoginPage _loginPage;

    public AuthFlow(LoginPage loginPage)
    {
        _loginPage = loginPage;
    }

    public async Task LoginAsync(
        LoginCredentials credentials)
    {
        await _loginPage.OpenAsync();
        await _loginPage.SubmitAsync(credentials);
    }
}
```

Flows may:

* Coordinate multiple Page Objects
* Coordinate multiple Component Objects
* Represent business journeys
* Return domain-relevant results

Flows must not:

* Contain selectors
* Resolve secrets
* Allocate users
* Load configuration files
* Parse test data
* Hide the final scenario assertion
* Add custom click retry loops
* Use `Task.Delay`

"No hidden final assertion" does not mean "no assertions in Flows." A Flow may contain *synchronization and invariant* assertions needed to execute reliably — transition completion, required intermediate page readiness, precondition invariants, expected popup appearance, successful navigation. The distinction is by purpose:

* **Synchronization / invariant assertions** may live in a Flow (they make the journey deterministic)
* **Scenario-outcome assertions** — the business result the test exists to prove — remain visible in the test

A Flow enforcing an internal invariant is fine; a Flow silently containing the test's real pass/fail decision is not.

---

## 12. Canonical Test Example

```csharp
public sealed class LoginSmokeTests :
    UiTestBase
{
    public LoginSmokeTests(
        UiAutomationHostFixture host)
        : base(host)
    {
    }

    [Fact]
    [Trait("Category", "Smoke")]
    [Trait("Role", "Admin")]
    public async Task Admin_can_log_in()
    {
        await RunScenarioAsync<LoginScenario>(
            new UserLeaseRequest
            {
                Role = Roles.Admin
            },
            async execution =>
            {
                await execution.Scenario.Auth.LoginAsAsync(
                    execution.Actor);

                await Assertions.Expect(execution.Scenario.Dashboard.Root)
                    .ToBeVisibleAsync();
            });
    }
}
```

`RunScenarioAsync<TScenario>()` is the single public entry point on `UiTestBase`. Tests never see a disposable lifecycle object: there is no `await using`, no scope variable, and no separately callable disposal. The framework — not the test author — owns scope creation, scope disposal, teardown accumulation, actor release, context disposal, DI disposal, concurrency-permit release, and final failure selection.

This example follows the framework rules:

* No selector appears in the test
* The test does not use `IPage` for application interactions
* The entire test body runs inside `RunScenarioAsync`; there is no lifecycle object the test could misuse
* The actor is declared before the body and acquired before the scenario graph is composed (actor-first ordering); the framework is the sole lifecycle owner of all leases
* Credential storage is hidden; the authentication Flow remains explicit
* The scenario assertion remains visible

---

## 13. Cancellation and Timeouts

Cancellation tokens are used where APIs genuinely support cooperative cancellation:

* User lease acquisition
* Secret retrieval
* API setup
* API cleanup
* File loading
* Distributed lock operations

Playwright action APIs use Playwright timeout configuration rather than framework cancellation-token ceremony.

Do not add meaningless code such as:

```csharp
cancellationToken.ThrowIfCancellationRequested();
await locator.ClickAsync();
```

when the token cannot interrupt the Playwright action.

Use:

* Playwright action timeout
* Navigation timeout
* Assertion timeout
* xUnit test timeout
* Environment health-check timeout

**xUnit timeouts are cooperative, not guarantees.** An xUnit v3 timeout signals `TestContext.Current.CancellationToken`; it does not guarantee the timed-out body or its teardown finishes. The design therefore:

* Exposes the test token through `ScenarioExecution` and links it automatically into all cooperative framework operations (actor acquisition, secret resolution, API setup/cleanup)
* Gives the body a soft deadline with reserved teardown time, so cleanup normally starts before the watchdog fires
* Keeps Playwright action/navigation/assertion timeouts below the outer xUnit watchdog
* Treats the xUnit timeout as a last-resort watchdog after which cleanup and artifacts are **not** guaranteed
* Relies on lease expiry, quarantine, and the stale-data janitor for process-loss recovery — never on teardown having run

Note: Playwright's assertion timeout is not a page setting — it is configured through runner configuration or `Assertions.SetDefaultExpectTimeout`, which is process-global and must not be changed concurrently mid-run.

---

## 14. User Model

Concrete roles such as `Approver`, `Reviewer`, `FinanceUser`, and `SupportAgent` are **application vocabulary** and belong in `UiAutomation.Application`, not in the reusable `UiAutomation.Framework` project.

To avoid a reverse dependency (framework types referencing an application enum), the framework's user and lease models use a framework-neutral role value:

```csharp
// Framework-owned, application-agnostic.
public readonly record struct RoleKey(string Value)
{
    public static implicit operator RoleKey(string value) => new(value);
}
```

The Application project owns the vocabulary and maps it to `RoleKey` constants:

```csharp
// Defined in UiAutomation.Application, not the framework.
public static class Roles
{
    public static readonly RoleKey Admin = "Admin";
    public static readonly RoleKey Manager = "Manager";
    public static readonly RoleKey Approver = "Approver";
    // ...
}
```

The framework never hardcodes the target application's role names. If the framework is only ever used for this one application, collapse the Framework/Application split rather than pretending the framework is reusable while it already knows product roles — but do not keep the contradiction of framework models depending on an application enum.

```csharp
public sealed record CredentialReference
{
    public required string Username { get; init; }

    public required string PasswordSecretName { get; init; }
}
```

```csharp
public sealed record TestUser
{
    public required string Key { get; init; }

    public required CredentialReference Credentials { get; init; }

    public required RoleKey Role { get; init; }

    public IReadOnlySet<string> Permissions { get; init; } =
        new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, string> Metadata { get; init; } =
        new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase);

    public int SessionCapacity { get; init; } = 1;

    public ConcurrencyMode ConcurrencyMode { get; init; } =
        ConcurrencyMode.Exclusive;

    public bool IsActive { get; init; } = true;
}
```

`SessionCapacity` alone does not prove that multiple tests can safely share an account — it must be paired with an explicit `ConcurrencyMode`, which defaults to `Exclusive`:

```csharp
public enum ConcurrencyMode
{
    Exclusive,
    ReadOnlyShared,
    IsolatedTenantShared,
    ApplicationVerifiedShared
}
```

A capacity greater than 1 is honored only for a non-exclusive mode. `SessionCapacity = 3` with `ReadOnlyShared` means three concurrent read-only sessions; with `Exclusive` the capacity is effectively 1.

---

## 15. User Leasing

### 15.1 Lease Request

```csharp
public sealed record UserLeaseRequest
{
    public RoleKey? Role { get; init; }

    public string? PersonaKey { get; init; }

    public IReadOnlySet<string> RequiredPermissions { get; init; } =
        new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);

    public IReadOnlySet<string> ForbiddenPermissions { get; init; } =
        new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, string> RequiredMetadata { get; init; } =
        new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase);

    public IReadOnlySet<string> RequiredAccountStates { get; init; } =
        new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);

    public TimeSpan AcquisitionTimeout { get; init; } =
        TimeSpan.FromMinutes(2);
}
```

At least one criterion is required; the lease manager rejects empty requests at validation time.

`ForbiddenPermissions` exists because negative tests cannot be expressed with required permissions alone: a "user without approval rights sees access denied" test is invalidated if the matcher hands it a user who *also* holds approval rights. `PersonaKey` selects an exact configured persona when a test needs a precise profile rather than criteria matching. Core requirements are expressed through these typed fields — not through the untyped metadata dictionary.

### 15.2 Lease Interfaces

```csharp
public interface IUserLeaseManager
{
    ValueTask<TestUserLease> AcquireAsync(
        UserLeaseRequest request,
        CancellationToken cancellationToken = default);

    ValueTask<TestUserLeaseSet> AcquireSetAsync(
        IReadOnlyList<UserLeaseRequest> requests,
        CancellationToken cancellationToken = default);
}
```

`AcquireSetAsync` is mandatory for multi-user scenarios.

It must acquire all required users atomically or acquire none.

This prevents deadlocks where two tests each hold one account while waiting for the other account.

### 15.3 Capacity Behavior

When no eligible user is immediately available:

1. The request enters a bounded queue
2. The request waits until its acquisition timeout
3. The lease manager returns an infrastructure-capacity error if the timeout expires
4. The failure is classified separately from a product defect

Pipeline parallelism must not exceed the available user and environment capacity.

Recommended effective concurrency:

```text
minimum of:
- configured maximum parallel tests
- environment capacity
- eligible user-pool capacity
- agent resource capacity
```

### 15.4 Distributed Leasing

For one process, an in-memory implementation is acceptable.

For multiple agents or processes, use a distributed implementation capable of:

* Atomic lease-set acquisition
* Lease expiry
* Heartbeat or renewal
* Recovery after terminated agents
* Run ID and test ID ownership
* Capacity enforcement
* **Fencing tokens** — each lease carries a monotonically increasing ownership token so a terminated agent that resumes after its lease has expired and been reassigned cannot release or mutate the current lease. Expiry and heartbeats alone are not sufficient.

Recommended implementations:

* Dedicated user-allocation service
* SQL-backed transactional lease table

A multi-user lease set must not be implemented through unrelated independent locks.

**Lease loss must propagate to the running test.** Heartbeats and fencing tokens protect the lease *store*; they cannot stop a stale browser from continuing to mutate the application. The distributed design must define heartbeat cadence, TTL, authoritative server time, clock-skew handling, and partition behavior — and when a lease is lost mid-test: cancel the actor session, close its contexts, fail the test as an infrastructure failure, and transition the account to `Unknown`/quarantined, never directly back to available. Lease renewal continues through diagnostics, cleanup, and context closure, not just the body.

**In-memory leasing is per-process, and MTP can run test modules concurrently.** A root-level `dotnet test` may execute `UiAutomation.Tests` and the application-dependent part of `UiAutomation.Framework.Tests` in parallel processes with independent in-memory allocators — assigning the same user twice. Phase 1 therefore requires either exactly one application-hitting test module per run (enforced with `--max-parallel-test-modules 1` where applicable, and sequential pipeline steps), or an immediate shared lease coordinator. This constraint is stated in the roadmap, not left as an operational surprise.

### 15.5 Lease-Set Requests with Named Slots

A per-request boolean cannot express set-level constraints ("all distinct", "these two distinct but the third may match", "same tenant", "requester must not be the approver"). Multi-actor acquisition therefore uses a set request with named actor slots and explicit cross-slot constraints:

```csharp
public sealed record LeaseSetRequest
{
    public required IReadOnlyDictionary<string, UserLeaseRequest> Slots { get; init; }

    public IReadOnlyList<SlotConstraint> Constraints { get; init; } = [];

    public TimeSpan AcquisitionTimeout { get; init; } =
        TimeSpan.FromMinutes(2);
}
```

`SlotConstraint` expresses relations between named slots: `Distinct("requester", "approver")`, `SameTenant("requester", "approver")`, and application-defined relationship constraints. Set acquisition has:

* One acquisition deadline for the whole set
* Deterministic matching (same pool state → same assignment)
* Cooperative cancellation
* Fairness/aging so large sets are not starved by streams of single-user requests — verified by starvation tests

A multi-actor request may require *different* accounts even when one account technically satisfies every slot (a requester who is also a valid approver must not approve their own request); the `Distinct` constraint is the norm, not the exception.

### 15.6 Release Outcomes and Quarantine

A failed test can leave an account in a bad state (password reset required, session locked, approval queue altered, tenant data mutated). `ReleaseAllAsync` therefore carries an outcome rather than always returning the account to the pool:

```csharp
public enum ActorReleaseOutcome
{
    Healthy,
    SuspectedContaminated,
    AuthenticationFailed,
    CleanupFailed,
    Quarantined
}
```

Only `Healthy` returns the account immediately to the available pool. Other outcomes quarantine the account for inspection or automated recovery, so a contaminated account is not handed to the next test. A cleanup failure (Section 21) can drive a `Quarantined` outcome.

`ReleaseAllAsync` semantics:

* Idempotent — repeated release is safe and does not double-return capacity
* Safe after partial release — already-released actors are skipped, remaining ones are still attempted
* Reports a per-actor outcome, not a single aggregate result
* Bound to the lease ownership/fencing token (Section 15.4) — it cannot release a lease that has already expired and been reassigned to another owner

A generic "release everything" call without these semantics is unsafe in a distributed implementation.

Quarantine is an operational design, not a label. It requires:

* Durable states with an owner, TTL, and SLA
* Defined recovery/reset operations and health verification before return to the pool
* Alerting and capacity thresholds (quarantined users reduce effective pool size)
* An audited manual recovery path
* Plausibility-based automatic quarantine: a failed test quarantines a user only when contamination is plausible (stateful mutation started, cleanup failed, context close failed) — otherwise routine assertion failures would collapse the pool
* Fail-closed on ambiguity: ambiguous release outcomes or expired lease ownership always quarantine, never silently return to available

---

## 16. Secret and Credential Handling

```csharp
public interface ISecretProvider
{
    ValueTask<string> GetSecretAsync(
        string secretName,
        CancellationToken cancellationToken = default);
}
```

```csharp
public sealed record LoginCredentials(
    string Username,
    string Password)
{
    public override string ToString() =>
        $"{Username}:[REDACTED]";
}
```

Record types generate a `ToString()` that prints all members. Every type carrying a secret must override `ToString()` so accidental logging, string interpolation, or exception messages cannot leak the value.

```csharp
public interface ICredentialResolver
{
    ValueTask<LoginCredentials> ResolveAsync(
        TestUser user,
        CancellationToken cancellationToken = default);
}
```

Credential caching is scenario-scoped only (Section 9.4). The baseline does **not** keep a run-wide plaintext secret cache: it would extend secret lifetime across the whole process, survive rotation, widen memory-dump exposure, and share secret material across unrelated tests.

If Key Vault traffic later becomes a measurable problem, introduce an explicitly designed bounded cache with a short TTL, no logging, no enumeration, controlled invalidation, and defined rotation behavior — as its own decision, not an implicit default.

Overriding `ToString()` is necessary but not sufficient: structured logging destructuring, JSON serialization, debugger inspection, and `Exception.Data` can still surface public secret properties. Secret-bearing objects must not be passed into generic logging, serialization, or destructuring APIs.

Credentials must not be stored in:

* Source control
* Excel files
* JSON test-data files
* Screenshots
* Logs
* TRX output
* Trace metadata

---

## 17. Authentication Storage State

Storage-state reuse is not part of the first vertical slice.

It is introduced only after UI login tests are stable.

### 17.1 Cache Key

The key must be strong enough that state can never be reused across an incompatible boundary:

```text
{environment}/{tenant-or-auth-realm}/{base-url-hash}/{auth-config-version}/{user-key}/{credential-version}/{scopes}/{browser}/{session-slot}
```

Environment/user/auth-version alone is too weak: tenant, auth realm, application/auth configuration version, credential/security-stamp version, scopes, browser, and session slot all invalidate state when they change.

A storage-state file **is a credential**: restrictive ACLs, short TTL, path/symlink protection, no shared build caches, and preferably job-local or in-memory storage.

### 17.2 Concurrency Rules

* Only the current user lease owner may create or refresh storage state
* State is written to a unique temporary file and atomically renamed after successful creation; readers never consume a partially written file
* Atomic rename alone is insufficient against stale owners: publication uses **CAS/generation metadata tied to the current lease fencing token**, and ownership is revalidated immediately before promotion, so an expired owner cannot overwrite a newer state file
* State is invalidated on authentication failure, password rotation, quarantine, logout, or cleanup failure
* Expired state is recreated
* Storage-state files remain in the agent’s secured temporary directory, deleted per retention policy, never committed, and never written beneath `TestResults`

Non-exclusive users may reuse state only when the application supports concurrent sessions safely.

---

## 18. Test Data Strategy

### 18.1 Primary Format

JSON is the primary source because it provides:

* Readable diffs
* Merge-friendly changes
* Code review visibility
* Schema validation
* Easy AI generation
* Easy source-control history

### 18.2 Excel Adapter

ClosedXML is supported for spreadsheet-owned scenarios.

Excel must contain:

* Business input
* Expected outcomes
* Role or permission requirements
* Scenario metadata

Excel must not contain:

* CSS selectors
* XPath selectors
* UI action commands
* Passwords
* Arbitrary executable instructions

### 18.3 Domain Case Example

Naming rule: data-side inputs use the `*Case` suffix; DI composition objects keep the `*Scenario` suffix (`LoginScenario`). The two concepts were previously both called "scenario", which confused the API surface — a `Case` is what a test consumes as input, a `Scenario` is what the framework composes for it.

```csharp
public sealed record ApproveRequestCase
{
    public required string Id { get; init; }

    public int SchemaVersion { get; init; } = 1;

    public required RoleKey ApproverRole { get; init; }

    public IReadOnlySet<string> RequiredPermissions { get; init; } =
        new HashSet<string>();

    public required RequestInput Request { get; init; }

    public required ApprovalExpectation Expected { get; init; }
}
```

### 18.3.1 Case Discovery Model

External cases must surface as **individual xUnit test cases**, never as a loop inside one `[Fact]` (which destroys per-case reporting, filtering, parallelism, and artifacts). The loader feeds a custom xUnit v3 data source / generated `TheoryDataRow<T>` model that produces, per row:

* A stable case ID (from the data, not the row index)
* A deterministic display name
* Case-level traits
* A defined behavior for validation failures (an invalid row becomes one failing case with the loader error, not a skipped file)

### 18.4 Case Versioning

Externally maintained data files include:

```text
SchemaVersion
```

Initial version:

```text
1
```

Schema changes require:

* Migration notes
* Loader compatibility decision
* Updated validation
* Updated examples

### 18.5 Excel Review Policy

When Excel files are stored in source control:

* A generated JSON or CSV representation must accompany the pull request
* Reviewers inspect the generated text diff
* The generated representation must match the workbook
* Workbook ownership must be documented

Where possible, Excel is stored outside the repository and validated during ingestion.

---

## 19. Assertions

Use Playwright assertions for live UI state:

```csharp
await Assertions.Expect(
        dashboardPage.Root)
    .ToBeVisibleAsync();
```

Use xUnit assertions for domain state:

```csharp
Assert.Equal(
    Roles.Admin,
    user.Role);
```

Rules:

* Page Objects expose named locators or state
* Flows perform business journeys
* Tests own scenario-level assertions
* Repeated assertion groups may use dedicated assertion objects
* A critical assertion must not be silently hidden inside `LoginAsync`, `NavigateAsync`, or similar helpers

---

## 20. Parallel Execution

xUnit parallelizes test collections. By default, each test class is its own collection, and tests in the same class do not execute concurrently with one another.

### 20.1 Rules

* Keep test classes feature-focused
* Avoid one giant regression class
* Use separate classes to expose useful parallelism
* Configure a conservative maximum thread count
* Never share `IPage`
* Never share `IBrowserContext`
* Never share mutable per-test state
* Acquire users before performing stateful actions
* Use unique test-data identifiers
* Serialize only tests that genuinely require it

### 20.2 Example Test Classes

```text
LoginSmokeTests
AdminPermissionTests
ManagerApprovalTests
ReadOnlyAccessTests
RequestCreationRegressionTests
```

### 20.3 Multi-User Scenarios

**Capability status — precise, not aspirational:**

```text
Multi-user lease acquisition is architecturally specified (AcquireSetAsync, slot constraints).
The baseline lifecycle is actor-first with ONE actor session (Section 9.2).
Multi-user UI scenarios are NOT part of the first working baseline —
they are enabled by adding named slots to the same lifecycle, pending spike validation.
```

Because the Phase 1 baseline already acquires the actor *before* creating its context and composing its session graph, multi-user support is an additive change — more named slots, more `ActorSession`s — not a lifecycle redesign. What remains spike-gated is validating multiple concurrent framework-owned contexts per test under the chosen base class. Until then, the framework must not present multi-user UI tests as a supported capability.

When the composition is designed, tests involving multiple actors:

```text
Requester creates request
    ↓
Approver approves request
    ↓
Requester verifies status
```

must:

1. Acquire the complete user lease set atomically (with distinct-user constraints where required)
2. Create one browser context per actor
3. Keep each actor’s Page and context isolated
4. Dispose only additional contexts created by that scenario
5. Release the complete lease set
6. Separate artifacts per actor (`{test-id}/requester/…`, `{test-id}/approver/…`)

---

## 21. Setup, Data Ownership, and Cleanup

Every test-created resource receives a unique ownership identifier.

```text
e2e-{run-id}-{test-id}
```

### 21.1 Cleanup Coordinator

The coordinator registers cleanup *operations*, not bare resources, so each registration knows how to undo itself:

```csharp
public interface ITestCleanupCoordinator
{
    void Register(
        string name,
        Func<CancellationToken, ValueTask> cleanup,
        CleanupOptions options);

    ValueTask ExecuteAsync(
        CancellationToken teardownToken);
}
```

Ordering and timeout rules:

* Cleanup is registered immediately after each resource is created
* Default execution order is LIFO (last created, first cleaned)
* Cleanup receives a dedicated teardown cancellation token, never the already-cancelled test-timeout token
* Every cleanup step has its own timeout via `CleanupOptions`
* A step is retried only if `CleanupOptions` explicitly declares it idempotent/retry-safe
* **All cleanup runs before actor release.** There is one cleanup stage, before release; a post-release cleanup phase is not part of this design (capacity optimization is not worth account/data races). Introducing one later requires an explicit ADR defining pre-release and post-release phases
* A cleanup failure may mark the actor for quarantine (Section 15)

Timeouts must not leak running operations. Racing a cleanup task against a delay (`Task.WhenAny`) does not stop the underlying operation:

* Each cleanup step gets its **own linked token** (step timeout linked to the teardown token) — a step timeout never cancels the shared teardown token for other steps
* The framework records whether the operation actually stopped or merely stopped being awaited
* A non-cooperative step that hit its timeout is treated as **potentially still running**: the framework drains outstanding operations for a bounded period before disposing anything they may use
* The DI scope is not disposed and the actor is not released while an uncooperative cleanup operation may still be using them; if work survives the drain window, contexts are closed, the actor and touched resources are quarantined (`CleanupFailed`), and the worker process is recycled

The coordinator tracks each cleanup step independently through an explicit state model:

```text
Pending
Running
Completed
Failed
```

State rules:

* Fallback disposal resumes incomplete work; it never reruns successfully completed steps
* A failed step is not automatically retried unless that step is explicitly designed as retry-safe
* After a step fails, the coordinator continues with the remaining pending steps while retaining the original failure
* All step failures are collected and reported together (Section 9.2 aggregation rule)

### 21.2 Cleanup Rules

* Prefer API setup when UI setup is not under test
* Prefer API cleanup when cleanup UI is not under test
* Cleanup runs after success and failure
* Cleanup failures are reported separately
* Cleanup failures must not replace the original test exception
* Shared baseline data must never be deleted
* Test-created data should contain ownership metadata
* A scheduled process removes abandoned stale data

Janitor safety rules:

* Consults run/lease heartbeats before deleting — age alone never justifies deletion of anything a live run may own
* Uses an age threshold greater than the maximum test duration
* Enforces environment and ownership prefixes; runs with least privilege
* Never deletes baseline data
* Cleanup semantics are explicitly **at-least-once and idempotent**, never implicitly exactly-once

---

## 22. Logging and Diagnostics

The framework uses `Microsoft.Extensions.Logging`.

Serilog is optional and may be configured as a provider later.

### 22.1 Per-Test Logging Context

Every log entry includes:

* Run ID
* Test ID
* Test name
* Environment
* Execution profile
* Browser
* User key
* Role
* Current flow

### 22.2 Identity Model and Artifact Structure

Identity dimensions are separated — one ID cannot serve all purposes:

* **Case ID** — stable logical test case (survives reruns and code moves)
* **Execution ID** — unique per execution of that case
* **CI run ID / job ID / shard ID / job attempt** — pipeline coordinates
* **Actor ID** — per-actor artifact separation in multi-user scenarios

Pipeline **reruns can overwrite artifacts even with test-level retries disabled**, so the attempt/execution dimension exists from day one:

```text
TestResults/
└── {run-id}/
    └── {job}-{shard}-attempt{n}/
        └── {environment}/
            └── {profile}/
                └── {case-id}/{execution-id}/
                    ├── screenshot.png
                    ├── trace.zip
                    ├── console.log
                    ├── network.log
                    └── metadata.json
```

Names are sanitized, path lengths capped, and theory arguments hashed rather than embedded raw in paths.

### 22.3 Attachments

xUnit v3 `TestContext` attachments accept strings or byte arrays — not file paths — so attaching a trace ZIP means loading it into memory and bloating TRX. Attachments therefore stay **small and bounded**: metadata, classifications, and *references* to artifacts. Trace and video files are published as pipeline artifacts, never inlined into xUnit attachments.

### 22.4 Diagnostics Data Policy

"Redact sensitive data" is not achievable by a redaction pass alone, because Playwright traces carry DOM snapshots and network data, screenshots can show names/emails/financial values/tokens, and network logs can carry authorization headers, cookies, query-string tokens, and request/response bodies. The policy therefore controls what is captured, not only what is scrubbed afterward:

1. Network response/request bodies are disabled by default
2. Header and network logging uses an **allowlist of safe fields**, not a denylist of "known sensitive" ones — unknown fields are dropped by default
3. Query-string parameters are redacted by name
4. Console and network buffers are bounded
5. Sensitive tests or environments can disable tracing and screenshots
6. Trace retention differs by environment; UAT diagnostics require explicit approval
7. Multi-user artifacts are separated by actor
8. Raw diagnostics are written to a restricted private directory; only policy-approved files are copied into the publish directory — a final redaction pass cannot reliably sanitize a trace ZIP or screenshot, so capture prevention, access control, encryption, and retention are the controls
9. Storage-state files are never written beneath `TestResults`

Never record: passwords, tokens, authorization headers, sensitive cookies, certificates, secret values, or personally identifiable information unless specifically approved.

### 22.5 Result Classification

The document distinguishes product failure, infrastructure failure, capacity failure, cleanup failure, and framework-initialization failure. xUnit and TRX do not understand this taxonomy natively, so classification is carried as **metadata, not by wrapping the assertion exception** (the original exception is preserved per Section 9.2):

* Framework-specific exception types for infrastructure/capacity/framework-init failures
* A `metadata.json` per test recording the classification
* Azure DevOps post-processing / pipeline summary that rolls the classifications up

Runtime classifications live in **structured metadata, not traits** — traits are discovery-time metadata and cannot represent an outcome decided at runtime.

The taxonomy also includes: **automation/test-code defect**, **invalid test data**, **cancellation**, and **inconclusive/unknown**. Classification derives primarily from the lifecycle stage that failed and the explicit exception source — a Playwright timeout is not automatically a product defect *or* an infrastructure defect; if the source is ambiguous, the classification is `inconclusive`, never a guess.

This lets a run distinguish "the product is broken" from "the environment or capacity failed" without corrupting the test-body exception.

---

## 23. Retry and Flaky-Test Policy

### 23.1 Baseline

There are no automatic retries.

### 23.2 Flaky Candidates

Mark suspected unstable tests:

```csharp
[Trait("Stability", "FlakyCandidate")]
```

Flaky candidates:

* Run in a separate pipeline stage
* Do not block the main smoke suite unless policy says otherwise
* Retain their first failure
* Are tracked with an owner and issue
* Must not remain quarantined indefinitely

### 23.3 Future Retry Decision

Before adding retries, a new ADR must select one mechanism:

* Custom xUnit test implementation
* MTP extension
* Azure DevOps post-processing and targeted rerun

The ADR must define:

* How failing tests are identified
* How exact test cases are filtered
* How TRX results are merged
* How artifacts are separated
* Whether a new user is leased
* Whether setup and cleanup rerun
* Maximum attempts
* Failure reporting semantics

---

## 24. Environment Health Checks

Before UI tests run, validate:

* Application URL is reachable
* Required API is reachable
* Authentication provider is available
* Required integrations are available
* Required feature flags are enabled
* Required test-user capacity exists
* Required test data is present
* Secret provider is accessible

Health-check failures are infrastructure failures, not product test failures.

A failed health check stops the affected test stage.

Scope limitation: the preflight validates *configured* capacity and obvious shortages. It cannot guarantee users will still be available when tests start, especially across parallel agents. Lease acquisition remains authoritative at execution time, the acquisition timeout remains an infrastructure-capacity outcome, and the preflight never replaces runtime lease handling.

---

## 25. Azure DevOps Pipeline

### 25.1 MTP Reporting

The pipeline uses Microsoft Testing Platform report arguments rather than VSTest’s legacy `--logger "trx"` syntax.

MTP uses TRX report extensions and `--report-trx`; the available command-line behavior depends on the selected test runner.

`--report-trx` requires the `Microsoft.Testing.Extensions.TrxReport` package, referenced through Central Package Management. Final argument placement must be validated against the pinned SDK with `dotnet test -?` (Phase 1 spike).

```yaml
trigger:
  branches:
    include:
      - main

parameters:
  - name: testEnvironment
    displayName: Test Environment
    type: string
    default: qa
    values:
      - dev
      - qa
      - uat

  - name: executionProfile
    displayName: Execution Profile
    type: string
    default: chromium-headless
    values:
      - chromium-headless
      - firefox-headless
      - webkit-headless

  - name: category
    displayName: Test Category
    type: string
    default: Smoke
    values:
      - Smoke
      - Regression
      - CriticalPath

pool:
  vmImage: ubuntu-latest

variables:
  buildConfiguration: Release
  resultsDirectory: $(Build.SourcesDirectory)/TestResults

steps:
  - task: UseDotNet@2
    displayName: Install .NET SDK
    inputs:
      packageType: sdk
      useGlobalJson: true

  - script: dotnet --info
    displayName: Print .NET information

  - script: dotnet restore
    displayName: Restore

  - script: >
      dotnet build
      --configuration $(buildConfiguration)
      --no-restore
    displayName: Build

  - pwsh: |
      $browser = ('${{ parameters.executionProfile }}' -split '-')[0]
      Write-Host "##vso[task.setvariable variable=playwrightBrowser]$browser"
    displayName: Resolve Playwright browser from profile

  - script: >
      pwsh
      tests/UiAutomation.Tests/bin/$(buildConfiguration)/net10.0/playwright.ps1
      install $(playwrightBrowser)
      --with-deps
    displayName: Install Playwright browser (profile-scoped)

  # Native .NET 10 MTP mode (selected via global.json): projects are passed
  # with --project, and MTP options are passed directly without a `--`
  # separator. Validate the final form against the pinned SDK via
  # `dotnet test -?` (Phase 1 spike) before treating this YAML as canonical.
  - script: >
      dotnet test
      --project tests/UiAutomation.Framework.Tests/UiAutomation.Framework.Tests.csproj
      --configuration $(buildConfiguration)
      --no-build
      --results-directory "$(resultsDirectory)/framework"
      --filter-not-trait "RequiresApplication=true"
      --report-trx
      --minimum-expected-tests 1
    displayName: Run framework tests (unit + mechanics)
    env:
      # Mechanics tests launch a real browser; they must use the same
      # profile-selected browser that was installed above.
      TEST_PROFILE: ${{ parameters.executionProfile }}

  # Preflight is a dedicated console project with a real entry point —
  # `dotnet run -- health-check` against a test project has no dispatcher
  # and would be rejected by the MTP runner.
  - script: >
      dotnet run
      --project tools/UiAutomation.Preflight/UiAutomation.Preflight.csproj
      --configuration $(buildConfiguration)
      --no-build
    displayName: Environment health check (preflight)
    env:
      TEST_ENV: ${{ parameters.testEnvironment }}
      TEST_PROFILE: ${{ parameters.executionProfile }}

  # Application-dependent framework integration tests run BEFORE the UI
  # suite: if the framework's own integration layer is broken, UI results
  # are noise.
  - script: >
      dotnet test
      --project tests/UiAutomation.Framework.Tests/UiAutomation.Framework.Tests.csproj
      --configuration $(buildConfiguration)
      --no-build
      --results-directory "$(resultsDirectory)/framework-integration"
      --filter-trait "RequiresApplication=true"
      --report-trx
    displayName: Run application-dependent framework integration tests
    env:
      TEST_ENV: ${{ parameters.testEnvironment }}
      TEST_PROFILE: ${{ parameters.executionProfile }}

  # Blocking suites exclude flaky candidates; those run in a separately
  # reported stage with an owner, issue link, and quarantine budget.
  - script: >
      dotnet test
      --project tests/UiAutomation.Tests/UiAutomation.Tests.csproj
      --configuration $(buildConfiguration)
      --no-build
      --results-directory "$(resultsDirectory)/ui"
      --filter-trait "Category=${{ parameters.category }}"
      --filter-not-trait "Stability=FlakyCandidate"
      --report-trx
      --minimum-expected-tests 1
    displayName: Run UI tests
    condition: succeededOrFailed()
    env:
      TEST_ENV: ${{ parameters.testEnvironment }}
      TEST_PROFILE: ${{ parameters.executionProfile }}

  - task: PublishTestResults@2
    displayName: Publish test results
    condition: always()
    inputs:
      testResultsFormat: VSTest
      testResultsFiles: "$(resultsDirectory)/**/*.trx"
      failTaskOnFailedTests: true

  - task: PublishPipelineArtifact@1
    displayName: Publish diagnostics
    condition: always()
    inputs:
      targetPath: "$(resultsDirectory)"
      artifact: ui-test-results
```

xUnit v3’s MTP runner supports trait filtering through `--filter-trait` and `--filter-not-trait`. `--minimum-expected-tests` guards required suites against a silently-empty filter; only intentionally optional stages may accept a zero-test outcome.

**This YAML is illustrative, not canonical, until the Phase 1 spike produces a working command** — in particular, how `TEST_PROFILE` translates into Playwright's browser selection for each test process is spike-determined (the profile-scoped install above only guarantees the browser is *present*). Independent suites belong in separate jobs as the pipeline grows; within one job, later suites use explicit conditions rather than default success-only chaining.

`local` is not available in a hosted pipeline unless the pipeline also starts the local application stack.

Additional pipeline requirements:

* **Reproducibility:** `ubuntu-latest` is mutable — pin an agent image or container. Use NuGet lock files with locked restore (direct version pins do not lock transitives). Record effective SDK, package, browser, and OS versions in run metadata.
* **UAT governance:** UAT execution binds to an Azure DevOps environment with approvals/checks, workload identity (OIDC) instead of secrets where possible, least-privilege Key Vault access, capability gating for destructive tests, restricted artifact access, and shorter sensitive-data retention.
* **Scale (Phase 3+):** deterministic case sharding across agents; a run-level circuit breaker that halts the run on widespread infrastructure failure so one outage does not produce hundreds of misleading product failures; operational metrics for lease wait time, pool headroom, quarantined users, cleanup failures, context count, artifact size, and infrastructure-failure rate.

---

## 26. Framework Testing Strategy

### 26.1 Unit Tests

Unit-test:

* Configuration parsing
* Configuration validation
* Environment selection
* Execution-profile selection
* User filtering
* Permission matching
* Lease capacity logic
* Atomic lease-set behavior
* Lease expiry
* Credential references
* Test-data mapping
* Schema-version validation
* Artifact naming
* Redaction
* Cleanup ordering
* Health-check decisions

Cleanup-coordinator state coverage (mandatory):

* Resume after partial cleanup
* Completed steps are not executed twice
* Remaining steps run after an earlier step fails
* Multiple failures are collected
* Repeated disposal is safe
* Lease release still runs after cleanup failure
* DI scope disposal still runs after lease-release failure

Execution-wrapper permutation coverage (mandatory — **the architecture is not production-safe until these pass against the actual wrapper**; a written review is not the confidence gate, this suite is):

*Failure preservation*

* Body fails, diagnostics succeed → original exception is primary
* Body fails, diagnostics fail → original exception is primary
* Body fails, cleanup fails → original exception is primary
* Body fails, actor release fails → original exception is primary
* Body fails, DI disposal fails → original exception is primary
* Body fails, every teardown stage fails → original exception is primary, all stage failures recorded

*Passing body with teardown failure*

* Body passes, cleanup fails → test fails as infrastructure/teardown failure
* Body passes, actor release fails → test fails as infrastructure/teardown failure
* Body passes, scope disposal fails → test fails as infrastructure/teardown failure (never a silent pass)

*Ordering*

* Diagnostics run before application cleanup
* Auth-dependent cleanup runs before actor release
* Context disposal occurs at the selected lifecycle point
* DI scope disposal occurs after scoped services finish
* Concurrency permit is always released

*Idempotency and concurrency*

* Double execution rejected deterministically
* Concurrent execution rejected deterministically
* Repeated disposal safe
* Disposal racing execution handled deterministically
* Completed cleanup steps do not repeat

*Reporting failures*

* `AddWarning` failure does not block cleanup
* Attachment failure does not block cleanup
* Metadata-write failure does not replace the original test exception

### 26.2 Integration Tests

Framework integration tests are split into two categories with different environment requirements.

**Mechanics tests** need a real browser but no application environment:

* Trace creation
* Screenshot capture
* Browser console capture
* Additional browser contexts
* Scenario composition (against local test pages)

**Application-dependent tests** need `TEST_ENV`, `TEST_PROFILE`, and a reachable application:

* Page Objects
* Component Objects
* Flows
* Storage-state creation

Application-dependent tests are marked:

```csharp
[Trait("RequiresApplication", "true")]
```

and run in the UI test stage of the pipeline, not in the framework test stage.

Do not build extensive mock trees for `IPage` and `ILocator`.

### 26.3 Static Rule Enforcement

Enforcement is sequenced. The primary defense is **structural**, not analyzer control-flow analysis: making misuse hard by construction is more reliable than proving it absent.

Phase 1 (structural — implement first):

* `internal` framework services so tests cannot reach lifecycle internals
* Project dependency rules (tests reference the Application project, not framework internals)
* Marker interfaces and attributes for approved test shapes
* Architecture tests (assembly-dependency assertions)
* Simple, high-precision semantic analyzers: no selectors in test projects, no direct `IUserLeaseManager` access from tests, no application-level `Page` calls from test methods, no `ClickAsync(string)`-style wrappers
* `Task.Delay` rule, scoped precisely: banned in UI test and application-interaction code; framework timing (lease expiry, heartbeats, cache TTL, cleanup deadlines, deliberate backoff) uses an injectable `TimeProvider` so it stays deterministic in unit tests

**Honesty about the boundary.** "Structurally impossible" is an overstatement and the document does not claim it:

* A test deriving from `UiTestBase` still inherits the Playwright base's `Page`, `Context`, and context-creation APIs — that is what the "no application-level `Page` calls from tests" analyzer exists for
* Actor and scenario handles can escape the delegate into test fields — which is why handles are invalidated when the execution completes (Section 9.4)
* A test can call the wrapper zero or multiple times — which is why the wrapper is one-shot with a post-test guard (Section 9.2)

Boundary enforcement is honestly described as **runtime- and analyzer-enforced**, layered on a structure that makes violations awkward rather than impossible.

Deferred (add only after the execution API stabilizes): analyzers that attempt to prove arbitrary Flow or actor calls occur only inside the `RunScenarioAsync` delegate. Correct control-flow analysis across local functions, helper methods, extension methods, nested lambdas, inheritance, and generated tests is hard and fragile, so it is not a Phase 1 rule. The adopted centralized, non-exposed `RunScenarioAsync` API (Section 9.2) removes most of the need for it: tests can only enter through the wrapper, and the scope/factory types are `internal`, so boundary violations are structurally impossible rather than analyzer-detected.

AI-generation validation (Section 27) may use simpler structural checks; compiler analyzers prefer symbol-based checks and precision over coverage.

---

## 27. AI-Assisted Test Generation

Order of generation: AI generates **domain cases first, test code second**. New selectors and new Flows remain human-owned. JSON, Excel, DOM text, and issue descriptions supplied to prompts are treated as untrusted input; secrets are never supplied to a prompt. Generated changes must be unable to disable analyzers, weaken assertions or timeouts, introduce `Force` options, catch-and-ignore failures, or modify framework policy — and are compiled and tested in an isolated environment before review.

AI prompts must include:

### Goal

The business behavior being validated.

### Scenario Input

A validated domain scenario.

### User Requirements

* Role
* Permissions
* Metadata
* Session-capacity requirements
* Number of actors

### Available Framework Components

* Typed scenario
* Existing Flows
* Existing Page Objects
* Existing Component Objects
* Existing cleanup APIs

### Required Rules

Generated code must:

* Use xUnit v3
* Derive from `UiTestBase`
* Run the entire test body through `RunScenarioAsync<TScenario>()` — the only execution entry point
* Declare actors through the `ActorPlan`/lease-request parameter of `RunScenarioAsync`; never acquire actors inside the body
* Acquire multi-actor leases through `CreateManyAsync` (atomic)
* Authenticate via `Auth.LoginAsAsync(actor)`; never touch plaintext credentials
* Use existing Flows
* Use named Page Object locators for UI assertions
* Use Playwright `Expect` for browser state
* Use unique test-data identifiers

### Forbidden Rules

Generated code must not:

* Put selectors in tests
* Use `Page.GetByTestId` directly in tests
* Use direct application-level `IPage` interactions in tests
* Hardcode credentials
* Hardcode URLs
* Use `Task.Delay`
* Parse Excel directly
* Create browser wrapper methods such as `ClickAsync(string selector)`
* Create generic command interpreters
* Select users without leases
* Call `IUserLeaseManager` directly instead of using actors
* Dispose actors or leases directly
* Acquire multiple exclusive users one at a time
* Perform application actions, user acquisition, or test-data creation before `RunScenarioAsync`
* Perform application assertions after `RunScenarioAsync` returns
* Create, dispose, or `await using` any scenario scope or lifecycle object (none is exposed; the wrapper owns the lifecycle)
* Write multi-user UI tests (not supported in the baseline; see Section 20.3)
* Hide the final assertion inside a Flow
* Dispose the default Playwright Page or BrowserContext

---

## 28. Consolidated Framework Rules

This is the **only normative rule list**. Every other statement of a rule elsewhere in this document is a contextual restatement that defers to this section; on any conflict, this section wins. Where a rule is analyzer- or architecture-test-enforced, the enforcement policy is the source of truth and this list mirrors it.

1. Tests contain scenario orchestration and scenario-level assertions.
2. Tests do not contain selectors.
3. Tests do not perform application-level actions through `IPage`.
4. Page and Component Objects may use Playwright directly.
5. Flows combine Pages and Components into business behavior.
6. Flows do not contain selectors.
7. Flows do not allocate users or resolve secrets.
8. Playwright’s xUnit v3 integration owns the default Page and BrowserContext.
9. Each test receives an isolated browser context.
10. Additional contexts have one explicit owner.
11. Stateful users are leased; the framework is the sole lifecycle owner of actor leases, and `TestActor` is a non-disposable handle obtained only by declaring a slot in the `ActorPlan` — actors are acquired before scenario composition, never inside the body.
12. Multi-user scenarios acquire leases atomically; multi-user UI composition is not part of the first working baseline (Section 20.3).
13. Environment and execution configuration are separate.
14. Configuration is validated before test execution.
15. JSON is the primary external test-data format.
16. Excel is an input adapter, not a programming language.
17. UI assertions use Playwright `Expect`.
18. Automatic retries are disabled in the baseline.
19. Cleanup failures do not hide original failures.
20. Logs and artifacts are correlated by run and test identity.
21. Secrets and session data are redacted.
22. AI-generated tests follow the same rules as human-written tests.
23. `RunScenarioAsync<TScenario>()` is the only public execution entry point; tests never see or manage a lifecycle object, and diagnostics, cleanup, lease release, disposal, and final failure selection are framework-ordered.
24. Secret-bearing types must not expose secrets through generated `ToString()`.
25. Capability-gated tests are skipped with an explicit reason, never silently passed.
26. A teardown failure after a passing body always fails the test; warnings are never the only record.
27. The execution wrapper's failure and teardown guarantees are proven by the mandatory permutation test suite (Section 26.1), not by review.

---

## 29. Implementation Roadmap

### Phase 1: Vertical Slice

**Compatibility spike (first task)**

Validate against the pinned SDK and package versions before building anything on top:

1. Playwright xUnit v3 lifecycle and parallel behavior: browser/Playwright instance reuse across parallel test classes, and a `UiTestBase` deriving from the Playwright xUnit v3 test base compiling and running with assembly-fixture constructor injection
2. The browser/profile configuration mechanism actually honored by `Microsoft.Playwright.Xunit.v3` under MTP (runner options vs. environment variables vs. `ContextOptions()` only)
3. `TestContext` `AddAttachment`, `AddWarning`, dynamic-skip behavior, and lifecycle timing (when warnings/attachments are still accepted relative to test completion and disposal)
4. MTP trait filtering and TRX generation, end to end in Azure DevOps: final `dotnet test` argument placement (`--report-trx`, `--filter-trait`, `--filter-not-trait`) verified via `dotnet test -?` and a published pipeline run

The spike results are authoritative: any executable-spike outcome overrides runner- or package-specific assumptions currently written in this document, and the document is updated to match.

**Executable gates — implementation does not proceed past Phase 1 sign-off until all eight pass:**

1. `BrowserTest` compatibility spike against the exact pinned SDK and packages
2. A compiling vertical slice with actor-first context creation
3. The lifecycle permutation and fault-injection suite (Section 26.1), including factory failure after *every* acquired resource (permit, DI scope, lease, context)
4. Race tests for execution/teardown interleavings and lease-loss propagation
5. Proof that DI-scope disposal does not close any Playwright-owned resource
6. An Azure DevOps pipeline run proving native MTP syntax, trait filtering, TRX publication, preflight, and browser selection end to end
7. A security test proving unapproved traces, screenshots, storage state, and secrets never enter the published artifact directory
8. A two-process lease test covering atomic acquisition, expiry, fencing, ambiguous client timeout, quarantine, and recovery

Written review rounds are complete; these gates are the only remaining acceptance mechanism.

Implement:

* Solution structure
* `global.json`
* MTP selection
* Central package management
* Analyzers and coding standards
* Configuration loading
* Environment/profile separation
* `config/execution-profiles.json`
* Official Playwright xUnit v3 integration via `UiTestBase`
* `RunScenarioAsync<TScenario>()` public execution API (internals provisional until the spike)
* Execution-wrapper permutation test suite (Section 26.1) — the confidence gate for the lifecycle guarantees
* `LoginPage`
* `DashboardPage`
* `AuthFlow`
* Typed `LoginScenario`
* `ActorSession` abstraction with actor-before-context ordering, single actor (Section 9.2 — this ordering is the baseline, not a later extension)
* Environment-variable secret provider
* Single-user in-memory lease (single application-hitting test module; module parallelism constrained — Section 15.4)
* Minimal cleanup coordinator (registration, LIFO execution, state tracking) — required because the wrapper permutation suite exercises cleanup behavior
* Diagnostics capture policy: private raw-artifact directory, policy-approved publish copy, network bodies off — traces and screenshots are not captured before this exists
* Minimal preflight tool (`UiAutomation.Preflight`): base URL and secret-provider reachability
* One login smoke test
* Screenshot and trace capture (under the capture policy)
* Azure DevOps smoke pipeline (chromium profile only; browser matrix is Phase 3)

**Acceptance criteria**

A QA smoke test can:

1. Load validated configuration
2. Start through `UiTestBase` and the official Playwright xUnit v3 integration
3. Receive an isolated browser context
4. Run the test body inside `RunScenarioAsync<TScenario>()` with no lifecycle object exposed to the test
5. Create an Admin actor (lease + credential resolution)
6. Log in through `AuthFlow`
7. Assert `DashboardPage.Root`
8. Capture failure artifacts while the lease is still held
9. Release the user through the wrapper
10. Publish TRX and diagnostics

And the execution-wrapper permutation suite (Section 26.1) passes against the actual wrapper implementation.

### Phase 2: Reliability

Implement:

* Full cleanup coordinator (per-step timeouts, linked tokens, drain, retry-safe steps)
* API setup and cleanup
* Full environment health checks (auth provider, integrations, capabilities, user capacity)
* Azure Key Vault secret provider with OIDC/workload identity
* Console and network diagnostics (allowlist-based)
* Full redaction and publish-gate policy validation
* Test-data ownership identifiers
* JSON case loading with per-case xUnit discovery (Section 18.3.1)
* Case-schema validation

**Acceptance criteria**

Failed setup, tests, and cleanup are clearly differentiated, and no sensitive data is published.

### Phase 3: Parallel Scale

Implement:

* User session capacity
* Atomic multi-user leasing
* Distributed lease store
* Capacity-aware parallel limits
* Multi-user browser contexts
* Browser execution matrix

**Acceptance criteria**

Two Azure DevOps agents can run safely without assigning conflicting users or deadlocking multi-user scenarios.

### Phase 4: Data and Authentication Optimization

Implement:

* ClosedXML adapter
* Excel review/export process
* Authenticated storage state
* Storage-state locking
* Storage-state expiry
* Role- and permission-based scenario catalog

**Acceptance criteria**

Storage state cannot be corrupted by parallel writers, and Excel changes remain reviewable.

### Phase 5: AI Assistance

Implement:

* Structured prompt templates
* Architecture-rule validation
* Forbidden-code analysis
* Generated-test build validation
* Human-review workflow

**Acceptance criteria**

Generated tests compile, use existing scenario boundaries, contain no selectors, and do not bypass leasing or credential resolution.

---

## 30. Final Notes

The framework’s first goal is not to create every possible abstraction.

Its first goal is to prove one complete, reliable vertical slice:

```text
Validated configuration
    ↓
Official Playwright xUnit lifecycle
    ↓
Isolated browser context
    ↓
Typed scenario scope
    ↓
Leased user
    ↓
Resolved credentials
    ↓
Page Object
    ↓
Business Flow
    ↓
Visible test assertion
    ↓
Diagnostics
    ↓
Cleanup
    ↓
Published CI result
```

Only after that slice works locally and in Azure DevOps should the framework add distributed leasing, Excel ingestion, authentication-state caching, browser matrices, and AI-generated tests.

---

## 31. Distribution: Atya.Templates.UiAutomation

This architecture is distributed as a `dotnet new` solution template in the Atya fleet, not as runtime packages.

### 31.1 Identity

| Purpose | Name |
| --- | --- |
| GitHub repository | `AtyaLibraries/Atya.Templates.UiAutomation` |
| NuGet package / template identity | `Atya.Templates.UiAutomation` |
| Display name | Atya UI Automation |
| `dotnet new` short name | `atya-ui-automation` |
| Local fleet folder | `Templates/atya-ui-automation` |

The template is named after its purpose, not its implementation (`Playwright` is an implementation choice, not the name). `UiAutomation`, not `UIAutomation`. The generated solution uses the consumer's root namespace (template parameter, e.g. `Contoso.Shop.UiAutomation`); `Atya` appears only on the template package itself.

### 31.2 Governance Prerequisite

`Templates` is a reserved infrastructure area and the constitution requires catalog approval before implementation. Before any code, add the catalog row:

```text
| Atya.Templates.UiAutomation | Templates (infra) | BUILD | Infra | .NET 10 Playwright UI-automation solution template. |
```

and decide the repository property (`atya-managed=true` if it uses the reusable managed CI, else `atya-infra=true`; mutually exclusive).

### 31.3 Packaging Rules

* The template package has **no runtime dependencies**: `<PackageType>Template</PackageType>` + `<SuppressDependenciesWhenPacking>true</SuppressDependenciesWhenPacking>`
* Generated projects use plain `Microsoft.NET.Sdk`, **not** `Atya.Build.Sdk`: the current SDK injects `Microsoft.NET.Test.Sdk`, `xunit.runner.visualstudio`, coverlet, FluentAssertions 7.2, NSubstitute, and `Atya.Governance.Testing` into every test project — conflicting with this document's verified MTP/xUnit-v3/Playwright matrix and its FluentAssertions licensing decision (Section 2.1). If fleet consistency later requires the SDK, that happens via a dedicated SDK profile (e.g. `<AtyaTestStack>UiAutomation</AtyaTestStack>`) that injects only the verified stack — not via per-repo disable flags
* The generated solution declares its exact test stack explicitly, pinned via Central Package Management (Section 7)

### 31.4 Default Atya Dependencies in Generated Code

Deliberately minimal:

* `Atya.Governance.CodeQuality` — default, `PrivateAssets="all"` (satisfies the Phase 1 analyzers requirement)
* `Atya.Governance.Testing` — opt-in, only where its helpers are actually used
* `Atya.Diagnostics.Logging` — opt-in, only after the redaction policy (Section 22.4) is operational
* Everything else (`Foundation.Time`, `Foundation.Guards`, `Foundation.Serialization`, `Errors.*`, `Diagnostics.Tracing`/`Observation`/OpenTelemetry) — **not** default: the framework uses BCL `TimeProvider`, per-boundary serializer policies, and its own precise exception taxonomy (Section 22.5); `Atya.Diagnostics.Tracing` is `ActivitySource` tracing and must not be confused with Playwright `trace.zip`

### 31.5 No Runtime Framework Package Yet

No `Atya.UiAutomation.Framework` NuGet package is published now. The template generates the framework as source into each repository. A reusable runtime package is extracted only after (a) the eight executable gates have passed and (b) at least two real consumers demonstrate stable common code. NuGet versions are immutable in the fleet model; an unproven lifecycle API is not published.
