# Activity data selection REST proof

This owned-host journey exercises real catalog metadata and server-returned member bindings through draft save/reopen, publication, process restart and execution. It does not attach to a developer's server or share its stores. The test producer is explicitly imported into Workbench for this run; ordinary builds and MemberReads defaults remain unchanged.

The owner selected explicit workflow inputs on14Nov. This source removes the undeclared-request option and qualifies four runtime cases plus undeclared whole/member refusal. Exact-head qualification and independent review are required before claiming the lane passes.

Build and run from the repository root with Python 3 and **PowerShell 7.3+ (`pwsh`)** available. Keep the normal `dotnet` build-slot wrapper on PATH. This folder is an explicit exception to the general Windows PowerShell 5.1 invocation: `ConvertFrom-Json -AsHashtable` preserves empty literal property names, and 7.3+ preserves ordered JSON dictionaries for stable fingerprint comparison; the refusal probe also uses `-SkipHttpErrorCheck`. All entry scripts enforce this with `#requires`. Windows PowerShell 5.1 is unsupported for this lane; Windows execution has not been qualified. Local exploratory checks used the official portable macOS ARM64 PowerShell 7.7.0 release; tool provenance/hash is retained with the private proof receipts. No global installation or common runner change is required.

```sh
DOTNET_PROCESSOR_COUNT=2 dotnet build Essentials/apps/Elsa.Workbench/Elsa.Workbench.csproj \
  -p:RestoreLockedMode=true -m:1 /nr:false \
  -p:CustomAfterMicrosoftCommonTargets="$PWD/e2e-tests/activity-data-selection/fixtures/fixture.targets"
python3 -m unittest discover -s e2e-tests/activity-data-selection -p 'test_*.py' -v
pwsh -NoProfile -File e2e-tests/activity-data-selection/Test-ExitGuard.ps1 \
  -EvidenceDirectory /absolute/path/to/exit-guard-evidence
pwsh -NoProfile -File e2e-tests/Test-ServerLifecycleGuard.ps1
pwsh -NoProfile -File e2e-tests/activity-data-selection/Test-ActivityDataSelection.ps1 \
  -EvidenceDirectory /absolute/path/to/new-evidence
pwsh -NoProfile -File e2e-tests/activity-data-selection/Test-ReceiptGuards.ps1 \
  -EvidenceDirectory /absolute/path/to/new-evidence
```

Use a new evidence directory for every attempt. `-BaseUrl` may name a free loopback port; omitting it allocates one. `-AssemblyPath` and `-Python` select already-built Workbench and Python executables. Failed runs preserve their owned content root and logs. Cleanup stops only the process this journey launched. Rebuild Workbench **without** the import after qualification to remove the fixture from local binaries.

The four runtime cases prove a known typed Customer result, nearest container variable and explicitly selected outer workflow variable with different sentinel values, a declared request whose name differs from its reference key. Dynamic cases retain the literal path `["a.b", ""]`. Each stores the exact canonical server offer and checks committed consumer Text after restart; only the result case has a producer, which must complete exactly once. The restart occurs after save/publication and before execution, and does not prove mid-execution recovery.

Historical exploratory qualification on merged S3 base `06d619756c39bdcf99a626ce1fcfee373e909283` plus uncommitted proof files observed the four runtime cases and exposed an open-request contradiction: metadata/publication admitted it, while execution rejected undeclared start inputs. The14Nov owner decision preserves the closed v1 input contract. The permanent journey now asserts no undeclared root discovery, exact saved unsupported whole/member values, actual snapshot publication compiler refusal (`activity-data.invalid` / `ActivityData/UnknownInput`), promotion refusal, and unchanged artifact/reference/publication/activation fingerprints. It does not bypass the invalid-draft promotion gate to manufacture a version. Owning publication tests additionally exercise stored-version refusal. `Test-ReceiptGuards.ps1` requires all four runtime cases and both refusal receipts; old failed five-case exploratory receipts are not relabeled passes.

The known Customer object→String refusal must come from the actual compiler (`VF-COER-001`). A read-only SQLite transaction includes committed WAL rows and fingerprints all columns of this workflow's artifacts, source references, publications and activation slots. A prior successful publication provides a positive control for every filter. Artifacts are scoped by the encoded unique `rootActivity.authoredActivityId`, including orphan artifacts; references/publications/slots use the encoded definition ID. Missing/ambiguous schemas fail closed. Other workflows and diagnostics are excluded. The retained-receipt controls reject a wrong committed value and a changed fingerprint with unchanged row counts; helper tests exercise actual SQLite mutation and write refusal.

This is the workflow backend portion of spec1215 T024. Studio permanent journeys/capture inventory (T025), reusable `InputMember` and current item surfaces (T041–T043), placed-body/result pinning (#4163/#4164), and producer→consumer persistent recovery (T038–T040) remain separate work. Design-time type fit remains deferred to #4311. No UI proof is implied by this REST script; S3 has its own composed-host evidence.

`Test-ExitGuard.ps1` runs the shared final gate in real child PowerShell processes: success exits zero; a final integrity refusal exits nonzero after retaining the cleanup receipt. This tests the command exit boundary, separately from the owned-host journey.
