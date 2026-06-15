# Tasks: Shell Feature Management

**Input**: `specs/072-shell-feature-management/spec.md`

## Phase 1: Setup

- [x] T001 Create `codex/shell-feature-management` branches in backend and Studio repos.
- [x] T002 Add Speckit spec, plan, and task artifacts under `specs/072-shell-feature-management`.

## Phase 2: Backend Foundation

- [x] T003 Create `Elsa.Modularity.Core`, `Elsa.Modularity.Nuplane`, and `Elsa.Modularity.Api` projects.
- [x] T004 Define feature catalog/apply DTOs and service contracts in `Elsa.Modularity.Core`.
- [x] T005 Implement Nuplane/runtime catalog discovery and package manifest parsing in `Elsa.Modularity.Nuplane`.
- [x] T006 Implement JSON shell configuration read/write, revision handling, and apply behavior.
- [x] T007 Add FastEndpoints list/apply endpoints and `ModularityApiFeature`.

## Phase 3: Backend Validation And Wiring

- [x] T008 Add backend unit tests for manifest parsing, catalog merge, setting conversion, revision conflicts, shell reload invocation, and feature registration.
- [x] T009 Add `EXTENSION_POINTS.md` for the Modularity domain.
- [x] T010 Wire `ModularityApiFeature` into `src/apps/Elsa.Server/Program.cs`.
- [x] T011 Refresh generated maps after project additions.

## Phase 4: Studio Foundation

- [x] T012 Extend the Studio SDK with a setting-editor contribution registry and built-in editor helpers.
- [x] T013 Create `Elsa.Studio.FeatureManagement` project, feature, service registration, manifest contributor, Vite client, and styles.
- [x] T014 Register `/features` navigation and route using `api.backend.http`.

## Phase 5: Studio Validation

- [x] T015 Add Studio manifest tests and frontend Vitest coverage for editor selection, dirty state, apply payload, conflicts, and successful refresh.
- [x] T016 Wire the module into `Elsa.Studio.Web`.

## Phase 6: Final Validation

- [x] T017 Run focused backend builds/tests.
- [x] T018 Run focused Studio builds/tests.
- [x] T019 Review diff, update task checkboxes, and commit backend and Studio changes.
