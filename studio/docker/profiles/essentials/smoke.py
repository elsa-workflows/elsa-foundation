#!/usr/bin/env python3
"""Qualify a Studio Essentials profile through its isolated HTTP and add-on APIs."""

import argparse
from contextlib import contextmanager
from hashlib import sha256
from http.client import HTTPResponse
from io import BytesIO
import json
from pathlib import Path
import subprocess
import time
from urllib.parse import urlsplit
from uuid import uuid4
from zipfile import ZipFile


MODULES = "/_elsa/studio/modules"
REGISTRY = "/_elsa/module-management/registry"
UPLOAD = "/_elsa/module-management/packages/upload"
FEATURES = "/modularity/features"
ETL_MODULE = "Elsa.Studio.Etl"
ETL_FEATURE = "EtlStudio"
BUILT_INS = {"Elsa.Studio.Backends", "Elsa.Studio.FeatureManagement"}


def docker(*args, data=None):
    return subprocess.run(["docker", *args], input=data, capture_output=True, check=True, timeout=30).stdout


def require(condition, message):
    if not condition:
        raise RuntimeError(message)


def read_json(path):
    return json.loads(path.read_bytes())


class BufferedResponse:
    def __init__(self, data):
        self.data = data

    def makefile(self, mode):
        return BytesIO(self.data)


def request(container, path, method="GET", body=b"", headers=None):
    uri = urlsplit(path)
    require(not uri.scheme and not uri.netloc and path.startswith("/") and "\r" not in path and "\n" not in path,
            f"Invalid Studio asset path: {path}")
    header_lines = {"Host": "127.0.0.1:8080", "Connection": "close", **(headers or {})}
    if method == "POST":
        header_lines["Content-Length"] = str(len(body))
    head = "\r\n".join([f"{method} {path} HTTP/1.1", *(f"{key}: {value}" for key, value in header_lines.items()), "", ""])
    reply = docker("exec", "-i", container, "bash", "-ec",
                   "exec 3<>/dev/tcp/127.0.0.1/8080; cat >&3; cat <&3",
                   data=head.encode("ascii") + body)
    with HTTPResponse(BufferedResponse(reply)) as response:
        response.begin()
        return response.status, response.headers, response.read()


def expect(container, path, status=200, **kwargs):
    actual, headers, body = request(container, path, **kwargs)
    require(actual == status, f"{path} returned HTTP {actual}, expected {status}. {body[:240]!r}")
    return headers, body


def wait_json(container, path, ready, timeout):
    deadline = time.monotonic() + timeout
    last = "no response"
    while time.monotonic() < deadline:
        try:
            status, _, body = request(container, path)
        except subprocess.CalledProcessError as error:
            last = error.stderr.decode(errors="replace").strip()
        else:
            require(status in {200, 503}, f"{path} returned HTTP {status}. {body[:240]!r}")
            if status == 200:
                value = json.loads(body)
                if ready(value):
                    return value
            last = f"HTTP {status}. {body[:240]!r}"
        time.sleep(0.25)
    raise RuntimeError(f"{path} did not reach the expected state in {timeout}s. Last response: {last}")


@contextmanager
def run_isolated(image, logs, demo=False):
    settings = ["-e", "Logging__LogLevel__Nuplane=Information"]
    if demo:
        settings += ["-e", "Studio__Auth__Enabled=false"]
    container = docker("run", "-d", "--pull", "never", "--network", "none", *settings, image).decode().strip()
    try:
        yield container
    finally:
        with logs.open("wb") as log:
            subprocess.run(["docker", "logs", container], stdout=log, stderr=subprocess.STDOUT, check=False)
        subprocess.run(["docker", "rm", "-f", container], capture_output=True, check=False)


def module_ids(manifest):
    return [module["id"] for module in manifest["modules"]]


def wait_modules(container, expected, timeout):
    return wait_json(container, MODULES, lambda result: sorted(module_ids(result)) == sorted(expected), timeout)


def verify_assets(container, module, archive=None):
    module_id = module["id"]
    base = f"/_content/{module_id}/"
    directory = urlsplit(module["entry"]).path.rsplit("/", 1)[0]
    require(directory.startswith(base), f"Unexpected entry URL for {module_id}")
    require(module["styles"], f"No styles published for {module_id}")
    if archive:
        contract = json.loads(archive.read(f"staticwebassets/{directory[len(base):]}/entry-styles.json"))
        require(contract["version"] == 1 and module.get("stylesHaveContentHashes") is True,
                f"{module_id} did not publish the packaged hashed stylesheet contract")
        require(module["styles"] == [f"{directory}/{style}" for style in contract["styles"]],
                f"{module_id} stylesheet URLs differ from the package")
    results = []
    for path, media_type in [(module["entry"], "text/javascript"), *((style, "text/css") for style in module["styles"])]:
        headers, data = expect(container, path)
        require(headers.get_content_type() == media_type and data, f"{path} returned an empty or unexpected asset")
        digest = sha256(data).hexdigest()
        if archive:
            asset = urlsplit(path).path
            require(asset.startswith(base) and data == archive.read(f"staticwebassets/{asset[len(base):]}"),
                    f"{path} differs from the official archive")
            require(headers["ETag"] == '"' + digest + '"', f"{path} lacks the content ETag")
            revalidation, unchanged = expect(container, path, 304, headers={"If-None-Match": headers["ETag"]})
            require(not unchanged and revalidation["ETag"] == headers["ETag"], f"{path} cache revalidation failed")
        results.append({"path": path, "bytes": len(data), "sha256": digest})
    return results


def verify_default_auth(container, expected_modules, archives, timeout):
    manifest = wait_modules(container, expected_modules, timeout)
    _, html = expect(container, "/", headers={"Accept": "text/html"})
    require(b"<title>Elsa Studio</title>" in html, "The Studio shell did not return its index")
    for path in ("/studio/assets/index.js", "/studio/assets/index.css"):
        _, data = expect(container, path)
        require(data, f"{path} is empty")
    for path in (REGISTRY, FEATURES):
        expect(container, path, 401)
    expect(container, UPLOAD, 401, method="POST", body=b"--probe--\r\n",
           headers={"Content-Type": "multipart/form-data; boundary=probe"})
    expect(container, f"{FEATURES}/apply", 401, method="POST", body=b"{}",
           headers={"Content-Type": "application/json"})
    expect(container, "/_elsa/module-management/reconcile", 401, method="POST")
    assets = {}
    for module in manifest["modules"]:
        module_id = module["id"]
        if module_id in archives:
            with ZipFile(archives[module_id]) as archive:
                assets[module_id] = verify_assets(container, module, archive)
        else:
            assets[module_id] = verify_assets(container, module)
    return {"shell": 200, "managementRead": 401, "managementWrite": 401, "assets": assets}


def enabled_features(catalog):
    return {feature["id"] for feature in catalog["features"] if feature["enabled"]}


def upload_etl(container, package):
    boundary = uuid4().hex
    prefix = (f"--{boundary}\r\nContent-Disposition: form-data; name=\"package\"; filename=\"{package.name}\"\r\n"
              "Content-Type: application/octet-stream\r\n\r\n")
    body = prefix.encode("ascii") + package.read_bytes() + f"\r\n--{boundary}--\r\n".encode("ascii")
    expect(container, UPLOAD, method="POST", body=body, headers={"Content-Type": f"multipart/form-data; boundary={boundary}"})
    staged = docker("exec", container, "cat", f"/app/packages/{package.name}")
    require(staged == package.read_bytes(), "Studio did not stage the official ETL archive in its writable feed")


def enable_etl(container, catalog, timeout):
    for attempt in range(3):
        update = {"revision": catalog["revision"], "features": []}
        for feature in catalog["features"]:
            if feature["sourceKind"] == "manifest-error":
                continue
            enabled = feature["enabled"] or feature["id"] == ETL_FEATURE
            update["features"].append({"id": feature["id"], "enabled": enabled,
                                       "configuration": feature.get("configuration") or {} if enabled else {}})
        status, _, body = request(container, f"{FEATURES}/apply", method="POST", body=json.dumps(update).encode(),
                                  headers={"Content-Type": "application/json"})
        if 200 <= status < 300:
            return
        require(status in {409, 503} and attempt < 2, f"ETL enable returned HTTP {status}. {body[:240]!r}")
        time.sleep(1)
        catalog = wait_json(container, FEATURES, lambda result: True, timeout)


def verify_demo(container, expected_modules, expected_features, curated, etl, timeout):
    wait_modules(container, expected_modules, timeout)
    catalog = wait_json(container, FEATURES, lambda result: True, timeout)
    require(enabled_features(catalog) == expected_features, f"Unexpected enabled Studio features: {sorted(enabled_features(catalog))}")
    require(ETL_FEATURE not in {feature["id"] for feature in catalog["features"]}, "The ETL add-on was present before upload")
    registry = wait_json(container, REGISTRY, lambda result: True, timeout)
    admitted = {package["id"]: (package["version"], package["feedName"]) for package in registry["packages"]}
    require(admitted == curated and not registry["dropFolderPackages"], f"Unexpected first-boot package registry: {admitted}")
    feeds = {feed["name"]: (feed["directoryPath"], feed["directory"]["watch"]) for feed in registry["feeds"]}
    require(feeds == {"studio-essentials-packages": ("/studio-profile/packages", False),
                      "studio-local-packages": ("packages", True)}, f"Unexpected Studio feeds: {feeds}")
    upload_etl(container, etl)
    catalog = wait_json(container, FEATURES, lambda result: any(feature["id"] == ETL_FEATURE for feature in result["features"]), timeout)
    require(enabled_features(catalog) == expected_features, "ETL became enabled before the explicit feature apply")
    wait_modules(container, expected_modules, timeout)
    enable_etl(container, catalog, timeout)
    manifest = wait_modules(container, expected_modules | {ETL_MODULE}, timeout)
    catalog = wait_json(container, FEATURES, lambda result: enabled_features(result) == expected_features | {ETL_FEATURE}, timeout)
    registry = wait_json(container, REGISTRY, lambda result: True, timeout)
    loaded = {package["id"]: (package["version"], package["feedName"]) for package in registry["packages"]}
    version = next(iter(curated.values()))[0]
    require(loaded == {**curated, ETL_MODULE: (version, "studio-local-packages")}, f"Unexpected ETL package registry: {loaded}")
    with ZipFile(etl) as archive:
        assets = verify_assets(container, next(module for module in manifest["modules"] if module["id"] == ETL_MODULE), archive)
    return {"enabledFeatures": sorted(enabled_features(catalog)), "packages": loaded,
            "etlArchiveSha256": sha256(etl.read_bytes()).hexdigest(), "etlAssets": assets}


def qualify(args):
    image_id, platform = docker("image", "inspect", "--format", "{{.Id}} {{.Os}}/{{.Architecture}}", args.image).decode().split()
    package_data = read_json(args.context / "profile" / "nuplane.lock.json")["packages"]
    curated = {row["id"]: (row["version"], row["feed"]) for row in package_data}
    archives = {package_id: args.context / "profile" / "packages" / f"{package_id}.{version}.nupkg".lower()
                for package_id, (version, _) in curated.items()}
    expected_modules = set(curated) | BUILT_INS
    expected_features = set(read_json(args.context / "shells.json")["CShells"]["Shells"]["Default"]["Features"])
    expected_features.add("Events")  # Foundation enables this infrastructure feature in the shell.
    docker("run", "--rm", "--pull", "never", "--network", "none", "--entrypoint", "sh", image_id, "-ec",
           'test ! -w /studio-profile && test ! -w /studio-profile/packages && test -w /app/packages && '
           '{ test ! -d /app/.nuplane || test -z "$(find /app/.nuplane -type f -print -quit)"; }')
    with run_isolated(image_id, args.output / "default-auth.log") as container:
        default = verify_default_auth(container, expected_modules, archives, args.timeout)
    with run_isolated(image_id, args.output / "etl.log", demo=True) as container:
        demo = verify_demo(container, expected_modules, expected_features, curated, args.etl, args.timeout)
    return {"imageId": image_id, "platform": platform, "network": "none", "defaultAuth": default, "demo": demo}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("image")
    parser.add_argument("--context", type=Path, required=True)
    parser.add_argument("--etl", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--timeout", type=int, default=90)
    args = parser.parse_args()
    args.output.mkdir(parents=True, exist_ok=True)
    try:
        result = qualify(args)
    except (OSError, RuntimeError, subprocess.SubprocessError) as error:
        parser.exit(1, f"Studio profile smoke failed: {error}\n")
    (args.output / "smoke.json").write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    print(f"Qualified {result['imageId']} on {result['platform']}")


if __name__ == "__main__":
    main()
