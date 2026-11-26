using System;
using Elsa.Activities.Design.Persistence.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Elsa.Activities.Design.Persistence.EntityFrameworkCore.Migrations.ActivitiesDesign.MySql;

[DbContext(typeof(ActivitiesDesignMySqlDbContext))]
[Migration("20261121012500_ArchiveActivityDefinitions")]
public partial class ArchiveActivityDefinitions : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "ArchivedAt",
            table: "elsa_activity_definition_authoring",
            type: "datetime",
            nullable: true);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "ArchivedAt",
            table: "elsa_activity_management_definitions",
            type: "datetime",
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "ArchivedAt", table: "elsa_activity_definition_authoring");
        migrationBuilder.DropColumn(name: "ArchivedAt", table: "elsa_activity_management_definitions");
    }
}