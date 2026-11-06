using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elsa.Workflows.Design.Persistence.EntityFrameworkCore.Migrations.WorkflowsDesign.SqlServer
{
    /// <inheritdoc />
    public partial class WorkflowLibraryOperationKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "OperationFingerprint",
                table: "elsa_workflow_tags",
                type: "varchar(64)",
                unicode: false,
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OperationKey",
                table: "elsa_workflow_tags",
                type: "varchar(64)",
                unicode: false,
                maxLength: 64,
                nullable: true,
                collation: "Latin1_General_100_BIN2");

            migrationBuilder.AddColumn<string>(
                name: "OperationFingerprint",
                table: "elsa_workflow_library_views",
                type: "varchar(64)",
                unicode: false,
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OperationKey",
                table: "elsa_workflow_library_views",
                type: "varchar(64)",
                unicode: false,
                maxLength: 64,
                nullable: true,
                collation: "Latin1_General_100_BIN2");

            migrationBuilder.CreateIndex(
                name: "IX_elsa_workflow_tags_ScopeKey_OperationKey",
                table: "elsa_workflow_tags",
                columns: new[] { "ScopeKey", "OperationKey" },
                unique: true,
                filter: "[OperationKey] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_elsa_workflow_library_views_ScopeKey_OwnerHash_OperationKey",
                table: "elsa_workflow_library_views",
                columns: new[] { "ScopeKey", "OwnerHash", "OperationKey" },
                unique: true,
                filter: "[OperationKey] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_elsa_workflow_tags_ScopeKey_OperationKey",
                table: "elsa_workflow_tags");

            migrationBuilder.DropIndex(
                name: "IX_elsa_workflow_library_views_ScopeKey_OwnerHash_OperationKey",
                table: "elsa_workflow_library_views");

            migrationBuilder.DropColumn(
                name: "OperationFingerprint",
                table: "elsa_workflow_tags");

            migrationBuilder.DropColumn(
                name: "OperationKey",
                table: "elsa_workflow_tags");

            migrationBuilder.DropColumn(
                name: "OperationFingerprint",
                table: "elsa_workflow_library_views");

            migrationBuilder.DropColumn(
                name: "OperationKey",
                table: "elsa_workflow_library_views");
        }
    }
}
