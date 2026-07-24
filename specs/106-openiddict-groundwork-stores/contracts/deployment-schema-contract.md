# Deployment and Schema Contract

## Shared Source of Truth

The OpenIddict feature contributes one public parameterless storage declaration to the selected host composition. Runtime admission and deployment automation resolve the same declaration, host naming policy, provider normalization, capability requirements, and resulting fingerprint.

## Operations

| Operation | Required behavior |
|---|---|
| Plan | Produces deterministic proposed changes and no mutation. |
| Validate | Reports pending/drift/collision/capability/topology diagnostics and no mutation. |
| Status | Reports applied state/fingerprint and no mutation. |
| Apply | Performs only authorized, locked, deterministic schema work and records applied history. |
| Runtime admission | Is validate-only; blocks traffic on missing, stale, conflicting, or unsupported storage. |

## Naming Contract

Feature code supplies stable logical unit identities and defaults. The app host supplies a provider-agnostic naming policy; provider normalization is applied once; collisions identify both logical owners. All evidence records include the resolved fingerprint and physical targets.

## Capability Contract

Before this feature can advertise a provider, executable proof must show the exact public preview.81 family supports its reviewed physical definitions, indexes, named query routes, bounded mutations, CAS/UoW, codec admission, and native plan/mutation-plan inspection. Mongo multi-record claims additionally require transaction-capable topology admission.

The four logical OpenIddict record units do not imply four physical entity tables.
Preview.81 does not expose linked multivalue declarations on
`PhysicalEntityTable`; the selected shared/dedicated or additional-membership-unit
shape must first be recorded in the data model and then consumed unchanged by this
contract.
