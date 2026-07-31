#!/usr/bin/env python3
"""Diff the Elsa 3 and Elsa 4 activity contract surfaces.

Emits a markdown parity table plus a machine-readable findings document.
Member-level renames are declared explicitly so a rename is reported as a
rename, not as "missing in Elsa 4 + extra in Elsa 4".
"""
import json
import os
import sys

HERE = os.environ.get("ELSA_PARITY_DIR") or os.path.abspath(os.path.join(
    os.path.dirname(__file__), "..", "..", "docs", "reports", "evidence", "activity-contract-parity"))

# Elsa 3 activity -> Elsa 4 activity. None means "no Elsa 4 counterpart".
ACTIVITY_MAP = {
    # control flow / primitives
    "If": "If",
    "Switch": "Switch",
    "ForEach": "ForEach",
    "For": "For",
    "While": "While",
    "Parallel": "Parallel",
    "Sequence": "Sequence",
    "Flowchart": "Flowchart",
    "Break": "Break",
    "Fault": "Fault",
    "Inline": "Inline",
    "ReadLine": "ReadLine",
    "WriteLine": "WriteLine",
    "ParallelForEach": None,
    "StateMachine": None,
    "DynamicActivity": "GraphActivity",
    # flow-model activities removed with the flow-activity model
    "FlowDecision": "If",
    "FlowSwitch": "Switch",
    "FlowFork": "Parallel",
    "FlowJoin": "Parallel",
    # moved to engine intrinsics (ADR 0045)
    "SetVariable": "@intrinsic:Set",
    "SetName": "@intrinsic:SetInstanceName",
    "Correlate": "@intrinsic:SetCorrelationId",
    "Finish": "@intrinsic:Finish",
    "Complete": "@intrinsic:Finish",
    # http
    "HttpEndpoint": "HttpEndpoint",
    "SendHttpRequest": "SendHttpRequest",
    "FlowSendHttpRequest": "SendHttpRequest",
    "WriteHttpResponse": "WriteHttpResponse",
    "DownloadHttpFile": None,
    "WriteFileHttpResponse": None,
    # scheduling
    "Delay": "Delay",
    "Timer": "Timer",
    "Cron": "Cron",
    "StartAt": None,
    # scripting
    "RunJavaScript": "RunJavaScript",
    # runtime / dispatch
    "DispatchWorkflow": "DispatchWorkflow",
    "ExecuteWorkflow": "DispatchWorkflow",
    "BulkDispatchWorkflows": None,
    "Event": "Event",
    "PublishEvent": "PublishEvent",
    "RunTask": None,
}

# (elsa4_activity, elsa3_member) -> elsa4_member. Declared renames, so they are
# not double-counted as a removal plus an addition.
MEMBER_RENAMES = {
    ("Delay", "TimeSpan"): "Duration",
    ("Cron", "CronExpression"): "Expression",
    ("ForEach", "Items"): "Collection",
    ("For", "OuterBoundInclusive"): "EndInclusive",
    ("SendHttpRequest", "ParsedContent"): "ResponseBody",
    ("WriteHttpResponse", "Content"): "Body",
    ("WriteHttpResponse", "ResponseHeaders"): "Headers",
    ("DispatchWorkflow", "Input"): "Inputs",
    ("RunJavaScript", "Result"): "Value",
}

# Members that exist only as an artefact of the Elsa 3 base-class shape, or that
# Elsa 4 deliberately dropped. Excluded from the "missing" count with a reason.
IGNORED_E3_MEMBERS = {
    "Result": "Elsa 3 base-class Output<T> Result; Elsa 4 models activity results as a typed result record",
    "EnableResiliency": "obsolete in Elsa 3 itself (superseded by the common Resilience Strategy setting)",
}

# (elsa4_activity, elsa3_member) -> reason. Verified by reading both sources: the
# capability exists in Elsa 4 through a different mechanism, so it is a divergence
# rather than a gap. Kept explicit so the reasoning is reviewable.
INTENTIONAL_DIVERGENCE = {
    ("ForEach", "CurrentValue"):
        "Elsa 4 exposes the loop item as the scoped variable `currentItem` "
        "(ForEach.CurrentItemVariableName), not as an output projection",
    ("For", "CurrentValue"):
        "Elsa 4 exposes the loop counter as the scoped variable `index` "
        "(For.IndexVariableName), not as an output projection",
    ("HttpEndpoint", "Headers"):
        "carried by the Elsa 4 `Request` output (HttpRequestModel.Headers)",
    ("HttpEndpoint", "QueryStringData"):
        "carried by the Elsa 4 `Request` output (HttpRequestModel.Query)",
    ("Parallel", "Branches"):
        "Elsa 4 models branches as authored child slots on the Parallel structure, not as an input",
    ("Switch", "Output"):
        "Elsa 4 Switch routes on case outcome ports; the matched value stays on the Value input",
}


# Elsa 3 types that are engine/composition internals rather than author-facing library
# activities. They have no toolbox entry to reach parity with, so they are excluded.
NOT_AUTHOR_FACING = {
    "Workflow": "root workflow composition type, not a toolbox activity",
    "CompositeWithResult": "base composition type",
    "NotFoundActivity": "runtime fallback for an unresolvable activity type",
    "Start": "flowchart start marker of the removed flow-activity model",
    "End": "flowchart end marker of the removed flow-activity model",
}


def load(name):
    with open(os.path.join(HERE, name), "r", encoding="utf-8") as fh:
        return json.load(fh)


def index4(rows):
    return {r["class"]: r for r in rows}


def index3(rows):
    out = {}
    for r in rows:
        if r["isAbstract"]:
            continue
        # Prefer the richer duplicate when a name appears twice (generic overloads).
        prev = out.get(r["class"])
        if prev is None or (len(r["inputs"]) + len(r["outputs"])) > (len(prev["inputs"]) + len(prev["outputs"])):
            out[r["class"]] = r
    return out


def names(members):
    return [m["name"] for m in members]


def compare(e3, e4):
    findings = []
    rows = []

    e3i, e4i = index3(e3), index4(e4)
    mapped4 = set()

    for e3name, reason in sorted(NOT_AUTHOR_FACING.items()):
        rows.append({"elsa3": e3name, "elsa4": "n/a", "verdict": "not-author-facing",
                     "detail": reason, "divergences": []})

    for e3name in sorted(ACTIVITY_MAP):
        target = ACTIVITY_MAP[e3name]
        src = e3i.get(e3name)
        if src is None:
            findings.append({
                "kind": "baseline-miss",
                "elsa3": e3name,
                "detail": "declared in the map but not found by the Elsa 3 extractor",
            })
            continue

        if target is None:
            rows.append({
                "elsa3": e3name, "elsa4": "—", "verdict": "missing",
                "detail": "no Elsa 4 counterpart",
            })
            findings.append({
                "kind": "missing-activity", "elsa3": e3name, "elsa4": None,
                "detail": f"Elsa 3 `{e3name}` ({src['module']}) has no Elsa 4 activity",
                "e3Inputs": names(src["inputs"]), "e3Outputs": names(src["outputs"]),
                "e3Outcomes": src["outcomes"] + src["dynamicOutcomes"],
            })
            continue

        if target.startswith("@intrinsic:"):
            rows.append({
                "elsa3": e3name, "elsa4": target.split(":", 1)[1] + " (intrinsic)",
                "verdict": "moved-to-intrinsic",
                "detail": "engine intrinsic per ADR 0045, not a CLR activity",
            })
            continue

        dst = e4i.get(target)
        if dst is None:
            rows.append({"elsa3": e3name, "elsa4": target, "verdict": "missing",
                         "detail": "mapped Elsa 4 activity not found"})
            continue
        mapped4.add(target)

        def diff(kind):
            src_members = src["inputs"] if kind == "input" else src["outputs"]
            dst_members = dst["inputs"] if kind == "input" else dst["outputs"]
            dst_names = set(names(dst_members))
            missing, renamed, diverged = [], [], []
            for m in src_members:
                n = m["name"]
                if n in IGNORED_E3_MEMBERS:
                    continue
                new = MEMBER_RENAMES.get((target, n))
                if new and new in dst_names:
                    renamed.append((n, new))
                    continue
                if n in dst_names:
                    continue
                if (target, n) in INTENTIONAL_DIVERGENCE:
                    diverged.append((n, INTENTIONAL_DIVERGENCE[(target, n)]))
                    continue
                missing.append(n)
            renamed_from = {a for a, _ in renamed}
            src_names = {m["name"] for m in src_members} | {b for _, b in renamed}
            added = [n for n in names(dst_members) if n not in src_names and n not in renamed_from]
            return missing, renamed, added, diverged

        mi, ri, ai, di = diff("input")
        mo, ro, ao, do = diff("output")

        e3_outcomes = [o for o in src["outcomes"] + src["dynamicOutcomes"]]
        e4_outcomes = list(dst["outcomes"])
        if dst.get("valueOutcomes"):
            e4_outcomes.append(f"<dynamic from {dst['valueOutcomes'].get('source')}>")
        missing_outcomes = [o for o in dict.fromkeys(e3_outcomes) if o not in e4_outcomes]

        verdict = "present" if not (mi or mo or missing_outcomes) else "gap"
        rows.append({
            "elsa3": e3name, "elsa4": target, "verdict": verdict,
            "detail": "; ".join(filter(None, [
                f"inputs missing: {', '.join(mi)}" if mi else "",
                f"outputs missing: {', '.join(mo)}" if mo else "",
                f"outcomes missing: {', '.join(missing_outcomes)}" if missing_outcomes else "",
                f"renamed: {', '.join(f'{a}->{b}' for a, b in ri + ro)}" if (ri or ro) else "",
                f"divergence: {', '.join(a for a, _ in di + do)}" if (di or do) else "",
                f"new in Elsa 4: {', '.join(ai + ao)}" if (ai or ao) else "",
            ])) or "full parity",
            "divergences": [{"member": a, "reason": b} for a, b in di + do],
        })

        if mi:
            findings.append({"kind": "missing-inputs", "elsa3": e3name, "elsa4": target, "members": mi})
        if mo:
            findings.append({"kind": "missing-outputs", "elsa3": e3name, "elsa4": target, "members": mo})
        if missing_outcomes:
            findings.append({"kind": "missing-outcomes", "elsa3": e3name, "elsa4": target,
                             "members": missing_outcomes})

    e4_only = sorted(set(e4i) - mapped4)
    for name in e4_only:
        rows.append({"elsa3": "—", "elsa4": name, "verdict": "elsa4-only",
                     "detail": "no Elsa 3 counterpart in the mapped modules"})

    return rows, findings


def main():
    e3, e4 = load("elsa3-surface.json"), load("elsa4-surface.json")
    rows, findings = compare(e3, e4)

    order = {"gap": 0, "missing": 1, "moved-to-intrinsic": 2, "present": 3, "elsa4-only": 4, "not-author-facing": 5}
    rows.sort(key=lambda r: (order.get(r["verdict"], 9), r["elsa3"], r["elsa4"]))

    with open(os.path.join(HERE, "parity-findings.json"), "w", encoding="utf-8") as fh:
        json.dump({"rows": rows, "findings": findings}, fh, indent=2)

    print("| Elsa 3 | Elsa 4 | Verdict | Detail |")
    print("|---|---|---|---|")
    for r in rows:
        print(f"| `{r['elsa3']}` | `{r['elsa4']}` | {r['verdict']} | {r['detail']} |")

    print("\n\n### Finding counts\n", file=sys.stderr)
    from collections import Counter
    for kind, n in Counter(f["kind"] for f in findings).most_common():
        print(f"{kind}: {n}", file=sys.stderr)


if __name__ == "__main__":
    main()
