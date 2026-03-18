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

Not implemented yet:
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

Test file:
- [`tests/ApiHealthDashboard.Tests/Configuration/YamlConfigLoaderTests.cs`](tests/ApiHealthDashboard.Tests/Configuration/YamlConfigLoaderTests.cs)
- [`tests/ApiHealthDashboard.Tests/State/InMemoryEndpointStateStoreTests.cs`](tests/ApiHealthDashboard.Tests/State/InMemoryEndpointStateStoreTests.cs)
- [`tests/ApiHealthDashboard.Tests/Services/EndpointPollerTests.cs`](tests/ApiHealthDashboard.Tests/Services/EndpointPollerTests.cs)
- [`tests/ApiHealthDashboard.Tests/Parsing/HealthResponseParserTests.cs`](tests/ApiHealthDashboard.Tests/Parsing/HealthResponseParserTests.cs)

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
- [ ] Phase 7 - Polling scheduler
- [ ] Phase 8 - Manual refresh actions
- [ ] Phase 9 - Dashboard summary page
- [ ] Phase 10 - Endpoint details page
- [ ] Phase 11 - Error handling and logging
- [ ] Phase 12 - Automated tests expansion
- [ ] Phase 13 - Publish and deployment validation
- [ ] Phase 14 - GitHub Actions CI/CD

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
