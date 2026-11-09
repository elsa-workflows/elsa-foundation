using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elsa.Agent.Specialists.Persistence.EntityFrameworkCore.Migrations.Specialists.PostgreSql
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
                    Key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, collation: "C"),
                    ScopeKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, collation: "C"),
                    SourceIdentity = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true, collation: "C"),
                    Partition = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false, collation: "C"),
                    TenantId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true, collation: "C"),
                    Kind = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false, collation: "C"),
                    Authority = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false, collation: "C"),
                    SourceId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false, collation: "C"),
                    CopyId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false, collation: "C"),
                    Name = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    NameSearchKey = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false, collation: "C"),
                    Profile = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Content = table.Column<string>(type: "text", nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    Fence = table.Column<long>(type: "bigint", nullable: false),
                    Discarded = table.Column<bool>(type: "boolean", nullable: false),
                    BaseSnapshotId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true, collation: "C"),
                    SchemaVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, defaultValue: "1.0.0", collation: "C")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_agent_sources_copies", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "elsa_agent_sources_snapshots",
                columns: table => new
                {
                    Key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, collation: "C"),
                    WorkingCopyKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, collation: "C"),
                    OperationId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Profile = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Content = table.Column<string>(type: "text", nullable: false),
                    Fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    BaseSnapshotId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true, collation: "C"),
                    SchemaVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, defaultValue: "1.0.0", collation: "C")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_agent_sources_snapshots", x => x.Key);
                    table.ForeignKey(
                        name: "FK_elsa_agent_sources_snapshots_elsa_agent_sources_copies_Work~",
                        column: x => x.WorkingCopyKey,
                        principalTable: "elsa_agent_sources_copies",
                        principalColumn: "Key",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_elsa_agent_sources_snapshots_elsa_agent_sources_snapshots_B~",
                        column: x => x.BaseSnapshotId,
                        principalTable: "elsa_agent_sources_snapshots",
                        principalColumn: "Key",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "elsa_agent_sources_pins",
                columns: table => new
                {
                    Key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, collation: "C"),
                    SnapshotKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, collation: "C"),
                    Owner = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    SchemaVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, defaultValue: "1.0.0", collation: "C")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_agent_sources_pins", x => x.Key);
                    table.ForeignKey(
                        name: "FK_elsa_agent_sources_pins_elsa_agent_sources_snapshots_Snapsh~",
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
                name: "FK_elsa_agent_sources_copies_elsa_agent_sources_snapshots_Base~",
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
                name: "FK_elsa_agent_sources_copies_elsa_agent_sources_snapshots_Base~",
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
