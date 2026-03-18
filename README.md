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

Not implemented yet:
- endpoint polling
- health response parsing
- scheduler
- manual refresh actions
- real dashboard data binding
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
- dashboard placeholder: [`src/ApiHealthDashboard/Pages/Index.cshtml`](src/ApiHealthDashboard/Pages/Index.cshtml)
- endpoint details placeholder: [`src/ApiHealthDashboard/Pages/Endpoints/Details.cshtml`](src/ApiHealthDashboard/Pages/Endpoints/Details.cshtml)

### YAML Configuration

Dashboard configuration is loaded at startup from [`src/ApiHealthDashboard/endpoints.yaml`](src/ApiHealthDashboard/endpoints.yaml).

Current configuration support:
- `dashboard.refreshUiSeconds`
- `dashboard.requestTimeoutSecondsDefault`
- `dashboard.showRawPayload`
- endpoint `id`, `name`, `url`, `enabled`, `frequencySeconds`, `timeoutSeconds`
- endpoint `headers`, `includeChecks`, `excludeChecks`
- `${ENV_VAR}` substitution in YAML values

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

Current automated coverage includes:
- valid YAML load
- normalization of null optional collections
- duplicate endpoint id validation
- invalid value aggregation
- environment variable substitution
- runtime state initialization
- runtime state upsert and deep-copy safety
- runtime state reinitialization
- runtime state concurrent update behavior

Test file:
- [`tests/ApiHealthDashboard.Tests/Configuration/YamlConfigLoaderTests.cs`](tests/ApiHealthDashboard.Tests/Configuration/YamlConfigLoaderTests.cs)
- [`tests/ApiHealthDashboard.Tests/State/InMemoryEndpointStateStoreTests.cs`](tests/ApiHealthDashboard.Tests/State/InMemoryEndpointStateStoreTests.cs)

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
- [ ] Phase 5 - HTTP poller
- [ ] Phase 6 - Health response parser
- [ ] Phase 7 - Polling scheduler
- [ ] Phase 8 - Manual refresh actions
- [ ] Phase 9 - Dashboard summary page
- [ ] Phase 10 - Endpoint details page
- [ ] Phase 11 - Error handling and logging
- [ ] Phase 12 - Automated tests expansion
- [ ] Phase 13 - Publish and deployment validation
- [ ] Phase 14 - GitHub Actions CI/CD

## Notes For Ongoing Updates

This README is intended to evolve with the project. As new phases land, we should keep these sections current:
- implemented features
- run and configuration instructions
- test coverage
- development progress checklist
- deployment and CI/CD notes
