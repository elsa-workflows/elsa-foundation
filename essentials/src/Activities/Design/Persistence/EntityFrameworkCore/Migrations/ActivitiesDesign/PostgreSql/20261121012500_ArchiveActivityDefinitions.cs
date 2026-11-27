using System;
using Elsa.Activities.Design.Persistence.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elsa.Activities.Design.Persistence.EntityFrameworkCore.Migrations.ActivitiesDesign.PostgreSql;

[DbContext(typeof(ActivitiesDesignPostgreSqlDbContext))]
[Migration("20261121012500_ArchiveActivityDefinitions")]
public partial class ArchiveActivityDefinitions : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "ArchivedAt",
            table: "elsa_activity_definition_authoring",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "ArchivedAt",
            table: "elsa_activity_management_definitions",
            type: "timestamp with time zone",
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "ArchivedAt", table: "elsa_activity_definition_authoring");
        migrationBuilder.DropColumn(name: "ArchivedAt", table: "elsa_activity_management_definitions");
    }
}