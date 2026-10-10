using Xunit;
using static Elsa.Architecture.Tests.EfSchemaFamilyTestFixtures;

namespace Elsa.Architecture.Tests;

/// <summary>
/// Content reads (spec 180, FR-009): every read of a declared content column, in every EF persistence source that can
/// see its declaration, goes through the family's chain or deserializes nothing, so no content is parsed or compared
/// from a stamp other than the current one without the chain.
/// </summary>
public sealed class EfSchemaFamilyContentReadGuardTests
{
    /// <summary>
    /// The reads of a declared content column that deserialize no stored row's content past the chain, each keyed by file,
    /// member, the read and what consumes it, with the reason. An exemption that no longer matches a read fails the build,
    /// so the list cannot outlive the code it describes, and it excuses that one use, not every read in its member.
    /// </summary>
    private static readonly IReadOnlyDictionary<(string File, string Member, string Read, string Consumer), string> ContentReadExemptions =
        new Dictionary<(string File, string Member, string Read, string Consumer), string>
        {
            [("EfIdentityAuthorityRelationshipCoordinator.cs", "MutateMembershipAsync", "membership.RoleIdsJson", "Fingerprint(...)")] =
                "The caller's new tenant membership, serialized in the current format by EfTenantMembershipStore and not yet stamped, " +
                "fingerprinted for replay; no stored row.",
            [("EfIdentityAuthorityRelationshipCoordinator.cs", "MutateMembershipAsync", "membership.DirectPermissionsJson", "Fingerprint(...)")] =
                "The caller's new tenant membership, serialized in the current format by EfTenantMembershipStore and not yet stamped, " +
                "fingerprinted for replay; no stored row.",
            [("Elsa3ImportRecordCodec.cs", "ReadCollection", "record.ContentJson", "EnsureEnvelope(...)")] =
                "The content hash check, an integrity clause over the stored bytes evaluated at the row's stamp before the upcast (FR-008).",
            [("Elsa3ImportRecordCodec.cs", "ReadReceipt", "record.ContentJson", "EnsureEnvelope(...)")] =
                "The content hash check, an integrity clause over the stored bytes evaluated at the row's stamp before the upcast (FR-008).",
            [("EfClusterMembershipStore.cs", "Republish", "row.ReportJson", "Equals(...)")] =
                "Compared with the report being published only on a row at the current version, whose report the chain returns " +
                "unchanged (FR-021); a row at an older version is always rewritten and restamped (FR-014).",
            [("EfStructuredLogStore.cs", "CommitBatchAsync", "item.PayloadJson", "new PersistedOutcome(...)")] =
                "An EfPendingAppend: the payload this batch serialized a moment ago in the current format, not a stored row.",
            [("EfStructuredLogStore.cs", "CommitBatchAsync", "outcome.PayloadJson", "DeserializePayload(...)")] =
                "A PersistedOutcome this batch built from its pending appends, in the current format, not a stored row.",
            [("EfStructuredLogStore.cs", "CommitBatchAsync", "outcome.PayloadJson", "new EfPendingAppend(...)")] =
                "A PersistedOutcome read from an append operation's OutcomeJson after the chain upcast it, recomputed into the " +
                "idempotency fingerprint the stored side would carry under the current format, so a retry replayed across a " +
                "version bump is not rejected as a different payload (FR-009, spec 180's 2026-09-28 note on FR-014's restamp).",
            [("EfStructuredLogStore.cs", "ToEntry", "outcome.PayloadJson", "DeserializePayload(...)")] =
                "A PersistedOutcome built by this batch, or read from an append operation's OutcomeJson after the chain upcast it; " +
                "its payload is a field of that document, upcast with it, not the records' column.",
            [("StructuredLogAppendFingerprint.cs", "Compute", "item.PayloadJson", "Append(...)")] =
                "An EfPendingAppend of the batch being appended, serialized in the current format; the fingerprint of an incoming " +
                "batch, not a stored row."
        };

    /// <summary>
    /// Every read of a declared content column goes through its family's chain, from the row's stamp, so a row stamped at
    /// an older version is upcast before anything deserializes it (spec 180, FR-009). The columns come from the families'
    /// <c>[EfSchemaContent]</c> declarations, which EfSchemaContentDeclarationTests holds complete against every module's
    /// model; a family EF materializes directly meets the rule through the materialization interceptor, which reads its
    /// rows at the current version alone.
    /// </summary>
    /// <remarks>
    /// Tree-wide over every EF persistence source, where the Identity-only pin this replaces could not be, since nothing
    /// said which columns were content. A read that deserializes nothing - a copy into the same column of another row, model
    /// configuration - is no violation; a read of a freshly built row, or of stored bytes an integrity clause compares
    /// (FR-008), is exempted by name with its reason, because telling it from a stored row's content takes data flow this
    /// syntax-only guard does not have. A presence check is held to the rule since upcasters work on rows (#2144): a step
    /// may fill a column an older writer left null, so whether it holds anything is read from the upcast row.
    /// </remarks>
    [Fact]
    public void Every_declared_content_column_is_read_through_its_chain()
    {
        var (violations, unused, reads) = EfSchemaFamilyTestFixtures.Persistence.ContentReads(IsPersistenceSource, ProjectVisibility.Sees, ContentReadExemptions);

        AssertNone(violations, "A declared content column is read through its family's chain, from the row's stamp, so an older row is " +
            "upcast before it is parsed or compared with anything this build serializes (spec 180, FR-009):");
        AssertNone(unused, "Every exemption from the content read rule still matches a read:");
        // 195 when #2144 moved upcasting to rows: a store now reads a row's content columns once, in the one call that
        // upcasts them together, where it read each column at every use before, so the floor moved from 200.
        Assert.True(reads >= 190, $"Expected the EF stores to keep reading their content columns; found {reads} reads.");
        Assert.True(EfSchemaFamilyTestFixtures.Persistence.RowUpcasts().Count >= 45, $"Expected the EF stores to keep upcasting whole rows; found {EfSchemaFamilyTestFixtures.Persistence.RowUpcasts().Count} row upcasts.");
    }

    /// <summary>
    /// The read rule judges a source only where its project can see a declaration, so it passes vacuously if that stops
    /// resolving: a reader in another module that references the family's, such as the Dashboard's, sees it; an unrelated
    /// module does not, so a member there that shares a declared column's name is not judged as one.
    /// </summary>
    [Fact]
    public void Read_rule_sees_a_declaration_from_its_own_and_referencing_projects_only()
    {
        const string runtime = "src/essentials/Workflows/Runtime/Persistence/EntityFrameworkCore/AssemblyInfo.cs";
        const string secrets = "src/essentials/Secrets/Persistence/EntityFrameworkCore/AssemblyInfo.cs";

        Assert.True(ProjectVisibility.Sees("src/essentials/Workflows/Runtime/Persistence/EntityFrameworkCore/Stores/EfBookmarkStateStore.cs", runtime));
        Assert.True(ProjectVisibility.Sees("src/essentials/Workflows/Dashboard/Persistence/EntityFrameworkCore/Stores/EfWorkflowPortfolioDataSource.cs", runtime));
        Assert.False(ProjectVisibility.Sees("src/essentials/Workflows/Runtime/Persistence/EntityFrameworkCore/Stores/EfBookmarkStateStore.cs", secrets));
    }

    /// <summary>
    /// A read goes through the chain as a column value of a chain's <c>Upcast</c>, or through a helper that hands its
    /// parameters to one. A presence check is a read like any other since #2144: a step sees the whole row and may fill a
    /// column, so testing the stored column for null past the chain can skip data the upcast row holds.
    /// </summary>
    [Fact]
    public void Content_read_detector_flags_a_read_past_the_chain_and_accepts_one_that_deserializes_nothing()
    {
        var scan = Scan(
            """
            [assembly: EfSchemaFamily(Orders.SchemaFamily, "Sales", Orders.SchemaVersion)]
            [assembly: EfSchemaContent(Orders.SchemaFamily, typeof(Row), nameof(Row.ClaimIdsJson), nameof(Row.ContentJson))]
            [assembly: EfSchemaIntegrity(Orders.SchemaFamily, typeof(Row), nameof(Row.DigestJson), "Compared as stored bytes.")]

            public static class Orders
            {
                public const string SchemaVersion = "1";
                public const string SchemaFamily = "Orders";
                public static readonly EfSchemaChain Chain = EfSchemaChain.Of(typeof(Orders).Assembly, SchemaFamily);
            }

            public sealed class Store
            {
                Set Raw(Row row) => Parse(row.ClaimIdsJson);
                Set Through(Row row) => Parse(Orders.Chain.Upcast<Row>(row.SchemaVersion, (nameof(row.ClaimIdsJson), row.ClaimIdsJson), (nameof(row.ContentJson), row.ContentJson))[nameof(row.ClaimIdsJson)]);
                Set Helped(Row row) => Parse(Content(row.SchemaVersion, row.ClaimIdsJson, row.ContentJson)[nameof(Row.ClaimIdsJson)]);
                Set Incoming(Row caller) => Parse(caller.ClaimIdsJson);
                bool Present(Row row) => row.ContentJson is not null;
                void Copy(Row row, Row replacement) { row.ContentJson = replacement.ContentJson; row.SchemaVersion = replacement.SchemaVersion; }
                Row Fresh(Row source) => new() { ContentJson = source.ContentJson, SchemaVersion = Orders.SchemaVersion };
                void Configure(Builder b) => b.Property(x => x.ContentJson).IsRequired();
                bool Digest(Row row) => Hash(row.DigestJson) == row.Hash;
                static EfSchemaRowContent Content(string? stamp, string? claims, string? content) =>
                    Orders.Chain.Upcast<Row>(stamp, (nameof(Row.ClaimIdsJson), claims), (nameof(Row.ContentJson), content));
            }
            """);

        var (violations, unused, _) = scan.ContentReads(
            _ => true,
            (_, _) => true,
            new Dictionary<(string, string, string, string), string>
            {
                [("Fixture.cs", "Incoming", "caller.ClaimIdsJson", "Parse(...)")] = "new row",
                [("Fixture.cs", "Gone", "row.ClaimIdsJson", "Parse(...)")] = "stale"
            });

        Assert.Collection(
            violations,
            raw => Assert.StartsWith("Fixture.cs(14): reads 'row.ClaimIdsJson' in 'Raw' (Parse(...))", raw),
            present => Assert.StartsWith("Fixture.cs(18): reads 'row.ContentJson' in 'Present' (IsPatternExpression)", present));
        Assert.StartsWith("Fixture.cs, Gone, row.ClaimIdsJson, Parse(...)", Assert.Single(unused));
        Assert.Empty(scan.UpcastDeclarationViolations());
    }

    /// <summary>A family EF materializes directly meets the read rule through the interceptor, so its content reads are not judged.</summary>
    [Fact]
    public void Content_read_detector_leaves_a_family_EF_materializes_to_the_interceptor()
    {
        var scan = Scan(
            """
            [assembly: EfSchemaFamily(Designs.SchemaFamily, "Design", Designs.SchemaVersion)]
            [assembly: EfSchemaContent(Designs.SchemaFamily, typeof(Plan), nameof(Plan.PlanJson))]

            public static class Designs
            {
                public const string SchemaVersion = "1";
                public const string SchemaFamily = "Designs";
                public static readonly EfSchemaChain Chain = EfSchemaChain.Of(typeof(Designs).Assembly, SchemaFamily);
            }

            public abstract class DesignContext : DbContext, IEfSchemaVersionedContext
            {
                EfSchemaChain IEfSchemaVersionedContext.SchemaChain => Designs.Chain;
            }

            public sealed class Store
            {
                PlanValue Read(Plan row) => Parse(row.PlanJson);
                void Write(Plan row, PlanValue plan) => row.PlanJson = Serialize(plan);
            }
            """);

        Assert.Empty(scan.ContentReads(_ => true, (_, _) => true, new Dictionary<(string, string, string, string), string>()).Violations);
        Assert.Empty(scan.RestampViolations());
    }
}
