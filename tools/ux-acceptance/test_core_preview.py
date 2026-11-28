"""Synthetic controls for the Core Preview acceptance runner, never product or screenshot evidence."""
import importlib.util
import json
import shutil
import sqlite3
from pathlib import Path
from contextlib import closing
import tempfile
import unittest
from unittest.mock import MagicMock, patch
import urllib.request

spec = importlib.util.spec_from_file_location("core_preview_acceptance", Path(__file__).with_name("core_preview.py"))
acceptance = importlib.util.module_from_spec(spec)
spec.loader.exec_module(acceptance)


class CorePreviewVerdictTests(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory(prefix="synthetic-core-preview-gate-")
        self.addCleanup(temporary.cleanup)
        self.root = Path(temporary.name)
        self.journey = "core preview stack is ready"
        attachments = []
        for name in ["trace", "reference.png", "reference.aria.yml", "core-preview-smoke.json"]:
            path = self.root / name
            path.write_text("SYNTHETIC gate control; not a screenshot or product witness")
            attachments.append({"name": name, "path": str(path)})
        self.test = {"projectName": acceptance.PROJECT, "expectedStatus": "passed", "status": "expected",
                     "results": [{"status": "passed", "attachments": attachments}]}

    def report(self, *tests):
        return {"suites": [{"specs": [{"title": self.journey, "tests": list(tests)}]}]}

    def test_a_complete_single_desktop_execution_passes(self):
        self.assertEqual(1, acceptance.verdict(self.report(self.test), self.root, [self.journey]))

    def test_another_project_or_a_second_execution_is_refused(self):
        other = dict(self.test, projectName="desktop-dark")
        with self.assertRaises(ValueError):
            acceptance.verdict(self.report(other), self.root, [self.journey])
        with self.assertRaises(ValueError):
            acceptance.verdict(self.report(self.test, self.test), self.root, [self.journey])

    def test_a_missing_receipt_is_refused(self):
        stripped = json.loads(json.dumps(self.test))
        stripped["results"][0]["attachments"] = [item for item in stripped["results"][0]["attachments"] if item["name"] != "core-preview-smoke.json"]
        with self.assertRaises(ValueError):
            acceptance.verdict(self.report(stripped), self.root, [self.journey])

    def test_an_artifact_outside_the_evidence_directory_is_refused(self):
        outside = tempfile.NamedTemporaryFile(delete=False)
        self.addCleanup(Path(outside.name).unlink)
        outside.write(b"outside"); outside.close()
        moved = json.loads(json.dumps(self.test))
        moved["results"][0]["attachments"][-1]["path"] = outside.name
        with self.assertRaises(ValueError):
            acceptance.verdict(self.report(moved), self.root, [self.journey])

    def test_unknown_journeys_are_refused(self):
        with self.assertRaises(ValueError):
            acceptance.inventory(["not a core preview journey"])


class CorePreviewCompositionTests(unittest.TestCase):
    def test_postgresql_actor_store_uses_the_stock_host_and_a_separate_platform_store(self):
        connection = "Host=127.0.0.1;Port=1;Database=x"
        runtime = Path("owned") / "runtime.sqlite"
        env = acceptance.kernel_environment(runtime, connection)
        feature = acceptance.KERNEL_HOSTING_FEATURE
        self.assertEqual({f"{feature}__Provider": "PostgreSQL", f"{feature}__Tenant": acceptance.TENANT,
                          f"{feature}__PostgresConnectionString": connection,
                          "ConnectionStrings__Elsa": f"Data Source={runtime}"}, env)

    def test_sqlite_uses_only_its_owned_database(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "kernel.sqlite"
            runtime = Path(directory) / "runtime.sqlite"
            env = acceptance.kernel_environment(runtime, sqlite_path=path)
            feature = acceptance.KERNEL_HOSTING_FEATURE
            self.assertEqual({f"{feature}__Provider": "SQLite", f"{feature}__Tenant": acceptance.TENANT,
                              f"{feature}__SqlitePath": str(path),
                              "ConnectionStrings__Elsa": f"Data Source={runtime}"}, env)
            with self.assertRaises(sqlite3.OperationalError):
                acceptance.sqlite_markers(path, acceptance.sqlite_shipped_markers())
            self.assertFalse(path.exists())

    @staticmethod
    def marker_source(definitions):
        return """
            internal const int StoreSchemaVersion = 3;
            internal const int JournalCodecVersion = 1;
            internal static StoreFormat RootShipped => Current with { MessageProtocol = 34 };
            private const int Sqlite = 2;
            _ when db.RequiredFormat == StoreFormat.RootShipped ||
                db.RequiredFormat == StoreFormat.RootOptionalAuthoredStandaloneEventQualification =>
                sqlite ? SqliteShippedRevision : PostgreSqlShippedRevision,
        """ + definitions

    def test_sqlite_markers_accept_a_decimal_or_a_same_file_alias(self):
        for declarations in (
            "private const int SqliteShippedRevision = 17;",
            "private const int SqliteShippedRevision = SqlitePlaybackDescendantsRevision;\n"
            "private const int SqlitePlaybackDescendantsRevision = 17;",
            "private const int SqliteShippedRevision = SqlitePlaybackDescendantsRevision;\n"
            "private const int SqlitePlaybackDescendantsRevision = SqliteDefinitionRevision;\n"
            "private const int SqliteDefinitionRevision = 17;",
        ):
            with self.subTest(declarations=declarations), patch.object(acceptance, "STORE_VERSIONS") as source:
                source.read_text.return_value = self.marker_source(declarations)
                self.assertEqual([(1, 3, 34, 1), (2, 2, 17, 0)], acceptance.sqlite_shipped_markers())

    def test_sqlite_markers_refuse_an_unresolved_ambiguous_or_cyclic_alias(self):
        for declarations in (
            "",
            "private const int Revision = 17;\nprivate const int Revision = 18;",
            "private const int Revision = SqliteShippedRevision;",
            "private const int Revision = 16 + 1;",
        ):
            with self.subTest(declarations=declarations), patch.object(acceptance, "STORE_VERSIONS") as source:
                source.read_text.return_value = self.marker_source("private const int SqliteShippedRevision = Revision;\n" + declarations)
                with self.assertRaisesRegex(ValueError, "Cannot derive SQLite shipped markers"):
                    acceptance.sqlite_shipped_markers()

    def test_a_different_sqlite_profile_is_refused_even_with_both_markers(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "kernel.sqlite"
            expected = acceptance.sqlite_shipped_markers()
            with closing(sqlite3.connect(path)) as store:
                store.execute('PRAGMA journal_mode = WAL')
                store.execute('CREATE TABLE "KernelStoreVersions" ("Id", "StoreSchema", "MessageProtocol", "JournalCodec")')
                store.executemany('INSERT INTO "KernelStoreVersions" VALUES (?, ?, ?, ?)', expected)
                store.commit()
                self.assertEqual((0, 0, 0), store.execute('PRAGMA wal_checkpoint(TRUNCATE)').fetchone())
            checkpointed = path.with_name('checkpointed.sqlite')
            shutil.copyfile(path, checkpointed)
            self.assertEqual(expected, acceptance.sqlite_markers(checkpointed, expected))
            self.assertFalse(checkpointed.with_name('checkpointed.sqlite-shm').exists())
            with closing(sqlite3.connect(path)) as store, store:
                store.execute('UPDATE "KernelStoreVersions" SET "MessageProtocol" = "MessageProtocol" + 1 WHERE "Id" = 1')
            with self.assertRaisesRegex(ValueError, "Expected SQLite shipped markers"):
                acceptance.sqlite_markers(path, expected)

    def test_the_password_stays_out_of_argv_and_an_interrupted_run_still_stops_its_container(self):
        calls = []
        def check_output(args, **kwargs):
            calls.append((args, kwargs))
            raise KeyboardInterrupt  # interrupted before docker run printed an id
        def run(args, **kwargs):
            calls.append((args, kwargs))
            return MagicMock(returncode=0)
        with tempfile.TemporaryDirectory() as directory, \
                patch.object(acceptance.subprocess, "check_output", side_effect=check_output), \
                patch.object(acceptance.subprocess, "run", side_effect=run):
            with self.assertRaises(KeyboardInterrupt):
                with acceptance.postgres(Path(directory) / "postgres.log"):
                    self.fail("No container was started")
        (started, options), (stopped, _) = calls
        name = started[started.index("--name") + 1]
        self.assertIn("POSTGRES_PASSWORD", started)
        self.assertFalse([argument for argument in started if argument.startswith("POSTGRES_PASSWORD=")], "The password is in argv")
        self.assertEqual(48, len(options["env"]["POSTGRES_PASSWORD"]))
        self.assertIn(acceptance.LABEL, started)
        self.assertIn(f"{acceptance.OWNER_LABEL}={acceptance.OWNER}", started)
        self.assertEqual(["docker", "stop", "--time", "3", name], stopped)

    def test_only_containers_whose_run_has_ended_are_swept(self):
        host = acceptance.socket.gethostname()
        live = f"{host}:{acceptance.os.getpid()}:1"
        gone = f"{host}:999999:1"
        listing = f"exited1 exited {live}\nlive1 running {live}\ngone1 running {gone}\nother1 running elsewhere:1:1\n"
        with patch.object(acceptance.subprocess, "check_output", return_value=listing) as listed, \
                patch.object(acceptance.subprocess, "run") as removed, \
                patch.object(acceptance.os, "kill", side_effect=lambda pid, _: (_ for _ in ()).throw(ProcessLookupError()) if pid == 999999 else None):
            swept = acceptance.sweep_stale_containers()
        self.assertEqual({"removed": ["exited1", "gone1"], "keptRunning": ["live1", "other1"]}, swept)
        self.assertIn(f"label={acceptance.LABEL}", listed.call_args.args[0])
        self.assertEqual(["docker", "rm", "-f", "exited1", "gone1"], removed.call_args.args[0])

    def test_each_container_is_labelled_with_its_owner(self):
        host, pid, started = acceptance.OWNER.split(":")
        self.assertEqual((acceptance.socket.gethostname(), str(acceptance.os.getpid())), (host, pid))
        self.assertTrue(started.isdigit())

    def test_the_stub_answers_with_what_it_got_and_records_it(self):
        with acceptance.Stub() as stub:
            sent = urllib.request.Request(f"{stub.url}/orders/7", data=b'{"id":7}', method="POST",
                                          headers={"Content-Type": "application/json"})
            with urllib.request.urlopen(sent, timeout=5) as response:
                answer = json.loads(response.read())
        self.assertEqual({"ok": True, "method": "POST", "path": "/orders/7", "body": '{"id":7}', "received": 1}, answer)
        self.assertEqual([{"method": "POST", "path": "/orders/7", "body": '{"id":7}'}], stub.recorded())
        self.assertTrue(stub.url.startswith("http://127.0.0.1:"))


if __name__ == "__main__":
    unittest.main()
