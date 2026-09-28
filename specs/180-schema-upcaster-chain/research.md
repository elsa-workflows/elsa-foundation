# Research: Schema families, stamping and read paths as they exist today

**Spec**: [spec.md](./spec.md)

**Grounded in**: the tree at the time of writing, per spec.md's "Current state" summary.

---

## Current state

| EF module | Schema families (declaring class and current version) |
|---|---|
| `Workflows.Runtime` | `BookmarkStateEfModule`, `RuntimeActivationSlotEfModule`, `RuntimeActivityExecutionEfModule`, `RuntimeArtifactEfModule`, `RuntimeOperationalStateEfModule`, `RuntimePostCommitOutboxEfModule`, `RuntimeSchedulerPoisonEfModule`, `RuntimeTriggerBindingEfModule`, `RuntimeWorkflowAlterationEfModule`, `RuntimeWorkflowDispatchEfModule`, `RuntimeWorkflowExecutionEfModule`, `RuntimeWorkflowTestScopeEfModule`, all `SchemaVersion = "1.0.0"` |
| `Workflows.Publishing` | `PublishingLedgerEfModule.ContentSchemaVersion = "1"` (activity-publication receipts, draft test runs) and `PublishingPolicyProjectionEfModule.SchemaVersion = "1.0.0"` (policies, projection intents). Publication records and snapshot reviews carry no stamp. |
| `Elsa3.Activities.Design.Import` | `Elsa3ImportEfModule.SchemaVersion = "1.0.0"` |
| `Activities.Design`, `Diagnostics.OpenTelemetry`, `Diagnostics.StructuredLogs`, `Identity.Iam`, `Identity.ProviderConfiguration`, `Secrets`, `Studio.Preferences`, `Workflows.Design`, `Workflows.Runtime.Distributed.Placement`, `Workflows.Runtime.Distributed.CommandTransport` | None. Their rows carry no persisted-schema stamp. `StudioPreferenceRecord.SchemaVersion` holds a client-supplied preference schema, and `Activities.Design`'s `MaterialSchemaVersion` versions idempotency material, so neither is a row stamp. |

- **Stamping.** Every write assigns the family's compile-time constant to the row's `SchemaVersion` column. There are
  38 such assignments across 30 store files under `src/essentials/Workflows/Runtime/Persistence/EntityFrameworkCore`,
  `src/essentials/Workflows/Publishing/Persistence/EntityFrameworkCore` and
  `src/extensions/Elsa3/src/Activities/Design/Import/Persistence/EntityFrameworkCore`. The stamp is a column beside
  the content. No hash covers it: the identity hashes cover scope and id, `Elsa3ImportRecordCodec`'s `ContentHash`
  covers the stored JSON, and its binding hash covers domain fields only.
- **Checking.** `EfSchemaVersion` in `src/essentials/Persistence/EntityFramework` is the one comparison. It is an
  ordinal equality check, so an older row is refused exactly like a newer one, and a missing stamp is refused as
  skew. It raises `EfSchemaVersionSkewException`, which a test in `EfSchemaVersionTests` keeps unassignable to every
  exception type the stores' catch filters name. There are 36 call sites in 30 files, including two readers outside
  the owning module: `EfWorkflowPortfolioDataSource` and `EfWorkflowRunHealthDataSource` in `Workflows.Dashboard`
  read `RuntimeArtifact` and `RuntimeOperationalState` rows directly. Each call site passes the family name as a
  string literal.
- **Ordering.** #1955 moved the version term to the front of its condition, and `EfSchemaVersionOrderingGuardTests`
  fails the build when another clause precedes a `Readable` or `NotReadable` term *inside one condition*. It does not
  see separate statements. `EfExecutionLivenessStateStore`, `EfWorkflowHoldStateStore`,
  `EfWorkflowAlterationStore` (`ReadPlan`) and `WorkflowTestScopeEfSupport` deserialize the content into the current
  type before the version term runs. `EfActivityPublicationReceiptStore` and `EfActivityDraftTestRunStore` check
  identity projections in an earlier statement.
- **Content.** Most families store their content as a JSON document. `RuntimeArtifactJson` and `PublishingEfJson`
  ignore unknown members on read, since no store sets `JsonUnmappedMemberHandling.Disallow`, and
  `RuntimeArtifactJson` writes null members. A predecessor's reader therefore tolerates members it does not know,
  and a predecessor that rewrites a newer row drops them without an error. ADR 0077's amendment names that
  data-loss mechanism.
- **Fixtures.** No Runtime or Publishing EF family has a committed payload fixture. The only golden fixtures over
  persisted runtime shapes are the Distributed leaf's `Fixtures/v1/executionPlacement.json` and
  `executionCommandTransport.json`, driven by `GoldenFixtureTestSupport`, whose failure message already states the
  rule this spec adopts: "bump the schema version, add an upcaster, add a new versioned fixture, and keep the old
  one".
- **Compression.** Spec 170's `EfPayloadCodec` marks a compressed payload in band (`elsaz1.`), below the schema
  version, so it is independent of this chain.
