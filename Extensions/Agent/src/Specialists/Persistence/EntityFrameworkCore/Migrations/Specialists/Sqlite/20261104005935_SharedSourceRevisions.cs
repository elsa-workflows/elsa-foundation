using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elsa.Agent.Specialists.Persistence.EntityFrameworkCore.Migrations.Specialists.Sqlite
{
    /// <inheritdoc />
    public partial class SharedSourceRevisions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "elsa_agent_sources_copies",
                columns: table => new
                {
                    Key = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ScopeKey = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    SourceIdentity = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    Partition = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    Authority = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    SourceId = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    CopyId = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    NameSearchKey = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    Profile = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Content = table.Column<string>(type: "TEXT", nullable: false),
                    Revision = table.Column<long>(type: "INTEGER", nullable: false),
                    Fence = table.Column<long>(type: "INTEGER", nullable: false),
                    Discarded = table.Column<bool>(type: "INTEGER", nullable: false),
                    BaseSnapshotId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    SchemaVersion = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false, defaultValue: "1.0.0")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_agent_sources_copies", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "elsa_agent_sources_snapshots",
                columns: table => new
                {
                    Key = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    WorkingCopyKey = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    OperationId = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    Profile = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Content = table.Column<string>(type: "TEXT", nullable: false),
                    Fingerprint = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Revision = table.Column<long>(type: "INTEGER", nullable: false),
                    BaseSnapshotId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    SchemaVersion = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false, defaultValue: "1.0.0")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_agent_sources_snapshots", x => x.Key);
                    table.ForeignKey(
                        name: "FK_elsa_agent_sources_snapshots_elsa_agent_sources_copies_WorkingCopyKey",
                        column: x => x.WorkingCopyKey,
                        principalTable: "elsa_agent_sources_copies",
                        principalColumn: "Key",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_elsa_agent_sources_snapshots_elsa_agent_sources_snapshots_BaseSnapshotId",
                        column: x => x.BaseSnapshotId,
                        principalTable: "elsa_agent_sources_snapshots",
                        principalColumn: "Key",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "elsa_agent_sources_pins",
                columns: table => new
                {
                    Key = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    SnapshotKey = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Owner = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    SchemaVersion = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false, defaultValue: "1.0.0")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_agent_sources_pins", x => x.Key);
                    table.ForeignKey(
                        name: "FK_elsa_agent_sources_pins_elsa_agent_sources_snapshots_SnapshotKey",
                        column: x => x.SnapshotKey,
                        principalTable: "elsa_agent_sources_snapshots",
                        principalColumn: "Key",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_agent_sources_copies_BaseSnapshotId",
                table: "elsa_agent_sources_copies",
                column: "BaseSnapshotId");

            migrationBuilder.CreateIndex(
                name: "IX_elsa_agent_sources_copies_SchemaVersion",
                table: "elsa_agent_sources_copies",
                column: "SchemaVersion");

            migrationBuilder.CreateIndex(
                name: "IX_elsa_agent_sources_copies_ScopeKey_Discarded_Key",
                table: "elsa_agent_sources_copies",
                columns: new[] { "ScopeKey", "Discarded", "Key" });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_agent_sources_copies_SourceIdentity",
                table: "elsa_agent_sources_copies",
                column: "SourceIdentity",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_elsa_agent_sources_pins_SchemaVersion",
                table: "elsa_agent_sources_pins",
                column: "SchemaVersion");

            migrationBuilder.CreateIndex(
                name: "IX_elsa_agent_sources_pins_SnapshotKey",
                table: "elsa_agent_sources_pins",
                column: "SnapshotKey");

            migrationBuilder.CreateIndex(
                name: "IX_elsa_agent_sources_snapshots_BaseSnapshotId",
                table: "elsa_agent_sources_snapshots",
                column: "BaseSnapshotId");

            migrationBuilder.CreateIndex(
                name: "IX_elsa_agent_sources_snapshots_SchemaVersion",
                table: "elsa_agent_sources_snapshots",
                column: "SchemaVersion");

            migrationBuilder.CreateIndex(
                name: "IX_elsa_agent_sources_snapshots_WorkingCopyKey_Revision_Key",
                table: "elsa_agent_sources_snapshots",
                columns: new[] { "WorkingCopyKey", "Revision", "Key" });

            migrationBuilder.AddForeignKey(
                name: "FK_elsa_agent_sources_copies_elsa_agent_sources_snapshots_BaseSnapshotId",
                table: "elsa_agent_sources_copies",
                column: "BaseSnapshotId",
                principalTable: "elsa_agent_sources_snapshots",
                principalColumn: "Key",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_elsa_agent_sources_copies_elsa_agent_sources_snapshots_BaseSnapshotId",
                table: "elsa_agent_sources_copies");

            migrationBuilder.DropTable(
                name: "elsa_agent_sources_pins");

            migrationBuilder.DropTable(
                name: "elsa_agent_sources_snapshots");

            migrationBuilder.DropTable(
                name: "elsa_agent_sources_copies");
        }
    }
}
