# Lifecycle and authority contract

Implemented under [the plan](../plan.md). The API maps `/decisions`; capability relations describe operation identity and are discovered by Studio.

## Core

The compiler replacement contract accepts bounded DMN bytes/profile and returns an immutable artifact or located diagnostics, without storage/publication. The evaluator replacement contract accepts a verified frozen artifact, copied typed inputs, limits and cancellation, returning exact identity, actual outcome/trace and metering. Neither depends on HTTP, EF, identity, workflow, Design storage or third-party CLR objects.

Profile/semantics IDs are immutable compatibility identities. Verify supported IDs and hashes. A deployment cannot reinterpret an old ID: retain its implementation for published content or introduce a new ID with explicit publication/migration behavior. Declare replacement-contract kind and diagnose duplicate composition. Default logic services are scoped and internal. Supplied-artifact direct callers own process authorization and retention.

## Protected operations

| Capability | Grants | Behavior |
| --- | --- | --- |
| decisions.list/read/versions | Read | Trusted-tenant bounded inventory/detail; omit protected source without source access. |
| decisions.create/update/import | Manage | Create or expected-revision mutation; import does not grant execution/publication. |
| decisions.clone-to-draft | Manage + Read | Explicitly copy a permitted immutable version into the draft with revision CAS and source/sensitivity access; never mutate the version. |
| decisions.validate | Manage | Validate an exact candidate without implicit mutation. |
| decisions.publish | Publish + Read | Atomic expected-revision publication under an operation key; confirmation/version identity. |
| decisions.evaluate | Evaluate | Exact published version; output/structural explanation subject to value policy. |
| decisions.cases | Test + Read | Case reads with additional sensitive-value checks. |
| decisions.save-case/delete-case | Test + Manage | Revision-checked case mutation. |
| decisions.test | Test + Read | Freeze draft/version and cases under an operation key; evaluate/retain receipt. |
| decisions.test-runs | History + Read | Read exact receipts/comparisons with redaction. |
| decisions.export | Read + Source | Download permitted XML; additional sensitivity enforcement. |
| decisions.delete-run | History + Manage | Delete retained payload under host retention policy. |

Decisions contributes grants/capabilities through existing contracts. Trusted host tenant/principal context governs every operation. Body/query IDs and cached browser capability state cannot grant authority. A missing tenant is allowed only in explicitly single-tenant composition, never as all-tenants access.

Manage does not imply Publish; Evaluate does not imply Test/History/Source. Apply existing 401/403/404 conventions without exposing another tenant's existence. Stale revisions/divergent operation-key reuse conflict. Diagnostics use safe codes/locations, not raw cell/input snippets.

## Sensitivity and history

Asset policy marks protected inputs/outputs and source sensitivity. Structural traces contain IDs and truth/selection states without echoed values or cell text. Derived content preserves protection. Invocation permission does not permit protected output exposure: enforce the output grant explicitly rather than redact a value and claim ordinary successful equivalence.

Source permission gates XML/rules; SensitiveValues is additionally required for protected source/case/output. Comparisons omit protected expected/actual values. Logs/validation never include raw business values by default.

Saved cases store their declared values. Tests execute synchronously with in-memory frozen candidate/case snapshots, then atomically insert a completed receipt keyed by trusted tenant/actor/operation ID and request fingerprint. Check an existing receipt before reloading edited cases. Concurrent duplicate pure computation may occur, but receipt uniqueness admits one result; divergent request reuse conflicts. No pending test job is durably accepted. Saved expected statuses are Success, NoMatch, InvalidInput, Unsupported, HitPolicyViolation and Unavailable. Success and NoMatch expectations use tagged values (including tagged null for NoMatch); error expectations may use the top-level raw null returned by the evaluator. Budget exhaustion and cancellation abort the complete bounded request without a completed receipt and cannot be saved as expected case outcomes.

If interruption precedes receipt commit, report no recorded completion. Retry requires the same expected revisions/hashes; changed/deleted content conflicts instead of selecting newer content. Completed receipts store identities/hashes/safe summaries by default; raw-value retention is opt-in. The response may deliver authorized detailed comparisons transiently. Summary-only replay explicitly reports detailed payload unavailable and never reconstructs it by reevaluation. No execution/admission/recovery ledger or cleanup scheduler is added.

A decision retains at most 1,000 completed test receipts. Admission checks the limit atomically with insertion and rejects a new run with an actionable deletion message when full. Existing operation-key replay still returns its receipt at the limit. Authorized History + Manage callers may explicitly delete retained runs to admit another; the provider never silently prunes history.

## Workflow publication/recovery

Specific version or latest-at-workflow-publication resolves once through `IExecutableCompilationSource`, within the publication tenant and authorization boundary. Verify eligibility/hash/profile/semantics and embed immutable content/provenance in node metadata. Do not use workflow-executable dependency claims for decision IDs.

Runtime `EvaluateDecision` verifies embedded identity and uses compatible installed semantics; it never reads Decisions Design or resolves latest. Existing activity input/retry behavior applies; this feature adds no new input-refresh policy. Re-evaluating pure frozen inputs/content has equivalent results, with no exactly-once business-effect claim.

The shared execution model owns native/model execution identity, authority, checkpoints, recovery and delivery. Decisions owns only its evaluation semantics and domain asset/test receipts. See [execution-integration.md](execution-integration.md) for the concrete existing host seams and the first-release boundary.

A normal BPMN bound task/businessRuleTask contains the activity, then gateways route on output. Preserve gateway `BpmnDecision`. Automatic vendor decisionRef import is outside v1; unsupported imported bindings remain explicit.

## Studio

Own /decisions routes, resource keys, authenticated SDK HTTP and capability discovery in the module. Context includes backend/tenant/principal/asset/draft-or-version; stale responses cannot retarget content after navigation. Dispose requests/listeners/editor on unmount. Do not use Studio Preferences as a decision store.

Published content is read-only; explicit clone-to-draft starts an edit. Save/validate/test/publish remain separate actions. Test results show their frozen candidate if the current draft has changed. Conflicts preserve unsaved work and require explicit reload/copy resolution.

Use public Studio primitives/tokens, deferred route loading, labeled native table cells, headers, keyboard access and focusable diagnostics. Table scrolling preserves surrounding controls at 320px. Normal light/dark, denial/unavailability, conflict/reload and dirty-navigation cases are browser gates.
