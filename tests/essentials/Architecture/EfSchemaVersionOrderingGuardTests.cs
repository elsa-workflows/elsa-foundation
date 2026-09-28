using System.Text;
using System.Text.RegularExpressions;
using Xunit;
using static Elsa.Architecture.Tests.RepoPaths;

namespace Elsa.Architecture.Tests;

/// <summary>
/// ADR 0077 and #1950 separate "this build cannot read this row's version" from "this row is not
/// internally consistent". #1952 routed every EF store's version comparison through
/// <c>EfSchemaVersion</c>; #1955 moved that term to the front of its condition, because an integrity
/// clause evaluated first reports corruption for a row whose only real problem is skew.
/// <para>
/// This guard keeps that ordering. It scans production sources under <c>src/</c> and fails when a
/// <c>EfSchemaVersion.Readable</c> / <c>EfSchemaVersion.NotReadable</c> call has another boolean
/// clause ahead of it in the same condition, whether the condition is written on one line or spread
/// over several. The detector itself is pinned by fixtures below, so a scanner that silently stops
/// finding anything fails too.
/// </para>
/// <para>
/// #2108: within-condition ordering is not enough. Four stores called <c>RuntimeArtifactJson.Deserialize</c>
/// on a row's content in a statement <em>earlier than</em> the condition that checked its version, so the
/// version term being first within its own condition never ran before the deserializer had already thrown
/// on the changed shape a newer schema version wrote. The second guard below widens the rule to: no call
/// to a method named <c>Deserialize</c> may precede the version check anywhere in its enclosing method,
/// not just within the condition that holds the check. A fifth instance - <c>EfSchedulerStateStore</c>'s
/// own custom <c>EfSchedulerStateJson.Deserialize</c> wrapper - surfaced only once the rule stopped
/// naming specific wrapper classes and started matching the method name itself, which is why the rule
/// matches the method name rather than a fixed list of receivers.
/// </para>
/// <para>
/// #2119 stamped the ten EF modules and two Publishing tables that had no stamp. Their stores also check with
/// <c>EfSchemaVersion.EnsureReadable</c>, which throws by itself rather than feeding a condition, and
/// deserialize through receiverless helpers (<c>Deserialize&lt;T&gt;(json)</c>, <c>DeserializePayload(row)</c>)
/// or <c>Deserialize</c>-prefixed ones
/// (<c>EfIdentityStoreSupport.DeserializeSet</c>). The widened guard counts all of them as what they are: a
/// version check, and deserializers that must not run ahead of it. The two modules that map domain types
/// directly (Workflows and Activities design) have no store code between EF reading a row and building it, so
/// their check is <c>EfSchemaVersionMaterializationInterceptor</c>, which this lexical scan does not see.
/// </para>
/// <para>
/// Spec 180, FR-011: the version check must be the first thing a read path does with a row at all, not only the first
/// clause of its own condition, and not only ahead of its deserializers. The third rule fails the build when any statement
/// ahead of the check, in the same method, reads a member of the checked row other than its stamp: a projection compared,
/// an identity decoded, a hash recomputed. Each evaluates the shape this build writes against a row a newer build may have
/// written, and reports that skew as corruption. The row is the expression whose <c>SchemaVersion</c> the check passes, so
/// a check whose stamp arrives through a parameter or a projection is not judged by it.
/// </para>
/// <para>
/// Blind spot: detection here is lexical and scoped to a single method body. It cannot follow a
/// deserialize call reached through a helper method the version-checking method calls, nor one written
/// inside a lambda body - either indirection puts the call outside the enclosing-method text this scan
/// walks, so an out-of-order deserializer hidden behind either one will not be flagged. It also knows a
/// deserializer only by a name starting with <c>Deserialize</c>, so a helper named otherwise (<c>ReadJson</c>,
/// <c>ReadState</c>) is not seen.
/// </para>
/// </summary>
public sealed class EfSchemaVersionOrderingGuardTests
{
    [Fact]
    public void Schema_version_is_the_first_clause_of_its_condition_in_every_production_source()
    {
        var sourceRoot = Path.Join(RepoRoot, "src");
        var violations = Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
            .Where(file => !IsBuildOutput(file))
            .SelectMany(file => FindLateSchemaChecks(File.ReadAllText(file))
                .Select(line => $"{Path.GetRelativePath(RepoRoot, file)}({line})"))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            violations.Length == 0,
            "EfSchemaVersion.Readable/NotReadable must be the first clause evaluated in its condition, " +
            "so a skewed row is diagnosed as skew rather than as corruption. Offending call sites:" +
            Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void Guard_scans_the_call_sites_it_claims_to_scan()
    {
        var sourceRoot = Path.Join(RepoRoot, "src");
        var callSites = Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
            .Where(file => !IsBuildOutput(file))
            .Sum(file => CountCalls(File.ReadAllText(file)));

        // The ordering rule is only meaningful while EF stores actually route through EfSchemaVersion.
        // If this ever drops to zero the first test passes vacuously, so pin a floor rather than a count.
        Assert.True(callSites >= 30, $"Expected the EF stores to keep routing through EfSchemaVersion; found {callSites} call sites.");
    }

    [Theory]
    [MemberData(nameof(OutOfOrderFixtures))]
    public void Detector_flags_a_condition_whose_schema_check_is_not_first(string name, string source)
    {
        Assert.True(FindLateSchemaChecks(source).Length > 0, $"The detector missed the out-of-order fixture '{name}'.");
    }

    [Theory]
    [MemberData(nameof(InOrderFixtures))]
    public void Detector_accepts_a_condition_whose_schema_check_is_first(string name, string source)
    {
        Assert.True(FindLateSchemaChecks(source).Length == 0, $"The detector wrongly flagged the in-order fixture '{name}'.");
    }

    /// <summary>
    /// #2108: <c>EfExecutionLivenessStateStore.Read</c>, <c>EfWorkflowHoldStateStore.Read</c>,
    /// <c>EfWorkflowAlterationStore.ReadPlan</c>, and <c>WorkflowTestScopeEfSupport.Read</c> each had the
    /// version term first in its own condition (the first guard above accepted all four), but deserialized
    /// the row's content in an earlier statement that ran unconditionally. This widened scan catches that:
    /// no deserialize call may precede the version check anywhere in the method that holds it.
    /// </summary>
    [Fact]
    public void No_deserialization_of_row_content_precedes_the_version_check_anywhere_in_its_method()
    {
        var sourceRoot = Path.Join(RepoRoot, "src");
        var violations = Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
            .Where(file => !IsBuildOutput(file))
            .SelectMany(file => FindDeserializeBeforeVersionCheck(File.ReadAllText(file))
                .Select(line => $"{Path.GetRelativePath(RepoRoot, file)}({line})"))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            violations.Length == 0,
            "No call to a method whose name starts with Deserialize may run before EfSchemaVersion.Readable/NotReadable/EnsureReadable " +
            "anywhere in the same method, or a skewed row's changed shape is reported as corruption before " +
            "the version check ever gets to run (#2108). Offending call sites:" +
            Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void Widened_guard_examines_the_stores_number_2108_fixed()
    {
        var sourceRoot = Path.Join(RepoRoot, "src");
        var deserializeCallSitesByFile = Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
            .Where(file => !IsBuildOutput(file))
            .GroupBy(Path.GetFileName)
            .ToDictionary(group => group.Key!, group => group.Sum(file => CountDeserializeCalls(File.ReadAllText(file))));

        // If any of these were renamed or merged elsewhere, this floor keeps the widened scan honest about
        // still reaching the exact call sites #2108 fixed, rather than passing because it stopped looking.
        // EfSchedulerStateStore.cs joined this list in round 2, once the rule stopped naming specific
        // wrapper classes and started matching any Deserialize call - which is how its own custom
        // EfSchedulerStateJson.Deserialize wrapper surfaced.
        string[] storesFixedByIssue2108 =
        [
            "EfExecutionLivenessStateStore.cs",
            "EfWorkflowHoldStateStore.cs",
            "EfWorkflowAlterationStore.cs",
            "WorkflowTestScopeEfSupport.cs",
            "EfSchedulerStateStore.cs"
        ];

        foreach (var store in storesFixedByIssue2108)
        {
            Assert.True(
                deserializeCallSitesByFile.TryGetValue(store, out var count) && count > 0,
                $"Expected the widened scan to examine '{store}' - one of the stores #2108 fixed - and find at least one Deserialize call site in it.");
        }
    }

    /// <summary>
    /// The stores #2119 stamped check with <c>EnsureReadable</c> and deserialize through receiverless or
    /// <c>Deserialize</c>-prefixed helpers. Each file named here holds both, so a scan that stopped recognising
    /// either form would find nothing in it and fail here instead of passing the ordering rule vacuously.
    /// </summary>
    [Fact]
    public void Widened_guard_examines_the_stores_number_2119_stamped()
    {
        var sources = Directory.EnumerateFiles(Path.Join(RepoRoot, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(file => !IsBuildOutput(file))
            .GroupBy(Path.GetFileName)
            .ToDictionary(group => group.Key!, group => group.Select(File.ReadAllText).ToArray());

        string[] storesStampedByIssue2119 =
        [
            "EfStructuredLogStore.cs",
            "EfOpenTelemetryStore.cs",
            "EfExecutionCommandTransport.cs",
            "IdentityEntityFrameworkAdapterSupport.cs",
            "EfApplicationStore.cs",
            "EfProviderConfigurationStore.cs"
        ];

        foreach (var store in storesStampedByIssue2119)
        {
            Assert.True(sources.TryGetValue(store, out var files), $"Expected '{store}', one of the stores #2119 stamped, under src/.");
            Assert.True(files!.Sum(CountCalls) > 0, $"Expected the guard to find an EfSchemaVersion check in '{store}'.");
            Assert.True(files!.Sum(CountDeserializeCalls) > 0, $"Expected the guard to find a deserialize call in '{store}'.");
        }
    }

    /// <summary>
    /// #2120: the finalization store every EF module maps checks a record row's stamp with <c>EnsureReadable</c> and then
    /// parses its JSON through a receiverless <c>Deserialize</c> helper. The guard must see both, or the ordering rule
    /// would pass over it vacuously.
    /// </summary>
    [Fact]
    public void Widened_guard_examines_the_finalization_store()
    {
        var store = Directory.EnumerateFiles(Path.Join(RepoRoot, "src"), "EfSchemaFinalizationStore.cs", SearchOption.AllDirectories)
            .Where(file => !IsBuildOutput(file))
            .Select(File.ReadAllText)
            .Single();

        Assert.True(CountCalls(store) > 0, "Expected the guard to find an EfSchemaVersion check in the finalization store.");
        Assert.True(CountDeserializeCalls(store) > 0, "Expected the guard to find a deserialize call in the finalization store.");
    }

    [Fact]
    public void No_statement_reads_the_checked_row_before_its_version_check_anywhere_in_its_method()
    {
        var violations = Directory.EnumerateFiles(Path.Join(RepoRoot, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(file => !IsBuildOutput(file))
            .SelectMany(file => FindRowReadBeforeVersionCheck(File.ReadAllText(file))
                .Select(line => $"{Path.GetRelativePath(RepoRoot, file)}({line})"))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            violations.Length == 0,
            "A read path checks a row's schema version before it reads anything else of the row, in any statement of " +
            "its method, or an integrity clause written for this build's shape reports a newer row as corrupt (spec 180, " +
            "FR-011). Offending checks:" + Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    /// <summary>
    /// SC-006: the rule sees the read paths research.md ("Ordering") named - the two Publishing stores that checked
    /// identity projections in an earlier statement, now restructured - and finds each check's row, so it passes on them
    /// by judging them rather than by missing them.
    /// </summary>
    [Fact]
    public void Row_read_guard_judges_the_read_paths_research_named()
    {
        string[] restructured =
        [
            "EfActivityPublicationReceiptStore.cs",
            "EfActivityDraftTestRunStore.cs",
            "EfExecutionLivenessStateStore.cs",
            "EfWorkflowHoldStateStore.cs",
            "EfWorkflowAlterationStore.cs",
            "WorkflowTestScopeEfSupport.cs"
        ];
        var sources = Directory.EnumerateFiles(Path.Join(RepoRoot, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(file => !IsBuildOutput(file))
            .GroupBy(Path.GetFileName)
            .ToDictionary(group => group.Key!, group => group.Select(File.ReadAllText).ToArray());

        foreach (var store in restructured)
        {
            Assert.True(sources.TryGetValue(store, out var files), $"Expected '{store}' under src/.");
            Assert.True(files!.Sum(CountJudgedChecks) > 0, $"Expected the row-read rule to find a check whose row it can name in '{store}'.");
        }
    }

    [Theory]
    [MemberData(nameof(RowReadBeforeCheckFixtures))]
    public void Row_read_detector_flags_a_row_read_ahead_of_its_version_check(string name, string source) =>
        Assert.True(FindRowReadBeforeVersionCheck(source).Length > 0, $"The row-read detector missed the fixture '{name}'.");

    [Theory]
    [MemberData(nameof(RowReadAfterCheckFixtures))]
    public void Row_read_detector_accepts_a_version_check_that_comes_first(string name, string source) =>
        Assert.True(FindRowReadBeforeVersionCheck(source).Length == 0, $"The row-read detector wrongly flagged the fixture '{name}'.");

    private static int CountJudgedChecks(string source)
    {
        var masked = MaskLiteralsAndComments(source);
        return CallTokens.Sum(token =>
        {
            var count = 0;
            for (var index = masked.IndexOf(token, StringComparison.Ordinal); index >= 0; index = masked.IndexOf(token, index + token.Length, StringComparison.Ordinal))
                count += CheckedRow.IsMatch(masked, index + token.Length) ? 1 : 0;
            return count;
        });
    }

    public static TheoryData<string, string> RowReadBeforeCheckFixtures() => new()
    {
        {
            "an integrity clause in an earlier statement (the Publishing receipt shape research.md named)",
            """
            var receiptKey = ReceiptKey(tenant, key);
            if (!StringComparer.Ordinal.Equals(row.Id, PhysicalId(scope, receiptKey)))
                throw new InvalidOperationException("identity projection is corrupt");
            if (EfSchemaVersion.NotReadable(Module.Chain, row.SchemaVersion))
                throw new InvalidOperationException("unsupported");
            """
        },
        {
            "an identity decoded ahead of the check",
            """
            var sourceReferenceId = Decode(row.SourceReferenceId);
            if (EfSchemaVersion.NotReadable(Module.Chain, row.SchemaVersion) || row.Revision <= 0)
                throw new InvalidDataException("corrupt");
            """
        },
        {
            "a statement earlier in the method carrying its own integrity chain",
            """
            if (row.Revision <= 0 || row.ScopeKey != Encode(scope))
                throw new InvalidDataException("corrupt");
            if (EfSchemaVersion.NotReadable(Module.Chain, row.SchemaVersion))
                throw new InvalidDataException("corrupt");
            """
        },
        {
            "a helper handed a projection ahead of an EnsureReadable statement",
            """
            public sealed class Store
            {
                private static Record Read(Row record, string id)
                {
                    EnsureProjection(id, record.IdHash, record.IdOrderKey);
                    EfSchemaVersion.EnsureReadable(Module.Chain, record.SchemaVersion);
                    return Map(record);
                }
            }
            """
        }
    };

    public static TheoryData<string, string> RowReadAfterCheckFixtures() => new()
    {
        {
            "the check first, integrity clauses after it",
            """
            EfSchemaVersion.EnsureReadable(Module.Chain, row.SchemaVersion);
            var receiptKey = ReceiptKey(tenant, key);
            if (!StringComparer.Ordinal.Equals(row.Id, PhysicalId(scope, receiptKey)))
                throw new InvalidOperationException("identity projection is corrupt");
            """
        },
        {
            "the check as the first clause, the row read in later clauses of the same condition",
            """
            if (EfSchemaVersion.NotReadable(Module.Chain, row.SchemaVersion) || row.Revision <= 0 || row.ScopeKey != Encode(scope))
                throw new InvalidDataException("corrupt");
            """
        },
        {
            "a second check of the row after the first has settled its version",
            """
            EfSchemaVersion.EnsureReadable(Module.Chain, row.SchemaVersion);
            var content = Module.Chain.Upcast(row.SchemaVersion, Table, nameof(row.ContentJson), row.ContentJson);
            if (EfSchemaVersion.NotReadable(Module.Chain, row.SchemaVersion) || row.Revision <= 0)
                throw new InvalidDataException("corrupt");
            """
        },
        {
            "a query over the rows ahead of a check inside a lambda over each row",
            """
            var rows = await context.Rows.Where(row => row.ScopeKey == key).ToListAsync();
            if (rows.Any(row => EfSchemaVersion.NotReadable(Module.Chain, row.SchemaVersion) || row.Revision <= 0))
                throw new InvalidDataException("corrupt");
            """
        },
        {
            "an expression-bodied check after a member that maps the row",
            """
            public sealed class Store
            {
                private static Role Map(RoleEntity entity) => new(entity.RoleId, entity.Name);

                private static bool Matches(RoleEntity entity) =>
                    EfSchemaVersion.Readable(Module.Chain, entity.SchemaVersion) && entity.Revision > 0;
            }
            """
        },
        {
            "a check of a stamp handed in as a parameter",
            """
            public static void EnsureRowEnvelope(string schemaVersion, string scopeKey, long revision)
            {
                if (EfSchemaVersion.NotReadable(Module.Chain, schemaVersion) || revision <= 0)
                    throw new InvalidDataException("corrupt");
            }
            """
        }
    };

    [Theory]
    [MemberData(nameof(DeserializeBeforeCheckFixtures))]
    public void Widened_detector_flags_a_deserialize_call_earlier_in_the_method_than_the_version_check(string name, string source)
    {
        Assert.True(FindDeserializeBeforeVersionCheck(source).Length > 0, $"The widened detector missed the fixture '{name}'.");
    }

    [Theory]
    [MemberData(nameof(DeserializeAfterCheckFixtures))]
    public void Widened_detector_accepts_a_deserialize_call_that_follows_the_version_check(string name, string source)
    {
        Assert.True(FindDeserializeBeforeVersionCheck(source).Length == 0, $"The widened detector wrongly flagged the fixture '{name}'.");
    }

    [Theory]
    [MemberData(nameof(InOrderFixtures))]
    public void Widened_detector_still_accepts_every_original_in_order_fixture(string name, string source)
    {
        Assert.True(FindDeserializeBeforeVersionCheck(source).Length == 0, $"The widened detector wrongly flagged the original in-order fixture '{name}'.");
    }

    public static TheoryData<string, string> DeserializeBeforeCheckFixtures() => new()
    {
        {
            "deserialize as an earlier statement, version term still first within its own condition",
            """
            var state = RuntimeArtifactJson.Deserialize<State>(row.ContentJson);
            var valid =
                EfSchemaVersion.Readable(Module.Chain, row.SchemaVersion) &&
                row.Id == state.Id;
            if (!valid)
                throw new InvalidDataException("corrupt");
            """
        },
        {
            "deserialize inside an earlier try block, guard clause runs afterwards",
            """
            Row state;
            try { state = RuntimeArtifactJson.Deserialize<Row>(row.ContentJson); }
            catch (JsonException exception) { throw new InvalidDataException("bad json", exception); }
            if (EfSchemaVersion.NotReadable(Module.Chain, row.SchemaVersion) || row.Id != state.Id)
                throw new InvalidDataException("corrupt");
            """
        },
        {
            "PublishingEfJson.Deserialize as an earlier statement",
            """
            var receipt = PublishingEfJson.Deserialize<Receipt>(row.Content, "receipt");
            if (EfSchemaVersion.NotReadable(Module.Chain, row.SchemaVersion) || row.Id != receipt.Id)
                throw new InvalidDataException("corrupt");
            """
        },
        {
            "bare JsonSerializer.Deserialize as an earlier statement",
            """
            var content = JsonSerializer.Deserialize<Content>(row.ContentJson, JsonOptions) ?? throw new JsonException("empty");
            if (EfSchemaVersion.NotReadable(Module.Chain, row.SchemaVersion) || row.Id != content.Id)
                throw new InvalidDataException("corrupt");
            """
        },
        {
            "a custom wrapper class's Deserialize as an earlier statement",
            """
            var state = FooJson.Deserialize(row.ContentJson);
            if (EfSchemaVersion.NotReadable(Module.Chain, row.SchemaVersion) || row.Id != state.Id)
                throw new InvalidDataException("corrupt");
            """
        },
        {
            "receiverless generic Deserialize ahead of an EnsureReadable statement",
            """
            var span = Deserialize<TelemetrySpan>(row.PayloadJson);
            EfSchemaVersion.EnsureReadable(Module.Chain, row.SchemaVersion);
            return span;
            """
        },
        {
            "receiverless Deserialize-prefixed helper ahead of an EnsureReadable statement",
            """
            var payload = DeserializePayload(row);
            EfSchemaVersion.EnsureReadable(Module.Chain, row.SchemaVersion);
            return payload;
            """
        },
        {
            "member-access Deserialize-prefixed helper ahead of the version check",
            """
            var ids = EfIdentityStoreSupport.DeserializeSet(row.ClaimIdsJson);
            if (EfSchemaVersion.NotReadable(Module.Chain, row.SchemaVersion) || ids.Count == 0)
                throw new InvalidDataException("corrupt");
            """
        },
        {
            "deserialize earlier in the same expression-bodied member",
            """
            public sealed class Store
            {
                private static bool Matches(RoleEntity entity) =>
                    Support.DeserializeSet(entity.PermissionsJson).Count > 0 &&
                    EfSchemaVersion.Readable(Module.Chain, entity.SchemaVersion);
            }
            """
        },
        {
            "a method whose class constraint must not read as a type declaration",
            """
            public sealed class Store
            {
                private static T Read<T>(Row row) where T : class
                {
                    var value = Support.DeserializeSet(row.Json);
                    EfSchemaVersion.EnsureReadable(Module.Chain, row.SchemaVersion);
                    return value;
                }
            }
            """
        }
    };

    public static TheoryData<string, string> DeserializeAfterCheckFixtures() => new()
    {
        {
            "version check first, deserialize follows as the next statement",
            """
            if (EfSchemaVersion.NotReadable(Module.Chain, row.SchemaVersion) || row.Revision <= 0)
                throw new InvalidDataException("corrupt");
            var state = RuntimeArtifactJson.Deserialize<Row>(row.ContentJson);
            return state;
            """
        },
        {
            "version check and deserialize both inside the same wrapping try, check first",
            """
            try
            {
                if (EfSchemaVersion.NotReadable(Module.Chain, row.SchemaVersion) || row.Revision <= 0)
                    throw new InvalidDataException("corrupt");
                var state = RuntimeArtifactJson.Deserialize<Row>(row.ContentJson);
                return state;
            }
            catch (JsonException exception) { throw new InvalidDataException("bad json", exception); }
            """
        },
        {
            "an unrelated deserialize call in a different, earlier method",
            """
            private static Cursor DecodeCursor(string token) => RuntimeArtifactJson.Deserialize<Cursor>(token);

            private static Row Read(Entity row, string scope)
            {
                if (EfSchemaVersion.NotReadable(Module.Chain, row.SchemaVersion) || row.Revision <= 0)
                    throw new InvalidDataException("corrupt");
                return Project(row);
            }
            """
        },
        {
            "version check first, PublishingEfJson.Deserialize follows as the next statement",
            """
            if (EfSchemaVersion.NotReadable(Module.Chain, row.SchemaVersion) || row.Revision <= 0)
                throw new InvalidDataException("corrupt");
            var receipt = PublishingEfJson.Deserialize<Receipt>(row.Content, "receipt");
            return receipt;
            """
        },
        {
            "version check first, bare JsonSerializer.Deserialize follows as the next statement",
            """
            if (EfSchemaVersion.NotReadable(Module.Chain, row.SchemaVersion) || row.Revision <= 0)
                throw new InvalidDataException("corrupt");
            var content = JsonSerializer.Deserialize<Content>(row.ContentJson, JsonOptions) ?? throw new JsonException("empty");
            return content;
            """
        },
        {
            "version check first, a custom wrapper class's Deserialize follows as the next statement",
            """
            if (EfSchemaVersion.NotReadable(Module.Chain, row.SchemaVersion) || row.Revision <= 0)
                throw new InvalidDataException("corrupt");
            var state = FooJson.Deserialize(row.ContentJson);
            return state;
            """
        },
        {
            "the wrapper method's own declaration is not mistaken for a call",
            """
            public static SchedulerState Deserialize(string content)
            {
                if (EfSchemaVersion.NotReadable(Module.Chain, row.SchemaVersion) || row.Revision <= 0)
                    throw new InvalidDataException("corrupt");
                return Project(content);
            }
            """
        },
        {
            "EnsureReadable first, receiverless helpers follow",
            """
            EfSchemaVersion.EnsureReadable(Module.Chain, row.SchemaVersion);
            var payload = DeserializePayload(row);
            return Deserialize<TelemetrySpan>(payload.Json);
            """
        },
        {
            "an expression-bodied check after a member that deserializes",
            """
            public sealed class Store
            {
                private static Role Map(RoleEntity entity) => new(entity.RoleId, Support.DeserializeSet(entity.PermissionsJson));

                private static bool Matches(RoleEntity entity) =>
                    EfSchemaVersion.Readable(Module.Chain, entity.SchemaVersion) && entity.Revision > 0;
            }
            """
        },
        {
            "expression-bodied and constrained helper declarations are not mistaken for calls",
            """
            private static T Deserialize<T>(string json) => Decode<T>(json);

            private static T DeserializeSet<T>(string json) where T : class
            {
                EfSchemaVersion.EnsureReadable(Module.Chain, row.SchemaVersion);
                return Decode<T>(json);
            }
            """
        }
    };

    public static TheoryData<string, string> OutOfOrderFixtures() => new()
    {
        {
            "single-line or-chain",
            """
            if (row.Revision <= 0 || EfSchemaVersion.NotReadable(Module.Chain, row.SchemaVersion))
                throw new InvalidDataException("corrupt");
            """
        },
        {
            "multi-line or-chain",
            """
            if (row.Revision <= 0 ||
                EfSchemaVersion.NotReadable(Module.Chain, row.SchemaVersion) ||
                row.ScopeKey != Encode(scope))
                throw new InvalidDataException("corrupt");
            """
        },
        {
            "multi-line and-chain assigned to a local",
            """
            var valid =
                (expectedId is null || row.Id == expectedId) &&
                EfSchemaVersion.Readable(Module.Chain, row.SchemaVersion) &&
                row.ScopeKey == Encode(scope);
            """
        },
        {
            "trailing clause of a long or-chain",
            """
            if (row.Id != CreateId(scope, id) ||
                row.ScopeKeyHash != Hash(scope) ||
                EfSchemaVersion.NotReadable(Module.Chain, row.SchemaVersion))
                throw new InvalidDataException("corrupt");
            """
        },
        {
            "nested inside a grouping parenthesis",
            """
            if (row.Revision <= 0 || (row.ScopeKey != Encode(scope) && EfSchemaVersion.NotReadable(Module.Chain, row.SchemaVersion)))
                throw new InvalidDataException("corrupt");
            """
        },
        {
            "returned expression body",
            """
            private static bool Matches(Row row) =>
                row.Revision > 0 && EfSchemaVersion.Readable(Module.Chain, row.SchemaVersion);
            """
        },
        {
            "preceded by a clause whose string literal looks like a statement boundary",
            """
            if (row.Id != Hash("(;") && EfSchemaVersion.Readable(Module.Chain, row.SchemaVersion))
                throw new InvalidDataException("corrupt");
            """
        }
    };

    public static TheoryData<string, string> InOrderFixtures() => new()
    {
        {
            "single-line or-chain",
            """
            if (EfSchemaVersion.NotReadable(Module.Chain, row.SchemaVersion) || row.Revision <= 0)
                throw new InvalidDataException("corrupt");
            """
        },
        {
            "multi-line or-chain",
            """
            if (EfSchemaVersion.NotReadable(Module.Chain, row.SchemaVersion) ||
                row.Revision <= 0 ||
                row.ScopeKey != Encode(scope))
                throw new InvalidDataException("corrupt");
            """
        },
        {
            "multi-line and-chain assigned to a local",
            """
            var valid =
                EfSchemaVersion.Readable(Module.Chain, row.SchemaVersion) &&
                (expectedId is null || row.Id == expectedId) &&
                row.ScopeKey == Encode(scope);
            """
        },
        {
            "and-chain opening a returned expression",
            """
            return EfSchemaVersion.Readable(Module.Chain, state.SchemaVersion) &&
                   state.Id == ProjectionId(scope, activation) &&
                   state.Revision > 0;
            """
        },
        {
            "the only earlier clause is commented out",
            """
            var valid =
                /* row.Revision > 0 && */ EfSchemaVersion.Readable(Module.Chain, row.SchemaVersion) &&
                row.Revision > 0;
            """
        },
        {
            "an earlier statement's string literal holds boolean operators",
            """
            var message = "a || b && c";
            if (EfSchemaVersion.NotReadable(Module.Chain, row.SchemaVersion))
                throw new InvalidDataException(message);
            """
        },
        {
            "passed as a method argument after another argument",
            """
            Assert.True(row.Revision > 0, Describe(row));
            Assert.True(EfSchemaVersion.Readable(Module.Chain, row.SchemaVersion));
            """
        },
        {
            "statement earlier in the method carries its own chain",
            """
            if (row.Revision <= 0 || row.ScopeKey != Encode(scope))
                throw new InvalidDataException("corrupt");
            if (EfSchemaVersion.NotReadable(Module.Chain, row.SchemaVersion))
                throw new InvalidDataException("corrupt");
            """
        }
    };

    private static readonly string[] CallTokens =
        ["EfSchemaVersion.Readable(", "EfSchemaVersion.NotReadable(", "EfSchemaVersion.EnsureReadable(", "EfSchemaVersion.EnsureCurrent(", "EfSchemaVersion.IsReadable("];

    /// <summary>The row a check reads the stamp of: the expression ahead of <c>.SchemaVersion</c> in its second argument.</summary>
    private static readonly Regex CheckedRow = new(@"\G[^,()]*,\s*(?<row>[A-Za-z_]\w*(?:\[\d+\])?(?:\.[A-Za-z_]\w*(?:\[\d+\])?)*?)\.SchemaVersion\s*\)", RegexOptions.Compiled);

    /// <summary>
    /// Returns the one-based line of every check whose row has a member other than its stamp read earlier in the same
    /// method (FR-011). Only the first check of a row in its method is judged: a second check of the same row follows the
    /// first, which already settled the version. A check inside a lambda over the row, <c>rows.Any(row =&gt; ...)</c>, is
    /// judged within that lambda.
    /// </summary>
    private static int[] FindRowReadBeforeVersionCheck(string source)
    {
        var masked = MaskLiteralsAndComments(source);
        var violations = new List<int>();
        foreach (var token in CallTokens)
        {
            for (var index = masked.IndexOf(token, StringComparison.Ordinal); index >= 0; index = masked.IndexOf(token, index + token.Length, StringComparison.Ordinal))
            {
                var checkedRow = CheckedRow.Match(masked, index + token.Length);
                if (!checkedRow.Success)
                    continue;

                var row = Regex.Escape(checkedRow.Groups["row"].Value);
                var start = FindEnclosingMethodStart(masked, index);
                if (Regex.Matches(masked[start..index], $@"(?<![\w.]){row}\s*=>").LastOrDefault() is { } lambda)
                    start += lambda.Index + lambda.Length;
                var before = masked[start..index];
                if (Regex.IsMatch(before, $@"EfSchemaVersion\.\w+\([^,()]*,\s*{row}\.SchemaVersion\s*\)"))
                    continue;
                if (Regex.IsMatch(before, $@"(?<![\w.]){row}\s*\.\s*(?!SchemaVersion\b)[A-Za-z_]"))
                    violations.Add(LineOf(source, index));
            }
        }

        return violations.Order().ToArray();
    }

    /// <summary>
    /// Matches a method whose name starts with <c>Deserialize</c> - <c>X.Deserialize(</c>, <c>X.DeserializeSet(</c>,
    /// a receiverless <c>Deserialize&lt;T&gt;(</c> or <c>DeserializePayload(</c>, or a nested-generic argument list
    /// such as <c>X.Deserialize&lt;Dictionary&lt;string, string&gt;&gt;(</c> - regardless of the receiver. A fixed
    /// token list (<c>RuntimeArtifactJson.Deserialize</c>, <c>PublishingEfJson.Deserialize</c>, ...) missed custom
    /// wrappers like <c>EfSchedulerStateJson.Deserialize</c> (#2108), and a member-access-only rule missed the
    /// receiverless helpers #2119's stores call; matching the name itself closes both gaps. The pattern also
    /// matches a helper's own declaration, which <see cref="FindDeserializeCalls"/> drops.
    /// </summary>
    private static readonly Regex DeserializeNamePattern = new(@"(?<![\w@])Deserialize\w*\s*(?:<[^()]*>)?\s*\(", RegexOptions.Compiled);

    private static int CountDeserializeCalls(string source) => FindDeserializeCalls(MaskLiteralsAndComments(source)).Length;

    /// <summary>
    /// Returns the index of every <see cref="DeserializeNamePattern"/> match that is a call. A match whose parameter
    /// list is followed by a body, <c>=&gt;</c> or a <c>where</c> constraint is the method's own declaration, e.g.
    /// <c>public static T Deserialize&lt;T&gt;(string value) =&gt; ...</c>, and is not one.
    /// </summary>
    private static int[] FindDeserializeCalls(string masked) =>
        DeserializeNamePattern.Matches(masked)
            .Where(match => !IsDeclaration(masked, match.Index + match.Length - 1))
            .Select(match => match.Index)
            .ToArray();

    private static bool IsDeclaration(string masked, int openParen)
    {
        var index = FindMatchingCloseParen(masked, openParen) + 1;
        if (index <= 0)
            return false;

        while (index < masked.Length && char.IsWhiteSpace(masked[index]))
            index++;

        var rest = masked.AsSpan(index);
        return rest.StartsWith("{") || rest.StartsWith("=>") ||
               (rest.StartsWith("where") && (rest.Length == 5 || !char.IsLetterOrDigit(rest[5])));
    }

    private static int FindMatchingCloseParen(string masked, int openParenIndex)
    {
        var depth = 0;
        for (var index = openParenIndex; index < masked.Length; index++)
        {
            if (masked[index] == '(')
            {
                depth++;
            }
            else if (masked[index] == ')')
            {
                depth--;
                if (depth == 0)
                    return index;
            }
        }

        return -1;
    }

    /// <summary>
    /// Returns the one-based line number of every <c>EfSchemaVersion.Readable</c> / <c>NotReadable</c> /
    /// <c>EnsureReadable</c> call that has a deserialize call (<see cref="FindDeserializeCalls"/>) earlier in its
    /// enclosing method - whether that deserialize call sits in an earlier statement, an earlier
    /// nested <c>try</c>/<c>if</c>/<c>using</c> block, or the same condition <see cref="HasEarlierClause"/>
    /// already covers.
    /// </summary>
    private static int[] FindDeserializeBeforeVersionCheck(string source)
    {
        var masked = MaskLiteralsAndComments(source);
        var deserializeCallIndices = FindDeserializeCalls(masked);
        var violations = new List<int>();
        foreach (var token in CallTokens)
        {
            var index = masked.IndexOf(token, StringComparison.Ordinal);
            while (index >= 0)
            {
                var methodStart = FindEnclosingMethodStart(masked, index);
                if (deserializeCallIndices.Any(deserializeIndex => deserializeIndex >= methodStart && deserializeIndex < index))
                    violations.Add(LineOf(source, index));

                index = masked.IndexOf(token, index + token.Length, StringComparison.Ordinal);
            }
        }

        return violations.Order().ToArray();
    }

    /// <summary>
    /// Walks outward from <paramref name="position"/> through nested control-flow blocks (<c>try</c>,
    /// <c>catch</c>, <c>finally</c>, <c>if</c>, <c>while</c>, <c>for</c>, <c>foreach</c>, <c>using</c>,
    /// <c>lock</c>, <c>switch</c>, <c>else</c>, <c>do</c>, <c>checked</c>, <c>unchecked</c>, <c>fixed</c>,
    /// <c>unsafe</c>) until it reaches the block that is <paramref name="position"/>'s enclosing method,
    /// local function, lambda, or accessor body, and returns the index just past that block's opening
    /// brace. An expression-bodied member has no brace of its own, so when the nearest one opens a type body
    /// the member starts where the previous member ended. Returns <c>0</c> when no enclosing brace exists at
    /// all, so a bare statement fixture with no wrapping method is treated as the whole method body.
    /// </summary>
    private static int FindEnclosingMethodStart(string masked, int position)
    {
        while (true)
        {
            var brace = FindNearestEnclosingBrace(masked, position);
            if (brace < 0)
                return 0;

            if (IsTypeBodyBrace(masked, brace))
                return FindMemberStart(masked, brace + 1, position);

            if (!IsControlBlockBrace(masked, brace))
                return brace + 1;

            position = brace;
        }
    }

    /// <summary>
    /// A type declaration's header, and not a generic constraint: <c>where T : class</c> names the keyword
    /// after a colon or comma, a declaration never does.
    /// </summary>
    private static readonly Regex TypeDeclarationHeader = new(
        @"(?<![:,]\s*)\b(?:class|struct|interface|enum)\b|\brecord\s+(?:class\s+|struct\s+)?[A-Z_]", RegexOptions.Compiled);

    private static bool IsTypeBodyBrace(string masked, int brace)
    {
        var headerStart = masked.LastIndexOfAny([';', '{', '}'], brace - 1) + 1;
        return TypeDeclarationHeader.IsMatch(masked[headerStart..brace]);
    }

    /// <summary>
    /// Returns where the type member holding <paramref name="position"/> starts: just past the last <c>;</c> or
    /// member-closing <c>}</c> at the type body's own depth.
    /// </summary>
    private static int FindMemberStart(string masked, int bodyStart, int position)
    {
        var start = bodyStart;
        var depth = 0;
        for (var index = bodyStart; index < position; index++)
        {
            var current = masked[index];
            if (current is '(' or '[' or '{')
            {
                depth++;
            }
            else if (current is ')' or ']' or '}')
            {
                depth--;
                if (depth == 0 && current == '}')
                    start = index + 1;
            }
            else if (current == ';' && depth == 0)
            {
                start = index + 1;
            }
        }

        return start;
    }

    /// <summary>Finds the index of the nearest unmatched '{' walking backward from <paramref name="position"/>.</summary>
    private static int FindNearestEnclosingBrace(string masked, int position)
    {
        var depth = 0;
        for (var index = position - 1; index >= 0; index--)
        {
            var current = masked[index];
            if (current == '}')
            {
                depth++;
            }
            else if (current == '{')
            {
                if (depth > 0)
                    depth--;
                else
                    return index;
            }
        }

        return -1;
    }

    private static readonly string[] ControlBlockKeywords =
        ["try", "finally", "else", "do", "unsafe", "checked", "unchecked", "fixed"];

    private static readonly string[] ControlConditionKeywords =
        ["if", "while", "for", "foreach", "using", "lock", "catch", "switch"];

    /// <summary>
    /// True when the '{' at <paramref name="brace"/> opens a control-flow block rather than a member,
    /// local-function, lambda, or accessor body - i.e. <see cref="FindEnclosingMethodStart"/> should keep
    /// stepping outward past it.
    /// </summary>
    private static bool IsControlBlockBrace(string masked, int brace)
    {
        var start = brace;
        while (start > 0 && char.IsWhiteSpace(masked[start - 1]))
            start--;

        if (start > 0 && masked[start - 1] == ')')
        {
            var openParen = FindMatchingOpenParen(masked, start - 1);
            return openParen >= 0 && PrecedingWordIsOneOf(masked, openParen, ControlConditionKeywords);
        }

        return PrecedingWordIsOneOf(masked, start, ControlBlockKeywords);
    }

    private static int FindMatchingOpenParen(string masked, int closeParenIndex)
    {
        var depth = 0;
        for (var index = closeParenIndex; index >= 0; index--)
        {
            var current = masked[index];
            if (current == ')')
                depth++;
            else if (current == '(')
            {
                depth--;
                if (depth == 0)
                    return index;
            }
        }

        return -1;
    }

    private static bool PrecedingWordIsOneOf(string masked, int end, string[] keywords)
    {
        var index = end;
        while (index > 0 && char.IsWhiteSpace(masked[index - 1]))
            index--;

        var wordEnd = index;
        var wordStart = index;
        while (wordStart > 0 && (char.IsLetterOrDigit(masked[wordStart - 1]) || masked[wordStart - 1] == '_'))
            wordStart--;

        return wordStart != wordEnd && keywords.Contains(masked[wordStart..wordEnd], StringComparer.Ordinal);
    }

    private static int CountCalls(string source)
    {
        var masked = MaskLiteralsAndComments(source);
        var count = 0;
        foreach (var token in CallTokens)
        {
            var index = masked.IndexOf(token, StringComparison.Ordinal);
            while (index >= 0)
            {
                count++;
                index = masked.IndexOf(token, index + token.Length, StringComparison.Ordinal);
            }
        }

        return count;
    }

    /// <summary>
    /// Returns the one-based line number of every <c>EfSchemaVersion.Readable</c> /
    /// <c>NotReadable</c> call that another boolean clause is evaluated ahead of.
    /// </summary>
    private static int[] FindLateSchemaChecks(string source)
    {
        var masked = MaskLiteralsAndComments(source);
        var violations = new List<int>();
        foreach (var token in CallTokens)
        {
            var index = masked.IndexOf(token, StringComparison.Ordinal);
            while (index >= 0)
            {
                if (HasEarlierClause(masked, index))
                    violations.Add(LineOf(source, index));

                index = masked.IndexOf(token, index + token.Length, StringComparison.Ordinal);
            }
        }

        return violations.Order().ToArray();
    }

    /// <summary>
    /// Walks left from the call, at the call's own parenthesis depth, looking for a <c>&amp;&amp;</c>
    /// or <c>||</c> that would be evaluated first. A grouping parenthesis is stepped out of and the
    /// walk continues, so a clause ahead of the group counts too. The walk stops at the start of the
    /// enclosing statement, argument, lambda body, or <c>if</c>/<c>while</c> condition.
    /// </summary>
    private static bool HasEarlierClause(string masked, int callStart)
    {
        var position = callStart;
        while (true)
        {
            var depth = 0;
            var index = position - 1;
            for (; index >= 0; index--)
            {
                var current = masked[index];
                if (current is ')' or ']')
                {
                    depth++;
                    continue;
                }

                if (current is '(' or '[')
                {
                    if (depth > 0)
                    {
                        depth--;
                        continue;
                    }

                    break;
                }

                if (depth > 0)
                    continue;

                if (index > 0 && (current is '&' or '|') && masked[index - 1] == current)
                    return true;

                if (current is ';' or '{' or '}' or ',' or ':' or '?')
                    return false;

                if (current == '=' && !IsCompoundOperatorTail(masked, index))
                    return false;

                if (char.IsLetter(current) || current == '_')
                {
                    var end = index + 1;
                    var start = index;
                    while (start >= 0 && (char.IsLetterOrDigit(masked[start]) || masked[start] == '_'))
                        start--;

                    var word = masked[(start + 1)..end];
                    if (word is "return" or "yield" or "throw" or "case" or "when" or "do" or "else")
                        return false;

                    index = start + 1;
                }
            }

            if (index < 0)
                return false;

            // Stopped on an unmatched '(' or '['. An argument list or indexer starts a fresh expression;
            // a grouping parenthesis does not, so step out of it and keep looking.
            if (masked[index] == '[' || !IsGroupingParenthesis(masked, index))
                return false;

            position = index;
        }
    }

    private static bool IsGroupingParenthesis(string masked, int index)
    {
        var previous = index - 1;
        while (previous >= 0 && char.IsWhiteSpace(masked[previous]))
            previous--;

        if (previous < 0)
            return false;

        // An invocation's argument list and a keyword's condition ("if (", "while (") both start a
        // fresh expression, so neither is stepped out of. Anything else ("&& (", "!(", "= (", "((")
        // is a grouping parenthesis whose own position still has clauses ahead of it.
        return !char.IsLetterOrDigit(masked[previous]) && masked[previous] is not ('_' or ')' or ']');
    }

    private static bool IsCompoundOperatorTail(string masked, int index)
    {
        if (index + 1 < masked.Length && masked[index + 1] == '=')
            return true;

        if (index == 0)
            return false;

        return masked[index - 1] is '=' or '!' or '<' or '>' or '+' or '-' or '*' or '/' or '%' or '&' or '|' or '^';
    }

    private static int LineOf(string source, int index)
    {
        var line = 1;
        for (var i = 0; i < index; i++)
        {
            if (source[i] == '\n')
                line++;
        }

        return line;
    }

    /// <summary>
    /// Replaces the contents of comments, strings and character literals with spaces, preserving
    /// length and line breaks, so a <c>||</c> inside a message never reads as a clause.
    /// </summary>
    private static string MaskLiteralsAndComments(string source)
    {
        var masked = new StringBuilder(source);
        var index = 0;
        while (index < source.Length)
        {
            var current = source[index];
            if (current == '/' && index + 1 < source.Length && source[index + 1] == '/')
            {
                while (index < source.Length && source[index] != '\n')
                    masked[index++] = ' ';

                continue;
            }

            if (current == '/' && index + 1 < source.Length && source[index + 1] == '*')
            {
                var end = source.IndexOf("*/", index + 2, StringComparison.Ordinal);
                end = end < 0 ? source.Length : end + 2;
                Blank(masked, source, index, end);
                index = end;
                continue;
            }

            if (current == '"' || current == '\'' ||
                (current is '@' or '$' && index + 1 < source.Length && source[index + 1] is '"' or '@' or '$'))
            {
                index = MaskLiteral(masked, source, index);
                continue;
            }

            index++;
        }

        return masked.ToString();
    }

    private static int MaskLiteral(StringBuilder masked, string source, int start)
    {
        var index = start;
        var verbatim = false;
        while (index < source.Length && source[index] is '@' or '$')
        {
            verbatim |= source[index] == '@';
            index++;
        }

        if (index >= source.Length || source[index] is not ('"' or '\''))
            return start + 1;

        var quote = source[index];
        var quotes = 0;
        while (index < source.Length && source[index] == quote)
        {
            quotes++;
            index++;
        }

        if (quote == '"' && quotes >= 3)
        {
            var fence = new string('"', quotes);
            var close = source.IndexOf(fence, index, StringComparison.Ordinal);
            var end = close < 0 ? source.Length : close + quotes;
            Blank(masked, source, start, end);
            return end;
        }

        // An empty literal ("" or '') already consumed both quotes.
        if (quotes >= 2)
        {
            Blank(masked, source, start, index);
            return index;
        }

        while (index < source.Length)
        {
            var current = source[index];
            if (!verbatim && current == '\\')
            {
                index += 2;
                continue;
            }

            if (verbatim && current == quote && index + 1 < source.Length && source[index + 1] == quote)
            {
                index += 2;
                continue;
            }

            index++;
            if (current == quote)
                break;

            if (!verbatim && current == '\n')
                break;
        }

        var stop = Math.Min(index, source.Length);
        Blank(masked, source, start, stop);
        return stop;
    }

    private static void Blank(StringBuilder masked, string source, int start, int end)
    {
        for (var i = start; i < end && i < source.Length; i++)
            masked[i] = source[i] == '\n' ? '\n' : ' ';
    }
}
