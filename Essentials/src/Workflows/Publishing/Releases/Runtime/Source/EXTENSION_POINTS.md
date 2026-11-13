# Runtime release source closure extension points

`WorkflowReleaseRuntimeSourceClosureFeature` adds one `IReleaseSourceMemberContributor` strategy;
its identity is `elsa.runtime-source-closure/1`. It requires `WorkflowReleaseRuntime`, one host-selected
`IReleaseRuntimeSourceContextAccessor`, and one `IReleaseRuntimeSourceAuthorization`. Missing or
ambiguous policy selection fails composition. Namespace and exact pins never grant read permission.
Authorization runs before each material/template provider read, and the selected source authority and
persistence partition must remain stable across acquisition and the awaited callback.

Only exact current-schema, uncompressed SQLite source rows are supported initially. The actual selected
Runtime store must preserve bounded reads and `IRuntimeBoundedSourceLeaseStore`; legacy providers and
arbitrary decorators refuse. Material closures are authorized, bounded and leased before expansion;
container bytes must match the actual retained provider material. Returned members have no root or serving
authority, and the shared final owner verifier discovers all edges and opaque requirements independently.
Templates are copied into bounded owned payloads before the callback; template source associations remain
the responsibility of the source owner. No lazy provider read escapes the callback lifetime.

A source host must still install its native/BPMN/ETL root selector and its source authorization policy.
This package alone proves no actual published source selection, import, target mapping or activation.

| Port | Kind / layer / default | Signature | Override and dependent callers |
|---|---|---|---|
| `IReleaseRuntimeSourceContextAccessor` | Replacement, Runtime.Core; no default | `ReleaseRuntimeContentScope Current { get; }` | Select one immutable trusted source authority, tenant and physical partition per operation. Used by `RuntimeSourceMemberContributor` before acquisition and around the retained callback. |
| `IReleaseRuntimeSourceAuthorization` | Replacement, Runtime.Core; no default | `ValueTask<bool> AuthorizeMaterialAsync(ReleaseRuntimeContentScope context, string artifactId, string artifactHash, WorkflowExecutableIdentity? fullPin, CancellationToken cancellationToken = default)` and `ValueTask<bool> AuthorizeTemplateAsync(ReleaseRuntimeContentScope context, string templateId, string templateHash, CancellationToken cancellationToken = default)` | Select one host export policy. Denial prevents the provider read; a namespace or dependency declaration is not permission. Used for initial containers and every acquired dependency. Preserve requested cancellation. |
| `IReleaseSourceMemberContributor` | Strategy, Capture.Core; this feature registers Runtime source closure | `ValueTask ExecuteAsync(IReadOnlyList<ReleaseContentMember> containers, IReleaseContentReader captured, ReleaseBundleLimits limits, Func<IReleaseContentReader, CancellationToken, ValueTask> capture, CancellationToken cancellationToken = default)` | Register additional owner strategies with distinct identities. `CaptureReleaseSourceMembers` invokes them through the named `ReleaseSourceMembersCapturing` event, holds callbacks, and feeds complete immutable bytes into independent final verification. |
