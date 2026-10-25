# Validation sequence

Run from the repository root. These are planned checks, not receipts.

1. Run the legacy capture tests before modifying pins or deleting the writer. Assert each required committed boundary, queue order and raw v1 fields, then commit fixtures with provenance. An opt-in regeneration path must not run during ordinary tests.
2. Reload the captured store with the old host as a control, then a fresh upgraded host. Deliver the recorded completion/fault/bookmark work and verify exact state, incident and parent outcome identities. Include negative missing/ambiguous-correlation controls.
3. Run the entire affected projects, separately:

```sh
dotnet test Essentials/tests/Activities/Bpmn/Tests/Elsa.Activities.Bpmn.Tests.csproj -c Release
dotnet test Essentials/tests/Activities/Bpmn/Interchange/Tests/Elsa.Activities.Bpmn.Interchange.Tests.csproj -c Release
```

Compare discovery, execution and skipped counts. Run the affected EF native-composite/combined-outcome projects with their normal required-provider setup; list exact commands and fixtures in the evidence receipt.

4. In the real source-built Workbench/Studio, import an existing document, verify the default edge, edit, publish, execute and export. Exercise binder registration through normal APIs. Preserve screenshots/results bound to the tested head; route mocks do not replace this proof.
5. Compare the frozen descriptor fingerprint and historical test lists without restamping. Mutation-test the deletion guard and risky compatibility/combined-outcome behavior.
6. Run architecture/maps and locked solution restore where required, plus affected Studio whole suites. Use the repository build-slot wrapper and check machine load before interpreting timing failures.
7. Complete draft review, required exact-head checks and resulting-main verification. Only then continue to the ADR acceptance PR. Keep #2898/#2907 and all unproved program acceptance open.
