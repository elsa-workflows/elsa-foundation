using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Migrations.Runtime.SqlServer
{
    /// <inheritdoc />
    public partial class UserTasks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UserTaskForms",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ScopeKey = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: false),
                    ScopeHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    TenantHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    TenantKey = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: false),
                    FormKey = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: false),
                    VersionKey = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: false),
                    ContentJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SchemaVersion = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserTaskForms", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UserTasks",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ScopeKey = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: false),
                    ScopeHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    TenantHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    TenantKey = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: false),
                    TaskId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ExecutionHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    AssigneeHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    GroupHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ClaimantHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ExecutionKey = table.Column<string>(type: "nvarchar(max)", maxLength: 4096, nullable: false),
                    AssigneeKey = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true),
                    GroupKey = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true),
                    ClaimantKey = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true),
                    SearchTitle = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    ContentJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SchemaVersion = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserTasks", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserTaskForms_SchemaVersion",
                table: "UserTaskForms",
                column: "SchemaVersion");

            migrationBuilder.CreateIndex(
                name: "IX_UserTaskForms_ScopeHash_TenantHash_Id",
                table: "UserTaskForms",
                columns: new[] { "ScopeHash", "TenantHash", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_UserTasks_SchemaVersion",
                table: "UserTasks",
                column: "SchemaVersion");

            migrationBuilder.CreateIndex(
                name: "IX_UserTasks_ScopeHash_AssigneeHash_Status_TaskId",
                table: "UserTasks",
                columns: new[] { "ScopeHash", "AssigneeHash", "Status", "TaskId" });

            migrationBuilder.CreateIndex(
                name: "IX_UserTasks_ScopeHash_ExecutionHash_Status",
                table: "UserTasks",
                columns: new[] { "ScopeHash", "ExecutionHash", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_UserTasks_ScopeHash_GroupHash_Status_TaskId",
                table: "UserTasks",
                columns: new[] { "ScopeHash", "GroupHash", "Status", "TaskId" });

            migrationBuilder.CreateIndex(
                name: "IX_UserTasks_ScopeHash_Status_TaskId",
                table: "UserTasks",
                columns: new[] { "ScopeHash", "Status", "TaskId" });

            migrationBuilder.CreateIndex(
                name: "IX_UserTasks_ScopeHash_TaskId",
                table: "UserTasks",
                columns: new[] { "ScopeHash", "TaskId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserTaskForms");

            migrationBuilder.DropTable(
                name: "UserTasks");
        }
    }
}
