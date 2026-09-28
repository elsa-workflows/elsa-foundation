using System.Text.RegularExpressions;
using Xunit;
using static Elsa.Architecture.Tests.RepoPaths;

namespace Elsa.Architecture.Tests;

/// <summary>
/// #2119 (spec 180 FR-026): every table an EF module maps carries a required schema-version stamp, so a row a newer
/// module version wrote reads as skew instead of as a row this build understands. A table without one could only
/// treat an unstamped row as some baseline, and there is no such rule: a missing version is skew (ADR 0077).
/// <para>
/// The guard reads each module's model snapshots, the per-provider record of the schema its migrations create,
/// because that is what a database gets. Which snapshots it must read comes independently from the
/// <c>[assembly: EfModule]</c> declarations under <c>src/</c>, so a provider context whose snapshot the scan cannot
/// find fails rather than passing unexamined. Contexts that are no EF module, such as the Workbench app's OpenIddict
/// context, which maps OpenIddict's own entity types, are outside the rule.
/// </para>
/// <para>
/// Owner decision Q27 on #2119: every stamp also carries a non-unique index of its own, so the rows still at a given
/// version are found without scanning the table. The index rule reads the same snapshots.
/// </para>
/// </summary>
public sealed class EfSchemaVersionStampGuardTests
{
    private const string StampColumn = "SchemaVersion";

    /// <summary>One table of each module whose stamp #2119 added, both of Publishing's.</summary>
    private static readonly string[] TablesStampedByIssue2119 =
    [
        "elsa_activity_definitions",
        "elsa_otel_spans",
        "elsa_structured_log_records",
        "identity_users",
        "identity_provider_configurations",
        "elsa_secrets",
        "elsa_studio_preferences",
        "elsa_workflow_definitions_v2",
        "elsa_distributed_execution_placement",
        "elsa_distributed_command_transport",
        "elsa_publication_records",
        "elsa_publication_snapshot_reviews"
    ];

    /// <summary>One table of each family stamped before #2119, whose stamp #2119 indexed.</summary>
    private static readonly string[] TablesStampedBeforeIssue2119 =
    [
        "elsa_runtime_workflow_execution_state",
        "elsa3_reusable_import_receipts",
        "elsa_publication_policies",
        "elsa_activity_publication_receipts"
    ];

    [Fact]
    public void Every_table_of_every_EF_module_carries_a_required_schema_version_stamp()
    {
        var violations = FindInDeclaredSnapshots(FindUnstampedTables);

        Assert.True(
            violations.Length == 0,
            $"Every table an EF module maps needs a required '{StampColumn}' column that its stores stamp on write and " +
            "check on read before deserializing anything, so a row a newer module version wrote reports skew (#2119). " +
            "Unstamped tables:" + Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void Every_stamped_table_indexes_its_stamp_without_making_it_unique()
    {
        var violations = FindInDeclaredSnapshots(FindUnindexedStamps);

        Assert.True(
            violations.Length == 0,
            $"Every stamped table needs a non-unique index on '{StampColumn}' alone, so the rows still at a given version " +
            "are found without a scan (#2119, owner decision Q27). A module context gets it from " +
            "modelBuilder.IndexSchemaVersionStamps(). Tables without it:" +
            Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    /// <summary>
    /// Pins what the scans above examined: a snapshot for every provider context every module declares, the tables in
    /// them, the stamps they index, and among those the tables #2119 stamped and the older families' tables it indexed,
    /// on every provider. A scan that stopped finding snapshots, tables or indexes would otherwise pass having checked
    /// nothing.
    /// </summary>
    [Fact]
    public void Guard_examines_every_declared_provider_context_and_the_tables_number_2119_stamped_and_indexed()
    {
        var snapshots = DeclaredSnapshots();
        var sources = snapshots.Values.Select(File.ReadAllText).ToArray();
        var tablesBySnapshot = sources
            .Select(source => FindTables(source).Select(table => table.Name).ToHashSet(StringComparer.Ordinal))
            .ToArray();
        var indexedBySnapshot = sources
            .Select(source => FindIndexedStamps(source).ToHashSet(StringComparer.Ordinal))
            .ToArray();

        // Thirteen [EfModule] declarations with four providers each, and a hundred tables per provider, as of #2119.
        Assert.True(snapshots.Count >= 52, $"Expected a snapshot for at least 52 declared provider contexts; examined {snapshots.Count}.");
        Assert.True(tablesBySnapshot.Sum(tables => tables.Count) >= 400, $"Expected at least 400 mapped tables across those snapshots; found {tablesBySnapshot.Sum(tables => tables.Count)}.");
        Assert.True(indexedBySnapshot.Sum(tables => tables.Count) >= 400, $"Expected at least 400 indexed stamps across those snapshots; found {indexedBySnapshot.Sum(tables => tables.Count)}.");
        foreach (var table in TablesStampedByIssue2119)
            Assert.Equal(4, tablesBySnapshot.Count(tables => tables.Contains(table)));
        foreach (var table in TablesStampedByIssue2119.Concat(TablesStampedBeforeIssue2119))
            Assert.Equal(4, indexedBySnapshot.Count(tables => tables.Contains(table)));
    }

    [Theory]
    [MemberData(nameof(UnstampedFixtures))]
    public void Detector_flags_a_table_without_a_required_stamp(string name, string snapshot, string table)
    {
        Assert.True(FindUnstampedTables(snapshot).Contains(table), $"The detector missed the unstamped fixture '{name}'.");
    }

    [Theory]
    [MemberData(nameof(StampedFixtures))]
    public void Detector_accepts_a_table_with_a_required_stamp(string name, string snapshot)
    {
        Assert.True(FindTables(snapshot).Length > 0, $"The fixture '{name}' maps no table, so accepting it would prove nothing.");
        Assert.True(FindUnstampedTables(snapshot).Length == 0, $"The detector wrongly flagged the stamped fixture '{name}'.");
    }

    [Theory]
    [MemberData(nameof(UnindexedFixtures))]
    public void Detector_flags_a_stamp_without_its_own_non_unique_index(string name, string snapshot, string table)
    {
        Assert.True(FindUnindexedStamps(snapshot).Contains(table), $"The detector missed the unindexed fixture '{name}'.");
    }

    [Theory]
    [MemberData(nameof(IndexedFixtures))]
    public void Detector_accepts_a_stamp_with_its_own_non_unique_index(string name, string snapshot)
    {
        Assert.True(FindIndexedStamps(snapshot).Length > 0, $"The fixture '{name}' indexes no stamp, so accepting it would prove nothing.");
        Assert.True(FindUnindexedStamps(snapshot).Length == 0, $"The detector wrongly flagged the indexed fixture '{name}'.");
    }

    [Fact]
    public void Declaration_scan_reads_every_provider_of_every_module_in_a_file()
    {
        const string source = """
            [assembly: EfModule(
                "First",
                typeof(FirstDbContext),
                Sqlite = typeof(FirstSqliteDbContext),
                SqlServer = typeof(FirstSqlServerDbContext),
                PostgreSql = typeof(FirstPostgreSqlDbContext),
                MySql = typeof(FirstMySqlDbContext))]

            [assembly: EfModule("Second", typeof(SecondDbContext), Sqlite = typeof(SecondSqliteDbContext))]
            """;

        Assert.Equal(
            ["FirstMySqlDbContext", "FirstPostgreSqlDbContext", "FirstSqlServerDbContext", "FirstSqliteDbContext", "SecondSqliteDbContext"],
            FindDeclaredProviderContexts(source).Order(StringComparer.Ordinal));
    }

    public static TheoryData<string, string, string> UnstampedFixtures() => new()
    {
        {
            "a table with no stamp at all",
            Entity("Rows", """
                b.Property<string>("Id").HasColumnType("TEXT");
                b.Property<string>("ContentJson").IsRequired().HasColumnType("TEXT");
                b.ToTable("rows", (string)null);
                """),
            "rows"
        },
        {
            "a stamp that may be null",
            Entity("Rows", """
                b.Property<string>("SchemaVersion")
                    .HasMaxLength(32)
                    .HasColumnType("TEXT");
                b.ToTable("rows", (string)null);
                """),
            "rows"
        },
        {
            "a differently named version column",
            Entity("Preferences", """
                b.Property<int>("PreferenceSchemaVersion")
                    .IsRequired()
                    .HasColumnType("INTEGER");
                b.ToTable("preferences", (string)null);
                """),
            "preferences"
        },
        {
            "the only stamp belongs to another table's entity",
            Entity("Stamped", """
                b.Property<string>("SchemaVersion")
                    .IsRequired()
                    .HasColumnType("TEXT");
                b.ToTable("stamped", (string)null);
                """) + Entity("Unstamped", """
                b.Property<string>("Id").HasColumnType("TEXT");
                b.ToTable("unstamped", (string)null);
                """),
            "unstamped"
        }
    };

    public static TheoryData<string, string> StampedFixtures() => new()
    {
        {
            "a required stamp",
            Entity("Rows", """
                b.Property<string>("SchemaVersion")
                    .IsRequired()
                    .HasMaxLength(32)
                    .HasColumnType("TEXT");
                b.ToTable("rows", (string)null);
                """)
        },
        {
            "a relationship block that maps no table of its own",
            Entity("Rows", """
                b.Property<string>("SchemaVersion")
                    .IsRequired()
                    .HasColumnType("TEXT");
                b.ToTable("rows", (string)null);
                """) + Entity("Rows", """
                b.HasOne("Parents", null).WithMany().HasForeignKey("ParentId");
                """)
        }
    };

    private const string RequiredStamp = """
        b.Property<string>("SchemaVersion")
            .IsRequired()
            .HasMaxLength(32)
            .HasColumnType("TEXT");
        """;

    public static TheoryData<string, string, string> UnindexedFixtures() => new()
    {
        {
            "a stamp with no index",
            Entity("Rows", RequiredStamp + """

                b.ToTable("rows", (string)null);
                """),
            "rows"
        },
        {
            "a unique index on the stamp",
            Entity("Rows", RequiredStamp + """

                b.HasIndex("SchemaVersion")
                    .IsUnique();
                b.ToTable("rows", (string)null);
                """),
            "rows"
        },
        {
            "the stamp only as the first column of a composite index",
            Entity("Rows", RequiredStamp + """

                b.HasIndex("SchemaVersion", "TenantId");
                b.ToTable("rows", (string)null);
                """),
            "rows"
        },
        {
            "the only stamp index belongs to another table's entity",
            Entity("Indexed", RequiredStamp + """

                b.HasIndex("SchemaVersion");
                b.ToTable("indexed", (string)null);
                """) + Entity("Unindexed", RequiredStamp + """

                b.ToTable("unindexed", (string)null);
                """),
            "unindexed"
        }
    };

    public static TheoryData<string, string> IndexedFixtures() => new()
    {
        {
            "a non-unique index under EF's default name",
            Entity("Rows", RequiredStamp + """

                b.HasIndex("SchemaVersion");
                b.ToTable("rows", (string)null);
                """)
        },
        {
            "a non-unique index under an explicit name",
            Entity("Rows", RequiredStamp + """

                b.HasIndex("SchemaVersion")
                    .HasDatabaseName("ix_rows_schema_version");
                b.ToTable("rows", (string)null);
                """)
        }
    };

    private static string Entity(string name, string body) =>
        $"            modelBuilder.Entity(\"{name}\", b =>\n                {{\n{Indent(body)}\n                }});\n";

    private static string Indent(string body) =>
        string.Join('\n', body.Split('\n').Select(line => "                    " + line));

    /// <summary>
    /// Every provider context the <c>[assembly: EfModule]</c> declarations under <c>src/</c> name, mapped to its one
    /// model snapshot. Fails when a declared context has no snapshot or more than one.
    /// </summary>
    private static Dictionary<string, string> DeclaredSnapshots()
    {
        var sources = Directory.EnumerateFiles(Path.Join(RepoRoot, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(file => !IsBuildOutput(file))
            .ToArray();
        var snapshots = sources
            .Where(file => file.EndsWith("ModelSnapshot.cs", StringComparison.Ordinal))
            .ToLookup(file => Path.GetFileName(file)[..^"ModelSnapshot.cs".Length], StringComparer.Ordinal);
        var contexts = sources
            .SelectMany(file => FindDeclaredProviderContexts(File.ReadAllText(file)))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var unmatched = contexts.Where(context => snapshots[context].Count() != 1).Order(StringComparer.Ordinal).ToArray();
        Assert.True(
            unmatched.Length == 0,
            "Every provider context an [EfModule] declares needs exactly one model snapshot under src/. Contexts without one:" +
            Environment.NewLine + string.Join(Environment.NewLine, unmatched));
        return contexts.ToDictionary(context => context, context => snapshots[context].Single(), StringComparer.Ordinal);
    }

    /// <summary>The tables <paramref name="detector"/> flags in any declared snapshot, each prefixed with its snapshot.</summary>
    private static string[] FindInDeclaredSnapshots(Func<string, string[]> detector) =>
        DeclaredSnapshots().Values
            .SelectMany(snapshot => detector(File.ReadAllText(snapshot))
                .Select(table => $"{Path.GetRelativePath(RepoRoot, snapshot)}: {table}"))
            .Order(StringComparer.Ordinal)
            .ToArray();

    private static readonly Regex ModuleDeclarationPattern = new(@"\[\s*assembly\s*:\s*EfModule\s*\(", RegexOptions.Compiled);

    private static readonly Regex ProviderContextPattern = new(@"\b(?:Sqlite|SqlServer|PostgreSql|MySql)\s*=\s*typeof\(\s*(\w+)\s*\)", RegexOptions.Compiled);

    private static IEnumerable<string> FindDeclaredProviderContexts(string source) =>
        ModuleDeclarationPattern.Matches(source)
            .Select(match => Block(source, match.Index + match.Length - 1, '(', ')'))
            .SelectMany(declaration => ProviderContextPattern.Matches(declaration).Select(match => match.Groups[1].Value));

    private static readonly Regex EntityPattern = new(@"modelBuilder\.Entity\(""(?<name>[^""]+)"",\s*b\s*=>\s*\{", RegexOptions.Compiled);

    private static readonly Regex TablePattern = new(@"\bb\.ToTable\(""(?<table>[^""]+)""", RegexOptions.Compiled);

    private static readonly Regex RequiredStampPattern = new(@"\bb\.Property<string>\(""" + StampColumn + @"""\)(?<chain>[^;]*);", RegexOptions.Compiled);

    /// <summary>
    /// The tables a snapshot maps, each with the body of the <c>modelBuilder.Entity</c> block that maps it. A block
    /// without <c>b.ToTable</c> only configures relationships of an entity mapped elsewhere in the snapshot.
    /// </summary>
    private static (string Name, string Body)[] FindTables(string snapshot) =>
        EntityPattern.Matches(snapshot)
            .Select(match => Block(snapshot, match.Index + match.Length - 1, '{', '}'))
            .Select(body => (Match: TablePattern.Match(body), Body: body))
            .Where(entity => entity.Match.Success)
            .Select(entity => (entity.Match.Groups["table"].Value, entity.Body))
            .ToArray();

    private static readonly Regex StampIndexPattern = new(@"\bb\.HasIndex\(""" + StampColumn + @"""\)(?<chain>[^;]*);", RegexOptions.Compiled);

    private static bool HasNonUniqueStampIndex(string body) =>
        StampIndexPattern.Matches(body).Any(index => !index.Groups["chain"].Value.Contains(".IsUnique()", StringComparison.Ordinal));

    /// <summary>The stamped tables whose entity block indexes the stamp alone, without uniqueness.</summary>
    private static string[] FindIndexedStamps(string snapshot) =>
        FindTables(snapshot)
            .Where(table => HasNonUniqueStampIndex(table.Body))
            .Select(table => table.Name)
            .ToArray();

    /// <summary>The stamped tables whose entity block has no such index. An unstamped table is the other rule's.</summary>
    private static string[] FindUnindexedStamps(string snapshot) =>
        FindTables(snapshot)
            .Where(table => RequiredStampPattern.IsMatch(table.Body) && !HasNonUniqueStampIndex(table.Body))
            .Select(table => table.Name)
            .ToArray();

    private static string[] FindUnstampedTables(string snapshot) =>
        FindTables(snapshot)
            .Where(table => !RequiredStampPattern.Matches(table.Body).Any(stamp => stamp.Groups["chain"].Value.Contains(".IsRequired()", StringComparison.Ordinal)))
            .Select(table => table.Name)
            .ToArray();

    /// <summary>Returns the text from <paramref name="open"/> through its matching closing character.</summary>
    private static string Block(string source, int open, char opening, char closing)
    {
        var depth = 0;
        for (var index = open; index < source.Length; index++)
        {
            if (source[index] == opening)
            {
                depth++;
            }
            else if (source[index] == closing)
            {
                depth--;
                if (depth == 0)
                    return source[open..(index + 1)];
            }
        }

        return source[open..];
    }
}
