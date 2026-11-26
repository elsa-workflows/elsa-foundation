using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;

namespace Kernel.EntityFramework;

public sealed partial class EfStore
{
    private static string SqliteCohortWaitingWork =>
        CohortWaitingWork.Replace("LIKE 'operator/%'", "GLOB 'operator/*'", StringComparison.Ordinal);

    private const string SqliteCohortDecision = """
        EXISTS (SELECT 1 FROM json_each({4}) decision
            WHERE (decision.value ->> 'Actor') = i."Id" AND (decision.value ->> 'Revision') = i."Revision")
        """;
    private static string SqliteCohortDecisions(string[] actors, long[] revisions) =>
        Json.Write(actors.Zip(revisions, (actor, revision) => new { Actor = actor, Revision = revision }).ToArray());

    private const string SqliteCohortSource = "\"Messages\" INDEXED BY \"IX_FoundationRootInbox_CohortGroups\"";

    private static FormattableString SqliteCohortCandidates(List<CohortFence> windows, int limit) => FormattableStringFactory.Create(
        $"""
        WITH fences AS MATERIALIZED (
            SELECT f.value ->> 'Partition' AS "Partition", f.value ->> 'Epoch' AS "Epoch",
                f.value ->> 'Through' AS "Through", f.value ->> 'ReadyThrough' AS "ReadyThrough",
                f.value ->> 'Seen' AS "Seen", f.value ->> 'Due' AS "Due", f.value ->> 'Seq' AS "Seq"
            FROM json_each({"{0}"}) f
        ), {CohortGroupBounds(SqliteCohortSource, SqliteCohortWaitingWork, SqliteCommitDialect.MillisecondClock)},
        picked AS MATERIALIZED (
            SELECT d."Partition", row.value ->> 'target' AS "Target", row.value ->> 'seq' AS "Seq"
            FROM by_due d CROSS JOIN json_each((
                SELECT json_group_array(json_object('target', page."Target", 'seq', page."Seq"))
                FROM ({CohortSequenceRange(SqliteCohortSource, SqliteCohortWaitingWork)} LIMIT ({"{1}"} + 1)) page
            )) row
        ), {CohortPage("{1}")}
        /* sqlite-cohort-waiting */
        """, Json.Write(windows), limit);
}
