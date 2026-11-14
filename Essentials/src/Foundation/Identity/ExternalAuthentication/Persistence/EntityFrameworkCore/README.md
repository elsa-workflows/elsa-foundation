# External authentication artifact persistence

Optional provider-neutral EF persistence for committed one-time sign-in, preview and preview-result artifacts.
Register `AddExternalAuthenticationEntityFrameworkCore` with the selected provider/connection/schema options,
or enable `FoundationIdentityExternalAuthenticationEntityFrameworkCore` in the composed host. Merely adding the
package or solution entry does not enable the feature.

The module is `Identity.ExternalAuthentication`; its frozen history module is `ElsaIdentityExternalAuthentication`.
At schema **1.0.0**, all four providers have a coherent Initial history containing artifact state and the shared
schema-finalization metadata. The model and registration intentionally contain no durable interactive sessions.

## Authority and acknowledgement

The selected `IAuthenticationArtifactStore` owns durable create/consume acknowledgements. IDs and tenant,
browser, purpose and administrator bindings are bounded server-owned values. A wrong tenant, browser, purpose
or administrator does not consume another flow. Conditional deletion acknowledges a matching unexpired
artifact to at most one consumer; uncertain provider failures never produce a reusable consume receipt.

The store refuses caller/ambient transactions and dirty tracked writes. Expiry cleanup is bounded and preserves
other tenants and unsupported future-schema rows. Provider failures cross the contract as
`AuthenticationStoreException`; cancellation and schema skew retain their distinct meaning. Protected payloads
are opaque here: the later flow broker owns encryption and browser/administrator provenance.

Only one artifact authority may be selected. Repeating identical registration is idempotent while options or
store-ownership changes are rejected. The provider's engine remains outside this production package; a host
selects its existing provider backend. See the [core contracts](../../Core/EXTENSION_POINTS.md) and
[connection module](../../Elsa.Foundation.Identity.ExternalAuthentication/README.md).

## Staged capability

This artifact-only prefix does not register `IAuthenticationSessionStore`, a callback/chooser/API route or a
canonical cookie authority, and does not claim interactive-session durability or runtime readiness. Session
state, qualified-link bindings and complete flow integration follow in their owning source slices. Deployment
must eventually share the flow broker's protected-key authority across nodes; source presence alone is no proof
of provider sign-in, restart or multi-node correctness.
