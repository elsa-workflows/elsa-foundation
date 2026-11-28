#!/usr/bin/env python3
"""Exercise one immutable profile image through its authenticated workflow API."""

import argparse
import copy
import json
import os
from pathlib import Path
import secrets
import subprocess
import tempfile
import time
from urllib.parse import urlencode, urlsplit
from uuid import uuid4

from profile_acceptance import (
    MIGRATED_FIXTURE, MIGRATED_PROFILE, ProfileRequestError, actor_activity_completions,
    verify_migrated_files, verify_secret_store, verify_variant,
)


CURATED = "essentials-curated"
LEAN = "essentials"
ESSENTIALS_PRESETS = Path(__file__).resolve().parents[2] / "docker/profiles/essentials/presets"
POSTGRES_IMAGE = "postgres:17-alpine"
SHELL_FEATURES = "CShells__Shells__default__Features__"
POSTGRES_KERNEL_CONNECTION = f"{SHELL_FEATURES}WorkflowsRuntimeKernelOptionalHttpHosting__PostgresConnectionString"
SMOKE_TLS_DIRECTORY = "/run/elsa-smoke-tls"
ACTOR_PROFILES = {LEAN, CURATED, "elsa3-import"}
WRITE_LINE = "Elsa.Activities.Primitives.Activities.WriteLine"
WRITE_LINE_NODE = "write-line"
LOGIN_PATH = "/_elsa/identity/login"
COOKIE_DIRECTORY = "/tmp/elsa-profile-smoke"
COOKIE_JAR = f"{COOKIE_DIRECTORY}/cookies"
COOKIE_HEADER = f"{COOKIE_DIRECTORY}/cookie-header"
OPTIONAL_TRIGGER_FEATURES = {
    "http": ("Http", "KernelActivitiesHttp", "WorkflowsRuntimeStandaloneHttp", "StandaloneHttpTriggerAuthoring"),
    "cron": ("KernelActivitiesScheduling", "StandaloneScheduleTriggerAuthoring", "PreparedStandaloneScheduleTriggers"),
}
OPTIONAL_TRIGGER_TYPES = {"http": ("http",), "cron": ("cron", "timer")}
OPTIONAL_TRIGGER_ACTIVITIES = {
    "http": "Elsa.Activities.Http.Activities.HttpEndpoint",
    "cron": "Elsa.Activities.Scheduling.Activities.Cron",
    "timer": "Elsa.Activities.Scheduling.Activities.Timer",
}
ALL_OPTIONAL_TRIGGER_TYPES = [kind for family in OPTIONAL_TRIGGER_TYPES.values() for kind in family]
OPTIONAL_TRIGGER_FEATURE_NAMES = {name for group in OPTIONAL_TRIGGER_FEATURES.values() for name in group}


def docker(*args, input_text=None, strip=True):
    output = subprocess.run(
        ["docker", *args], input=input_text, capture_output=True, text=True, check=True,
    ).stdout
    return output.strip() if strip else output


def request(container, path, data=None, *, authenticated=True, header_file=COOKIE_HEADER):
    # Send the session cookie only to the same loopback origin used for sign-in.
    script = '''path=$1; shift
if [ -n "${ELSA_PROFILE_SMOKE_HTTPS:-}" ]; then
    exec curl --cacert /run/elsa-smoke-tls/cert.pem "$@" "https://127.0.0.1:8443$path"
fi
exec curl "$@" "http://127.0.0.1:8080$path"'''
    args = ["exec", "-i", container, "sh", "-ec", script, "sh", path, "-sS", "--max-time", "5",
            "-w", "\n%{http_code}\n%{redirect_url}"]
    if authenticated:
        args.extend(["-H", f"@{header_file}"])
    elif path == LOGIN_PATH:
        args.extend(["-c", COOKIE_JAR])
    if data is not None:
        args.extend(["-H", "Content-Type: application/json", "--data-binary", "@-"])
    response = docker(*args, input_text=json.dumps(data) if data is not None else None, strip=False)
    body, status, redirect = response.rsplit("\n", 2)
    return int(status), body, redirect


def read_json(container, path, data=None, *, authenticated=True):
    status, body, _ = request(container, path, data, authenticated=authenticated)
    if not 200 <= status < 300:
        raise ProfileRequestError(path, status, body)
    return json.loads(body)


def wait_ready(container, started, timeout):
    last_response = "no HTTP response"
    while time.monotonic() - started < timeout:
        try:
            status, body, _ = request(container, "/health/ready", authenticated=False)
            if status == 200:
                return round(time.monotonic() - started, 3), json.loads(body)
            last_response = f"HTTP {status}: {body}"
        except subprocess.CalledProcessError:
            if docker("inspect", "--format", "{{.State.Running}}", container) != "true":
                raise RuntimeError("The container exited before readiness") from None
        time.sleep(0.2)
    raise RuntimeError(f"/health/ready did not return HTTP 200 within {timeout}s ({last_response})")


def matching_activities(container, type_key):
    query = urlencode({"search": type_key})
    items = read_json(container, f"/design/activities/definitions?{query}")["items"]
    return [item["definition"] for item in items if item["definition"]["activityTypeKey"] == type_key]


def prepare_cookie_header(container):
    # libcurl 8.5 omits chunks when a session cookie exceeds 8190 bytes. Keep the complete
    # header in the container and send it only to the same loopback HTTP origin as the login.
    script = r'''umask 077
awk -v now="$3" -v https="${ELSA_PROFILE_SMOKE_HTTPS:-}" -F '\t' '
    BEGIN { printf "Cookie: " }
    NF == 7 && ($1 == "127.0.0.1" || $1 == "#HttpOnly_127.0.0.1") &&
      $3 == "/" && ($4 == "FALSE" || https == 1) && ($5 == 0 || $5 > now) {
        printf "%s%s=%s", separator, $6, $7
        separator = "; "
    }
    END { if (separator == "") exit 1; printf "\n" }
' "$1" > "$2"'''
    docker("exec", container, "sh", "-ec", script, "sh", COOKIE_JAR, COOKIE_HEADER, str(int(time.time())))


def authenticate(container, args):
    path = "/design/activities/definitions"
    unauthenticated_status, _, redirect = request(container, path, authenticated=False)
    cookie_challenge = unauthenticated_status == 302 and urlsplit(redirect).path == LOGIN_PATH
    if unauthenticated_status not in (401, 403) and not cookie_challenge:
        raise RuntimeError(f"Unauthenticated {path} returned HTTP {unauthenticated_status}")
    docker("exec", container, "mkdir", "-p", "-m", "700", COOKIE_DIRECTORY)
    login = read_json(container, LOGIN_PATH, {"username": args.username, "password": args.password}, authenticated=False)
    if login["status"] != "authenticated":
        raise RuntimeError(f"Identity did not report an authenticated session: {login['status']}")
    prepare_cookie_header(container)
    return unauthenticated_status, login["status"]


def write_header(container, file_name, value):
    path = f"{COOKIE_DIRECTORY}/{file_name}"
    docker("exec", "-i", container, "sh", "-ec", 'umask 077; cat > "$1"', "sh", path, input_text=value + "\n")
    return path


def verify_curated_profile(container, args):
    token_path = "/_elsa/identity/token"
    if request(container, token_path, authenticated=False)[0] != 401:
        raise RuntimeError("Token exchange must require a signed-in session")
    access_token = read_json(container, token_path)["accessToken"]
    bearer = write_header(container, "bearer-header", f"Authorization: Bearer {access_token}")
    if request(container, "/design/activities/definitions", header_file=bearer)[0] != 200:
        raise RuntimeError("The token issued by this host was refused by its protected workflow API")

    registry = "/_elsa/module-management/registry"
    if request(container, registry, authenticated=False)[0] != 401:
        raise RuntimeError("The module registry must challenge requests missing the configured management key")
    management = write_header(container, "management-header", f"X-Elsa-Module-Management-Key: {args.management_key}")
    status, body, _ = request(container, registry, header_file=management)
    if status != 200:
        raise RuntimeError(f"The module registry returned HTTP {status}")
    feeds = [feed["name"] for feed in json.loads(body)["feeds"]]
    if feeds != ["local-packages", "profile-packages"]:
        raise RuntimeError(f"The writable registry feed must precede the baked package cache: {feeds}")

    origin = "http://localhost:5089"
    headers = docker("exec", container, "curl", "-fsS", "--max-time", "5", "-X", "OPTIONS",
                     "-D", "-", "-o", "/dev/null", "-H", f"Origin: {origin}",
                     "-H", "Access-Control-Request-Method: GET",
                     "http://127.0.0.1:8080/design/activities/definitions").lower()
    if f"access-control-allow-origin: {origin}" not in headers.splitlines():
        raise RuntimeError(f"CORS preflight for {origin} was refused")

    return {"tokenExchange": "verified", "bearerAuthorization": "verified", "corsOrigin": origin, "registryFeeds": feeds,
            "secrets": verify_secret_store(lambda path, data=None: read_json(container, path, data))}


def verify_actor_workflow_history(container, instance, execution, version, publication, *, scheduled=False):
    def request_json(path):
        return read_json(container, path)

    source_reference_id = None if scheduled else publication["sourceReferenceId"]
    completed = actor_activity_completions(
        request_json, instance, execution, publication["artifactId"], source_reference_id, version)
    write_line = [item for item in completed if item["executableNodeId"] == WRITE_LINE_NODE]
    if len(write_line) != 1:
        raise RuntimeError(f"The Actor History did not contain the committed WriteLine activity: {completed}")
    return write_line[0]["historyEventId"]


def run_workflow(container, args):
    unauthenticated_status, login_status = authenticate(container, args)
    for type_key in args.expect_activity:
        if not matching_activities(container, type_key):
            raise RuntimeError(f"Expected activity missing from the catalog: {type_key}")
    for type_key in args.omit_activity:
        if matching_activities(container, type_key):
            raise RuntimeError(f"Omitted activity still appears in the catalog: {type_key}")

    trigger_types = None
    if args.expect_trigger or args.omit_trigger:
        page = read_json(container, "/runtime/standalone-trigger-types")
        trigger_types = sorted({item["typeKey"] for item in page["items"]})
        if set(args.expect_trigger).difference(trigger_types) or set(args.omit_trigger).intersection(trigger_types):
            raise RuntimeError(f"Unexpected standalone trigger types: {trigger_types}")

    write_line = matching_activities(container, WRITE_LINE)
    if len(write_line) != 1:
        raise RuntimeError(f"Expected one {WRITE_LINE} definition, found {len(write_line)}")
    marker = f"elsa-profile-smoke-{uuid4().hex}"
    submitted = read_json(container, "/design/workflows/definitions/submit", {
        "name": marker,
        "state": {
            "variables": [], "inputs": [], "outputs": [],
            "rootActivity": {
                "nodeId": WRITE_LINE_NODE, "activityVersionId": write_line[0]["headVersionId"],
                "inputs": [{"referenceKey": "text", "value": {"value": marker, "expressionType": "Literal"}}],
                "outputs": [],
            },
        },
    })
    version = submitted["version"]["id"]
    publication = read_json(container, f"/publishing/workflows/{version}/publish", {})
    artifact = publication["artifactId"]
    run = read_json(container, f"/runtime/workflows/executables/{artifact}/execute", {
        "sourceReferenceId": publication["sourceReferenceId"],
    })
    execution = run["workflowExecutionId"]
    deadline = time.monotonic() + 30
    while time.monotonic() < deadline:
        status, body, _ = request(container, f"/runtime/workflows/instances/{execution}")
        if status == 200:
            details = json.loads(body)
            instance = details["instance"]
            if instance["status"] in ("Completed", "Finished"):
                if instance["incidentCount"] != 0:
                    raise RuntimeError(f"Unexpected workflow incidents: {instance}")
                history_event = None
                if args.variant in ACTOR_PROFILES:
                    history_event = verify_actor_workflow_history(container, instance, execution, version, publication)
                elif instance["activityCount"] < 1:
                    raise RuntimeError(f"Unexpected workflow activity count: {instance}")
                if marker not in docker("logs", container).splitlines():
                    raise RuntimeError("WriteLine output was absent from container logs")
                return {
                    "unauthenticatedStatus": unauthenticated_status, "loginStatus": login_status,
                    "executionId": execution, "workflowStatus": instance["status"], "output": marker,
                    "standaloneTriggerTypes": trigger_types,
                    **({"activityHistoryEventId": history_event} if history_event is not None else {}),
                }
            if instance["status"] in ("Faulted", "Cancelled"):
                raise RuntimeError(f"Workflow did not complete: {details}")
        elif status != 404:
            raise RuntimeError(f"Runtime instance returned HTTP {status}: {body}")
        time.sleep(0.25)
    raise RuntimeError(f"Workflow {execution} did not complete within 30s")


def profile_activity(container, type_key, node_id, inputs=None, structure=None):
    definition, = matching_activities(container, type_key)
    return {"nodeId": node_id, "activityVersionId": definition["headVersionId"],
            "inputs": [{"referenceKey": key, "value": {"value": value, "expressionType": "Literal"}}
                       for key, value in (inputs or {}).items()],
            "outputs": [], "structure": structure}


def submit_lean_sequence(container, name, activities):
    root = profile_activity(container, "Elsa.Activities.Sequence.Activities.Sequence", "root",
                            structure={"kind": "elsa.sequence.structure", "schemaVersion": "1.0.0",
                                       "payload": {"activities": activities}})
    draft = read_json(container, "/design/workflows/definitions/submit", {
        "name": name, "state": {"rootActivity": root, "variables": [], "inputs": [], "outputs": []},
    })
    version = draft["version"]["id"]
    return draft["definition"]["id"], version


def verify_lean_triggers(container, expected_types):
    marker = f"elsa-lean-trigger-{uuid4().hex}"
    route = f"/workflows/http/{marker}"
    catalog = read_json(container, "/design/activities/catalog?availability=Addable")
    addable_types = {value["activityTypeKey"] for value in catalog["activities"]}
    addable_triggers = {kind for kind, type_key in OPTIONAL_TRIGGER_ACTIVITIES.items() if type_key in addable_types}
    if addable_triggers != set(expected_types):
        raise RuntimeError(f"Lean Essentials advertised the wrong addable trigger activities: {sorted(addable_triggers)}")

    catalog = read_json(container, "/design/activities/catalog?availability=All")
    all_types = {value["activityTypeKey"] for value in catalog["activities"]}
    missing_definitions = set(OPTIONAL_TRIGGER_ACTIVITIES.values()) - all_types
    if missing_definitions:
        raise RuntimeError(f"Lean Essentials omitted underlying trigger definitions: {sorted(missing_definitions)}")

    capabilities = {value["id"] for value in read_json(container, "/capabilities")["capabilities"]}
    expects_http = "http" in expected_types
    if ("elsa.http.workflow-endpoints" in capabilities) != expects_http:
        raise RuntimeError("Lean Essentials advertised the wrong authored HTTP endpoint capability")

    result = {"authoredHttp": "disabled", "addableTriggerActivities": sorted(addable_triggers)}
    if expects_http:
        _, version = submit_lean_sequence(container, marker, [
            profile_activity(container, OPTIONAL_TRIGGER_ACTIVITIES["http"], "http",
                             {"Path": marker, "SupportedMethods": ["POST"], "CanStartWorkflow": True}),
            profile_activity(container, "Elsa.Activities.Http.Activities.WriteHttpResponse", "respond",
                             {"StatusCode": 201, "Body": marker}),
        ])
        read_json(container, f"/publishing/workflows/{version}/publish", {})
        status, body, _ = request(container, route, {})
        if status != 201 or body.strip('"') != marker:
            raise RuntimeError(f"The mounted Actor HTTP endpoint returned HTTP {status}: {body}")
        result["authoredHttp"] = "verified"
    elif request(container, route, {})[0] != 404:
        raise RuntimeError("Lean Essentials served the authored HTTP route without the mounted feature")

    definition, version = submit_lean_sequence(container, marker + "-timer", [
        profile_activity(container, OPTIONAL_TRIGGER_ACTIVITIES["timer"], "timer", {"Interval": "PT4S"}),
        profile_activity(container, WRITE_LINE, WRITE_LINE_NODE, {"text": marker}),
    ])
    publish = f"/publishing/workflows/{version}/publish"
    if "timer" in expected_types:
        publication = read_json(container, publish, {})
        query = urlencode({"definitionId": definition, "take": 10})
        deadline = time.monotonic() + 35
        while time.monotonic() < deadline:
            instances = read_json(container, f"/runtime/workflows/instances/page?{query}")["items"]
            for instance in instances:
                if instance["status"] not in ("Completed", "Finished"):
                    continue
                execution = instance["workflowExecutionId"]
                full = read_json(container, f"/runtime/workflows/instances/{execution}")["instance"]
                history_event = verify_actor_workflow_history(container, full, execution, version, publication, scheduled=True)
                result["authoredTimer"] = history_event
                return result
            time.sleep(0.25)
        raise RuntimeError("The mounted Actor Timer did not complete its published workflow")

    status, body, _ = request(container, publish, {})
    if status < 400 or "definition_unsupported" not in body:
        raise RuntimeError(f"Lean Essentials accepted an authored Timer with scheduling disabled: HTTP {status}: {body}")
    result["authoredTimer"] = "disabled"
    return result


def smoke(args, migrated_directory=None, *, preset=None, network="none", verify_resume=False):
    environment = "Production" if preset else "Development"
    create = ["create", "--pull", "never", "--network", network,
              "-e", f"ASPNETCORE_ENVIRONMENT={environment}"]
    if preset:
        create.extend(["--env-file", str(ESSENTIALS_PRESETS / f"{preset}.env")])
    if args.env_file:
        create.extend(["--env-file", str(args.env_file)])
    if preset:
        create.extend([
            "-e", "ELSA_PROFILE_SMOKE_HTTPS=1",
            "-e", "ASPNETCORE_URLS=http://+:8080;https://+:8443",
            "-e", f"ASPNETCORE_Kestrel__Certificates__Default__Path={SMOKE_TLS_DIRECTORY}/cert.pem",
            "-e", f"ASPNETCORE_Kestrel__Certificates__Default__KeyPath={SMOKE_TLS_DIRECTORY}/key.pem",
            "-e", f"{SHELL_FEATURES}FoundationIdentityAspNetCoreIdentityEntityFrameworkCore__IsDevelopmentOrDemo=false",
            "--mount", f"type=bind,source={args.tls_directory},target={SMOKE_TLS_DIRECTORY},readonly",
        ])
    if args.shells:
        create.extend(["--mount", f"type=bind,source={args.shells.resolve()},target=/app/shells.json,readonly"])
    if migrated_directory:
        create.extend(["--mount", f"type=bind,source={migrated_directory},target=/app/migrated,readonly"])
    container = docker(*create, args.image_id)
    completed = False
    try:
        # Check before the first process start. Docker gives every run a new writable layer.
        find_cache = "test -d /app/.nuplane && find /app/.nuplane -type f -print -quit"
        cache_contents = docker("run", "--rm", "--pull", "never", "--network", "none",
                                "--entrypoint", "sh", args.image_id, "-ec", find_cache)
        if cache_contents:
            raise RuntimeError(f"Image contains pre-existing Nuplane state: {cache_contents}")
        started = time.monotonic()
        docker("start", container)
        ready_seconds, readiness = wait_ready(container, started, args.timeout)
        results = {"image": args.image, "imageId": args.image_id, "mountedShells": args.shells is not None,
                   "network": "none" if network == "none" else "internal", "initialNuplaneCacheEmpty": True,
                   "readySeconds": ready_seconds, "readiness": readiness}
        if preset:
            results["databasePreset"] = preset
        if args.variant:
            results["pinnedPackageCount"] = args.pinned_package_count
        if migrated_directory:
            results["mountedMigratedFiles"] = True
        else:
            results.update({"expectedActivities": args.expect_activity, "omittedActivities": args.omit_activity,
                            "expectedTriggerTypes": args.expect_trigger, "omittedTriggerTypes": args.omit_trigger})
        if not args.ready_only:
            try:
                def request_json(path, data=None):
                    return read_json(container, path, data)

                if migrated_directory:
                    unauthenticated_status, login_status = authenticate(container, args)
                    results.update({"unauthenticatedStatus": unauthenticated_status, "loginStatus": login_status})
                    results["migratedFiles"] = verify_migrated_files(
                        request_json, lambda key: matching_activities(container, key)
                    )
                else:
                    results.update(run_workflow(container, args))
                    results.update(verify_variant(args.variant, request_json))
                    if args.variant == LEAN:
                        results.update(verify_lean_triggers(container, args.expect_trigger))
                    if args.variant == CURATED:
                        results.update(verify_curated_profile(container, args))
                lines = docker("logs", container).splitlines()
                if migrated_directory and results["migratedFiles"]["output"] not in lines:
                    raise RuntimeError("Output from the mounted catalog activity was absent from container logs")
                for line in lines:
                    if "not available in the runtime feature catalog" in line:
                        raise RuntimeError(line.strip())
                if preset:
                    verify_run_after_restart(container, args, results, preset)
                if verify_resume:
                    results["suspendedRunAfterRestart"] = verify_delayed_run_after_restart(container, args)
            except (RuntimeError, KeyError, ValueError, subprocess.CalledProcessError):
                print(json.dumps(results), flush=True)
                raise
        print(json.dumps(results), flush=True)
        completed = True
    finally:
        logs = subprocess.run(["docker", "logs", container], capture_output=True, text=True, check=False)
        try:
            if args.log:
                args.log.write_text(logs.stdout + logs.stderr)
                if not completed:
                    subprocess.run(["docker", "cp", f"{container}:/app/.nuplane/store-state.json",
                                    str(args.log.with_suffix(".nuplane-state.json"))],
                                   capture_output=True, text=True, check=False)
        finally:
            docker("rm", "-f", container)


def verify_run_after_restart(container, args, results, preset):
    started = time.monotonic()
    docker("restart", container)
    wait_ready(container, started, args.timeout)
    authenticate(container, args)
    instance = read_json(container, f"/runtime/workflows/instances/{results['executionId']}")["instance"]
    if instance["status"] != results["workflowStatus"] or instance["incidentCount"] != 0:
        raise RuntimeError(f"The {preset} preset lost the completed workflow after restart: {instance}")
    if preset == "sqlite":
        docker("exec", container, "sh", "-ec", "test -s /app/data/elsa.db && test -s /app/data/elsa-kernel.db")
    else:
        docker("exec", container, "sh", "-ec", "test ! -e /app/data/elsa.db && test ! -e /app/data/elsa-kernel.db")
    results["completedRunAfterRestart"] = "verified"


def verify_delayed_run_after_restart(container, args):
    marker = f"elsa-profile-restart-{uuid4().hex}"
    _, version = submit_lean_sequence(container, marker, [
        profile_activity(container, "Elsa.Activities.Scheduling.Activities.Delay", "delay", {"Duration": "00:00:20"}),
        profile_activity(container, WRITE_LINE, WRITE_LINE_NODE, {"text": marker}),
    ])
    publication = read_json(container, f"/publishing/workflows/{version}/publish", {})
    started = time.monotonic()
    execution = read_json(container, f"/runtime/workflows/executables/{publication['artifactId']}/execute", {
        "sourceReferenceId": publication["sourceReferenceId"],
    })["workflowExecutionId"]
    path = f"/runtime/workflows/instances/{execution}"
    observed = None
    while time.monotonic() - started < 16:
        status, body, _ = request(container, path)
        if status == 200:
            instance = json.loads(body)["instance"]
            observed = (instance["status"], instance.get("subStatus"), instance["incidentCount"])
            if instance["status"] in ("Completed", "Finished", "Faulted", "Cancelled"):
                raise RuntimeError(f"The delayed workflow ended before restart: {observed}")
            # A pending durable timer can project as Running. Require a committed
            # Delay start and no Delay completion before the server stops.
            history = read_json(container, f"{path}/history")
            events = {event["name"] for event in history["items"]
                      if event["executableNodeId"] == "delay" and event["authority"] == "CommittedTransition"}
            if "ActivityStarted" in events and "ActivityCompleted" not in events:
                break
        elif status != 404:
            raise RuntimeError(f"Could not inspect delayed workflow: HTTP {status}: {body}")
        time.sleep(0.25)
    else:
        raise RuntimeError(f"The delayed workflow did not suspend: {execution}, last status: {observed}")

    docker("stop", "--time", "3", container)
    if time.monotonic() - started >= 20:
        raise RuntimeError("The restart probe did not stop the server before the delay elapsed")
    started = time.monotonic()
    docker("start", container)
    wait_ready(container, started, args.timeout)
    authenticate(container, args)
    deadline = time.monotonic() + 35
    while time.monotonic() < deadline:
        instance = read_json(container, path)["instance"]
        if instance["status"] == "Completed" and instance["incidentCount"] == 0:
            if marker not in docker("logs", container).splitlines():
                raise RuntimeError("WriteLine output after the delay was absent from container logs")
            return verify_actor_workflow_history(container, instance, execution, version, publication)
        if instance["status"] in ("Faulted", "Cancelled") or instance["incidentCount"] != 0:
            raise RuntimeError(f"The delayed workflow failed after restart: {instance}")
        time.sleep(0.25)
    raise RuntimeError(f"The delayed workflow did not resume after restart: {execution}")


def verify_postgres_preset(args, directory, scheduling):
    if subprocess.run(["docker", "image", "inspect", POSTGRES_IMAGE], capture_output=True).returncode:
        docker("pull", POSTGRES_IMAGE)
    password = secrets.token_hex(24)
    network = docker("network", "create", "--internal", f"elsa-profile-smoke-{uuid4().hex}")
    database = None
    try:
        database = docker("create", "--pull", "never", "--network", network, "--network-alias", "postgres",
                          "-e", f"POSTGRES_PASSWORD={password}", POSTGRES_IMAGE)
        docker("start", database)
        deadline = time.monotonic() + args.timeout
        while time.monotonic() < deadline:
            if not subprocess.run(["docker", "exec", database, "pg_isready", "-h", "127.0.0.1", "-U", "postgres",
                                   "-d", "postgres"], capture_output=True).returncode:
                break
            time.sleep(0.25)
        else:
            raise RuntimeError("PostgreSQL did not become ready for the Essentials preset")
        def connection(database_name):
            return f"Host=postgres;Database={database_name};Username=postgres;Password={password}"

        # The Actor store binds its schedule owner at initialization. Qualify
        # the default and scheduling shells against separate fresh databases.
        for label, variant, verify_resume in (("default", args, False), ("scheduling", scheduling, True)):
            module_database, actor_database = f"elsa_{label}", f"elsa_{label}_kernel"
            for name in (module_database, actor_database):
                docker("exec", database, "createdb", "-U", "postgres", name)
            postgres = copy.copy(variant)
            postgres.env_file = directory / f"postgresql-{label}.env"
            postgres.env_file.write_text(args.env_file.read_text() + (
                f"ConnectionStrings__Elsa={connection(module_database)}\n"
                f"{POSTGRES_KERNEL_CONNECTION}={connection(actor_database)}\n"
            ))
            if args.log:
                postgres.log = args.log.with_suffix(f".postgresql-{label}.log")
            smoke(postgres, preset="postgresql", network=network, verify_resume=verify_resume)
            for name in (module_database, actor_database):
                count = docker("exec", database, "psql", "-U", "postgres", "-d", name,
                               "-Atc", "SELECT count(*) FROM pg_tables WHERE schemaname NOT IN ('pg_catalog', 'information_schema')")
                if int(count) == 0:
                    raise RuntimeError(f"The Essentials preset did not initialize PostgreSQL database {name}")
    finally:
        try:
            if database:
                docker("rm", "-f", "-v", database)
        finally:
            docker("network", "rm", network)


def read_image_file(image_id, path):
    return docker("run", "--rm", "--pull", "never", "--network", "none",
                  "--entrypoint", "cat", image_id, path)


def verify_pinned_packages(image_id, allow_add_ons, required_packages=()):
    settings = json.loads(read_image_file(image_id, "/app/appsettings.json"))["Nuplane"]
    feed_name = "profile-packages"
    manifest_path = "/app/profile-desired.json"
    lock_path = "/app/nuplane.lock.json"
    feeds = [{"Name": feed_name, "DirectoryPath": feed_name, "Directory": {"Role": "Cache", "Watch": False}}]
    if allow_add_ons:
        feeds.insert(0, {
            "Name": "local-packages",
            "DirectoryPath": "packages",
            "IncludePatterns": ["*"],
            "Directory": {"Watch": True, "DebounceWindow": "00:00:01"},
        })
    if settings["Setup"]["Feeds"] != feeds:
        raise RuntimeError("Unexpected runtime feeds for the profile")
    if settings["Convergence"]["Manifest"] != {"Enabled": True, "Path": manifest_path}:
        raise RuntimeError("Expected the baked Nuplane desired manifest to be enabled")
    mode = "Enforce" if allow_add_ons else "Strict"
    if settings["LockFile"] != {"Mode": mode, "Path": lock_path, "FailOnHashMismatch": True}:
        raise RuntimeError("Expected Nuplane to enforce the baked archive hashes")
    if settings["FeedResolution"]["OfflineMode"] != (not allow_add_ons):
        raise RuntimeError("Unexpected profile offline policy")
    if settings["Reconciliation"]["StartupFailurePolicy"] != "FailHost":
        raise RuntimeError("Expected startup to fail if package admission fails")
    lock = json.loads(read_image_file(image_id, lock_path))
    manifest = json.loads(read_image_file(image_id, manifest_path))
    pins = [
        {"id": package["id"], "version": package["version"], "sourceHint": package["feed"]}
        for package in lock["packages"]
    ]
    if not pins or manifest != {"schemaVersion": "1.0", "generatedAtUtc": lock["generatedAt"], "packages": pins}:
        raise RuntimeError("The baked desired manifest differs from the Nuplane lock")
    missing = set(required_packages).difference(package["id"].lower() for package in pins)
    if missing:
        raise RuntimeError(f"The baked lock omits required provider archives: {sorted(missing)}")
    return len(pins)


def smoke_migrated_files(args, directory):
    mounted = copy.copy(args)
    mounted.shells = directory / "migrated-files.json"
    if args.log:
        mounted.log = args.log.with_suffix(".migrated-files.log")
    composition = json.loads(read_image_file(args.image_id, "/app/shells.json"))
    features = composition["CShells"]["Shells"]["default"]["Features"]
    readers = json.loads((MIGRATED_PROFILE / "migrated-files.features.json").read_text(encoding="utf-8"))
    features.update(readers)
    mounted.shells.write_text(json.dumps(composition))
    smoke(mounted, MIGRATED_FIXTURE)


def verify_profile(args):
    providers = ("microsoft.entityframeworkcore.sqlite", "npgsql.entityframeworkcore.postgresql") if args.variant == LEAN else ()
    args.pinned_package_count = verify_pinned_packages(args.image_id, allow_add_ons=args.variant == CURATED,
                                                     required_packages=providers)
    with tempfile.TemporaryDirectory(prefix="elsa-profile-smoke-") as temporary:
        directory = Path(temporary)
        args.env_file = directory / "smoke.env"
        args.password = f"Smoke1!{secrets.token_hex(16)}"
        settings = {
            "FoundationIdentityAspNetCoreIdentityEntityFrameworkCore__SeedAdminPassword": args.password,
            "FoundationIdentityAspNetCoreIdentityEntityFrameworkCore__IsDevelopmentOrDemo": "true",
        }
        settings["WorkflowsRuntimeArtifactsEntityFrameworkCorePersistence__RecoveryContinuationSigningKey"] = secrets.token_hex(32)
        if args.variant == CURATED:
            settings["Secrets__EncryptionKey"] = secrets.token_hex(32)
        lines = [f"{SHELL_FEATURES}{name}={value}\n" for name, value in settings.items()]
        if args.variant == CURATED:
            args.management_key = secrets.token_hex(32)
            lines.append(f"Elsa__ModuleManagement__ApiKey={args.management_key}\n")
        args.env_file.write_text("".join(lines))

        if args.variant != LEAN:
            args.expect_trigger.extend(ALL_OPTIONAL_TRIGGER_TYPES)
            smoke(args)
            if args.variant == "elsa3-import":
                smoke_migrated_files(args, directory)
            return

        args.omit_trigger.extend(ALL_OPTIONAL_TRIGGER_TYPES)
        smoke(args)
        args.tls_directory = directory / "tls"
        args.tls_directory.mkdir()
        cert, key = (args.tls_directory / filename for filename in ("cert.pem", "key.pem"))
        subprocess.run(["openssl", "req", "-x509", "-newkey", "rsa:2048", "-nodes", "-days", "1",
                        "-subj", "/CN=127.0.0.1", "-addext", "subjectAltName=IP:127.0.0.1",
                        "-out", str(cert), "-keyout", str(key)], capture_output=True, check=True)
        # The container user reads the fixture key through a read-only mount. The parent temp directory is private.
        key.chmod(0o644)
        sqlite = copy.copy(args)
        if args.log:
            sqlite.log = args.log.with_suffix(".sqlite.log")
        smoke(sqlite, preset="sqlite")
        shell_file = read_image_file(args.image_id, "/app/shells.json")
        for trigger, feature_names in OPTIONAL_TRIGGER_FEATURES.items():
            mounted = copy.copy(args)
            enabled_types = OPTIONAL_TRIGGER_TYPES[trigger]
            mounted.expect_trigger = [*args.expect_trigger, *enabled_types]
            mounted.omit_trigger = [name for name in args.omit_trigger if name not in enabled_types]
            mounted.shells = directory / f"{trigger}.json"
            if args.log:
                mounted.log = args.log.with_suffix(f".{trigger}-enabled.log")
            composition = json.loads(shell_file)
            features = composition["CShells"]["Shells"]["default"]["Features"]
            for name in OPTIONAL_TRIGGER_FEATURE_NAMES:
                features.pop(name, None)
            features.update({name: {} for name in feature_names})
            mounted.shells.write_text(json.dumps(composition))
            if trigger == "cron":
                smoke(mounted, preset="sqlite", verify_resume=True)
                verify_postgres_preset(args, directory, mounted)
            else:
                smoke(mounted)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("image", help="locally available image reference")
    parser.add_argument("--env-file", type=Path, help="Docker env file with local test configuration")
    parser.add_argument("--username", default="admin", help="local seeded username")
    parser.add_argument("--variant", choices=("essentials", "elsa3-import", CURATED),
                        help="verify a built profile with generated local test configuration")
    parser.add_argument("--shells", type=Path, help="read-only shells.json override")
    parser.add_argument("--expect-activity", action="append", default=[], help="CLR type key required in the catalog")
    parser.add_argument("--omit-activity", action="append", default=[], help="CLR type key required absent from the catalog")
    parser.add_argument("--expect-trigger", action="append", default=[], help="standalone trigger type required in the catalog")
    parser.add_argument("--omit-trigger", action="append", default=[], help="standalone trigger type required absent from the catalog")
    parser.add_argument("--log", type=Path, help="save the container log, including on failure")
    parser.add_argument("--ready-only", action="store_true", help="measure readiness without running the workflow")
    parser.add_argument("--timeout", type=int, default=120, help="maximum startup wait in seconds")
    args = parser.parse_args()
    if args.variant and (args.env_file or args.shells or args.ready_only):
        parser.error("--variant generates its own test configuration and always runs a workflow")
    args.password = os.environ.get("ELSA_SMOKE_PASSWORD", "Password123!")
    try:
        args.image_id = docker("image", "inspect", "--format", "{{.Id}}", args.image)
        if args.variant:
            verify_profile(args)
        else:
            smoke(args)
    except subprocess.CalledProcessError as error:
        parser.exit(1, f"Docker command failed ({error.returncode}): {error.stderr}\n")
    except (RuntimeError, KeyError, ValueError) as error:
        parser.exit(1, f"Smoke test failed: {error}\n")


if __name__ == "__main__":
    main()
