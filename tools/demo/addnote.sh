#!/usr/bin/env bash
# Act 3 without the designer: runs a workflow with one "Add note" node on the Workbench, through the API calls Studio makes. It
# creates the workflow the first time, with the node pinned to the exact activity version asked for; when the node is pinned to
# another version it changes it, as the designer's "Change exact version" and "Apply to this occurrence" do; it sets the node's
# inputs, and runs the draft, as the designer's Run does. The rehearsal drives Act 3 with it, and it is the fallback when the
# designer misbehaves on stage.
set -euo pipefail
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"

usage() {
  cat <<'USAGE'
Usage: bash tools/demo/addnote.sh PORT VERSION TEXT [TAGS] [--workflow NAME] [--host NAME]

  PORT             the Workbench's port (5301 in the runbook)
  VERSION          the exact version of the Add note activity the node is pinned to: 1.0.0, or 1.1.0 once release 2 is
                   installed and the shell reloaded. A node pinned to another version is changed to this one first
  TEXT             the note's Text input
  TAGS             the note's Tags input, comma-separated (Add note 1.1.0 only)
  --workflow NAME  the workflow to run; default "Add note (API)". It is created when there is none of that name
  --host NAME      the Workbench host directory, artifacts/demo/hosts/NAME, whose development admin it signs in as;
                   default wb

It signs in as the Workbench's seeded development admin (SeedAdminUserName and SeedAdminPassword in the host's shells.json):
the password is read there, used for this run only, and never printed or written down. It prints the workflow, the node's
exact version (and the change, when it made one), the inputs, and how the run ended; it exits 1 when the run faulted or was
refused, with the reason.
USAGE
}

positional=()
workflow="Add note (API)"
host_name="wb"
while [[ $# -gt 0 ]]; do
  case "$1" in
    --workflow) demo_need_value "$@"; workflow="$2"; shift 2 ;;
    --host) demo_need_value "$@"; host_name="$2"; shift 2 ;;
    -h|--help) usage; exit 0 ;;
    -*) demo_fail "unknown argument '$1' (see --help)." ;;
    *) positional+=("$1"); shift ;;
  esac
done
[[ ${#positional[@]} -ge 3 && ${#positional[@]} -le 4 ]] || demo_fail "Give the port, the version and the text, and optionally the tags: bash tools/demo/addnote.sh 5301 1.0.0 \"a note\" (see --help)."
port="${positional[0]}"
[[ "$port" =~ ^[0-9]+$ ]] || demo_fail "PORT is a number, not '$port'."
shells="$demo_artifacts/hosts/$host_name/shells.json"
[[ -f "$shells" ]] || demo_fail "There is no Workbench host '$host_name' (${shells#"$demo_root"/} is missing). Prepare it with tools/demo/run-workbench.sh."

demo_require_python
python3 - "$port" "${positional[1]}" "${positional[2]}" "${positional[3]:-}" "$workflow" "$shells" <<'PY'
import hashlib, http.cookiejar, json, sys, time, urllib.error, urllib.request

port, version, text, tags, workflow, shells = sys.argv[1:7]
base = f"http://localhost:{port}"
activity_type = "Elsa.Samples.Nuplane.Notes.Activities.AddNote"


def fail(message):
    print(f"error: {message}", file=sys.stderr)
    sys.exit(1)


features = json.load(open(shells))["CShells"]["Shells"]["default"]["Features"]
seed = next((value for value in features.values() if isinstance(value, dict) and "SeedAdminUserName" in value), None)
if seed is None:
    fail("the host's shells.json seeds no development admin to sign in as")

opener = urllib.request.build_opener(urllib.request.HTTPCookieProcessor(http.cookiejar.CookieJar()))
token = None


def call(method, path, body=None):
    headers = {"Accept": "application/json", "Content-Type": "application/json"}
    if token:
        headers["Authorization"] = "Bearer " + token
    request = urllib.request.Request(base + path, method=method, headers=headers,
                                     data=None if body is None else json.dumps(body).encode())
    try:
        with opener.open(request, timeout=60) as response:
            text = response.read().decode()
            return json.loads(text) if text else None
    except urllib.error.HTTPError as error:
        fail(f"{method} {path} answered HTTP {error.code}: {error.read().decode()[:600]}")
    except (urllib.error.URLError, OSError) as error:
        fail(f"nothing answers on port {port} ({error}): is the Workbench running?")


call("POST", "/_elsa/identity/login", {"username": seed["SeedAdminUserName"], "password": seed["SeedAdminPassword"]})
token = call("GET", "/_elsa/identity/token")["accessToken"]

catalog = call("GET", "/design/activities/catalog")
items = catalog.get("activities", []) if isinstance(catalog, dict) else catalog
versions = {item["version"]: item for item in items if item["activityTypeKey"] == activity_type}
target = versions.get(version)
if target is None:
    present = ", ".join(sorted(versions)) or "none"
    fail(f"Add note {version} is not in the activity catalog (it has: {present}). A new release appears there once it is installed and the shell is reloaded.")
names = {entry["referenceKey"] for entry in target["inputs"]}
if tags and "Tags" not in names:
    fail(f"Add note {version} has no Tags input; Add note 1.1.0 has")
by_id = {item["activityVersionId"]: item["version"] for item in versions.values()}


def literal(key, value):
    return {"referenceKey": key, "value": {"value": value, "expressionType": "Literal"}, "autoEvaluate": None,
            "evaluatorType": None, "storageDriverType": None, "isSensitive": None}


def add_note_nodes(node):
    """Every Add note node in the tree, wherever the workflow's structure keeps it."""
    if isinstance(node, dict):
        if node.get("activityVersionId") in by_id:
            yield node
        for value in node.values():
            yield from add_note_nodes(value)
    elif isinstance(node, list):
        for value in node:
            yield from add_note_nodes(value)


listed = call("GET", "/design/workflows/definitions") or {}
existing = next((item for item in listed.get("items", []) if item.get("name") == workflow), None)
if existing is None:
    node = {"nodeId": "addnote", "activityVersionId": target["activityVersionId"], "inputs": [], "outputs": []}
    details = call("POST", "/design/workflows/definitions", {
        "name": workflow, "description": "The Add note activity, run through the API the designer uses.",
        "initialState": {"variables": [], "rootActivity": node, "inputs": [], "outputs": []}})
    print(f'workflow "{workflow}": created, its Add note node pinned to {version}')
else:
    details = call("GET", f"/design/workflows/definitions/{existing['id']}")
definition_id, draft = details["definition"]["id"], details["draft"]
if draft is None:
    fail(f'workflow "{workflow}" has no draft to run')

nodes = list(add_note_nodes(draft["state"]))
if not nodes:
    fail(f'workflow "{workflow}" has no Add note node')
node = nodes[0]
pinned = by_id[node["activityVersionId"]]
if pinned != version:
    node["activityVersionId"] = target["activityVersionId"]
    print(f'workflow "{workflow}": its Add note node changed from exact version {pinned} to {version}')
elif existing is not None:
    print(f'workflow "{workflow}": its Add note node is pinned to {version}')
wanted = {"Text": text, **({"Tags": tags} if tags else {})}
node["inputs"] = [entry for entry in node.get("inputs") or [] if entry.get("referenceKey") not in wanted and entry.get("referenceKey") in names]
node["inputs"] += [literal(key, value) for key, value in wanted.items()]
print("inputs: " + ", ".join(f'{key} "{value}"' for key, value in wanted.items()))

saved = call("PUT", f"/design/workflows/drafts/{draft['id']}", {
    "state": draft["state"], "layout": draft.get("layout"), "activityPresentation": draft.get("activityPresentation")})
state = saved["state"]
snapshot = f"{draft['id']}-{hashlib.sha1(json.dumps(state, sort_keys=True).encode()).hexdigest()[:8]}"
run = call("POST", "/publishing/workflows/drafts/test-runs", {
    "definitionId": definition_id, "snapshotId": snapshot, "state": state, "inputs": {},
    "activityPresentation": saved.get("activityPresentation") or []})
execution = run.get("workflowExecutionId")
if not execution:
    fail(f"the run was refused: {run.get('status')}: {run.get('reason')}")

status = "?"
for _ in range(120):
    status = call("GET", f"/runtime/workflows/instances/{execution}")["instance"]["status"]
    if status in ("Completed", "Faulted", "Cancelled"):
        break
    time.sleep(0.5)
print(f"run: {status}")
if status != "Completed":
    incidents = (call("GET", f"/runtime/workflows/instances/{execution}/incidents") or {}).get("incidents", [])
    for incident in incidents:
        print(f"  {incident.get('failureType')}: {incident.get('message')}")
    sys.exit(1)
PY
