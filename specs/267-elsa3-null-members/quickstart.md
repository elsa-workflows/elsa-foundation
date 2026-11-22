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

## #3281 follow-up verification

The follow-up is implemented separately. Filtered commands above exercise the original delivered unit. Run both
whole affected projects for #3281, including `Elsa3MemberDefaultParityTests` and `MemberDefaultParityTests`; the
fresh result/limitations are in spec.md's follow-up local validation receipt.

Cover 200/404/500, cleared-list refusal/no writes/no request, absent-versus-blank ContentType with empty/nonempty
String Content, For boundary counts, Delay zero, effective GET and Timer/status no-default controls.

Run both complete affected projects with discovery comparison, correction-removal red/green, Release/Debug graph
restores, whole Architecture and generated-filter check. Use isolated local/Linux validation with shared-machine
build limits. Run fresh measurements from the authorized read-only corpus; neither old tables nor #3201 totals prove
#3281. Preserve every failed/not-run row.

Keep implementation draft through independent review/fix/review until clean. CI waits are deferred to initiative
integration; organizational Code Review and coordinator merge remain gates. S-2 policy-on/off integration is tested
when its implementation exists and otherwise remains NOT RUN.
