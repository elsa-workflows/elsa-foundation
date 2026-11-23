using Microsoft.EntityFrameworkCore;

namespace Kernel.EntityFramework;

// One database call. The text retains the exact tuple including boundaries. A negative scope is its
// physical row handle within one wire ID. An unequal tuple never deduplicates, even under a lock-key collision.
internal static class EventInsert
{
    internal static byte[]? Bytes(string? match) => match is null ? null : System.Text.Encoding.UTF8.GetBytes(match);

    private const string Columns = """
        "Id","InboxScope","EventMatch","Partition","Target","Kind","Payload","Start","Due","Seen","Attempts","Trial","KeepUntil","Done","ReplyTo"
        """;
    internal const string InstallPostgres = """
        CREATE SEQUENCE IF NOT EXISTS kernel_event_actor;
        CREATE OR REPLACE FUNCTION kernel_insert_event(wire text, match bytea, part integer, target text, kind text,
            payload text, spec text, keep bigint, reply text) RETURNS TABLE ("Added" integer, "Target" text) AS $$
        DECLARE slot bigint; dest text;
        BEGIN
            -- Only concurrent inserts for this wire need to serialize. A hash collision merely shares the lock.
            PERFORM pg_advisory_xact_lock(hashtextextended(wire, 0));
            SELECT m."Target" INTO dest FROM "Messages" m WHERE m."Id" = wire AND m."EventMatch" = match;
            IF FOUND THEN RETURN QUERY SELECT 0, dest; RETURN; END IF;
            dest := CASE WHEN spec IS NULL THEN target ELSE 'e' || part || '/' || nextval('kernel_event_actor') END;
            SELECT least(-1, coalesce(min("InboxScope"), -1)) - 1 INTO slot FROM "Messages" WHERE "Id" = wire;
            INSERT INTO "Messages"
        """ + " (" + Columns + ") " + """
            VALUES (wire,slot,match,part,dest,kind,payload,spec,0,0,0,0,keep,false,reply);
            RETURN QUERY SELECT 1, dest;
        END;
        $$ LANGUAGE plpgsql VOLATILE
        """;

    internal const string InstallMySql = """
        CREATE PROCEDURE kernel_insert_event(IN wire VARCHAR(255), IN match_data LONGBLOB, IN part INT, IN target_id TEXT,
            IN kind_name TEXT, IN body_data LONGTEXT, IN spec_data LONGTEXT, IN keep_time BIGINT, IN reply_to TEXT)
        BEGIN
            DECLARE slot BIGINT;
            DECLARE existing BIGINT;
            DECLARE wrote INT DEFAULT 0;
            DECLARE dest TEXT;
            DECLARE EXIT HANDLER FOR SQLEXCEPTION BEGIN ROLLBACK; RESIGNAL; END;
            -- Per-wire serialization is explicit. READ COMMITTED avoids InnoDB next-key conflicts
            -- between distinct registrations inserting intervals of the same composite inbox index.
            SET TRANSACTION ISOLATION LEVEL READ COMMITTED;
            START TRANSACTION;
            -- Weight the same fixed-width wire under the column collation so equivalent keys share a stripe.
            SELECT Bucket INTO existing FROM EventInsertLocks WHERE Bucket=MOD(CRC32(WEIGHT_STRING(wire AS CHAR(255))),64) FOR UPDATE;
            SELECT LEAST(-1,COALESCE(MIN(InboxScope),-1))-1 INTO slot FROM Messages
                WHERE Id=wire AND InboxScope <= -2;
            SELECT MAX(Target) INTO dest FROM Messages WHERE Id=wire AND EventMatch=match_data;
            IF dest IS NULL THEN
                SET dest=target_id;
                IF spec_data IS NOT NULL THEN
                    UPDATE EventActorCounter SET LastIssued=LastIssued+1 WHERE CounterId=0;
                    SELECT CONCAT('e',part,'/',LastIssued) INTO dest FROM EventActorCounter WHERE CounterId=0;
                END IF;
                INSERT INTO Messages
        """ + " (" + Columns + ") " + """
                VALUES (wire,slot,match_data,part,dest,kind_name,body_data,spec_data,0,0,0,0,keep_time,0,reply_to);
                SET wrote=1;
            END IF;
            COMMIT;
            SELECT wrote AS Added, dest AS Target;
        END
        """;

    private const string SqlServer = """
        SET XACT_ABORT ON;
        BEGIN TRANSACTION;
        DECLARE @slot bigint, @inserted int = 0, @lock int, @target nvarchar(max);
        -- CHECKSUM uses the wire column's database collation. Sharing a lock never makes two tuples equal.
        DECLARE @resource nvarchar(64) = CONCAT('kernel-event:',CHECKSUM(CAST({0} AS nvarchar(450)) COLLATE DATABASE_DEFAULT));
        EXEC @lock = sys.sp_getapplock @Resource=@resource,@LockMode='Exclusive',@LockOwner='Transaction';
        IF @lock < 0 THROW 51001, 'Failed to acquire event match lock.', 1;
        SELECT @slot = COALESCE(MIN("InboxScope"), -1) - 1 FROM "Messages"
            WHERE "Id"={0} AND "InboxScope" <= -2;
        SELECT @target="Target" FROM "Messages" WHERE "Id"={0} AND "EventMatch"={1};
        IF @target IS NULL
        BEGIN
            SET @target={3};
            IF {6} IS NOT NULL
            BEGIN
                DECLARE @serial bigint = NEXT VALUE FOR kernel_event_actor;
                SET @target=CONCAT('e',{2},'/',@serial);
            END;
            INSERT INTO "Messages"
        """ + " (" + Columns + ") " + """
            VALUES ({0},@slot,{1},{2},@target,{4},{5},{6},0,0,0,0,{7},0,{8});
            SET @inserted=1;
        END
        COMMIT;
        SELECT @inserted AS "Added", @target AS "Target";
        """;

    private const string Sqlite = "BEGIN IMMEDIATE; INSERT INTO \"Messages\" (" + Columns + ",\"Seq\") " + """
        SELECT {0},COALESCE((SELECT MIN("InboxScope") FROM "Messages" WHERE "Id"={0} AND "InboxScope" <= -2),-1)-1,
            {1},{2},CASE WHEN {6} IS NULL THEN {3} ELSE 'e' || {2} || '/' || (SELECT LastIssued+1 FROM EventActorCounter) END,
            {4},{5},{6},0,0,0,0,{7},0,{8},(SELECT COALESCE(MAX("Seq"),0)+1 FROM "Messages")
        WHERE NOT EXISTS (SELECT 1 FROM "Messages" WHERE "Id"={0} AND "EventMatch"={1});
        SELECT changes() AS "Added", "Target" FROM "Messages" WHERE "Id"={0} AND "EventMatch"={1};
        COMMIT;
        """;

    private sealed class Accepted { public int Added { get; set; } public string Target { get; set; } = ""; }

    internal static async ValueTask<EventAcceptance> Run(KernelDb db, Engine? engine, bool postgres, MessageEntity m, CancellationToken token)
    {
        object[] args = [m.Id, m.EventMatch!, m.Partition, m.Target, m.Kind, m.Payload!, m.Start!, m.KeepUntil!, m.ReplyTo!];

        var sql = engine switch
        {
            MySqlEngine => "CALL kernel_insert_event({0},{1},{2},{3},{4},{5},{6},{7},{8})",
            SqlServerEngine => SqlServer,
            _ when postgres => """SELECT * FROM kernel_insert_event({0},{1},{2},{3},{4},{5}::text,{6}::text,{7}::bigint,{8}::text)""",
            _ => Sqlite
        };
        var result = (await db.Database.SqlQueryRaw<Accepted>(sql, args).ToListAsync(token)).Single();
        return new EventAcceptance(result.Added == 1, new(result.Target));
    }
}
