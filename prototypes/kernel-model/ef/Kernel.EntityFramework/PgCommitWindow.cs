namespace Kernel.EntityFramework;

/// <summary>Bounded PostgreSQL experiment. Only a guarded partition UPDATE specifies the two window columns.</summary>
internal static class PgCommitWindow
{
    internal const string Expired = "PJR01";
    internal const string Install = """
        CREATE OR REPLACE FUNCTION check_kernel_commit_window() RETURNS trigger LANGUAGE plpgsql AS $$
        DECLARE checked_at timestamptz := clock_timestamp();
        BEGIN
            IF checked_at < transaction_timestamp() OR
               extract(epoch FROM checked_at) * 1000 < NEW."WindowStart" OR
               extract(epoch FROM checked_at) * 1000 >= NEW."WindowEnd" THEN
                RAISE EXCEPTION USING ERRCODE = 'PJR01', MESSAGE = 'expired kernel decision window';
            END IF;
            RETURN NEW;
        END $$;
        DROP TRIGGER IF EXISTS kernel_commit_window ON "Partitions";
        CREATE CONSTRAINT TRIGGER kernel_commit_window
            AFTER UPDATE OF "WindowStart", "WindowEnd" ON "Partitions"
            DEFERRABLE INITIALLY DEFERRED FOR EACH ROW
            EXECUTE FUNCTION check_kernel_commit_window();
        """;
}
