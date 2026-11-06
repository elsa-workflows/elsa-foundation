using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Migrations.Runtime.MySql
{
    /// <inheritdoc />
    public partial class StandaloneTriggers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "StimulusType",
                table: "elsa_runtime_recurring_trigger_schedule",
                type: "longtext",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "longtext");

            migrationBuilder.AlterColumn<string>(
                name: "StimulusHash",
                table: "elsa_runtime_recurring_trigger_schedule",
                type: "longtext",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "longtext");

            migrationBuilder.AlterColumn<string>(
                name: "ExecutableNodeId",
                table: "elsa_runtime_recurring_trigger_schedule",
                type: "longtext",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "longtext");

            migrationBuilder.AlterColumn<string>(
                name: "ArtifactIdOrderKey",
                table: "elsa_runtime_recurring_trigger_schedule",
                type: "varchar(516)",
                maxLength: 516,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "varchar(516)",
                oldMaxLength: 516);

            migrationBuilder.AlterColumn<string>(
                name: "ArtifactIdHash",
                table: "elsa_runtime_recurring_trigger_schedule",
                type: "varchar(64)",
                maxLength: 64,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "varchar(64)",
                oldMaxLength: 64);

            migrationBuilder.AlterColumn<string>(
                name: "ArtifactId",
                table: "elsa_runtime_recurring_trigger_schedule",
                type: "varchar(344)",
                maxLength: 344,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "varchar(344)",
                oldMaxLength: 344);

            migrationBuilder.AlterColumn<string>(
                name: "WorkflowExecutionIdOrderKey",
                table: "elsa_runtime_post_commit_outbox",
                type: "varchar(196)",
                maxLength: 196,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "varchar(196)",
                oldMaxLength: 196);

            migrationBuilder.AlterColumn<string>(
                name: "WorkflowExecutionIdHash",
                table: "elsa_runtime_post_commit_outbox",
                type: "varchar(64)",
                maxLength: 64,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "varchar(64)",
                oldMaxLength: 64);

            migrationBuilder.AlterColumn<string>(
                name: "WorkflowExecutionId",
                table: "elsa_runtime_post_commit_outbox",
                type: "longtext",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "longtext");

            migrationBuilder.AddColumn<string>(
                name: "SourceId",
                table: "elsa_runtime_post_commit_outbox",
                type: "varchar(1024)",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceKeyHash",
                table: "elsa_runtime_post_commit_outbox",
                type: "varchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SourceKind",
                table: "elsa_runtime_post_commit_outbox",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TriggerIdHash",
                table: "elsa_runtime_post_commit_outbox",
                type: "varchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "elsa_runtime_ingress_reservations",
                columns: table => new
                {
                    Id = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false),
                    ScopeKey = table.Column<string>(type: "longtext", nullable: false),
                    ScopeHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    TenantKey = table.Column<string>(type: "longtext", nullable: false),
                    StimulusType = table.Column<string>(type: "longtext", nullable: false),
                    StimulusTypeHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    StimulusHash = table.Column<string>(type: "longtext", nullable: false),
                    StimulusHashHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    OwnerKind = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    OwnerId = table.Column<string>(type: "longtext", nullable: false),
                    OwnerHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    ContentJson = table.Column<string>(type: "longtext", nullable: false),
                    SchemaVersion = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_runtime_ingress_reservations", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "elsa_runtime_standalone_triggers",
                columns: table => new
                {
                    Id = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    ScopeKey = table.Column<string>(type: "longtext", nullable: false),
                    ScopeHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    TenantKey = table.Column<string>(type: "longtext", nullable: false),
                    TenantHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    TriggerKey = table.Column<string>(type: "longtext", nullable: false),
                    TriggerOrderKey = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: false),
                    TypeKey = table.Column<string>(type: "longtext", nullable: false),
                    SearchName = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false),
                    DesiredState = table.Column<int>(type: "int", nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    ContentJson = table.Column<string>(type: "longtext", nullable: false),
                    SchemaVersion = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_runtime_standalone_triggers", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "elsa_runtime_trigger_mutations",
                columns: table => new
                {
                    Id = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    ScopeKey = table.Column<string>(type: "longtext", nullable: false),
                    TenantKey = table.Column<string>(type: "longtext", nullable: false),
                    ActorKey = table.Column<string>(type: "longtext", nullable: false),
                    RequestKey = table.Column<string>(type: "longtext", nullable: false),
                    TriggerRowId = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    ContentJson = table.Column<string>(type: "longtext", nullable: false),
                    SchemaVersion = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_runtime_trigger_mutations", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "elsa_runtime_trigger_subscriptions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    ScopeKey = table.Column<string>(type: "longtext", nullable: false),
                    ScopeHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    TenantKey = table.Column<string>(type: "longtext", nullable: false),
                    TenantHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    TriggerRowId = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    SubscriptionKey = table.Column<string>(type: "longtext", nullable: false),
                    SubscriptionOrderKey = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: false),
                    DefinitionHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    DefinitionKey = table.Column<string>(type: "longtext", nullable: false),
                    SlotKey = table.Column<string>(type: "longtext", nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    IsDeleted = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    ContentJson = table.Column<string>(type: "longtext", nullable: false),
                    SchemaVersion = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_runtime_trigger_subscriptions", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_elsa_runtime_post_commit_outbox_ScopeKeyHash_SourceKind",
                table: "elsa_runtime_post_commit_outbox",
                columns: new[] { "ScopeKeyHash", "SourceKind" });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_runtime_post_commit_outbox_ScopeKeyHash_TriggerIdHash_S~",
                table: "elsa_runtime_post_commit_outbox",
                columns: new[] { "ScopeKeyHash", "TriggerIdHash", "SourceKeyHash" });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_runtime_ingress_reservations_SchemaVersion",
                table: "elsa_runtime_ingress_reservations",
                column: "SchemaVersion");

            migrationBuilder.CreateIndex(
                name: "IX_elsa_runtime_ingress_reservations_ScopeHash_OwnerKind_OwnerH~",
                table: "elsa_runtime_ingress_reservations",
                columns: new[] { "ScopeHash", "OwnerKind", "OwnerHash" });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_runtime_ingress_reservations_ScopeHash_StimulusTypeHash~",
                table: "elsa_runtime_ingress_reservations",
                columns: new[] { "ScopeHash", "StimulusTypeHash", "StimulusHashHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_elsa_runtime_standalone_triggers_SchemaVersion",
                table: "elsa_runtime_standalone_triggers",
                column: "SchemaVersion");

            migrationBuilder.CreateIndex(
                name: "IX_elsa_runtime_standalone_triggers_ScopeHash_TenantHash_Desire~",
                table: "elsa_runtime_standalone_triggers",
                columns: new[] { "ScopeHash", "TenantHash", "DesiredState", "TriggerOrderKey" });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_runtime_trigger_mutations_SchemaVersion",
                table: "elsa_runtime_trigger_mutations",
                column: "SchemaVersion");

            migrationBuilder.CreateIndex(
                name: "IX_elsa_runtime_trigger_mutations_TriggerRowId",
                table: "elsa_runtime_trigger_mutations",
                column: "TriggerRowId");

            migrationBuilder.CreateIndex(
                name: "IX_elsa_runtime_trigger_subscriptions_SchemaVersion",
                table: "elsa_runtime_trigger_subscriptions",
                column: "SchemaVersion");

            migrationBuilder.CreateIndex(
                name: "IX_elsa_runtime_trigger_subscriptions_ScopeHash_TenantHash_Defi~",
                table: "elsa_runtime_trigger_subscriptions",
                columns: new[] { "ScopeHash", "TenantHash", "DefinitionHash", "IsDeleted", "SubscriptionOrderKey" });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_runtime_trigger_subscriptions_TriggerRowId_IsDeleted",
                table: "elsa_runtime_trigger_subscriptions",
                columns: new[] { "TriggerRowId", "IsDeleted" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "elsa_runtime_ingress_reservations");

            migrationBuilder.DropTable(
                name: "elsa_runtime_standalone_triggers");

            migrationBuilder.DropTable(
                name: "elsa_runtime_trigger_mutations");

            migrationBuilder.DropTable(
                name: "elsa_runtime_trigger_subscriptions");

            migrationBuilder.DropIndex(
                name: "IX_elsa_runtime_post_commit_outbox_ScopeKeyHash_SourceKind",
                table: "elsa_runtime_post_commit_outbox");

            migrationBuilder.DropIndex(
                name: "IX_elsa_runtime_post_commit_outbox_ScopeKeyHash_TriggerIdHash_S~",
                table: "elsa_runtime_post_commit_outbox");

            migrationBuilder.DropColumn(
                name: "SourceId",
                table: "elsa_runtime_post_commit_outbox");

            migrationBuilder.DropColumn(
                name: "SourceKeyHash",
                table: "elsa_runtime_post_commit_outbox");

            migrationBuilder.DropColumn(
                name: "SourceKind",
                table: "elsa_runtime_post_commit_outbox");

            migrationBuilder.DropColumn(
                name: "TriggerIdHash",
                table: "elsa_runtime_post_commit_outbox");

            migrationBuilder.AlterColumn<string>(
                name: "StimulusType",
                table: "elsa_runtime_recurring_trigger_schedule",
                type: "longtext",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "longtext",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "StimulusHash",
                table: "elsa_runtime_recurring_trigger_schedule",
                type: "longtext",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "longtext",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ExecutableNodeId",
                table: "elsa_runtime_recurring_trigger_schedule",
                type: "longtext",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "longtext",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ArtifactIdOrderKey",
                table: "elsa_runtime_recurring_trigger_schedule",
                type: "varchar(516)",
                maxLength: 516,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "varchar(516)",
                oldMaxLength: 516,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ArtifactIdHash",
                table: "elsa_runtime_recurring_trigger_schedule",
                type: "varchar(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "varchar(64)",
                oldMaxLength: 64,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ArtifactId",
                table: "elsa_runtime_recurring_trigger_schedule",
                type: "varchar(344)",
                maxLength: 344,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "varchar(344)",
                oldMaxLength: 344,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "WorkflowExecutionIdOrderKey",
                table: "elsa_runtime_post_commit_outbox",
                type: "varchar(196)",
                maxLength: 196,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "varchar(196)",
                oldMaxLength: 196,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "WorkflowExecutionIdHash",
                table: "elsa_runtime_post_commit_outbox",
                type: "varchar(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "varchar(64)",
                oldMaxLength: 64,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "WorkflowExecutionId",
                table: "elsa_runtime_post_commit_outbox",
                type: "longtext",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "longtext",
                oldNullable: true);
        }
    }
}
