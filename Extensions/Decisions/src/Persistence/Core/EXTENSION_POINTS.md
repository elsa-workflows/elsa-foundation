# Decisions persistence extension points

## Overridable contracts

### IDecisionReader / IDecisionWriter

- Kind: scoped replacement contracts, Layer 1 domain-owned CQS persistence boundary.
- Signatures: exact tenant-scoped asset/version/case/completed-receipt queries; expected-revision mutations return `DecisionWriteConfirmation` rather than successor aggregates.
- Default implementation: internal `EfDecisionStore<TContext>` in `Elsa.Decisions.Persistence.EntityFrameworkCore`, installed by `AddDecisionsEntityFrameworkCore<TContext>`. No persistence default is installed by Core.
- Override when supplying another authoritative provider. Preserve source identity, tenant/owner keys, atomic CAS/publication, immutable versions, actor-scoped operation replay/conflicts and completed-only receipts. Reads never mutate or choose another version.
- Consumers: `Elsa.Decisions.Design`; the workflow publication adapter reads immutable versions through `IDecisionReader`. Runtime evaluation uses frozen content and does not read this store.

## Implementable contributor interfaces

None.

## Events

None. This store is not an execution-state, queue, lease or recovery ledger.
