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
                    UniqueName = table.Column<string>(type: "TEXT", nullable: false),
                    Category = table.Column<string>(type: "TEXT", nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", nullable: true),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    IsBrowsable = table.Column<bool>(type: "INTEGER", nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<string>(type: "TEXT", nullable: false),
                    LastModifiedAt = table.Column<string>(type: "TEXT", nullable: false)
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
                    TypeInfo_TypeName = table.Column<string>(type: "TEXT", nullable: false),
                    TypeInfo_Namespace = table.Column<string>(type: "TEXT", nullable: false),
                    TypeInfo_AssemblyName = table.Column<string>(type: "TEXT", nullable: false),
                    TypeInfo_AssemblyVersion = table.Column<string>(type: "TEXT", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false),
                    DefinitionId = table.Column<string>(type: "TEXT", nullable: false),
                    InputsSource = table.Column<string>(type: "TEXT", maxLength: -1, nullable: true),
                    OutputsSource = table.Column<string>(type: "TEXT", maxLength: -1, nullable: true),
                    PortsSource = table.Column<string>(type: "TEXT", maxLength: -1, nullable: true),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    TenantId = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<string>(type: "TEXT", nullable: false),
                    LastModifiedAt = table.Column<string>(type: "TEXT", nullable: false)
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
                name: "IX_ActivityDefinition_IsBrowsable",
                schema: "Elsa",
                table: "ActivityDefinitions",
                column: "IsBrowsable");

            migrationBuilder.CreateIndex(
                name: "IX_ActivityDefinition_TenantId",
                schema: "Elsa",
                table: "ActivityDefinitions",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ActivityDefinition_UniqueName",
                schema: "Elsa",
                table: "ActivityDefinitions",
                column: "UniqueName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ActivityDefinitionVersion_TenantId",
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
