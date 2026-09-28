using Xunit;
using static Elsa.Architecture.Tests.EfSchemaFamilyTestFixtures;

namespace Elsa.Architecture.Tests;

/// <summary>
/// Restamps (spec 180, FR-014, amended by the owner's 2026-09-28 note on #2093): a write that changes a family's
/// content stamps the row again in the same member, itself or through a helper that does, so its stamp always
/// describes its content. The amendment extends the same rule to a row with no content, such as Identity's child
/// rows: a write that assigns every mapped non-key column of a stamped row, or replaces the row, must set the stamp;
/// a write that only bumps a concurrency revision need not.
/// </summary>
public sealed class EfSchemaFamilyRestampGuardTests
{
    [Fact]
    public void Every_in_place_content_rewrite_restamps_the_row() =>
        AssertNone(Persistence.RestampViolations(), "A write that changes a declared content column writes it in the current format, so it " +
            "stamps the row with the current version in the same member; left at an older stamp, the next read would upcast " +
            "content that is already current (spec 180, FR-014):");

    /// <summary>
    /// A row with no declared content, such as Identity's claims, tokens, role links and external logins, carries only
    /// its own columns, so the content-column restamp rule above never judges it. The owner's amendment covers it
    /// separately: a helper that copies two or more of a stamped type's columns from one row into another is a full
    /// rewrite, and must set the target's stamp; a helper that only bumps a Revision is left alone (spec 180, FR-014,
    /// 2026-09-28 note).
    /// </summary>
    [Fact]
    public void Every_full_row_rewrite_of_a_stamped_row_sets_the_stamp() =>
        AssertNone(Persistence.FullRowRewriteViolations(), "A method that copies every mapped non-key column of a stamped row from another row of " +
            "the same type is a full rewrite, so it stamps the target row to the write version; a write that only bumps a " +
            "concurrency revision need not (spec 180, FR-014, 2026-09-28 note):");

    [Fact]
    public void Full_row_rewrite_detector_flags_a_copy_that_never_restamps_and_leaves_a_revision_bump_and_an_unstamped_type_alone()
    {
        var scan = Scan(
            """
            public sealed class Row
            {
                public string TenantId { get; set; } = "";
                public string OwnerId { get; set; } = "";
                public long Revision { get; set; }
                public string SchemaVersion { get; set; } = "";
            }

            public sealed class Plain
            {
                public string TenantId { get; set; } = "";
                public string OwnerId { get; set; } = "";
            }

            public sealed class Store
            {
                private static void Overwrite(Row target, Row source)
                {
                    target.TenantId = source.TenantId;
                    target.OwnerId = source.OwnerId;
                }

                private static void BumpRevision(Row target, Row source)
                {
                    target.Revision = source.Revision + 1;
                }

                private static void Restamp(Row target, Row source)
                {
                    target.TenantId = source.TenantId;
                    target.OwnerId = source.OwnerId;
                    target.SchemaVersion = source.SchemaVersion;
                }

                private static void CopyPlain(Plain target, Plain source)
                {
                    target.TenantId = source.TenantId;
                    target.OwnerId = source.OwnerId;
                }
            }
            """);

        var violation = Assert.Single(scan.FullRowRewriteViolations());
        Assert.Contains("'Overwrite'", violation);
        Assert.Contains("never stamps 'target'", violation);
    }
}
