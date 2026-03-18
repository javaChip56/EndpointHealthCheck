# ApiHealthDashboard

Portable ASP.NET Core Razor Pages dashboard for monitoring API health check endpoints.

This project is being built to:
- poll configured health endpoints directly over HTTP
- parse JSON health responses without Xabaril UI libraries
- render a local-only AdminLTE dashboard
- load endpoint definitions from YAML
- keep runtime state in memory
- stay portable for internal and restricted environments

## Current Status

Implemented so far:
- Phase 1: solution bootstrap
- Phase 2: local AdminLTE layout and placeholder pages
- Phase 3: YAML configuration models, loader, validation, and tests
- Phase 4: in-memory runtime state store and tests
- Phase 5: HTTP endpoint poller and tests
- Phase 6: health response parser and tests
- Phase 7: background polling scheduler and tests
- Phase 8: manual refresh actions and page-model tests
- Phase 9: dashboard summary view and tests
- Phase 10: endpoint details diagnostics view and tests
- Phase 11: error handling and structured logging improvements
- Phase 12: automated test expansion for invalid and edge-case coverage
- Phase 13: publish and deployment validation
- Phase 14: GitHub Actions CI/CD and Dependabot automation

Not implemented yet:
- CI/CD workflows

## Solution Layout

```text
.
|-- ApiHealthDashboard.sln
|-- README.md
|-- src/
|   `-- ApiHealthDashboard/
|       |-- Configuration/
|       |-- Domain/
|       |-- Parsing/
|       |-- Scheduling/
|       |-- Services/
|       |-- State/
|       |-- Pages/
|       |-- wwwroot/
|       `-- endpoints.yaml
`-- tests/
    `-- ApiHealthDashboard.Tests/
```

## Tech Stack

- .NET 8
- ASP.NET Core Razor Pages
- AdminLTE v3.2.0 bundled locally
- YamlDotNet
- xUnit

## Current Features

### Local AdminLTE Shell

The app uses locally bundled AdminLTE assets under [`src/ApiHealthDashboard/wwwroot/adminlte`](src/ApiHealthDashboard/wwwroot/adminlte).

Current UI pages:
- dashboard summary page: [`src/ApiHealthDashboard/Pages/Index.cshtml`](src/ApiHealthDashboard/Pages/Index.cshtml)
- endpoint details page: [`src/ApiHealthDashboard/Pages/Endpoints/Details.cshtml`](src/ApiHealthDashboard/Pages/Endpoints/Details.cshtml)

### YAML Configuration

Dashboard configuration is loaded at startup from [`src/ApiHealthDashboard/endpoints.yaml`](src/ApiHealthDashboard/endpoints.yaml).

Current configuration support:
- `dashboard.refreshUiSeconds`
- `dashboard.requestTimeoutSecondsDefault`
- `dashboard.showRawPayload`
- endpoint `id`, `name`, `url`, `enabled`, `frequencySeconds`, `timeoutSeconds`
- endpoint `headers`, `includeChecks`, `excludeChecks`
- `${ENV_VAR}` substitution in YAML values
- `endpoints.yaml` is copied to both build and publish output by default

Validation currently checks:
- required endpoint id, name, and url
- unique endpoint ids
- absolute HTTP/HTTPS URLs only
- positive refresh, frequency, and timeout values
- non-empty header names

### Runtime State Store

The app now includes an in-memory endpoint state store for current runtime status.

Current runtime models:
- [`src/ApiHealthDashboard/Domain/EndpointState.cs`](src/ApiHealthDashboard/Domain/EndpointState.cs)
- [`src/ApiHealthDashboard/Domain/HealthSnapshot.cs`](src/ApiHealthDashboard/Domain/HealthSnapshot.cs)
- [`src/ApiHealthDashboard/Domain/HealthNode.cs`](src/ApiHealthDashboard/Domain/HealthNode.cs)

State store components:
- [`src/ApiHealthDashboard/State/IEndpointStateStore.cs`](src/ApiHealthDashboard/State/IEndpointStateStore.cs)
- [`src/ApiHealthDashboard/State/InMemoryEndpointStateStore.cs`](src/ApiHealthDashboard/State/InMemoryEndpointStateStore.cs)

Current behavior:
- initializes one runtime state entry per configured endpoint at startup
- stores endpoint state in memory only
- supports get-all, get-one, upsert, and reinitialize operations
- returns deep copies so callers cannot mutate internal store state accidentally
- uses thread-safe locking for concurrent access

### HTTP Poller

The app now includes an `HttpClientFactory`-based endpoint poller for individual health endpoint requests.

Poller components:
- [`src/ApiHealthDashboard/Services/IEndpointPoller.cs`](src/ApiHealthDashboard/Services/IEndpointPoller.cs)
- [`src/ApiHealthDashboard/Services/EndpointPoller.cs`](src/ApiHealthDashboard/Services/EndpointPoller.cs)
- [`src/ApiHealthDashboard/Services/PollResult.cs`](src/ApiHealthDashboard/Services/PollResult.cs)
- [`src/ApiHealthDashboard/Services/PollResultKind.cs`](src/ApiHealthDashboard/Services/PollResultKind.cs)

Current behavior:
- performs async HTTP GET requests for configured endpoints
- uses per-endpoint timeout with fallback to the dashboard default timeout
- applies configured request headers
- captures check time, duration, HTTP status, response body, and error message
- distinguishes success, timeout, network failure, HTTP failure, empty response, and unknown failure
- avoids logging secret header values

### Health Response Parser

The app now includes a JSON health response parser that normalizes flexible payload shapes into recursive runtime models.

Parser components:
- [`src/ApiHealthDashboard/Parsing/IHealthResponseParser.cs`](src/ApiHealthDashboard/Parsing/IHealthResponseParser.cs)
- [`src/ApiHealthDashboard/Parsing/HealthResponseParser.cs`](src/ApiHealthDashboard/Parsing/HealthResponseParser.cs)

Current behavior:
- parses simple top-level health payloads with an overall `status`
- parses `entries` dictionaries common in ASP.NET Core health endpoint output
- parses nested child structures recursively
- preserves useful extra fields in snapshot metadata and node data
- applies per-endpoint `includeChecks` and `excludeChecks` filtering
- returns a parser-error snapshot instead of crashing on malformed JSON

### Polling Scheduler

The app now includes a hosted background scheduler for per-endpoint polling loops.

Scheduler components:
- [`src/ApiHealthDashboard/Scheduling/IEndpointScheduler.cs`](src/ApiHealthDashboard/Scheduling/IEndpointScheduler.cs)
- [`src/ApiHealthDashboard/Scheduling/PollingSchedulerService.cs`](src/ApiHealthDashboard/Scheduling/PollingSchedulerService.cs)

Current behavior:
- starts one polling loop per enabled endpoint
- respects each endpoint's own polling frequency
- prevents overlapping polls for the same endpoint with endpoint-level locking
- updates runtime state before and after each poll
- records last checked time, last successful time, duration, status, and current error
- keeps slow endpoints from blocking other endpoint loops
- already exposes a scheduler interface that Phase 8 can reuse for manual refresh actions

### Manual Refresh Actions

The dashboard and endpoint details pages now expose working manual refresh actions through Razor Pages handlers.

Current manual refresh behavior:
- dashboard supports refresh-all and refresh-single-endpoint actions
- endpoint details page supports refresh for the current endpoint
- refresh actions reuse the scheduler interface instead of duplicating poll logic
- user feedback is shown through page status messages after each action
- dashboard and details pages now read live configured endpoints and current runtime state instead of hard-coded placeholder rows

### Dashboard Summary

The dashboard home page now acts as an operational summary instead of a transitional placeholder.

Current dashboard behavior:
- shows configured, enabled, disabled, and actively polling endpoint counts
- highlights healthy, degraded, unhealthy, and unknown totals in summary cards
- renders a live endpoint table with last check, duration, error summary, and manual refresh actions
- surfaces degraded and unhealthy endpoints in an active issues panel for faster triage
- shows a clearer empty state when no endpoints are configured

### Endpoint Details

The endpoint details page now acts as a diagnostic view for a single configured endpoint.

Current details-page behavior:
- shows endpoint metadata including enabled state, frequency, timeout, and masked request headers
- shows request filter configuration for included and excluded checks
- summarizes the latest poll with status, timings, retrieved timestamp, and current error
- renders top-level and nested health checks recursively with native expand and collapse support
- surfaces snapshot metadata captured from the parsed response
- shows the raw payload section only when enabled in configuration

### Error Handling And Logging

The app now emits clearer structured logs around startup, configuration loading, polling, parsing failures, and manual refresh actions.

Current logging behavior:
- logs startup initialization begin, completion, app-started, and app-stopping events
- logs configuration load attempts, success, and startup-failure cases with the resolved YAML path
- logs manual refresh requests from both the dashboard and endpoint details pages
- logs poll start and completion with trigger source, duration, result kind, and HTTP status code when available
- logs timeout, network, HTTP, parser, and scheduler-loop failure conditions without exposing secret header values

### Automated Test Coverage

The test suite now covers both core happy paths and a wider set of invalid and operational edge cases.

Recent test expansion includes:
- isolated dashboard configuration validator tests
- YAML loader missing-file and malformed-YAML cases
- parser coverage for unknown-field preservation and recursive exclusion filtering
- poller coverage for unexpected failures and caller-driven cancellation
- scheduler coverage for parser-error snapshots and refresh-all enabled-endpoint counting
- page-model coverage for default details routing and missing endpoint-id refresh actions

### Publish And Deployment Validation

The app has now been validated as a portable publishable deployment, not just a local source checkout.

Validated deployment behavior:
- framework-dependent publish completes successfully
- self-contained Windows `win-x64` publish completes successfully
- published output includes `endpoints.yaml`, `appsettings.json`, and bundled local AdminLTE assets
- both published variants run directly from their publish folders and return HTTP `200` for `/`
- bundled CSS assets load from the published folders without relying on external CDNs
- no database package or runtime dependency is required
- no Node.js, npm, yarn, or frontend build tool dependency is required

### GitHub Actions And Dependabot

The repository now includes automation for CI, CodeQL scanning, release packaging, and dependency updates.

Current automation files:
- [`.github/workflows/ci-build.yml`](.github/workflows/ci-build.yml)
- [`.github/workflows/sast-scan.yml`](.github/workflows/sast-scan.yml)
- [`.github/workflows/release.yml`](.github/workflows/release.yml)
- [`.github/dependabot.yml`](.github/dependabot.yml)

Current automation behavior:
- CI runs on pushes to `main` and `develop`, and on pull requests targeting those branches
- CI restores, builds in Release mode, runs tests, and uploads TRX test results
- CodeQL runs for C# on pushes to `main`, pull requests targeting `main`, and manual dispatches
- Release automation verifies the solution, publishes self-contained artifacts for `win-x64` and `linux-x64`, packages them, and uploads them to the GitHub release
- Dependabot monitors both NuGet dependencies and GitHub Actions workflow dependencies on a weekly schedule

## Running The App

From the repository root:

```powershell
dotnet run --project .\src\ApiHealthDashboard\ApiHealthDashboard.csproj
```

The app reads the YAML path from the `Bootstrap:EndpointsConfigPath` setting in:
- [`src/ApiHealthDashboard/appsettings.json`](src/ApiHealthDashboard/appsettings.json)
- [`src/ApiHealthDashboard/appsettings.Development.json`](src/ApiHealthDashboard/appsettings.Development.json)

You can also override it with an environment variable:

```powershell
$env:APIHEALTHDASHBOARD_BOOTSTRAP__ENDPOINTSCONFIGPATH="D:\path\to\endpoints.yaml"
dotnet run --project .\src\ApiHealthDashboard\ApiHealthDashboard.csproj
```

## Running Tests

```powershell
dotnet test .\ApiHealthDashboard.sln -c Release
```

## Publishing

Framework-dependent publish:

```powershell
dotnet publish .\src\ApiHealthDashboard\ApiHealthDashboard.csproj -c Release --self-contained false
```

Self-contained Windows publish:

```powershell
dotnet publish .\src\ApiHealthDashboard\ApiHealthDashboard.csproj -c Release -r win-x64 --self-contained true
```

Deployment notes:
- the published folder is runnable on its own with the included `endpoints.yaml`
- local UI assets under `wwwroot/adminlte` remain bundled after publish
- no additional database or Node.js setup is required for the published app

## CI/CD Automation

Repository automation now includes:
- CI build and test workflow for `main` and `develop`
- CodeQL SAST workflow for C#
- release packaging workflow for self-contained GitHub release artifacts
- Dependabot configuration for NuGet and GitHub Actions dependencies

Local note:
- the workflow files were added and reviewed locally, and the application build still passes, but this shell environment does not include a YAML workflow linter for direct execution-free validation

Current automated coverage includes:
- valid YAML load
- normalization of null optional collections
- missing configuration file handling
- malformed YAML handling
- duplicate endpoint id validation
- invalid value aggregation
- isolated validator success and invalid-case checks
- environment variable substitution
- runtime state initialization
- runtime state upsert and deep-copy safety
- runtime state reinitialization
- runtime state concurrent update behavior
- poller success handling
- poller timeout handling
- poller network failure handling
- poller non-success HTTP status handling
- poller empty-response handling
- poller request header application
- parser flat payload handling
- parser entries payload handling
- parser nested payload handling
- parser recursive filtering behavior
- parser malformed JSON handling
- parser malformed JSON warning logging
- parser extra metadata preservation
- parser recursive exclusion filtering
- scheduler state update handling
- scheduler overlap prevention
- scheduler independent polling for enabled endpoints only
- scheduler refresh-all enabled-count behavior
- scheduler parser-error snapshot persistence
- dashboard page-model mixed counter and problem-endpoint calculation
- dashboard page-model empty dashboard state handling
- dashboard page-model missing endpoint-id refresh handling
- dashboard page-model refresh-all behavior
- dashboard page-model refresh-single behavior
- endpoint details page-model default endpoint resolution
- endpoint details page-model no-endpoint refresh handling
- endpoint details page-model diagnostic summary loading
- endpoint details raw-payload visibility rules
- endpoint details page-model refresh behavior
- endpoint details not-found behavior

Test file:
- [`tests/ApiHealthDashboard.Tests/Configuration/DashboardConfigValidatorTests.cs`](tests/ApiHealthDashboard.Tests/Configuration/DashboardConfigValidatorTests.cs)
- [`tests/ApiHealthDashboard.Tests/Configuration/YamlConfigLoaderTests.cs`](tests/ApiHealthDashboard.Tests/Configuration/YamlConfigLoaderTests.cs)
- [`tests/ApiHealthDashboard.Tests/State/InMemoryEndpointStateStoreTests.cs`](tests/ApiHealthDashboard.Tests/State/InMemoryEndpointStateStoreTests.cs)
- [`tests/ApiHealthDashboard.Tests/Services/EndpointPollerTests.cs`](tests/ApiHealthDashboard.Tests/Services/EndpointPollerTests.cs)
- [`tests/ApiHealthDashboard.Tests/Parsing/HealthResponseParserTests.cs`](tests/ApiHealthDashboard.Tests/Parsing/HealthResponseParserTests.cs)
- [`tests/ApiHealthDashboard.Tests/Scheduling/PollingSchedulerServiceTests.cs`](tests/ApiHealthDashboard.Tests/Scheduling/PollingSchedulerServiceTests.cs)
- [`tests/ApiHealthDashboard.Tests/Pages/IndexModelTests.cs`](tests/ApiHealthDashboard.Tests/Pages/IndexModelTests.cs)
- [`tests/ApiHealthDashboard.Tests/Pages/Endpoints/DetailsModelTests.cs`](tests/ApiHealthDashboard.Tests/Pages/Endpoints/DetailsModelTests.cs)

## Important Constraints

- Do not use Xabaril health check UI packages
- Do not rely on CDN-hosted frontend assets
- Do not require a database
- Keep runtime state in memory only
- Prefer small, focused services

## Development Progress

- [x] Phase 1 - Solution bootstrap
- [x] Phase 2 - Local AdminLTE integration
- [x] Phase 3 - YAML configuration loader
- [x] Phase 4 - Runtime state store
- [x] Phase 5 - HTTP poller
- [x] Phase 6 - Health response parser
- [x] Phase 7 - Polling scheduler
- [x] Phase 8 - Manual refresh actions
- [x] Phase 9 - Dashboard summary page
- [x] Phase 10 - Endpoint details page
- [x] Phase 11 - Error handling and logging
- [x] Phase 12 - Automated tests expansion
- [x] Phase 13 - Publish and deployment validation
- [x] Phase 14 - GitHub Actions CI/CD

## Future Plans

These are planned enhancements after the current v1 path:
- add an import flow that can derive YAML endpoint config from API request and response inspection, with preview and diff comparison before any manual save
- add CLI execution with machine-readable output for automation and scripting scenarios
- allow per-endpoint priority so important endpoints can be surfaced and scheduled differently
- optionally allow email sending, either through direct SMTP configuration or by calling an external API

## Notes For Ongoing Updates

This README is intended to evolve with the project. As new phases land, we should keep these sections current:
- implemented features
- run and configuration instructions
- test coverage
- development progress checklist
- deployment and CI/CD notes
