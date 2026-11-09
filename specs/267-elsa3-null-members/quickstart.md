# Quickstart: verify spec 267

Build and test only the touched projects (shared machine).

```bash
dotnet test Extensions/Elsa3/tests/Mapping/Tests/Elsa3.Mapping.Tests.csproj --filter "FullyQualifiedName~Elsa3NullMember|FullyQualifiedName~Elsa3ActivityMapCoverage"
```

```bash
dotnet test Extensions/Elsa3/tests/Activities/Design/Import/Integration/Tests/Elsa3.Activities.Design.Import.Integration.Tests.csproj --filter "FullyQualifiedName~NullMemberPublishTests"
```

Expected:
- Each row of the spec's per-member table converts as stated with the member `null` (and with the deliberate-null
  shape).
- The coverage test passes: every Carry and Narrowed-contract input records its null conversion, and the real catalog
  agrees with each typed-null and no-binding row.
- The integration test imports, publishes and runs a workflow with `null` members, and the run completes as in Elsa 3.

Corpus check: the throwaway measurement (research R-11) shows composition-bulk-dispatch-sub-workflow and
performance-bigdata past the `possibleOutcomes` error, and no definition that converted before stops converting.
