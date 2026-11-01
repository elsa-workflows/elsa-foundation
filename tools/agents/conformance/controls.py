"""Adversarial host controls against an immutable, successful durable package qualification."""
import argparse
import base64
import hashlib
import json
import os
from pathlib import Path
import shutil
import sqlite3
import subprocess


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--baseline", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    baseline = args.baseline.resolve()
    if not args.output.is_absolute() or args.output.exists():
        parser.error("output must be a new absolute directory")
    out = args.output.resolve()
    if out.is_relative_to(Path(__file__).resolve().parents[3]) or out.is_relative_to(baseline):
        parser.error("output must be outside the source checkout and baseline")
    receipt = json.loads((baseline / "report.json").read_text())
    if receipt["status"] != "PassedDurableRecoveryProfile":
        parser.error("baseline must be a successful durable recovery profile")
    out.mkdir(parents=True)
    source = (baseline / "DurableConsumer/Program.cs").read_text()
    changes = [
        ("missing-readability", "services.AddEfSchemaReadability();", "", "start"),
        ("retired-content-key", 'Encoding.UTF8.GetBytes("synthetic-specialist-durable-content-key-v1")',
         'Encoding.UTF8.GetBytes(role == "recover" ? "synthetic-wrong-recovery-key" : "synthetic-specialist-durable-content-key-v1")', "recover"),
        ("omitted-application", "        await inner.DispatchAsync(source, cancellationToken);",
         '        if (source.Intent.Kind != "elsa.specialist.applied/1") await inner.DispatchAsync(source, cancellationToken);', "recover"),
    ]
    report = {"baselineSource": receipt["source"], "baselineVersion": receipt["version"],
              "baselineReceiptSha256": hashlib.sha256((baseline / "report.json").read_bytes()).hexdigest(),
              "status": "InProgress", "controls": []}

    def save():
        (out / "report.json").write_text(json.dumps(report, indent=2) + "\n")

    def decoded(value):
        try:
            return base64.b64decode(value, validate=True).decode("utf-16-le")
        except (ValueError, UnicodeError):
            return value

    try:
        for name, old, new, failure_role in changes:
            if source.count(old) != 1:
                raise RuntimeError(name + ": unexpected counterfactual target count")
            directory = out / name
            directory.mkdir()
            for file in ["DurableConsumer.csproj", "NuGet.Config", "global.json", "packages.lock.json"]:
                shutil.copy2(baseline / "DurableConsumer" / file, directory / file)
            mutated = source.replace(old, new)
            (directory / "Program.cs").write_text(mutated)
            environment = dict(os.environ, NUGET_PACKAGES=str(baseline / "consumer-packages"),
                               NUGET_HTTP_CACHE_PATH=str(directory / "http-cache"))
            project = directory / "DurableConsumer.csproj"
            steps = []
            for stage, command in [
                ("locked-restore", ["dotnet", "restore", str(project), "--locked-mode"]),
                ("build", ["dotnet", "build", str(project), "-c", "Release", "--no-restore"]),
            ]:
                log = directory / (stage + ".log")
                with log.open("w") as stream:
                    result = subprocess.run(command, cwd=directory, env=environment, stdout=stream, stderr=subprocess.STDOUT)
                if result.returncode:
                    raise RuntimeError(name + ": setup failed, not a causal refusal: " + str(log))
                steps.append({"name": stage, "exitCode": 0, "status": "Passed", "log": str(log)})
            data = directory / "state"
            for role in ["start", "worker", "recover"]:
                log = directory / (role + ".log")
                with log.open("w") as stream:
                    result = subprocess.run(["dotnet", "run", "--project", str(project), "-c", "Release", "--no-build", "--no-restore", "--",
                        str(baseline / "runtime-inbox"), role, str(data), "Durable"], cwd=directory, env=environment, stdout=stream, stderr=subprocess.STDOUT)
                if role == failure_role:
                    if result.returncode in [0, 197, 198]:
                        raise RuntimeError(name + ": expected normal refusal, observed " + str(result.returncode))
                    steps.append({"name": role, "exitCode": result.returncode, "status": "ExpectedRefusal", "log": str(log)})
                    break
                expected = 197 if role == "worker" else 0
                if result.returncode != expected:
                    raise RuntimeError(name + ": failed before intended boundary: " + str(log))
                steps.append({"name": role, "exitCode": result.returncode, "status": "ExpectedCrash" if expected else "Passed", "log": str(log)})
            dispatches = len(list((data / "dispatches").glob("dispatch-*.json")))
            database = sqlite3.connect((data / "runtime.db").as_uri() + "?mode=ro", uri=True)
            turns = [json.loads(row[0]) for row in database.execute("SELECT Payload FROM elsa_specialist_turns")]
            observations = {"modelDispatches": dispatches, "turnCount": len(turns)}
            if name == "missing-readability":
                if "ISchemaDormancyCheck" not in (directory / "start.log").read_text() or dispatches or turns:
                    raise RuntimeError(name + ": wrong refusal or model work occurred")
            else:
                if dispatches != 1 or len(turns) != 1 or turns[0]["status"] != "Accepted" or turns[0].get("applications"):
                    raise RuntimeError(name + ": lost acceptance, repeated model or unexpectedly applied")
                if name == "retired-content-key":
                    work = json.loads(database.execute("SELECT ContentJson FROM elsa_runtime_background_activity_work").fetchone()[0])
                    code = decoded(work["result"]["fault"]["code"])
                    if code != "specialist.journal.protection":
                        raise RuntimeError(name + ": wrong background refusal code " + code)
                    observations["nativeRefusalCode"] = code
                else:
                    workflow = database.execute("SELECT Status FROM elsa_runtime_workflow_execution_state").fetchone()[0]
                    sources = list(database.execute("SELECT Status, ContentJson FROM elsa_runtime_post_commit_outbox WHERE IntentKind = ?", ("elsa.specialist.applied/1",)))
                    if workflow != 3 or not sources or any(status == 2 for status, _ in sources):
                        raise RuntimeError(name + ": wrong native/source failure boundary")
                    if not any("acknowledgement crash requires actual applied" in decoded(json.loads(payload).get("lastFailureMessage") or "") for _, payload in sources):
                        raise RuntimeError(name + ": missing exact application-proof failure")
                    observations["nativeCompletedWithoutApplication"] = True
            database.close()
            report["controls"].append({"name": name, "consumerSourceSha256": hashlib.sha256(mutated.encode()).hexdigest(),
                                       "steps": steps, "observations": observations})
            save()
            print(name + ": intended refusal verified", flush=True)
        report["status"] = "PassedHostRefusalControls"
        save()
    except Exception as error:
        report["status"] = "Failed"
        report["error"] = str(error)
        save()
        raise


if __name__ == "__main__":
    main()
