#!/usr/bin/env python3
"""Strict public-API proof; test payloads only, no direct store writes or scheduler shortcuts."""

from pathlib import Path
import argparse, os
import importlib.util, json, uuid, time, urllib.request
import subprocess

s = importlib.util.spec_from_file_location(
    "q", Path(__file__).resolve().parents[2] / "tools/bpmn/qualify-hosting.py"
)
q = importlib.util.module_from_spec(s)
s.loader.exec_module(q)
parser = argparse.ArgumentParser(
    description="Strict native block transparency/restart/cancellation/rescheduling proof against run_owned_prototype.py only."
)
parser.add_argument("--control-dir", type=Path, required=True)
OUT = parser.parse_args().control_dir.resolve()
if (OUT / "native-transparency.json").exists():
    raise ValueError("Refuse to overwrite an existing proof")
ready = json.loads((OUT / "ready.json").read_text())
REPO = Path(__file__).resolve().parents[2]


def verify_owned_run(processes=None):
    # Before authenticated writes, require this checkout's live controller, owned children and exact products.
    if (
        (OUT / "closed.json").exists()
        or ready["repositoryRoot"] != str(REPO)
        or ready["controlDirectory"] != str(OUT)
    ):
        raise ValueError("This is not an active owned run in this checkout")
    current = subprocess.check_output(
        ["git", "rev-parse", "HEAD"], cwd=REPO, text=True
    ).strip()
    if (
        current != ready["source"]
        or subprocess.check_output(
            ["git", "status", "--porcelain"], cwd=REPO, text=True
        ).strip()
    ):
        raise ValueError(
            "Qualification requires the exact clean committed source of the owned run"
        )
    controller = ready["controller"]
    if (
        q.digest(Path(__file__).with_name("run_owned_prototype.py"))
        != controller["scriptSha256"]
    ):
        raise ValueError("The owned controller source changed")
    for field, ps_field in (
        ("started", "lstart"),
        ("executable", "comm"),
        ("command", "args"),
    ):
        if q.process_field(controller["pid"], ps_field) != controller[field]:
            raise ValueError("The owned controller identity changed")
    latest = {process["role"]: process for process in (processes or ready["processes"])}
    if set(latest) != {"studio", "workbench"}:
        raise ValueError("Both actual owned hosts are required")
    for process in latest.values():
        if (
            process["parentPid"] != controller["pid"]
            or int(q.process_field(process["pid"], "ppid")) != controller["pid"]
            or q.process_field(process["pid"], "lstart") != process["started"]
            or q.process_field(process["pid"], "comm") != process["executable"]
        ):
            raise ValueError("An owned child identity changed")
    directories = {
        "workbench": REPO / "Essentials/apps/Elsa.Workbench/bin/Debug/net10.0",
        "studio": REPO / "Studio/src/apps/Elsa.Studio.Web/bin/Debug/net10.0",
    }
    for role, product_set in ready["products"].items():
        if product_set["sourceCommit"] != current:
            raise ValueError("Host product source differs from this checkout")
        for product in product_set["actualProductVersions"]:
            if (
                Path(product["name"]).name != product["name"]
                or q.digest(directories[role] / product["name"]) != product["sha256"]
            ):
                raise ValueError("A recorded actual host product changed")
    for relative, expected in ready["browserAssets"].items():
        asset = (REPO / relative).resolve()
        asset.relative_to(REPO)
        if q.digest(asset) != expected:
            raise ValueError("A recorded actual browser asset changed")


verify_owned_run()
if q.digest(OUT / "workbench/shells.json") != ready["compositionSha256"]:
    raise ValueError("The initial owned composition changed")
q.Api(
    ready["studioUrl"]
)  # Validate both loopback URLs before any authenticated request.
a = q.Api(ready["backendUrl"])
a.login(
    os.environ.get("ELSA_STUDIO_USER", "admin"),
    os.environ.get("ELSA_STUDIO_PASSWORD", "Password123!"),
)
v = {
    k: a.activity(t)
    for k, t in {
        "seq": "Elsa.Activities.Sequence.Activities.Sequence",
        "inline": "Elsa.Activities.Primitives.Activities.Inline",
        "write": "Elsa.Activities.Primitives.Activities.WriteLine",
        "event": "Elsa.Activities.Primitives.Activities.Event",
        "while": "Elsa.Activities.While.Activities.While",
        "flow": "Elsa.Activities.Flowchart.Activities.Flowchart",
        "bpmn": "Elsa.Activities.Bpmn.Activities.BpmnProcess",
    }.items()
}


def expr(key, value, kind="Literal"):
    return {"referenceKey": key, "value": {"value": value, "expressionType": kind}}


def seq(id, *children):
    return q.node(
        id,
        v["seq"],
        structure={
            "kind": "elsa.sequence.structure",
            "schemaVersion": "1.0.0",
            "payload": {"activities": list(children)},
        },
    )


def result(id):
    return {
        "producerNodeId": id,
        "projectionKey": "$result",
        "producerScopeId": "scope:root",
    }


def output(id, key, value, kind="Literal"):
    n = q.node(
        id,
        "elsa.intrinsic.set-output@1",
        [expr("name", key), expr("value", value, kind)],
    )
    n["intrinsic"] = {
        "kind": "setOutput",
        "valueType": {"alias": "Elsa.Any", "collectionKind": "single"},
    }
    return n


def variable(key, default, alias="Int32"):
    return {
        "referenceKey": key,
        "name": key,
        "type": {"alias": alias, "collectionKind": "single"},
        "storageDriverType": None,
        "default": {"value": default, "expressionType": "Literal"},
    }


def setvar(id, key, value, kind="Literal"):
    n = q.node(id, "elsa.intrinsic.set@1", [expr("value", value, kind)])
    n["intrinsic"] = {
        "kind": "set",
        "valueType": {"alias": "Elsa.Any", "collectionKind": "single"},
        "variable": {"referenceKey": key},
    }
    return n


def wrap(kind, id, children):
    if kind == "seq":
        return seq(id, *children)
    if kind == "flow":
        return q.node(
            id,
            v["flow"],
            structure={
                "kind": "elsa.flowchart.structure",
                "schemaVersion": "1.0.0",
                "payload": {
                    "activities": children,
                    "startNodeId": children[0]["nodeId"],
                    "nodeMetadata": {},
                    "connectionMetadata": {},
                    "connections": [
                        {
                            "id": f"f{i}",
                            "source": {"nodeId": x["nodeId"], "port": "Done"},
                            "target": {"nodeId": y["nodeId"]},
                        }
                        for i, (x, y) in enumerate(zip(children, children[1:]))
                    ],
                },
            },
        )
    if kind == "bpmn":
        elements = (
            [{"elementId": "start", "elementType": "startEvent"}]
            + [
                {
                    "elementId": f"t{i}",
                    "elementType": "task",
                    "childNodeId": n["nodeId"],
                }
                for i, n in enumerate(children)
            ]
            + [{"elementId": "end", "elementType": "endEvent"}]
        )
        return q.node(
            id,
            v["bpmn"],
            structure={
                "kind": "elsa.bpmn.structure",
                "schemaVersion": "1.0.0",
                "payload": {
                    "activities": children,
                    "elements": elements,
                    "sequenceFlows": [
                        {
                            "flowId": f"f{i}",
                            "sourceRef": x["elementId"],
                            "targetRef": y["elementId"],
                        }
                        for i, (x, y) in enumerate(zip(elements, elements[1:]))
                    ],
                },
            },
        )
    raise ValueError(kind)


def create(name, root, variables=(), options=None):
    d = a.request(
        "/design/workflows/definitions/submit",
        {
            "name": "Blocks S001 " + name + " " + uuid.uuid4().hex,
            "description": "Owned prototype qualification",
            "state": {
                "variables": list(variables),
                "inputs": [],
                "outputs": [],
                "rootActivity": root,
                "strategyOptions": options or {"persistenceMode": "Balanced"},
            },
        },
    )
    p = a.request("/publishing/workflows/" + d["version"]["id"] + "/publish", {})
    r = a.request(
        "/runtime/workflows/executables/" + p["artifactId"] + "/execute",
        {"sourceReferenceId": p["sourceReferenceId"]},
    )
    return {
        "name": name,
        "definitionId": d["definition"]["id"],
        "versionId": d["version"]["id"],
        "publication": p,
        "workflowExecutionId": r["workflowExecutionId"],
        "authoredRoot": root,
        "variables": list(variables),
        "options": options,
    }


def awaitview(c, predicate):
    end = time.monotonic() + 90
    while True:
        view = a.view(c["workflowExecutionId"])
        if predicate(view):
            return view
        if time.monotonic() > end or view["instance"]["status"] in (
            "Faulted",
            "Cancelled",
        ):
            raise ValueError(
                c["name"]
                + " unexpected "
                + view["instance"]["status"]
                + " incidents "
                + str(view.get("incidents"))
            )
        time.sleep(0.2)


def completed(c):
    view = awaitview(c, lambda x: x["instance"]["status"] == "Completed")
    if view.get("incidents"):
        raise ValueError(c["name"] + " incidents")
    c["view"] = view
    return view


cases = []
report = {
    "source": json.loads((OUT / "ready.json").read_text())["source"],
    "status": "Running",
    "cases": cases,
}


def save():
    q.write_json(OUT / "native-transparency.json", report)


def plan(c, kind, payload):
    req = urllib.request.Request(
        a.base + "/runtime/workflows/alteration-plans",
        data=json.dumps(
            {
                "target": {"executionIds": [c["workflowExecutionId"]]},
                "alterations": [{"kind": kind, "schemaVersion": 1, "payload": payload}],
            }
        ).encode(),
        headers={
            "Content-Type": "application/json",
            "Idempotency-Key": "blocks-" + uuid.uuid4().hex,
        },
    )
    with a.opener.open(req, timeout=30) as r:
        receipt = json.load(r)
    end = time.monotonic() + 90
    while True:
        p = a.request("/runtime/workflows/alteration-plans/" + receipt["planId"])
        if p["status"] in ("Completed", "CompletedWithFailures", "Failed", "Cancelled"):
            break
        if time.monotonic() > end:
            raise TimeoutError("alteration completion")
        time.sleep(0.25)
    jobs = a.request(
        "/runtime/workflows/alteration-plans/"
        + receipt["planId"]
        + "/jobs/page?take=100"
    )
    assert (
        p["status"] == "Completed"
        and p["counts"]["succeeded"] == 1
        and p["counts"]["failed"] == 0
    )
    assert len(jobs["items"]) == 1 and jobs["items"][0]["status"] == "Succeeded"
    return {"receipt": receipt, "terminal": p, "jobs": jobs}


for kind in ["seq", "flow", "bpmn"]:
    for extracted in [False, True]:
        for direction in ["out-to-in", "in-to-out"]:
            prod = q.node("producer", v["inline"], [expr("Expression", 42)])
            cons = output("consumer", "answer", result("producer"), "ActivityResult")
            nodes = (
                [prod, cons]
                if not extracted
                else [prod, wrap(kind, "block", [cons])]
                if direction == "out-to-in"
                else [wrap(kind, "block", [prod]), cons]
            )
            c = create(f"{kind}-{direction}-extracted-{extracted}", seq("root", *nodes))
            cases.append(c)
            save()
            view = completed(c)
            assert view["outputs"]["answer"]["value"]["preview"] == "42"
            save()
            print(c["name"], "answer=42; completed without incidents", flush=True)
# Root variable is read/written through a new transparent block on every loop occurrence.
for extracted in [False, True]:
    prod = q.node("producer", v["inline"], [expr("Expression", "counter", "Variable")])
    cons = q.node(
        "consumer",
        v["inline"],
        [expr("Expression", result("producer"), "ActivityResult")],
    )
    inc = setvar("increment", "counter", "getVariable('counter') + 1", "JavaScript")
    body = (
        seq("body", wrap("seq", "block", [inc, prod]), cons)
        if extracted
        else seq("body", inc, prod, cons)
    )
    loop = q.node(
        "loop",
        v["while"],
        [expr("Condition", "getVariable('counter') < 3", "JavaScript")],
        {
            "kind": "elsa.while.structure",
            "schemaVersion": "1.0.0",
            "payload": {"body": body},
        },
    )
    c = create(
        "loop-extracted-" + str(extracted),
        seq("root", loop, output("last", "counter", "counter", "Variable")),
        [variable("counter", 0)],
    )
    cases.append(c)
    save()
    view = completed(c)
    assert view["outputs"]["counter"]["value"]["value"] == 3
    assert all(
        len(
            [
                n
                for n in view["activities"]
                if n["executableNodeId"] == key and n["status"] == "Completed"
            ]
        )
        == 3
        for key in ["increment", "producer", "consumer"]
    )
    c["consumerDetails"] = [
        a.request(
            "/runtime/workflows/instances/"
            + c["workflowExecutionId"]
            + "/activity-executions/"
            + n["activityExecutionId"]
        )
        for n in view["activities"]
        if n["executableNodeId"] == "consumer"
    ]
    c["consumerPayloads"] = [
        a.request(
            "/runtime/workflows/instances/"
            + c["workflowExecutionId"]
            + "/activity-executions/"
            + n["activityExecutionId"]
            + "/value-evidence/"
            + n["valueSnapshots"][0]["evidenceId"]
            + "/payload"
        )
        for n in sorted(c["consumerDetails"], key=lambda n: n["scheduledAt"])
    ]
    assert [p["payload"]["value"] for p in c["consumerPayloads"]] == [1, 2, 3]
    save()
    print(c["name"], "counter=3; exact consumer values 1,2,3", flush=True)
waiting = []
for mode in ["Balanced", "Durable"]:
    for extracted in [False, True]:
        event = "block-lifecycle-" + uuid.uuid4().hex
        prod = q.node("producer", v["inline"], [expr("Expression", 73)])
        wait = q.event_node("wait", v["event"], event)
        middle = [wrap("seq", "block", [prod, wait])] if extracted else [prod, wait]
        root = seq(
            "root",
            setvar("set", "message", "retained"),
            *middle,
            output("answer", "answer", result("producer"), "ActivityResult"),
            output("message", "message", "message", "Variable"),
        )
        c = create(
            f"restart-{mode}-extracted-{extracted}",
            root,
            [variable("message", "", "String")],
            {"persistenceMode": mode},
        )
        c["event"] = event
        report["cases"].append(c)
        waiting.append(c)
        save()
        c["held"] = awaitview(
            c,
            lambda x: any(
                n["executableNodeId"] == "wait" and n["status"] == "Suspended"
                for n in x["activities"]
            ),
        )
        save()
        assert all(
            len([n for n in c["held"]["activities"] if n["executableNodeId"] == key])
            == 1
            for key in ["set", "producer", "wait"]
        )
print("Four genuine baseline/grouped runs held", flush=True)
restart = OUT / "restarted.json"
previous = restart.read_bytes() if restart.exists() else None
q.write_json(OUT / "command.json", {"action": "restart"})
end = time.monotonic() + 180
while not restart.exists() or restart.read_bytes() == previous:
    if time.monotonic() > end:
        raise TimeoutError("Owned host restart")
    time.sleep(0.25)
a.login(
    os.environ.get("ELSA_STUDIO_USER", "admin"),
    os.environ.get("ELSA_STUDIO_PASSWORD", "Password123!"),
)
report["restart"] = json.loads(restart.read_text())
save()
for c in waiting:
    c["afterRestart"] = a.view(c["workflowExecutionId"])
    assert [
        (n["activityExecutionId"], n["status"]) for n in c["afterRestart"]["activities"]
    ] == [(n["activityExecutionId"], n["status"]) for n in c["held"]["activities"]]
    c["resume"] = a.send(c["event"], c["workflowExecutionId"])
    c["completed"] = completed(c)
    assert c["completed"]["outputs"]["answer"]["value"]["preview"] == "73"
    assert c["completed"]["outputs"]["message"]["value"]["preview"] == "retained"
    assert all(
        len([n for n in c["completed"]["activities"] if n["executableNodeId"] == key])
        == 1
        for key in ["set", "producer", "wait", "answer", "message"]
    )
    save()
    print(c["name"], "resumed once after process restart; outputs intact", flush=True)
for extracted in [False, True]:
    event = "block-cancel-" + uuid.uuid4().hex
    wait = q.event_node("wait", v["event"], event)
    root = seq(
        "root",
        wrap("seq", "block", [wait]) if extracted else wait,
        output("after", "after", "must not run"),
    )
    c = create("cancel-extracted-" + str(extracted), root)
    c["event"] = event
    report["cases"].append(c)
    save()
    c["held"] = awaitview(
        c,
        lambda x: any(
            n["executableNodeId"] == "wait" and n["status"] == "Suspended"
            for n in x["activities"]
        ),
    )
    c["cancelPlan"] = plan(c, "CancelWorkflow", {})
    c["cancelled"] = a.view(c["workflowExecutionId"])
    assert c["cancelled"]["instance"]["status"] == "Cancelled"
    assert {n["activityExecutionId"] for n in c["cancelled"]["activities"]} == {
        n["activityExecutionId"] for n in c["held"]["activities"]
    }
    assert all(n["status"] == "Cancelled" for n in c["cancelled"]["activities"])
    assert not any(
        n["executableNodeId"] == "after" for n in c["cancelled"]["activities"]
    )
    c["lateDelivery"] = a.request(
        "/runtime/workflows/stimuli",
        {
            "stimulusType": "Event",
            "stimulusHash": "sha256:" + q.hashlib.sha256(event.encode()).hexdigest(),
            "mode": "ResumeOnly",
            "correlationId": c["workflowExecutionId"],
            "input": {"eventName": event},
        },
    )
    assert (
        c["lateDelivery"]["startedCount"] == 0
        and c["lateDelivery"]["resumedCount"] == 0
    )
    c["afterLateDelivery"] = a.view(c["workflowExecutionId"])
    assert c["afterLateDelivery"]["instance"]["status"] == "Cancelled"
    assert not any(
        n["executableNodeId"] == "after" for n in c["afterLateDelivery"]["activities"]
    )
    save()
    print(c["name"], "cancelled; late delivery cannot continue", flush=True)


def restart():
    p = OUT / "restarted.json"
    old = p.read_bytes() if p.exists() else None
    q.write_json(OUT / "command.json", {"action": "restart"})
    end = time.monotonic() + 180
    while not p.exists() or p.read_bytes() == old:
        if time.monotonic() > end:
            raise TimeoutError("restart")
        time.sleep(0.25)
    a.login(
        os.environ.get("ELSA_STUDIO_USER", "admin"),
        os.environ.get("ELSA_STUDIO_PASSWORD", "Password123!"),
    )
    return json.loads(p.read_text())


config = OUT / "workbench/shells.json"
original = config.read_bytes()
shells = json.loads(original)
f = shells["CShells"]["Shells"]["default"]["Features"][
    "ActivitiesDispatchWorkflowRuntime"
]
f["MaxNestingDepth"] = 1


def publish(name, root, options=None):
    d = a.request(
        "/design/workflows/definitions/submit",
        {
            "name": "Blocks recovery " + name + " " + uuid.uuid4().hex,
            "state": {
                "variables": [],
                "inputs": [],
                "outputs": [],
                "rootActivity": root,
                "strategyOptions": options or {"persistenceMode": "Balanced"},
            },
        },
    )
    p = a.request("/publishing/workflows/" + d["version"]["id"] + "/publish", {})
    return {"definitionId": d["definition"]["id"], "publication": p}


def dispatchnode(id, target):
    return q.node(
        id,
        dispatch,
        [
            expr("WorkflowDefinitionId", target["definitionId"]),
            expr("WaitForCompletion", True),
        ],
    )


recoveries = []
try:
    q.write_json(config, shells)
    report["restrictedCompositionSha256"] = q.digest(config)
    report["limitedRestart"] = restart()
    save()
    dispatch = a.activity(
        "Elsa.Activities.DispatchWorkflow.Runtime.Activities.DispatchWorkflow"
    )
    target = publish(
        "grandchild", seq("target-root", output("target-after", "target", "ok"))
    )
    for extracted in [False, True]:
        failing = dispatchnode("fault-child", target)
        child = publish(
            "child-extracted-" + str(extracted),
            seq(
                "root",
                seq("block", failing) if extracted else failing,
                output("after", "after", "continued"),
            ),
            {
                "persistenceMode": "Balanced",
                "incidentStrategy": {"alias": "ContinueWithIncidents", "version": "1"},
            },
        )
        outer = create(
            "dispatch-parent-extracted-" + str(extracted),
            seq("outer-root", dispatchnode("dispatch-child", child)),
        )
        end = time.monotonic() + 90
        while True:
            hits = a.request(
                "/runtime/workflows/instances?artifactId="
                + child["publication"]["artifactId"]
            )
            if len(hits) == 1:
                break
            if time.monotonic() > end:
                raise TimeoutError("child dispatch")
            time.sleep(0.25)
        instance = hits[0].get("instance", hits[0])
        c = {
            "name": "recover-extracted-" + str(extracted),
            "workflowExecutionId": instance["workflowExecutionId"],
            "outer": outer,
            "childDefinition": child,
            "target": target,
        }
        report["cases"].append(c)
        recoveries.append(c)
        save()
        c["faulted"] = awaitview(
            c,
            lambda x: any(
                n["executableNodeId"] == "fault-child" and n["status"] == "Faulted"
                for n in x["activities"]
            ),
        )
        assert c["faulted"]["instance"]["status"] == "Running"
        assert not any(
            n["executableNodeId"] == "after" for n in c["faulted"]["activities"]
        )
        save()
        print(
            c["name"],
            "native dispatch fault committed; parent still waiting",
            flush=True,
        )
    config.write_bytes(original)
    report["restoredCompositionSha256"] = q.digest(config)
    report["restoredRestart"] = restart()
    save()
    for c in recoveries:
        old = next(
            n
            for n in c["faulted"]["activities"]
            if n["executableNodeId"] == "fault-child"
        )
        c["reschedulePlan"] = plan(
            c,
            "RescheduleActivity",
            {"sourceActivityExecutionId": old["activityExecutionId"]},
        )
        save()
        c["recovered"] = awaitview(c, lambda x: x["instance"]["status"] == "Completed")
        assert c["recovered"]["outputs"]["after"]["value"]["preview"] == "continued"
        leaves = [
            n
            for n in c["recovered"]["activities"]
            if n["executableNodeId"] == "fault-child"
        ]
        assert len(leaves) == 2 and sorted(n["status"] for n in leaves) == [
            "Completed",
            "Superseded",
        ]
        assert (
            len(
                [
                    n
                    for n in c["recovered"]["activities"]
                    if n["executableNodeId"] == "after"
                ]
            )
            == 1
        )
        c["retainedIncidents"] = c["recovered"]["incidents"]
        assert len(c["retainedIncidents"]) == 1
        assert c["retainedIncidents"][0]["status"] == "Open"
        assert (
            c["retainedIncidents"][0]["incidentId"]
            == c["faulted"]["incidents"][0]["incidentId"]
        )
        assert all(
            n["status"] == "Completed"
            for n in c["recovered"]["activities"]
            if n["executableNodeId"] in ("root", "block")
        )
        expected_ancestors = (
            {"root", "block"} if c["name"].endswith("True") else {"root"}
        )
        ancestors = [
            n
            for n in c["recovered"]["activities"]
            if n["executableNodeId"] in expected_ancestors
        ]
        assert len(ancestors) == len(expected_ancestors)
        assert {n["executableNodeId"] for n in ancestors} == expected_ancestors
        c["outerCompleted"] = completed(c["outer"])
        save()
        print(
            c["name"],
            "child and outer workflow completed; original fault retained",
            flush=True,
        )
    print("Native fault/recovery proof complete.", flush=True)
finally:
    if config.read_bytes() != original:
        config.write_bytes(original)
        restart()

verify_owned_run(report["restoredRestart"]["processes"])
assert q.digest(config) == ready["compositionSha256"]
assert len(report["cases"]) == 22
assert len({case["name"] for case in report["cases"]}) == 22
report["status"] = "Passed"
save()
