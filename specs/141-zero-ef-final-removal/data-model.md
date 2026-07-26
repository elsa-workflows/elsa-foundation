# Data Model: Zero-EF Final Removal

This work unit does not introduce a production persistence model. Its durable entities are evidence and control records used to prove safe removal.

## 1. Prerequisite Gate

Represents one condition that must pass before a deletion slice.

| Field | Meaning |
|---|---|
| `GateId` | Stable identifier such as `diagnostics-four-provider`, `openiddict-conformance`, `performance-verdicts`, or `dashboard-provider-parity` |
| `Owner` | Issue/spec that owns the evidence |
| `RequiredFor` | EF family or integration slice blocked by the gate |
| `State` | `Pending`, `Pass`, `Blocked`, or `RatifiedAmendment` |
| `EvidenceIdentity` | Immutable merge SHA/report/artifact reference |
| `VerifiedOnMain` | Whether remote `main` contains the evidence |
| `VerifiedAt` | Verification date/time |

**Validation rules**:

- A deletion slice requires every linked gate to be `Pass` or `RatifiedAmendment`.
- `Pass` without an immutable evidence identity or remote-main verification is invalid.
- An amendment must name the ratifier and retained data.

## 2. EF Surface Entry

Represents one discovered EF artifact or dependency.

| Field | Meaning |
|---|---|
| `Category` | Scanner category (project, direct package, central version, direct project edge, static/restored transitive consumer, migration, context, registration, host configuration, missing assets, boundary violation) |
| `Identity` | Normalized repository-relative path or `consumer -> dependency` tuple |
| `OwningFamily` | Diagnostics, OpenIddict, Identity oracle, shared persistence, test/tool, host/package |
| `RequiredGateIds` | Gates that must pass before removal |
| `RemovalSlice` | Dependency-ordered slice that removes it |
| `State` | `Present`, `ApprovedForRemoval`, `Removed`, or `Unexpected` |
| `Evidence` | Commit/test proving removal |

**Validation rules**:

- Intake is mechanically derived from current `origin/main`.
- `Unexpected` entries fail the work unit.
- Final certification requires zero entries in every EF category and zero missing-assets entries.

## 3. Test-Retention Ledger Entry

Represents one affected test method or parameterized source method.

| Field | Meaning |
|---|---|
| `TestIdentity` | Fully qualified source method identity |
| `Reachability` | `DirectToken`, `SharedFixture`, `SharedHost`, or `TransitiveProject` |
| `OriginalSubject` | Behavior/implementation under test before removal |
| `Objective` | Provider-neutral behavior or provider-specific mechanism being asserted |
| `Disposition` | `Preserve`, `Convert`, or `RemoveApproved` |
| `ReplacementEvidence` | Named test method(s), opened and verified |
| `Architect` | Required for `RemoveApproved` |
| `DecisionDate` | Required for `RemoveApproved` |
| `Verification` | Passing command/result on the candidate |

**Validation rules**:

- Every affected method has exactly one row.
- `Convert` requires named replacement evidence before original deletion.
- `RemoveApproved` requires architect, date, and rationale.
- File-level or token-level claims do not substitute for method-level coverage.

## 4. Provider Composition

Represents one supported host provider shape.

| Field | Meaning |
|---|---|
| `Provider` | SQLite, SQL Server, PostgreSQL, or MongoDB |
| `Topology` | Required provider topology/configuration class without connection values |
| `EnabledLanes` | Runtime, Design, IAM/Secrets, diagnostics, Identity, OpenIddict, dashboard |
| `ResolvedContracts` | Expected Groundwork-backed service identities |
| `SchemaState` | Validated manifest/fingerprint state |
| `StartupVerdict` | `Pass` or fail-closed diagnostic |
| `BehaviorVerdict` | Linked correctness/restart/tenancy evidence |

**Validation rules**:

- Exactly one provider backs every enabled durable lane.
- No EF service resolves.
- Dashboard is enabled for all four providers.
- Connection values and secrets are never retained in evidence.

## 5. Zero-EF Certification

Represents the permanent architecture result.

| Field | Meaning |
|---|---|
| `RepositoryHead` | Exact tested commit |
| `RestoreHead` | Exact project/input state used for evaluated restore |
| `ProjectCount` | Number of repository projects discovered independently of solutions |
| `ProjectsMissingAssets` | Must be empty |
| `Categories` | Every former ratchet category and its entries |
| `Verdict` | `Pass` only when all categories are empty |
| `CommandEvidence` | Restore/test command identity and retained CI result |

**State transition**:

`Uncertified` → `RestoreComplete` → `Scanned` → `Pass`

Any missing asset, nonempty category, changed project input after restore, or omitted project returns the result to `Uncertified`/`Fail`.

## 6. Review Record

| Field | Meaning |
|---|---|
| `Axis` | Correctness/mechanism, evidence integrity, or scope/test preservation |
| `BaseSha` / `HeadSha` | Exact reviewed range |
| `Reviewer` | Independent reviewer identity |
| `Verdict` | `Pass`, `Blocked`, or `Fail` |
| `Findings` | Severity, path, rationale, proof |
| `Disposition` | Fix, withdrawal, or rejected-with-evidence |
| `Reverification` | Originating reviewer verdict on the final candidate |

## 7. Completion Evidence Record

| Field | Meaning |
|---|---|
| `LaneIssue` | #647 or parent #629 |
| `MergeSha` | Merge commit on remote `main` |
| `PrerequisiteEvidence` | #642/#643/#646/#932 and Groundwork release references |
| `Certification` | Zero-EF result |
| `ProviderMatrix` | Four-provider results |
| `PerformanceVerdicts` | Coverage-ledger/report references |
| `TestLedger` | Final preservation/removal ledger |
| `Reviews` | Exact-range review records |
| `ProjectStatus` | Project 33 state after verification |

**Validation rules**:

- Closure is invalid before `MergeSha` is verified on remote `main`.
- #629 closure requires all six program completion conditions, not merely #647 source deletion.
