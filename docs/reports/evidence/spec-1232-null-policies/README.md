# Spec 1232 local delivery evidence

S-2 implements the approved [spec](../../../../specs/1232-null-missing-undefined/spec.md) in
[#5549](https://github.com/elsa-workflows/elsa-foundation/pull/5549), against actual initiative base
`79ada540ac7fcc80db354ef3bef7fe6c0c7e17a2`. The proposal merged in #3726; this is the separate implementation.

The clean composed source and PR head tested here is **4f3416436711e97fe1ca6b70f078c4c241efd1d2**. Production source last changed in
`0163b4791dbfe852a1d077ae58c3969d373d7a23`; later commits add tests, acceptance coverage and evidence. A subsequent
delivery-record commit changes only documentation and these artifacts. The PR gate records its final head and
verifies that production, tests and the journey/runner remain identical to the tested revision. Reports retain their
actual source SHA; no report was rewritten to claim a different build or binary identity.

## Downloadable composed proof

Download **both** parts; each contains three of the six required appearances.

| Archive | Bytes | SHA-256 |
|---|---:|---|
| [Part 1](studio-null-policies-part-1.zip) | 42299318 | `916b668adbb133b4e4ec32614c20c4ab2dfb65ae0ef4346bc52cc2311746aed4` |
| [Part 2](studio-null-policies-part-2.zip) | 42389605 | `b453d482d7e94b4cd51843075c4036a7260375c283995c9ae69e7ecb95589927` |

The collection contains **258 named PNGs**, their **258 accessibility snapshots**, **six business-proof records**,
six shared trace copies, and unchanged source-bound `report.json` / `playwright.json`. Each part's `manifest.json`
records its exact project inventory, file hashes and trace transformations. Open the PNGs and `.aria.yml` directly;
use `pnpm exec playwright show-trace results/<project>/trace.zip` for actions, DOM and network evidence.

Shared trace copies redact actual owned-host credentials and omit continuous screencast events and their otherwise
unreferenced JPEG resources. Named captures, accessibility snapshots, proofs and reports are unchanged. Full original
traces and continuous filmstrips are retained privately and are **not** claimed as shared artifacts. The manifest
records original/shared hashes, every omitted frame/resource and credential replacement locations. Independent actual
archive inspection found **zero remaining privacy or integrity findings** across both parts, including cross-part
credential scans, JSON parsing, exact byte/hash comparisons and retained resource references.

The audit's initial non-overlapping matcher failed synthetic overlap/prefix controls. The corrected matcher passes
raw/URL/JSON and location controls and scans the complete final collection. An unfinished slower comparison remains
incomplete; it is not counted as a pass. Failed control attempts are retained with their corrections.

This is local composed-source evidence. It is not Actions evidence or a CI result.

## Acceptance and review

**UX acceptance: passed**, scoped to the [43-state S-2 journey](../../../../specs/1232-null-missing-undefined/ux-acceptance.md)
at the tested source above. All six desktop/360/390 light/dark executions pass without retries, omissions or skips;
both owned hosts exit zero and the temporary root is removed. The independent acceptance/spec agent opened all 258
named images and their paired snapshots, inspected the six proofs/traces and critical native-size images, and found
zero remaining verified usability or coverage findings after the capture correction.

Observed evidence includes empty activity Inputs/Outputs and an empty workflow Test Run; populated workflow/activity
contracts; saved type changes; Required/default recovery and cancellation; nullable omission; required and invalid-null
refusal/correction; Any/Boolean controls; actual Add output/save wire behavior; explicit null versus text `"null"` and
empty string; exact published-contract metadata loading/refusal/recovery; authored historical/read-only and C# metadata;
and readable ordinary/protected boundary warnings. Controlled held/aborted metadata reads qualify that transport UX
only. Successful admission and execution use real owned-server responses.

Keyboard evidence starts from a known focused control and uses Home/ArrowDown, Tab/Shift+Tab, Space and Enter through
default editing, cancellation, recovery, opening/reopening and execution. It does not establish application-wide Tab
navigation. General denied-access administration, unrelated loading and graph wiring remain excluded for the reasons
in the acceptance record.

Independent correctness, spec/experience and repository/constitution reviewers converged with **zero verified findings**.
These are the author's independent local review agents, not the separate final Code Review role. Confirmed findings
were fixed and re-reviewed; disproved candidates and mutation non-detections remain recorded rather than counted as
defects or proof. Final app review and coordinator integration remain distinct gates.

## Local verification scope

[qualification.json](qualification.json) retains 42 chronological selected TRX records with original hashes, raw
counters, outcomes and explicit skipped/unexecuted counts. They are not summed into one fresh aggregate. Relevant
whole-project reruns follow their source changes; later fixture/evidence-only commits do not pretend to rebuild all
earlier projects. Supporting results include:

| Scope | Actual result |
|---|---|
| Whole Publishing after root omission repair and supplied-null controls | 2,109 passed |
| Whole Jint after entered-object depth repair | 392 passed |
| Whole NativeCapture after the same production repairs | 48 passed |
| Whole HTTP / Microsoft365 after declaration audit | 341 / 52 passed |
| Whole Architecture / Modularity / import persistence reruns | 625 / 370 / 75 passed |
| Whole Studio client, unchanged `Studio/src` tree | 5,355 passed across 332 files |
| Strict S-2 presence/null checks on InMemory, SQLite, PostgreSQL, MySQL, SQL Server | 5 passed, zero skipped |
| Clean composed S-2 journey | 6 passed, 43 required capture pairs each |
| Acceptance TypeScript, Python compilation, capture inventory, generated maps/filter check | Passed; no filter changes |

The earlier broad runtime provider run is **5 passed / 363 skipped**, not whole native-provider qualification.
Modularity provider cases remain **six skipped**, import-persistence provider cases **three skipped**, and Serialization
has **one platform skip**. The strict five-provider S-2 run qualifies its specifically named storage paths, including
literal bindings, invocation/request snapshots, variables/outputs and logical External/Custom minima.

The [private 36-definition smoke measurement](https://github.com/elsa-workflows/elsa-foundation/issues/3052#issuecomment-7121750253)
remains **16 converted / 11 published**, with observed completions **4 → 8**, no conversion/publication regressions and
zero new T031a unsupported-default refusals in this corpus. The known bulk-dispatch workflow completes without supplied
inputs. Private names, definitions and refusal details are excluded. This is not business-output parity or S-15 final
JTest acceptance.

Compiled causal controls cover admission, writes, capture/graph warnings, sparse arrays, protected diagnostics,
release/catalog contracts, root omission and depth guards. The latest two supplied-null controls passed on existing
production and both assertion-failed when the existing post-selection literal-null guard was removed; exact source
bytes were restored. That disproved the alleged bypass, so no speculative production patch was kept. Earlier failed
fixtures and isolated non-detections remain outside positive proof claims.

## Retained failures and limits

Earlier failed/incomplete collectors are retained: legacy seed revision pairing, Present-null transport, omitted
activity-root bindings, the seed `initialState` shape, empty-string edit events, select readiness and terminal dialog
reopening each have their actual corrective scope. UX83/86 passed the former 17-state selection but did not establish
the expanded experience. UX94 passed 31-state execution but had off-viewport visual gaps; this 43-state proof closes those
specific gaps. UX96 passed both desktop executions and failed four narrow executions on strict container visibility:
historical group padding or a warning card edge sat against the scroll boundary. The corrected fixtures center and
strictly qualify the actual heading/member rows or warning code/message/facts; they do not lower a global visibility
tolerance or change product styles. The original failed artifacts are not erased or relabelled as passes.

UX92's broader User Tasks authoring selection remains **six failed** at Form ID focus before the changed output-wire
assertion. Its production files are unchanged from the actual initiative baseline. Preserve the existing
[#4929 unchanged-source disposition](https://github.com/elsa-workflows/elsa-foundation/issues/4929#issuecomment-7088529039);
this is not a newly executed main-baseline comparison. S-2's real output-authoring path qualifies its changed shared
wire boundary, without claiming broader User Tasks acceptance or repairing that unrelated focus defect.

Earlier Runtime/Scripting exact exception-type assertions, Architecture prerequisites/narrow domain-default boundary
guard and import warning-count assertion failures remain in the chronological records beside their passing whole
reruns. The earlier Modularity cancellation-timer failure remains retained with **cause unestablished**. Previous
“lower-load” wording is superseded: retained load snapshots do not support that comparison; the unchanged rerun's
370-pass result is the supported fact.

The [main-sync source/experience ledger](https://github.com/elsa-workflows/elsa-foundation/pull/5381#issuecomment-7102384141)
keeps its historical failed collectors and read-only closure scopes. #5381 and #5458 are completed and were not redone.
S-9/#5460 independently owns serializer baseline/null preservation and its conversion/date/hash work. Joint initiative
integration and S-15 business parity are not inferred from this S-2 evidence.

CI is explicitly deferred by the coordinator; no CI wait, workflow/protection change or CI pass is claimed. The author
marks the implementation ready and requests Code Review after local convergence. Code Review reviews/approves;
the coordinator merges. T040's issue closures/reclassification occur only after the actual implementation merge.
