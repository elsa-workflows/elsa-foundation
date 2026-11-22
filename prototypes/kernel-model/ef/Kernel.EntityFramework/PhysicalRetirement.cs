using Microsoft.EntityFrameworkCore;

namespace Kernel.EntityFramework;

// Disposable prototype schemas only. Guards execute inside the existing DML transaction, without a header read.
internal static class PhysicalRetirement
{
    internal static Task InstallAsync(KernelDb db, Engine? engine, bool postgres)
    {
        var sql = engine switch
        {
            SqlServerEngine => """
                CREATE OR ALTER TRIGGER kernel_physical_retirement ON Instances AFTER UPDATE AS
                BEGIN
                    SET NOCOUNT ON;
                    IF EXISTS(SELECT 1 FROM inserted n JOIN deleted o ON n.Id=o.Id
                        WHERE o.Retired=1 OR n.Retired=1 AND (o.Status=0 OR n.Status<>o.Status))
                        THROW 51002, 'Physical retirement is irreversible and requires an ended run.', 1;
                END
                """,
            MySqlEngine => """
                CREATE TRIGGER IF NOT EXISTS kernel_physical_retirement BEFORE UPDATE ON Instances FOR EACH ROW
                BEGIN
                    IF OLD.Retired=1 OR NEW.Retired=1 AND (OLD.Status=0 OR NEW.Status<>OLD.Status) THEN
                        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='Physical retirement is irreversible and requires an ended run.';
                    END IF;
                END
                """,
            _ when postgres => """
                CREATE OR REPLACE FUNCTION kernel_physical_retirement_guard() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF OLD."Retired" OR NEW."Retired" AND (OLD."Status"=0 OR NEW."Status"<>OLD."Status") THEN
                        RAISE EXCEPTION 'Physical retirement is irreversible and requires an ended run.';
                    END IF;
                    RETURN NEW;
                END $$;
                CREATE OR REPLACE TRIGGER kernel_physical_retirement BEFORE UPDATE ON "Instances"
                    FOR EACH ROW EXECUTE FUNCTION kernel_physical_retirement_guard();
                """,
            _ => """
                CREATE TRIGGER IF NOT EXISTS kernel_physical_retirement BEFORE UPDATE ON Instances
                WHEN OLD.Retired=1 OR NEW.Retired=1 AND (OLD.Status=0 OR NEW.Status<>OLD.Status)
                BEGIN SELECT RAISE(ABORT,'Physical retirement is irreversible and requires an ended run.'); END;
                """
        };
        return db.Database.ExecuteSqlRawAsync(sql);
    }
}
