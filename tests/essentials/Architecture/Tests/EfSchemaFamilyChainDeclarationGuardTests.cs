using Xunit;
using static Elsa.Architecture.Tests.EfSchemaFamilyTestFixtures;

namespace Elsa.Architecture.Tests;

/// <summary>
/// Chain soundness and declarations (spec 180): a family's <c>[EfSchemaFamily]</c> declaration (FR-001) is the one
/// source of what a build reads and writes for it, over every production source under <c>src/</c>.
/// <list type="bullet">
/// <item>every family a check names, and every <c>SchemaFamily</c> constant, is declared exactly once, and every check
/// names its family through the family's one chain handle, never a string (FR-002);</item>
/// <item>every family's module class holds that handle, resolved from its own assembly and its own family;</item>
/// <item>every declared chain is sound: no gap, duplicate, branch or cycle, and it ends at the current version (FR-005);</item>
/// <item>every upcaster belongs to exactly one declared chain (FR-003);</item>
/// <item>a family EF materializes directly, through <c>IEfSchemaVersionedContext</c>, declares no upcasters, since its
/// content is deserialized before any upcaster could run;</item>
/// <item>every write that stamps a constant stamps the version its family's declaration names current (FR-013).</item>
/// </list>
/// The full-detector theory (<see cref="Detector_flags_every_kind_of_violation"/>) and the two acceptance fixtures below
/// it exercise every concern's rules together through <c>SchemaFamilyScan.AllViolations()</c>; the concern-specific
/// theories live beside the rule each one judges, in <c>EfSchemaFamilyFixtureProofGuardTests</c>,
/// <c>EfSchemaFamilyContentIntegrityGuardTests</c>, <c>EfSchemaFamilyContentReadGuardTests</c> and
/// <c>EfSchemaFamilyRestampGuardTests</c>.
/// </summary>
/// <remarks>
/// It reads syntax only, and resolves a family or version when it is a string literal or a <c>Type.Constant</c> whose
/// <c>const string</c> initializer is a literal. The version rule this guard had before the chain landed, which held a
/// check's version to its declaration, is gone: a check states no version any more, it reads the family's chain.
/// </remarks>
public sealed class EfSchemaFamilyChainDeclarationGuardTests
{
    [Fact]
    public void Every_family_the_stores_check_is_declared_once_and_checked_through_its_chain() =>
        AssertNone(Production.DeclarationViolations(), "Every schema family a store checks, and every SchemaFamily constant, needs exactly one " +
            "[EfSchemaFamily] declaration, and every check names its family through the family's chain handle, or the host's " +
            "readability report omits it or its reads use another family's versions (spec 180, FR-001 and FR-002):");

    [Fact]
    public void Every_family_has_one_chain_handle_resolved_from_its_own_declaration() =>
        AssertNone(Production.HandleViolations(), "Every class with a SchemaFamily constant holds 'public static readonly EfSchemaChain Chain = " +
            "EfSchemaChain.Of(typeof(<that class>).Assembly, SchemaFamily)', so its stores read through the chain the host " +
            "reports (spec 180, FR-010):");

    [Fact]
    public void Every_declared_chain_is_sound() =>
        AssertNone(Production.ChainViolations(), "A family's upcaster chain must list upcasters oldest first, each starting where the one " +
            "before it ends, the last ending at the current version, with no version twice; a gap is never bridged at read " +
            "time (spec 180, FR-005):");

    [Fact]
    public void Every_upcaster_belongs_to_exactly_one_declared_chain() =>
        AssertNone(Production.UpcasterViolations(), "An upcaster transforms one version of one family (spec 180, FR-003), so exactly one " +
            "[EfSchemaFamily] declaration lists it; one listed by none never runs:");

    [Fact]
    public void A_family_EF_materializes_directly_declares_no_upcasters() =>
        AssertNone(Production.MaterializedFamilyViolations(), "EF deserializes an IEfSchemaVersionedContext family's content in its value " +
            "converters before any upcaster could run, so the materialization interceptor reads its current version alone " +
            "and the family must declare no chain until its content moves to store code:");

    [Fact]
    public void Every_constant_stamp_is_its_familys_declared_current_version() =>
        AssertNone(Production.StampViolations(), "A write stamps the current version its family's declaration names (spec 180, FR-013), " +
            "never a literal or another constant:");

    /// <summary>
    /// The rules pass vacuously if the scan stops finding what they judge, so pin floors rather than counts: the
    /// twenty-eight declared families - the twenty-six every EF content table stamps since #2119, the shared finalization
    /// family (#2134) and cluster membership's (#2098) - the checks and stamps of their stores, and a chain handle for each
    /// family.
    /// </summary>
    [Fact]
    public void Guard_scans_the_checks_declarations_handles_and_stamps_it_claims_to_scan()
    {
        Assert.True(Production.Checks.Count >= 80, $"Expected the EF stores to keep checking families through EfSchemaVersion; found {Production.Checks.Count} checks.");
        Assert.True(Production.Declarations.Count >= 28, $"Expected at least twenty-eight [EfSchemaFamily] declarations; found {Production.Declarations.Count}.");
        Assert.True(Production.NamedFamilies.Count >= 28, $"Expected at least twenty-eight families named by checks and constants; found {Production.NamedFamilies.Count}.");
        Assert.True(Production.Handles.Count >= 28, $"Expected a chain handle for each of at least twenty-eight families; found {Production.Handles.Count}.");
        Assert.True(Production.Stamps.Count >= 60, $"Expected the EF stores to keep stamping their families' constants; found {Production.Stamps.Count} stamps.");
        Assert.True(Production.MaterializedFamilies.Count >= 2, $"Expected the two design contexts EF materializes directly; found {Production.MaterializedFamilies.Count}.");
        Assert.True(EfSchemaFamilyTestFixtures.Persistence.ContentRewrites.Count >= 60, $"Expected the EF stores' in-place content rewrites; found {EfSchemaFamilyTestFixtures.Persistence.ContentRewrites.Count}.");
        Assert.True(EfSchemaFamilyTestFixtures.Persistence.BulkContentWrites().Count >= 1, $"Expected the bulk update of a workflow draft's state; found {EfSchemaFamilyTestFixtures.Persistence.BulkContentWrites().Count}.");
        Assert.True(Production.DeclaredColumns().Count >= 100, $"Expected the families' content and integrity declarations; found {Production.DeclaredColumns().Count} columns.");
        Assert.Superset(
            new HashSet<string>
            {
                "ClaimIdsJson", "LoginIdsJson", "RoleLinkIdsJson", "TokenIdsJson", "TenantMembershipIdsJson", "UserLinkIdsJson", "RoleIdsJson",
                "ContentJson", "PayloadJson", "MetadataJson", "OutcomeJson", "PendingPostCommitWorkIdsJson", "ConsumedSchedulerWorkItemIdsJson",
                "Content", "Payload", "ValueJson", "ReportJson"
            },
            EfSchemaFamilyTestFixtures.Persistence.ContentColumns.ToHashSet());
        Assert.Superset(new HashSet<string> { "WorkflowsDesign", "ActivitiesDesign" }, EfSchemaFamilyTestFixtures.Persistence.MaterializedFamilyNames.ToHashSet());
        Assert.Contains(("Upgrade", 1, 0), EfSchemaFamilyTestFixtures.Persistence.StampingMethods);
    }

    [Theory]
    [MemberData(nameof(ViolatingFixtures))]
    public void Detector_flags_every_kind_of_violation(string name, string source, string expected)
    {
        var violations = Scan(source).AllViolations();

        Assert.True(violations.Any(violation => violation.Contains(expected, StringComparison.Ordinal)),
            $"The detector missed '{name}'. It reported: {string.Join("; ", violations)}");
    }

    [Fact]
    public void Detector_accepts_a_family_declared_checked_stamped_and_chained_as_the_rules_require() =>
        Assert.Empty(Scan(SoundFixture).AllViolations());

    /// <summary>
    /// The shared, no-module form (spec 180, FR-001 extension for a family owned by no single EF module) is a two-argument
    /// declaration; it must still be read, not silently dropped for having one fewer argument than an owned family's.
    /// </summary>
    [Fact]
    public void Detector_accepts_a_family_declared_shared_with_no_module()
    {
        var scan = Scan(
            """
            [assembly: EfSchemaFamily(SchemaFinalization.SchemaFamily, SchemaFinalization.SchemaVersion)]

            public static class SchemaFinalization
            {
                public const string SchemaVersion = "1";
                public const string SchemaFamily = "SchemaFinalization";
                public static readonly EfSchemaChain Chain = EfSchemaChain.Of(typeof(SchemaFinalization).Assembly, SchemaFamily);
            }

            public sealed class Store
            {
                bool Valid(Row row) => EfSchemaVersion.Readable(SchemaFinalization.Chain, row.SchemaVersion);
            }
            """);

        Assert.Empty(scan.AllViolations());
        Assert.Equal(["SchemaFinalization"], scan.NamedFamilies);
    }

    private const string SoundFixture =
        """
        [assembly: EfSchemaFamily(Orders.SchemaFamily, "Sales", Orders.SchemaVersion, Upcasters = new[] { typeof(OrdersOneToTwo), typeof(OrdersTwoToThree) })]
        [assembly: Elsa.Persistence.EntityFramework.EfSchemaFamilyAttribute(Invoices.SchemaFamily, "Sales", Invoices.SchemaVersion)]
        [assembly: EfSchemaContent(Orders.SchemaFamily, typeof(Row), nameof(Row.ContentJson), nameof(Row.ClaimIdsJson))]
        [assembly: EfSchemaContent(Orders.SchemaFamily, typeof(TenantSetting), nameof(TenantSetting.SettingsJson))]
        [assembly: Elsa.Persistence.EntityFramework.EfSchemaIntegrityAttribute(Invoices.SchemaFamily, typeof(Invoice), nameof(Invoice.DigestJson), "Compared as stored bytes.")]

        public static class Orders
        {
            public const string SchemaVersion = "3";
            public const string SchemaFamily = "Orders";
            public static readonly EfSchemaChain Chain = EfSchemaChain.Of(typeof(Orders).Assembly, SchemaFamily);
        }

        public static class Invoices
        {
            public const string SchemaVersion = "7";
            public const string SchemaFamily = "Invoices";
            public static readonly EfSchemaChain Chain = EfSchemaChain.Of(typeof(Invoices).Assembly, SchemaFamily);
        }

        [EfSchemaUpcaster("1", "2")]
        public sealed class OrdersOneToTwo : IEfSchemaUpcaster { public EfSchemaRowContent Upcast(EfSchemaRowContent row) => row; }

        [EfSchemaUpcaster("2", Orders.SchemaVersion)]
        public sealed class OrdersTwoToThree : IEfSchemaUpcaster { public EfSchemaRowContent Upcast(EfSchemaRowContent row) => row; }

        public sealed class Store
        {
            void Read(Row row, IContext context)
            {
                if (EfSchemaVersion.NotReadable(Orders.Chain, row.SchemaVersion) || row.Id is null)
                    throw new InvalidDataException("corrupt");
                EfSchemaVersion.EnsureReadable(
                    Invoices.Chain,
                    Entry(row).Property<string>("SchemaVersion").CurrentValue);
                EfSchemaVersion.EnsureCurrent(context.SchemaChain, row.SchemaVersion);
            }

            Row Write(Order order) => new() { Id = order.Id, SchemaVersion = Orders.SchemaVersion };

            void Replace(Row row, Row replacement)
            {
                row.ContentJson = replacement.ContentJson;
                row.ClaimIdsJson = replacement.ClaimIdsJson;
                row.SchemaVersion = replacement.SchemaVersion;
            }

            void Upgrade(Row row)
            {
                row.ContentJson = row.ContentJson;
                row.ClaimIdsJson = row.ClaimIdsJson;
                row.SchemaVersion = Invoices.Chain.CurrentVersion;
            }

            Cursor Page() => new() { SchemaVersion = 1 };

            Task Bulk(IQueryable<Row> rows, string content) =>
                rows.ExecuteUpdateAsync(updates => updates
                    .SetProperty(x => x.ContentJson, content)
                    .SetProperty(x => EF.Property<string>(x, EfSchemaVersionMaterializationInterceptor.PropertyName), Orders.Chain.CurrentVersion));

            void Rewrite(Row row, Order order)
            {
                row.ContentJson = Serialize(order);
                row.ClaimIdsJson = Serialize(order);
                row.SchemaVersion = Orders.SchemaVersion;
            }

            Row Fresh(Order order)
            {
                var row = new Row { Id = order.Id, SchemaVersion = Orders.SchemaVersion };
                row.ContentJson = Serialize(order);
                return row;
            }

            Set Claims(Row row) => Parse(Content(row)[nameof(row.ClaimIdsJson)]);

            static EfSchemaRowContent Content(Row row) =>
                Orders.Chain.Upcast<Row>(row.SchemaVersion, (nameof(row.ContentJson), row.ContentJson), (nameof(row.ClaimIdsJson), row.ClaimIdsJson));

            Settings Settings(Setting setting) =>
                Parse(Orders.Chain.Upcast<TenantSetting>(setting.SchemaVersion, (nameof(setting.SettingsJson), setting.SettingsJson))[nameof(setting.SettingsJson)]);

            static void Upgrade(Row row)
            {
                var content = Content(row);
                row.ContentJson = content[nameof(row.ContentJson)];
                row.ClaimIdsJson = content[nameof(row.ClaimIdsJson)];
                row.SchemaVersion = Orders.Chain.CurrentVersion;
            }

            static void SetClaims(Row row, Set ids)
            {
                Upgrade(row);
                row.ClaimIdsJson = Serialize(ids);
            }

            void AddClaim(Row row, Set ids)
            {
                SetClaims(row, ids);
                row.ClaimIdsJson = Serialize(ids);
            }
        }

        public abstract class Setting;

        public sealed class TenantSetting : Setting;
        """;

    public static TheoryData<string, string, string> ViolatingFixtures() => new()
    {
        {
            "a family checked through a handle whose class declares no family",
            """
            public static class Orders
            {
                public const string SchemaFamily = "Orders";
                public static readonly EfSchemaChain Chain = EfSchemaChain.Of(typeof(Orders).Assembly, SchemaFamily);
            }

            public sealed class Store
            {
                bool Valid(Row row) => EfSchemaVersion.Readable(Orders.Chain, row.SchemaVersion) && row.Id is not null;
            }
            """,
            "'Orders' is checked here but no [EfSchemaFamily] declares it"
        },
        {
            "a SchemaFamily constant with no declaration",
            """
            public static class Orders
            {
                public const string SchemaFamily = "Orders";
                public static readonly EfSchemaChain Chain = EfSchemaChain.Of(typeof(Orders).Assembly, SchemaFamily);
            }
            """,
            "'Orders' is a SchemaFamily constant but no [EfSchemaFamily] declares it"
        },
        {
            "a check that names its family by something other than a chain handle",
            """
            public sealed class Store
            {
                bool Valid(Row row, EfSchemaChain chain) => EfSchemaVersion.Readable(chain, row.SchemaVersion);
            }
            """,
            "names its family through 'chain'"
        },
        {
            "one family declared twice",
            """
            [assembly: EfSchemaFamily("Orders", "Sales", "1")]
            [assembly: EfSchemaFamily("Orders", "Billing", "1")]
            """,
            "'Orders' is declared 2 times"
        },
        {
            "a declaration whose family is not a literal or a constant",
            """
            [assembly: EfSchemaFamily(nameof(Orders), "Sales", "1")]
            """,
            "declares a family this guard cannot resolve"
        },
        {
            "a family class without a chain handle",
            """
            [assembly: EfSchemaFamily(Orders.SchemaFamily, "Sales", Orders.SchemaVersion)]

            public static class Orders
            {
                public const string SchemaVersion = "1";
                public const string SchemaFamily = "Orders";
            }
            """,
            "'Orders' holds no chain handle"
        },
        {
            "a chain handle resolved from another class's assembly",
            """
            [assembly: EfSchemaFamily(Orders.SchemaFamily, "Sales", Orders.SchemaVersion)]

            public static class Orders
            {
                public const string SchemaVersion = "1";
                public const string SchemaFamily = "Orders";
                public static readonly EfSchemaChain Chain = EfSchemaChain.Of(typeof(Invoices).Assembly, SchemaFamily);
            }
            """,
            "resolves its chain from 'typeof(Invoices).Assembly'"
        },
        {
            "a chain handle resolving another family",
            """
            [assembly: EfSchemaFamily(Orders.SchemaFamily, "Sales", Orders.SchemaVersion)]
            [assembly: EfSchemaFamily("Invoices", "Sales", "1")]

            public static class Orders
            {
                public const string SchemaVersion = "1";
                public const string SchemaFamily = "Orders";
                public static readonly EfSchemaChain Chain = EfSchemaChain.Of(typeof(Orders).Assembly, "Invoices");
            }
            """,
            "resolves family 'Invoices' but its class's SchemaFamily is 'Orders'"
        },
        {
            "a chain with a gap",
            """
            [assembly: EfSchemaFamily("Orders", "Sales", "3", Upcasters = new[] { typeof(ZeroToOne), typeof(TwoToThree) })]

            [EfSchemaUpcaster("0", "1")] public sealed class ZeroToOne : IEfSchemaUpcaster { }
            [EfSchemaUpcaster("2", "3")] public sealed class TwoToThree : IEfSchemaUpcaster { }
            """,
            "'Orders' has a gap: 'ZeroToOne' produces '1' but 'TwoToThree' reads '2'"
        },
        {
            "a chain that does not end at the current version",
            """
            [assembly: EfSchemaFamily("Orders", "Sales", "4", Upcasters = [typeof(OneToTwo)])]

            [EfSchemaUpcaster("1", "2")] public sealed class OneToTwo : IEfSchemaUpcaster { }
            """,
            "'Orders' ends its chain at '2', not at its current version '4'"
        },
        {
            "a chain with a cycle",
            """
            [assembly: EfSchemaFamily("Orders", "Sales", "2", Upcasters = new[] { typeof(TwoToOne), typeof(OneToTwo) })]

            [EfSchemaUpcaster("2", "1")] public sealed class TwoToOne : IEfSchemaUpcaster { }
            [EfSchemaUpcaster("1", "2")] public sealed class OneToTwo : IEfSchemaUpcaster { }
            """,
            "'Orders' reaches version '2' more than once"
        },
        {
            "a chain listing a type that is no upcaster",
            """
            [assembly: EfSchemaFamily("Orders", "Sales", "2", Upcasters = new[] { typeof(Helper) })]

            public sealed class Helper { }
            """,
            "'Orders' lists 'Helper', which carries no [EfSchemaUpcaster(from, to)]"
        },
        {
            "an upcaster no chain lists",
            """
            [EfSchemaUpcaster("1", "2")] public sealed class Orphan : IEfSchemaUpcaster { }
            """,
            "'Orphan' is listed by 0 [EfSchemaFamily] declarations"
        },
        {
            "an upcaster two chains list",
            """
            [assembly: EfSchemaFamily("Orders", "Sales", "2", Upcasters = new[] { typeof(OneToTwo) })]
            [assembly: EfSchemaFamily("Invoices", "Sales", "2", Upcasters = new[] { typeof(OneToTwo) })]

            [EfSchemaUpcaster("1", "2")] public sealed class OneToTwo : IEfSchemaUpcaster { }
            """,
            "'OneToTwo' is listed by 2 [EfSchemaFamily] declarations"
        },
        {
            "a family EF materializes directly that declares a chain",
            """
            [assembly: EfSchemaFamily(Designs.SchemaFamily, "Design", Designs.SchemaVersion, Upcasters = new[] { typeof(OneToTwo) })]

            public static class Designs
            {
                public const string SchemaVersion = "2";
                public const string SchemaFamily = "Designs";
                public static readonly EfSchemaChain Chain = EfSchemaChain.Of(typeof(Designs).Assembly, SchemaFamily);
            }

            [EfSchemaUpcaster("1", "2")] public sealed class OneToTwo : IEfSchemaUpcaster { }

            public abstract class DesignContext : DbContext, IEfSchemaVersionedContext
            {
                EfSchemaChain IEfSchemaVersionedContext.SchemaChain => Designs.Chain;
            }
            """,
            "'Designs' is materialized directly by 'DesignContext' but declares 1 upcaster"
        },
        {
            "a literal stamp",
            """
            public sealed class Store
            {
                Row Write() => new() { SchemaVersion = "1.0.0" };
            }
            """,
            "stamps the literal '1.0.0'"
        },
        {
            "a content rewrite that leaves the row's old stamp",
            """
            [assembly: EfSchemaContent("Orders", typeof(Row), nameof(Row.ContentJson))]

            public sealed class Store
            {
                void Touch(Row row, Order order)
                {
                    row.ContentJson = Serialize(order);
                    row.Revision++;
                }
            }
            """,
            "rewrites 'row.ContentJson' but never stamps 'row'"
        },
        {
            "a declared registry rewritten without its stamp",
            """
            [assembly: EfSchemaContent(Users.SchemaFamily, typeof(Row), nameof(Row.ClaimIdsJson))]

            public sealed class Store
            {
                Set Read(Row row) => Parse(Users.Chain.Upcast<Row>(row.SchemaVersion, (nameof(row.ClaimIdsJson), row.ClaimIdsJson))[nameof(row.ClaimIdsJson)]);

                void Edit(Row row, Set ids)
                {
                    row.ClaimIdsJson = Serialize(ids);
                    row.Revision++;
                }
            }
            """,
            "rewrites 'row.ClaimIdsJson' but never stamps 'row'"
        },
        {
            "a bulk update that sets a content column but not the stamp",
            """
            [assembly: EfSchemaContent("Designs", typeof(Draft), nameof(Draft.StateSource))]

            public sealed class Store
            {
                Task Update(IQueryable<Draft> rows, string state) =>
                    rows.ExecuteUpdateAsync(updates => updates.SetProperty(x => x.StateSource, state).SetProperty(x => x.LastModifiedAt, now));
            }
            """,
            "sets 'StateSource' in a bulk update that never sets the row's SchemaVersion"
        },
        {
            "a column a store upcasts through a family's chain that the family does not declare content",
            """
            [assembly: EfSchemaFamily(Orders.SchemaFamily, "Sales", Orders.SchemaVersion)]
            [assembly: EfSchemaContent(Orders.SchemaFamily, typeof(Row), nameof(Row.ContentJson))]

            public static class Orders
            {
                public const string SchemaVersion = "1";
                public const string SchemaFamily = "Orders";
                public static readonly EfSchemaChain Chain = EfSchemaChain.Of(typeof(Orders).Assembly, SchemaFamily);
            }

            public sealed class Store
            {
                Order Read(Row row) => Parse(Orders.Chain.Upcast<Row>(row.SchemaVersion, (nameof(row.PayloadJson), row.PayloadJson))[nameof(row.PayloadJson)]);
            }
            """,
            "upcasts 'PayloadJson' through the 'Orders' chain, but 'Orders' does not declare it content"
        },
        {
            "a read that upcasts some of a row's declared content columns and leaves the others at the row's stamp",
            """
            [assembly: EfSchemaFamily(Orders.SchemaFamily, "Sales", Orders.SchemaVersion)]
            [assembly: EfSchemaContent(Orders.SchemaFamily, typeof(Row), nameof(Row.ContentJson), nameof(Row.ClaimIdsJson))]

            public static class Orders
            {
                public const string SchemaVersion = "1";
                public const string SchemaFamily = "Orders";
                public static readonly EfSchemaChain Chain = EfSchemaChain.Of(typeof(Orders).Assembly, SchemaFamily);
            }

            public sealed class Store
            {
                Order Read(Row row) => Parse(Orders.Chain.Upcast<Row>(row.SchemaVersion, (nameof(row.ContentJson), row.ContentJson))[nameof(row.ContentJson)]);
            }
            """,
            "passes 'ContentJson' of 'Row', but its family declares 'ClaimIdsJson', 'ContentJson'"
        },
        {
            "a column value read from another column than the one it is passed as",
            """
            [assembly: EfSchemaFamily(Orders.SchemaFamily, "Sales", Orders.SchemaVersion)]
            [assembly: EfSchemaContent(Orders.SchemaFamily, typeof(Row), nameof(Row.ContentJson), nameof(Row.ClaimIdsJson))]

            public static class Orders
            {
                public const string SchemaVersion = "1";
                public const string SchemaFamily = "Orders";
                public static readonly EfSchemaChain Chain = EfSchemaChain.Of(typeof(Orders).Assembly, SchemaFamily);
            }

            public sealed class Store
            {
                EfSchemaRowContent Read(Row row) =>
                    Orders.Chain.Upcast<Row>(row.SchemaVersion, (nameof(row.ContentJson), row.ClaimIdsJson), (nameof(row.ClaimIdsJson), row.ContentJson));
            }
            """,
            "passes 'row.ClaimIdsJson' as column 'ContentJson'"
        },
        {
            "a row upcast as another table's that declares the same columns",
            """
            [assembly: EfSchemaFamily(Orders.SchemaFamily, "Sales", Orders.SchemaVersion)]
            [assembly: EfSchemaContent(Orders.SchemaFamily, typeof(Order), nameof(Order.ContentJson))]
            [assembly: EfSchemaContent(Orders.SchemaFamily, typeof(Invoice), nameof(Invoice.ContentJson))]

            public static class Orders
            {
                public const string SchemaVersion = "1";
                public const string SchemaFamily = "Orders";
                public static readonly EfSchemaChain Chain = EfSchemaChain.Of(typeof(Orders).Assembly, SchemaFamily);
            }

            public sealed class Store
            {
                Value Read(Invoice row) => Parse(Orders.Chain.Upcast<Order>(row.SchemaVersion, (nameof(row.ContentJson), row.ContentJson))[nameof(row.ContentJson)]);
            }
            """,
            "upcasts 'row', a 'Invoice', as a row of 'Order'"
        },
        {
            "a row of a table its family declares no content columns for",
            """
            [assembly: EfSchemaFamily(Orders.SchemaFamily, "Sales", Orders.SchemaVersion)]
            [assembly: EfSchemaContent(Orders.SchemaFamily, typeof(Row), nameof(Row.ContentJson))]

            public static class Orders
            {
                public const string SchemaVersion = "1";
                public const string SchemaFamily = "Orders";
                public static readonly EfSchemaChain Chain = EfSchemaChain.Of(typeof(Orders).Assembly, SchemaFamily);
            }

            public sealed class Store
            {
                Value Read(Ghost row) => Parse(Orders.Chain.Upcast<Ghost>(row.SchemaVersion, (nameof(row.ContentJson), row.ContentJson))[nameof(row.ContentJson)]);
            }
            """,
            "upcasts a row of 'Ghost', which 'Orders' declares no content columns for"
        },
        {
            "a content column tested for presence past the chain",
            """
            [assembly: EfSchemaFamily("Orders", "Sales", "1")]
            [assembly: EfSchemaContent("Orders", typeof(Row), nameof(Row.PayloadJson))]

            public sealed class Store
            {
                bool Present(Row row) => !string.IsNullOrWhiteSpace(row.PayloadJson);
            }
            """,
            "reads 'row.PayloadJson' in 'Present' (IsNullOrWhiteSpace(...)) without its family's chain"
        },
        {
            "a content column read and deserialized past the chain",
            """
            [assembly: EfSchemaFamily("Orders", "Sales", "1")]
            [assembly: EfSchemaContent("Orders", typeof(Row), nameof(Row.PayloadJson))]

            public sealed class Store
            {
                Order Read(Row row) => JsonSerializer.Deserialize<Order>(row.PayloadJson);
            }
            """,
            "reads 'row.PayloadJson' in 'Read' (Deserialize(...)) without its family's chain"
        },
        {
            "a content column read past the chain through a conditional access",
            """
            [assembly: EfSchemaFamily("Orders", "Sales", "1")]
            [assembly: EfSchemaContent("Orders", typeof(Row), nameof(Row.PayloadJson))]

            public sealed class Store
            {
                Order? Read(Row? row) => Parse(row?.PayloadJson);
            }
            """,
            "reads 'row?.PayloadJson' in 'Read' (Parse(...)) without its family's chain"
        },
        {
            "a content declaration of a family nothing declares",
            """
            [assembly: EfSchemaContent("Orders", typeof(Row), nameof(Row.ContentJson))]
            """,
            "declares a content column of 'Orders', which no [EfSchemaFamily] declares"
        },
        {
            "a content declaration whose column this guard cannot resolve",
            """
            [assembly: EfSchemaFamily("Orders", "Sales", "1")]
            [assembly: EfSchemaContent("Orders", typeof(Row), Columns.Content)]
            """,
            "declares a content column this guard cannot resolve"
        },
        {
            "an integrity column with no reason",
            """
            [assembly: EfSchemaFamily("Orders", "Sales", "1")]
            [assembly: EfSchemaIntegrity("Orders", typeof(Row), nameof(Row.DigestJson), "")]
            """,
            "declares an integrity column 'Row.DigestJson' with no reason"
        },
        {
            "a stamp of a constant no declaration names current",
            """
            [assembly: EfSchemaFamily(Orders.SchemaFamily, "Sales", Orders.SchemaVersion)]

            public static class Orders
            {
                public const string SchemaVersion = "2";
                public const string PreviousVersion = "1";
                public const string SchemaFamily = "Orders";
                public static readonly EfSchemaChain Chain = EfSchemaChain.Of(typeof(Orders).Assembly, SchemaFamily);
            }

            public sealed class Store
            {
                void Write(Row row) => row.SchemaVersion = Orders.PreviousVersion;
            }
            """,
            "stamps 'Orders.PreviousVersion', which no [EfSchemaFamily] declaration names as its current version"
        }
    };
}
