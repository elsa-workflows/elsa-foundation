using System.Reflection;
using System.Text.RegularExpressions;
using Elsa3.Activities.Design.Import.Models;
using Elsa3.Activities.Design.Import.Reconciliation.Models;
using Elsa3.Mapping.Services;
using Elsa3.Migration.Tooling;
using Elsa3.Models;

namespace Elsa3.Migration.Tests;

/// <summary>
/// Spec 1237 T004 (FR-016, FR-019): every code the converter, the import, reconciliation and the tooling can emit has a
/// code-table entry with a remedy, and no Elsa 3 code is emitted as an inline literal the reflection cannot see.
/// </summary>
public sealed class Elsa3MigrationCodeTableTests
{
    private static readonly Type[] ConstantClasses =
    [
        typeof(Elsa3ActivityDiagnosticCodes),
        typeof(Elsa3MigrationDiagnosticCodes),
        typeof(Elsa3ImportDiagnosticCodes),
        typeof(ReusableActivityImportDiagnosticCodes),
        typeof(Elsa3ReconciliationDiagnosticCodes),
        typeof(Elsa3MigrationToolingCodes),
    ];

    private static readonly string[] CodePrefixes = ["ELS3-", "VF-IMP-"];

    [Fact]
    public void Every_emitted_code_has_an_entry()
    {
        var missing = DeclaredCodes().Where(code => !Elsa3MigrationCodeTable.Contains(code)).Order(StringComparer.Ordinal).ToArray();

        Assert.True(missing.Length == 0, "Codes without a code-table entry: " + string.Join(", ", missing));
    }

    [Fact]
    public void Every_entry_names_a_declared_code_and_has_a_defined_remedy()
    {
        var declared = DeclaredCodes().ToHashSet(StringComparer.Ordinal);

        Assert.All(Elsa3MigrationCodeTable.Codes, code =>
        {
            Assert.Contains(code, declared);
            Assert.True(Enum.IsDefined(Elsa3MigrationCodeTable.Get(code).Remedy), code);
        });
    }

    [Fact]
    public void Component_codes_build_a_component_and_routine_codes_are_the_null_policy_record()
    {
        var components = Elsa3MigrationCodeTable.Codes.Where(code => Elsa3MigrationCodeTable.Get(code).ComponentKind is not null).Order(StringComparer.Ordinal).ToArray();

        Assert.Equal(
            new[]
            {
                Elsa3ImportDiagnosticCodes.CustomStorageDriverReplaced,
                Elsa3ActivityDiagnosticCodes.UnmappedType,
                Elsa3ImportDiagnosticCodes.HostFunctionMissing,
                Elsa3ImportDiagnosticCodes.MemoryStoragePersisted,
                Elsa3ImportDiagnosticCodes.VariableTypeUnregistered,
            }.Order(StringComparer.Ordinal),
            components);
        Assert.All(components, code => Assert.Equal(Elsa3MigrationRemedy.BuildComponent, Elsa3MigrationCodeTable.Get(code).Remedy));
        Assert.Equal([Elsa3ActivityDiagnosticCodes.NullPolicies], Elsa3MigrationCodeTable.Codes.Where(code => Elsa3MigrationCodeTable.Get(code).Routine));
        Assert.Equal(Elsa3MigrationRemedy.PatchDefinition, Elsa3MigrationCodeTable.Get(Elsa3ImportDiagnosticCodes.ReplacedFunctionReference).Remedy);
        Assert.Null(Elsa3MigrationCodeTable.Get(Elsa3ImportDiagnosticCodes.ReplacedFunctionReference).ComponentKind);
        Assert.Equal(Elsa3MigrationRemedy.PatchDefinition, Elsa3MigrationCodeTable.Get(Elsa3ReconciliationDiagnosticCodes.BlockedByDependency).Remedy);
    }

    [Fact]
    public void An_unlisted_Elsa4_code_gets_patch_definition_and_the_default_text()
    {
        var entry = Elsa3MigrationCodeTable.Get("WF-PUBLISH-UNKNOWN");

        Assert.Equal((Elsa3MigrationRemedy.PatchDefinition, Elsa3MigrationCodeTable.UnlistedText), (entry.Remedy, entry.FixedText));
    }

    [Fact]
    public void No_Elsa3_code_is_emitted_as_an_inline_literal()
    {
        var root = Path.Combine(RepoRoot(), "extensions", "Elsa3", "src");
        var literal = new Regex("\"(ELS3-|VF-IMP-)[A-Z0-9-]*\"");
        var constant = new Regex(@"\bconst\s+string\s+\w+\s*=\s*""(ELS3-|VF-IMP-)");

        var offenders = Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .SelectMany(path => File.ReadLines(path).Select((line, index) => (path, line, index)))
            .Where(item => literal.IsMatch(item.line) && !constant.IsMatch(item.line))
            .Select(item => $"{Path.GetRelativePath(root, item.path)}:{item.index + 1}")
            .ToArray();

        Assert.True(offenders.Length == 0, "Inline code literals outside the constant classes: " + string.Join(", ", offenders));
    }

    private static IEnumerable<string> DeclaredCodes() => ConstantClasses
        .SelectMany(type => type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
        .Where(field => field is { IsLiteral: true, IsInitOnly: false } && field.FieldType == typeof(string))
        .Select(field => (string)field.GetRawConstantValue()!)
        .Where(value => CodePrefixes.Any(prefix => value.StartsWith(prefix, StringComparison.Ordinal)))
        .Distinct(StringComparer.Ordinal);

    private static string RepoRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Elsa.Server.slnx")))
                return directory.FullName;
        throw new InvalidOperationException("The repository root (Elsa.Server.slnx) was not found above the test output.");
    }
}
