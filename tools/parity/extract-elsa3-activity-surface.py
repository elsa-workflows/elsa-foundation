#!/usr/bin/env python3
"""Extract the Elsa 3 (elsa-core) activity contract surface from C# source.

Mirrors extract_elsa4.py so the two documents can be diffed directly.

Elsa 3 declares contract members as `Input<T>` / `Output<T>` properties. The
[Input]/[Output] attributes are OPTIONAL metadata (description, options, UI
hints) — many activities declare a bare `public Input<string> Text { get; set; }`
with no attribute at all. So membership is decided by the property's wrapper
type, and the attribute only enriches it.

Outcomes come from [FlowNode("A","B")] on the class, or from Outcomes(...) /
CompleteActivityWithOutcomesAsync(...) calls in the body.
"""
import json
import os
import re
import sys

REPO = os.environ.get("ELSA_CORE_ROOT", "/workspace/elsa-core")
ROOT = os.path.join(REPO, "src", "modules")

# Modules whose activities elsa-foundation owns in-repo. Integration connectors
# (Email, MassTransit, SQL, CSV, file IO, Slack, ...) are destined for a separate
# extensions workspace and are deliberately excluded from the parity diff.
MODULES = [
    "Elsa.Workflows.Core",
    "Elsa.Http",
    "Elsa.Scheduling",
    "Elsa.Expressions.JavaScript",
    "Elsa.Workflows.Runtime",
]

CLASS_TOKEN_RE = re.compile(
    r"(?:^|\n)\s*(?:public|internal)\s+(?:sealed\s+|abstract\s+|partial\s+|static\s+)*"
    r"class\s+(?P<name>\w+)"
)

MEMBER_RE = re.compile(
    r"public\s+(?P<kind>Input|Output)<(?P<type>(?:[^<>]|<[^<>]*>)*)>\s*\??\s+(?P<name>\w+)\s*\{\s*get"
)

FLOWNODE_RE = re.compile(r"\[FlowNode\((?P<args>[^\]]*)\)\]")
ACTIVITY_ATTR_RE = re.compile(r"\[Activity\((?P<args>(?:[^()]|\([^()]*\))*)\)\]")
STRING_RE = re.compile(r'"((?:[^"\\]|\\.)*)"')
CONST_DECL_RE = re.compile(
    r"(?:^|\n)\s*(?:public|internal)\s+const\s+string\s+(?P<name>\w+)\s*=\s*\"(?P<value>(?:[^\"\\]|\\.)*)\""
)
CONST_REF_RE = re.compile(r"\b([A-Z]\w+)\.(\w+)\b")
OUTCOME_CALL_RE = re.compile(
    r"(?:CompleteActivityWithOutcomesAsync|new\s+Outcomes)\s*\((?P<args>(?:[^()]|\([^()]*\))*)\)"
)

ACTIVITY_BASE_HEADS = {
    "Activity", "CodeActivity", "CodeActivityWithResult", "Trigger",
    "EventGenerator", "Container", "Composite", "FlowNode",
}
# Abstract bases in elsa-core that are not themselves author-facing activities.
ABSTRACT_BASE_NAMES = ACTIVITY_BASE_HEADS | {
    "ActivityBase", "SendHttpRequestBase", "HttpEndpointBase", "TimerBase", "EventBase",
}

CONSTANTS: dict = {}


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
    i, depth, bases_start = decl_end, 0, None
    while i < len(text):
        ch = text[i]
        if ch in "<([":
            depth += 1
        elif ch in ">)]":
            depth -= 1
        elif ch == ":" and depth == 0 and bases_start is None:
            bases_start = i + 1
        elif ch in "{;" and depth == 0:
            return (text[bases_start:i].strip() if bases_start else ""), i
        i += 1
    return "", len(text)


def preceding_attributes(text: str, start: int) -> str:
    head = text[:start].rstrip()
    collected = []
    while True:
        stripped = head.rstrip()
        if stripped.endswith("]"):
            depth, k = 0, len(stripped) - 1
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
        lines = stripped.splitlines()
        if lines and lines[-1].strip().startswith("//"):
            head = "\n".join(lines[:-1])
            continue
        break
    return "\n".join(reversed(collected))


def build_constants(root: str) -> dict:
    consts = {}
    for dirpath, dirnames, filenames in os.walk(root):
        dirnames[:] = [d for d in dirnames if d not in ("bin", "obj")]
        for fn in filenames:
            if not fn.endswith(".cs"):
                continue
            with open(os.path.join(dirpath, fn), "r", encoding="utf-8", errors="replace") as fh:
                text = fh.read()
            for cm in CLASS_TOKEN_RE.finditer(text):
                cls = cm.group("name")
                _b, body_start = class_header(text, cm.end())
                for dm in CONST_DECL_RE.finditer(balanced(text, body_start, "{", "}")):
                    consts.setdefault(f"{cls}.{dm.group('name')}", dm.group("value"))
    return consts


def literals(args: str):
    out = list(STRING_RE.findall(args or ""))
    for cls, member in CONST_REF_RE.findall(args or ""):
        key = f"{cls}.{member}"
        if key in CONSTANTS:
            out.append(CONSTANTS[key])
    return out


def parse_named_args(args: str) -> dict:
    out = {}
    if not args:
        return out
    for key in ("Description", "DisplayName", "Category", "DefaultValue", "UIHint", "Name"):
        m = re.search(key + r"\s*=\s*" + r'"((?:[^"\\]|\\.)*)"', args)
        if m:
            out[key] = m.group(1)
    m = re.search(r"Options\s*=\s*(?:new\[\]\s*)?[\[\{](?P<o>[^\]\}]*)[\]\}]", args, re.DOTALL)
    if m:
        out["Options"] = STRING_RE.findall(m.group("o"))
    for flag in ("IsRequired", "CanContainSecrets", "IsSerializable"):
        m = re.search(flag + r"\s*=\s*(true|false)", args)
        if m:
            out[flag] = m.group(1) == "true"
    return out


def member_attribute(body: str, member_start: int, kind: str):
    """Return the [Input(...)]/[Output(...)] args attached to a member, if any."""
    attrs = preceding_attributes(body, member_start)
    m = re.search(r"\[" + kind + r"(?P<args>\((?:[^()]|\((?:[^()]|\([^()]*\))*\))*\))?\s*\]", attrs, re.DOTALL)
    if not m:
        return None
    return m.group("args") or ""


def scan_file(path: str, module: str):
    with open(path, "r", encoding="utf-8", errors="replace") as fh:
        text = fh.read()

    results = []
    for cm in CLASS_TOKEN_RE.finditer(text):
        name = cm.group("name")
        bases, body_start = class_header(text, cm.end())
        if not bases:
            continue
        head = split_top_level(bases)[0]
        head_name = head.split("<")[0].strip().split("(")[0].strip().split(".")[-1]
        if head_name not in ABSTRACT_BASE_NAMES:
            continue
        if name in ABSTRACT_BASE_NAMES or name.endswith("Base"):
            # Keep abstract bases indexed so derived activities can inherit from them,
            # but flag them so the diff can flatten rather than report them.
            pass

        body = balanced(text, body_start, "{", "}")
        attrs = preceding_attributes(text, cm.start() + (len(cm.group(0)) - len(cm.group(0).lstrip())))

        inputs, outputs = [], []
        for mm in MEMBER_RE.finditer(body):
            entry = {"name": mm.group("name"), "type": " ".join(mm.group("type").split())}
            arg = member_attribute(body, mm.start(), mm.group("kind"))
            if arg is not None:
                entry.update(parse_named_args(arg))
                entry["hasAttribute"] = True
            (inputs if mm.group("kind") == "Input" else outputs).append(entry)

        outcomes = []
        fm = FLOWNODE_RE.search(attrs)
        if fm:
            outcomes = literals(fm.group("args"))

        dynamic = []
        for om in OUTCOME_CALL_RE.finditer(body):
            dynamic.extend(literals(om.group("args")))

        meta = {}
        am = ACTIVITY_ATTR_RE.search(attrs)
        if am:
            meta["activityAttributeStrings"] = STRING_RE.findall(am.group("args"))
            meta.update(parse_named_args(am.group("args")))

        results.append({
            "module": module,
            "file": os.path.relpath(path, REPO),
            "class": name,
            "baseType": head,
            "baseName": head_name,
            "isAbstract": name in ABSTRACT_BASE_NAMES or name.endswith("Base"),
            "isTrigger": head_name in ("Trigger", "EventGenerator") or name.endswith("Base") and "Trigger" in head_name,
            "meta": meta,
            "outcomes": outcomes,
            "dynamicOutcomes": sorted(set(dynamic)),
            "inputs": inputs,
            "outputs": outputs,
        })
    return results


def main():
    global CONSTANTS
    CONSTANTS = build_constants(ROOT)

    all_results = []
    for module in MODULES:
        base = os.path.join(ROOT, module)
        if not os.path.isdir(base):
            print(f"WARN: missing module {module}", file=sys.stderr)
            continue
        for dirpath, dirnames, filenames in os.walk(base):
            dirnames[:] = [d for d in dirnames if d not in ("bin", "obj")]
            for fn in filenames:
                if fn.endswith(".cs"):
                    all_results.extend(scan_file(os.path.join(dirpath, fn), module))

    # Flatten single-level inheritance: a derived activity inherits its base's members.
    by_name = {r["class"]: r for r in all_results}
    for r in all_results:
        base = by_name.get(r["baseName"])
        seen_in = {i["name"] for i in r["inputs"]}
        seen_out = {o["name"] for o in r["outputs"]}
        hops = 0
        while base is not None and base is not r and hops < 4:
            for i in base["inputs"]:
                if i["name"] not in seen_in:
                    r["inputs"].append({**i, "inheritedFrom": base["class"]})
                    seen_in.add(i["name"])
            for o in base["outputs"]:
                if o["name"] not in seen_out:
                    r["outputs"].append({**o, "inheritedFrom": base["class"]})
                    seen_out.add(o["name"])
            for oc in base["outcomes"] + base["dynamicOutcomes"]:
                if oc not in r["outcomes"] and oc not in r["dynamicOutcomes"]:
                    r["dynamicOutcomes"].append(oc)
            base = by_name.get(base["baseName"])
            hops += 1

    all_results.sort(key=lambda r: (r["module"], r["class"]))
    json.dump(all_results, sys.stdout, indent=2)
    print()


if __name__ == "__main__":
    main()
