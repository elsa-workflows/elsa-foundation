"""Failure controls for owned resources and exact catalog receipts; no database server is started."""
import contextlib
import importlib.util
import io
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path
from unittest.mock import Mock, patch

SPEC = importlib.util.spec_from_file_location("design_store_journey", Path(__file__).with_name("design_store_workbench_journey.py"))
journey = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(journey)


class InitializationFailure(RuntimeError):
    pass


class ControllerTests(unittest.TestCase):
    def check_database_cleanup(self, stage):
        owned = {"alive": False, "name": None}

        def command(args, **_):
            if args[:2] == ["docker", "run"]:
                owned.update(alive=True, name=args[args.index("--name") + 1])
            elif args[:3] == ["docker", "rm", "-f"]:
                self.assertEqual(owned["name"], args[-1])
                owned["alive"] = False
            else:
                self.fail("Unexpected container command.")
            return subprocess.CompletedProcess([], 0, stdout="owned", stderr="")

        port = Mock(return_value="127.0.0.1:12345\n")
        wait = Mock(return_value=True)
        if stage == "port":
            port.side_effect = InitializationFailure("port mapping failed")
        elif stage == "ready":
            wait.side_effect = InitializationFailure("readiness failed")
        sql = Mock(side_effect=InitializationFailure("database setup failed"))
        if stage == "setup-status":
            sql = Mock(return_value=subprocess.CompletedProcess(["private-command-placeholder"], 1))
        with patch.object(journey.subprocess, "run", command), patch.object(journey.subprocess, "check_output", port), \
                patch.object(journey, "wait_for", wait), patch.object(journey.Database, "_sqlcmd", sql):
            with self.assertRaises(RuntimeError) as failure:
                journey.Database("SqlServer" if stage.startswith("setup") else "PostgreSql")
            self.assertNotIn("private-command-placeholder", str(failure.exception))
        self.assertFalse(owned["alive"], "Constructor failure retained its owned container.")

    def test_database_port_failure_removes_owned_container(self):
        self.check_database_cleanup("port")

    def test_database_readiness_failure_removes_owned_container(self):
        self.check_database_cleanup("ready")

    def test_database_setup_failure_removes_owned_container(self):
        self.check_database_cleanup("setup")

    def test_unsuccessful_database_setup_keeps_command_arguments_out_of_error(self):
        self.check_database_cleanup("setup-status")

    def check_host_cleanup(self, stage):
        original_popen = subprocess.Popen
        processes = []
        host = object.__new__(journey.Host)

        def launch(*_, **kwargs):
            if stage == "spawn":
                raise InitializationFailure("spawn failed")
            process = original_popen([sys.executable, "-c", "import time; time.sleep(60)"],
                                     stdout=kwargs["stdout"], stderr=kwargs["stderr"])
            processes.append(process)
            return process

        waits = ["http://127.0.0.1:12345", True]
        if stage == "listen":
            waits = [InitializationFailure("listen failed")]
        elif stage == "ready":
            waits[1] = InitializationFailure("readiness failed")
        api = Mock()
        api.request.side_effect = InitializationFailure("login failed")
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            try:
                with patch.object(journey.subprocess, "Popen", launch), patch.object(journey, "wait_for", side_effect=waits), \
                        patch.object(journey, "Api", return_value=api):
                    with self.assertRaises(InitializationFailure):
                        journey.Host.__init__(host, root / "host.dll", root, root / "host.log", 0)
                self.assertTrue(host.log.closed, "Constructor failure retained its log handle.")
                self.assertTrue(all(process.poll() is not None for process in processes), "Constructor failure retained its owned child process.")
            finally:
                # Also reap the intentionally failing pre-correction control's real child.
                for process in processes:
                    if process.poll() is None:
                        process.terminate()
                    process.wait(timeout=10)
                if getattr(host, "log", None) is not None:
                    host.log.close()

    def test_host_spawn_failure_closes_log(self):
        self.check_host_cleanup("spawn")

    def test_host_listen_failure_reaps_process_and_closes_log(self):
        self.check_host_cleanup("listen")

    def test_host_readiness_failure_reaps_process_and_closes_log(self):
        self.check_host_cleanup("ready")

    def test_host_login_failure_reaps_process_and_closes_log(self):
        self.check_host_cleanup("login")

    def catalog_result(self, entries):
        case = {"definition": "people", "twin": "People", "workflowStatus": "Completed", "incidents": 0,
                "executedNodes": ["etl", "after-done"], "delivered": ["Ada", "Bram", "Cleo"],
                "catalogBefore": entries, "catalogAfter": entries, "reopenedLastPublishedVersion": 1,
                "versionsAfter": [1], "designTables": ["elsa_etl_definition_drafts", "elsa_etl_published_artifacts"],
                "secretInOutput": False}
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            assembly = root / "host.dll"
            assembly.write_bytes(b"controller-control")
            args = ["journey", "--workbench-assembly", str(assembly), "--output", str(root / "evidence"), "--provider", "PostgreSql"]
            with patch.object(sys, "argv", args), patch.object(journey, "run_engine", return_value=case), \
                    patch.object(journey.subprocess, "check_output", side_effect=["f" * 40, ""]), contextlib.redirect_stdout(io.StringIO()):
                return journey.main()

    def test_catalog_rejects_duplicate_definitions(self):
        self.assertEqual(1, self.catalog_result(["People", "people", "people"]))

    def test_catalog_rejects_unexpected_definitions(self):
        self.assertEqual(1, self.catalog_result(["People", "people", "unexpected"]))

    def test_catalog_accepts_exact_case_twins(self):
        self.assertEqual(0, self.catalog_result(["People", "people"]))

    def test_content_root_failure_removes_owned_database(self):
        database = Mock()
        with patch.object(journey, "Database", return_value=database), \
                patch.object(journey.tempfile, "mkdtemp", side_effect=InitializationFailure("content root failed")):
            with self.assertRaises(InitializationFailure):
                journey.run_engine("PostgreSql", Path("unused.dll"), Path("unused"), 0)
        database.stop.assert_called_once_with()


if __name__ == "__main__":
    unittest.main()
