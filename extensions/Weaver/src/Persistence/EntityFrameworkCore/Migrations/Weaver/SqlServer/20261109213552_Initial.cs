using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elsa.Weaver.Persistence.EntityFrameworkCore.Migrations.Weaver.SqlServer
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "__ElsaDatabaseIdentity_ElsaWeaver",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    DatabaseIdentity = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    SchemaVersion = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false, collation: "Latin1_General_100_BIN2")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK___ElsaDatabaseIdentity_ElsaWeaver", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "__ElsaSchemaFinalization_ElsaWeaver",
                columns: table => new
                {
                    Family = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    SchemaVersion = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false, collation: "Latin1_General_100_BIN2"),
                    DatabaseIdentity = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    FinalizedVersion = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    IntentJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    HoldsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    HistoryJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    FinishJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    FinishHistoryJson = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK___ElsaSchemaFinalization_ElsaWeaver", x => x.Family);
                });

            migrationBuilder.CreateTable(
                name: "elsa_weaver_audit",
                columns: table => new
                {
                    Sequence = table.Column<long>(type: "bigint", nullable: false),
                    EventId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    SessionId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true, collation: "Latin1_General_100_BIN2"),
                    ActorId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true, collation: "Latin1_General_100_BIN2"),
                    CreatedAtUtcTicks = table.Column<long>(type: "bigint", nullable: false),
                    SchemaVersion = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Payload = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_weaver_audit", x => x.Sequence);
                });

            migrationBuilder.CreateTable(
                name: "elsa_weaver_audit_owners",
                columns: table => new
                {
                    SessionId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    ActorId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false, collation: "Latin1_General_100_BIN2"),
                    TenantId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false, collation: "Latin1_General_100_BIN2"),
                    SchemaVersion = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false, collation: "Latin1_General_100_BIN2")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_weaver_audit_owners", x => x.SessionId);
                });

            migrationBuilder.CreateTable(
                name: "elsa_weaver_audit_sequence",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    Last = table.Column<long>(type: "bigint", nullable: false),
                    SchemaVersion = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false, collation: "Latin1_General_100_BIN2")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_weaver_audit_sequence", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "elsa_weaver_sessions",
                columns: table => new
                {
                    SessionId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    TenantId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false, collation: "Latin1_General_100_BIN2"),
                    ActorId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Workspace = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true, collation: "Latin1_General_100_BIN2"),
                    Title = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    UpdatedAtUtcTicks = table.Column<long>(type: "bigint", nullable: false),
                    ExpiresAtUtcTicks = table.Column<long>(type: "bigint", nullable: true),
                    PolicyRevision = table.Column<long>(type: "bigint", nullable: true),
                    ContinuedFrom = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    LimitReached = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    OwnerToken = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Fence = table.Column<long>(type: "bigint", nullable: false),
                    LeaseExpiresAtUtcTicks = table.Column<long>(type: "bigint", nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    NextSequence = table.Column<long>(type: "bigint", nullable: false),
                    SchemaVersion = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Payload = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PausedTurnPayload = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PendingAuditsPayload = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_weaver_sessions", x => x.SessionId);
                });

            migrationBuilder.CreateTable(
                name: "elsa_weaver_call_keys",
                columns: table => new
                {
                    SessionId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Kind = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false, collation: "Latin1_General_100_BIN2"),
                    KeyHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Key = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Value = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Sequence = table.Column<long>(type: "bigint", nullable: false),
                    SchemaVersion = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false, collation: "Latin1_General_100_BIN2")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_weaver_call_keys", x => new { x.SessionId, x.Kind, x.KeyHash });
                    table.ForeignKey(
                        name: "FK_elsa_weaver_call_keys_elsa_weaver_sessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "elsa_weaver_sessions",
                        principalColumn: "SessionId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "elsa_weaver_context",
                columns: table => new
                {
                    SessionId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    AttachmentKey = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Sequence = table.Column<long>(type: "bigint", nullable: false),
                    SchemaVersion = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Payload = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_weaver_context", x => new { x.SessionId, x.AttachmentKey });
                    table.ForeignKey(
                        name: "FK_elsa_weaver_context_elsa_weaver_sessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "elsa_weaver_sessions",
                        principalColumn: "SessionId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "elsa_weaver_disclosure_snapshots",
                columns: table => new
                {
                    SessionId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Digest = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Sequence = table.Column<long>(type: "bigint", nullable: false),
                    Size = table.Column<long>(type: "bigint", nullable: false),
                    SchemaVersion = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Canonical = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_weaver_disclosure_snapshots", x => new { x.SessionId, x.Digest });
                    table.ForeignKey(
                        name: "FK_elsa_weaver_disclosure_snapshots_elsa_weaver_sessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "elsa_weaver_sessions",
                        principalColumn: "SessionId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "elsa_weaver_messages",
                columns: table => new
                {
                    SessionId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    MessageId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Sequence = table.Column<long>(type: "bigint", nullable: false),
                    Role = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    StatusReason = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    SchemaVersion = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Payload = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_weaver_messages", x => new { x.SessionId, x.MessageId });
                    table.ForeignKey(
                        name: "FK_elsa_weaver_messages_elsa_weaver_sessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "elsa_weaver_sessions",
                        principalColumn: "SessionId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "elsa_weaver_proposals",
                columns: table => new
                {
                    ProposalId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    SessionId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Sequence = table.Column<long>(type: "bigint", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ExpiresAtUtcTicks = table.Column<long>(type: "bigint", nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    SchemaVersion = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Payload = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_weaver_proposals", x => x.ProposalId);
                    table.ForeignKey(
                        name: "FK_elsa_weaver_proposals_elsa_weaver_sessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "elsa_weaver_sessions",
                        principalColumn: "SessionId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX___ElsaDatabaseIdentity_ElsaWeaver_SchemaVersion",
                table: "__ElsaDatabaseIdentity_ElsaWeaver",
                column: "SchemaVersion");

            migrationBuilder.CreateIndex(
                name: "IX___ElsaSchemaFinalization_ElsaWeaver_SchemaVersion",
                table: "__ElsaSchemaFinalization_ElsaWeaver",
                column: "SchemaVersion");

            migrationBuilder.CreateIndex(
                name: "IX_elsa_weaver_audit_CreatedAtUtcTicks",
                table: "elsa_weaver_audit",
                column: "CreatedAtUtcTicks");

            migrationBuilder.CreateIndex(
                name: "IX_elsa_weaver_audit_SchemaVersion",
                table: "elsa_weaver_audit",
                column: "SchemaVersion");

            migrationBuilder.CreateIndex(
                name: "IX_elsa_weaver_audit_SessionId_CreatedAtUtcTicks",
                table: "elsa_weaver_audit",
                columns: new[] { "SessionId", "CreatedAtUtcTicks" });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_weaver_audit_owners_SchemaVersion",
                table: "elsa_weaver_audit_owners",
                column: "SchemaVersion");

            migrationBuilder.CreateIndex(
                name: "IX_elsa_weaver_audit_sequence_SchemaVersion",
                table: "elsa_weaver_audit_sequence",
                column: "SchemaVersion");

            migrationBuilder.CreateIndex(
                name: "IX_elsa_weaver_call_keys_SchemaVersion",
                table: "elsa_weaver_call_keys",
                column: "SchemaVersion");

            migrationBuilder.CreateIndex(
                name: "IX_elsa_weaver_context_SchemaVersion",
                table: "elsa_weaver_context",
                column: "SchemaVersion");

            migrationBuilder.CreateIndex(
                name: "IX_elsa_weaver_disclosure_snapshots_SchemaVersion",
                table: "elsa_weaver_disclosure_snapshots",
                column: "SchemaVersion");

            migrationBuilder.CreateIndex(
                name: "IX_elsa_weaver_messages_SchemaVersion",
                table: "elsa_weaver_messages",
                column: "SchemaVersion");

            migrationBuilder.CreateIndex(
                name: "IX_elsa_weaver_proposals_SchemaVersion",
                table: "elsa_weaver_proposals",
                column: "SchemaVersion");

            migrationBuilder.CreateIndex(
                name: "IX_elsa_weaver_proposals_SessionId_Sequence",
                table: "elsa_weaver_proposals",
                columns: new[] { "SessionId", "Sequence" });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_weaver_sessions_SchemaVersion",
                table: "elsa_weaver_sessions",
                column: "SchemaVersion");

            migrationBuilder.CreateIndex(
                name: "IX_elsa_weaver_sessions_TenantId_ActorId_UpdatedAtUtcTicks_SessionId",
                table: "elsa_weaver_sessions",
                columns: new[] { "TenantId", "ActorId", "UpdatedAtUtcTicks", "SessionId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "__ElsaDatabaseIdentity_ElsaWeaver");

            migrationBuilder.DropTable(
                name: "__ElsaSchemaFinalization_ElsaWeaver");

            migrationBuilder.DropTable(
                name: "elsa_weaver_audit");

            migrationBuilder.DropTable(
                name: "elsa_weaver_audit_owners");

            migrationBuilder.DropTable(
                name: "elsa_weaver_audit_sequence");

            migrationBuilder.DropTable(
                name: "elsa_weaver_call_keys");

            migrationBuilder.DropTable(
                name: "elsa_weaver_context");

            migrationBuilder.DropTable(
                name: "elsa_weaver_disclosure_snapshots");

            migrationBuilder.DropTable(
                name: "elsa_weaver_messages");

            migrationBuilder.DropTable(
                name: "elsa_weaver_proposals");

            migrationBuilder.DropTable(
                name: "elsa_weaver_sessions");
        }
    }
}
