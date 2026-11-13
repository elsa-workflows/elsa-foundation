# External authentication artifact and session persistence

Optional provider-neutral EF persistence for committed one-time sign-in, preview and preview-result artifacts,
and protected durable authentication-session records with revisioned revocation.
Register `AddExternalAuthenticationEntityFrameworkCore` with the selected provider/connection/schema options,
or enable `FoundationIdentityExternalAuthenticationEntityFrameworkCore` in the composed host. Merely adding the
package or solution entry does not enable the feature.

The module is `Identity.ExternalAuthentication`; its frozen history module is `ElsaIdentityExternalAuthentication`.
At schema **1.1.0**, all four providers retain the Initial artifact history and add the DurableSessions migration.
The content-preserving V1 upcaster leaves earlier protected artifact payloads unchanged. Session rows bind
protected provenance to their tenant, owner, identifier and timestamps; indexed identifiers are opaque hashes.

## Authority and acknowledgement

The selected `IAuthenticationArtifactStore` owns durable create/consume acknowledgements. IDs and tenant,
browser, purpose and administrator bindings are bounded server-owned values. A wrong tenant, browser, purpose
or administrator does not consume another flow. Conditional deletion acknowledges a matching unexpired
artifact to at most one consumer; uncertain provider failures never produce a reusable consume receipt.

The store refuses caller/ambient transactions and dirty tracked writes. Expiry cleanup is bounded and preserves
other tenants and unsupported future-schema rows. Provider failures cross the contract as
`AuthenticationStoreException`; cancellation and schema skew retain their distinct meaning. Protected payloads
are opaque here: the later flow broker owns encryption and browser/administrator provenance.

Only one artifact/session authority may be selected. Repeating identical registration is idempotent while options or
either store's ownership changes are rejected. Both stores refuse caller/ambient transactions and pending tracked
writes. Session creation validates bounded provenance, including an exact link revision for external sessions;
listing is bounded and revocation uses the stored revision. Shared protected-key custody is required across nodes.
The provider's engine remains outside this production package; a host
selects its existing provider backend. See the [core contracts](../../Core/EXTENSION_POINTS.md) and
[connection module](../../Elsa.Foundation.Identity.ExternalAuthentication/README.md).

## Staged capability

This prefix registers the optional stores without activating a callback/chooser/API route or switching local
password sign-in, cookie validation or token issuance to the canonical session pipeline. The qualified-link
content version and complete flow/session enforcement follow in their owning source slices. Source presence
alone is no proof of provider sign-in, database/model compatibility, restart or multi-node correctness.
