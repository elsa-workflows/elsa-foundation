# Jobs Secrets controls

Status: source only. No local .NET/build/discovery/host/test execution has run for this checkpoint.

The main fixture composes actual GenericHost with public Jobs EF SQLite and public Secrets
manager/resolver/encrypted-store/EF SQLite registration. Stores, key material and logs are owned test
fixtures; no production host, credential or existing database is used. Producer preparation, process
restart, two concurrent then alternating tenants, colliding aliases, missing/scope-denied/revoked
bindings, retry after rotation, null/empty inputs and disclosure refusal use the actual worker and
selected command/query authority. Constructor-time resolution checks that policy binding precedes
handler construction. Captured observations are private test probes, never product output.

`JobConnectionsControls.cs` adds ten declared, unexecuted cases to this same test project. Its optional
test-only references compose the actual Connections manager, resolver and EF SQLite repository with
the real Jobs worker and encrypted Secrets provider. Producers persist a declared connection alias
and its underlying declared `SecretReference`, then restart the real host before concurrent and
alternating tenants use that same alias with distinct canaries. Each actual `ConnectionResolveRequest`
sets `TenantId = context.Scope.TenantId`; no ambient tenant is selected. A paused retry crosses host
disposal/recreation, secret rotation and a connection metadata revision, then uses a new invocation
scope and the current value. Missing/revoked connection and secret cases require positive typed
failure observations at the real resolver or credential-read boundary and no successful foreign
binding. Useful totals and attempted plaintext result/progress/exception paths are inspected through
retained Jobs snapshots, attempts, receipts, Connections metadata and captured framework logs.

The fixture's handler also calls the existing `IJobSecrets` facade for the declared underlying
credential and verifies that it matches the real resolved connection value before exposing it to
business logic. That call tracks the literal for the existing output/progress policy. This is the
explicit integration contract exercised here: `IConnectionResolver` alone is not a Jobs data policy,
and arbitrary trusted C# or external logging is not sandboxed. No resolver/repository is mocked or
replaced in these Connections cases, and no new production package or universal wrapper is added.
Only this full-integration test project gains Connections dependencies; the packaged standalone Jobs
dependency closure and its forbidden-package checks remain unchanged. Its lock/transitive dependency
delta still requires SDK restore/generation and source-bound execution; no lock file is hand-edited.

Adverse resolver controls replace only the documented Secrets resolver to return mismatched tenant,
name/type/scope or provider failure text, or throw a canary. Those are labelled replacement controls;
default-provider tenant, lifecycle and persistence controls remain distinct. Constant diagnostics,
nonretryability and absence of candidate payload/progress/receipt/log literals are inspected through
public queries. Protected scalar input itself remains governed by the separate input-read permission.

A first-unavailable/next-success control uses the actual default resolver and encrypted SQLite-backed
secret store with one injected store read outage. A second variant returns a typed `StoreUnavailable`
failure with provider-text canary, then delegates recovery to that same default resolver. Both inspect
two actual worker attempts, a safe retryable first receipt, successful recovery in a fresh scope and
absence of provider text from receipts/results/framework logs. Denied/missing/mismatch controls keep
their distinct nonretryable assertions.

The HTTP control maps the actual optional API against the real SQLite authority and trusted test
principal/current-authorizer seam; it proves submit/schedule preadmission refusal without a secret
store. It does not replace the full real identity-directory security qualification.

One new `JobsNativeDataPolicyTests.cs` in the existing native test project exercises actual RunJob
initial preparation with the owned `NativeFixture`, checking positive fault versus suspend before any
admission or persisted wait. This is a finite fixture boundary, not a full persisted native/BPMN
host journey. Full package/engine/model/Studio/log/trace evidence and independent review remain open.
