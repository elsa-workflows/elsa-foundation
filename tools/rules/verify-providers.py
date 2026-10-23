#!/usr/bin/env python3
"""Run the Rules real-engine invariant suite and retain an explicit qualification receipt."""
import argparse
import json
import hashlib
import re
from xml.sax.saxutils import escape
import os
from pathlib import Path
import subprocess
import sys
import uuid
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[2]
PROJECT = "Extensions/Rules/tests/Persistence/EntityFrameworkCore/ProviderTests/Elsa.Rules.Persistence.EntityFrameworkCore.ProviderTests.csproj"
REQUIRE = "ELSA_RULES_EF_REQUIRE_NATIVE_PROVIDERS"
PROVIDERS = ("Sqlite", "PostgreSql", "SqlServer", "MySql")


def source_state():
    head = subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=ROOT, text=True).strip()
    digest = hashlib.sha256(head.encode())
    diff = subprocess.check_output(["git", "diff", "HEAD", "--binary"], cwd=ROOT)
    digest.update(diff)
    untracked = subprocess.check_output(["git", "ls-files", "--others", "--exclude-standard", "-z"], cwd=ROOT)
    for name in sorted(filter(None, untracked.split(b"\0"))):
        path = ROOT / os.fsdecode(name)
        digest.update(name + b"\0")
        digest.update(os.readlink(path).encode() if path.is_symlink() else path.read_bytes())
    return {"head": head, "dirty": bool(diff or untracked), "fingerprint": digest.hexdigest()}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--require-all", action="store_true", help="Missing engines or skipped tests fail qualification.")
    parser.add_argument("--evidence-dir", required=True, type=Path)
    args = parser.parse_args()
    evidence = args.evidence_dir.expanduser().resolve() / ("run-" + uuid.uuid4().hex)
    if evidence.is_relative_to(ROOT):
        parser.error("Evidence must be outside the source checkout.")
    evidence.mkdir(parents=True)
    env = os.environ.copy()
    env[REQUIRE] = "1" if args.require_all else "0"
    command = ["dotnet", "test", PROJECT, "--logger", "trx;LogFileName=rules-providers.trx",
               "--results-directory", str(evidence)]
    receipt = {"schemaVersion": 1, "requiredProviders": list(PROVIDERS), "requireAll": args.require_all,
               "sourceBefore": source_state(),
               "command": command, "qualified": False}
    # Connection strings stay in the environment, never in command arguments or receipt fields.
    secrets = [value for name, value in env.items()
               if name.startswith("ELSA_RULES_EF_") and name.endswith("_CONNECTION_STRING") and value]
    credentials = []
    for secret in secrets:
        credentials.extend(match[1].strip("\"'") for match in
                           re.findall(r"(?i)(password|pwd)\s*=\s*(\"[^\"]*\"|'[^']*'|[^;]*)", secret))

    def redact(text):
        for secret in sorted(set(secrets + credentials), key=len, reverse=True):
            if secret:
                text = text.replace(secret, "[REDACTED]").replace(escape(secret), "[REDACTED]")
        return re.sub(r"(?i)(password|pwd)\s*=\s*[^;\r\n<]*", r"\1=[REDACTED]", text)

    print(f"Rules provider evidence: {evidence}", flush=True)
    try:
        result = subprocess.run(command, cwd=ROOT, env=env, text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
        output = redact(result.stdout)
        (evidence / "command.log").write_text(output)
        receipt["exitCode"] = result.returncode
        trx = evidence / "rules-providers.trx"
        if trx.exists():
            # Preserve the report while removing any supplied full connection string a provider printed.
            xml = redact(trx.read_text())
            trx.write_text(xml)
            ns = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
            report = ET.fromstring(xml)
            counters = report.find("t:ResultSummary/t:Counters", ns)
            receipt["counts"] = dict(counters.attrib) if counters is not None else {}
            results = report.findall("t:Results/t:UnitTestResult", ns)
            receipt["providers"] = {
                provider: {"total": len(matches := [r for r in results if f"Rules{provider}" in r.attrib.get("testName", "")]),
                           "passed": sum(r.attrib.get("outcome") == "Passed" for r in matches)}
                for provider in PROVIDERS
            }
            counts = {name: int(value) for name, value in receipt["counts"].items()}
            complete = (counts.get("total") == len(results) == counts.get("passed") == counts.get("executed")
                        and all(value == 0 for name, value in counts.items()
                                if name not in {"total", "passed", "executed", "completed"}))
            receipt["qualified"] = (args.require_all and result.returncode == 0 and bool(results) and complete
                                    and all(r.attrib.get("outcome") == "Passed" for r in results)
                                    and all(p["total"] > 0 and p["total"] == p["passed"] for p in receipt["providers"].values()))
        else:
            receipt["error"] = "The test runner produced no TRX report."
    except (OSError, ValueError, ET.ParseError) as error:
        receipt["error"] = type(error).__name__
    finally:
        receipt["sourceAfter"] = source_state()
        receipt["sourceUnchanged"] = receipt["sourceBefore"] == receipt["sourceAfter"]
        receipt["qualified"] = receipt["qualified"] and receipt["sourceUnchanged"]
        (evidence / "receipt.json").write_text(json.dumps(receipt, indent=2) + "\n")
    print(json.dumps(receipt, indent=2))
    return 0 if receipt["qualified"] else 1


if __name__ == "__main__":
    sys.exit(main())
