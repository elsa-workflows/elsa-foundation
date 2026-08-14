# Extension points — Activities.Design.Persistence.Groundwork domain

Groundwork provider catalog for activity-design persistence replacement contracts. Contracts are defined in `Elsa.Activities.Design.Persistence.Core`; this feature supplies the Groundwork document-store implementations when a shell selects Groundwork persistence.

## Replacement contracts

| Contract | Groundwork implementation |
|---|---|
| `IActivityDefinitionStore` | `GroundworkActivityDefinitionStore` |
| `IActivityDefinitionVersionStore` | `GroundworkActivityDefinitionVersionStore` |
| `IAddActivityDefinitionCommand` | `GroundworkAddActivityDefinitionCommand` |
| `IAddActivityDefinitionVersionCommand` | `GroundworkAddActivityDefinitionVersionCommand` |
| `IActivityDefinitionLookup` | Core `ActivityDefinitionLookup` |
| `IActivityAvailabilitySettingsStore` | `GroundworkActivityAvailabilitySettingsStore` |
| `IActivityDefinitionManagementProjectionStore` | `GroundworkActivityDefinitionManagementProjectionStore` |
| `IActivityDefinitionAuthoringStore` | `GroundworkReusableActivityStores` |
| `IActivityDefinitionDraftStore` | `GroundworkReusableActivityStores` |
| `IActivityDefinitionVersionPublicationStore` | `GroundworkReusableActivityStores` |
| `IRecommendedActivityDefinitionPickerStore` | `GroundworkReusableActivityStores` |
| `IActivityDefinitionLayoutStore` | `GroundworkReusableActivityStores` |
| `IActivityDraftValidationStore` | `GroundworkReusableActivityStores` |
| `IActivityForkStore` | `GroundworkReusableActivityStores` |
| `IActivityDirectDependencyStore` | `GroundworkReusableActivityStores` |
| `IActivityDependencyProjectionStore` | `GroundworkActivityDependencyProjection` |
| `IActivityDependencyProjectionRebuilder` | `GroundworkActivityDependencyProjection` |
| `IActivityUpgradePlanStore` | `GroundworkActivityUpgradePlanStore` |
| `IActivityUpgradeApplyReceiptStore` | `GroundworkActivityUpgradePlanStore` |
| `ICreateActivityDefinitionCommand` | `GroundworkReusableActivityStores` |
| `ISaveActivityForkCandidateCommand` | `GroundworkReusableActivityStores` |
| `IPruneActivityForkCandidatesCommand` | `GroundworkReusableActivityStores` |
| `IApplyActivityForkCandidateCommand` | `GroundworkReusableActivityStores` |
| `IUpdateActivityDefinitionPresentationCommand` | `GroundworkReusableActivityStores` |
| `ICreateActivityDraftCommand` | `GroundworkReusableActivityStores` |
| `IUpdateActivityDraftPresentationCommand` | `GroundworkReusableActivityStores` |
| `ICreateActivityDraftConflictCopyCommand` | `GroundworkReusableActivityStores` |
| `IReplaceActivityDraftCommand` | `GroundworkReusableActivityStores` |
| `IApplyActivityContractProposalCommand` | `GroundworkReusableActivityStores` |
| `IDiscardActivityDraftCommand` | `GroundworkReusableActivityStores` |
| `IStoreActivityDraftValidationCommand` | `GroundworkReusableActivityStores` |
| `IChangeActivityVersionLifecycleCommand` | `GroundworkReusableActivityStores` |
| `ISetActivityDefinitionRecommendationCommand` | `GroundworkReusableActivityStores` |

`AddGroundworkActivitiesDesignStores()` removes existing registrations for these contracts before adding the Groundwork implementations, preserving the one-active-implementation replacement-contract rule.

## Feature specialization seam

`IDesignAtomicWriter` defaults to `GroundworkDesignAtomicWrite` and uses `TryAddScoped`, so a host
can register a specialization before composing the Groundwork activity-design stores. An
inheriting feature that specializes after its base registration must use
`services.Replace(ServiceDescriptor.Scoped<IDesignAtomicWriter, Implementation>())`; direct
`AddScoped` would create an invalid duplicate replacement registration. Both orders are covered by
registration tests. The contract owns replay-safe multi-document mutations, durable operation
markers, and uncertain-commit reconciliation for workflow and activity design commands.

## Storage manifest declaration

`ActivitiesDesignGroundworkStorageManifestSource` (feature identity `elsa-activities-design`)
implements `IGroundworkStorageManifestSource`. It contributes
`ActivitiesDesignStorageManifest.Create()` and declares the read ports it owns
(`IActivityDefinitionStore`, `IActivityDefinitionVersionStore`, `IActivityAvailabilitySettingsStore`,
`IActivityDefinitionManagementProjectionStore`).

The manifest declares projected columns, logical and physical indexes, and bounded, scale-bearing
queries; there is no load-all or client-side evaluation route.

### Bounded-width and numeric-member rules

Projected and residual columns are width-bounded so every declared compound index key stays under SQL
Server's 1700-byte nonclustered index limit (searchable text bounded, identity/sort-key columns
shorter). Over-limit values **fail projection validation rather than truncate**.

Most residual/projected members are keyword strings. The enum-kind members are canonical JSON numbers
and are declared numeric (`IndexValueKind.Number`) via `NumericMemberPaths`:

- `ManagementAuthorityField`
- `ManagementDraftStatusField`
- `ManagementVersionLifecycleField`

Their residual predicates and projected columns are both numeric; every other residual member is a
keyword string.

## Design atomic writer and shared operation document

`IDesignAtomicWriter` (defined in `Elsa.Persistence.Groundwork.Querying`, default
`GroundworkDesignAtomicWrite`) owns replay-safe multi-document mutation for the activity-design
commands: durable operation markers, staged writes, and uncertain-commit reconciliation. Its durable
ledger is the shared `designOperation` document declared by `GroundworkDesignAtomicWriteStorageManifest`
(owner `elsa.design.atomic-write`, route `design-atomic-write`, topology requirement
`multi-document-transactions`). This lane contributes `GroundworkDesignAtomicWriteStorageManifestSource`
via `TryAddEnumerable`, so the operation document is declared once across both design lanes.

## Registration and manifest sources

`AddGroundworkActivitiesDesignStores()`
(`Elsa.Activities.Design.Persistence.Groundwork.DependencyInjection`) is the lane registration method.
It:

- swaps every replacement contract in the table above to its Groundwork implementation
  (`RemoveAll<T>()` then `AddScoped<T, …>()`), with the reusable-activity ports resolving to the shared
  `GroundworkReusableActivityStores` aggregate;
- contributes `ActivitiesDesignGroundworkStorageManifestSource` and the shared
  `GroundworkDesignAtomicWriteStorageManifestSource` as `IGroundworkStorageManifestSource` enumerables;
- registers the `IDesignAtomicWriter` specialization seam with `TryAddScoped`;
- registers the default identity generator, hasher, and entity factories.

## Schema readiness guard

The `GroundworkSchemaReadinessTask` start-phase guard (base `Elsa.Persistence.Groundwork`) validates
the applied physical target against the composed manifest and **never auto-applies or repairs** schema
unless the host opts into safe startup auto-apply. It is wired per provider by
`AddGroundworkSchemaReadinessGuard()` from each provider's document-store registration — not by this
lane.

## Host composition (unified vs lane-specific)

- **Unified (shipped reference host):** enabling one provider feature —
  `AddGroundwork{Sqlite|SqlServer|PostgreSql|MongoDb}UnifiedPersistence(…)` — routes through
  `AddGroundworkUnifiedStoreFamilies()`, which composes this lane
  (`AddGroundworkActivitiesDesignStores`) alongside the workflows-design and other store families over
  one physical document store; the selected provider's document store owns the readiness guard. See
  [`../../../../Persistence/Groundwork/Unified/README.md`](../../../../Persistence/Groundwork/Unified/README.md).
- **Lane-specific:** a host may call `AddGroundworkActivitiesDesignStores()` directly after registering
  a provider `IDocumentStore`/`IBoundedDocumentStore` and the host `IPayloadSerializer`. The bounded
  store is required for the admitted activity-management and reusable-activity queries.

## Cross-references

- The EF Core design persistence implementation is removed by spec 093 US4; Groundwork is the sole
  activity-design persistence provider.
- Activity reconciliation extension points: [`../../Reconciliation/EXTENSION_POINTS.md`](../../Reconciliation/EXTENSION_POINTS.md)
- Unified provider selection and schema operations: [`../../../../Persistence/Groundwork/Unified/README.md`](../../../../Persistence/Groundwork/Unified/README.md)
- Repo-wide index: [`../../../../../../EXTENSION_POINTS.md`](../../../../../../EXTENSION_POINTS.md)
