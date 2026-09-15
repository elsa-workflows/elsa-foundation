using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Migrations.BookmarkState.SqlServer
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "elsa_runtime_activity_execution_hierarchy",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ScopeKey = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ScopeKeyHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    WorkflowExecutionId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    WorkflowExecutionIdHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ActivityExecutionId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    ActivityExecutionIdHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ActivityExecutionIdOrderKey = table.Column<string>(type: "nvarchar(655)", maxLength: 655, nullable: false),
                    ExecutionScopeId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    ExecutionScopeIdHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ParentActivityExecutionId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    ParentActivityExecutionIdHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    IsScopeRoot = table.Column<bool>(type: "bit", nullable: false),
                    ExecutionSequence = table.Column<long>(type: "bigint", nullable: false),
                    ContentJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SchemaVersion = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_runtime_activity_execution_hierarchy", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "elsa_runtime_activity_execution_inspection",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ScopeKey = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ScopeKeyHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    WorkflowExecutionId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    WorkflowExecutionIdHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ActivityExecutionId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    ActivityExecutionIdHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ActivityExecutionIdOrderKey = table.Column<string>(type: "nvarchar(655)", maxLength: 655, nullable: false),
                    ExecutionScopeId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    ExecutionScopeIdHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    SummaryExecutionSequence = table.Column<long>(type: "bigint", nullable: false),
                    SummaryScheduledAtUtcTicks = table.Column<long>(type: "bigint", nullable: false),
                    SummaryScheduledAtOffsetMinutes = table.Column<int>(type: "int", nullable: false),
                    ContentJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SchemaVersion = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_runtime_activity_execution_inspection", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "elsa_runtime_activity_execution_state",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ScopeKey = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ScopeKeyHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    WorkflowExecutionId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    WorkflowExecutionIdHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ActivityExecutionId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    ActivityExecutionIdHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ActivityExecutionIdOrderKey = table.Column<string>(type: "nvarchar(655)", maxLength: 655, nullable: false),
                    ParentActivityExecutionId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    ParentActivityExecutionIdHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ExecutionScopeId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    ExecutionScopeIdHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    ExecutionSequence = table.Column<long>(type: "bigint", nullable: false),
                    ScheduledAtUtcTicks = table.Column<long>(type: "bigint", nullable: false),
                    ScheduledAtOffsetMinutes = table.Column<int>(type: "int", nullable: false),
                    ContentJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SchemaVersion = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_runtime_activity_execution_state", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "elsa_runtime_bookmark_state",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ScopeKey = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ScopeKeyHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    WorkflowExecutionId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    WorkflowExecutionIdHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    WorkflowExecutionIdOrderKey = table.Column<string>(type: "nvarchar(645)", maxLength: 645, nullable: false),
                    BookmarkId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    BookmarkIdHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    BookmarkIdOrderKey = table.Column<string>(type: "nvarchar(645)", maxLength: 645, nullable: false),
                    ActivityExecutionId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ExecutableNodeId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ResumeTargetId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    StimulusType = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    StimulusHash = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    StimulusLookupKey = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    StimulusTypeLookupKey = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    PayloadJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ContentJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    MetadataJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SchemaVersion = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    CreatedAtUtcTicks = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAtOffsetMinutes = table.Column<int>(type: "int", nullable: false),
                    ExpiresAtUtcTicks = table.Column<long>(type: "bigint", nullable: true),
                    ExpiresAtOffsetMinutes = table.Column<int>(type: "int", nullable: true),
                    Revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_runtime_bookmark_state", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "elsa_runtime_checkpoint_commit",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(264)", maxLength: 264, nullable: false),
                    ScopeKey = table.Column<string>(type: "nvarchar(684)", maxLength: 684, nullable: false),
                    ScopeKeyHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CommitId = table.Column<string>(type: "nvarchar(344)", maxLength: 344, nullable: false),
                    CommitIdHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CommitIdOrderKey = table.Column<string>(type: "nvarchar(516)", maxLength: 516, nullable: false),
                    WorkflowExecutionId = table.Column<string>(type: "nvarchar(344)", maxLength: 344, nullable: false),
                    WorkflowExecutionIdHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    WorkflowExecutionIdOrderKey = table.Column<string>(type: "nvarchar(516)", maxLength: 516, nullable: false),
                    OccurredAtUtcTicks = table.Column<long>(type: "bigint", nullable: false),
                    Fingerprint = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ContentJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PendingPostCommitWorkIdsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ConsumedSchedulerWorkItemIdsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SchemaVersion = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_runtime_checkpoint_commit", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "elsa_runtime_durable_timer",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ScopeKey = table.Column<string>(type: "nvarchar(max)", maxLength: 684, nullable: false),
                    ScopeKeyHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    WorkflowExecutionId = table.Column<string>(type: "nvarchar(344)", maxLength: 344, nullable: false),
                    WorkflowExecutionIdHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    WorkflowExecutionIdOrderKey = table.Column<string>(type: "nvarchar(516)", maxLength: 516, nullable: false),
                    TimerId = table.Column<string>(type: "nvarchar(344)", maxLength: 344, nullable: false),
                    TimerIdHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    TimerIdOrderKey = table.Column<string>(type: "nvarchar(516)", maxLength: 516, nullable: false),
                    StimulusType = table.Column<string>(type: "nvarchar(max)", maxLength: 684, nullable: false),
                    StimulusHash = table.Column<string>(type: "nvarchar(max)", maxLength: 1200, nullable: false),
                    DueTimeUtcTicks = table.Column<long>(type: "bigint", nullable: false),
                    DueTimeOffsetMinutes = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtcTicks = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAtOffsetMinutes = table.Column<int>(type: "int", nullable: false),
                    ClaimOrderKey = table.Column<string>(type: "nvarchar(84)", maxLength: 84, nullable: false),
                    ClaimOwnerId = table.Column<string>(type: "nvarchar(max)", maxLength: 344, nullable: true),
                    ClaimToken = table.Column<long>(type: "bigint", nullable: false),
                    ClaimedAtUtcTicks = table.Column<long>(type: "bigint", nullable: true),
                    ClaimedAtOffsetMinutes = table.Column<int>(type: "int", nullable: true),
                    VisibleAfterUtcTicks = table.Column<long>(type: "bigint", nullable: true),
                    VisibleAfterOffsetMinutes = table.Column<int>(type: "int", nullable: true),
                    FailureCount = table.Column<int>(type: "int", nullable: false),
                    ContentJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SchemaVersion = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_runtime_durable_timer", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "elsa_runtime_durable_value_state",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(264)", maxLength: 264, nullable: false),
                    ScopeKey = table.Column<string>(type: "nvarchar(max)", maxLength: 684, nullable: false),
                    ScopeKeyHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    WorkflowExecutionId = table.Column<string>(type: "nvarchar(344)", maxLength: 344, nullable: false),
                    WorkflowExecutionIdHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    WorkflowExecutionIdOrderKey = table.Column<string>(type: "nvarchar(516)", maxLength: 516, nullable: false),
                    DurableValueId = table.Column<string>(type: "nvarchar(344)", maxLength: 344, nullable: false),
                    DurableValueIdHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    DurableValueIdOrderKey = table.Column<string>(type: "nvarchar(516)", maxLength: 516, nullable: false),
                    ContentJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SchemaVersion = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_runtime_durable_value_state", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "elsa_runtime_executable_activity_template",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ScopeKey = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ScopeKeyHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    TemplateId = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    TemplateIdHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    TemplateHash = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    TemplateHashHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    TemplateIdOrderKey = table.Column<string>(type: "nvarchar(655)", maxLength: 655, nullable: false),
                    ContentJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SchemaVersion = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    IncarnationId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_runtime_executable_activity_template", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "elsa_runtime_executable_activity_template_hash_claim",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ScopeKey = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ScopeKeyHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    TemplateHash = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    TemplateHashHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    TemplateId = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    ContentJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SchemaVersion = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    IncarnationId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_runtime_executable_activity_template_hash_claim", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "elsa_runtime_execution_liveness_state",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ScopeKey = table.Column<string>(type: "nvarchar(max)", maxLength: 684, nullable: false),
                    ScopeKeyHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    WorkflowExecutionId = table.Column<string>(type: "nvarchar(344)", maxLength: 344, nullable: false),
                    WorkflowExecutionIdHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    WorkflowExecutionIdOrderKey = table.Column<string>(type: "nvarchar(516)", maxLength: 516, nullable: false),
                    OperationalStateId = table.Column<string>(type: "nvarchar(344)", maxLength: 344, nullable: false),
                    OperationalStateIdHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    OperationalStateIdOrderKey = table.Column<string>(type: "nvarchar(516)", maxLength: 516, nullable: false),
                    InterruptedStatus = table.Column<int>(type: "int", nullable: true),
                    InterruptedAtUtcTicks = table.Column<long>(type: "bigint", nullable: true),
                    LeaseOwnerId = table.Column<string>(type: "nvarchar(344)", maxLength: 344, nullable: true),
                    LeaseAcquiredAtUtcTicks = table.Column<long>(type: "bigint", nullable: true),
                    LeaseExpiresAtUtcTicks = table.Column<long>(type: "bigint", nullable: true),
                    HeartbeatOwnerId = table.Column<string>(type: "nvarchar(344)", maxLength: 344, nullable: true),
                    HeartbeatRecordedAtUtcTicks = table.Column<long>(type: "bigint", nullable: true),
                    HasOperationalOwner = table.Column<bool>(type: "bit", nullable: false),
                    ContentJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SchemaVersion = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_runtime_execution_liveness_state", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "elsa_runtime_incident_state",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ScopeKey = table.Column<string>(type: "nvarchar(max)", maxLength: 684, nullable: false),
                    ScopeKeyHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    WorkflowExecutionId = table.Column<string>(type: "nvarchar(344)", maxLength: 344, nullable: false),
                    WorkflowExecutionIdHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    WorkflowExecutionIdOrderKey = table.Column<string>(type: "nvarchar(516)", maxLength: 516, nullable: false),
                    IncidentId = table.Column<string>(type: "nvarchar(344)", maxLength: 344, nullable: false),
                    IncidentIdHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    IncidentIdOrderKey = table.Column<string>(type: "nvarchar(516)", maxLength: 516, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Severity = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtcTicks = table.Column<long>(type: "bigint", nullable: false),
                    ResolvedAtUtcTicks = table.Column<long>(type: "bigint", nullable: true),
                    ContentJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SchemaVersion = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_runtime_incident_state", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "elsa_runtime_post_commit_outbox",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: false),
                    ScopeKey = table.Column<string>(type: "nvarchar(max)", maxLength: 684, nullable: false),
                    ScopeKeyHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    OutboxItemId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    OutboxItemIdHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    OutboxItemIdOrderKey = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    WorkflowExecutionId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    WorkflowExecutionIdHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    WorkflowExecutionIdOrderKey = table.Column<string>(type: "nvarchar(196)", maxLength: 196, nullable: false),
                    IntentKind = table.Column<string>(type: "nvarchar(230)", maxLength: 230, nullable: false),
                    IntentKindHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    RecordedAtUtcTicks = table.Column<long>(type: "bigint", nullable: false),
                    DeliverableAtUtcTicks = table.Column<long>(type: "bigint", nullable: true),
                    ClaimableAtUtcTicks = table.Column<long>(type: "bigint", nullable: false),
                    ClaimableIsEligible = table.Column<bool>(type: "bit", nullable: false),
                    ContentJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SchemaVersion = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_runtime_post_commit_outbox", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "elsa_runtime_recurring_schedule_projection_state",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(264)", maxLength: 264, nullable: false),
                    ScopeKey = table.Column<string>(type: "nvarchar(max)", maxLength: 684, nullable: false),
                    ScopeKeyHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ActivationId = table.Column<string>(type: "nvarchar(344)", maxLength: 344, nullable: false),
                    ActivationIdHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ActivationIdOrderKey = table.Column<string>(type: "nvarchar(516)", maxLength: 516, nullable: false),
                    ArtifactId = table.Column<string>(type: "nvarchar(max)", maxLength: 344, nullable: true),
                    ArtifactIdHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ArtifactIdOrderKey = table.Column<string>(type: "nvarchar(516)", maxLength: 516, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    ScheduleCount = table.Column<int>(type: "int", nullable: false),
                    ProjectionFingerprint = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ScheduleIdsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ScheduleFingerprintsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ContentJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SchemaVersion = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_runtime_recurring_schedule_projection_state", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "elsa_runtime_recurring_trigger_schedule",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(264)", maxLength: 264, nullable: false),
                    ScopeKey = table.Column<string>(type: "nvarchar(max)", maxLength: 684, nullable: false),
                    ScopeKeyHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ScheduleId = table.Column<string>(type: "nvarchar(max)", maxLength: 4104, nullable: false),
                    ScheduleIdHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ScheduleIdOrderKey = table.Column<string>(type: "nvarchar(max)", maxLength: 6160, nullable: false),
                    ArtifactId = table.Column<string>(type: "nvarchar(344)", maxLength: 344, nullable: false),
                    ArtifactIdHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ArtifactIdOrderKey = table.Column<string>(type: "nvarchar(516)", maxLength: 516, nullable: false),
                    ExecutableNodeId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    StimulusType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    StimulusHash = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    Expression = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    NextOccurrenceUtcTicks = table.Column<long>(type: "bigint", nullable: false),
                    NextOccurrenceOffsetMinutes = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtcTicks = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAtOffsetMinutes = table.Column<int>(type: "int", nullable: false),
                    ActivationId = table.Column<string>(type: "nvarchar(max)", maxLength: 344, nullable: true),
                    ActivationIdHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ActivationIdOrderKey = table.Column<string>(type: "nvarchar(516)", maxLength: 516, nullable: true),
                    SlotId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    ContentJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SchemaVersion = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_runtime_recurring_trigger_schedule", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "elsa_runtime_scheduler_poison",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ScopeKey = table.Column<string>(type: "nvarchar(max)", maxLength: 684, nullable: false),
                    ScopeKeyHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    WorkflowExecutionId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    WorkflowExecutionIdHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    WorkflowExecutionIdOrderKey = table.Column<string>(type: "nvarchar(516)", maxLength: 516, nullable: false),
                    WorkItemId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    WorkItemIdHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    WorkItemIdOrderKey = table.Column<string>(type: "nvarchar(516)", maxLength: 516, nullable: false),
                    FirstFailedAtUtcTicks = table.Column<long>(type: "bigint", nullable: false),
                    LastFailedAtUtcTicks = table.Column<long>(type: "bigint", nullable: false),
                    ContentJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SchemaVersion = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_runtime_scheduler_poison", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "elsa_runtime_scheduler_state",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ScopeKey = table.Column<string>(type: "nvarchar(max)", maxLength: 684, nullable: false),
                    ScopeKeyHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    WorkflowExecutionId = table.Column<string>(type: "nvarchar(344)", maxLength: 344, nullable: false),
                    WorkflowExecutionIdHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    WorkflowExecutionIdOrderKey = table.Column<string>(type: "nvarchar(516)", maxLength: 516, nullable: false),
                    Collection = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ContentJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SchemaVersion = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_runtime_scheduler_state", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "elsa_runtime_scheduler_work_item",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ScopeKey = table.Column<string>(type: "nvarchar(max)", maxLength: 684, nullable: false),
                    ScopeKeyHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    WorkflowExecutionId = table.Column<string>(type: "nvarchar(344)", maxLength: 344, nullable: false),
                    WorkflowExecutionIdHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    WorkflowExecutionIdOrderKey = table.Column<string>(type: "nvarchar(516)", maxLength: 516, nullable: false),
                    WorkItemId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    WorkItemIdHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    WorkOrderKey = table.Column<string>(type: "nvarchar(170)", maxLength: 170, nullable: false),
                    EnqueuedAtUtcTicks = table.Column<long>(type: "bigint", nullable: false),
                    EnqueuedAtOffsetMinutes = table.Column<int>(type: "int", nullable: false),
                    RecordedAtUtcTicks = table.Column<long>(type: "bigint", nullable: false),
                    RecordedAtOffsetMinutes = table.Column<int>(type: "int", nullable: false),
                    ClaimOwnerId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ClaimToken = table.Column<long>(type: "bigint", nullable: false),
                    ClaimedAtUtcTicks = table.Column<long>(type: "bigint", nullable: true),
                    ClaimedAtOffsetMinutes = table.Column<int>(type: "int", nullable: true),
                    VisibleAfterUtcTicks = table.Column<long>(type: "bigint", nullable: true),
                    VisibleAfterOffsetMinutes = table.Column<int>(type: "int", nullable: true),
                    ContentJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SchemaVersion = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_runtime_scheduler_work_item", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "elsa_runtime_trigger_binding_projection_state",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(264)", maxLength: 264, nullable: false),
                    ScopeKey = table.Column<string>(type: "nvarchar(max)", maxLength: 684, nullable: false),
                    ScopeKeyHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ActivationId = table.Column<string>(type: "nvarchar(344)", maxLength: 344, nullable: false),
                    ActivationIdHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ActivationIdOrderKey = table.Column<string>(type: "nvarchar(516)", maxLength: 516, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    BindingCount = table.Column<int>(type: "int", nullable: false),
                    ProjectionFingerprint = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ContentJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SchemaVersion = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_runtime_trigger_binding_projection_state", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "elsa_runtime_workflow_activation_slot",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(264)", maxLength: 264, nullable: false),
                    ScopeKey = table.Column<string>(type: "nvarchar(max)", maxLength: 684, nullable: false),
                    ScopeKeyHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    SlotId = table.Column<string>(type: "nvarchar(752)", maxLength: 752, nullable: false),
                    SlotIdHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    SlotIdOrderKey = table.Column<string>(type: "nvarchar(1128)", maxLength: 1128, nullable: false),
                    WorkflowDefinitionId = table.Column<string>(type: "nvarchar(344)", maxLength: 344, nullable: false),
                    WorkflowDefinitionIdHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    WorkflowDefinitionIdOrderKey = table.Column<string>(type: "nvarchar(516)", maxLength: 516, nullable: false),
                    SlotName = table.Column<string>(type: "nvarchar(344)", maxLength: 344, nullable: false),
                    SlotNameHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    SlotNameOrderKey = table.Column<string>(type: "nvarchar(516)", maxLength: 516, nullable: false),
                    ActiveActivationId = table.Column<string>(type: "nvarchar(max)", maxLength: 344, nullable: true),
                    ActiveActivationIdHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ActiveActivationIdOrderKey = table.Column<string>(type: "nvarchar(516)", maxLength: 516, nullable: true),
                    ActiveActivationUniquenessKey = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    SourceKind = table.Column<string>(type: "nvarchar(max)", maxLength: 344, nullable: true),
                    SourceId = table.Column<string>(type: "nvarchar(max)", maxLength: 344, nullable: true),
                    UpdatedAtUtcTicks = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAtOffsetMinutes = table.Column<int>(type: "int", nullable: false),
                    ContentJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SchemaVersion = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_runtime_workflow_activation_slot", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "elsa_runtime_workflow_alteration_job",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    ScopeKey = table.Column<string>(type: "nvarchar(684)", maxLength: 684, nullable: false),
                    ScopeKeyHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    JobId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    JobIdHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    JobIdOrderKey = table.Column<string>(type: "nvarchar(536)", maxLength: 536, nullable: false),
                    PlanId = table.Column<string>(type: "nvarchar(684)", maxLength: 684, nullable: false),
                    PlanIdHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    WorkflowExecutionId = table.Column<string>(type: "nvarchar(684)", maxLength: 684, nullable: false),
                    WorkflowExecutionIdOrderKey = table.Column<string>(type: "nvarchar(536)", maxLength: 536, nullable: false),
                    WorkflowExecutionIdHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    TenantPartition = table.Column<string>(type: "nvarchar(684)", maxLength: 684, nullable: false),
                    TenantPartitionHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CaptureOrdinal = table.Column<long>(type: "bigint", nullable: false),
                    ClaimableAtUtcTicks = table.Column<long>(type: "bigint", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CheckpointCommitId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    CheckpointCommitIdHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    ContentJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SchemaVersion = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_runtime_workflow_alteration_job", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "elsa_runtime_workflow_alteration_plan",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    ScopeKey = table.Column<string>(type: "nvarchar(684)", maxLength: 684, nullable: false),
                    ScopeKeyHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    PlanId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    PlanIdHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    PlanIdOrderKey = table.Column<string>(type: "nvarchar(536)", maxLength: 536, nullable: false),
                    TenantIdempotencyKey = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    TenantIdempotencyKeyHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ActiveOrderKey = table.Column<string>(type: "nvarchar(536)", maxLength: 536, nullable: false),
                    CreatedAtUtcTicks = table.Column<long>(type: "bigint", nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    CleanupTerminalStatus = table.Column<int>(type: "int", nullable: true),
                    CleanupSafeFailureJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CleanupCompletedAtUtcTicks = table.Column<long>(type: "bigint", nullable: true),
                    CleanupDeletedCount = table.Column<long>(type: "bigint", nullable: false),
                    ContentJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SchemaVersion = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_runtime_workflow_alteration_plan", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "elsa_runtime_workflow_dispatch",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ScopeKey = table.Column<string>(type: "nvarchar(max)", maxLength: 684, nullable: false),
                    ScopeKeyHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    DispatchId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DispatchIdHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    DispatchIdOrderKey = table.Column<string>(type: "nvarchar(1800)", maxLength: 1800, nullable: false),
                    ParentWorkflowExecutionId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ParentWorkflowExecutionIdHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ParentWorkflowExecutionIdOrderKey = table.Column<string>(type: "nvarchar(196)", maxLength: 196, nullable: false),
                    ParentActivityExecutionId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ParentActivityExecutionIdHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ParentActivityExecutionIdOrderKey = table.Column<string>(type: "nvarchar(196)", maxLength: 196, nullable: false),
                    ChildWorkflowExecutionId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ChildWorkflowExecutionIdHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ChildWorkflowExecutionIdOrderKey = table.Column<string>(type: "nvarchar(196)", maxLength: 196, nullable: false),
                    ChildArtifactId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ChildArtifactIdHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ChildArtifactIdOrderKey = table.Column<string>(type: "nvarchar(196)", maxLength: 196, nullable: false),
                    TestScopeId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TestScopeIdHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    TestScopeIdOrderKey = table.Column<string>(type: "nvarchar(196)", maxLength: 196, nullable: true),
                    TenantId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TenantIdHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Mode = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtcTicks = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAtUtcTicks = table.Column<long>(type: "bigint", nullable: false),
                    ContentJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SchemaVersion = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_runtime_workflow_dispatch", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "elsa_runtime_workflow_executable",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ScopeKey = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ScopeKeyHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ArtifactId = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    ArtifactIdHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ArtifactHash = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    ArtifactIdOrderKey = table.Column<string>(type: "nvarchar(655)", maxLength: 655, nullable: false),
                    ContentJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SchemaVersion = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    IncarnationId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_runtime_workflow_executable", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "elsa_runtime_workflow_executable_coordination",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ScopeKey = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ScopeKeyHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ArtifactId = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    ArtifactIdHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ContentJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SchemaVersion = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    IncarnationId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_runtime_workflow_executable_coordination", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "elsa_runtime_workflow_executable_source_reference",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    SourceReferenceId = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    SourceReferenceIdHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    SourceReferenceIdOrderKey = table.Column<string>(type: "nvarchar(655)", maxLength: 655, nullable: false),
                    ArtifactId = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    ArtifactIdHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    DefinitionVersionId = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    DefinitionVersionIdHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    DefinitionId = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    DefinitionIdHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ScopeKey = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ScopeKeyHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ScopeKeyOrderKey = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Scope = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    IsRetired = table.Column<bool>(type: "bit", nullable: false),
                    ExpiresAtUtcTicks = table.Column<long>(type: "bigint", nullable: true),
                    ContentJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SchemaVersion = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    IncarnationId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_runtime_workflow_executable_source_reference", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "elsa_runtime_workflow_execution_state",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ScopeKey = table.Column<string>(type: "nvarchar(684)", maxLength: 684, nullable: false),
                    ScopeKeyHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    WorkflowExecutionId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    WorkflowExecutionIdHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    WorkflowExecutionIdOrderKey = table.Column<string>(type: "nvarchar(516)", maxLength: 516, nullable: false),
                    TenantId = table.Column<string>(type: "nvarchar(684)", maxLength: 684, nullable: true),
                    TenantIdHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    DefinitionId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    DefinitionIdHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    RunKind = table.Column<int>(type: "int", nullable: false),
                    SortTimestampUtcTicks = table.Column<long>(type: "bigint", nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    CorrelationIdHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ArtifactId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    ArtifactIdHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ArtifactIdOrderKey = table.Column<string>(type: "nvarchar(516)", maxLength: 516, nullable: false),
                    AuthorityPartitionKey = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ContentJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SchemaVersion = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_runtime_workflow_execution_state", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "elsa_runtime_workflow_hold_state",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ScopeKey = table.Column<string>(type: "nvarchar(max)", maxLength: 684, nullable: false),
                    ScopeKeyHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ControlPlaneStateId = table.Column<string>(type: "nvarchar(344)", maxLength: 344, nullable: false),
                    ControlPlaneStateIdHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ControlPlaneStateIdOrderKey = table.Column<string>(type: "nvarchar(516)", maxLength: 516, nullable: false),
                    WorkflowExecutionId = table.Column<string>(type: "nvarchar(344)", maxLength: 344, nullable: true),
                    WorkflowExecutionIdHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    WorkflowExecutionIdOrderKey = table.Column<string>(type: "nvarchar(516)", maxLength: 516, nullable: true),
                    ContentJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SchemaVersion = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_runtime_workflow_hold_state", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "elsa_runtime_workflow_run_health_state",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(264)", maxLength: 264, nullable: false),
                    ScopeKey = table.Column<string>(type: "nvarchar(max)", maxLength: 684, nullable: false),
                    ScopeKeyHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    WorkflowExecutionId = table.Column<string>(type: "nvarchar(344)", maxLength: 344, nullable: false),
                    WorkflowExecutionIdHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    WorkflowExecutionIdOrderKey = table.Column<string>(type: "nvarchar(516)", maxLength: 516, nullable: false),
                    DefinitionId = table.Column<string>(type: "nvarchar(344)", maxLength: 344, nullable: false),
                    DefinitionIdHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    DefinitionIdOrderKey = table.Column<string>(type: "nvarchar(516)", maxLength: 516, nullable: false),
                    RunKind = table.Column<int>(type: "int", nullable: false),
                    StartedAtUtcTicks = table.Column<long>(type: "bigint", nullable: true),
                    StartedAtOffsetMinutes = table.Column<int>(type: "int", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    IncidentCount = table.Column<long>(type: "bigint", nullable: false),
                    IncidentBearingCount = table.Column<long>(type: "bigint", nullable: false),
                    ContentJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SchemaVersion = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_runtime_workflow_run_health_state", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "elsa_runtime_workflow_test_scope",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    AccessScopeKey = table.Column<string>(type: "nvarchar(684)", maxLength: 684, nullable: false),
                    AccessScopeKeyHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ScopeId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    ScopeIdHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ScopeIdOrderKey = table.Column<string>(type: "nvarchar(516)", maxLength: 516, nullable: false),
                    TenantId = table.Column<string>(type: "nvarchar(684)", maxLength: 684, nullable: true),
                    TenantIdHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Partition = table.Column<string>(type: "nvarchar(684)", maxLength: 684, nullable: false),
                    PartitionHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    PartitionOrderKey = table.Column<string>(type: "nvarchar(516)", maxLength: 516, nullable: false),
                    ExpiresAtUtcTicks = table.Column<long>(type: "bigint", nullable: false),
                    State = table.Column<int>(type: "int", nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    ContentJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SchemaVersion = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_runtime_workflow_test_scope", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "elsa_runtime_workflow_trigger_binding",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(264)", maxLength: 264, nullable: false),
                    ScopeKey = table.Column<string>(type: "nvarchar(max)", maxLength: 684, nullable: false),
                    ScopeKeyHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    TriggerBindingId = table.Column<string>(type: "nvarchar(344)", maxLength: 344, nullable: false),
                    TriggerBindingIdHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    TriggerBindingIdOrderKey = table.Column<string>(type: "nvarchar(516)", maxLength: 516, nullable: false),
                    ArtifactId = table.Column<string>(type: "nvarchar(344)", maxLength: 344, nullable: false),
                    ArtifactIdHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ArtifactIdOrderKey = table.Column<string>(type: "nvarchar(516)", maxLength: 516, nullable: false),
                    DefinitionId = table.Column<string>(type: "nvarchar(344)", maxLength: 344, nullable: false),
                    ArtifactVersion = table.Column<string>(type: "nvarchar(344)", maxLength: 344, nullable: false),
                    ArtifactHash = table.Column<string>(type: "nvarchar(344)", maxLength: 344, nullable: false),
                    ExecutableNodeId = table.Column<string>(type: "nvarchar(344)", maxLength: 344, nullable: false),
                    StimulusType = table.Column<string>(type: "nvarchar(640)", maxLength: 640, nullable: false),
                    StimulusHash = table.Column<string>(type: "nvarchar(344)", maxLength: 344, nullable: false),
                    StimulusLookupKey = table.Column<string>(type: "nvarchar(344)", maxLength: 344, nullable: false),
                    StimulusTypeLookupKey = table.Column<string>(type: "nvarchar(344)", maxLength: 344, nullable: false),
                    CorrelationScope = table.Column<string>(type: "nvarchar(max)", maxLength: 344, nullable: true),
                    ActivationId = table.Column<string>(type: "nvarchar(max)", maxLength: 344, nullable: true),
                    ActivationIdHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ActivationIdOrderKey = table.Column<string>(type: "nvarchar(516)", maxLength: 516, nullable: true),
                    SlotId = table.Column<string>(type: "nvarchar(max)", maxLength: 344, nullable: true),
                    Cardinality = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtcTicks = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAtOffsetMinutes = table.Column<int>(type: "int", nullable: false),
                    ContentJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SchemaVersion = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_runtime_workflow_trigger_binding", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_runtime_activity_execution_hierarchy_ScopeKeyHash_WorkflowExecutionIdHash_ExecutionScopeIdHash_IsScopeRoot_ExecutionSeq~",
                table: "elsa_runtime_activity_execution_hierarchy",
                columns: new[] { "ScopeKeyHash", "WorkflowExecutionIdHash", "ExecutionScopeIdHash", "IsScopeRoot", "ExecutionSequence" });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_runtime_activity_execution_hierarchy_ScopeKeyHash_WorkflowExecutionIdHash_ExecutionSequence",
                table: "elsa_runtime_activity_execution_hierarchy",
                columns: new[] { "ScopeKeyHash", "WorkflowExecutionIdHash", "ExecutionSequence" });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_runtime_activity_execution_inspection_ScopeKeyHash_WorkflowExecutionIdHash_SummaryExecutionSequence_SummaryScheduledAtU~",
                table: "elsa_runtime_activity_execution_inspection",
                columns: new[] { "ScopeKeyHash", "WorkflowExecutionIdHash", "SummaryExecutionSequence", "SummaryScheduledAtUtcTicks" });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_runtime_activity_execution_state_ScopeKeyHash_WorkflowExecutionIdHash",
                table: "elsa_runtime_activity_execution_state",
                columns: new[] { "ScopeKeyHash", "WorkflowExecutionIdHash" });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_runtime_activity_execution_state_ScopeKeyHash_WorkflowExecutionIdHash_ParentActivityExecutionIdHash",
                table: "elsa_runtime_activity_execution_state",
                columns: new[] { "ScopeKeyHash", "WorkflowExecutionIdHash", "ParentActivityExecutionIdHash" });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_runtime_bookmark_state_ScopeKeyHash_StimulusLookupKey",
                table: "elsa_runtime_bookmark_state",
                columns: new[] { "ScopeKeyHash", "StimulusLookupKey" });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_runtime_bookmark_state_ScopeKeyHash_StimulusTypeLookupKey",
                table: "elsa_runtime_bookmark_state",
                columns: new[] { "ScopeKeyHash", "StimulusTypeLookupKey" });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_runtime_bookmark_state_ScopeKeyHash_WorkflowExecutionIdHash",
                table: "elsa_runtime_bookmark_state",
                columns: new[] { "ScopeKeyHash", "WorkflowExecutionIdHash" });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_runtime_checkpoint_commit_ScopeKeyHash_CommitIdHash_CommitId",
                table: "elsa_runtime_checkpoint_commit",
                columns: new[] { "ScopeKeyHash", "CommitIdHash", "CommitId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_elsa_runtime_checkpoint_commit_ScopeKeyHash_WorkflowExecutionIdHash_CommitIdOrderKey",
                table: "elsa_runtime_checkpoint_commit",
                columns: new[] { "ScopeKeyHash", "WorkflowExecutionIdHash", "CommitIdOrderKey" });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_runtime_durable_timer_ScopeKeyHash_ClaimOrderKey",
                table: "elsa_runtime_durable_timer",
                columns: new[] { "ScopeKeyHash", "ClaimOrderKey" });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_runtime_durable_timer_ScopeKeyHash_DueTimeUtcTicks_TimerIdOrderKey",
                table: "elsa_runtime_durable_timer",
                columns: new[] { "ScopeKeyHash", "DueTimeUtcTicks", "TimerIdOrderKey" });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_runtime_durable_timer_ScopeKeyHash_WorkflowExecutionIdHash_TimerIdHash",
                table: "elsa_runtime_durable_timer",
                columns: new[] { "ScopeKeyHash", "WorkflowExecutionIdHash", "TimerIdHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_elsa_runtime_durable_timer_ScopeKeyHash_WorkflowExecutionIdHash_TimerIdOrderKey",
                table: "elsa_runtime_durable_timer",
                columns: new[] { "ScopeKeyHash", "WorkflowExecutionIdHash", "TimerIdOrderKey" });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_runtime_durable_value_state_ScopeKeyHash_DurableValueIdOrderKey",
                table: "elsa_runtime_durable_value_state",
                columns: new[] { "ScopeKeyHash", "DurableValueIdOrderKey" });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_runtime_durable_value_state_ScopeKeyHash_WorkflowExecutionIdHash_DurableValueIdHash",
                table: "elsa_runtime_durable_value_state",
                columns: new[] { "ScopeKeyHash", "WorkflowExecutionIdHash", "DurableValueIdHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_elsa_runtime_executable_activity_template_ScopeKeyHash_TemplateHashHash_TemplateHash",
                table: "elsa_runtime_executable_activity_template",
                columns: new[] { "ScopeKeyHash", "TemplateHashHash", "TemplateHash" });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_runtime_executable_activity_template_ScopeKeyHash_TemplateIdHash_TemplateId",
                table: "elsa_runtime_executable_activity_template",
                columns: new[] { "ScopeKeyHash", "TemplateIdHash", "TemplateId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_elsa_runtime_executable_activity_template_hash_claim_ScopeKeyHash_TemplateHashHash_TemplateHash",
                table: "elsa_runtime_executable_activity_template_hash_claim",
                columns: new[] { "ScopeKeyHash", "TemplateHashHash", "TemplateHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_elsa_runtime_execution_liveness_state_ScopeKeyHash_HeartbeatRecordedAtUtcTicks_WorkflowExecutionIdOrderKey_OperationalStateI~",
                table: "elsa_runtime_execution_liveness_state",
                columns: new[] { "ScopeKeyHash", "HeartbeatRecordedAtUtcTicks", "WorkflowExecutionIdOrderKey", "OperationalStateIdHash" });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_runtime_execution_liveness_state_ScopeKeyHash_InterruptedStatus_InterruptedAtUtcTicks_WorkflowExecutionIdOrderKey_Opera~",
                table: "elsa_runtime_execution_liveness_state",
                columns: new[] { "ScopeKeyHash", "InterruptedStatus", "InterruptedAtUtcTicks", "WorkflowExecutionIdOrderKey", "OperationalStateIdHash" });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_runtime_execution_liveness_state_ScopeKeyHash_LeaseAcquiredAtUtcTicks_WorkflowExecutionIdOrderKey_OperationalStateIdHash",
                table: "elsa_runtime_execution_liveness_state",
                columns: new[] { "ScopeKeyHash", "LeaseAcquiredAtUtcTicks", "WorkflowExecutionIdOrderKey", "OperationalStateIdHash" });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_runtime_execution_liveness_state_ScopeKeyHash_LeaseExpiresAtUtcTicks_WorkflowExecutionIdOrderKey_OperationalStateIdHash",
                table: "elsa_runtime_execution_liveness_state",
                columns: new[] { "ScopeKeyHash", "LeaseExpiresAtUtcTicks", "WorkflowExecutionIdOrderKey", "OperationalStateIdHash" });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_runtime_execution_liveness_state_ScopeKeyHash_WorkflowExecutionIdHash_OperationalStateIdHash",
                table: "elsa_runtime_execution_liveness_state",
                columns: new[] { "ScopeKeyHash", "WorkflowExecutionIdHash", "OperationalStateIdHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_elsa_runtime_execution_liveness_state_ScopeKeyHash_WorkflowExecutionIdOrderKey_OperationalStateIdHash",
                table: "elsa_runtime_execution_liveness_state",
                columns: new[] { "ScopeKeyHash", "WorkflowExecutionIdOrderKey", "OperationalStateIdHash" });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_runtime_incident_state_ScopeKeyHash_Status_CreatedAtUtcTicks_WorkflowExecutionIdOrderKey_IncidentIdHash",
                table: "elsa_runtime_incident_state",
                columns: new[] { "ScopeKeyHash", "Status", "CreatedAtUtcTicks", "WorkflowExecutionIdOrderKey", "IncidentIdHash" });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_runtime_incident_state_ScopeKeyHash_WorkflowExecutionIdHash_IncidentIdHash",
                table: "elsa_runtime_incident_state",
                columns: new[] { "ScopeKeyHash", "WorkflowExecutionIdHash", "IncidentIdHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_elsa_runtime_incident_state_ScopeKeyHash_WorkflowExecutionIdOrderKey_IncidentIdHash",
                table: "elsa_runtime_incident_state",
                columns: new[] { "ScopeKeyHash", "WorkflowExecutionIdOrderKey", "IncidentIdHash" });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_runtime_post_commit_outbox_ScopeKeyHash_ClaimableIsEligible_ClaimableAtUtcTicks_RecordedAtUtcTicks",
                table: "elsa_runtime_post_commit_outbox",
                columns: new[] { "ScopeKeyHash", "ClaimableIsEligible", "ClaimableAtUtcTicks", "RecordedAtUtcTicks" });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_runtime_post_commit_outbox_ScopeKeyHash_DeliverableAtUtcTicks_RecordedAtUtcTicks",
                table: "elsa_runtime_post_commit_outbox",
                columns: new[] { "ScopeKeyHash", "DeliverableAtUtcTicks", "RecordedAtUtcTicks" });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_runtime_post_commit_outbox_ScopeKeyHash_IntentKindHash_DeliverableAtUtcTicks_RecordedAtUtcTicks",
                table: "elsa_runtime_post_commit_outbox",
                columns: new[] { "ScopeKeyHash", "IntentKindHash", "DeliverableAtUtcTicks", "RecordedAtUtcTicks" });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_runtime_post_commit_outbox_ScopeKeyHash_OutboxItemIdHash",
                table: "elsa_runtime_post_commit_outbox",
                columns: new[] { "ScopeKeyHash", "OutboxItemIdHash" });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_runtime_post_commit_outbox_ScopeKeyHash_WorkflowExecutionIdHash_DeliverableAtUtcTicks_RecordedAtUtcTicks",
                table: "elsa_runtime_post_commit_outbox",
                columns: new[] { "ScopeKeyHash", "WorkflowExecutionIdHash", "DeliverableAtUtcTicks", "RecordedAtUtcTicks" });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_runtime_recurring_schedule_projection_state_ScopeKeyHash_ActivationIdHash_ActivationId",
                table: "elsa_runtime_recurring_schedule_projection_state",
                columns: new[] { "ScopeKeyHash", "ActivationIdHash", "ActivationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_elsa_runtime_recurring_schedule_projection_state_ScopeKeyHash_ArtifactIdHash_ActivationIdHash",
                table: "elsa_runtime_recurring_schedule_projection_state",
                columns: new[] { "ScopeKeyHash", "ArtifactIdHash", "ActivationIdHash" });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_runtime_recurring_trigger_schedule_ScopeKeyHash_ActivationIdHash_ScheduleIdHash",
                table: "elsa_runtime_recurring_trigger_schedule",
                columns: new[] { "ScopeKeyHash", "ActivationIdHash", "ScheduleIdHash" });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_runtime_recurring_trigger_schedule_ScopeKeyHash_ArtifactIdHash_ScheduleIdHash",
                table: "elsa_runtime_recurring_trigger_schedule",
                columns: new[] { "ScopeKeyHash", "ArtifactIdHash", "ScheduleIdHash" });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_runtime_recurring_trigger_schedule_ScopeKeyHash_IsActive_NextOccurrenceUtcTicks_ScheduleIdHash",
                table: "elsa_runtime_recurring_trigger_schedule",
                columns: new[] { "ScopeKeyHash", "IsActive", "NextOccurrenceUtcTicks", "ScheduleIdHash" });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_runtime_recurring_trigger_schedule_ScopeKeyHash_ScheduleIdHash",
                table: "elsa_runtime_recurring_trigger_schedule",
                columns: new[] { "ScopeKeyHash", "ScheduleIdHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_elsa_runtime_scheduler_poison_ScopeKeyHash_WorkflowExecutionIdHash_FirstFailedAtUtcTicks_LastFailedAtUtcTicks_WorkItemIdOrde~",
                table: "elsa_runtime_scheduler_poison",
                columns: new[] { "ScopeKeyHash", "WorkflowExecutionIdHash", "FirstFailedAtUtcTicks", "LastFailedAtUtcTicks", "WorkItemIdOrderKey", "WorkItemIdHash" });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_runtime_scheduler_poison_ScopeKeyHash_WorkflowExecutionIdHash_WorkItemIdHash",
                table: "elsa_runtime_scheduler_poison",
                columns: new[] { "ScopeKeyHash", "WorkflowExecutionIdHash", "WorkItemIdHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_elsa_runtime_scheduler_state_ScopeKeyHash_WorkflowExecutionIdHash_WorkflowExecutionId",
                table: "elsa_runtime_scheduler_state",
                columns: new[] { "ScopeKeyHash", "WorkflowExecutionIdHash", "WorkflowExecutionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_elsa_runtime_scheduler_state_ScopeKeyHash_WorkflowExecutionIdOrderKey",
                table: "elsa_runtime_scheduler_state",
                columns: new[] { "ScopeKeyHash", "WorkflowExecutionIdOrderKey" });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_runtime_scheduler_work_item_ScopeKeyHash_WorkflowExecutionIdHash_VisibleAfterUtcTicks_WorkOrderKey",
                table: "elsa_runtime_scheduler_work_item",
                columns: new[] { "ScopeKeyHash", "WorkflowExecutionIdHash", "VisibleAfterUtcTicks", "WorkOrderKey" });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_runtime_scheduler_work_item_ScopeKeyHash_WorkflowExecutionIdHash_WorkItemIdHash",
                table: "elsa_runtime_scheduler_work_item",
                columns: new[] { "ScopeKeyHash", "WorkflowExecutionIdHash", "WorkItemIdHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_elsa_runtime_scheduler_work_item_ScopeKeyHash_WorkflowExecutionIdHash_WorkOrderKey",
                table: "elsa_runtime_scheduler_work_item",
                columns: new[] { "ScopeKeyHash", "WorkflowExecutionIdHash", "WorkOrderKey" });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_runtime_scheduler_work_item_ScopeKeyHash_WorkflowExecutionIdOrderKey_WorkflowExecutionIdHash",
                table: "elsa_runtime_scheduler_work_item",
                columns: new[] { "ScopeKeyHash", "WorkflowExecutionIdOrderKey", "WorkflowExecutionIdHash" });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_runtime_trigger_binding_projection_state_ScopeKeyHash_ActivationIdHash_ActivationId",
                table: "elsa_runtime_trigger_binding_projection_state",
                columns: new[] { "ScopeKeyHash", "ActivationIdHash", "ActivationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_elsa_runtime_workflow_activation_slot_ScopeKeyHash_ActiveActivationUniquenessKey",
                table: "elsa_runtime_workflow_activation_slot",
                columns: new[] { "ScopeKeyHash", "ActiveActivationUniquenessKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_elsa_runtime_workflow_activation_slot_ScopeKeyHash_SlotIdHash",
                table: "elsa_runtime_workflow_activation_slot",
                columns: new[] { "ScopeKeyHash", "SlotIdHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_elsa_runtime_workflow_activation_slot_ScopeKeyHash_WorkflowDefinitionIdHash_SlotNameHash_SlotIdHash",
                table: "elsa_runtime_workflow_activation_slot",
                columns: new[] { "ScopeKeyHash", "WorkflowDefinitionIdHash", "SlotNameHash", "SlotIdHash" });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_runtime_workflow_alteration_job_ScopeKeyHash_CheckpointCommitIdHash_CheckpointCommitId",
                table: "elsa_runtime_workflow_alteration_job",
                columns: new[] { "ScopeKeyHash", "CheckpointCommitIdHash", "CheckpointCommitId" });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_runtime_workflow_alteration_job_ScopeKeyHash_JobIdHash_JobId",
                table: "elsa_runtime_workflow_alteration_job",
                columns: new[] { "ScopeKeyHash", "JobIdHash", "JobId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_elsa_runtime_workflow_alteration_job_ScopeKeyHash_PlanIdHash_CaptureOrdinal_JobIdOrderKey",
                table: "elsa_runtime_workflow_alteration_job",
                columns: new[] { "ScopeKeyHash", "PlanIdHash", "CaptureOrdinal", "JobIdOrderKey" });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_runtime_workflow_alteration_job_ScopeKeyHash_PlanIdHash_Status_ClaimableAtUtcTicks_JobIdOrderKey",
                table: "elsa_runtime_workflow_alteration_job",
                columns: new[] { "ScopeKeyHash", "PlanIdHash", "Status", "ClaimableAtUtcTicks", "JobIdOrderKey" });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_runtime_workflow_alteration_plan_ScopeKeyHash_PlanIdHash_PlanId",
                table: "elsa_runtime_workflow_alteration_plan",
                columns: new[] { "ScopeKeyHash", "PlanIdHash", "PlanId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_elsa_runtime_workflow_alteration_plan_ScopeKeyHash_Status_ActiveOrderKey",
                table: "elsa_runtime_workflow_alteration_plan",
                columns: new[] { "ScopeKeyHash", "Status", "ActiveOrderKey" });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_runtime_workflow_alteration_plan_ScopeKeyHash_TenantIdempotencyKeyHash_TenantIdempotencyKey",
                table: "elsa_runtime_workflow_alteration_plan",
                columns: new[] { "ScopeKeyHash", "TenantIdempotencyKeyHash", "TenantIdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_elsa_runtime_workflow_dispatch_ScopeKeyHash_ChildWorkflowExecutionIdHash_CreatedAtUtcTicks",
                table: "elsa_runtime_workflow_dispatch",
                columns: new[] { "ScopeKeyHash", "ChildWorkflowExecutionIdHash", "CreatedAtUtcTicks" });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_runtime_workflow_dispatch_ScopeKeyHash_DispatchIdHash",
                table: "elsa_runtime_workflow_dispatch",
                columns: new[] { "ScopeKeyHash", "DispatchIdHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_elsa_runtime_workflow_dispatch_ScopeKeyHash_ParentWorkflowExecutionIdHash_CreatedAtUtcTicks",
                table: "elsa_runtime_workflow_dispatch",
                columns: new[] { "ScopeKeyHash", "ParentWorkflowExecutionIdHash", "CreatedAtUtcTicks" });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_runtime_workflow_dispatch_ScopeKeyHash_Status_CreatedAtUtcTicks",
                table: "elsa_runtime_workflow_dispatch",
                columns: new[] { "ScopeKeyHash", "Status", "CreatedAtUtcTicks" });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_runtime_workflow_dispatch_ScopeKeyHash_TestScopeIdHash_CreatedAtUtcTicks",
                table: "elsa_runtime_workflow_dispatch",
                columns: new[] { "ScopeKeyHash", "TestScopeIdHash", "CreatedAtUtcTicks" });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_runtime_workflow_executable_ScopeKeyHash_ArtifactIdHash_ArtifactId",
                table: "elsa_runtime_workflow_executable",
                columns: new[] { "ScopeKeyHash", "ArtifactIdHash", "ArtifactId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_elsa_runtime_workflow_executable_coordination_ScopeKeyHash_ArtifactIdHash_ArtifactId",
                table: "elsa_runtime_workflow_executable_coordination",
                columns: new[] { "ScopeKeyHash", "ArtifactIdHash", "ArtifactId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_elsa_runtime_workflow_executable_source_reference_ScopeKeyHash_ArtifactIdHash_ArtifactId",
                table: "elsa_runtime_workflow_executable_source_reference",
                columns: new[] { "ScopeKeyHash", "ArtifactIdHash", "ArtifactId" });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_runtime_workflow_executable_source_reference_ScopeKeyHash_DefinitionIdHash_DefinitionId",
                table: "elsa_runtime_workflow_executable_source_reference",
                columns: new[] { "ScopeKeyHash", "DefinitionIdHash", "DefinitionId" });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_runtime_workflow_executable_source_reference_ScopeKeyHash_DefinitionVersionIdHash_DefinitionVersionId",
                table: "elsa_runtime_workflow_executable_source_reference",
                columns: new[] { "ScopeKeyHash", "DefinitionVersionIdHash", "DefinitionVersionId" });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_runtime_workflow_executable_source_reference_ScopeKeyHash_IsRetired_ExpiresAtUtcTicks",
                table: "elsa_runtime_workflow_executable_source_reference",
                columns: new[] { "ScopeKeyHash", "IsRetired", "ExpiresAtUtcTicks" });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_runtime_workflow_executable_source_reference_ScopeKeyHash_SourceReferenceIdHash_SourceReferenceId",
                table: "elsa_runtime_workflow_executable_source_reference",
                columns: new[] { "ScopeKeyHash", "SourceReferenceIdHash", "SourceReferenceId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_elsa_runtime_workflow_execution_state_ScopeKeyHash_ArtifactIdHash",
                table: "elsa_runtime_workflow_execution_state",
                columns: new[] { "ScopeKeyHash", "ArtifactIdHash" });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_runtime_workflow_execution_state_ScopeKeyHash_SortTimestampUtcTicks_WorkflowExecutionIdOrderKey",
                table: "elsa_runtime_workflow_execution_state",
                columns: new[] { "ScopeKeyHash", "SortTimestampUtcTicks", "WorkflowExecutionIdOrderKey" });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_runtime_workflow_execution_state_ScopeKeyHash_Status_SortTimestampUtcTicks_WorkflowExecutionIdOrderKey",
                table: "elsa_runtime_workflow_execution_state",
                columns: new[] { "ScopeKeyHash", "Status", "SortTimestampUtcTicks", "WorkflowExecutionIdOrderKey" });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_runtime_workflow_execution_state_ScopeKeyHash_TenantIdHash_AuthorityPartitionKey_WorkflowExecutionIdOrderKey",
                table: "elsa_runtime_workflow_execution_state",
                columns: new[] { "ScopeKeyHash", "TenantIdHash", "AuthorityPartitionKey", "WorkflowExecutionIdOrderKey" });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_runtime_workflow_execution_state_ScopeKeyHash_WorkflowExecutionIdHash_WorkflowExecutionId",
                table: "elsa_runtime_workflow_execution_state",
                columns: new[] { "ScopeKeyHash", "WorkflowExecutionIdHash", "WorkflowExecutionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_elsa_runtime_workflow_hold_state_ScopeKeyHash_ControlPlaneStateIdHash",
                table: "elsa_runtime_workflow_hold_state",
                columns: new[] { "ScopeKeyHash", "ControlPlaneStateIdHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_elsa_runtime_workflow_hold_state_ScopeKeyHash_ControlPlaneStateIdOrderKey",
                table: "elsa_runtime_workflow_hold_state",
                columns: new[] { "ScopeKeyHash", "ControlPlaneStateIdOrderKey" });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_runtime_workflow_hold_state_ScopeKeyHash_WorkflowExecutionIdHash_WorkflowExecutionIdOrderKey",
                table: "elsa_runtime_workflow_hold_state",
                columns: new[] { "ScopeKeyHash", "WorkflowExecutionIdHash", "WorkflowExecutionIdOrderKey" });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_runtime_workflow_run_health_state_ScopeKeyHash_DefinitionIdHash_StartedAtUtcTicks_WorkflowExecutionIdOrderKey",
                table: "elsa_runtime_workflow_run_health_state",
                columns: new[] { "ScopeKeyHash", "DefinitionIdHash", "StartedAtUtcTicks", "WorkflowExecutionIdOrderKey" });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_runtime_workflow_run_health_state_ScopeKeyHash_RunKind_Status_DefinitionIdHash_StartedAtUtcTicks_WorkflowExecutionIdOrd~",
                table: "elsa_runtime_workflow_run_health_state",
                columns: new[] { "ScopeKeyHash", "RunKind", "Status", "DefinitionIdHash", "StartedAtUtcTicks", "WorkflowExecutionIdOrderKey" });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_runtime_workflow_run_health_state_ScopeKeyHash_StartedAtUtcTicks_WorkflowExecutionIdOrderKey",
                table: "elsa_runtime_workflow_run_health_state",
                columns: new[] { "ScopeKeyHash", "StartedAtUtcTicks", "WorkflowExecutionIdOrderKey" });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_runtime_workflow_run_health_state_ScopeKeyHash_Status_StartedAtUtcTicks_WorkflowExecutionIdOrderKey",
                table: "elsa_runtime_workflow_run_health_state",
                columns: new[] { "ScopeKeyHash", "Status", "StartedAtUtcTicks", "WorkflowExecutionIdOrderKey" });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_runtime_workflow_run_health_state_ScopeKeyHash_WorkflowExecutionIdHash_WorkflowExecutionId",
                table: "elsa_runtime_workflow_run_health_state",
                columns: new[] { "ScopeKeyHash", "WorkflowExecutionIdHash", "WorkflowExecutionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_elsa_runtime_workflow_test_scope_AccessScopeKeyHash_ScopeIdHash_ScopeId",
                table: "elsa_runtime_workflow_test_scope",
                columns: new[] { "AccessScopeKeyHash", "ScopeIdHash", "ScopeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_elsa_runtime_workflow_test_scope_AccessScopeKeyHash_State_ExpiresAtUtcTicks_ScopeIdOrderKey",
                table: "elsa_runtime_workflow_test_scope",
                columns: new[] { "AccessScopeKeyHash", "State", "ExpiresAtUtcTicks", "ScopeIdOrderKey" });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_runtime_workflow_trigger_binding_ScopeKeyHash_ActivationIdHash_TriggerBindingIdHash",
                table: "elsa_runtime_workflow_trigger_binding",
                columns: new[] { "ScopeKeyHash", "ActivationIdHash", "TriggerBindingIdHash" });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_runtime_workflow_trigger_binding_ScopeKeyHash_ArtifactIdHash_TriggerBindingIdHash",
                table: "elsa_runtime_workflow_trigger_binding",
                columns: new[] { "ScopeKeyHash", "ArtifactIdHash", "TriggerBindingIdHash" });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_runtime_workflow_trigger_binding_ScopeKeyHash_StimulusLookupKey_IsActive_TriggerBindingIdHash",
                table: "elsa_runtime_workflow_trigger_binding",
                columns: new[] { "ScopeKeyHash", "StimulusLookupKey", "IsActive", "TriggerBindingIdHash" });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_runtime_workflow_trigger_binding_ScopeKeyHash_StimulusTypeLookupKey_IsActive_TriggerBindingIdHash",
                table: "elsa_runtime_workflow_trigger_binding",
                columns: new[] { "ScopeKeyHash", "StimulusTypeLookupKey", "IsActive", "TriggerBindingIdHash" });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_runtime_workflow_trigger_binding_ScopeKeyHash_TriggerBindingIdHash_TriggerBindingId",
                table: "elsa_runtime_workflow_trigger_binding",
                columns: new[] { "ScopeKeyHash", "TriggerBindingIdHash", "TriggerBindingId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "elsa_runtime_activity_execution_hierarchy");

            migrationBuilder.DropTable(
                name: "elsa_runtime_activity_execution_inspection");

            migrationBuilder.DropTable(
                name: "elsa_runtime_activity_execution_state");

            migrationBuilder.DropTable(
                name: "elsa_runtime_bookmark_state");

            migrationBuilder.DropTable(
                name: "elsa_runtime_checkpoint_commit");

            migrationBuilder.DropTable(
                name: "elsa_runtime_durable_timer");

            migrationBuilder.DropTable(
                name: "elsa_runtime_durable_value_state");

            migrationBuilder.DropTable(
                name: "elsa_runtime_executable_activity_template");

            migrationBuilder.DropTable(
                name: "elsa_runtime_executable_activity_template_hash_claim");

            migrationBuilder.DropTable(
                name: "elsa_runtime_execution_liveness_state");

            migrationBuilder.DropTable(
                name: "elsa_runtime_incident_state");

            migrationBuilder.DropTable(
                name: "elsa_runtime_post_commit_outbox");

            migrationBuilder.DropTable(
                name: "elsa_runtime_recurring_schedule_projection_state");

            migrationBuilder.DropTable(
                name: "elsa_runtime_recurring_trigger_schedule");

            migrationBuilder.DropTable(
                name: "elsa_runtime_scheduler_poison");

            migrationBuilder.DropTable(
                name: "elsa_runtime_scheduler_state");

            migrationBuilder.DropTable(
                name: "elsa_runtime_scheduler_work_item");

            migrationBuilder.DropTable(
                name: "elsa_runtime_trigger_binding_projection_state");

            migrationBuilder.DropTable(
                name: "elsa_runtime_workflow_activation_slot");

            migrationBuilder.DropTable(
                name: "elsa_runtime_workflow_alteration_job");

            migrationBuilder.DropTable(
                name: "elsa_runtime_workflow_alteration_plan");

            migrationBuilder.DropTable(
                name: "elsa_runtime_workflow_dispatch");

            migrationBuilder.DropTable(
                name: "elsa_runtime_workflow_executable");

            migrationBuilder.DropTable(
                name: "elsa_runtime_workflow_executable_coordination");

            migrationBuilder.DropTable(
                name: "elsa_runtime_workflow_executable_source_reference");

            migrationBuilder.DropTable(
                name: "elsa_runtime_workflow_execution_state");

            migrationBuilder.DropTable(
                name: "elsa_runtime_workflow_hold_state");

            migrationBuilder.DropTable(
                name: "elsa_runtime_workflow_run_health_state");

            migrationBuilder.DropTable(
                name: "elsa_runtime_workflow_test_scope");

            migrationBuilder.DropTable(
                name: "elsa_runtime_workflow_trigger_binding");
        }
    }
}
