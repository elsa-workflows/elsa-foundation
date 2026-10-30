"""Index retained native observations without turning an unrelated failure into a control.

These helpers validate driver evidence. They do not execute a tool, simulate a
provider, or infer an owning effect from transport success.
"""
from __future__ import annotations

import copy
import json
from pathlib import Path
from urllib.parse import urlparse

from agent_transport import _json as strict_json
from artifacts import check, sha
from evidence import CONTROL_IDS, REQUIRED_CASES, UNCHANGED_CONTROLS
from runtime import private_json


def retained(path):
    path = Path(path)
    return {"path": str(path), "sha256": sha(path)}


class CaseRecords:
    def __init__(self, run, expected_controls):
        check(set(expected_controls) == CONTROL_IDS, "Every refusal needs a driver-owned expected cause")
        self.run, self.expected = run, copy.deepcopy(expected_controls)
        self.root = run.root / "cases"
        self.root.mkdir(mode=0o700)

    def add(self, name, observation):
        """Wrap a completed observation; the original remains separate and unchanged."""
        check(name in REQUIRED_CASES and not any(row["id"] == name for row in self.run.report["cases"]),
              "Unknown or duplicate witness case: " + name)
        control = name in CONTROL_IDS
        status = "failed" if control else "passed"
        check(observation.get("status") == status and not observation.get("failure"),
              "An unexpected or unfinished observation cannot qualify: " + name)
        check(all(observation.get("observations", {}).get(key) is True for key in REQUIRED_CASES[name]),
              "Actual required observations are missing: " + name)
        record = copy.deepcopy(observation)
        if control:
            check(record.get("expectedFailureObserved") is True, "The expected refusal was not observed: " + name)
            actual = record.get("actual", {"stage": record.get("stage"), "diagnostic": record.get("diagnostic")})
            expected = self.expected[name]
            diagnostics = expected["diagnostic"] if isinstance(expected["diagnostic"], list) else [expected["diagnostic"]]
            check(actual["stage"] == expected["stage"] and actual["diagnostic"] in diagnostics,
                  "Wrong-cause refusal cannot qualify: " + name)
            record.update(stage=actual["stage"], diagnostic=actual["diagnostic"])
        else:
            check(not record.get("expectedFailureObserved"), "A positive journey cannot be an expected failure")
        refs = [record["ownerBefore"], record["ownerAfter"], *record["evidence"]]
        check(bool(record["evidence"]), "Owner markers alone cannot qualify a case")
        for ref in refs:
            path = Path(ref["path"])
            check(path.is_absolute() and path.resolve().is_relative_to(self.run.root.resolve())
                  and path.is_file() and sha(path) == ref["sha256"], "Missing or changed actual evidence: " + str(path))
        if name in UNCHANGED_CONTROLS:
            check(record["ownerBefore"]["sha256"] == record["ownerAfter"]["sha256"],
                  "A pre-effect control changed its owner: " + name)
        record.update(id=name, source=self.run.source)
        path = self.root / (name + ".json")
        check(not path.exists(), "Never overwrite a retained case")
        private_json(path, record)
        entry = {"id": name, "source": self.run.source, "status": status, "receipt": retained(path)}
        if control:
            entry["expectedFailureObserved"] = True
        self.run.report["cases"].append(entry)
        self.run.save()
        return entry


def _normalized_result(value):
    """The native DTO and adapter use different casing for the same generic enum."""
    result = copy.deepcopy(value)
    if result.get("receipt"):
        result["receipt"]["scope"] = result["receipt"]["scope"].lower()
    return result


def _same_json(left, right):
    # Python considers True == 1 and False == 0. Opaque JSON must not acquire
    # those coercions while crossing the evidence boundary.
    return json.dumps(left, sort_keys=True, separators=(",", ":"), allow_nan=False) == json.dumps(
        right, sort_keys=True, separators=(",", ":"), allow_nan=False)


def correlate_browser_protocol(transport, browser_output, provider, *, offset=0):
    """Match actual advertised calls/results, never model text or domain payload fields.

    Command IDs identify the generic fixture request. Native tool-call IDs identify
    owning results; they need not equal SDK call IDs. Copilot additionally supplies
    its native session identity. Anthropic supplies FunctionResult call correlation.
    """
    check(provider in ("copilot", "anthropic"), "Unknown actual adapter")
    output = Path(browser_output)
    trace = transport.protocol_trace(offset)
    check(trace["incompleteTailBytes"] == 0, "A partial protocol record cannot prove a completed journey")
    commands = [json.loads(path.read_text()) for path in output.glob("command-*.json")]
    check(bool(commands) and len({item["commandId"] for item in commands}) == len(commands),
          "Actual distinct browser commands are required")
    requests = json.loads((output / "agent-requests.json").read_text())
    sessions = {urlparse(item["url"]).path.split("/")[-2] for item in requests
                if urlparse(item["url"]).path.endswith("/messages")}
    check(bool(sessions), "The browser did not send a native message")
    # These are real accepted execute/ACK responses, not an expected domain model.
    accepted = []
    for path in output.glob("*.json"):
        value = json.loads(path.read_text())
        if isinstance(value, dict):
            response = value.get("response")
            data = response.get("data") if isinstance(response, dict) else None
            if (type(value.get("status")) is int and 200 <= value["status"] < 300
                    and isinstance(response, dict) and response.get("error") is None and isinstance(data, dict)
                    and data.get("executed") is True and isinstance(data.get("toolResult"), dict)):
                accepted.append(data["toolResult"])
    correlations = []
    for command in commands:
        rows = [row for row in trace["records"] if row.get("data", {}).get("commandId") == command["commandId"]]
        offered = [row["data"] for row in rows if row["kind"] == provider + ".request" and row["data"]["tools"]]
        calls = [row["data"] for row in rows if row["kind"] == provider + ".call"]
        check(len(offered) == len(calls) == 1, "The actual adapter did not advertise and call exactly once")
        check(sum(tool["name"] == command["toolName"] for tool in offered[0]["tools"]) == 1
              and calls[0]["toolName"] == command["toolName"] and _same_json(calls[0]["arguments"], command["arguments"]),
              "Actual adapter advertisement/call differs from the browser command")
        if provider == "copilot":
            check(offered[0]["sessionId"] in sessions, "The SDK call belongs to another native session")
            returned = [row["data"]["result"] for row in rows if row["kind"] == "copilot.return"]
            check(len(returned) == 1, "The actual SDK function did not return once")
        continuations = [row["data"] for row in rows if row["kind"] == provider + ".continuation"]
        if continuations:
            check(len(continuations) == 1, "The actual adapter received duplicate continuations")
            if provider == "copilot":
                check(continuations[0]["sessionId"] == offered[0]["sessionId"], "Continuation session changed")
                results = continuations[0]["results"]
            else:
                items = continuations[0]["results"]
                check(len(items) == 1 and items[0]["kind"] == "functionResult"
                      and items[0]["callId"] == command["commandId"], "Anthropic function-result correlation changed")
                # The actual Anthropic adapter puts its serialized generic tool
                # result in FunctionResultContent as a string. Decode that envelope
                # once; module payload data remains opaque.
                check(isinstance(items[0]["result"], str), "Anthropic did not return its serialized tool-result envelope")
                try:
                    results = [strict_json(items[0]["result"])]
                except (ValueError, UnicodeError, TypeError):
                    check(False, "Anthropic returned an invalid serialized tool-result envelope")
        else:
            check(provider == "copilot", "Anthropic did not receive its actual function result")
            results = returned
        check(len(results) == 1 and isinstance(results[0], dict)
              and results[0].get("toolName") == command["toolName"] and results[0].get("toolCallId"),
              "The actual adapter result does not identify the native tool call")
        result = results[0]
        check(result.get("succeeded") is True, "A refused outcome cannot qualify a positive journey")
        matches = [value for value in accepted if value["toolCallId"] == result["toolCallId"]]
        if result.get("receipt"):
            check(bool(matches) and all(_same_json(_normalized_result(value), _normalized_result(result)) for value in matches),
                  "Adapter continuation differs from the actual execute/ACK result")
        else:
            check(result.get("succeeded") is True, "A refused read cannot qualify a positive journey")
        correlations.append({"commandId": command["commandId"], "toolName": command["toolName"],
                             "nativeToolCallId": result["toolCallId"], "actualAdvertisement": True,
                             "oneActualCall": True, "actualResultObserved": True})
    path = output / "adapter-correlation.json"
    check(not path.exists(), "Never overwrite an actual adapter correlation")
    private_json(path, {"provider": provider, "evidenceCategory": "deterministic-provider-protocol", "liveProvider": False,
                        "correlations": correlations, "protocolTrace": trace["receipt"]})
    return {"receipt": retained(path), "trace": trace["receipt"], "nextOffset": trace["nextOffset"]}
