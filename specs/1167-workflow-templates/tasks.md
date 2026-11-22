# Tasks
- [x] T001 Verify healthy main, claim #3456, specify and review plan.
- [x] T002 Add Design template document/store, EF table/atomic persistence and provider migrations, API/capabilities and regression tests.
- [x] T003 Add Studio catalog/types and shared designer document adapter; management, save-as-template and create-from-template.
- [x] T004 Add rendered browser and owned backend restart/independence tests; run affected gates and mutation proof.
- [x] T005 Review diff, commit/push a PR, attach it and publish exact evidence.

## Built-in starters amendment — staged delivery

The following tasks describe future implementation work; none is complete as part of this documentation amendment. Execute in order. Each task cites the requirements it delivers or verifies.

### Server catalog

- [ ] T006 Define the internal starter recipe and metadata model plus the public module contribution contract; include stable id, owner, required activity types/languages, authored content, type-based activity references and placeholder node ids. (FR-009, FR-010, FR-012)
- [ ] T007 Add host-level `Workflows:Design:Templates:RemovedStarters` options and filter behavior; ignore unknown ids with one startup diagnostic and cover removal of one, unknown ids and all starters. Keep Blank outside server removal. (FR-011, FR-015)
- [ ] T008 Resolve each recipe against the server activity catalog on use; choose the newest available activity version, assign fresh node ids and remap references. Return actionable unavailability reasons and refuse incomplete creation when required activities or expression languages are missing. (FR-012, FR-013)
- [ ] T009 Add the read-only built-in list endpoint and its Design capability link without changing custom template CRUD. Verify built-ins cannot be updated/deleted through the API and can still be copied to custom templates. (FR-009, FR-014)

### Recipe and Studio integration

- [ ] T010 Implement and register the binding-readonly Liquid `parse_json` filter and supported `ILiquidFilter` implementations; verify array iteration, malformed JSON failure and HTML escaping for the Backend API Call body. (FR-017, FR-018)
- [ ] T011 Add the module-owned Hello World Console, Hello World HTTP (`http`), Backend API Call, Scheduled job, Dispatch child workflow and BPMN process recipes; verify each graph and its required activities/languages. Use a local HTTP stub for Backend API Call tests. (FR-010, FR-017)
- [ ] T012 Update Studio to consume the advertised server list in Templates and Starting point, reflect unavailable/removed states, preserve client-side Blank with root-activity choice, keep built-ins read-only, and use the existing client HTTP recipe when the capability is absent. (FR-008, FR-011, FR-014, FR-015, FR-016)
- [ ] T013 *(P3)* Add Studio export of a template as a module-embeddable starter recipe with activity-type references, no pinned versions and placeholder node ids. (FR-019)

### Integration and acceptance

- [ ] T014 Add server-to-Studio integration coverage for enabled/disabled owner modules, missing activity/language, removed/unknown/all-removed ids, fresh ids/newest versions, built-in read-only/copy behavior, capability-absent fallback, and upstream failure outcomes. (FR-009–FR-017)
- [ ] T015 Record the visible Studio goal, reference and observable states, run composed-host UX proof, retain current-commit downloadable evidence, and obtain independent UX acceptance for the changed starter journeys. (FR-003, FR-006, FR-008, FR-011, FR-015, FR-016)

### Requirement-to-task traceability

| Requirement | Tasks |
|---|---|
| FR-009 | T006, T009, T014 |
| FR-010 | T006, T011, T014 |
| FR-011 | T007, T012, T014 |
| FR-012 | T006, T008, T011, T014 |
| FR-013 | T008, T014 |
| FR-014 | T009, T012, T014 |
| FR-015 | T007, T012, T014 |
| FR-016 | T012, T014 |
| FR-017 | T010, T011, T014 |
| FR-018 | T010, T014 |
| FR-019 | T013 |
