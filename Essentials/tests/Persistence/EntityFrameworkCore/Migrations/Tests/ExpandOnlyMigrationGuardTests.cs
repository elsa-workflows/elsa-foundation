using Elsa.Persistence.EntityFramework;
using Elsa.Persistence.EntityFramework.SchemaFinalization;
using Elsa.Persistence.EntityFramework.Tests;
using Elsa.Persistence.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;

namespace Elsa.Persistence.EntityFrameworkCore.Migrations.Tests;

/// <summary>
/// Spec 185 "Expand-Only Migration Guard" (elsa-workflows/elsa-foundation#2104, B8 of the cluster-safe schema
/// rollout #2093): every fixture here proves <see cref="ExpandOnlyMigrationGuard"/> against operations built the
/// same way a generated migration's own <c>Up</c> builds them — <see cref="MigrationBuilder"/>'s fluent calls —
/// and runs through the exact classifier the real scan (<see cref="ExpandOnlyMigrationScanner"/>) calls, so a
/// passing fixture proves the real scan's logic and not a copy of it (FR-022).
/// </summary>
public sealed class ExpandOnlyMigrationGuardTests
{
    private const string Table = "elsa_example";

    private static IReadOnlyList<MigrationOperation> Build(Action<MigrationBuilder> up)
    {
        var builder = new MigrationBuilder(activeProvider: null);
        up(builder);
        return builder.Operations;
    }

    // --- User Story 2: a destructive change fails, named in canonical form (#2104's acceptance) -----------

    [Fact]
    public void A_dropped_column_is_named_DropColumn_table_dot_column()
    {
        var operations = Build(migration => migration.DropColumn(name: "Legacy", table: Table));

        var violations = ExpandOnlyMigrationGuard.Classify(operations);

        Assert.Equal(["DropColumn elsa_example.Legacy"], violations);
    }

    [Fact]
    public void A_renamed_column_is_named_RenameColumn()
    {
        var operations = Build(migration => migration.RenameColumn(name: "OldName", table: Table, newName: "NewName"));

        var violations = ExpandOnlyMigrationGuard.Classify(operations);

        Assert.Equal(["RenameColumn elsa_example.OldName"], violations);
    }

    [Fact]
    public void A_retyped_column_is_named_AlterColumn()
    {
        var operations = Build(migration => migration.AlterColumn<int>(name: "Amount", table: Table, oldClrType: typeof(short)));

        var violations = ExpandOnlyMigrationGuard.Classify(operations);

        Assert.Equal(["AlterColumn elsa_example.Amount"], violations);
    }

    [Fact]
    public void A_dropped_table_is_named_DropTable_with_no_column_part()
    {
        var operations = Build(migration => migration.DropTable(name: Table));

        Assert.Equal(["DropTable elsa_example"], ExpandOnlyMigrationGuard.Classify(operations));
    }

    [Fact]
    public void Every_kind_of_alter_column_is_a_violation_including_a_widening_that_is_safe_on_every_engine()
    {
        // A longer varchar is still an AlterColumn (spec 185 Edge Cases): the guard does not carry per-engine
        // rules about which alterations an older host survives.
        var operations = Build(migration => migration.AlterColumn<string>(name: "Name", table: Table, maxLength: 512, oldMaxLength: 128));

        Assert.Equal(["AlterColumn elsa_example.Name"], ExpandOnlyMigrationGuard.Classify(operations));
    }

    [Fact]
    public void A_non_nullable_added_column_is_a_violation_even_with_a_default()
    {
        // Decisions: a default does not give "unset" for an older host's insert, so it stays a violation.
        var operations = Build(migration => migration.AddColumn<string>(name: "Stamp", table: Table, nullable: false, defaultValue: "1.0.0"));

        Assert.Equal(["AddColumn elsa_example.Stamp"], ExpandOnlyMigrationGuard.Classify(operations));
    }

    [Fact]
    public void A_computed_added_column_is_a_violation_even_when_nullable()
    {
        var operations = Build(migration => migration.AddColumn<int>(name: "Total", table: Table, nullable: true, computedColumnSql: "1 + 1"));

        Assert.Equal(["AddColumn elsa_example.Total"], ExpandOnlyMigrationGuard.Classify(operations));
    }

    [Fact]
    public void A_unique_index_on_a_pre_existing_table_is_a_violation()
    {
        // Edge Cases: SQL Server treats NULLs as equal in a unique index, so even one on a brand-new nullable
        // column can refuse the second older-host insert.
        var operations = Build(migration => migration.CreateIndex(name: "IX_Example_Key", table: Table, column: "Key", unique: true));

        Assert.Equal(["CreateIndex elsa_example.IX_Example_Key"], ExpandOnlyMigrationGuard.Classify(operations));
    }

    [Fact]
    public void Raw_sql_is_a_violation()
    {
        var operations = Build(migration => migration.Sql("update elsa_example set x = 1"));

        Assert.Equal(["Sql"], ExpandOnlyMigrationGuard.Classify(operations));
    }

    [Fact]
    public void A_constraint_added_to_a_pre_existing_table_is_a_violation()
    {
        var operations = Build(migration => migration.AddForeignKey(
            name: "FK_Example_Parent",
            table: Table,
            column: "ParentId",
            principalTable: "elsa_parent"));

        Assert.Equal(["AddForeignKey elsa_example.FK_Example_Parent"], ExpandOnlyMigrationGuard.Classify(operations));
    }

    [Fact]
    public void Insert_data_into_a_pre_existing_table_is_a_violation()
    {
        var operations = Build(migration => migration.InsertData(table: Table, column: "Key", value: "seed"));

        Assert.Equal(["InsertData elsa_example"], ExpandOnlyMigrationGuard.Classify(operations));
    }

    /// <summary>FR-006: an operation type this guard has never seen is a violation too, not silently allowed.</summary>
    [Fact]
    public void An_operation_type_the_guard_does_not_know_is_a_violation()
    {
        var operations = new MigrationOperation[] { new UnknownFixtureOperation() };

        Assert.Equal(["UnknownFixture"], ExpandOnlyMigrationGuard.Classify(operations));
    }

    // --- User Story 1: additive changes pass -----------------------------------------------------------------

    [Fact]
    public void A_nullable_non_computed_added_column_passes()
    {
        var operations = Build(migration => migration.AddColumn<string>(name: "Note", table: Table, nullable: true));

        Assert.Empty(ExpandOnlyMigrationGuard.Classify(operations));
    }

    [Fact]
    public void A_non_unique_index_on_a_pre_existing_table_passes()
    {
        var operations = Build(migration => migration.CreateIndex(name: "IX_Example_Note", table: Table, column: "Note"));

        Assert.Empty(ExpandOnlyMigrationGuard.Classify(operations));
    }

    [Fact]
    public void Ensuring_a_schema_and_creating_a_sequence_pass()
    {
        var operations = Build(migration =>
        {
            migration.EnsureSchema(name: "elsa");
            migration.Operations.Add(new CreateSequenceOperation { Schema = "elsa", Name = "elsa_example_seq", ClrType = typeof(long) });
        });

        Assert.Empty(ExpandOnlyMigrationGuard.Classify(operations));
    }

    /// <summary>Acceptance Scenario 2: a new table together with its own keys, unique constraints and indexes passes whole.</summary>
    [Fact]
    public void A_new_table_with_its_own_constraints_and_a_following_index_passes()
    {
        var operations = Build(migration =>
        {
            migration.CreateTable(
                name: Table,
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false),
                    ParentId = table.Column<int>(nullable: false),
                    Key = table.Column<string>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Example", x => x.Id);
                    table.ForeignKey("FK_Example_Parent", x => x.ParentId, "elsa_parent", principalColumn: "Id");
                    table.UniqueConstraint("AK_Example_Key", x => x.Key);
                });
            // A unique index on the table this same migration just created is still safe: no older host's
            // model knows the table exists at all.
            migration.CreateIndex(name: "IX_Example_Key", table: Table, column: "Key", unique: true);
            migration.InsertData(table: Table, columns: ["Id", "ParentId", "Key"], values: new object[] { 1, 1, "seed" });
        });

        Assert.Empty(ExpandOnlyMigrationGuard.Classify(operations));
    }

    // --- User Story 3: an opt-out matches exactly, and nothing more -------------------------------------------

    private static ExpandOnlyMigrationOptOutAttribute OptOut(params string[] violations) =>
        new(reason: "Removed after #1976 finalizes past the version that stops reading it.", reviewReference: "#2104", violations);

    [Fact]
    public void An_opt_out_whose_list_equals_the_violations_passes()
    {
        var operations = Build(migration => migration.DropColumn(name: "Legacy", table: Table));

        var result = ExpandOnlyMigrationGuard.Evaluate(operations, OptOut("DropColumn elsa_example.Legacy"), ExpandOnlyMigrationFamilies.None);

        Assert.True(result.Passed);
        Assert.Equal(["DropColumn elsa_example.Legacy"], result.Violations);
        Assert.Empty(result.UnlistedViolations);
        Assert.Empty(result.StaleOptOutEntries);
    }

    [Fact]
    public void A_violation_the_opt_out_does_not_list_fails_naming_it()
    {
        var operations = Build(migration =>
        {
            migration.DropColumn(name: "Legacy", table: Table);
            migration.RenameColumn(name: "OldName", table: Table, newName: "NewName");
        });

        var result = ExpandOnlyMigrationGuard.Evaluate(operations, OptOut("DropColumn elsa_example.Legacy"), ExpandOnlyMigrationFamilies.None);

        Assert.False(result.Passed);
        Assert.Equal(["RenameColumn elsa_example.OldName"], result.UnlistedViolations);
    }

    [Fact]
    public void An_opt_out_listing_a_violation_that_does_not_occur_fails_naming_the_stale_entry()
    {
        var operations = Build(migration => migration.DropColumn(name: "Legacy", table: Table));

        var result = ExpandOnlyMigrationGuard.Evaluate(operations, OptOut("DropColumn elsa_example.Legacy", "DropTable elsa_example"), ExpandOnlyMigrationFamilies.None);

        Assert.False(result.Passed);
        Assert.Equal(["DropTable elsa_example"], result.StaleOptOutEntries);
    }

    [Fact]
    public void An_opt_out_on_a_migration_with_no_violations_fails()
    {
        var operations = Build(migration => migration.AddColumn<string>(name: "Note", table: Table, nullable: true));

        var result = ExpandOnlyMigrationGuard.Evaluate(operations, OptOut("DropColumn elsa_example.Legacy"), ExpandOnlyMigrationFamilies.None);

        Assert.False(result.Passed);
        Assert.Empty(result.Violations);
    }

    [Fact]
    public void With_no_opt_out_a_clean_migration_passes()
    {
        var operations = Build(migration => migration.AddColumn<string>(name: "Note", table: Table, nullable: true));

        var result = ExpandOnlyMigrationGuard.Evaluate(operations, optOut: null, ExpandOnlyMigrationFamilies.None);

        Assert.True(result.Passed);
        Assert.False(result.HasOptOut);
    }

    // --- The opt-out attribute itself (FR-012) -----------------------------------------------------------------

    [Fact]
    public void The_opt_out_attribute_refuses_a_blank_reason()
    {
        Assert.Throws<ArgumentException>(() => new ExpandOnlyMigrationOptOutAttribute("   ", "#2104", "DropColumn elsa_example.Legacy"));
    }

    [Theory]
    [InlineData("2104")]
    [InlineData("issue 2104")]
    [InlineData("#")]
    [InlineData("")]
    public void The_opt_out_attribute_refuses_a_review_reference_not_in_NNNN_form(string reviewReference)
    {
        Assert.Throws<ArgumentException>(() => new ExpandOnlyMigrationOptOutAttribute("Reason.", reviewReference, "DropColumn elsa_example.Legacy"));
    }

    [Fact]
    public void The_opt_out_attribute_refuses_an_empty_violation_list()
    {
        Assert.Throws<ArgumentException>(() => new ExpandOnlyMigrationOptOutAttribute("Reason.", "#2104"));
    }

    // --- FR-023: a contracting opt-out names its schema family and version, and no other opt-out does ---------------

    private const string Orders = "elsa_orders";

    /// <summary>
    /// Before the migration: <c>elsa_orders</c> is stamped by <c>Orders</c>, which reads 1 and 2; <c>elsa_invoices</c> by
    /// <c>Invoices</c>; and which family stamps <c>elsa_shared</c> cannot be told. <c>elsa_example</c> is not stamped.
    /// </summary>
    private static readonly ExpandOnlyMigrationFamilies Stamped = new(
        new Dictionary<string, string?> { [Orders] = "Orders", ["elsa_invoices"] = "Invoices", ["elsa_shared"] = null },
        new Dictionary<string, IReadOnlyList<string>> { ["Orders"] = ["1", "2"], ["Invoices"] = ["1"] });

    private static ExpandOnlyMigrationOptOutAttribute Contracting(string? family, string? version, params string[] violations) =>
        new(reason: "Nothing reads it once version 2 is finalized.", reviewReference: "#2136", violations) { SchemaFamily = family, FinalizedVersion = version };

    public static TheoryData<string, string> Removals() => new()
    {
        { "DropColumn", "DropColumn elsa_orders.Legacy" },
        { "RenameColumn", "RenameColumn elsa_orders.Legacy" },
        { "DropTable", "DropTable elsa_orders" },
        { "RenameTable", "RenameTable elsa_orders" },
        { "DropIndex", "DropIndex elsa_orders.IX_Orders_Legacy" },
        { "DeleteData", "DeleteData elsa_orders" }
    };

    private static IReadOnlyList<MigrationOperation> Removal(string kind) => Build(migration =>
    {
        _ = kind switch
        {
            "DropColumn" => (object)migration.DropColumn(name: "Legacy", table: Orders),
            "RenameColumn" => migration.RenameColumn(name: "Legacy", table: Orders, newName: "Replacement"),
            "DropTable" => migration.DropTable(name: Orders),
            "RenameTable" => migration.RenameTable(name: Orders, newName: "elsa_orders_v2"),
            "DropIndex" => migration.DropIndex(name: "IX_Orders_Legacy", table: Orders),
            "DeleteData" => migration.DeleteData(table: Orders, keyColumn: "Id", keyValue: "legacy"),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
        };
    });

    [Theory]
    [MemberData(nameof(Removals))]
    public void A_removal_from_a_stamped_table_passes_with_an_opt_out_naming_its_family_and_a_version_it_reads(string kind, string violation)
    {
        var result = ExpandOnlyMigrationGuard.Evaluate(Removal(kind), Contracting("Orders", "2", violation), Stamped);

        Assert.True(result.Passed, string.Join("; ", result.ContractionFaults));
    }

    [Theory]
    [MemberData(nameof(Removals))]
    public void A_removal_from_a_stamped_table_fails_when_its_opt_out_names_no_family_and_says_which_to_name(string kind, string violation)
    {
        var result = ExpandOnlyMigrationGuard.Evaluate(Removal(kind), Contracting(null, null, violation), Stamped);

        Assert.False(result.Passed);
        Assert.Contains("SchemaFamily = \"Orders\"", Assert.Single(result.ContractionFaults), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Orders", null)]
    [InlineData(null, "2")]
    public void A_contracting_opt_out_naming_only_one_of_the_two_fails(string? family, string? version)
    {
        var result = ExpandOnlyMigrationGuard.Evaluate(Removal("DropColumn"), Contracting(family, version, "DropColumn elsa_orders.Legacy"), Stamped);

        Assert.False(result.Passed);
        Assert.Contains("must name SchemaFamily", Assert.Single(result.ContractionFaults), StringComparison.Ordinal);
    }

    [Fact]
    public void An_opt_out_naming_a_family_on_a_migration_that_removes_nothing_a_stamped_family_covers_fails()
    {
        var operations = Build(migration => migration.DropColumn(name: "Legacy", table: Table));

        var result = ExpandOnlyMigrationGuard.Evaluate(operations, Contracting("Orders", "2", "DropColumn elsa_example.Legacy"), Stamped);

        Assert.False(result.Passed);
        Assert.Contains("removes nothing a stamped schema family covers", Assert.Single(result.ContractionFaults), StringComparison.Ordinal);
    }

    [Fact]
    public void A_contracting_opt_out_naming_another_family_fails()
    {
        var result = ExpandOnlyMigrationGuard.Evaluate(Removal("DropColumn"), Contracting("Invoices", "1", "DropColumn elsa_orders.Legacy"), Stamped);

        Assert.False(result.Passed);
        Assert.Contains("belongs to 'Orders'", Assert.Single(result.ContractionFaults), StringComparison.Ordinal);
    }

    [Fact]
    public void A_contracting_opt_out_naming_a_version_the_family_does_not_read_fails()
    {
        var result = ExpandOnlyMigrationGuard.Evaluate(Removal("DropColumn"), Contracting("Orders", "3", "DropColumn elsa_orders.Legacy"), Stamped);

        Assert.False(result.Passed);
        Assert.Contains("version '3' of 'Orders'", Assert.Single(result.ContractionFaults), StringComparison.Ordinal);
    }

    [Fact]
    public void A_migration_that_contracts_two_families_fails_because_one_opt_out_names_one()
    {
        var operations = Build(migration =>
        {
            migration.DropColumn(name: "Legacy", table: Orders);
            migration.DropColumn(name: "Legacy", table: "elsa_invoices");
        });

        var result = ExpandOnlyMigrationGuard.Evaluate(
            operations, Contracting("Orders", "2", "DropColumn elsa_orders.Legacy", "DropColumn elsa_invoices.Legacy"), Stamped);

        Assert.False(result.Passed);
        Assert.Contains("more than one stamped schema family ('Invoices', 'Orders')", Assert.Single(result.ContractionFaults), StringComparison.Ordinal);
    }

    [Fact]
    public void A_removal_from_a_stamped_table_whose_family_cannot_be_told_fails()
    {
        var operations = Build(migration => migration.DropColumn(name: "Legacy", table: "elsa_shared"));

        var result = ExpandOnlyMigrationGuard.Evaluate(operations, Contracting("Orders", "2", "DropColumn elsa_shared.Legacy"), Stamped);

        Assert.False(result.Passed);
        Assert.Contains("'elsa_shared'", Assert.Single(result.ContractionFaults), StringComparison.Ordinal);
    }

    /// <summary>
    /// A reviewed alteration of a stamped table removes nothing, so its opt-out is not contracting: it names no family and
    /// the apply-time check never holds it (spec 185, Decisions, "Every AlterColumn is a violation").
    /// </summary>
    [Fact]
    public void An_alteration_of_a_stamped_table_is_not_contracting()
    {
        var operations = Build(migration => migration.AlterColumn<string>(name: "Name", table: Orders, maxLength: 512, oldMaxLength: 128));

        Assert.True(ExpandOnlyMigrationGuard.Evaluate(operations, Contracting(null, null, "AlterColumn elsa_orders.Name"), Stamped).Passed);
    }

    // --- Which tables a stamped family covered before a migration: the previous migration's target model -------------

    [Fact]
    public void Before_a_contexts_first_migration_no_table_is_stamped()
    {
        var before = ExpandOnlyMigrationFamilies.Before(null, ContractingFamilies, ContractingAssembly);

        Assert.Empty(before.StampedTables);
        Assert.Equal(["1", "2"], before.ReadableVersions[ContractingModule.Family]);
    }

    /// <summary>
    /// A snapshot names its entity types rather than holding them. The module's own stamped tables count; an unstamped
    /// table and the finalization record's own tables, which belong to no module family, do not.
    /// </summary>
    [Fact]
    public void A_snapshot_s_stamped_tables_belong_to_the_module_s_family_and_the_finalization_tables_to_none()
    {
        var snapshot = new ModelBuilder();
        ContractingTargetModel.Build(snapshot, ContractingModule.DropObsolete);
        snapshot.MapSchemaFinalization(ContractingModule.HistoryModule);

        var before = ExpandOnlyMigrationFamilies.Before(snapshot.Model, ContractingFamilies, ContractingAssembly);

        Assert.Equal(new Dictionary<string, string?> { [ContractingModule.RowsTable] = ContractingModule.Family }, before.StampedTables);
    }

    /// <summary>With more than one family, each table's is found through its entity type's name, and an unknown one stays unknown.</summary>
    [Fact]
    public void With_two_families_each_stamped_table_is_resolved_by_its_entity_type_s_name()
    {
        var families = EfSchemaModuleFamilies.FromDeclarations("Sales",
        [
            new EfSchemaFamilyDescriptor("Orders", "Sales", "1", typeof(OrderRow).Assembly) { Entities = [typeof(OrderRow)] },
            new EfSchemaFamilyDescriptor("Invoices", "Sales", "1", typeof(InvoiceRow).Assembly) { Entities = [typeof(InvoiceRow)] }
        ]);
        var snapshot = new ModelBuilder();
        Stamp(snapshot, typeof(OrderRow).FullName!, "elsa_orders");
        Stamp(snapshot, typeof(InvoiceRow).FullName!, "elsa_invoices");
        Stamp(snapshot, "Removed.Since.RetiredRow", "elsa_retired");

        var before = ExpandOnlyMigrationFamilies.Before(snapshot.Model, families, typeof(OrderRow).Assembly);

        Assert.Equal(
            new Dictionary<string, string?> { ["elsa_orders"] = "Orders", ["elsa_invoices"] = "Invoices", ["elsa_retired"] = null },
            before.StampedTables);

        static void Stamp(ModelBuilder model, string entity, string table) => model.Entity(entity, row =>
        {
            row.Property<string>("Id");
            row.Property<string>(EfSchemaVersion.ColumnName);
            row.HasKey("Id");
            row.ToTable(table);
        });
    }

    private static readonly System.Reflection.Assembly ContractingAssembly = typeof(ContractingDbContext).Assembly;

    private static EfSchemaModuleFamilies ContractingFamilies => EfSchemaModuleFamilies.For(ContractingModule.Name, ContractingAssembly);

    private sealed class OrderRow;

    private sealed class InvoiceRow;
}

/// <summary>A shape the guard has never classified, for FR-006's "unknown operation is still a violation".</summary>
internal sealed class UnknownFixtureOperation : MigrationOperation;
