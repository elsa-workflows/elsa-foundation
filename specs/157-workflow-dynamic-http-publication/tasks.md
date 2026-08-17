# Tasks: Workflow-authored Dynamic HTTP Publication

## Phase 1 - Foundational contracts

- [x] T001 Add HTTP-core ownership/security metadata and immutable snapshot/lease contracts in `src/Elsa/Http/Core/`.
- [x] T002 Add owner-aware canonical route manifest validation in `src/Elsa/Http/Services/HttpRouteManifestValidator.cs`.

## Phase 2 - Publish and resolve

- [x] T003 [US1] Extend `HttpRouteData` with method and metadata snapshots and preserve legacy constructors in `src/Elsa/Http/Core/Models/HttpRouteData.cs`.
- [x] T004 [US1] Implement atomic candidate preparation, metadata enrichment, generation numbering, and rollback preservation in `src/Elsa/Http/Services/RouteTable.cs`.
- [x] T005 [US1] Resolve workflow methods and authorization options into route metadata in `src/Elsa/Workflows/Runtime/Http/Services/HttpEndpointRoutesResolver.cs`.

## Phase 3 - Exact request generation

- [x] T006 [US3] Lease one route snapshot through middleware matching and dispatch in `src/Elsa/Activities/Http/Middleware/HttpEndpointMiddleware.cs`.
- [x] T007 [US3] Add race, replacement, and drain tests in `tests/Elsa/Http/Tests/DynamicHttpRoutePublicationTests.cs` and affected middleware tests.

## Phase 4 - Security and lifecycle evidence

- [x] T008 [US2] Add collision tests for equivalent templates, method overlap, and host/module/dynamic owner diagnostics.
- [x] T009 [US4] Add resolver security metadata, compatibility, and collectible metadata evidence tests, including real CShells reload cycles.
- [x] T010 Run affected test projects, architecture/map checks, and perform a categorized self-review; mark this task file complete.

## Dependencies

T001 -> T002/T003 -> T004/T005 -> T006 -> T007/T008/T009 -> T010.
