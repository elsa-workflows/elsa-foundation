"""Verify actual authored-publication snapshots using the supported read-only provider observer."""
import argparse
import base64
import gzip
import hashlib
import json
import sqlite3
import subprocess
import tempfile
import xml.etree.ElementTree as ET
from pathlib import Path

parser = argparse.ArgumentParser()
parser.add_argument("--evidence", type=Path, required=True)
parser.add_argument("--reader", type=Path, required=True)
args = parser.parse_args()
evidence = args.evidence.resolve(strict=True)
reader = args.reader.resolve(strict=True)
actual = json.loads((evidence / "authored-direct-publication.json").read_text())
trx = ET.parse(evidence / "authored-host.trx").getroot()
ns = {"t": trx.tag.split("}")[0][1:]}
counters = trx.find(".//t:Counters", ns)
assert counters is not None and counters.get("total") == counters.get("passed") == "1"
assert counters.get("failed") == counters.get("notExecuted") == "0"
def verify_snapshots(snapshots):
    observations = []
    previous = None
    for snapshot in snapshots:
        name = snapshot["file"]
        assert Path(name).name == name
        compressed = (evidence / name).read_bytes()
        data = gzip.decompress(compressed)
        assert hashlib.sha256(data).hexdigest() == snapshot["databaseSha256"]
        assert hashlib.sha256(compressed).hexdigest() == snapshot["gzipSha256"]
        with tempfile.TemporaryDirectory() as directory:
            database = Path(directory) / "copy.sqlite"
            database.write_bytes(data)
            with sqlite3.connect("file:" + str(database) + "?mode=ro", uri=True) as connection:
                assert connection.execute("pragma integrity_check").fetchone()[0] == "ok"
                if "runtime" not in name:
                    observations.append({"file": name, "integrity": "ok", "bytes": len(data)})
                    continue
                references = []
                for (raw,) in connection.execute("select ContentJson from elsa_runtime_workflow_executable_source_reference"):
                    value = json.loads(raw)["reference"]
                    # These typed string fields use RuntimeArtifactJson's UTF-16/base64 codec;
                    # opaque JsonElement values must not be recursively decoded.
                    references.append({key: base64.b64decode(value[key], validate=True).decode("utf-16-le")
                        if value.get(key) is not None else None for key in ("sourceReferenceId", "artifactId", "sourceKind",
                        "sourceId", "sourceVersion", "definitionId", "definitionVersionId", "artifactVersion")})
            typed = json.loads(subprocess.check_output(["dotnet", str(reader), "--read-bpmn-direct",
                str(database), actual["started"]["executionId"]], text=True))
            (evidence / (name + ".typed-read.json")).write_text(json.dumps(typed, indent=2) + "\n")
            assert typed["readOnly"] is True
            state = typed["model"]
            reference = next(item for item in references if item["sourceReferenceId"] == actual["published"]["sourceReferenceId"])
            assert reference["sourceKind"] == "BpmnModel"
            assert state["payload"]["sourceReferenceId"] == reference["sourceReferenceId"]
            assert reference["sourceId"] == actual["promoted"]["id"]
            assert reference["sourceVersion"] == actual["promoted"]["version"]
            assert state["executable"] == actual["published"]["executable"]
            assert reference["definitionId"] == state["executable"]["definitionId"]
            assert reference["definitionVersionId"] == state["executable"]["definitionVersionId"]
            assert all(typed[key] == 0 for key in ("nativeWorkflowRows", "nativeActivityRows", "nativeBookmarkRows"))
            if previous is not None:
                assert state["authority"] == previous["authority"]
                assert state["materialIncarnationId"] == previous["materialIncarnationId"]
                assert state["revision"] == previous["revision"] + 1
            else:
                assert len(state["payload"]["work"]) == 1
                first_model = state
            previous = state
            observations.append({"file": name, "revision": state["revision"], "lifecycle": state["lifecycle"],
                "adapterFormatVersion": state["payload"]["formatVersion"], "sourceReferenceId": reference["sourceReferenceId"],
                "authoredVersionId": reference["sourceId"], "authoredVersion": reference["sourceVersion"],
                "inputReceipts": len(typed["inputs"]), "nativeRows": 0, "integrity": "ok"})
    assert previous["terminalResult"]["status"] == "Completed" and not previous["payload"]["work"]
    return observations, first_model

observations, original_wait = verify_snapshots(actual["snapshots"])
runtime_only = actual.get("runtimeOnly")
if runtime_only is not None:
    assert runtime_only["actualRuntimeCopy"] is True and runtime_only["designStoreSupplied"] is False
    assert runtime_only["designDatabaseAbsent"] is True and runtime_only["admission"]["admitNewExecutions"] is False
    assert runtime_only["recovered"] == actual["inspection"]
    assert "elsa.api.bpmn-publishing" not in json.dumps(runtime_only["capabilities"])
    assert "elsa.api.bpmn-inspection" in json.dumps(runtime_only["capabilities"])
    extra, copied_wait = verify_snapshots(runtime_only["snapshots"])
    assert copied_wait == original_wait
    observations.extend(extra)
for binary in actual["binaries"] + (runtime_only["binaries"] if runtime_only else []):
    if binary["file"].startswith("Elsa."):
        assert binary["productVersion"].endswith("+" + actual["sourceRevision"])
result = {"source": actual["sourceRevision"], "hostTestPassed": True, "allElsaProductsMatchSource": True,
    "snapshots": observations, "immutablePinAuthorityIncarnationPreserved": True, "nativeWrapperAbsent": True,
    "studioBrowserProved": False, "runtimeOnlyHostProvedByThisCase": runtime_only is not None,
    "runtimeOnlyAdmissionOffProved": runtime_only is not None}
(evidence / "physical-verification.json").write_text(json.dumps(result, indent=2) + "\n")
print(json.dumps(result, indent=2))
