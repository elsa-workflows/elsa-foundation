using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elsa.Agent.Persistence.EntityFrameworkCore.Migrations.Agent.SqlServer
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "__ElsaDatabaseIdentity_ElsaAgent",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    DatabaseIdentity = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    SchemaVersion = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false, collation: "Latin1_General_100_BIN2")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK___ElsaDatabaseIdentity_ElsaAgent", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "__ElsaSchemaFinalization_ElsaAgent",
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
                    table.PrimaryKey("PK___ElsaSchemaFinalization_ElsaAgent", x => x.Family);
                });

            migrationBuilder.CreateTable(
                name: "elsa_agent_audit",
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
                    table.PrimaryKey("PK_elsa_agent_audit", x => x.Sequence);
                });

            migrationBuilder.CreateTable(
                name: "elsa_agent_audit_owners",
                columns: table => new
                {
                    SessionId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    ActorId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false, collation: "Latin1_General_100_BIN2"),
                    TenantId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false, collation: "Latin1_General_100_BIN2"),
                    SchemaVersion = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false, collation: "Latin1_General_100_BIN2")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_agent_audit_owners", x => x.SessionId);
                });

            migrationBuilder.CreateTable(
                name: "elsa_agent_audit_sequence",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    Last = table.Column<long>(type: "bigint", nullable: false),
                    SchemaVersion = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false, collation: "Latin1_General_100_BIN2")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_agent_audit_sequence", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "elsa_agent_sessions",
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
                    table.PrimaryKey("PK_elsa_agent_sessions", x => x.SessionId);
                });

            migrationBuilder.CreateTable(
                name: "elsa_agent_call_keys",
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
                    table.PrimaryKey("PK_elsa_agent_call_keys", x => new { x.SessionId, x.Kind, x.KeyHash });
                    table.ForeignKey(
                        name: "FK_elsa_agent_call_keys_elsa_agent_sessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "elsa_agent_sessions",
                        principalColumn: "SessionId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "elsa_agent_context",
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
                    table.PrimaryKey("PK_elsa_agent_context", x => new { x.SessionId, x.AttachmentKey });
                    table.ForeignKey(
                        name: "FK_elsa_agent_context_elsa_agent_sessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "elsa_agent_sessions",
                        principalColumn: "SessionId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "elsa_agent_disclosure_snapshots",
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
                    table.PrimaryKey("PK_elsa_agent_disclosure_snapshots", x => new { x.SessionId, x.Digest });
                    table.ForeignKey(
                        name: "FK_elsa_agent_disclosure_snapshots_elsa_agent_sessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "elsa_agent_sessions",
                        principalColumn: "SessionId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "elsa_agent_messages",
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
                    table.PrimaryKey("PK_elsa_agent_messages", x => new { x.SessionId, x.MessageId });
                    table.ForeignKey(
                        name: "FK_elsa_agent_messages_elsa_agent_sessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "elsa_agent_sessions",
                        principalColumn: "SessionId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "elsa_agent_proposals",
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
                    table.PrimaryKey("PK_elsa_agent_proposals", x => x.ProposalId);
                    table.ForeignKey(
                        name: "FK_elsa_agent_proposals_elsa_agent_sessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "elsa_agent_sessions",
                        principalColumn: "SessionId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX___ElsaDatabaseIdentity_ElsaAgent_SchemaVersion",
                table: "__ElsaDatabaseIdentity_ElsaAgent",
                column: "SchemaVersion");

            migrationBuilder.CreateIndex(
                name: "IX___ElsaSchemaFinalization_ElsaAgent_SchemaVersion",
                table: "__ElsaSchemaFinalization_ElsaAgent",
                column: "SchemaVersion");

            migrationBuilder.CreateIndex(
                name: "IX_elsa_agent_audit_CreatedAtUtcTicks",
                table: "elsa_agent_audit",
                column: "CreatedAtUtcTicks");

            migrationBuilder.CreateIndex(
                name: "IX_elsa_agent_audit_SchemaVersion",
                table: "elsa_agent_audit",
                column: "SchemaVersion");

            migrationBuilder.CreateIndex(
                name: "IX_elsa_agent_audit_SessionId_CreatedAtUtcTicks",
                table: "elsa_agent_audit",
                columns: new[] { "SessionId", "CreatedAtUtcTicks" });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_agent_audit_owners_SchemaVersion",
                table: "elsa_agent_audit_owners",
                column: "SchemaVersion");

            migrationBuilder.CreateIndex(
                name: "IX_elsa_agent_audit_sequence_SchemaVersion",
                table: "elsa_agent_audit_sequence",
                column: "SchemaVersion");

            migrationBuilder.CreateIndex(
                name: "IX_elsa_agent_call_keys_SchemaVersion",
                table: "elsa_agent_call_keys",
                column: "SchemaVersion");

            migrationBuilder.CreateIndex(
                name: "IX_elsa_agent_context_SchemaVersion",
                table: "elsa_agent_context",
                column: "SchemaVersion");

            migrationBuilder.CreateIndex(
                name: "IX_elsa_agent_disclosure_snapshots_SchemaVersion",
                table: "elsa_agent_disclosure_snapshots",
                column: "SchemaVersion");

            migrationBuilder.CreateIndex(
                name: "IX_elsa_agent_messages_SchemaVersion",
                table: "elsa_agent_messages",
                column: "SchemaVersion");

            migrationBuilder.CreateIndex(
                name: "IX_elsa_agent_proposals_SchemaVersion",
                table: "elsa_agent_proposals",
                column: "SchemaVersion");

            migrationBuilder.CreateIndex(
                name: "IX_elsa_agent_proposals_SessionId_Sequence",
                table: "elsa_agent_proposals",
                columns: new[] { "SessionId", "Sequence" });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_agent_sessions_SchemaVersion",
                table: "elsa_agent_sessions",
                column: "SchemaVersion");

            migrationBuilder.CreateIndex(
                name: "IX_elsa_agent_sessions_TenantId_ActorId_UpdatedAtUtcTicks_SessionId",
                table: "elsa_agent_sessions",
                columns: new[] { "TenantId", "ActorId", "UpdatedAtUtcTicks", "SessionId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "__ElsaDatabaseIdentity_ElsaAgent");

            migrationBuilder.DropTable(
                name: "__ElsaSchemaFinalization_ElsaAgent");

            migrationBuilder.DropTable(
                name: "elsa_agent_audit");

            migrationBuilder.DropTable(
                name: "elsa_agent_audit_owners");

            migrationBuilder.DropTable(
                name: "elsa_agent_audit_sequence");

            migrationBuilder.DropTable(
                name: "elsa_agent_call_keys");

            migrationBuilder.DropTable(
                name: "elsa_agent_context");

            migrationBuilder.DropTable(
                name: "elsa_agent_disclosure_snapshots");

            migrationBuilder.DropTable(
                name: "elsa_agent_messages");

            migrationBuilder.DropTable(
                name: "elsa_agent_proposals");

            migrationBuilder.DropTable(
                name: "elsa_agent_sessions");
        }
    }
}
