"""Bounded observations of failed packaged author tests; never raw test/log export or acceptance."""
from __future__ import annotations

import hashlib
import json
import os
from pathlib import Path
import re
import stat
import subprocess
import xml.etree.ElementTree as ET

STAGES = {"authors-protocol-tests-test": "protocol", "authors-backend-tests-test": "backend"}
COUNTERS = {"total", "executed", "passed", "failed", "error", "timeout", "aborted", "inconclusive",
            "passedButRunAborted", "notRunnable", "notExecuted", "disconnected", "warning", "completed", "inProgress", "pending"}
ASSERTIONS = {"Equal", "NotEqual", "True", "False", "Null", "NotNull", "Empty", "NotEmpty", "Single", "Contains",
              "DoesNotContain", "All", "Collection", "IsType", "IsAssignableFrom", "Same", "NotSame", "Throws", "ThrowsAny", "Fail"}
TRX_LIMIT, LOG_LIMIT, SOURCE_LIMIT = 8 * 1024 * 1024, 2 * 1024 * 1024, 1024 * 1024
NAMESPACE = "{http://microsoft.com/schemas/VisualStudio/TeamTest/2010}"


def _read(root, path, limit):
    """Only regular owned files, with no symlink below the owned root, and bounded stable bytes."""
    root, path = Path(root).resolve(), Path(os.path.abspath(path))
    try:
        if not path.resolve().is_relative_to(root): return "unsafe", None
        cursor = path
        while True:
            if cursor.is_symlink(): return "unsafe", None
            if cursor.resolve() == root: break
            if cursor == cursor.parent: return "unsafe", None
            cursor = cursor.parent
        if not path.exists(): return "missing", None
        if not path.is_file(): return "unsafe", None
        descriptor = os.open(path, os.O_RDONLY | getattr(os, "O_NOFOLLOW", 0))
        with os.fdopen(descriptor, "rb") as stream:
            before = os.fstat(stream.fileno())
            if not stat.S_ISREG(before.st_mode): return "unsafe", None
            if before.st_size > limit: return "over-limit", None
            data = stream.read(limit + 1)
            after = os.fstat(stream.fileno())
        if len(data) > limit: return "over-limit", None
        if (before.st_dev, before.st_ino, before.st_size, before.st_mtime_ns) != (after.st_dev, after.st_ino, after.st_size, after.st_mtime_ns):
            return "changed", None
        return "available", data
    except OSError:
        return "unreadable", None


def _declarations(root, source):
    """The current fixtures use one public test class and file-scoped namespace per test file.

    This deliberately is not a C# parser: unsupported declarations yield unavailable
    identities, never display-name/InlineData fallbacks. Strings in attributes are not names.
    """
    declared = {}
    files = sorted(Path(source).glob("*.cs"))
    if len(files) > 64: return declared
    for path in files:
        status, data = _read(root, path, SOURCE_LIMIT)
        if status != "available": continue
        text = data.decode("utf-8-sig", errors="replace")
        namespaces = re.findall(r"(?m)^namespace ([A-Za-z_][A-Za-z0-9_.]*);\s*$", text)
        classes = re.findall(r"(?m)^public (?:sealed )?class ([A-Za-z_][A-Za-z0-9_]*Tests)\b", text)
        if len(namespaces) != 1 or len(classes) != 1: continue
        if len(namespaces[0] + "." + classes[0]) > 256: continue
        for match in re.finditer(r"(?m)^    public (?:async )?(?:Task|ValueTask|void) ([A-Za-z_][A-Za-z0-9_]*)\(", text):
            if len(match[1]) > 256: continue
            attributes = text[max(0, match.start() - 8192):match.start()].rsplit("\n\n", 1)[-1]
            if re.search(r"(?m)^    \[(?:Fact|Theory)(?:\([^\n]*\))?\]\s*$", attributes):
                declared[(namespaces[0] + "." + classes[0], match[1])] = path.resolve()
    return declared


def _command(report, stage, error):
    if stage not in STAGES: return {"termination": "unavailable", "exitCode": None}
    rows = report.get("stages", [])
    exit_code = rows[-1].get("exitCode") if rows and rows[-1].get("name") == stage else None
    if type(exit_code) is not int or not -2147483648 <= exit_code <= 2147483647: exit_code = None
    termination = ("timeout" if isinstance(error, subprocess.TimeoutExpired) else "interrupted" if isinstance(error, (InterruptedError, KeyboardInterrupt))
                   else "nonzero-exit" if exit_code not in (None, 0) else "post-test-check-failed" if exit_code == 0 else "unavailable")
    return {"termination": termination, "exitCode": exit_code}


def _compiler(root, path):
    status, data = _read(root, path, LOG_LIMIT)
    value = {"status": status, "errors": [], "warnings": [], "truncated": False}
    if data is not None:
        matches = re.findall(r"(?m)^.*(?:\(\d+,\d+\)|\.(?:csproj|props|targets))\s*:\s*(error|warning)\s+((?:CS|NU|MSB|NETSDK)\d{3,5})\b", data.decode("utf-8", errors="replace"))
        for label, key in [("error", "errors"), ("warning", "warnings")]:
            codes = sorted({code for level, code in matches if level == label})
            value[key] = codes[:8]
            value["truncated"] |= len(codes) > 8
    return value


def _trx(root, source, path):
    status, data = _read(root, path, TRX_LIMIT)
    empty = {"status": status, "counts": None, "failedMethods": [], "unavailableFailedIdentities": 0, "omittedFailedMethods": 0}
    if data is None: return empty
    empty["sha256"] = hashlib.sha256(data).hexdigest()
    try:
        text = data.decode("utf-8-sig")
    except UnicodeError:
        return dict(empty, status="unsupported-encoding")
    if "\x00" in text: return dict(empty, status="unsupported-encoding")
    if "<!DOCTYPE" in text.upper() or "<!ENTITY" in text.upper(): return dict(empty, status="malformed")
    try:
        document = ET.fromstring(text)
        if document.tag != NAMESPACE + "TestRun": raise ValueError()
        counters = document.find(NAMESPACE + "ResultSummary/" + NAMESPACE + "Counters")
        if counters is None or not {"total", "executed", "passed", "failed"} <= set(counters.attrib) <= COUNTERS: raise ValueError()
        if not all(re.fullmatch(r"\d{1,7}", item) for item in counters.attrib.values()): raise ValueError()
        counts = {key: int(item) for key, item in counters.attrib.items()}
        rows = document.findall(NAMESPACE + "Results/" + NAMESPACE + "UnitTestResult")
        if len(rows) > 10000 or len(rows) != counts["total"] or not counts["passed"] + counts["failed"] <= counts["executed"] <= counts["total"]: raise ValueError()
        if sum(number for key, number in counts.items() if key not in {"total", "executed"}) != counts["total"]: raise ValueError()
        ids = [row.get("executionId") for row in rows]
        if None in ids or "" in ids or len(ids) != len(set(ids)): raise ValueError()
        for outcome in {row.get("outcome") for row in rows}:
            key = outcome[0].lower() + outcome[1:] if isinstance(outcome, str) and outcome else ""
            if key not in COUNTERS - {"total", "executed"} or counts.get(key, 0) != sum(row.get("outcome") == outcome for row in rows): raise ValueError()
        if any(counts[key] != sum(row.get("outcome") == key.capitalize() for row in rows) for key in ("passed", "failed")): raise ValueError()
        definitions = {}
        units = document.findall(NAMESPACE + "TestDefinitions/" + NAMESPACE + "UnitTest")
        if len(units) > 10000: raise ValueError()
        for unit in units:
            method = unit.find(NAMESPACE + "TestMethod")
            if method is None or not unit.get("id") or unit.get("id") in definitions: raise ValueError()
            definitions[unit.get("id")] = (method.get("className"), method.get("name"))
        if any(row.get("testId") not in definitions for row in rows): raise ValueError()
    except ET.ParseError:
        return dict(empty, status="malformed")
    except ValueError:
        return dict(empty, status="inconsistent")
    declared = _declarations(root, source)
    failures, unavailable = {}, 0
    for row in rows:
        if row.get("outcome") != "Failed": continue
        identity = definitions[row.get("testId")]
        if identity not in declared:
            unavailable += 1
            continue
        item = failures.setdefault(identity, {"className": identity[0], "method": identity[1], "executions": 0, "assertionKinds": [], "frames": []})
        item["executions"] += 1
        message = row.findtext(NAMESPACE + "Output/" + NAMESPACE + "ErrorInfo/" + NAMESPACE + "Message", "")
        assertion = re.match(r"(?:Xunit\.Sdk\.[A-Za-z]+Exception\s*:\s*)?Assert\.([A-Za-z]+)\(\) Failure\b", message)
        kind = assertion[1] if assertion and assertion[1] in ASSERTIONS else "Unavailable"
        if kind not in item["assertionKinds"] and len(item["assertionKinds"]) < 4: item["assertionKinds"].append(kind)
        stack = row.findtext(NAMESPACE + "Output/" + NAMESPACE + "ErrorInfo/" + NAMESPACE + "StackTrace", "")
        for filename, number in re.findall(r"(?m)^\s*at [^\n]+? in ([^\n]+):line (\d{1,7})\s*$", stack):
            candidate = Path(filename)
            if not candidate.is_absolute() or not re.fullmatch(r"[A-Za-z_][A-Za-z0-9_]{0,127}\.cs", candidate.name) or not 0 < int(number) <= 1000000: continue
            try:
                if candidate.resolve() not in set(declared.values()): continue
                safe, _ = _read(root, candidate, SOURCE_LIMIT)
                frame = {"file": candidate.name, "line": int(number)}
                if safe == "available" and frame not in item["frames"] and len(item["frames"]) < 4: item["frames"].append(frame)
            except (OSError, ValueError, RuntimeError):
                continue
    selected = [failures[key] for key in sorted(failures)][:16]
    return dict(empty, status="valid", counts=counts, failedMethods=selected, unavailableFailedIdentities=unavailable,
                omittedFailedMethods=max(0, len(failures) - len(selected)))


def retain_test_failure(run, tag, source, error):
    """Best-effort diagnostic retention must never replace the original exception or set success."""
    try:
        stage = tag + "-test"
        author = STAGES[stage]
        if run.report.get("stage") != stage: raise ValueError()
        run.report["authors"][author]["status"] = "failed"
        if not isinstance(run.source, str) or not re.fullmatch(r"[a-f0-9]{40}", run.source): raise ValueError()
        value = {"kind": "safe-packaged-author-test-failure", "source": run.source, "author": author, "stage": stage,
                 "status": "failed", "diagnosticOnly": True, "qualificationComplete": False, "rawTrxExported": False,
                 "retention": "available", "command": _command(run.report, stage, error),
                 "compiler": _compiler(run.root, run.root / (stage + ".log")),
                 "trx": _trx(run.root, source, run.root / "author-evidence" / (tag + "-results") / "contracts.trx")}
        path = run.root / "author-evidence" / (tag + "-failure.json")
        # Refuse pre-existing files/symlinked parents; this path belongs only to this failed attempt.
        if path.exists() or path.is_symlink() or path.parent.is_symlink() or Path(run.root).is_symlink(): raise ValueError()
        path.parent.mkdir(parents=True, exist_ok=True)
        payload = (json.dumps(value, indent=2) + "\n").encode()
        with path.open("xb") as stream:
            stream.write(payload)
        run.report["authorTestFailureDiagnostic"] = {"path": str(path), "sha256": hashlib.sha256(payload).hexdigest()}
        run.save()
    except BaseException:
        try:
            stage = tag + "-test" if tag + "-test" in STAGES and run.report.get("stage") == tag + "-test" else None
            source_revision = run.source if isinstance(run.source, str) and re.fullmatch(r"[a-f0-9]{40}", run.source) else None
            run.report["authorTestFailureDiagnostic"] = {"kind": "safe-packaged-author-test-failure", "retention": "unavailable",
                "source": source_revision, "stage": stage, "author": STAGES.get(stage),
                "command": _command(run.report, stage, error)}
            run.save()
        except BaseException:
            pass


def load_test_failure(root, reference, source):
    """Project only the fixed, hash-bound producer schema; failure export follows no arbitrary graph."""
    unavailable = {"kind": "safe-packaged-author-test-failure", "status": "failed", "diagnosticOnly": True,
                   "qualificationComplete": False, "rawTrxExported": False, "retention": "unavailable"}
    if not isinstance(reference, dict) or not isinstance(source, str) or not re.fullmatch(r"[a-f0-9]{40}", source): return unavailable
    inline_stage = reference.get("stage")
    if reference.get("kind") == "safe-packaged-author-test-failure" and reference.get("retention") == "unavailable" \
            and reference.get("source") == source and isinstance(inline_stage, str) and inline_stage in STAGES \
            and reference.get("author") == STAGES[inline_stage]:
        try:
            return _project(reference, {})
        except (ValueError, TypeError, AttributeError): return unavailable
    for stage, author in STAGES.items():
        expected = Path(root) / "author-evidence" / (stage.removesuffix("-test") + "-failure.json")
        if not isinstance(reference.get("path"), str): continue
        try:
            path = Path(reference["path"])
            if not path.is_absolute() or path.resolve() != expected.resolve(): continue
            status, data = _read(root, path, SOURCE_LIMIT)
        except (ValueError, RuntimeError, OSError): return unavailable
        if status != "available" or hashlib.sha256(data).hexdigest() != reference.get("sha256"): return unavailable
        try:
            def unique(pairs):
                result = dict(pairs)
                if len(result) != len(pairs): raise ValueError()
                return result
            value = json.loads(data, object_pairs_hook=unique)
            # Replay the bounded projection, never trust extra JSON fields or display names.
            if not isinstance(value, dict) or value.get("kind") != "safe-packaged-author-test-failure" or value.get("source") != source or value.get("stage") != stage or value.get("author") != author: return unavailable
            return _project(value, _declarations(root, Path(root) / ("author-" + author + "-tests")))
        except (ValueError, UnicodeError, TypeError, AttributeError, OSError): return unavailable
    return unavailable


def _project(value, declared):
    """Safe scalar fields only, including when the retained JSON was extended or malformed."""
    result = {key: value[key] for key in ("kind", "source", "author", "stage")}
    result.update(kind="safe-packaged-author-test-failure", status="failed", diagnosticOnly=True, qualificationComplete=False,
                  rawTrxExported=False, retention="unavailable" if value.get("retention") == "unavailable" else "available")
    command = value.get("command", {})
    code = command.get("exitCode")
    result["command"] = {"termination": command.get("termination") if command.get("termination") in
        {"timeout", "interrupted", "nonzero-exit", "post-test-check-failed", "unavailable"} else "unavailable",
        "exitCode": code if type(code) is int and -2147483648 <= code <= 2147483647 else None}
    compiler = value.get("compiler", {})
    states = {"available", "missing", "unsafe", "over-limit", "changed", "unreadable", "unavailable"}
    result["compiler"] = {"status": compiler.get("status") if compiler.get("status") in states else "unavailable",
        **{key: sorted({code for code in compiler.get(key, []) if isinstance(code, str) and re.fullmatch(r"(?:CS|NU|MSB|NETSDK)\d{3,5}", code)})[:8]
           for key in ("errors", "warnings")}, "truncated": compiler.get("truncated") is True}
    trx = value.get("trx", {})
    counts = trx.get("counts")
    result["trx"] = {"status": trx.get("status") if trx.get("status") in states | {"valid", "malformed", "inconsistent", "unsupported-encoding"} else "unavailable",
        "counts": {key: number for key, number in counts.items() if key in COUNTERS and type(number) is int and 0 <= number <= 1000000} if isinstance(counts, dict) else None,
        "failedMethods": [], **{key: trx.get(key) if type(trx.get(key)) is int and 0 <= trx[key] <= 10000 else 0
                               for key in ("unavailableFailedIdentities", "omittedFailedMethods")}}
    if isinstance(trx.get("sha256"), str) and re.fullmatch(r"[a-f0-9]{64}", trx["sha256"]): result["trx"]["sha256"] = trx["sha256"]
    for row in trx.get("failedMethods", [])[:16]:
        if not isinstance(row, dict) or (row.get("className"), row.get("method")) not in declared: continue
        if type(row.get("executions")) is not int or not 0 < row["executions"] <= 10000: continue
        result["trx"]["failedMethods"].append({"className": row["className"], "method": row["method"], "executions": row["executions"],
            "assertionKinds": [kind for kind in row.get("assertionKinds", []) if kind in ASSERTIONS | {"Unavailable"}][:4],
            "frames": [{"file": frame["file"], "line": frame["line"]} for frame in row.get("frames", []) if isinstance(frame, dict)
                       and frame.get("file") in {path.name for path in declared.values()}
                       and isinstance(frame.get("file"), str) and re.fullmatch(r"[A-Za-z_][A-Za-z0-9_]{0,127}\.cs", frame["file"])
                       and type(frame.get("line")) is int and 0 < frame["line"] <= 1000000][:4]})
    return result
