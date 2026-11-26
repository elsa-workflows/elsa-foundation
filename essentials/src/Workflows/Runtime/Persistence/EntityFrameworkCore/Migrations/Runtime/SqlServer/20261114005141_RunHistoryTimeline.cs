using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Migrations.Runtime.SqlServer
{
    /// <inheritdoc />
    public partial class RunHistoryTimeline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "elsa_runtime_history_activity",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ScopeKey = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ScopeKeyHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    WorkflowExecutionId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    WorkflowExecutionIdHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ContentJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SchemaVersion = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    ActivityExecutionId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ActivityExecutionIdHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ParentActivityExecutionId = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_runtime_history_activity", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "elsa_runtime_history_bookmark",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ScopeKey = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ScopeKeyHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    WorkflowExecutionId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    WorkflowExecutionIdHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ContentJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SchemaVersion = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    BookmarkId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    BookmarkIdHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ActivityExecutionId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ActivityExecutionIdHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    FirstRecordedSequence = table.Column<long>(type: "bigint", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_runtime_history_bookmark", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "elsa_runtime_history_bookmark_count",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ScopeKey = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ScopeKeyHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    WorkflowExecutionId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    WorkflowExecutionIdHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ContentJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SchemaVersion = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    ActivityExecutionId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ActivityExecutionIdHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    TotalCount = table.Column<long>(type: "bigint", nullable: false),
                    ActiveCount = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_runtime_history_bookmark_count", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "elsa_runtime_history_event",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ScopeKey = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ScopeKeyHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    WorkflowExecutionId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    WorkflowExecutionIdHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ContentJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SchemaVersion = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    HistoryEventId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    HistoryEventIdHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    NameHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Category = table.Column<int>(type: "int", nullable: false),
                    CandidateFingerprint = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    IsPurged = table.Column<bool>(type: "bit", nullable: false),
                    ActivityExecutionId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ActivityExecutionIdHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    BookmarkId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    BookmarkIdHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Sequence = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_runtime_history_event", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "elsa_runtime_history_head",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ScopeKey = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ScopeKeyHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    WorkflowExecutionId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    WorkflowExecutionIdHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ContentJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SchemaVersion = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    FirstSequence = table.Column<long>(type: "bigint", nullable: false),
                    LastSequence = table.Column<long>(type: "bigint", nullable: false),
                    PurgedThroughSequence = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_runtime_history_head", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "elsa_runtime_history_occurrence",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ScopeKey = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ScopeKeyHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    WorkflowExecutionId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    WorkflowExecutionIdHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ContentJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SchemaVersion = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    ActivityExecutionId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ActivityExecutionIdHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    FirstHistoryEventId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    FirstSequence = table.Column<long>(type: "bigint", nullable: false),
                    ParentActivityExecutionId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AncestryKnown = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_runtime_history_occurrence", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "elsa_runtime_history_occurrence_counter",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ScopeKey = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ScopeKeyHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    WorkflowExecutionId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    WorkflowExecutionIdHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ContentJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SchemaVersion = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    ActivityScopeId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ActivityScopeIdHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    NextOrdinal = table.Column<long>(type: "bigint", nullable: false),
                    ProjectionStartsAtSequence = table.Column<long>(type: "bigint", nullable: false),
                    CoveredThroughSequence = table.Column<long>(type: "bigint", nullable: false),
                    FirstUnknownObservationSequence = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_runtime_history_occurrence_counter", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "elsa_runtime_history_occurrence_member",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ScopeKey = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ScopeKeyHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    WorkflowExecutionId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    WorkflowExecutionIdHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ContentJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SchemaVersion = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    ScopeOwnerId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ActivityScopeId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ActivityExecutionId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ActivityExecutionIdHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    FirstSequence = table.Column<long>(type: "bigint", nullable: false),
                    Ordinal = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_runtime_history_occurrence_member", x => x.Id);
                });

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

            migrationBuilder.CreateIndex(
                name: "IX_elsa_runtime_history_occurrence_SchemaVersion",
                table: "elsa_runtime_history_occurrence",
                column: "SchemaVersion");

            migrationBuilder.CreateIndex(
                name: "IX_runtime_history_occurrence_ancestry",
                table: "elsa_runtime_history_occurrence",
                columns: new[] { "ScopeKeyHash", "WorkflowExecutionIdHash", "AncestryKnown", "FirstSequence" });

            migrationBuilder.CreateIndex(
                name: "IX_runtime_history_occurrence_first",
                table: "elsa_runtime_history_occurrence",
                columns: new[] { "ScopeKeyHash", "WorkflowExecutionIdHash", "FirstSequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_runtime_history_occurrence_identity",
                table: "elsa_runtime_history_occurrence",
                columns: new[] { "ScopeKeyHash", "WorkflowExecutionIdHash", "ActivityExecutionIdHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_elsa_runtime_history_occurrence_counter_SchemaVersion",
                table: "elsa_runtime_history_occurrence_counter",
                column: "SchemaVersion");

            migrationBuilder.CreateIndex(
                name: "IX_runtime_history_occurrence_counter_scope",
                table: "elsa_runtime_history_occurrence_counter",
                columns: new[] { "ScopeKeyHash", "WorkflowExecutionIdHash", "ActivityScopeIdHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_elsa_runtime_history_occurrence_member_SchemaVersion",
                table: "elsa_runtime_history_occurrence_member",
                column: "SchemaVersion");

            migrationBuilder.CreateIndex(
                name: "IX_runtime_history_occurrence_anchor",
                table: "elsa_runtime_history_occurrence_member",
                columns: new[] { "ScopeOwnerId", "ActivityExecutionIdHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_runtime_history_occurrence_bound",
                table: "elsa_runtime_history_occurrence_member",
                columns: new[] { "ScopeOwnerId", "FirstSequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_runtime_history_occurrence_member_run",
                table: "elsa_runtime_history_occurrence_member",
                columns: new[] { "ScopeKeyHash", "WorkflowExecutionIdHash" });

            migrationBuilder.CreateIndex(
                name: "IX_runtime_history_occurrence_ordinal",
                table: "elsa_runtime_history_occurrence_member",
                columns: new[] { "ScopeOwnerId", "Ordinal" },
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

            migrationBuilder.DropTable(
                name: "elsa_runtime_history_occurrence");

            migrationBuilder.DropTable(
                name: "elsa_runtime_history_occurrence_counter");

            migrationBuilder.DropTable(
                name: "elsa_runtime_history_occurrence_member");
        }
    }
}
