#!/usr/bin/env python3
"""Exercise a fresh, network-isolated Elsa image through its authenticated workflow API."""

import argparse
import base64
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

from profile_acceptance import MIGRATED_FIXTURE, MIGRATED_PROFILE, verify_migrated_files, verify_variant


WRITE_LINE = "Elsa.Activities.Primitives.Activities.WriteLine"
LOGIN_PATH = "/_elsa/identity/login"
COOKIE_DIRECTORY = "/tmp/elsa-profile-smoke"
COOKIE_JAR = f"{COOKIE_DIRECTORY}/cookies"
COOKIE_HEADER = f"{COOKIE_DIRECTORY}/cookie-header"
OPTIONAL_TRIGGER_FEATURES = {
    "http": ("Http", "ActivitiesHttp", "WorkflowsRuntimeHttp", "StandaloneHttpTriggerAuthoring"),
    "cron": ("ActivitiesScheduling", "StandaloneScheduleTriggerAuthoring", "StandaloneScheduleTriggers"),
}
OPTIONAL_TRIGGER_TYPES = {"http": ("http",), "cron": ("cron", "timer")}
ALL_OPTIONAL_TRIGGER_TYPES = [kind for family in OPTIONAL_TRIGGER_TYPES.values() for kind in family]
OPTIONAL_TRIGGER_FEATURE_NAMES = {name for group in OPTIONAL_TRIGGER_FEATURES.values() for name in group}


def docker(*args, input_text=None, strip=True):
    output = subprocess.run(
        ["docker", *args], input=input_text, capture_output=True, text=True, check=True,
    ).stdout
    return output.strip() if strip else output


def request(container, path, data=None, *, authenticated=True):
    args = ["exec", "-i", container, "curl", "-sS", "--max-time", "5",
            "-w", "\n%{http_code}\n%{redirect_url}"]
    if authenticated:
        args.extend(["-H", f"@{COOKIE_HEADER}"])
    elif path == LOGIN_PATH:
        args.extend(["-c", COOKIE_JAR])
    if data is not None:
        args.extend(["-H", "Content-Type: application/json", "--data-binary", "@-"])
    args.append(f"http://127.0.0.1:8080{path}")
    response = docker(*args, input_text=json.dumps(data) if data is not None else None, strip=False)
    body, status, redirect = response.rsplit("\n", 2)
    return int(status), body, redirect


def read_json(container, path, data=None, *, authenticated=True):
    status, body, _ = request(container, path, data, authenticated=authenticated)
    if not 200 <= status < 300:
        raise RuntimeError(f"{path} returned HTTP {status}: {body}")
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
awk -v now="$3" -F '\t' '
    BEGIN { printf "Cookie: " }
    NF == 7 && ($1 == "127.0.0.1" || $1 == "#HttpOnly_127.0.0.1") &&
      $3 == "/" && $4 == "FALSE" && ($5 == 0 || $5 > now) {
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
    docker("exec", container, "mkdir", "-m", "700", COOKIE_DIRECTORY)
    login = read_json(container, LOGIN_PATH, {"username": args.username, "password": args.password}, authenticated=False)
    if login["status"] != "authenticated":
        raise RuntimeError(f"Identity did not report an authenticated session: {login['status']}")
    prepare_cookie_header(container)
    return unauthenticated_status, login["status"]


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
                "nodeId": "write-line", "activityVersionId": write_line[0]["headVersionId"],
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
                if instance["activityCount"] < 1 or instance["incidentCount"] != 0:
                    raise RuntimeError(f"Unexpected workflow activity or incident count: {instance}")
                if marker not in docker("logs", container).splitlines():
                    raise RuntimeError("WriteLine output was absent from container logs")
                return {
                    "unauthenticatedStatus": unauthenticated_status, "loginStatus": login_status,
                    "executionId": execution, "workflowStatus": instance["status"], "output": marker,
                    "standaloneTriggerTypes": trigger_types,
                }
            if instance["status"] in ("Faulted", "Cancelled"):
                raise RuntimeError(f"Workflow did not complete: {details}")
        elif status != 404:
            raise RuntimeError(f"Runtime instance returned HTTP {status}: {body}")
        time.sleep(0.25)
    raise RuntimeError(f"Workflow {execution} did not complete within 30s")


def smoke(args, migrated_directory=None):
    create = ["create", "--pull", "never", "--network", "none",
              "-e", "ASPNETCORE_ENVIRONMENT=Development"]
    if args.env_file:
        create.extend(["--env-file", str(args.env_file)])
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
                   "network": "none", "initialNuplaneCacheEmpty": True,
                   "readySeconds": ready_seconds, "readiness": readiness}
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
                lines = docker("logs", container).splitlines()
                if migrated_directory and results["migratedFiles"]["output"] not in lines:
                    raise RuntimeError("Output from the mounted catalog activity was absent from container logs")
                for line in lines:
                    if "not available in the runtime feature catalog" in line:
                        raise RuntimeError(line.strip())
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


def read_image_shells(image_id):
    return docker("run", "--rm", "--pull", "never", "--network", "none",
                  "--entrypoint", "cat", image_id, "/app/shells.json")


def smoke_migrated_files(args, directory):
    mounted = copy.copy(args)
    mounted.shells = directory / "migrated-files.json"
    if args.log:
        mounted.log = args.log.with_suffix(".migrated-files.log")
    composition = json.loads(read_image_shells(args.image_id))
    features = composition["CShells"]["Shells"]["default"]["Features"]
    readers = json.loads((MIGRATED_PROFILE / "migrated-files.features.json").read_text(encoding="utf-8"))
    features.update(readers)
    mounted.shells.write_text(json.dumps(composition))
    smoke(mounted, MIGRATED_FIXTURE)


def verify_profile(args):
    with tempfile.TemporaryDirectory(prefix="elsa-profile-smoke-") as temporary:
        directory = Path(temporary)
        args.env_file = directory / "smoke.env"
        args.password = f"Smoke1!{secrets.token_hex(16)}"
        feature = "CShells__Shells__default__Features__"
        settings = {
            "FoundationIdentityAspNetCoreIdentityEntityFrameworkCore__SeedAdminPassword": args.password,
            "FoundationIdentityAspNetCoreIdentityEntityFrameworkCore__IsDevelopmentOrDemo": "true",
            "WorkflowsRuntimeEntityFrameworkCore__RecoveryContinuationSigningKey": secrets.token_hex(32),
            "WorkflowsRuntimeEntityFrameworkCore__HierarchyCursorSigningKey": secrets.token_hex(32),
            "WorkflowsRuntimeApi__WorkflowAlterationPayloadProtectionActiveKeyId": "local-smoke",
            "WorkflowsRuntimeApi__WorkflowAlterationPayloadProtectionKeys__local-smoke":
                base64.b64encode(secrets.token_bytes(32)).decode("ascii"),
        }
        if args.variant == "full":
            settings["Secrets__EncryptionKey"] = secrets.token_hex(32)
        args.env_file.write_text("".join(f"{feature}{name}={value}\n" for name, value in settings.items()))

        if args.variant != "essentials":
            args.expect_trigger.extend(ALL_OPTIONAL_TRIGGER_TYPES)
            smoke(args)
            if args.variant == "elsa3-import":
                smoke_migrated_files(args, directory)
            return

        args.omit_trigger.extend(ALL_OPTIONAL_TRIGGER_TYPES)
        smoke(args)
        shell_file = read_image_shells(args.image_id)
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
            smoke(mounted)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("image", help="locally available image reference")
    parser.add_argument("--env-file", type=Path, help="Docker env file with local test configuration")
    parser.add_argument("--username", default="admin", help="local seeded username")
    parser.add_argument("--variant", choices=("essentials", "elsa3-import", "full"),
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
