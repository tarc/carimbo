---
phase: 01-walking-skeleton
plan: 02
subsystem: infra
tags: [dotnet, net10, central-package-management, slnx, system-text-json, domain-records, money]

requires:
  - phase: 01-walking-skeleton
    provides: "none (wave 1, no dependencies; 01-01 owns .editorconfig, .gitignore and python/)"
provides:
  - "Root global.json pinning SDK 10.0.100 (latestFeature) with the Microsoft.Testing.Platform opt-in for dotnet test"
  - "dotnet/Carimbo.slnx building under warnings-as-errors with Carimbo.Domain, Carimbo.Llm and Carimbo.Extraction"
  - "Central Package Management with exact pins (Anthropic 12.53.0, xunit.v3 4.0.1, Mvc.Testing 10.0.12)"
  - "Pure Domain records Invoice, Party, Money, Decision/DecisionOutcome and Patterns (BCL only, no references)"
  - "Wire.Options: the single JsonSerializerOptions for schema export and model-output parsing"
affects: [01-03, 01-04, 01-05, 01-12, 01-13, phase-02-validators]

actuals:
  tokens: 1825
  tasks: 2
  commits: 2
plan_head_before: cf346021293ca6dafd0070e1d641de21d1c78e00
plan_head_after: 85238e09869767a01102850ff15219e078ab95c9

tech-stack:
  added: []
  patterns:
    - "One explicit JsonSerializerOptions (snake_case, string enums, required ctor params, Disallow unmapped) shared by export and parse"
    - "Money on the wire is a two-decimal invariant string; pattern check precedes decimal.Parse"
    - "Explicit [0-9] digit classes in every pattern (never the digit shorthand)"

key-files:
  created:
    - global.json
    - dotnet/Directory.Build.props
    - dotnet/Directory.Packages.props
    - dotnet/Carimbo.slnx
    - dotnet/src/Carimbo.Domain/Carimbo.Domain.csproj
    - dotnet/src/Carimbo.Domain/Invoice.cs
    - dotnet/src/Carimbo.Domain/Wire.cs
    - dotnet/src/Carimbo.Llm/Carimbo.Llm.csproj
    - dotnet/src/Carimbo.Extraction/Carimbo.Extraction.csproj
  modified: []

key-decisions:
  - "global.json lives at the repo root so the SDK pin and test-runner opt-in apply to every command run from the root (research Open Question 6)"
  - "Number and Series are int in Phase 1 (research Open Question 2), graded by value"
  - "Money.Parse validates Patterns.Money before decimal.Parse with AllowLeadingSign | AllowDecimalPoint, so 12,34 can never be read as 1234"
  - "Wire.Options is built explicitly, never from JsonSerializerDefaults.Web, to keep the exported schema free of string-or-number unions"

patterns-established:
  - "Domain stays pure: no PackageReference or ProjectReference, BCL namespaces only"
  - "Project csproj files are minimal; shared properties live in dotnet/Directory.Build.props"

requirements-completed: [REPO-01, DOM-01]

coverage:
  - id: D1
    description: "The .NET solution builds with warnings as errors from the root global.json SDK selection"
    requirement: REPO-01
    verification:
      - kind: other
        ref: "nix shell nixpkgs#dotnet-sdk_10 -c dotnet build dotnet/Carimbo.slnx -warnaserror (0 warnings, 0 errors)"
        status: pass
    human_judgment: false
  - id: D2
    description: "Domain records compile with no package or project references and use only System namespaces"
    requirement: DOM-01
    verification:
      - kind: other
        ref: "grep -c '<PackageReference|<ProjectReference' Carimbo.Domain.csproj prints 0; source usings all start with System"
        status: pass
    human_judgment: false
  - id: D3
    description: "Wire.Options serializes snake_case JSON, string enums and two-decimal money strings, and rejects unknown, missing and null members; pt-BR amounts are rejected"
    requirement: DOM-01
    verification:
      - kind: other
        ref: "scratch console spike (not committed) against the built Carimbo.Domain.dll: comma, number token, one decimal, trailing newline, unknown, missing and null party all raised JsonException"
        status: pass
    human_judgment: true
    rationale: "The permanent unit tests for the wire conventions and Money land in plan 01-04; until then the evidence is an uncommitted spike, so the verifier should confirm after 01-04."

duration: 3min
completed: 2026-10-05
status: complete
---

# Phase 1 Plan 02: .NET solution and pure Domain records Summary

**.NET 10 solution under Central Package Management with a root global.json (MTP opt-in), pure Invoice/Party/Money/Decision records, and one Wire.Options object (snake_case, string enums, two-decimal money strings, strict member handling)**

## Performance

- **Duration:** 3 min
- **Started:** 2026-10-05T17:26:49Z
- **Completed:** 2026-10-05T17:29:18Z
- **Tasks:** 2
- **Files modified:** 9 (all created)

## Accomplishments
- Solution scaffold in the D-12 layout: `dotnet/Carimbo.slnx` with Domain (no references), Llm (no references yet) and Extraction (references Domain and Llm), building clean under `-warnaserror`.
- `global.json` at the repo root with `rollForward: latestFeature` and the `Microsoft.Testing.Platform` runner opt-in, so xUnit v3 works with `dotnet test` on the .NET 10 SDK.
- Exact package pins only (no ranges or wildcards), and none of the packages research lists under "Not needed in Phase 1".
- Domain records with the D-01 Invoice subset, Party, the D-02 Decision record (unreachable from Invoice, so it never enters the extraction schema), and Patterns using explicit `[0-9]` classes.
- `Money` closes the pt-BR hole (research Pitfall 4): pattern check first, then restricted-NumberStyles invariant parse; `Wire.Options` carries every conversion rule in one read-only object.

## Task Commits

Each task was committed atomically:

1. **Task 1: Solution scaffold with root global.json, Central Package Management and three source projects** - `0b61d14` (feat)
2. **Task 2: Pure Phase 1 Domain records and the single wire-options object** - `85238e0` (feat)

**Plan metadata:** recorded in the following docs commit (SUMMARY) and the STATE/ROADMAP commit.

## Files Created/Modified
- `global.json` - SDK pin 10.0.100 with latestFeature roll-forward and the Microsoft.Testing.Platform runner opt-in
- `dotnet/Directory.Build.props` - net10.0, nullable, implicit usings, warnings as errors, deterministic, invariant globalization, CI build flag
- `dotnet/Directory.Packages.props` - Central Package Management with exact pins for Anthropic, xunit.v3, Mvc.Testing
- `dotnet/Carimbo.slnx` - solution holding the three source projects
- `dotnet/src/Carimbo.Domain/Carimbo.Domain.csproj` - empty project, no references (D-04)
- `dotnet/src/Carimbo.Domain/Invoice.cs` - Patterns, Invoice, Party, DecisionOutcome, Decision
- `dotnet/src/Carimbo.Domain/Wire.cs` - Money, MoneyJsonConverter, Wire.Options
- `dotnet/src/Carimbo.Llm/Carimbo.Llm.csproj` - domain-agnostic, no references (provider package arrives in 01-12)
- `dotnet/src/Carimbo.Extraction/Carimbo.Extraction.csproj` - ProjectReference to Domain and Llm

## Decisions Made
- Followed the plan's resolved decisions: root `global.json`, `int` for Number/Series, explicit options instead of the web defaults preset.
- Rewrote the `dotnet new classlib` csproj output to be minimal (no BOM, no duplicated properties) so `dotnet/Directory.Build.props` is the single source of shared settings.

## Deviations from Plan

### Acceptance-criterion mismatch (documented, not a code change)

**1. [Plan defect] Single-line `Invoice` signature grep cannot match an attributed record**
- **Found during:** Task 2 (acceptance_criteria)
- **Issue:** The criterion `grep -c 'public sealed record Invoice(string AccessKey, int Number, ...)' Invoice.cs` prints 1 only if the whole positional record sits on one line with no attribute before `string AccessKey`. The same task requires `[property: Description]` and `[property: RegularExpression]` on `AccessKey`, which must sit on that parameter, so the two requirements conflict textually. A positional record cannot carry those property attributes elsewhere.
- **Resolution:** Kept the attributes (they drive the exported schema's `description` and `pattern`). The record has exactly the specified members in the specified order: `AccessKey, Number, Series, IssueDate, Issuer, Recipient, TotalAmount` with the specified types. The grep as written prints 0.
- **Files modified:** none beyond the planned ones
- **Verification:** Read Invoice.cs; the build succeeds; the spike round-tripped an Invoice through `Wire.Options` and the field names are `access_key`, `number`, `series`, `issue_date`, `issuer`, `recipient`, `total_amount`.
- **Impact:** Plan 01-04's schema snapshot test is the durable check for the record shape. The planner may want to loosen that grep.

---

**Total deviations:** 0 auto-fixed, 1 plan-defect note (acceptance grep vs required attributes)
**Impact on plan:** No scope change.

## Issues Encountered
- `grep -rhoE '^using ...' dotnet/src/Carimbo.Domain` also scans `bin/` and `obj/` (git-ignored build output) and lists generated `using System.Reflection`. Every namespace still starts with `System`; the source files use only BCL namespaces.
- First `dotnet` invocation printed the ASP.NET HTTPS dev certificate message. That is a one-time SDK first-run banner and changed nothing in the repo.

## Verification Results
- `dotnet build dotnet/Carimbo.slnx -warnaserror`: 0 warnings, 0 errors
- `dotnet sln dotnet/Carimbo.slnx list`: Domain, Llm, Extraction listed
- `dotnet format dotnet/Carimbo.slnx --verify-no-changes`: exit 0 (editor config from 01-01 present)
- Acceptance greps for Task 1 all pass (global.json runner and rollForward, one `Version="12.53.0"` line, no `*`/`[`/`(` in PackageVersion lines, zero references in Domain csproj, Extraction references Domain, no Class1.cs)
- Acceptance greps for Task 2 pass except the single-line signature grep noted above
- Behavior spike (scratch, not committed): `12,34`, a JSON number token, `1234.5`, `1234.50\n`, an unknown member, a missing member and a null party all raise `JsonException`; `"1234.50"` round-trips; `Decision` serializes as `{"outcome":"escalate","reason":"r"}`

## User Setup Required

None - no external service configuration required.

## Known Stubs

None.

## Threat Flags

None - no new network endpoints, auth paths or trust-boundary surface beyond the plan's threat model (T-01-SC, T-01-03, T-01-04 mitigations are implemented as specified).

## Next Phase Readiness
- 01-03 can build the endpoint and extractor on `Carimbo.Domain` records and `Wire.Options`.
- 01-04 adds the Domain unit tests, the schema exporter and the schema snapshot; it owns the permanent tests for Money and the wire conventions.
- `dotnet`/`just` are not on PATH here; every .NET command in later plans needs the `nix shell nixpkgs#dotnet-sdk_10 -c` prefix. No GC root was created (`nix shell` only); NuGet restores landed in `~/.nuget`.

## Self-Check: PASSED

All nine created files exist on disk; commits `0b61d14` and `85238e0` are ancestors of HEAD; `git rev-list --count` from the recorded base gives 2 commits.

---
*Phase: 01-walking-skeleton*
*Completed: 2026-10-05*
