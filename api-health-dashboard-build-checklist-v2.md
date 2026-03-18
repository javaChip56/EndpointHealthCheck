# API Health Check Dashboard - Build Task Checklist

## 1. Goal

Use this checklist to implement the **ApiHealthDashboard** project in a practical sequence.

This checklist is designed for:
- Codex task execution
- developer handoff
- sprint planning
- personal build tracking

It assumes the following fixed constraints:
- .NET 8 ASP.NET Core app
- Razor Pages preferred
- AdminLTE theme
- all frontend assets hosted locally
- no public CDN usage
- no Xabaril libraries
- YAML-based endpoint configuration
- in-memory runtime state only
- GitHub Actions CI/CD with SAST and release packaging

---

## 2. Suggested Delivery Phases

## Phase 1 - Solution Bootstrap
Objective: create the base application and folder structure.

### Tasks
- [ ] Create solution named `ApiHealthDashboard`
- [ ] Create main web project
- [ ] Create test project
- [ ] Add project references
- [ ] Create base folder structure:
  - [ ] `Configuration`
  - [ ] `Domain`
  - [ ] `Services`
  - [ ] `Parsing`
  - [ ] `Scheduling`
  - [ ] `State`
  - [ ] `Pages`
  - [ ] `wwwroot`
- [ ] Add initial `Program.cs`
- [ ] Verify app runs locally
- [ ] Commit initial scaffold

### Deliverables
- running empty Razor Pages app
- solution and test project created
- clean project layout

### Acceptance Check
- App starts successfully
- Home page loads
- Test project builds

---

## Phase 2 - Local AdminLTE Integration
Objective: set up the dashboard layout using only local frontend assets.

### Tasks
- [ ] Obtain AdminLTE distribution files
- [ ] Store AdminLTE locally under `wwwroot/adminlte`
- [ ] Store Bootstrap locally under `wwwroot/lib` or equivalent
- [ ] Store icon assets locally
- [ ] Ensure no CDN references exist
- [ ] Create shared layout page using AdminLTE shell
- [ ] Add:
  - [ ] top navbar
  - [ ] left sidebar
  - [ ] content wrapper
  - [ ] footer
- [ ] Add placeholder dashboard page
- [ ] Add placeholder endpoint details page
- [ ] Confirm CSS and JS load correctly from local files only

### Deliverables
- AdminLTE-based site layout
- local-only static assets
- placeholder pages using dashboard theme

### Acceptance Check
- Browser dev tools show no public CDN/network dependency for UI assets
- Dashboard layout renders correctly
- Sidebar and navbar display properly

---

## Phase 3 - YAML Configuration Loader
Objective: load endpoint definitions from YAML into strongly typed models.

### Tasks
- [ ] Add YAML parsing library
- [ ] Create `DashboardConfig`
- [ ] Create `DashboardSettings`
- [ ] Create `EndpointConfig`
- [ ] Create `IYamlConfigLoader`
- [ ] Implement YAML loader
- [ ] Add startup config file path handling
- [ ] Create sample `endpoints.yaml`
- [ ] Support missing optional arrays as empty
- [ ] Add configuration validation logic
- [ ] Validate:
  - [ ] unique endpoint IDs
  - [ ] non-empty names
  - [ ] valid absolute URLs
  - [ ] positive frequency values
  - [ ] positive timeout values
- [ ] Return clear validation messages

### Deliverables
- typed configuration models
- YAML loader
- config validation
- sample YAML file

### Acceptance Check
- Valid YAML loads successfully
- Invalid YAML returns clear errors
- Duplicate IDs are rejected
- Empty optional fields are handled safely

---

## Phase 4 - Runtime State Store
Objective: create in-memory storage for endpoint status and snapshots.

### Tasks
- [ ] Create `EndpointState`
- [ ] Create `HealthSnapshot`
- [ ] Create `HealthNode`
- [ ] Create `IEndpointStateStore`
- [ ] Implement thread-safe in-memory store
- [ ] Add methods for:
  - [ ] get all endpoint states
  - [ ] get one endpoint state
  - [ ] insert/update endpoint state
  - [ ] initialize endpoint states at startup
- [ ] Add support for `IsPolling`
- [ ] Add support for `LastError`
- [ ] Add support for raw payload storage

### Deliverables
- thread-safe in-memory runtime state store
- initial endpoint state initialization

### Acceptance Check
- Store can return all configured endpoints
- State updates do not corrupt data
- Concurrent updates behave correctly

---

## Phase 5 - HTTP Poller
Objective: call health endpoints safely and consistently.

### Tasks
- [ ] Add `HttpClientFactory`
- [ ] Create `IEndpointPoller`
- [ ] Create `PollResult` model
- [ ] Implement HTTP GET polling
- [ ] Apply per-endpoint timeout
- [ ] Apply custom headers
- [ ] Measure response duration
- [ ] Capture status code
- [ ] Read response body as text
- [ ] Handle:
  - [ ] timeout
  - [ ] DNS/network failure
  - [ ] non-success HTTP status
  - [ ] empty response
- [ ] Ensure secrets are not logged

### Deliverables
- reusable endpoint polling service
- clear result contract for success/failure

### Acceptance Check
- Poller retrieves successful endpoint payload
- Timeout is detected correctly
- Failure result includes useful error message
- Per-endpoint headers are applied

---

## Phase 6 - Health Response Parser
Objective: normalize different health payload shapes into one internal tree model.

### Tasks
- [ ] Create `IHealthResponseParser`
- [ ] Implement JSON parsing with `JsonDocument`
- [ ] Detect overall status
- [ ] Support top-level `entries` dictionary pattern
- [ ] Support flat health result payloads
- [ ] Support nested health structures recursively
- [ ] Preserve unknown fields into metadata/data where practical
- [ ] Map parsed data into `HealthSnapshot`
- [ ] Map health items into recursive `HealthNode`
- [ ] Add filtering logic for:
  - [ ] includeChecks
  - [ ] excludeChecks
- [ ] Ensure parsing can partially succeed even with unknown fields
- [ ] Distinguish parsing error from endpoint/network failure

### Deliverables
- robust health payload parser
- recursive internal model
- filter support

### Acceptance Check
- Parser handles simple payload
- Parser handles `entries` payload
- Parser handles nested payload
- Parser does not crash on unknown fields
- Filtering behaves correctly

---

## Phase 7 - Polling Scheduler
Objective: poll all enabled endpoints on independent schedules.

### Tasks
- [ ] Create background scheduler service
- [ ] Load enabled endpoints at startup
- [ ] Create endpoint scheduling loop per endpoint
- [ ] Prevent overlapping polls for the same endpoint
- [ ] Use `SemaphoreSlim` or equivalent per endpoint
- [ ] Update runtime state before and after poll
- [ ] Record:
  - [ ] last checked time
  - [ ] last successful time
  - [ ] duration
  - [ ] current status
  - [ ] current error
- [ ] Ensure failure does not stop future polling
- [ ] Support graceful shutdown
- [ ] Add structured logs

### Deliverables
- background polling scheduler
- isolated per-endpoint polling loops

### Acceptance Check
- Multiple endpoints poll independently
- Slow endpoint does not block others
- Same endpoint is not double-polled concurrently
- App shuts down cleanly

---

## Phase 8 - Manual Refresh Actions
Objective: allow the user to trigger refreshes from the UI.

### Tasks
- [ ] Add refresh-all action
- [ ] Add refresh-single-endpoint action
- [ ] Reuse poller/scheduler logic without duplication
- [ ] Prevent collision with in-progress scheduled polls
- [ ] Return user-friendly result messages
- [ ] Update state immediately after manual refresh

### Deliverables
- manual refresh capability
- safe poll triggering path

### Acceptance Check
- User can refresh one endpoint
- User can refresh all endpoints
- Manual refresh does not break scheduler
- In-progress refresh is handled safely

---

## Phase 9 - Dashboard Summary Page
Objective: display all endpoints in a useful health overview.

### Tasks
- [ ] Build home/dashboard Razor Page
- [ ] Read endpoint states from store
- [ ] Show summary counters:
  - [ ] total
  - [ ] healthy
  - [ ] degraded
  - [ ] unhealthy
  - [ ] unknown/unreachable
- [ ] Render endpoint cards or table
- [ ] Show:
  - [ ] endpoint name
  - [ ] status badge
  - [ ] last checked
  - [ ] last successful
  - [ ] duration
  - [ ] frequency
  - [ ] current error
- [ ] Add link to endpoint details page
- [ ] Add refresh buttons
- [ ] Add empty-state UI when no endpoints configured

### Deliverables
- functional summary dashboard page

### Acceptance Check
- Summary page loads without errors
- Endpoint statuses display correctly
- Counters reflect current runtime state
- Refresh actions are visible and usable

---

## Phase 10 - Endpoint Details Page
Objective: show full details for one endpoint including nested checks.

### Tasks
- [ ] Build endpoint details Razor Page
- [ ] Load endpoint config and runtime state
- [ ] Display metadata:
  - [ ] name
  - [ ] URL
  - [ ] enabled
  - [ ] frequency
  - [ ] timeout
  - [ ] headers summary without exposing secrets
- [ ] Display overall status
- [ ] Render recursive health node tree
- [ ] Add expand/collapse support for nested checks
- [ ] Show description/duration/error/data when available
- [ ] Add raw payload section if enabled by config
- [ ] Add refresh action from details page
- [ ] Handle endpoint-not-found case cleanly

### Deliverables
- endpoint details page with nested rendering

### Acceptance Check
- Nested health checks render clearly
- Status badges display properly
- Metadata is accurate
- Raw payload visibility follows config setting

---

## Phase 11 - Error Handling and Logging
Objective: make the application operationally understandable.

### Tasks
- [ ] Add startup logging
- [ ] Add config load success/failure logging
- [ ] Add poll start/finish logging
- [ ] Add parsing error logging
- [ ] Add timeout logging
- [ ] Add manual refresh logging
- [ ] Avoid logging secrets
- [ ] Normalize user-facing error messages
- [ ] Ensure invalid endpoint payloads do not crash pages

### Deliverables
- useful operational logs
- safer and clearer error handling

### Acceptance Check
- Logs are readable and structured
- Errors are visible but not overly noisy
- Secrets are not exposed in logs or UI

---

## Phase 12 - Automated Tests
Objective: cover the main behavior with repeatable tests.

### Tasks
- [ ] Add config loader unit tests
- [ ] Add config validation tests
- [ ] Add poller tests using mocked HTTP handler
- [ ] Add parser tests for:
  - [ ] flat payload
  - [ ] `entries` payload
  - [ ] nested payload
  - [ ] malformed JSON
  - [ ] extra unknown fields
- [ ] Add filter tests for include/exclude logic
- [ ] Add state store concurrency/basic tests
- [ ] Add scheduler tests where practical
- [ ] Add page model tests where practical

### Deliverables
- automated test coverage for core logic

### Acceptance Check
- Tests pass locally
- Core behaviors are covered
- Invalid cases are explicitly tested

---

## Phase 13 - Publish and Deployment Validation
Objective: make sure the app is actually portable.

### Tasks
- [ ] Validate framework-dependent local run
- [ ] Validate self-contained publish
- [ ] Verify static assets are copied to publish output
- [ ] Verify app runs from published folder
- [ ] Confirm no database dependency exists
- [ ] Confirm no Node.js/npm/yarn dependency exists
- [ ] Optionally add Dockerfile
- [ ] Optionally test container run

### Deliverables
- runnable publish output
- deployment notes

### Acceptance Check
- App runs from published folder
- Local assets load correctly after publish
- No missing asset errors appear
- App remains functional without internet access for UI assets

---

## Phase 14 - GitHub Actions CI/CD
Objective: add repository automation for build, security scanning, and release packaging.

### Tasks
- [ ] Create `.github/workflows/ci-build.yml`
- [ ] Create `.github/workflows/sast-scan.yml`
- [ ] Create `.github/workflows/release.yml`
- [ ] Create `.github/dependabot.yml`
- [ ] Configure CI workflow triggers for:
  - [ ] push to `main`
  - [ ] push to `develop`
  - [ ] pull request to `main`
  - [ ] pull request to `develop`
- [ ] Configure CI steps:
  - [ ] checkout
  - [ ] setup .NET 8
  - [ ] restore
  - [ ] build Release
  - [ ] test Release
- [ ] Configure CodeQL SAST workflow
- [ ] Configure release workflow triggered by tag or GitHub release
- [ ] Publish self-contained artifacts
- [ ] Upload release assets
- [ ] Configure Dependabot for NuGet
- [ ] Confirm CI does not require public frontend CDNs
- [ ] Add workflow status notes to README if desired

### Deliverables
- working GitHub Actions workflows
- Dependabot config
- release artifact packaging automation

### Acceptance Check
- Push triggers CI successfully
- Pull request triggers CI successfully
- CodeQL runs successfully
- Release workflow packages artifacts successfully
- Dependabot config is valid

---

## 3. Cross-Cutting Rules

These rules apply throughout implementation.

### Dependency Rules
- [ ] Do not add any Xabaril package
- [ ] Do not add SPA frameworks
- [ ] Keep NuGet dependencies minimal
- [ ] Prefer built-in ASP.NET Core features where practical

### Frontend Rules
- [ ] No public CDN links
- [ ] No Google Fonts
- [ ] No internet-hosted scripts or styles
- [ ] All static assets stored locally

### Runtime Rules
- [ ] No database
- [ ] In-memory runtime state only
- [ ] No endpoint polling overlap for the same endpoint
- [ ] One endpoint failure must not impact others

### Security Rules
- [ ] Do not log secret header values
- [ ] Sanitize user-visible errors
- [ ] Support HTTPS endpoints
- [ ] CI must include SAST scanning

---

## 4. Developer Checklist by Component

## Configuration
- [ ] YAML models created
- [ ] YAML loader implemented
- [ ] validation implemented
- [ ] sample config added

## Parsing
- [ ] overall status parsing
- [ ] `entries` parsing
- [ ] nested parsing
- [ ] unknown field tolerance
- [ ] include/exclude filtering

## Polling
- [ ] HTTP poller implemented
- [ ] timeout support implemented
- [ ] custom headers implemented
- [ ] request duration captured

## Scheduling
- [ ] background service implemented
- [ ] independent endpoint loops
- [ ] overlap prevention
- [ ] graceful cancellation

## State
- [ ] endpoint state store implemented
- [ ] initialization from config
- [ ] update logic complete

## UI
- [ ] AdminLTE layout complete
- [ ] summary page complete
- [ ] details page complete
- [ ] nested tree rendering complete
- [ ] refresh actions wired

## Testing
- [ ] config tests
- [ ] parser tests
- [ ] poller tests
- [ ] filtering tests
- [ ] state tests

## CI/CD
- [ ] CI build workflow added
- [ ] CodeQL workflow added
- [ ] release workflow added
- [ ] Dependabot added
- [ ] release artifacts validated

## Deployment
- [ ] self-contained publish validated
- [ ] local assets validated after publish
- [ ] optional Docker validation

---

## 5. Suggested Milestones

## Milestone A - Foundation
Complete:
- Phase 1
- Phase 2
- Phase 3
- Phase 4

Outcome:
- app skeleton exists
- AdminLTE layout works
- YAML config loads
- runtime state exists

## Milestone B - Monitoring Core
Complete:
- Phase 5
- Phase 6
- Phase 7

Outcome:
- endpoints are polled
- health responses are parsed
- state updates automatically

## Milestone C - UI Completion
Complete:
- Phase 8
- Phase 9
- Phase 10

Outcome:
- dashboard is usable
- endpoint details page works
- manual refresh works

## Milestone D - Hardening
Complete:
- Phase 11
- Phase 12
- Phase 13
- Phase 14

Outcome:
- tests added
- logging improved
- publish verified
- CI/CD and SAST enabled

---

## 6. Suggested Ticket Breakdown

These can be used as small implementation tasks.

1. Create solution and Razor Pages scaffold
2. Add local AdminLTE shell layout
3. Add YAML config models
4. Implement YAML loader and validation
5. Build in-memory endpoint state store
6. Implement HTTP endpoint poller
7. Implement health payload parser
8. Implement include/exclude filter logic
9. Build scheduler background service
10. Add manual refresh actions
11. Build dashboard summary page
12. Build endpoint details page
13. Add logging and error handling
14. Add automated tests
15. Validate publish output
16. Add GitHub Actions workflows and Dependabot

---

## 7. Definition of Done

The feature set is done when all of the following are true:

- [ ] App starts successfully using YAML config
- [ ] Multiple endpoints are monitored concurrently
- [ ] Per-endpoint frequency works
- [ ] Nested health checks display correctly
- [ ] Manual refresh works
- [ ] No Xabaril dependency exists
- [ ] AdminLTE assets are local only
- [ ] No public CDN references exist
- [ ] No database is required
- [ ] Core automated tests pass
- [ ] Self-contained publish works
- [ ] GitHub Actions CI works
- [ ] CodeQL SAST works
- [ ] Release packaging works

---

## 8. Optional Nice-to-Have Tasks

These should not block v1.

- [ ] file-watch config reload
- [ ] lightweight readonly config page
- [ ] small status trend memory
- [ ] grouping endpoints by environment
- [ ] tag-based filtering
- [ ] export current dashboard state
- [ ] retry with backoff
- [ ] Dockerfile
- [ ] health status mini charts
- [ ] SBOM generation
- [ ] artifact signing
- [ ] CLI execution mode with machine-readable output
- [ ] per-endpoint priority support
- [ ] optional email sending via SMTP or an external API

---

## 9. Suggested Codex Usage

A practical way to use Codex is to implement one phase at a time.

Example sequence:
1. ask Codex to scaffold the project and base layout
2. ask Codex to implement YAML loading and validation
3. ask Codex to implement polling and parser
4. ask Codex to implement scheduler
5. ask Codex to implement pages
6. ask Codex to add tests
7. ask Codex to add GitHub Actions and release workflows

This usually produces cleaner outputs than asking for the entire system in one step.
