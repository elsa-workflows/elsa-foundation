# Research: Secrets Module

## Decision: Create a dedicated `Elsa.Secrets` domain

**Rationale**: Secrets are retrievable sensitive values and references used by workflows and modules. Identity credentials are non-retrievable hashes or tokens with a different lifecycle. Keeping Secrets separate avoids mixing runtime value resolution with authentication credential storage.

**Alternatives considered**:

- Extend Foundation Identity: rejected because identity credentials and workflow/module secrets have different security semantics.
- Put contracts under Workflows: rejected because module settings outside workflows also need secret references.

## Decision: Use immutable normalized technical names

**Rationale**: Workflow definitions and module settings need stable serialized references. Normalizing names for uniqueness avoids collisions from case or whitespace while preserving a simple reference model.

**Alternatives considered**:

- Use generated IDs in references: rejected because IDs are less portable across export/import and harder for operators to reason about.
- Allow renames: rejected because references could silently drift or require broad updates.

## Decision: Resolve latest active version only

**Rationale**: Operators expect rotation to update future workflow executions without editing every consumer. Version pinning can be added later if a real reproducibility requirement appears.

**Alternatives considered**:

- Version-pinned references: deferred because it complicates rotation UX and is not needed for the first Foundation slice.
- Resolve any active version: rejected because multiple active versions should be treated as unhealthy state.

## Decision: No cleartext reveal after create or rotate

**Rationale**: The safest management model lets operators submit replacement values, test resolution, and rotate secrets without allowing the current value to be displayed by general APIs or Studio.

**Alternatives considered**:

- Administrator reveal: rejected for the first slice because it increases blast radius and requires stronger audit/authorization controls.
- Encrypted payload display: rejected because encrypted payloads are store-private material and not useful to operators.

## Decision: Built-in stores are encrypted and configuration-backed

**Rationale**: The encrypted store supports Elsa-managed values for local and simple deployments. The configuration-backed store supports deployment-managed values without moving those values into Elsa storage.

**Alternatives considered**:

- Cloud vault provider in v1: deferred until the base store/type contracts prove stable.
- Configuration store as read-only only: refined to "read-only for underlying values, writable for lookup metadata" so operators can create references to configured keys without Elsa mutating configuration.

## Decision: Use Groundwork persistence for durable Secrets

**Rationale**: Secrets are a low-risk document aggregate with simple access patterns: load by normalized name, list/filter by declared metadata indexes, and save the full aggregate with optimistic concurrency. This fits Groundwork's provider-neutral document model and builds on the completed Groundwork persistence readiness work.

**Alternatives considered**:

- EF Core provider packages: rejected for the first Foundation slice because the current repository direction favors host-selectable Groundwork document storage where it fits.
- In-memory only: rejected because Studio management needs durable behavior beyond tests/development.

## Decision: Add a Secret expression descriptor and handler

**Rationale**: Workflow inputs already use expression wrappers. A `Secret` expression lets Studio persist a secret reference in the same authoring shape as other expressions, while runtime materialization resolves the value at point of use through `ISecretResolver`.

**Alternatives considered**:

- Literal serialized reference with special casing in the workflow designer: rejected because it bypasses existing expression extensibility.
- JavaScript helper only: rejected because secret selection should not require arbitrary script input.

## Decision: Port product behavior, not upstream implementation shape

**Rationale**: Upstream `elsa-core` has a mature Secrets intent, but Foundation has different architecture: CShells features, mediator-backed endpoints, Groundwork, and React Studio modules. The implementation should preserve behavior while using local package and extension patterns.

**Alternatives considered**:

- Literal copy of upstream projects: rejected because it would introduce the old feature/module model, EF Core package shape, and Blazor Studio UI.

## Decision: Represent permissions and audit now, enforce per host

**Rationale**: The contracts should make security operations explicit from the start. The local development host can still run permissively while authorization integration matures.

**Alternatives considered**:

- Omit permissions/audit until a later release: rejected because it would encourage unsafe API assumptions and make later hardening a breaking change.
