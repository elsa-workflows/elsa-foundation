using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Migrations.Runtime.SqlServer
{
    /// <inheritdoc />
    public partial class PreparationRetention : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "elsa_runtime_workflow_preparation_retention",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ScopeKey = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ScopeKeyHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    OwnerAddress = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    OperationId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PreparationOrderKey = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    IncarnationId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    State = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    SchemaVersion = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    ContentJson = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_elsa_runtime_workflow_preparation_retention", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_elsa_runtime_workflow_preparation_retention_SchemaVersion",
                table: "elsa_runtime_workflow_preparation_retention",
                column: "SchemaVersion");

            migrationBuilder.CreateIndex(
                name: "IX_elsa_runtime_workflow_preparation_retention_ScopeKeyHash_PreparationOrderKey",
                table: "elsa_runtime_workflow_preparation_retention",
                columns: new[] { "ScopeKeyHash", "PreparationOrderKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Permanent roots and terminal replay history have no automatic destructive downgrade.
            migrationBuilder.Operations.Add(new PreparationRetentionDowngradeNotSupportedOperation());
        }
    }
}
