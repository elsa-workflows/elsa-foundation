using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elsa.Activities.Design.Persistence.EFCore.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "Elsa");

            migrationBuilder.CreateTable(
                name: "ActivityDefinitions",
                schema: "Elsa",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    ActivityTypeKey = table.Column<string>(type: "TEXT", nullable: false),
                    Category = table.Column<string>(type: "TEXT", nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", nullable: true),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<string>(type: "TEXT", nullable: false),
                    LastModifiedAt = table.Column<string>(type: "TEXT", nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActivityDefinitions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ActivityDefinitionVersions",
                schema: "Elsa",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false),
                    DefinitionId = table.Column<string>(type: "TEXT", nullable: false),
                    ImplementationKind = table.Column<string>(type: "TEXT", nullable: false),
                    ImplementationDescriptorPayload = table.Column<string>(type: "TEXT", maxLength: -1, nullable: true),
                    InputsSource = table.Column<string>(type: "TEXT", maxLength: -1, nullable: true),
                    OutputsSource = table.Column<string>(type: "TEXT", maxLength: -1, nullable: true),
                    PortsSource = table.Column<string>(type: "TEXT", maxLength: -1, nullable: true),
                    ExecutionType = table.Column<int>(type: "INTEGER", nullable: false),
                    SourceKind = table.Column<string>(type: "TEXT", nullable: false),
                    SourceId = table.Column<string>(type: "TEXT", nullable: false),
                    ReconciledAt = table.Column<string>(type: "TEXT", nullable: false),
                    ReconciledBy = table.Column<string>(type: "TEXT", nullable: true),
                    ReconcilliationHash = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<string>(type: "TEXT", nullable: false),
                    LastModifiedAt = table.Column<string>(type: "TEXT", nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActivityDefinitionVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ActivityDefinitionVersions_ActivityDefinitions_DefinitionId",
                        column: x => x.DefinitionId,
                        principalSchema: "Elsa",
                        principalTable: "ActivityDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ActivityDefinition_Category",
                schema: "Elsa",
                table: "ActivityDefinitions",
                column: "Category");

            migrationBuilder.CreateIndex(
                name: "IX_ActivityDefinitions_TenantId",
                schema: "Elsa",
                table: "ActivityDefinitions",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "UX_ActivityDefinition_ActivityTypeKey",
                schema: "Elsa",
                table: "ActivityDefinitions",
                column: "ActivityTypeKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ActivityDefinitionVersions_TenantId",
                schema: "Elsa",
                table: "ActivityDefinitionVersions",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "UX_ActivityDefinitionVersion_DefinitionId_Version",
                schema: "Elsa",
                table: "ActivityDefinitionVersions",
                columns: new[] { "DefinitionId", "Version" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ActivityDefinitionVersions",
                schema: "Elsa");

            migrationBuilder.DropTable(
                name: "ActivityDefinitions",
                schema: "Elsa");
        }
    }
}
