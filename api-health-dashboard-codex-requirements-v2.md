# API Health Check Dashboard - Codex Implementation Requirements

## 1. Project Overview

Build a **portable .NET web application** that acts as a dashboard for monitoring API health check endpoints.

The dashboard will:
- call health check endpoints exposed by APIs
- consume JSON responses from ASP.NET Core health check middleware endpoints
- show overall endpoint health and individual health checks
- support nested health checks when present
- allow per-endpoint polling frequency
- allow users to manage configured endpoints
- store configuration in YAML
- use a locally bundled **AdminLTE** theme
- avoid public internet dependencies at both runtime and frontend asset loading
- **not use the Xabaril HealthChecks libraries**
- include **GitHub CI/CD** with build, test, SAST, and publish/release workflows

The solution should be lightweight, portable, maintainable, and suitable for internal or restricted enterprise environments.

---

## 2. Primary Goals

The application should provide:
1. A summary dashboard of all monitored APIs
2. A details page for each API endpoint
3. Configurable per-endpoint polling
4. YAML-based endpoint configuration
5. Nested health check rendering
6. A clean AdminLTE-based UI
7. Self-contained deployment without requiring a database
8. GitHub Actions workflows for CI, SAST, and release packaging

---

## 3. Scope

### 3.1 In Scope
- ASP.NET Core web application
- Dashboard UI
- Endpoint configuration from YAML
- Manual and scheduled health polling
- Nested health response rendering
- In-memory runtime state
- Local static asset hosting
- Health check filtering by configured check names
- Lightweight endpoint management approach
- Support for restricted / air-gapped environments
- GitHub Actions workflows for build, test, SAST, dependency scanning, and release artifacts

### 3.2 Out of Scope for v1
- Database persistence
- Alerts via email, Slack, or Teams
- Authentication/authorization unless explicitly added later
- Full edit-in-browser configuration UI
- Historical analytics persistence
- Distributed polling agents
- Auto-discovery of APIs

---

## 4. Key Constraints

## 4.1 Do Not Use Xabaril
The application **must NOT use** any Xabaril-related health check UI libraries.

This includes but is not limited to:
- `AspNetCore.Diagnostics.HealthChecks`
- `AspNetCore.HealthChecks.UI`
- `AspNetCore.HealthChecks.UI.Client`

The app must instead:
- call configured health endpoints directly via HTTP
- parse returned JSON payloads itself
- normalize payloads into an internal model
- render a custom dashboard UI

## 4.2 Use AdminLTE
The application shall use **AdminLTE** as the dashboard theme.

Requirements:
- AdminLTE assets must be bundled locally
- no runtime dependency on CDNs
- pages should follow AdminLTE dashboard layout patterns

## 4.3 No Public Internet Dependencies
The application must not load external assets from the public internet.

Do not use:
- CDN-hosted CSS
- CDN-hosted JavaScript
- Google Fonts
- hosted icon references
- npm/yarn runtime fetches

All assets must be committed locally and served by the app.

## 4.4 Portable and Lightweight
The app should:
- run as a standard .NET web app
- support self-contained publish
- support standalone folder deployment
- optionally support Docker containerization
- avoid requiring a database

---

## 5. Functional Requirements

## 5.1 Endpoint Configuration
The system shall load endpoint configuration from a YAML file.

Each endpoint shall support:
- `id`
- `name`
- `url`
- `enabled`
- `frequencySeconds`
- `timeoutSeconds`
- `includeChecks`
- `excludeChecks`
- `headers`

Behavior:
1. Disabled endpoints shall not be polled automatically.
2. Endpoint IDs must be unique.
3. Missing optional arrays shall be treated as empty.
4. Timeout should fall back to a global default when omitted.
5. Custom request headers must be supported.

## 5.2 Polling
The system shall:
1. poll each enabled endpoint independently
2. support per-endpoint frequency configuration
3. support manual refresh for one endpoint
4. support manual refresh for all enabled endpoints
5. prevent overlapping polls for the same endpoint
6. record last checked time
7. record last successful check time
8. record request duration
9. record last error when a poll fails

## 5.3 Response Parsing
The system shall:
1. consume JSON from health endpoints
2. parse overall status
3. parse individual health checks when available
4. support nested health structures when present
5. tolerate schema variation
6. preserve useful unknown fields where practical
7. normalize all parsed responses into a consistent internal model

## 5.4 Check Filtering
The system shall support per-endpoint filtering of health checks.

Rules:
1. If `includeChecks` is empty, all checks are eligible.
2. If `includeChecks` is populated, only named checks are eligible.
3. `excludeChecks` shall then remove matching checks.
4. Filtering should support nested checks where names are available.

## 5.5 Dashboard Views
The application shall provide:
- a summary page
- an endpoint details page

### Summary Page
The summary page shall show:
- endpoint name
- overall status
- last checked time
- last successful time
- frequency
- response duration
- current error state if any

### Details Page
The details page shall show:
- endpoint metadata
- top-level health checks
- nested health checks recursively
- status for each node
- optional description / duration / error summary / metadata
- raw response payload section if enabled

## 5.6 Endpoint Management
For v1, endpoint management may be YAML-driven rather than full CRUD UI.

Minimum requirement:
- users can manage endpoints through YAML
- UI should display current endpoint definitions and runtime state

Optional v1 enhancement:
- reload YAML without full redeploy
- file-watch-based config refresh

---

## 6. Non-Functional Requirements

## 6.1 Portability
The app shall run on:
- Windows
- Linux
- macOS where supported by .NET runtime

## 6.2 Reliability
1. A single endpoint failure must not stop the application.
2. A malformed response must only affect the endpoint that returned it.
3. The system should recover automatically on later polls after transient failures.

## 6.3 Performance
1. HTTP polling must use async I/O.
2. UI should remain responsive while polling occurs in background.
3. The solution should be efficient enough for at least 100 endpoints with moderate polling intervals.

## 6.4 Maintainability
The codebase should separate:
- configuration loading
- polling / scheduling
- response parsing
- state storage
- UI rendering

## 6.5 Security
1. HTTPS endpoints must be supported.
2. Secret header values must not be logged in plaintext.
3. Displayed errors should avoid exposing unnecessary sensitive information.

---

## 7. Recommended Tech Stack

Use these implementation preferences unless a clearly better alternative exists.

### Backend
- .NET 8 or latest stable .NET
- ASP.NET Core
- Razor Pages preferred, MVC acceptable
- `HttpClientFactory`
- background hosted services for polling
- in-memory state store

### Frontend
- AdminLTE
- Bootstrap bundled locally
- FontAwesome bundled locally
- minimal JavaScript
- server-rendered pages

### Configuration
- YAML file
- strongly typed config models
- environment variable substitution if practical

### Testing
- xUnit or NUnit
- mock HTTP handlers for endpoint tests

---

## 8. Recommended Solution Structure

Use a clean but practical project layout.

```text
/src
  /ApiHealthDashboard
    /Configuration
    /Domain
    /Services
    /Parsing
    /Scheduling
    /State
    /Pages
    /wwwroot
      /adminlte
      /css
      /js
      /lib
      /fonts
      /icons
    Program.cs
    appsettings.json
    endpoints.yaml

/tests
  /ApiHealthDashboard.Tests
```

---

## 9. Suggested Domain Models

These class names are recommendations and may be adjusted slightly if needed.

## 9.1 Configuration Models

```csharp
public sealed class DashboardConfig
{
    public DashboardSettings Dashboard { get; set; } = new();
    public List<EndpointConfig> Endpoints { get; set; } = new();
}

public sealed class DashboardSettings
{
    public int RefreshUiSeconds { get; set; } = 10;
    public int RequestTimeoutSecondsDefault { get; set; } = 10;
    public bool ShowRawPayload { get; set; } = false;
}

public sealed class EndpointConfig
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Url { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public int FrequencySeconds { get; set; } = 30;
    public int? TimeoutSeconds { get; set; }
    public Dictionary<string, string> Headers { get; set; } = new();
    public List<string> IncludeChecks { get; set; } = new();
    public List<string> ExcludeChecks { get; set; } = new();
}
```

## 9.2 Runtime State Models

```csharp
public sealed class EndpointState
{
    public string EndpointId { get; set; } = "";
    public string EndpointName { get; set; } = "";
    public string Status { get; set; } = "Unknown";
    public DateTimeOffset? LastCheckedUtc { get; set; }
    public DateTimeOffset? LastSuccessfulUtc { get; set; }
    public long? DurationMs { get; set; }
    public string? LastError { get; set; }
    public HealthSnapshot? Snapshot { get; set; }
    public bool IsPolling { get; set; }
}

public sealed class HealthSnapshot
{
    public string OverallStatus { get; set; } = "Unknown";
    public DateTimeOffset RetrievedUtc { get; set; }
    public long DurationMs { get; set; }
    public string RawPayload { get; set; } = "";
    public List<HealthNode> Nodes { get; set; } = new();
    public Dictionary<string, object?> Metadata { get; set; } = new();
}

public sealed class HealthNode
{
    public string Name { get; set; } = "";
    public string Status { get; set; } = "Unknown";
    public string? Description { get; set; }
    public string? ErrorMessage { get; set; }
    public string? DurationText { get; set; }
    public Dictionary<string, object?> Data { get; set; } = new();
    public List<HealthNode> Children { get; set; } = new();
}
```

---

## 10. Recommended Services

## 10.1 IYamlConfigLoader
Responsibility:
- load YAML from file
- map to config models
- validate config
- expose errors clearly

Suggested methods:
```csharp
public interface IYamlConfigLoader
{
    DashboardConfig Load(string path);
}
```

## 10.2 IEndpointPoller
Responsibility:
- execute HTTP request for a single endpoint
- apply timeout
- apply headers
- capture result
- invoke parser

Suggested methods:
```csharp
public interface IEndpointPoller
{
    Task<PollResult> PollAsync(EndpointConfig endpoint, CancellationToken cancellationToken);
}
```

## 10.3 IHealthResponseParser
Responsibility:
- parse JSON from endpoint
- normalize response into `HealthSnapshot`

Suggested methods:
```csharp
public interface IHealthResponseParser
{
    HealthSnapshot Parse(string endpointId, string endpointName, string json, long durationMs);
}
```

## 10.4 IEndpointStateStore
Responsibility:
- hold current runtime state in memory
- update endpoint states safely
- expose list/details to UI

Suggested methods:
```csharp
public interface IEndpointStateStore
{
    IReadOnlyCollection<EndpointState> GetAll();
    EndpointState? Get(string endpointId);
    void Upsert(EndpointState state);
}
```

## 10.5 PollingSchedulerService
Responsibility:
- schedule independent endpoint polling loops
- prevent overlap per endpoint
- support startup initialization
- support manual refresh triggers

Implementation suggestion:
- one background coordinator service
- endpoint-level locking using `SemaphoreSlim` or equivalent
- use async timers / delay loops

---

## 11. Scheduler Design Requirements

The polling engine should be simple and reliable.

Recommended behavior:
1. Load all enabled endpoints at startup.
2. Start independent scheduling loops per endpoint.
3. Each endpoint loop waits for its own `frequencySeconds`.
4. Manual refresh should trigger an immediate poll without corrupting schedule state.
5. Poll overlap for the same endpoint must be prevented.
6. A failed poll should not terminate the loop.
7. Cancellation should stop all loops cleanly on shutdown.

Suggested approach:
- `BackgroundService` for orchestration
- per-endpoint `SemaphoreSlim`
- capture timestamps using UTC
- use structured logging

Pseudo-flow:
```text
Startup
  -> load config
  -> initialize in-memory states
  -> start scheduler
      -> for each enabled endpoint
          -> run endpoint polling loop
              -> wait until next due time
              -> try acquire lock
              -> call endpoint
              -> parse response
              -> update state
              -> release lock
```

---

## 12. Health Response Parser Requirements

The parser must not assume one exact response schema.

It should support at least these patterns:
1. simple top-level `status`
2. `entries` dictionary pattern common in ASP.NET custom health output
3. nested object graphs where child checks are embedded
4. additional metadata fields such as `description`, `duration`, `data`, `tags`, `exception`

### Parser strategy
1. parse JSON into `JsonDocument`
2. detect overall status
3. detect known child containers like `entries`
4. recursively walk child nodes where structure indicates nested checks
5. preserve additional non-structural fields into `Data`
6. produce a consistent `HealthSnapshot`

### Parsing rules
- unknown field names should not cause total failure
- invalid JSON should produce a parsing error state
- partial extraction is better than complete discard
- if only overall status exists, still produce a valid snapshot

---

## 13. YAML Configuration Design

## 13.1 Example YAML

```yaml
dashboard:
  refreshUiSeconds: 10
  requestTimeoutSecondsDefault: 10
  showRawPayload: true

endpoints:
  - id: orders-api
    name: Orders API
    url: https://orders.example.com/health
    enabled: true
    frequencySeconds: 30
    timeoutSeconds: 10
    headers:
      X-API-Key: ${ORDERS_API_KEY}
    includeChecks:
      - self
      - database
      - redis
    excludeChecks:
      - optional-third-party

  - id: billing-api
    name: Billing API
    url: https://billing.example.com/health
    enabled: true
    frequencySeconds: 60
    timeoutSeconds: 15
    headers: {}
    includeChecks: []
    excludeChecks: []
```

## 13.2 Validation Rules
- `id` is required and unique
- `name` is required
- `url` must be absolute HTTP or HTTPS
- `frequencySeconds` must be greater than zero
- `timeoutSeconds`, if present, must be greater than zero
- header names must be non-empty
- null arrays should be normalized to empty arrays

---

## 14. UI Requirements Using AdminLTE

## 14.1 Layout
Use an AdminLTE layout with:
- top navbar
- left sidebar
- main dashboard content area
- cards/widgets for endpoint summary
- detail cards/tables for health nodes

## 14.2 Pages

### Home / Dashboard
Show:
- total endpoints
- count healthy
- count degraded
- count unhealthy
- count unreachable / unknown
- endpoint cards or table

Each endpoint row/card should show:
- name
- status badge
- last checked time
- duration
- frequency
- quick action for refresh
- link to details page

### Endpoint Details Page
Show:
- endpoint metadata
- URL
- enabled status
- timeout
- headers summary excluding secret values
- current overall status
- recursive tree/table of checks

### Optional Config View
Show:
- effective YAML-derived endpoint config
- validation errors if any

## 14.3 Nested Rendering
Nested checks should be shown with:
- indentation
- collapsible sections
- tree or nested table style
- badge per node status

## 14.4 Status Styling
Suggested mapping:
- Healthy -> green
- Degraded -> yellow/orange
- Unhealthy -> red
- Unknown / Unreachable -> gray

---

## 15. Local Asset Requirements

All frontend assets must live under `wwwroot`.

Suggested organization:
```text
/wwwroot
  /adminlte
  /css
  /js
  /lib
  /fonts
  /icons
```

Requirements:
1. AdminLTE CSS/JS must be stored locally.
2. Bootstrap must be stored locally.
3. FontAwesome or chosen icon set must be stored locally.
4. No CDN references are allowed in layout files.
5. No public internet fetch should be required for UI rendering.

---

## 16. Deployment Requirements

The app should support:
1. framework-dependent run
2. self-contained publish
3. standalone folder deployment
4. optional Docker deployment

Example self-contained publish:

```bash
dotnet publish -c Release -r win-x64 --self-contained true
```

All static assets must be copied into publish output.

The app should not require:
- SQL Server
- Redis
- external asset pipeline
- Node.js
- npm
- yarn

---

## 17. Testing Requirements

Create automated tests for:
1. YAML parsing
2. YAML validation errors
3. polling success
4. timeout handling
5. unreachable endpoint handling
6. malformed JSON handling
7. nested response parsing
8. include/exclude filtering
9. state store updates
10. controller/page model behavior where practical

Test scenarios should include:
- Healthy endpoint
- Degraded endpoint
- Unhealthy endpoint
- Unknown endpoint
- nested health payload
- payload with extra fields
- duplicate endpoint IDs
- invalid URL
- null include/exclude arrays

---

## 18. Acceptance Criteria

The solution is accepted for v1 when:
1. The app starts from YAML configuration successfully.
2. Multiple endpoints can be monitored concurrently.
3. Each endpoint can have its own polling frequency.
4. Overall endpoint health is visible in the dashboard.
5. Individual health checks are shown when provided.
6. Nested health checks render correctly.
7. Endpoint failures do not crash the application.
8. AdminLTE styling is applied using only local assets.
9. No Xabaril package is used.
10. The app runs without a database.
11. Automated tests are included and pass in CI.
12. GitHub Actions can build, scan, and package the project.

---

## 19. Build Guidance for Codex

Implement the project in this order:

1. Create ASP.NET Core Razor Pages app
2. Add local AdminLTE assets and base layout
3. Create config models and YAML loader
4. Add config validation
5. Create runtime state store
6. Create endpoint poller using `HttpClientFactory`
7. Create health response parser with recursive normalization
8. Create background scheduler service
9. Create dashboard page
10. Create endpoint details page
11. Add manual refresh actions
12. Add tests
13. Add GitHub Actions workflows

### Code style expectations
- use clear naming
- avoid overengineering
- prefer small services with single responsibilities
- use dependency injection
- use async/await
- add comments only where useful
- keep models serializable and easy to debug

---

## 20. Strong Codex Prompt

Use the following instruction block as the build prompt for Codex.

```text
Create a portable ASP.NET Core Razor Pages application in .NET 8 named ApiHealthDashboard.

Requirements:
- Do NOT use Xabaril health check libraries or UI packages.
- Use AdminLTE theme with all assets stored locally under wwwroot.
- Do not reference any public CDN or internet-hosted frontend assets.
- Load endpoint configuration from a YAML file named endpoints.yaml.
- Poll multiple ASP.NET Core health check endpoints independently.
- Support per-endpoint polling frequency, timeout, headers, includeChecks, and excludeChecks.
- Parse JSON health responses without assuming one exact schema.
- Support nested health checks and normalize them into a recursive HealthNode model.
- Keep runtime state in memory only.
- Provide a dashboard summary page and an endpoint details page.
- Show endpoint name, status, last checked time, last successful time, duration, and errors.
- Allow manual refresh for a single endpoint and all endpoints.
- Prevent overlapping polls for the same endpoint.
- Use HttpClientFactory, BackgroundService, dependency injection, and structured logging.
- Keep the app lightweight: no database, no Node.js, no SPA frameworks.
- Prefer Razor Pages and server-side rendering.
- Include unit tests for YAML parsing, parser behavior, filtering, and polling.
- Add GitHub Actions workflows for CI build, CodeQL SAST, and release packaging.
- Add Dependabot configuration for NuGet dependency updates.
- Organize code into configuration, parsing, services, scheduling, state, and pages.
- Include sample endpoints.yaml.
- Make the solution self-contained and ready for local run or publish.
```

---

## 21. Future Enhancements

These are optional later improvements and should not block v1:
- lightweight auth
- config reload on file change
- short in-memory status history
- charts / mini trends
- export of current status
- environment grouping
- tag-based filtering
- retry policy with backoff
- readonly config viewer page

---

## 22. GitHub CI/CD Requirements

The project shall include **GitHub Actions workflows** that provide:
- automated build and test
- static application security testing (SAST)
- dependency vulnerability scanning
- artifact packaging
- publish/release workflow

The CI/CD pipeline must run automatically on commits and pull requests.

## 22.1 Workflow Files
The repository shall include workflows under:

```text
.github/workflows/
  ci-build.yml
  sast-scan.yml
  release.yml
```

The repository should also include:

```text
.github/dependabot.yml
```

## 22.2 CI Build Workflow
The CI build workflow shall:
1. trigger on pushes to `main` and `develop`
2. trigger on pull requests targeting `main` and `develop`
3. check out the repository
4. set up .NET 8
5. restore packages
6. build the solution in Release mode
7. run automated tests
8. fail when build or tests fail
9. upload test results where practical

## 22.3 SAST Workflow
The SAST workflow shall use **GitHub CodeQL**.

Requirements:
1. trigger on pull requests and pushes to `main`
2. initialize CodeQL for C#
3. build the project
4. run CodeQL analysis
5. upload results to the GitHub Security tab

## 22.4 Dependency Scanning
The repository should use **Dependabot** for NuGet dependencies.

Requirements:
1. monitor NuGet dependencies
2. create update pull requests
3. help surface vulnerable dependency versions

Optional enhancement:
- add OWASP Dependency Check or equivalent later if needed

## 22.5 Release Workflow
The release workflow shall:
1. trigger when a GitHub release tag such as `v1.0.0` is created
2. restore packages
3. build the solution
4. run tests
5. publish self-contained release artifacts
6. support at least one runtime target such as `win-x64` or `linux-x64`
7. package artifacts as `.zip` or `.tar.gz`
8. upload artifacts to the GitHub Release

## 22.6 CI Security Rules
The CI/CD setup must enforce the following:
1. pull requests should pass CI before merge
2. security scan results should be visible in GitHub Security
3. build should fail when compilation or tests fail
4. critical SAST failures should block release readiness

## 22.7 Acceptance Criteria for CI/CD
The CI/CD setup is complete when:
1. pushing code triggers CI automatically
2. tests execute automatically
3. CodeQL scan runs successfully
4. dependency update scanning is configured
5. creating a GitHub release produces build artifacts automatically

## 22.8 Future CI Enhancements
Possible future improvements:
- container image build and push
- SBOM generation
- license scanning
- artifact signing
- deployment workflows