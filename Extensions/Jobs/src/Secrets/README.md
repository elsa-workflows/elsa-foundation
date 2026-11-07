# Jobs Secrets binding

Optional adapter over [Secrets Core](../../../../Essentials/src/Secrets/Core/EXTENSION_POINTS.md).
It uses `SecretReference` and the host-selected `ISecretValueResolver`; it installs no secret store,
vault facade, ambient tenant or workflow dependency. Plain Jobs installs no data policies by default.

The host calls `AddJobSecrets` with declarations for exact logical handler and input-schema pairs.
Each declaration supplies at most 32 JSON-pointer reference slots, each with 1–32 allowed exact
references, and at most 32 protected scalar input pointers. Pointers use exact JSON property spelling
and RFC 6901 escaping; at most 32 segments/1,024 characters are accepted. Declaration snapshots are
immutable after registration. Names use the owning Secrets name bounds and trimmed lowercase lookup
identity; optional type/scope constraints retain Secrets' case-insensitive semantics. Tenants are exact.

For a matching declaration, admission checks slot presence, strict reference-object shape, allowed
reference identity, duplicate/ambiguous members, undeclared reference-shaped objects and protected
scalar bounds without resolving values or requiring a CLR handler/store. An optional slot may be
absent or JSON null. Unrelated handler/schema pairs keep their ordinary business JSON behavior;
reference-shaped `{ "name": ... }` objects are not globally reclassified as secrets.

```csharp
services.AddJobSecrets(options => options.Declarations.Add(new(
    new("reports.generate", "1"), new("reports.input", 1),
    [new("/credential", [new("payments.api", "text", "reports")])],
    ["/customerIdentifier"])));
```

The accepted input retains the explicit reference object, for example
`{ "credential": { "name": "payments.api", "typeName": "text", "scope": "reports" } }`.
The adapter does not alter `JobRequest` or its v1 digest. A protected scalar remains in accepted input
under its existing input-read permission; the optional policy prevents literal output/progress echoes.

The actual worker supplies the accepted scope and handler, begins the scoped policy before handler
construction, and gives that invocation's `IJobSecrets` to its handler through DI. `ResolveAsync`
accepts an existing `SecretReference`, with no tenant parameter. It permits only references present in
the accepted declared slots, calls the selected resolver with the accepted tenant, verifies returned
tenant/name/type/scope and active metadata, and drops all provider exception/failure text. A producer
may install declarations without a resolver. Resolution outside a bound/declared invocation refuses.

The scope tracks returned secret strings and declared protected scalar literals. Before result or
progress persistence it refuses matching literal text in values, keys and messages. Empty/null
protected scalars contribute no literal. Individual literals are bounded to 65,536 characters and an
attempt tracks at most 128 distinct values. Scoped references are cleared at disposal and never reused
for another tenant. This is a literal-disclosure policy, not a sandbox for arbitrary trusted host code,
external logging, encodings, hashes or deliberate transformations.

`JobDataPolicyException` has fixed text, code and no inner cause; worker refusal is nonretryable with
null result payload. A typed `StoreUnavailable` resolver failure instead raises a fixed
`JobDataUnavailableException`; the worker retries within the accepted attempt/backoff budget. Denied,
missing, invalid metadata and other refusal classifications remain nonretryable. Provider text is
discarded in both cases, and caller-requested cancellation keeps its existing cancellation path.
Current admission checks run in the client, API submit/schedule and initial
RunJob preparation before saved wait/source creation. Already materialized workflow inputs are used
as-is; no binding is reevaluated. Raw handler exception paths retain their existing fixed diagnostics.

Current source is unbuilt/unqualified. [Control source](../../tests/Secrets/Tests/README.md) uses actual
GenericHost, public Jobs/Secrets SQLite registrations and HTTP, plus an explicitly bounded native
preparation fixture. Full package/model/Studio/API/log/trace adverse qualification remains required by
the [acceptance matrix](../../../../specs/1218-standalone-jobs/acceptance-matrix.md).
