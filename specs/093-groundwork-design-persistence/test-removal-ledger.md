# Test-removal approval ledger

**Work unit**: `093-groundwork-design-persistence`
**Baseline**: `origin/main` at `d1548991f` on 2026-07-20

This is the T003 intake ledger. It inventories every test source that directly
references EF Core or an EF design implementation. Test identities are source
method identities; parameterized display variants are covered by their method.
None is approved for deletion. A later architect must split a grouped line if
different methods receive different decisions.

| Test identity or exact source-method group | Objective classification | Replacement evidence / rationale | Architect | Decision | Date |
|---|---|---|---|---|---|
| `Activities/Integration/CrossContextLifecycleTests`: `ActivitiesDesignDbContext_ImmutableEnforcement_AppliesToActivityDefinition`; `ActivitiesDesignDbContext_RegistersTenantIdIndex_OnTenantEntityDescendants`; `WorkflowsDesignDbContext_ImmutableEnforcement_AppliesToWorkflowDefinitionVersion`; `WorkflowsDesignDbContext_ImmutableEnforcement_AppliesToVersionLayout`; `BothContexts_BaseEntityProperties_AreImmutable`; `WorkflowsDesignDbContext_RegistersTenantIdIndex_OnTenantEntityDescendants`; `BothContexts_ShareRowNumberIndex_OnEveryEntity` | Preserve immutable-write and tenancy-index domain intent; EF metadata assertions are mechanism-specific. | Groundwork stale-write/scope suite, T021–T024 and T075. | — | Pending | — |
| `Activities/Integration/ExactVersionResolutionTests`: `ResolvesExistingVersion_AndReturnsNoneForAbsent`; `BuildMetadataIsIgnored_WhenResolving` | Preserve exact-version identity. | Activity query suite, T022/T038. | — | Pending | — |
| `Activities/Integration/VersionOrderingTests`: `ListingOrdersByPrecedence_MajorMinor_DescendingDbSide`; `ListingOrdersByPrecedence_Patch_MultiDigitDescendingDbSide` | Preserve SemVer ordering. | Activity query suite and physical-route plans, T038/T045. | — | Pending | — |
| `Activities/Registration/FeatureRegistrationTests`: all `[Fact]` methods, including `SqliteActivitiesDesignPersistenceShellFeature_RegistersLookupAndSavingHandler` | Preserve feature composition; the SQLite EF shell assertion is removable mechanism only after Groundwork registration parity exists. | T062 provider-registration suite. | — | Pending | — |
| `Activities/Unit/ActivityDefinitionIdentityTests`: `ActivityTypeKey_IsImmutable_AfterInsert`; `Identity_SurvivesNewVersionWithDifferentTypeInfo`; `DuplicateActivityTypeKey_ThrowsOnInsert` | Preserve identity/uniqueness invariants. | Activity lifecycle and OCC suite, T022–T024. | — | Pending | — |
| `Activities/Unit/ActivityDefinitionLookupTests`: `ListDefinitions_SearchTerm_MatchesActivityTypeKey`; `ListDefinitions_ForwardsTenantAgnosticToTheFilter` | Preserve lookup and explicit privileged-access intent. | Activity query and scope suite, T038/T024. | — | Pending | — |
| `Activities/Unit/DescriptorPersistenceRoundTripTests`: `ActivityDefinitionVersion_RoundTripsDescriptor_AsOpaqueTypeAndPayload`; `MalformedDescriptorPayload_SoftFailsToDefault_WithoutThrowing` | Preserve logical serialization behavior. | Activity lifecycle suite, T022. | — | Pending | — |
| `Activities/Unit/EFCoreActivityDefinitionVersionStoreTests`: `GetWithDefinition_throws_EntityNotFound_when_absent`; `GetWithDefinition_loads_owning_definition` | Preserve public store outcomes; EF implementation class is removable. | Activity lifecycle suite, T022. | — | Pending | — |
| `Activities/Unit/SavingEventDispatchTests`: `SavingHandler_ProducesShadowDescriptor`; `LoadingHandler_RehydratesDesignFacets`; `Aggregator_DispatchesToEveryRegisteredTypedHandler`; `Aggregator_IsNoOp_ForUnrelatedEntities`; `ExactlyOneEventHandler_HandlesOnEntitySaving` | Preserve payload round-trip only; EF saving/loading aggregator mechanics are removable. | Groundwork serializer and activity contract scenarios, T022/T035. | — | Pending | — |
| `Workflows/EFCoreWorkflowPortfolioDataSourceTests`: `ReturnsTheCompletePortfolioFixtureBeyondOnePage` | Preserve bounded portfolio result semantics; EF data source is removable. | Workflow query-scale suite, T037/T039. | — | Pending | — |
| `Workflows/Unit/CrossFeatureValidatorSubscriptionTests`: `Cross_feature_validator_contributes_errors_surfaced_on_OnDraftValidated`; `Cross_feature_and_baseline_validators_both_contribute_in_the_same_pass` | Preserve validation gate behavior. | Workflow lifecycle suite, T021. | — | Pending | — |
| `Workflows/Unit/DraftMutationCommandTests/CloneDraftFromVersionTests`: all five `[Fact]` methods | Preserve copied state/layout/provenance and lifecycle event behavior. | Workflow lifecycle suite, T021. | — | Pending | — |
| `Workflows/Unit/DraftMutationCommandTests/CreateDraftTests`: `CreateDraft_persists_the_draft_with_a_layout_sibling`; `CreateDraft_publishes_OnDraftCreated_with_no_source_version_id` | Preserve draft aggregate creation and event behavior. | Workflow lifecycle suite, T021. | — | Pending | — |
| `Workflows/Unit/DraftMutationCommandTests/DiscardDraftTests`: all four `[Fact]` methods | Preserve atomic discard, non-interference, event, and idempotent outcome. | Workflow atomicity suite, T021/T023. | — | Pending | — |
| `Workflows/Unit/EventSourcingContractTests`: all three `[Fact]` methods | Preserve event failure policy and durable outcome. | Workflow lifecycle/atomicity suite, T021/T023. | — | Pending | — |
| `Workflows/Unit/LayoutEntityTests/DraftLayoutUpsertTests`: all three `[Fact]` methods | Preserve layout aggregate behavior. | Workflow lifecycle suite, T021. | — | Pending | — |
| `Workflows/Unit/MigrationSmokeTests`: `Migration_chain_applies_cleanly_and_drops_the_validations_table`; `Drop_migration_replays_cleanly_when_the_validations_table_was_never_created` | EF migration mechanism only. | Groundwork schema evolution/CLI suite, T061/T064. | — | Pending | — |
| `Workflows/Unit/PromotionGateTests`: all three `[Fact]` methods | Preserve validation gate and promotion outcomes. | Workflow lifecycle suite, T021. | — | Pending | — |
| `Workflows/Unit/StateSourceHandlerRegistrationTests`: both `[Fact]` methods | Preserve serialization availability; EF handler registration is mechanism-specific. | Groundwork registration and serializer scenarios, T021/T035. | — | Pending | — |
| `Workflows/Unit/SubmitWorkflowDefinitionCommandTests`: all three `[Fact]` methods | Preserve complete aggregate submission and validation rejection. | Workflow atomicity/lifecycle suite, T021/T023. | — | Pending | — |
| `Workflows/Unit/UpdateDraftCommand/ActivityDiffTests`: all three `[Fact]` methods | Preserve authored-state diff/event behavior. | Workflow lifecycle suite, T021. | — | Pending | — |
| `Workflows/Unit/UpdateDraftCommand/LastWriterWinsTests`: `Stale_write_overwrites_concurrent_changes_without_conflict` | Preserve the currently public conflict policy until an approved change replaces it. | Workflow OCC suite, T024. | — | Pending | — |
| `Workflows/Unit/UpdateDraftCommand/LockingTests`: all three `[Fact]` methods | Preserve per-draft locking semantics. | Workflow concurrency suite, T023/T024. | — | Pending | — |
| `Workflows/Unit/UpdateDraftCommand/MultiDimensionDiffTests`: both `[Fact]` methods | Preserve deterministic diff/event ordering. | Workflow lifecycle suite, T021. | — | Pending | — |
| `Workflows/Unit/UpdateDraftCommand/PipelineAbsorptionTests`: all three `[Fact]` methods | Preserve public command behavior; retired implementation-type assertion is removable mechanism. | Workflow lifecycle suite, T021. | — | Pending | — |
| `Workflows/Unit/UpdateDraftCommand/ShellOrderingTests`: `One_execute_runs_the_new_shell_in_order_with_a_single_lock_and_transaction` | Preserve lock and transaction semantics. | Workflow atomicity suite, T023. | — | Pending | — |
| `Workflows/Unit/WorkflowDefinitionMetadataUpdateTests`: all four `[Fact]` methods | Preserve metadata update behavior and aggregate isolation. | Workflow lifecycle suite, T021. | — | Pending | — |
| `Workflows/Unit/WorkflowDefinitionSoftDeleteTests`: all seven `[Fact]` methods | Preserve soft-delete and permanent-delete behavior. | Workflow lifecycle/atomicity suite, T021/T023. | — | Pending | — |

## Excluded helper sources

`InMemoryReconcilerHarness`, `TestDbContextFactory`, `ThrowingStores`,
`ActivitiesDesignTestHost`, and `WorkflowsDesignTestHost` have no test identities.
They are EF test infrastructure and remain until the approved ledger decisions allow
their dependent tests to move or be removed.
