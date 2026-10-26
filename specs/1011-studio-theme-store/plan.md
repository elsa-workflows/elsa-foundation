# Implementation Plan: Reusable Studio Theme Store

**Branch**: `agent/codex/3360-studio-theme-store` | **Date**: 2026-10-26 | **Spec**: [spec.md](spec.md)

## Summary

Extract the existing server theme capability into optional Elsa.Studio.Hosting.Themes using the package/API boundaries already settled by #3355. Keep the existing three feature identities, routes, JSON appearance format, host policy and frontend validator. Separate managed persistence from directory-enriched read state before adopting the library in Web. Include the accepted #3380 persistence, empty-selection and startup-validation follow-ups. #3359 remains stopped on its saved branch.

## Technical Context

**Language/Version**: C# / .NET10, TypeScript/React/Vite/pnpm.
**Primary Dependencies**: existing Hosting/Api/Core, CShells public configuration/sharing APIs, ASP.NET Core, System.Text.Json. No Web, Nuplane or backend-domain reference.
**Storage**: application-owned JSON store and material-asset folder; optional JSON collection file/directory as an independent source.
**Testing**: public-host HTTP xUnit/TestServer, existing appearance/frontend unit suites and runtime browser journeys; affected whole .NET project and architecture checks.
**Target Platform**: existing supported .NET hosting platforms; local qualification on macOS and CI on its existing matrix.
**Project Type**: optional plain SDK library, no independent browser payload.
**Performance Goals**: retain existing request behavior and 2MiB asset cap; no new latency/concurrency claims.
**Constraints**: one implementation leaf; frozen installed options; source precedence; no accidental data adoption or legacy deletion; atomic coordinated local writes; existing authorization.
**Scale/Scope**: #3360 extraction plus its accepted review follow-ups. Final cross-package external-consumer combinations are #3364.

## Constitution Check

Pre-design and post-design: PASS. Framework6.1.0 and Elsa6.3.0 (as currently pinned) govern the work. ADR0080 favors package consolidation, internal defaults and risk-based verification. Public surface is limited to composition/options and extensible shell features; JSON endpoint DTOs and implementation helpers remain private/internal. No Studio production friend is introduced. Feature types are public/nonsealed with virtual composition methods. No new Core/helper split, event, extension point, scheduler, persistence backend or general file framework is needed. Host authorization comes from Api's existing public policy contract; whole mutations use Hosting's existing public gate. Versions remain derived. Required PR/exact-head/review/main gates remain mandatory. Existing unit/integration test tier classification is retained; no new test project.

## Project Structure

- `Studio/src/essentials/Elsa.Studio.Hosting.Themes/`: composition/options/tracked public API, private directory/store/API/model/writer implementation, README.
- `Studio/src/apps/Elsa.Studio.Web/`: replace theme registration with the public addon call; remove moved implementations and update assembly roster.
- `Studio/tests/Elsa.Studio.Tests/`: public-host theme test fixtures and JSON test models; no internal production access.
- Existing Web Client theme files: retain ownership until #3361 moves the whole frontend; adjust only source/default/selection behavior and tests here.
- `specs/1011-studio-theme-store/`: spec, plan, research, data model, contract, quickstart, checklist and tasks.

## Design

### Composition and installed options

Public `StudioThemeStoreOptions` retains ThemesDirectory, ThemesMode and DefaultThemeId from the Themes section and adds StoreFile (`studio-theme-store.json`) and AssetsDirectory (`theme-assets`). `AddElsaStudioThemes(this StudioBuilder, Action<StudioThemeStoreOptions>?)` binds first, invokes callbacks second, registers normal options validation with ValidateOnStart, root-owned private settings/directory source, and the feature assembly. Guard duplicate/after-Build composition. ThemesMode uses the approved public StudioThemeSourceMode enum for callbacks; the legacy named configuration strings are parsed explicitly so numeric strings remain invalid. Relative paths use ContentRoot. Store/asset paths must be nonblank, parseable and of the expected existing file/directory shape; invalid source modes fail startup. Applied settings copy the validated options so retained callback mutation cannot redirect storage. Source file contents remain read per request.

Borrow only required root-owned singleton settings/directory source through CShells. All writers acquire the exact installed StudioConfigurationMutationGate across read/modify/atomic replacement. Feature-local scopes do not own/dispose root services. Core, picker and management stay independently selected by the existing feature IDs; picker/management depend on core. They map the existing routes using StudioBridgeAuthorization.PolicyName. No separate public Map API is necessary.

### Managed state and read projection

Use an internal managed state/document containing original managed definitions, stored default and disabled built-in IDs. Preserve existing JSON field names and tolerate legacy metadata; derived assets, source policy and directory provenance never become persistence authority. Build a separate read snapshot/view by merging directory definitions over managed definitions, computing provenance, applying configured-default precedence, and finally projecting default availability under the source mode. Keep complete management inventory even when themes are excluded from selection.

Every mutation starts from a fresh snapshot under the gate. Save/import explicitly replace only the submitted managed IDs; duplicate explicitly creates its one new managed definition. Visibility/default/asset changes preserve all original managed definitions, including those shadowed by the directory. A directory definition is copied only by an explicit operation choosing to create a managed definition, never by writing a merged read view. Existing legacy managed copies are preserved because their intent cannot be inferred.

A source-owned change that cannot be persisted (for example deleting an unmanaged directory definition while it is still supplied externally) returns a clear conflict/refusal. Never report deletion while the next read immediately restores the same source definition. Directory files are not rewritten through management routes. Keep the existing default/visibility/appearance validation and deliberately qualify overlapping IDs and directory removal.

### Files, assets and browser behavior

Keep private sibling-temp atomic JSON writing (allowed by the existing hosting contract); no general public file writer. Retain 2MiB PNG/WebP/JPEG upload rules, safe IDs and route URLs. Stage uploads and clean incomplete files on cancellation/error; publish complete files only. Configured asset/source/store locations are independent host-owned paths; mutable package/control roots and concurrent external process writers remain outside the qualified profile.

Use existing server and browser validation rules and test unsafe values at both boundaries. ThemeBuilder deletion chooses an existing inventory entry when the projected default is empty, otherwise a coherent empty state. Appearance fallback may display a built-in safety appearance without claiming it is selectable or a valid configured default under DirectoryOnly. Preserve explicit invalid-mode failure.

## Verification and Delivery

Migrate existing internal-type tests to public composition/HTTP with test-owned JSON models. Recreate hosts when installed options change; do not rely on mutating a private service. Add feature-combination/auth tests, nondefault path/content-root tests, no-copy/directory-removal/shadow-preservation tests, concurrency/import/export/assets/cancellation cases and retained-options validation. Run whole affected Studio tests with discovery accounting, targeted risk-based causal controls, affected frontend suites, designer typecheck/lint/build and runtime journeys. Validate locks, solution/maps/architecture, actual candidate package closure and reference Web smoke. Independent correctness/standards/acceptance review is required at the draft PR, then exact-head CI/external review and actual resulting-main verification. Do not push before opening that PR in the same session. No releases/deploys/publisher activation.

No mandatory application-level TDD cadence is declared for this extraction; use risk-based behavior tests and causal controls. Optional automatic commit hooks are skipped in favor of explicit coherent checkpoints; the mandatory branch hook ran once. No implementation is delegated.
