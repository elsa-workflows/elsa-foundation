using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Migrations.Runtime.MySql
{
    /// <inheritdoc />
    public partial class RunHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "HistoryPending",
                table: "elsa_runtime_durable_timer",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "HistoryRetired",
                table: "elsa_runtime_durable_timer",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<long>(
                name: "HistoryRevision",
                table: "elsa_runtime_durable_timer",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.CreateTable(
                name: "elsa_runtime_history_activity",
                columns: table => new
                {
                    Id = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    ScopeKey = table.Column<string>(type: "longtext", nullable: false),
                    ScopeKeyHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    WorkflowExecutionId = table.Column<string>(type: "longtext", nullable: false),
                    WorkflowExecutionIdHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    ContentJson = table.Column<string>(type: "longtext", nullable: false),
                    SchemaVersion = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    ActivityExecutionId = table.Column<string>(type: "longtext", nullable: false),
                    ActivityExecutionIdHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    ParentActivityExecutionId = table.Column<string>(type: "longtext", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_runtime_history_activity", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "elsa_runtime_history_bookmark",
                columns: table => new
                {
                    Id = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    ScopeKey = table.Column<string>(type: "longtext", nullable: false),
                    ScopeKeyHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    WorkflowExecutionId = table.Column<string>(type: "longtext", nullable: false),
                    WorkflowExecutionIdHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    ContentJson = table.Column<string>(type: "longtext", nullable: false),
                    SchemaVersion = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    BookmarkId = table.Column<string>(type: "longtext", nullable: false),
                    BookmarkIdHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    ActivityExecutionId = table.Column<string>(type: "longtext", nullable: false),
                    ActivityExecutionIdHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    FirstRecordedSequence = table.Column<long>(type: "bigint", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_runtime_history_bookmark", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "elsa_runtime_history_bookmark_count",
                columns: table => new
                {
                    Id = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    ScopeKey = table.Column<string>(type: "longtext", nullable: false),
                    ScopeKeyHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    WorkflowExecutionId = table.Column<string>(type: "longtext", nullable: false),
                    WorkflowExecutionIdHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    ContentJson = table.Column<string>(type: "longtext", nullable: false),
                    SchemaVersion = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    ActivityExecutionId = table.Column<string>(type: "longtext", nullable: true),
                    ActivityExecutionIdHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    TotalCount = table.Column<long>(type: "bigint", nullable: false),
                    ActiveCount = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_runtime_history_bookmark_count", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "elsa_runtime_history_event",
                columns: table => new
                {
                    Id = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    ScopeKey = table.Column<string>(type: "longtext", nullable: false),
                    ScopeKeyHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    WorkflowExecutionId = table.Column<string>(type: "longtext", nullable: false),
                    WorkflowExecutionIdHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    ContentJson = table.Column<string>(type: "longtext", nullable: false),
                    SchemaVersion = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    HistoryEventId = table.Column<string>(type: "longtext", nullable: false),
                    HistoryEventIdHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "longtext", nullable: false),
                    NameHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    Category = table.Column<int>(type: "int", nullable: false),
                    CandidateFingerprint = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    IsPurged = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    ActivityExecutionId = table.Column<string>(type: "longtext", nullable: true),
                    ActivityExecutionIdHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true),
                    BookmarkId = table.Column<string>(type: "longtext", nullable: true),
                    BookmarkIdHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true),
                    Sequence = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_runtime_history_event", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "elsa_runtime_history_head",
                columns: table => new
                {
                    Id = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    ScopeKey = table.Column<string>(type: "longtext", nullable: false),
                    ScopeKeyHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    WorkflowExecutionId = table.Column<string>(type: "longtext", nullable: false),
                    WorkflowExecutionIdHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    ContentJson = table.Column<string>(type: "longtext", nullable: false),
                    SchemaVersion = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    FirstSequence = table.Column<long>(type: "bigint", nullable: false),
                    LastSequence = table.Column<long>(type: "bigint", nullable: false),
                    PurgedThroughSequence = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_runtime_history_head", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_runtime_timer_history_pending",
                table: "elsa_runtime_durable_timer",
                columns: new[] { "ScopeKeyHash", "HistoryPending", "WorkflowExecutionIdHash", "TimerIdHash" });

            migrationBuilder.CreateIndex(
                name: "IX_runtime_timer_live_claim",
                table: "elsa_runtime_durable_timer",
                columns: new[] { "ScopeKeyHash", "HistoryRetired", "ClaimOrderKey" });

            migrationBuilder.CreateIndex(
                name: "IX_runtime_timer_live_due",
                table: "elsa_runtime_durable_timer",
                columns: new[] { "ScopeKeyHash", "HistoryRetired", "DueTimeUtcTicks", "TimerIdOrderKey" });

            migrationBuilder.CreateIndex(
                name: "IX_runtime_timer_run_history_pending",
                table: "elsa_runtime_durable_timer",
                columns: new[] { "ScopeKeyHash", "WorkflowExecutionIdHash", "HistoryPending", "HistoryRetired", "TimerIdHash" });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_runtime_history_activity_SchemaVersion",
                table: "elsa_runtime_history_activity",
                column: "SchemaVersion");

            migrationBuilder.CreateIndex(
                name: "IX_runtime_history_activity_owner",
                table: "elsa_runtime_history_activity",
                columns: new[] { "ScopeKeyHash", "WorkflowExecutionIdHash", "ActivityExecutionIdHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_elsa_runtime_history_bookmark_SchemaVersion",
                table: "elsa_runtime_history_bookmark",
                column: "SchemaVersion");

            migrationBuilder.CreateIndex(
                name: "IX_runtime_history_bookmark_activity",
                table: "elsa_runtime_history_bookmark",
                columns: new[] { "ScopeKeyHash", "WorkflowExecutionIdHash", "ActivityExecutionIdHash", "FirstRecordedSequence" });

            migrationBuilder.CreateIndex(
                name: "IX_runtime_history_bookmark_id",
                table: "elsa_runtime_history_bookmark",
                columns: new[] { "ScopeKeyHash", "WorkflowExecutionIdHash", "BookmarkIdHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_runtime_history_bookmark_sequence",
                table: "elsa_runtime_history_bookmark",
                columns: new[] { "ScopeKeyHash", "WorkflowExecutionIdHash", "FirstRecordedSequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_elsa_runtime_history_bookmark_count_SchemaVersion",
                table: "elsa_runtime_history_bookmark_count",
                column: "SchemaVersion");

            migrationBuilder.CreateIndex(
                name: "IX_runtime_history_bookmark_count_owner",
                table: "elsa_runtime_history_bookmark_count",
                columns: new[] { "ScopeKeyHash", "WorkflowExecutionIdHash", "ActivityExecutionIdHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_elsa_runtime_history_event_SchemaVersion",
                table: "elsa_runtime_history_event",
                column: "SchemaVersion");

            migrationBuilder.CreateIndex(
                name: "IX_runtime_history_event_activity",
                table: "elsa_runtime_history_event",
                columns: new[] { "ScopeKeyHash", "WorkflowExecutionIdHash", "ActivityExecutionIdHash", "Sequence" });

            migrationBuilder.CreateIndex(
                name: "IX_runtime_history_event_bookmark",
                table: "elsa_runtime_history_event",
                columns: new[] { "ScopeKeyHash", "WorkflowExecutionIdHash", "BookmarkIdHash", "Sequence" });

            migrationBuilder.CreateIndex(
                name: "IX_runtime_history_event_category",
                table: "elsa_runtime_history_event",
                columns: new[] { "ScopeKeyHash", "WorkflowExecutionIdHash", "Category", "Sequence" });

            migrationBuilder.CreateIndex(
                name: "IX_runtime_history_event_id",
                table: "elsa_runtime_history_event",
                columns: new[] { "ScopeKeyHash", "WorkflowExecutionIdHash", "HistoryEventIdHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_runtime_history_event_kind",
                table: "elsa_runtime_history_event",
                columns: new[] { "ScopeKeyHash", "WorkflowExecutionIdHash", "NameHash", "Sequence" });

            migrationBuilder.CreateIndex(
                name: "IX_runtime_history_event_sequence",
                table: "elsa_runtime_history_event",
                columns: new[] { "ScopeKeyHash", "WorkflowExecutionIdHash", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_elsa_runtime_history_head_SchemaVersion",
                table: "elsa_runtime_history_head",
                column: "SchemaVersion");

            migrationBuilder.CreateIndex(
                name: "IX_runtime_history_head_scope_run",
                table: "elsa_runtime_history_head",
                columns: new[] { "ScopeKeyHash", "WorkflowExecutionIdHash" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "elsa_runtime_history_activity");

            migrationBuilder.DropTable(
                name: "elsa_runtime_history_bookmark");

            migrationBuilder.DropTable(
                name: "elsa_runtime_history_bookmark_count");

            migrationBuilder.DropTable(
                name: "elsa_runtime_history_event");

            migrationBuilder.DropTable(
                name: "elsa_runtime_history_head");

            migrationBuilder.DropIndex(
                name: "IX_runtime_timer_history_pending",
                table: "elsa_runtime_durable_timer");

            migrationBuilder.DropIndex(
                name: "IX_runtime_timer_live_claim",
                table: "elsa_runtime_durable_timer");

            migrationBuilder.DropIndex(
                name: "IX_runtime_timer_live_due",
                table: "elsa_runtime_durable_timer");

            migrationBuilder.DropIndex(
                name: "IX_runtime_timer_run_history_pending",
                table: "elsa_runtime_durable_timer");

            migrationBuilder.DropColumn(
                name: "HistoryPending",
                table: "elsa_runtime_durable_timer");

            migrationBuilder.DropColumn(
                name: "HistoryRetired",
                table: "elsa_runtime_durable_timer");

            migrationBuilder.DropColumn(
                name: "HistoryRevision",
                table: "elsa_runtime_durable_timer");
        }
    }
}
