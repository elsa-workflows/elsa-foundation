#!/usr/bin/env python3
"""Extract the Elsa 4 (elsa-foundation) activity contract surface from C# source.

Mirrors extract_elsa3.py so the two documents can be diffed directly. Reads the
attribute model in src/Elsa/Activities/Runtime/Core/Attributes/:
ActivityInput, Output, ActivityOutcome, ActivityValueOutcomes, Required,
TriggerActivity, ActivityChildSlot.
"""
import json
import os
import re
import sys

REPO = os.environ.get("ELSA_FOUNDATION_ROOT") or os.path.abspath(
    os.path.join(os.path.dirname(__file__), "..", ".."))
ROOT = os.path.join(REPO, "src", "Elsa", "Activities")

CLASS_TOKEN_RE = re.compile(
    r"(?:^|\n)\s*(?:public|internal)\s+(?:sealed\s+|abstract\s+|partial\s+|static\s+)*"
    r"class\s+(?P<name>\w+)"
)
RECORD_TOKEN_RE = re.compile(
    r"(?:^|\n)\s*(?:public|internal)\s+(?:sealed\s+|abstract\s+|partial\s+)*record\s+(?P<name>\w+)"
)

INPUT_RE = re.compile(
    r"\[ActivityInput(?P<args>\((?:[^()]|\((?:[^()]|\([^()]*\))*\))*\))?\]\s*"
    r"(?P<extra>(?:\[[^\]]*\]\s*)*)"
    r"public\s+(?P<type>[^;{]+?)\s+(?P<name>\w+)\s*\{\s*get",
    re.DOTALL,
)
OUTPUT_PROP_RE = re.compile(
    r"\[Output(?P<args>\((?:[^()]|\((?:[^()]|\([^()]*\))*\))*\))?\]\s*"
    r"(?:\[[^\]]*\]\s*)*"
    r"public\s+(?P<type>[^;{]+?)\s+(?P<name>\w+)\s*\{\s*get",
    re.DOTALL,
)
OUTPUT_PARAM_ATTR_RE = re.compile(
    r"^\s*\[property:\s*Output(?P<args>\((?:[^()]|\((?:[^()]|\([^()]*\))*\))*\))?\s*\]\s*(?P<decl>.*)$",
    re.DOTALL,
)

# Matches both [ActivityOutcome(x)] and the grouped form [ ActivityOutcome(x), ActivityOutcome(y) ].
OUTCOME_RE = re.compile(r"ActivityOutcome\((?P<args>(?:[^()]|\([^()]*\))*)\)")
VALUE_OUTCOMES_RE = re.compile(r"\[ActivityValueOutcomes\((?P<args>(?:[^()]|\([^()]*\))*)\)\]")
CHILD_SLOT_RE = re.compile(r"\[ActivityChildSlot\((?P<args>(?:[^()]|\([^()]*\))*)\)\]")
TRIGGER_RE = re.compile(r"\[TriggerActivity[\(\]]")
RESUME_TARGET_RE = re.compile(r"\[ResumeTarget[\(\]]")
STRING_RE = re.compile(r'"((?:[^"\\]|\\.)*)"')
NAMEOF_RE = re.compile(r"nameof\(\s*([\w.]+)\s*\)")
CONST_REF_RE = re.compile(r"\b([A-Z]\w+)\.(\w+)\b")
CONST_DECL_RE = re.compile(
    r"(?:^|\n)\s*(?:public|internal)\s+const\s+string\s+(?P<name>\w+)\s*=\s*\"(?P<value>(?:[^\"\\]|\\.)*)\""
)

# Base types that make a class a runnable, author-facing activity.
ACTIVITY_BASE_HEADS = {
    "Activity", "CodeActivity", "StructuralActivity",
    "StatefulActivity", "StatefulTriggerActivity",
}
# The abstract bases themselves live in Elsa.Activities.Runtime.Core and are not activities.
ABSTRACT_BASE_NAMES = ACTIVITY_BASE_HEADS


def balanced(text: str, start: int, opener: str, closer: str) -> str:
    i = text.find(opener, start)
    if i < 0:
        return ""
    depth = 0
    for j in range(i, len(text)):
        if text[j] == opener:
            depth += 1
        elif text[j] == closer:
            depth -= 1
            if depth == 0:
                return text[i:j + 1]
    return text[i:]


def split_top_level(s: str, sep=","):
    parts, depth, buf = [], 0, []
    for ch in s:
        if ch in "<([{":
            depth += 1
        elif ch in ">)]}":
            depth -= 1
        if ch == sep and depth == 0:
            parts.append("".join(buf))
            buf = []
        else:
            buf.append(ch)
    parts.append("".join(buf))
    return [p.strip() for p in parts if p.strip()]


def class_header(text: str, decl_end: int):
    """Return (bases, body_start) for a class/record declaration starting at decl_end."""
    i = decl_end
    depth = 0
    bases_start = None
    while i < len(text):
        ch = text[i]
        if ch in "<([":
            depth += 1
        elif ch in ">)]":
            depth -= 1
        elif ch == ":" and depth == 0 and bases_start is None:
            bases_start = i + 1
        elif ch == "{" and depth == 0:
            return (text[bases_start:i].strip() if bases_start else ""), i
        elif ch == ";" and depth == 0:
            return (text[bases_start:i].strip() if bases_start else ""), i
        i += 1
    return "", len(text)


def preceding_attributes(text: str, start: int) -> str:
    """Grab the attribute block above a declaration, including multi-line [ ... ] lists."""
    head = text[:start].rstrip()
    collected = []
    while True:
        stripped = head.rstrip()
        if stripped.endswith("]"):
            # Walk back to the matching '['.
            depth = 0
            k = len(stripped) - 1
            while k >= 0:
                if stripped[k] == "]":
                    depth += 1
                elif stripped[k] == "[":
                    depth -= 1
                    if depth == 0:
                        break
                k -= 1
            if k < 0:
                break
            collected.append(stripped[k:])
            head = stripped[:k]
            continue
        # Skip doc comments / plain comments between attributes.
        lines = stripped.splitlines()
        if lines and (lines[-1].strip().startswith("///") or lines[-1].strip().startswith("//")):
            head = "\n".join(lines[:-1])
            continue
        break
    return "\n".join(reversed(collected))


def build_constants(repo_root: str) -> dict:
    """Map 'ClassName.MemberName' -> literal string for const string declarations."""
    consts = {}
    for dirpath, dirnames, filenames in os.walk(os.path.join(repo_root, "src")):
        dirnames[:] = [d for d in dirnames if d not in ("bin", "obj")]
        for fn in filenames:
            if not fn.endswith(".cs"):
                continue
            path = os.path.join(dirpath, fn)
            with open(path, "r", encoding="utf-8", errors="replace") as fh:
                text = fh.read()
            for cm in CLASS_TOKEN_RE.finditer(text):
                cls = cm.group("name")
                _bases, body_start = class_header(text, cm.end())
                body = balanced(text, body_start, "{", "}")
                for dm in CONST_DECL_RE.finditer(body):
                    consts.setdefault(f"{cls}.{dm.group('name')}", dm.group("value"))
    return consts


CONSTANTS: dict = {}
# Result records are resolved globally: an activity's Activity<TResult> often lives in a
# different file from the record that declares its [property: Output] projections.
RECORDS: dict = {}


def literals(args: str):
    """String literals, nameof(...) targets and resolved const references, in order."""
    out = []
    out += STRING_RE.findall(args or "")
    out += [n.split(".")[-1] for n in NAMEOF_RE.findall(args or "")]
    for cls, member in CONST_REF_RE.findall(args or ""):
        key = f"{cls}.{member}"
        if key in CONSTANTS:
            out.append(CONSTANTS[key])
    return out


def parse_named_args(args: str) -> dict:
    out = {}
    if not args:
        return out
    for key in ("Key", "Path", "DefaultValue", "DefaultSyntax", "DisplayName",
                "Description", "Category", "UIHint", "UnmatchedOutcome", "Name"):
        m = re.search(key + r"\s*=\s*" + r'"((?:[^"\\]|\\.)*)"', args)
        if m:
            out[key] = m.group(1)
            continue
        m = re.search(key + r"\s*=\s*nameof\(\s*([\w.]+)\s*\)", args)
        if m:
            out[key] = m.group(1).split(".")[-1]
            continue
        m = re.search(key + r"\s*=\s*([A-Z]\w+\.\w+)", args)
        if m and m.group(1) in CONSTANTS:
            out[key] = CONSTANTS[m.group(1)]
    m = re.search(r"Options\s*=\s*(?:new\[\]\s*)?[\[\{](?P<o>[^\]\}]*)[\]\}]", args, re.DOTALL)
    if m:
        out["Options"] = STRING_RE.findall(m.group("o"))
    for flag in ("IsRequired", "HasSourceRepresentation"):
        m = re.search(flag + r"\s*=\s*(true|false)", args)
        if m:
            out[flag] = m.group(1) == "true"
    return out


def split_decl(decl: str):
    """Split 'IDictionary<string, string[]>? ResponseHeaders' into (type, name)."""
    decl = decl.strip().rstrip(",")
    parts = split_top_level(decl, " ")
    if len(parts) < 2:
        # Fall back: last whitespace-separated token at depth 0.
        toks = decl.split()
        return (" ".join(toks[:-1]), toks[-1]) if len(toks) > 1 else (decl, decl)
    return " ".join(parts[:-1]), parts[-1]


def collect_outputs(scope: str):
    outs = []
    for om in OUTPUT_PROP_RE.finditer(scope):
        entry = {"name": om.group("name"), "type": " ".join(om.group("type").split())}
        entry.update(parse_named_args(om.group("args") or ""))
        outs.append(entry)
    return outs


def index_records(path: str):
    """Record every record/class that declares [Output] projections, keyed by type name."""
    with open(path, "r", encoding="utf-8", errors="replace") as fh:
        text = fh.read()

    for rm in list(RECORD_TOKEN_RE.finditer(text)) + list(CLASS_TOKEN_RE.finditer(text)):
        name = rm.group("name")
        outs = []
        params = balanced(text, rm.end(), "(", ")")
        # Only treat a parenthesised block as a positional parameter list when it
        # opens before the type's body.
        _bases, body_start = class_header(text, rm.end())
        if params and text.find("(", rm.end()) < body_start:
            inner = params.strip()
            inner = inner[1:-1] if inner.startswith("(") else inner
            for param in split_top_level(inner):
                om = OUTPUT_PARAM_ATTR_RE.match(param)
                if not om:
                    continue
                ptype, pname = split_decl(om.group("decl"))
                entry = {"name": pname, "type": ptype}
                entry.update(parse_named_args(om.group("args") or ""))
                outs.append(entry)
        outs.extend(collect_outputs(balanced(text, body_start, "{", "}")))
        if outs:
            RECORDS.setdefault(name, outs)


def scan_file(path: str):
    with open(path, "r", encoding="utf-8", errors="replace") as fh:
        text = fh.read()

    module = os.path.relpath(path, ROOT).split(os.sep)[0]
    results = []
    records = RECORDS

    for cm in CLASS_TOKEN_RE.finditer(text):
        name = cm.group("name")
        bases, body_start = class_header(text, cm.end())
        if not bases:
            continue
        head = split_top_level(bases)[0]
        head_name = head.split("<")[0].strip().split(".")[-1]
        if head_name not in ACTIVITY_BASE_HEADS:
            continue
        if name in ABSTRACT_BASE_NAMES:
            continue

        body = balanced(text, body_start, "{", "}")
        attrs = preceding_attributes(text, cm.start() + len(cm.group(0)) - len(cm.group(0).lstrip()))

        inputs = []
        for im in INPUT_RE.finditer(body):
            decl = " ".join(im.group("type").split())
            # The production scanner honours BOTH Elsa's [Required] marker and the
            # RequiredMemberAttribute the C# compiler emits for `required` members
            # (ClrAssemblyScanner.HasRequired). Mirror that here, or an input declared with the
            # `required` modifier is silently reported as optional.
            required_modifier = decl == "required" or decl.startswith("required ")
            if required_modifier:
                decl = decl[len("required "):].strip()
            entry = {"name": im.group("name"), "type": decl}
            entry.update(parse_named_args(im.group("args") or ""))
            entry["required"] = required_modifier or "[Required]" in (im.group("extra") or "")
            inputs.append(entry)

        outputs = collect_outputs(body)

        result_type = None
        gm = re.match(r"[\w.]*Activity<(?P<t>.+)>$", head, re.DOTALL)
        if gm:
            result_type = split_top_level(gm.group("t"))[0]
            outputs.extend(records.get(result_type, []))

        outcomes = []
        for om in OUTCOME_RE.finditer(attrs):
            outcomes.extend(literals(om.group("args")))

        value_outcomes = None
        vm = VALUE_OUTCOMES_RE.search(attrs)
        if vm:
            src = literals(vm.group("args"))
            value_outcomes = {"source": src[0] if src else None}
            value_outcomes.update(parse_named_args(vm.group("args")))

        child_slots = []
        for sm in CHILD_SLOT_RE.finditer(attrs + body):
            got = literals(sm.group("args"))
            if got:
                child_slots.append(got[0])

        results.append({
            "module": module,
            "file": os.path.relpath(path, REPO),
            "class": name,
            "baseType": head,
            "resultType": result_type,
            "isTrigger": bool(TRIGGER_RE.search(attrs)),
            "hasResumeTarget": bool(RESUME_TARGET_RE.search(body)),
            "outcomes": outcomes,
            "valueOutcomes": value_outcomes,
            "childSlots": child_slots,
            "inputs": inputs,
            "outputs": outputs,
        })
    return results


def main():
    global CONSTANTS
    CONSTANTS = build_constants(REPO)

    sources = []
    for dirpath, dirnames, filenames in os.walk(ROOT):
        dirnames[:] = [d for d in dirnames if d not in ("bin", "obj")]
        sources.extend(os.path.join(dirpath, fn) for fn in filenames if fn.endswith(".cs"))

    for path in sources:
        index_records(path)

    all_results = []
    for path in sources:
        all_results.extend(scan_file(path))
    all_results.sort(key=lambda r: (r["module"], r["class"]))
    json.dump(all_results, sys.stdout, indent=2)
    print()


if __name__ == "__main__":
    main()
