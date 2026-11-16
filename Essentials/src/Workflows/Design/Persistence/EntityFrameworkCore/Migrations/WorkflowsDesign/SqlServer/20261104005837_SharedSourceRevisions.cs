using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elsa.Workflows.Design.Persistence.EntityFrameworkCore.Migrations.WorkflowsDesign.SqlServer
{
    /// <inheritdoc />
    public partial class SharedSourceRevisions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "StateSource",
                table: "elsa_workflow_definition_drafts");

            migrationBuilder.AddColumn<string>(
                name: "SourceKey",
                table: "elsa_workflow_definition_drafts",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true,
                collation: "Latin1_General_100_BIN2");

            migrationBuilder.CreateTable(
                name: "elsa_workflow_sources_copies",
                columns: table => new
                {
                    Key = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    ScopeKey = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    SourceIdentity = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true, collation: "Latin1_General_100_BIN2"),
                    Partition = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false, collation: "Latin1_General_100_BIN2"),
                    TenantId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true, collation: "Latin1_General_100_BIN2"),
                    Kind = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Authority = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false, collation: "Latin1_General_100_BIN2"),
                    SourceId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false, collation: "Latin1_General_100_BIN2"),
                    CopyId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Name = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false, collation: "Latin1_General_100_BIN2"),
                    NameSearchKey = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Profile = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Content = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    Fence = table.Column<long>(type: "bigint", nullable: false),
                    Discarded = table.Column<bool>(type: "bit", nullable: false),
                    BaseSnapshotId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true, collation: "Latin1_General_100_BIN2"),
                    SchemaVersion = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false, defaultValue: "1.0.0")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_workflow_sources_copies", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "elsa_workflow_sources_snapshots",
                columns: table => new
                {
                    Key = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    WorkingCopyKey = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    OperationId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Profile = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Content = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Fingerprint = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    BaseSnapshotId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true, collation: "Latin1_General_100_BIN2"),
                    SchemaVersion = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false, defaultValue: "1.0.0")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_workflow_sources_snapshots", x => x.Key);
                    table.ForeignKey(
                        name: "FK_elsa_workflow_sources_snapshots_elsa_workflow_sources_copies_WorkingCopyKey",
                        column: x => x.WorkingCopyKey,
                        principalTable: "elsa_workflow_sources_copies",
                        principalColumn: "Key",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_elsa_workflow_sources_snapshots_elsa_workflow_sources_snapshots_BaseSnapshotId",
                        column: x => x.BaseSnapshotId,
                        principalTable: "elsa_workflow_sources_snapshots",
                        principalColumn: "Key",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "elsa_workflow_sources_pins",
                columns: table => new
                {
                    Key = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    SnapshotKey = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Owner = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    SchemaVersion = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false, defaultValue: "1.0.0")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_workflow_sources_pins", x => x.Key);
                    table.ForeignKey(
                        name: "FK_elsa_workflow_sources_pins_elsa_workflow_sources_snapshots_SnapshotKey",
                        column: x => x.SnapshotKey,
                        principalTable: "elsa_workflow_sources_snapshots",
                        principalColumn: "Key",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_workflow_definition_drafts_SourceKey",
                table: "elsa_workflow_definition_drafts",
                column: "SourceKey");

            migrationBuilder.CreateIndex(
                name: "IX_elsa_workflow_sources_copies_BaseSnapshotId",
                table: "elsa_workflow_sources_copies",
                column: "BaseSnapshotId");

            migrationBuilder.CreateIndex(
                name: "IX_elsa_workflow_sources_copies_SchemaVersion",
                table: "elsa_workflow_sources_copies",
                column: "SchemaVersion");

            migrationBuilder.CreateIndex(
                name: "IX_elsa_workflow_sources_copies_ScopeKey_Discarded_Key",
                table: "elsa_workflow_sources_copies",
                columns: new[] { "ScopeKey", "Discarded", "Key" });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_workflow_sources_copies_SourceIdentity",
                table: "elsa_workflow_sources_copies",
                column: "SourceIdentity",
                unique: true,
                filter: "[SourceIdentity] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_elsa_workflow_sources_pins_SchemaVersion",
                table: "elsa_workflow_sources_pins",
                column: "SchemaVersion");

            migrationBuilder.CreateIndex(
                name: "IX_elsa_workflow_sources_pins_SnapshotKey",
                table: "elsa_workflow_sources_pins",
                column: "SnapshotKey");

            migrationBuilder.CreateIndex(
                name: "IX_elsa_workflow_sources_snapshots_BaseSnapshotId",
                table: "elsa_workflow_sources_snapshots",
                column: "BaseSnapshotId");

            migrationBuilder.CreateIndex(
                name: "IX_elsa_workflow_sources_snapshots_SchemaVersion",
                table: "elsa_workflow_sources_snapshots",
                column: "SchemaVersion");

            migrationBuilder.CreateIndex(
                name: "IX_elsa_workflow_sources_snapshots_WorkingCopyKey_Revision_Key",
                table: "elsa_workflow_sources_snapshots",
                columns: new[] { "WorkingCopyKey", "Revision", "Key" });

            migrationBuilder.AddForeignKey(
                name: "FK_elsa_workflow_definition_drafts_elsa_workflow_sources_copies_SourceKey",
                table: "elsa_workflow_definition_drafts",
                column: "SourceKey",
                principalTable: "elsa_workflow_sources_copies",
                principalColumn: "Key",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_elsa_workflow_sources_copies_elsa_workflow_sources_snapshots_BaseSnapshotId",
                table: "elsa_workflow_sources_copies",
                column: "BaseSnapshotId",
                principalTable: "elsa_workflow_sources_snapshots",
                principalColumn: "Key",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_elsa_workflow_definition_drafts_elsa_workflow_sources_copies_SourceKey",
                table: "elsa_workflow_definition_drafts");

            migrationBuilder.DropForeignKey(
                name: "FK_elsa_workflow_sources_copies_elsa_workflow_sources_snapshots_BaseSnapshotId",
                table: "elsa_workflow_sources_copies");

            migrationBuilder.DropTable(
                name: "elsa_workflow_sources_pins");

            migrationBuilder.DropTable(
                name: "elsa_workflow_sources_snapshots");

            migrationBuilder.DropTable(
                name: "elsa_workflow_sources_copies");

            migrationBuilder.DropIndex(
                name: "IX_elsa_workflow_definition_drafts_SourceKey",
                table: "elsa_workflow_definition_drafts");

            migrationBuilder.DropColumn(
                name: "SourceKey",
                table: "elsa_workflow_definition_drafts");

            migrationBuilder.AddColumn<string>(
                name: "StateSource",
                table: "elsa_workflow_definition_drafts",
                type: "nvarchar(max)",
                nullable: true);
        }
    }
}
