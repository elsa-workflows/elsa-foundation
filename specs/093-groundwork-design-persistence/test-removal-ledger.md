# Test-removal approval ledger

**Work unit**: `093-groundwork-design-persistence`
**Baseline**: `origin/main` at `d1548991f` on 2026-07-20

This is the T003 intake ledger. It inventories every test method in a source file
that directly references EF Core or an EF design implementation. Parameterized
display variants are covered by their source method. Every row is deliberately
`Pending`: no row authorizes deletion until an architect records a decision and
the stated replacement evidence exists.

| Test identity | Objective classification | Replacement evidence / rationale | Architect | Decision | Date |
|---|---|---|---|---|---|
| `Activities/Integration/CrossContextLifecycleTests.ActivitiesDesignDbContext_ImmutableEnforcement_AppliesToActivityDefinition` | Preserve immutable-write intent; EF metadata assertion is mechanism-specific. | Groundwork stale-write/scope suite, T021–T024 and T075. | — | Pending | — |
| `Activities/Integration/CrossContextLifecycleTests.ActivitiesDesignDbContext_RegistersTenantIdIndex_OnTenantEntityDescendants` | Preserve tenancy-index intent; EF metadata assertion is mechanism-specific. | Groundwork stale-write/scope suite, T021–T024 and T075. | — | Pending | — |
| `Activities/Integration/CrossContextLifecycleTests.WorkflowsDesignDbContext_ImmutableEnforcement_AppliesToWorkflowDefinitionVersion` | Preserve immutable-write intent; EF metadata assertion is mechanism-specific. | Groundwork stale-write/scope suite, T021–T024 and T075. | — | Pending | — |
| `Activities/Integration/CrossContextLifecycleTests.WorkflowsDesignDbContext_ImmutableEnforcement_AppliesToVersionLayout` | Preserve immutable-write intent; EF metadata assertion is mechanism-specific. | Groundwork stale-write/scope suite, T021–T024 and T075. | — | Pending | — |
| `Activities/Integration/CrossContextLifecycleTests.BothContexts_BaseEntityProperties_AreImmutable` | Preserve immutable-write intent; EF metadata assertion is mechanism-specific. | Groundwork stale-write/scope suite, T021–T024 and T075. | — | Pending | — |
| `Activities/Integration/CrossContextLifecycleTests.WorkflowsDesignDbContext_RegistersTenantIdIndex_OnTenantEntityDescendants` | Preserve tenancy-index intent; EF metadata assertion is mechanism-specific. | Groundwork stale-write/scope suite, T021–T024 and T075. | — | Pending | — |
| `Activities/Integration/CrossContextLifecycleTests.BothContexts_ShareRowNumberIndex_OnEveryEntity` | Preserve deterministic row ordering intent; EF metadata assertion is mechanism-specific. | Groundwork query/physical-route suite, T038/T045. | — | Pending | — |
| `Activities/Integration/ExactVersionResolutionTests.ResolvesExistingVersion_AndReturnsNoneForAbsent` | Preserve exact-version identity. | Activity query suite, T022/T038. | — | Pending | — |
| `Activities/Integration/ExactVersionResolutionTests.BuildMetadataIsIgnored_WhenResolving` | Preserve exact-version identity. | Activity query suite, T022/T038. | — | Pending | — |
| `Activities/Integration/VersionOrderingTests.ListingOrdersByPrecedence_MajorMinor_DescendingDbSide` | Preserve SemVer ordering. | Activity query suite and physical-route plans, T038/T045. | — | Pending | — |
| `Activities/Integration/VersionOrderingTests.ListingOrdersByPrecedence_Patch_MultiDigitDescendingDbSide` | Preserve SemVer ordering. | Activity query suite and physical-route plans, T038/T045. | — | Pending | — |
| `Activities/Registration/FeatureRegistrationTests.ActivitiesRuntimeFeature_RegistersCanonicalMaterializationAndTypeDiscovery` | Preserve feature composition. | T062 provider-registration suite. | — | Pending | — |
| `Activities/Registration/FeatureRegistrationTests.ActivitiesDesignReconciliationFeature_RegistersReconcilerHasherStartupTaskAndHandler` | Preserve feature composition. | T062 provider-registration suite. | — | Pending | — |
| `Activities/Registration/FeatureRegistrationTests.ActivitiesDesignApiFeature_RegistersActivityAvailabilityEvaluator` | Preserve feature composition. | T062 provider-registration suite. | — | Pending | — |
| `Activities/Registration/FeatureRegistrationTests.ActivitiesDesignApiFeature_Registers_Canonical_Contract_Proposal_Capability_Relations` | Preserve feature composition. | T062 provider-registration suite. | — | Pending | — |
| `Activities/Registration/FeatureRegistrationTests.ActivitiesDesignApiFeature_Registers_Request_Scoped_Tenant_And_Permission_Authorization` | Preserve feature composition. | T062 provider-registration suite. | — | Pending | — |
| `Activities/Registration/FeatureRegistrationTests.ActivitiesDesignApiFeature_Provides_A_Process_Stable_Development_Cursor_Key_When_Host_Omits_One` | Preserve feature composition. | T062 provider-registration suite. | — | Pending | — |
| `Activities/Registration/FeatureRegistrationTests.ActivitiesDesignApiFeature_RegistersActivityAvailabilitySettingsStore` | Preserve feature composition. | T062 provider-registration suite. | — | Pending | — |
| `Activities/Registration/FeatureRegistrationTests.ActivitiesDesignApiFeature_RegistersActivityAvailabilityDiagnosticsProjector` | Preserve feature composition. | T062 provider-registration suite. | — | Pending | — |
| `Activities/Registration/FeatureRegistrationTests.ActivitiesDesignApiFeature_UsesHostConfiguredActivityAvailabilityOptions` | Preserve feature composition. | T062 provider-registration suite. | — | Pending | — |
| `Activities/Registration/FeatureRegistrationTests.ClrActivityReconciliationFeature_RegistersClrReconciliationSource` | Preserve feature composition. | T062 provider-registration suite. | — | Pending | — |
| `Activities/Registration/FeatureRegistrationTests.SqliteActivitiesDesignPersistenceShellFeature_RegistersLookupAndSavingHandler` | Preserve feature composition; SQLite EF-shell assertion is mechanism-specific. | T062 provider-registration suite. | — | Pending | — |
| `Activities/Unit/ActivityDefinitionIdentityTests.ActivityTypeKey_IsImmutable_AfterInsert` | Preserve identity/uniqueness invariant. | Activity lifecycle and OCC suite, T022–T024. | — | Pending | — |
| `Activities/Unit/ActivityDefinitionIdentityTests.Identity_SurvivesNewVersionWithDifferentTypeInfo` | Preserve identity/uniqueness invariant. | Activity lifecycle and OCC suite, T022–T024. | — | Pending | — |
| `Activities/Unit/ActivityDefinitionIdentityTests.DuplicateActivityTypeKey_ThrowsOnInsert` | Preserve identity/uniqueness invariant. | Activity lifecycle and OCC suite, T022–T024. | — | Pending | — |
| `Activities/Unit/ActivityDefinitionLookupTests.ListDefinitions_SearchTerm_MatchesActivityTypeKey` | Preserve lookup behavior. | Activity query and scope suite, T038/T024. | — | Pending | — |
| `Activities/Unit/ActivityDefinitionLookupTests.ListDefinitions_ForwardsTenantAgnosticToTheFilter` | Preserve explicit privileged-access intent. | Activity query and scope suite, T038/T024. | — | Pending | — |
| `Activities/Unit/DescriptorPersistenceRoundTripTests.ActivityDefinitionVersion_RoundTripsDescriptor_AsOpaqueTypeAndPayload` | Preserve logical serialization behavior. | Activity lifecycle suite, T022. | — | Pending | — |
| `Activities/Unit/DescriptorPersistenceRoundTripTests.MalformedDescriptorPayload_SoftFailsToDefault_WithoutThrowing` | Preserve logical serialization behavior. | Activity lifecycle suite, T022. | — | Pending | — |
| `Activities/Unit/EFCoreActivityDefinitionVersionStoreTests.GetWithDefinition_throws_EntityNotFound_when_absent` | Preserve public store outcome; EF implementation class is removable. | Activity lifecycle suite, T022. | — | Pending | — |
| `Activities/Unit/EFCoreActivityDefinitionVersionStoreTests.GetWithDefinition_loads_owning_definition` | Preserve public store outcome; EF implementation class is removable. | Activity lifecycle suite, T022. | — | Pending | — |
| `Activities/Unit/SavingEventDispatchTests.SavingHandler_ProducesShadowDescriptor` | Preserve payload round-trip; EF event aggregator is mechanism-specific. | Groundwork serializer and activity contract scenarios, T022/T035. | — | Pending | — |
| `Activities/Unit/SavingEventDispatchTests.LoadingHandler_RehydratesDesignFacets` | Preserve payload round-trip; EF event aggregator is mechanism-specific. | Groundwork serializer and activity contract scenarios, T022/T035. | — | Pending | — |
| `Activities/Unit/SavingEventDispatchTests.Aggregator_DispatchesToEveryRegisteredTypedHandler` | Preserve payload round-trip; EF event aggregator is mechanism-specific. | Groundwork serializer and activity contract scenarios, T022/T035. | — | Pending | — |
| `Activities/Unit/SavingEventDispatchTests.Aggregator_IsNoOp_ForUnrelatedEntities` | Preserve payload round-trip; EF event aggregator is mechanism-specific. | Groundwork serializer and activity contract scenarios, T022/T035. | — | Pending | — |
| `Activities/Unit/SavingEventDispatchTests.ExactlyOneEventHandler_HandlesOnEntitySaving` | Preserve payload round-trip; EF event aggregator is mechanism-specific. | Groundwork serializer and activity contract scenarios, T022/T035. | — | Pending | — |
| `Workflows/EFCoreWorkflowPortfolioDataSourceTests.ReturnsTheCompletePortfolioFixtureBeyondOnePage` | Preserve bounded portfolio result semantics; EF data source is removable. | Workflow query-scale suite, T037/T039. | — | Pending | — |
| `Workflows/Unit/CrossFeatureValidatorSubscriptionTests.Cross_feature_validator_contributes_errors_surfaced_on_OnDraftValidated` | Preserve validation-gate behavior. | Workflow lifecycle suite, T021. | — | Pending | — |
| `Workflows/Unit/CrossFeatureValidatorSubscriptionTests.Cross_feature_and_baseline_validators_both_contribute_in_the_same_pass` | Preserve validation-gate behavior. | Workflow lifecycle suite, T021. | — | Pending | — |
| `Workflows/Unit/DraftMutationCommandTests/CloneDraftFromVersionTests.Clone_deep_copies_State_from_source_Version` | Preserve copied state/layout/provenance and lifecycle events. | Workflow lifecycle suite, T021. | — | Pending | — |
| `Workflows/Unit/DraftMutationCommandTests/CloneDraftFromVersionTests.Clone_deep_copies_Layout_records_from_source_Version` | Preserve copied state/layout/provenance and lifecycle events. | Workflow lifecycle suite, T021. | — | Pending | — |
| `Workflows/Unit/DraftMutationCommandTests/CloneDraftFromVersionTests.Clone_carries_NodeIds_one_to_one` | Preserve copied state/layout/provenance and lifecycle events. | Workflow lifecycle suite, T021. | — | Pending | — |
| `Workflows/Unit/DraftMutationCommandTests/CloneDraftFromVersionTests.Clone_publishes_OnDraftCreated_carrying_the_source_version_id` | Preserve copied state/layout/provenance and lifecycle events. | Workflow lifecycle suite, T021. | — | Pending | — |
| `Workflows/Unit/DraftMutationCommandTests/CloneDraftFromVersionTests.Clone_persists_the_source_version_id_on_the_Draft_entity` | Preserve copied state/layout/provenance and lifecycle events. | Workflow lifecycle suite, T021. | — | Pending | — |
| `Workflows/Unit/DraftMutationCommandTests/CreateDraftTests.CreateDraft_persists_the_draft_with_a_layout_sibling` | Preserve draft aggregate creation and event behavior. | Workflow lifecycle suite, T021. | — | Pending | — |
| `Workflows/Unit/DraftMutationCommandTests/CreateDraftTests.CreateDraft_publishes_OnDraftCreated_with_no_source_version_id` | Preserve draft aggregate creation and event behavior. | Workflow lifecycle suite, T021. | — | Pending | — |
| `Workflows/Unit/DraftMutationCommandTests/DiscardDraftTests.Discard_deletes_Draft_and_Layout_atomically` | Preserve atomic discard behavior. | Workflow atomicity suite, T021/T023. | — | Pending | — |
| `Workflows/Unit/DraftMutationCommandTests/DiscardDraftTests.Discard_does_not_touch_any_WorkflowDefinitionVersion` | Preserve atomic discard behavior. | Workflow atomicity suite, T021/T023. | — | Pending | — |
| `Workflows/Unit/DraftMutationCommandTests/DiscardDraftTests.Discard_publishes_OnDraftDiscarded` | Preserve atomic discard behavior. | Workflow atomicity suite, T021/T023. | — | Pending | — |
| `Workflows/Unit/DraftMutationCommandTests/DiscardDraftTests.Second_discard_on_same_id_is_a_no_op` | Preserve atomic discard behavior. | Workflow atomicity suite, T021/T023. | — | Pending | — |
| `Workflows/Unit/EventSourcingContractTests.No_mutation_event_is_published_even_when_a_subscriber_is_registered` | Preserve event failure policy and durable outcome. | Workflow lifecycle/atomicity suite, T021/T023. | — | Pending | — |
| `Workflows/Unit/EventSourcingContractTests.The_command_still_completes_and_persists_with_no_mutation_subscriber` | Preserve event failure policy and durable outcome. | Workflow lifecycle/atomicity suite, T021/T023. | — | Pending | — |
| `Workflows/Unit/EventSourcingContractTests.Background_shielding_a_throwing_OnDraftValidated_subscriber_does_not_break_Execute_or_lose_the_Draft` | Preserve event failure policy and durable outcome. | Workflow lifecycle/atomicity suite, T021/T023. | — | Pending | — |
| `Workflows/Unit/LayoutEntityTests/DraftLayoutUpsertTests.AddWorkflowDefinition_creates_an_empty_layout_row_at_origin` | Preserve layout aggregate behavior. | Workflow lifecycle suite, T021. | — | Pending | — |
| `Workflows/Unit/LayoutEntityTests/DraftLayoutUpsertTests.AddWorkflowDefinition_persists_the_initial_layout_at_origin` | Preserve layout aggregate behavior. | Workflow lifecycle suite, T021. | — | Pending | — |
| `Workflows/Unit/LayoutEntityTests/DraftLayoutUpsertTests.UpdateDraft_upserts_a_layout_row_when_the_draft_has_none` | Preserve layout aggregate behavior. | Workflow lifecycle suite, T021. | — | Pending | — |
| `Workflows/Unit/MigrationSmokeTests.Migration_chain_applies_cleanly_and_drops_the_validations_table` | EF migration mechanism only. | Groundwork schema evolution/CLI suite, T061/T064. | — | Pending | — |
| `Workflows/Unit/MigrationSmokeTests.Drop_migration_replays_cleanly_when_the_validations_table_was_never_created` | EF migration mechanism only. | Groundwork schema evolution/CLI suite, T061/T064. | — | Pending | — |
| `Workflows/Unit/PromotionGateTests.Promotion_throws_when_the_draft_state_produces_validation_errors` | Preserve validation-gate and promotion outcomes. | Workflow lifecycle suite, T021. | — | Pending | — |
| `Workflows/Unit/PromotionGateTests.Promotion_succeeds_when_the_draft_state_produces_no_errors` | Preserve validation-gate and promotion outcomes. | Workflow lifecycle suite, T021. | — | Pending | — |
| `Workflows/Unit/PromotionGateTests.Draft_referencing_unknown_activity_version_cannot_be_promoted` | Preserve validation-gate and promotion outcomes. | Workflow lifecycle suite, T021. | — | Pending | — |
| `Workflows/Unit/StateSourceHandlerRegistrationTests.Loading_handlers_resolve_for_both_draft_and_version` | Preserve serialization availability; EF handler registration is mechanism-specific. | Groundwork registration and serializer scenarios, T021/T035. | — | Pending | — |
| `Workflows/Unit/StateSourceHandlerRegistrationTests.Saving_handlers_resolve_for_both_draft_and_version` | Preserve serialization availability; EF handler registration is mechanism-specific. | Groundwork registration and serializer scenarios, T021/T035. | — | Pending | — |
| `Workflows/Unit/SubmitWorkflowDefinitionCommandTests.Execute_persists_definition_draft_and_initial_version_with_same_state` | Preserve complete aggregate submission and validation rejection. | Workflow atomicity/lifecycle suite, T021/T023. | — | Pending | — |
| `Workflows/Unit/SubmitWorkflowDefinitionCommandTests.Execute_rejects_root_activity_without_activity_version_id` | Preserve complete aggregate submission and validation rejection. | Workflow atomicity/lifecycle suite, T021/T023. | — | Pending | — |
| `Workflows/Unit/SubmitWorkflowDefinitionCommandTests.Execute_rejects_missing_root_activity` | Preserve complete aggregate submission and validation rejection. | Workflow atomicity/lifecycle suite, T021/T023. | — | Pending | — |
| `Workflows/Unit/UpdateDraftCommand/ActivityDiffTests.Adding_an_activity_emits_OnActivityAddedToDraft` | Preserve authored-state diff/event behavior. | Workflow lifecycle suite, T021. | — | Pending | — |
| `Workflows/Unit/UpdateDraftCommand/ActivityDiffTests.Adding_an_activity_persists_it` | Preserve authored-state diff/event behavior. | Workflow lifecycle suite, T021. | — | Pending | — |
| `Workflows/Unit/UpdateDraftCommand/ActivityDiffTests.Removing_an_activity_emits_OnActivityRemovedFromDraft` | Preserve authored-state diff/event behavior. | Workflow lifecycle suite, T021. | — | Pending | — |
| `Workflows/Unit/UpdateDraftCommand/LastWriterWinsTests.Stale_write_overwrites_concurrent_changes_without_conflict` | Preserve current public conflict policy until an approved change replaces it. | Workflow OCC suite, T024. | — | Pending | — |
| `Workflows/Unit/UpdateDraftCommand/LockingTests.One_execute_acquires_the_draft_lock_exactly_once` | Preserve per-draft locking semantics. | Workflow concurrency suite, T023/T024. | — | Pending | — |
| `Workflows/Unit/UpdateDraftCommand/LockingTests.Concurrent_calls_on_the_same_draft_serialise_and_both_complete` | Preserve per-draft locking semantics. | Workflow concurrency suite, T023/T024. | — | Pending | — |
| `Workflows/Unit/UpdateDraftCommand/LockingTests.Calls_on_different_drafts_use_distinct_keys` | Preserve per-draft locking semantics. | Workflow concurrency suite, T023/T024. | — | Pending | — |
| `Workflows/Unit/UpdateDraftCommand/MultiDimensionDiffTests.Multi_dimension_change_emits_events_in_deterministic_order` | Preserve deterministic diff/event ordering. | Workflow lifecycle suite, T021. | — | Pending | — |
| `Workflows/Unit/UpdateDraftCommand/MultiDimensionDiffTests.Multi_dimension_change_persists_the_desired_state` | Preserve deterministic diff/event ordering. | Workflow lifecycle suite, T021. | — | Pending | — |
| `Workflows/Unit/UpdateDraftCommand/PipelineAbsorptionTests.DraftMutationPipeline_type_no_longer_exists` | Preserve public command behavior; retired implementation-type assertion is mechanism-specific. | Workflow lifecycle suite, T021. | — | Pending | — |
| `Workflows/Unit/UpdateDraftCommand/PipelineAbsorptionTests.No_Draft_command_takes_a_pipeline_collaborator` | Preserve public command behavior; retired implementation-type assertion is mechanism-specific. | Workflow lifecycle suite, T021. | — | Pending | — |
| `Workflows/Unit/UpdateDraftCommand/PipelineAbsorptionTests.Lifecycle_create_then_discard_still_round_trips` | Preserve public command behavior; retired implementation-type assertion is mechanism-specific. | Workflow lifecycle suite, T021. | — | Pending | — |
| `Workflows/Unit/UpdateDraftCommand/ShellOrderingTests.One_execute_runs_the_new_shell_in_order_with_a_single_lock_and_transaction` | Preserve lock and transaction semantics. | Workflow atomicity suite, T023. | — | Pending | — |
| `Workflows/Unit/WorkflowDefinitionMetadataUpdateTests.Updating_name_and_description_persists_both_fields` | Preserve metadata update behavior and aggregate isolation. | Workflow lifecycle suite, T021. | — | Pending | — |
| `Workflows/Unit/WorkflowDefinitionMetadataUpdateTests.Partial_update_leaves_the_unspecified_field_untouched` | Preserve metadata update behavior and aggregate isolation. | Workflow lifecycle suite, T021. | — | Pending | — |
| `Workflows/Unit/WorkflowDefinitionMetadataUpdateTests.Updating_metadata_bumps_last_modified_timestamp` | Preserve metadata update behavior and aggregate isolation. | Workflow lifecycle suite, T021. | — | Pending | — |
| `Workflows/Unit/WorkflowDefinitionMetadataUpdateTests.Updating_metadata_does_not_touch_versions_or_draft` | Preserve metadata update behavior and aggregate isolation. | Workflow lifecycle suite, T021. | — | Pending | — |
| `Workflows/Unit/WorkflowDefinitionSoftDeleteTests.From_preserves_soft_delete_metadata_round_trip` | Preserve soft-delete and permanent-delete behavior. | Workflow lifecycle/atomicity suite, T021/T023. | — | Pending | — |
| `Workflows/Unit/WorkflowDefinitionSoftDeleteTests.From_leaves_soft_delete_metadata_unset_for_non_deleted_source` | Preserve soft-delete and permanent-delete behavior. | Workflow lifecycle/atomicity suite, T021/T023. | — | Pending | — |
| `Workflows/Unit/WorkflowDefinitionSoftDeleteTests.From_leaves_soft_delete_metadata_unset_for_non_entity_source` | Preserve soft-delete and permanent-delete behavior. | Workflow lifecycle/atomicity suite, T021/T023. | — | Pending | — |
| `Workflows/Unit/WorkflowDefinitionSoftDeleteTests.Soft_deleted_definitions_are_excluded_from_active_query_and_included_in_deleted_query` | Preserve soft-delete and permanent-delete behavior. | Workflow lifecycle/atomicity suite, T021/T023. | — | Pending | — |
| `Workflows/Unit/WorkflowDefinitionSoftDeleteTests.Restore_clears_soft_delete_metadata` | Preserve soft-delete and permanent-delete behavior. | Workflow lifecycle/atomicity suite, T021/T023. | — | Pending | — |
| `Workflows/Unit/WorkflowDefinitionSoftDeleteTests.Permanent_delete_removes_definition_dependents_transactionally_after_soft_delete` | Preserve soft-delete and permanent-delete behavior. | Workflow lifecycle/atomicity suite, T021/T023. | — | Pending | — |
| `Workflows/Unit/WorkflowDefinitionSoftDeleteTests.Permanent_delete_command_rejects_an_active_definition` | Preserve soft-delete and permanent-delete behavior. | Workflow lifecycle/atomicity suite, T021/T023. | — | Pending | — |

## Inventory proof

The 2026-07-20 baseline search found 28 test source files with an EF/EF-design
reference and six support-only sources. The 28 test sources contain 90 public test
methods, and the table above contains exactly 90 method rows. Re-run this check before
adding or deleting a ledger row:

```bash
sources=$(rg -l 'EntityFrameworkCore|EFCore|DbContext' \
  tests/Elsa/Activities/Design/Tests tests/Elsa/Workflows/Design/Tests \
  | rg '\\.cs$' \
  | rg -v '(InMemoryReconcilerHarness|TestDbContextFactory|ThrowingStores|ActivitiesDesignTestHost|WorkflowsDesignTestHost|StubActivityCatalog)')

printf 'test sources: '; printf '%s\n' "$sources" | wc -l
printf 'test methods: '; printf '%s\n' "$sources" | xargs rg '^    public (?:async )?(?:Task|void|ValueTask)' | wc -l
printf 'ledger rows: '; rg '^\\| `(Activities|Workflows)/' \
  specs/093-groundwork-design-persistence/test-removal-ledger.md | wc -l
```

Expected output: `28` test sources, `90` test methods, and `90` ledger rows. The
normalized source-to-ledger identity comparison produced no differences at capture
time. The excluded files below are direct-EF support code with no test identities.

## Excluded helper sources

`InMemoryReconcilerHarness`, `TestDbContextFactory`, `ThrowingStores`,
`ActivitiesDesignTestHost`, `WorkflowsDesignTestHost`, and `StubActivityCatalog` have
no test identities. They are EF test infrastructure and remain until the approved
ledger decisions allow their dependent tests to move or be removed.
