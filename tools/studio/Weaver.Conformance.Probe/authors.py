"""Build independent authors from retained distributions; never starts application hosts.

build_authors(run) needs the normal ConformanceRun APIs plus completed product
packages/sdk/producer metadata. It returns run.module_packages, leaving product
run.packages unchanged. Receipts go to report.authors.{protocol,backend,studio}
and report.modules; detailed JSON/log/TRX files stay outside author roots.
Each invocation requires a fresh owned run root and pnpm 11.9.0 on normal PATH.
"""
from __future__ import annotations

import base64
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import tarfile
import xml.etree.ElementTree as ET
import zipfile

from artifacts import REPO, check, package_identity, sha, write_json

MODULE_IDS = ("Acme.Weaver.ProtocolFixtures", "Acme.Home.Weaver", "Acme.Home.Studio")


def _ref(path):
    path = Path(path)
    check(path.is_file(), "Missing retained author file: " + str(path))
    return {"path": str(path), "sha256": sha(path)}


def _receipt(run, name, value):
    path = run.root / "author-evidence" / (name + ".json")
    write_json(path, value)
    return _ref(path)


def _environment(run, name):
    prefixes = ("anthropic", "copilot", "foundationagent", "githubcopilot", "studio__", "cshells__", "nuplane__", "qualification__")
    env = {key: value for key, value in os.environ.items() if not key.lower().startswith(prefixes)}
    env.update(NUGET_PACKAGES=str(run.root / (name + "-nuget-cache")),
               NUGET_HTTP_CACHE_PATH=str(run.root / (name + "-http-cache")), DOTNET_NOLOGO="1")
    return env


def _configure(run, source):
    run.config(source)
    path = source / "NuGet.Config"
    document = ET.parse(path)
    mapping = document.getroot().find("packageSourceMapping")
    check(mapping is not None, "Author needs normal NuGet package source mappings")
    group = next((item for item in mapping if item.attrib.get("key") == "weaver-candidate"), None)
    check(group is not None, "Candidate feed mapping is missing")
    existing = {item.attrib.get("pattern") for item in group}
    for package_id in MODULE_IDS:
        if package_id not in existing:
            ET.SubElement(group, "package", pattern=package_id)
    document.write(path, encoding="unicode")


def _project(source):
    projects = list(source.glob("*.csproj"))
    check(len(projects) == 1, "Expected one independent author project")
    tree = ET.parse(projects[0])
    check(not list(tree.iter("ProjectReference")), "Independent author cannot use project references")
    check(not list(tree.iter("Import")), "Independent author cannot import product build implementation")
    return projects[0]


def _closure(run, source, env):
    assets_path = source / "obj/project.assets.json"
    graph = json.loads(assets_path.read_text())
    cache = Path(env["NUGET_PACKAGES"]).resolve()
    check(set(Path(path).resolve() for path in graph["packageFolders"]) == {cache}, "Author package cache is not isolated")
    check(graph["libraries"] and all(item["type"] == "package" for item in graph["libraries"].values()), "Author closure contains source/project references")
    candidates = {key.lower(): value for key, value in {**run.packages, **run.module_packages}.items()}
    cached = []
    for identity, library in graph["libraries"].items():
        package_id, version = identity.rsplit("/", 1)
        directory = cache / library["path"]
        archive_path = directory / (package_id.lower() + "." + version.lower() + ".nupkg")
        check(archive_path.resolve().is_relative_to(cache), "Author archive escapes its isolated cache")
        # NuGet's graph uses the signature-independent contentHash from its extraction
        # metadata. The .nupkg.sha512 sidecar hashes the actual downloaded archive,
        # including its repository signature. Verify and retain both native identities.
        # https://github.com/NuGet/Home/wiki/Nupkg-Metadata-File
        hash_path = archive_path.with_suffix(archive_path.suffix + ".sha512")
        metadata_path = directory / ".nupkg.metadata"
        archive_hash = base64.b64encode(hashlib.sha512(archive_path.read_bytes()).digest()).decode()
        check(archive_hash == hash_path.read_text().strip(), "Cached NuGet archive hash mismatch: " + identity)
        metadata = json.loads(metadata_path.read_text())
        check(metadata["contentHash"] == library["sha512"], "Cached NuGet content hash mismatch: " + identity)
        expected = candidates.get(package_id.lower())
        if expected:
            check(version == expected["version"] and sha(archive_path) == expected["sha256"], "Author restored a different candidate archive: " + identity)
            with zipfile.ZipFile(archive_path) as archive:
                for entry in archive.namelist():
                    if entry.startswith("lib/") and entry.endswith(".dll"):
                        check(hashlib.sha256(archive.read(entry)).hexdigest() == sha(directory / entry), "Cached candidate DLL differs from its archive")
        cached.append({"identity": identity, "archive": _ref(archive_path), "sha512": library["sha512"],
                       "archiveSha512": archive_hash, "archiveHashSidecar": _ref(hash_path), "nugetMetadata": _ref(metadata_path)})
    return {"source": run.source, "status": "passed", "directory": str(source), "sourceReferences": [],
            "assets": _ref(assets_path), "lock": _ref(source / "packages.lock.json"),
            "packageFolders": [str(cache)], "cachedArchives": cached}


def _restore(run, tag, source, env):
    project = _project(source)
    arguments = [str(project), "--configfile", str(source / "NuGet.Config"), "--no-http-cache"]
    run.run(tag + "-restore", [run.dotnet, "restore", *arguments, "--use-lock-file"], source, env)
    lock_hash = sha(source / "packages.lock.json")
    run.run(tag + "-locked-restore", [run.dotnet, "restore", *arguments, "--locked-mode"], source, env)
    check(sha(source / "packages.lock.json") == lock_hash, "Locked author restore changed its lock")
    return _closure(run, source, env)


def _pack(run, tag, source, env, package_id, version, *, register=True):
    project = _project(source)
    path = run.feed / (package_id + "." + version + ".nupkg")
    check(not path.exists(), "Refuse to overwrite a retained module archive: " + str(path))
    run.run(tag + "-pack", [run.dotnet, "pack", str(project), "-c", "Release", "--no-restore", "-o", str(run.feed)], source, env)
    metadata = package_identity(path)
    check(metadata["id"] == package_id and metadata["version"] == version, "Module package identity mismatch")
    assembly = next((node.text for node in ET.parse(project).iter("AssemblyName")), project.stem)
    entry = "lib/net10.0/" + assembly + ".dll"
    with zipfile.ZipFile(path) as archive:
        names = archive.namelist()
        check(not any(name.startswith(("content/", "contentFiles/")) for name in names), "Module packed author content/configuration")
        forbidden = {"nuget.config", "global.json", "package.json", "pnpm-lock.yaml", "stages.json"}
        check(not any(Path(name).name.lower() in forbidden or "receipt" in Path(name).name.lower() for name in names), "Module packed author metadata")
        built_dll = source / "bin/Release/net10.0" / (assembly + ".dll")
        check(hashlib.sha256(archive.read(entry)).hexdigest() == sha(built_dll), "Packed author DLL differs from actual build")
        assets = {}
        for file in (source / "wwwroot").rglob("*"):
            if file.is_file():
                asset_entry = "staticwebassets/" + file.relative_to(source / "wwwroot").as_posix()
                check(asset_entry in names and hashlib.sha256(archive.read(asset_entry)).hexdigest() == sha(file), "Packed browser asset differs from actual build")
                assets[asset_entry] = _ref(file)
        if assets:
            javascript = "\n".join(archive.read(name).decode() for name in assets if name.endswith(".js"))
            check(re.search(r"from\s*['\"]@elsa-workflows/studio-sdk['\"]", javascript), "Browser module bundled/aliased its SDK instead of using the shared import")
        for name in names:
            if name.endswith((".js", ".css", ".json", ".props", ".targets")):
                contents = archive.read(name)
                check(str(REPO).encode() not in contents and str(run.sdk).encode() not in contents, "Module archive embeds source/SDK paths")
    if register:
        run.module_packages[package_id] = metadata
        run.report["modules"] = dict(run.module_packages)
        run.save()
    return {"package": metadata, "assembly": assembly, "builtDll": _ref(built_dll), "archiveEntry": entry, "browserAssets": assets}


def _tests(run, tag, source, package_id, packed, version, expected):
    _configure(run, source)
    env = _environment(run, tag)
    record = _restore(run, tag, source, env)
    destination = run.root / "author-evidence" / (tag + "-results")
    run.run(tag + "-test", [run.dotnet, "test", str(_project(source)), "-c", "Release", "--no-restore",
                            "--logger", "trx;LogFileName=contracts.trx", "--results-directory", str(destination)], source, env)
    trx = destination / "contracts.trx"
    document = ET.parse(trx)
    namespace = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
    counts = {key: int(value) for key, value in document.find("t:ResultSummary/t:Counters", namespace).attrib.items()}
    check(counts["total"] == counts["executed"] == counts["passed"] == expected and
          all(value == 0 for key, value in counts.items() if key not in {"total", "executed", "passed"}), "Author tests failed, skipped, or changed expected count")
    rows = document.findall("t:Results/t:UnitTestResult", namespace)
    check(len(rows) == expected and all(row.attrib["outcome"] == "Passed" for row in rows), "Author test result rows are incomplete")
    cache = Path(env["NUGET_PACKAGES"]) / package_id.lower() / version.lower()
    cached_package = cache / (package_id.lower() + "." + version.lower() + ".nupkg")
    cached_dll = cache / packed["archiveEntry"]
    test_dll = source / "bin/Release/net10.0" / (packed["assembly"] + ".dll")
    check(sha(cached_package) == packed["package"]["sha256"] and sha(cached_dll) == sha(test_dll) == packed["builtDll"]["sha256"], "Tests did not consume the retained packed fixture")
    record.update(counts=counts, skipped=0, retried=0, flaky=0, trx=_ref(trx),
                  cachedPackage=_ref(cached_package), cachedDll=_ref(cached_dll), testDll=_ref(test_dll))
    return record


def _sdk_graph(run, source, env):
    client = source / "Client"
    package = json.loads((client / "package.json").read_text())
    check(package["packageManager"] == "pnpm@11.9.0", "Author requires pnpm 11.9.0")
    check(package["dependencies"]["@elsa-workflows/studio-sdk"] == "file:" + str(run.sdk), "Author does not reference the actual SDK tarball")
    run.run("authors-studio-pnpm-version", ["pnpm", "--version"], client, env)
    check((run.root / "authors-studio-pnpm-version.log").read_text().splitlines()[-1].strip() == "11.9.0", "Actual author pnpm version differs")
    install = ["pnpm", "install", "--store-dir", str(run.root / "authors-pnpm-store")]
    run.run("authors-studio-lock", [*install, "--lockfile-only"], client, env)
    lock = client / "pnpm-lock.yaml"
    lock_hash = sha(lock)
    run.run("authors-studio-frozen-install", [*install, "--frozen-lockfile"], client, env)
    check(sha(lock) == lock_hash, "Frozen frontend install changed its lock")
    lock_text = lock.read_text()
    integrity = "sha512-" + base64.b64encode(hashlib.sha512(run.sdk.read_bytes()).digest()).decode()
    check("specifier: file:" + str(run.sdk) in lock_text and integrity in lock_text, "Actual frontend graph does not lock retained SDK bytes")
    check("workspace:" not in lock_text and "link:" not in lock_text, "External frontend graph contains workspace/source links")
    installed = client / "node_modules/@elsa-workflows/studio-sdk"
    check(installed.resolve().is_relative_to(run.root.resolve()), "Installed SDK resolves outside the owned author")
    with tarfile.open(run.sdk) as archive:
        for item in archive.getmembers():
            if item.isfile():
                check(item.name.startswith("package/") and ".." not in Path(item.name).parts, "Unsafe SDK archive member")
                actual = installed / item.name.removeprefix("package/")
                check(hashlib.sha256(archive.extractfile(item).read()).hexdigest() == sha(actual), "Installed SDK differs from actual tarball: " + item.name)
    source_refs = []
    for file in (client / "src").rglob("*"):
        if file.is_file() and file.suffix in {".ts", ".tsx", ".js", ".jsx"}:
            for value in re.findall(r"(?:from\s*|import\s*\(?\s*)['\"]([^'\"]+)['\"]", file.read_text()):
                if value.startswith(("/", "workspace:", "file:")) or (value.startswith(".") and not (file.parent / value).resolve().is_relative_to(client.resolve())):
                    source_refs.append(value)
    config = json.loads((client / "tsconfig.json").read_text())
    options = config.get("compilerOptions", {})
    check(not source_refs and not options.get("paths") and not options.get("baseUrl"), "External frontend imports source or workspace aliases")
    run.run("authors-studio-typecheck", ["pnpm", "typecheck"], client, env)
    run.run("authors-studio-build", ["pnpm", "build"], client, env)
    return {"sdkSha256": sha(run.sdk), "sdkTgz": str(run.sdk), "installedSdk": _ref(installed / "package.json"),
            "sourceReferences": source_refs, "workspaceImports": [], "pnpmVersion": "11.9.0",
            "pnpmLock": _ref(lock), "packageJson": _ref(client / "package.json"), "allInstalledSdkFilesMatchArchive": True}


def build_authors(run):
    """Execute fresh real package authors/tests; runtime case status remains untouched."""
    check(re.fullmatch(r"[0-9a-f]{40}", run.source) and run.packages, "Produce committed product distributions first")
    check(run.report["producer"]["source"] == run.source, "Product and module author source identities differ")
    check(sha(run.sdk) == run.report["producer"]["sdk"]["sha256"], "Retained SDK changed before module authorship")
    check(not getattr(run, "module_packages", {}), "Use a fresh run for module authorship")
    product_snapshot = json.dumps(run.packages, sort_keys=True)
    run.module_packages = {}
    run.report["modules"] = {}
    authors = run.report.setdefault("authors", {})
    version = "1.0.0-witness.g" + run.source[:8]
    replacements = {"__FIXTURE_VERSION__": version}
    for name, fixture, package_id, count in (("protocol", "ProviderProtocolFixtures", MODULE_IDS[0], 21),
                                              ("backend", "Resource.Backend", MODULE_IDS[1], 78),
                                              ("studio", "Resource.Studio", MODULE_IDS[2], None)):
        authors[name] = {"source": run.source, "status": "building"}
        run.save()
        source = run.materialize("author-" + name, fixture, replacements)
        if name == "protocol":
            shutil.rmtree(source / "Tests")  # Tests are independently materialized below.
        _configure(run, source)
        env = _environment(run, "authors-" + name)
        graph = _sdk_graph(run, source, env) if name == "studio" else None
        record = _restore(run, "authors-" + name, source, env)
        packed = _pack(run, "authors-" + name, source, env, package_id, version)
        if count:
            test_fixture = "ProviderProtocolFixtures/Tests" if name == "protocol" else "Resource.Backend.Tests"
            tests = run.materialize("author-" + name + "-tests", test_fixture, replacements)
            record["tests"] = _tests(run, "authors-" + name + "-tests", tests, package_id, packed, version, count)
        else:
            check(any(key.endswith(".js") for key in packed["browserAssets"]) and any(key.endswith(".css") for key in packed["browserAssets"]), "RCL lacks actual prebuilt JS/CSS")
            record.update(sdkSha256=sha(run.sdk), graphAudit=_receipt(run, "studio-sdk-graph", graph))
        record.update(packed=packed, status="passed", fixtureVersion=version)
        record["receipt"] = _receipt(run, name + "-author", record)
        authors[name] = record
        check(json.dumps(run.packages, sort_keys=True) == product_snapshot, "Module metadata contaminated product package map")
        run.save()
    return run.module_packages


def build_incompatible_studio(run):
    """Compile a real unsupported-admission variant; never rewrite an existing archive.

    Reuse only the independently compiled author's retained browser assets. The
    variant gets its own NuGet version, cache, locks and compiled module attribute.
    The normal archive/author records remain authoritative for restoration.
    Native rejection and restored positives must still be observed by the driver.
    """
    original = run.report["authors"]["studio"]
    check(original["status"] == "passed", "Build the actual SDK author before its incompatible variant")
    check("incompatibleStudio" not in run.report["authors"], "Use a fresh run for the incompatible variant")
    version = original["fixtureVersion"] + ".incompatible"
    source = run.materialize("author-studio-incompatible", "Resource.Studio", {
        "__FIXTURE_VERSION__": version, "__REQUIRED_SDK_VERSION__": "999.0.0"})
    # These exact assets already passed external SDK typecheck/build/pack audits.
    for entry, retained in original["packed"]["browserAssets"].items():
        file = Path(retained["path"])
        check(sha(file) == retained["sha256"], "Original authored browser asset changed")
        relative = entry.removeprefix("staticwebassets/")
        check(relative != entry and ".." not in Path(relative).parts, "Unexpected author browser archive entry")
        target = source / "wwwroot" / relative
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(file, target)
    _configure(run, source)
    env = _environment(run, "authors-studio-incompatible")
    record = _restore(run, "authors-studio-incompatible", source, env)
    normal_modules = json.dumps(run.module_packages, sort_keys=True)
    packed = _pack(run, "authors-studio-incompatible", source, env, MODULE_IDS[2], version, register=False)
    check(json.dumps(run.module_packages, sort_keys=True) == normal_modules, "Variant replaced the normal restoration archive")
    check(packed["builtDll"]["sha256"] != original["packed"]["builtDll"]["sha256"], "Variant did not compile a distinct admission declaration")
    check({key: value["sha256"] for key, value in packed["browserAssets"].items()} ==
          {key: value["sha256"] for key, value in original["packed"]["browserAssets"].items()}, "Variant browser implementation differs")
    record.update(status="passed", packed=packed, requiredSdkVersion="999.0.0", fixtureVersion=version,
                  originalArchive=original["packed"]["package"], runtimeRejectionObserved=False)
    record["receipt"] = _receipt(run, "studio-incompatible-author", record)
    run.report["authors"]["incompatibleStudio"] = record
    run.save()
    return packed["package"]
