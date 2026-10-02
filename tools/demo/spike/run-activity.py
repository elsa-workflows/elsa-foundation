#!/usr/bin/env python3
"""Spike only: submits a one-activity workflow for a catalog activity version, publishes it and executes it, through the
same backend APIs Studio uses, then waits for the run and prints its status (and its incidents when it faulted).

    python3 tools/demo/spike/run-activity.py TYPE_KEY_SUFFIX VERSION [NAME=VALUE ...] [--save FILE] [--name NAME]
    python3 tools/demo/spike/run-activity.py --rerun FILE        executes a workflow published earlier, as it was published

NAME=VALUE pairs become literal inputs (NAME is the input's referenceKey). --save writes the published artifact and its
source reference to FILE, so --rerun can execute the same published workflow again later (for instance after an upgrade).
Options: --base URL (default http://localhost:7211), --token FILE (default artifacts/spike/hosts/wb/.token).
"""
import argparse
import json
import os
import sys
import time
import urllib.error
import urllib.request

root = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", ".."))
parser = argparse.ArgumentParser()
parser.add_argument("type_key", nargs="?")
parser.add_argument("version", nargs="?")
parser.add_argument("inputs", nargs="*")
parser.add_argument("--base", default="http://localhost:7211")
parser.add_argument("--token", default=os.path.join(root, "artifacts/spike/hosts/wb/.token"))
parser.add_argument("--name", default=None)
parser.add_argument("--save", default=None)
parser.add_argument("--rerun", default=None)
args = parser.parse_args()
token = open(args.token).read().strip()


def call(method, path, body=None):
    data = None if body is None else json.dumps(body).encode()
    request = urllib.request.Request(args.base + path, data=data, method=method, headers={
        "Authorization": "Bearer " + token, "Accept": "application/json", "Content-Type": "application/json"})
    try:
        with urllib.request.urlopen(request) as response:
            text = response.read().decode()
            return response.status, (json.loads(text) if text else None)
    except urllib.error.HTTPError as error:
        text = error.read().decode()
        print(f"{method} {path} -> HTTP {error.code}\n{text[:3000]}", file=sys.stderr)
        sys.exit(1)


def execute(artifact, source_reference):
    status, executed = call("POST", f"/runtime/workflows/executables/{artifact}/execute",
                            {"sourceReferenceId": source_reference} if source_reference else {})
    run = executed["workflowExecutionId"]
    print(f"execute -> HTTP {status}, {executed['commandDispatchStatus']}, run {run}")
    state = "?"
    for _ in range(60):
        _, instance = call("GET", f"/runtime/workflows/instances/{run}")
        state = instance["instance"]["status"]
        if state in ("Completed", "Faulted", "Cancelled"):
            break
        time.sleep(0.5)
    print(f"run {run}: {state}")
    if state == "Faulted":
        _, incidents = call("GET", f"/runtime/workflows/instances/{run}/incidents")
        for incident in incidents["incidents"]:
            print(f"  incident {incident['failureType']}: {incident['message']}")
        sys.exit(2)


if args.rerun:
    saved = json.load(open(args.rerun))
    print(f"re-running published artifact {saved['artifactId']} ({saved['activity']})")
    execute(saved["artifactId"], saved["sourceReferenceId"])
    sys.exit(0)

_, catalog = call("GET", "/design/activities/catalog")
items = catalog if isinstance(catalog, list) else next((v for v in catalog.values() if isinstance(v, list)), [])
matches = [i for i in items if i["activityTypeKey"].endswith(args.type_key) and i["version"] == args.version]
if not matches:
    print(f"no catalog entry for *{args.type_key} {args.version}; versions present: "
          f"{[(i['activityTypeKey'], i['version']) for i in items if i['activityTypeKey'].endswith(args.type_key)]}", file=sys.stderr)
    sys.exit(1)
activity = matches[0]
print(f"activity: {activity['displayName']} {activity['version']}, inputs {[i['name'] for i in activity['inputs']]}")

inputs = []
for pair in args.inputs:
    name, value = pair.split("=", 1)
    inputs.append({"referenceKey": name, "value": {"value": value, "expressionType": "Literal"}, "autoEvaluate": None,
                   "evaluatorType": None, "storageDriverType": None, "isSensitive": None})

name = args.name or f"spike {args.type_key} {args.version} {time.strftime('%H:%M:%S')}"
_, submitted = call("POST", "/design/workflows/definitions/submit", {
    "name": name, "description": "spike", "state": {
        "variables": [], "rootActivity": {"nodeId": "activity", "activityVersionId": activity["activityVersionId"],
                                          "inputs": inputs, "outputs": []},
        "inputs": [], "outputs": [], "workflowActivityOptions": None, "strategyOptions": None}})
version_id = submitted["version"]["id"]
_, published = call("POST", f"/publishing/workflows/{version_id}/publish", {})
artifact, source_reference = published["artifactId"], published.get("sourceReferenceId")
print(f"workflow '{name}': version {version_id}, published as {artifact}")
if args.save:
    json.dump({"artifactId": artifact, "sourceReferenceId": source_reference,
               "activity": f"{activity['displayName']} {activity['version']}"}, open(args.save, "w"))
execute(artifact, source_reference)
