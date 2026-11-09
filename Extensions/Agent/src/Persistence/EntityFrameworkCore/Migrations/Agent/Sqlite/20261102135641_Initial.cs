using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elsa.Agent.Persistence.EntityFrameworkCore.Migrations.Agent.Sqlite
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
                    Id = table.Column<int>(type: "INTEGER", nullable: false),
                    DatabaseIdentity = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    SchemaVersion = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK___ElsaDatabaseIdentity_ElsaAgent", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "__ElsaSchemaFinalization_ElsaAgent",
                columns: table => new
                {
                    Family = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    SchemaVersion = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    DatabaseIdentity = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Revision = table.Column<long>(type: "INTEGER", nullable: false),
                    FinalizedVersion = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    IntentJson = table.Column<string>(type: "TEXT", nullable: true),
                    HoldsJson = table.Column<string>(type: "TEXT", nullable: false),
                    HistoryJson = table.Column<string>(type: "TEXT", nullable: false),
                    FinishJson = table.Column<string>(type: "TEXT", nullable: true),
                    FinishHistoryJson = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK___ElsaSchemaFinalization_ElsaAgent", x => x.Family);
                });

            migrationBuilder.CreateTable(
                name: "elsa_agent_audit",
                columns: table => new
                {
                    Sequence = table.Column<long>(type: "INTEGER", nullable: false),
                    EventId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    SessionId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    ActorId = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    CreatedAtUtcTicks = table.Column<long>(type: "INTEGER", nullable: false),
                    SchemaVersion = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Payload = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_agent_audit", x => x.Sequence);
                });

            migrationBuilder.CreateTable(
                name: "elsa_agent_audit_owners",
                columns: table => new
                {
                    SessionId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    ActorId = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    SchemaVersion = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_agent_audit_owners", x => x.SessionId);
                });

            migrationBuilder.CreateTable(
                name: "elsa_agent_audit_sequence",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false),
                    Last = table.Column<long>(type: "INTEGER", nullable: false),
                    SchemaVersion = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_agent_audit_sequence", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "elsa_agent_sessions",
                columns: table => new
                {
                    SessionId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    ActorId = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    Workspace = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    Title = table.Column<string>(type: "TEXT", nullable: true),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    UpdatedAtUtcTicks = table.Column<long>(type: "INTEGER", nullable: false),
                    ExpiresAtUtcTicks = table.Column<long>(type: "INTEGER", nullable: true),
                    PolicyRevision = table.Column<long>(type: "INTEGER", nullable: true),
                    ContinuedFrom = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    LimitReached = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    OwnerToken = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Fence = table.Column<long>(type: "INTEGER", nullable: false),
                    LeaseExpiresAtUtcTicks = table.Column<long>(type: "INTEGER", nullable: true),
                    Version = table.Column<long>(type: "INTEGER", nullable: false),
                    NextSequence = table.Column<long>(type: "INTEGER", nullable: false),
                    SchemaVersion = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Payload = table.Column<string>(type: "TEXT", nullable: false),
                    PausedTurnPayload = table.Column<string>(type: "TEXT", nullable: true),
                    PendingAuditsPayload = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_agent_sessions", x => x.SessionId);
                });

            migrationBuilder.CreateTable(
                name: "elsa_agent_call_keys",
                columns: table => new
                {
                    SessionId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    KeyHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Key = table.Column<string>(type: "TEXT", nullable: false),
                    Value = table.Column<string>(type: "TEXT", nullable: false),
                    Sequence = table.Column<long>(type: "INTEGER", nullable: false),
                    SchemaVersion = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false)
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
                    SessionId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    AttachmentKey = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Sequence = table.Column<long>(type: "INTEGER", nullable: false),
                    SchemaVersion = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Payload = table.Column<string>(type: "TEXT", nullable: false)
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
                    SessionId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Digest = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Sequence = table.Column<long>(type: "INTEGER", nullable: false),
                    Size = table.Column<long>(type: "INTEGER", nullable: false),
                    SchemaVersion = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Canonical = table.Column<string>(type: "TEXT", nullable: false)
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
                    SessionId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    MessageId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Sequence = table.Column<long>(type: "INTEGER", nullable: false),
                    Role = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    StatusReason = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    SchemaVersion = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Payload = table.Column<string>(type: "TEXT", nullable: false)
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
                    ProposalId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    SessionId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Sequence = table.Column<long>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    ExpiresAtUtcTicks = table.Column<long>(type: "INTEGER", nullable: true),
                    Version = table.Column<long>(type: "INTEGER", nullable: false),
                    SchemaVersion = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Payload = table.Column<string>(type: "TEXT", nullable: false)
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
