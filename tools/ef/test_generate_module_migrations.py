#!/usr/bin/env python3
"""Exercise the destructive generator boundary in an isolated tree with recording tools."""
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest


class RetainedMigrationGeneratorTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory(prefix="elsa-retained-migrations-")
        self.addCleanup(self.directory.cleanup)
        self.root = Path(self.directory.name)
        scripts = self.root / "tools/ef"
        scripts.mkdir(parents=True)
        shutil.copyfile(Path(__file__).with_name("generate-module-migrations.sh"), scripts / "generate-module-migrations.sh")
        (scripts / "strip-provider-calls.sh").write_text("#!/bin/bash\nexit 0\n")
        self.originals = {}
        for module in ("Runtime", "WorkflowsDesign"):
            assembly = {"Runtime": "Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore",
                        "WorkflowsDesign": "Elsa.Workflows.Design.Persistence.EntityFrameworkCore"}[module]
            base = self.root / "Essentials/src" / module / "Persistence/EntityFrameworkCore"
            base.mkdir(parents=True)
            (base / (assembly + ".csproj")).write_text("<Project />")
            for provider in ("Sqlite", "SqlServer", "PostgreSql", "MySql"):
                migrations = base / "Migrations" / module / provider
                migrations.mkdir(parents=True)
                for name, content in ((module + provider + "DbContextModelSnapshot.cs", "snapshot"),
                                      ("20260928040714_Initial.cs", "original migration"),
                                      ("20261030020434_WorkflowTemplates.cs", "already applied addition")):
                    path = migrations / name
                    path.write_text(content)
                    self.originals[path] = content
        shim = self.root / "shims"
        shim.mkdir()
        dotnet = shim / "dotnet"
        dotnet.write_text('''#!/bin/bash
set -eu
printf '%s\\n' "$*" >> "$ELSA_GENERATOR_TEST_LOG"
if [[ "${1:-}" == "run" ]]; then
  for module in Runtime WorkflowsDesign; do
    case "$module" in
      Runtime) assembly=Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;;
      *) assembly=Elsa.Workflows.Design.Persistence.EntityFrameworkCore;;
    esac
    for provider in Sqlite SqlServer PostgreSql MySql; do
      printf '%s%sDbContext|%s|%s|Elsa.Test\\n' "$module" "$provider" "$provider" "$assembly"
    done
  done
fi
''')
        dotnet.chmod(0o755)
        self.log = self.root / "calls.txt"
        self.environment = {**os.environ, "PATH": str(shim) + os.pathsep + os.environ["PATH"],
                            "ELSA_GENERATOR_TEST_LOG": str(self.log)}

    def run_generator(self, *arguments):
        return subprocess.run(["bash", str(self.root / "tools/ef/generate-module-migrations.sh"), *arguments],
                              env=self.environment, capture_output=True, text=True, timeout=15)

    def assert_preserved(self):
        for path, content in self.originals.items():
            self.assertTrue(path.is_file(), str(path))
            self.assertEqual(content, path.read_text())

    def test_default_regeneration_does_not_delete_runtime_or_design_history(self):
        result = self.run_generator()
        self.assertEqual(0, result.returncode, result.stdout + result.stderr)
        self.assert_preserved()
        self.assertNotIn("ef migrations add", self.log.read_text())

    def test_design_incremental_targets_only_design_and_preserves_all_history(self):
        self.check_incremental("design", "WorkflowsDesign")

    def test_runtime_incremental_still_targets_only_runtime_and_preserves_all_history(self):
        self.check_incremental("runtime", "Runtime")

    def check_incremental(self, mode, context):
        result = self.run_generator("--" + mode + "-incremental", "NextAddition")
        self.assertEqual(0, result.returncode, result.stdout + result.stderr)
        self.assert_preserved()
        additions = [line for line in self.log.read_text().splitlines() if line.startswith("ef migrations add")]
        self.assertEqual(4, len(additions))
        self.assertTrue(all("migrations add NextAddition --context " + context in line for line in additions))
        self.assertTrue(all("--startup-project tools/ef/Elsa.EntityFrameworkCore.Tooling/" in line for line in additions))

    def test_invalid_incremental_names_refuse_before_invoking_any_tool(self):
        for mode in ("runtime", "design"):
            for name in ("Initial", "../escape", ""):
                result = self.run_generator("--" + mode + "-incremental", name)
                self.assertEqual(2, result.returncode)
                self.assertFalse(self.log.exists())
                self.assert_preserved()


if __name__ == "__main__":
    unittest.main()
