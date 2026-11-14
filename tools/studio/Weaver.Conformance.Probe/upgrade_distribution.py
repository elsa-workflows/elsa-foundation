"""T038 package prerequisites, never an upgrade qualification or witness promotion.

Product source and driver/fixture source are deliberately separate identities. The
historical product is archived verbatim; current committed fixtures are external
authors compiled independently against each distribution. Nothing here starts a
host, fabricates a proposal, changes a schema record or consumes provider secrets.
"""
from __future__ import annotations

import json
from pathlib import Path
import shutil
import subprocess

from artifacts import (BACKEND_PACKAGES, FIXTURES, REPO, ConformanceRun, check,
                       pack_product_projects, package_identity, sha, write_json)
from authors import _closure, _environment, _pack, _project, _ref, _restore
from consumers import (COMMON_EFFECTS_FEATURE, DURABLE_EFFECTS_FEATURE, DURABLE_STORE_FEATURE,
                       consumer_binary_provenance, enable_durable_weaver_store, prepare_consumers,
                       select_native_archives, stage_native_archives)
from runtime import private_json

BASELINE_SOURCE = "dff7c490d0e5501b22d4ba89beb389c956d4f423"
BASELINE_TREE = "9664f7ff693ce2f244f6e27fdd63b2aeb1e70330"
SOURCE_FETCH_TIMEOUT_SECONDS = 60
RESOURCE_PACKAGE = "Acme.Weaver.UpgradeResource"
RESOURCE_FEATURE = "AcmeWeaverUpgradeResource"
RESOURCE_MODULE = "Acme.Upgrade"
RESOURCE_OWNER = "/_qualification/acme/upgrade-widget"
RESOURCE_TOOLS = {"read": "acme_upgrade_read_widget", "write": "acme_upgrade_set_visibility",
                  "admin": "acme_upgrade_admin_visibility"}
JOURNAL_HEADER = '{"Format":"acme.home.execution-entries","Version":2}'
OPERATOR_ASSEMBLY = "Acme.Weaver.UpgradeOperator.dll"


def _product_snapshot(run):
    return json.dumps(run.packages, sort_keys=True)


def _ensure_product_source(distribution, revision):
    """Acquire only a missing pinned commit in a shallow checkout, without moving refs.

    Fetch writes Git's object/shallow metadata only; no checkout, branch, tag or
    FETCH_HEAD is changed. Both source cleanliness checks remain mandatory.
    """
    distribution.assert_immutable_inputs()
    probe = ["git", "-c", "core.fsmonitor=false", "cat-file", "-e", revision + "^{commit}"]
    present = subprocess.run(probe, cwd=REPO, capture_output=True, timeout=10).returncode == 0
    acquired = False
    if not present:
        check(revision == BASELINE_SOURCE, "Only the exact pinned historical writer may be acquired")
        distribution.run("acquire-pinned-product-source", ["git", "-c", "core.fsmonitor=false", "fetch",
                         "--no-tags", "--no-write-fetch-head", "--depth=1", "origin", BASELINE_SOURCE], REPO,
                         timeout_seconds=SOURCE_FETCH_TIMEOUT_SECONDS)
        check(subprocess.run(probe, cwd=REPO, capture_output=True, timeout=10).returncode == 0,
              "The exact pinned historical writer is still absent after bounded acquisition")
        acquired = True
    distribution.assert_immutable_inputs()
    return {"acquired": acquired, "commit": revision,
            "fetchLog": _ref(distribution.root / "acquire-pinned-product-source.log") if acquired else None}


def _source_identity(distribution, revision):
    acquisition = _ensure_product_source(distribution, revision)
    distribution.run("product-source-identity", ["git", "-c", "core.fsmonitor=false", "show", "--no-patch",
                     "--format=%H%n%T", revision], REPO)
    lines = (distribution.root / "product-source-identity.log").read_text().splitlines()
    check(len(lines) == 2 and lines[0] == revision and len(lines[1]) == 40,
          "Historical writer source must resolve to the exact pinned commit and tree")
    check(revision != BASELINE_SOURCE or lines[1] == BASELINE_TREE, "Pinned historical writer tree differs")
    return {"commit": lines[0], "tree": lines[1], "acquisition": acquisition,
            "identityLog": _ref(distribution.root / "product-source-identity.log")}


def _archive_product(distribution, revision):
    identity = _source_identity(distribution, revision)
    archive = distribution.root / "source.tar"
    producer = distribution.root / "producer"
    producer.mkdir()
    distribution.run("archive", ["git", "-c", "core.fsmonitor=false", "archive", "--format=tar",
                     "--output=" + str(archive), revision], REPO)
    distribution.run("extract", ["tar", "-xf", str(archive), "-C", str(producer)], REPO)
    distribution.package_source_root = producer
    return producer, dict(identity, archive=_ref(archive))


def _author(distribution, name, fixture, package_id, version):
    source = distribution.materialize("author-" + name, fixture, {"__FIXTURE_VERSION__": version})
    if name == "protocol":
        shutil.rmtree(source / "Tests")
    else:
        # Reuse the independently reviewed durable execution observer verbatim.
        # Its settings/allowed-tool names are supplied by this separate legacy fixture.
        journal = FIXTURES / "Resource.Backend/WidgetExecutionJournal.cs.in"
        shutil.copy2(journal, source / "WidgetExecutionJournal.cs")
    env = _environment(distribution, "authors-" + name)
    record = _restore(distribution, "authors-" + name, source, env)
    packed = _pack(distribution, "authors-" + name, source, env, package_id, version)
    record.update(packed=packed, fixtureVersion=version, productSource=distribution.report["productSource"],
                  sourceFiles={str(path.relative_to(source)): _ref(path) for path in source.rglob("*")
                               if path.is_file() and not {"obj", "bin"}.intersection(path.relative_to(source).parts)},
                  fixtureSource=distribution.source, tests={"status": "not-run"}, runtimeObserved=False)
    if name == "upgrade-resource":
        record["tests"].update(fixture="Resource.LegacyUpgrade.Tests", expectedCases=12)
    distribution.report.setdefault("authors", {})[name] = record
    distribution.save()
    return record


def _new_distribution(run, name, version):
    distribution = ConformanceRun(run.root / ("upgrade-" + name), version)
    check(distribution.source == run.source, "Upgrade driver source differs from the current witness")
    distribution.module_packages = {}
    distribution.report.update(scope="T038-package-prerequisites", generation=name,
                               qualified=False, independentPackageWitness=False, runtimeObserved=False)
    return distribution


def _prepare_operator(distribution):
    """Publish a package-only operator, without launching it or opening a database."""
    source = distribution.materialize("upgrade-operator", "Upgrade.Operator", {
        "__EF_SQLITE_VERSION__": distribution.pin("Microsoft.EntityFrameworkCore.Sqlite", scope="backend")})
    for ancestor in source.parents:
        check(not any((ancestor / name).exists() for name in ("Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props")),
              "External operator would inherit ambient build configuration")
    env, audit = distribution.consumer_environment("upgrade-operator")
    audit.touch(exist_ok=False)
    closure = _restore(distribution, "upgrade-operator", source, env)
    project = _project(source)
    published = distribution.root / "upgrade-operator-published"
    distribution.run("upgrade-operator-build", [distribution.dotnet, "build", str(project), "-c", "Release", "--no-restore", "--nologo"], source, env)
    distribution.run("upgrade-operator-publish", [distribution.dotnet, "publish", str(project), "-c", "Release", "--no-restore", "--no-build",
                     "-o", str(published)], source, env)
    profile = {"published": published, "assembly": project.stem + ".dll"}
    check(profile["assembly"] == OPERATOR_ASSEMBLY, "Unexpected upgrade operator entry point")
    binaries = consumer_binary_provenance(profile, closure)
    from runtime import RUNTIME_ENVIRONMENT, published_files, published_files_digest
    files = published_files(published)
    manifest = distribution.root / "operator-published-files.json"
    write_json(manifest, files)
    check(not audit.read_text().strip(), "Package-only operator preparation invoked frontend tools")
    record = {"status": "prepared", "source": distribution.source, "productSource": distribution.report["productSource"],
              "executed": False, "databaseOpened": False, "qualified": False, "independentPackageWitness": False,
              "allowedOperations": ["hold", "release", "status"], "family": "Weaver", "provider": "Sqlite",
              "published": str(published), "assembly": profile["assembly"], "commandPrefix": [distribution.dotnet, str(published / profile["assembly"])],
              "runtimeEnv": {key: value for key, value in env.items() if key in RUNTIME_ENVIRONMENT},
              "sourceFiles": {str(path.relative_to(source)): _ref(path) for path in source.rglob("*")
                              if path.is_file() and not {"obj", "bin"}.intersection(path.relative_to(source).parts)},
              "closure": closure, "binaryProvenance": binaries, "publishedFiles": files,
              "publishedFilesManifest": _ref(manifest), "publishedFilesSha256": published_files_digest(files), "frontendAudit": _ref(audit),
              "tests": {"status": "not-run"}}
    distribution.report["upgradeOperator"] = record
    distribution.save()
    return record


def upgrade_operator_command(run, prepared, name, database):
    """Return a validated launch command; caller owns stdin, execution and outcome proof."""
    check(name in prepared and "operator" in prepared[name], "Select a prepared upgrade operator generation")
    item = prepared[name]
    operator = item["operator"]
    from runtime import owned_content_root, published_files
    root = owned_content_root(run, item["profile"])
    database = Path(database)
    check(database.is_absolute() and database.is_file() and not database.is_symlink()
          and database == database.resolve() and database.is_relative_to(root)
          and not any(character in str(database) for character in (';', '"', '\r', '\n')),
          "Operator requires an existing canonical owned runtime database path")
    published = Path(operator["published"])
    check(published.is_absolute() and published.resolve().is_relative_to(run.root.resolve())
          and operator["assembly"] == OPERATOR_ASSEMBLY
          and operator["commandPrefix"] == [run.dotnet, str(published / OPERATOR_ASSEMBLY)],
          "Operator command must name the exact owned prepared entry point")
    check(not root.is_relative_to(published) and not published.is_relative_to(root), "Operator publish and runtime content must not overlap")
    check(published_files(published) == operator["publishedFiles"], "Operator published inputs changed")
    return [*operator["commandPrefix"], "--owned-root", str(root), "--database", str(database)]


def prepare_upgrade_distributions(run):
    """Return old/current independently restored backend profiles, without launching them.

    Requires the current ordinary product distribution. Returned mutable profiles
    are never implicitly installed in run.profiles. Caller selects a generation
    only while its owned backend is stopped, using select_upgrade_generation().
    """
    run.assert_immutable_inputs()
    check(run.packages and run.report.get("producer", {}).get("source") == run.source,
          "Prepare the genuine committed current product distribution first")
    check("upgradeDistributions" not in run.report, "Never replace retained upgrade distribution evidence")
    current_products = _product_snapshot(run)
    result = {}
    section = {"status": "preparing", "source": run.source, "baselineSource": BASELINE_SOURCE,
               "qualified": False, "independentPackageWitness": False, "runtimeObserved": False, "generations": {}}
    run.report["upgradeDistributions"] = section
    run.save()
    try:
        for name in ("old", "current"):
            product_version = "4.0.0-upgradeold.g" + BASELINE_SOURCE[:12] if name == "old" else run.version
            distribution = _new_distribution(run, name, product_version)
            revision = BASELINE_SOURCE if name == "old" else run.source
            producer, identity = _archive_product(distribution, revision)
            distribution.report["productSource"] = identity
            if name == "old":
                distribution.packages, projects = pack_product_projects(distribution, producer, BACKEND_PACKAGES, revision)
                distribution.report["producer"] = {"source": revision, "backendOnly": True, "projectCount": len(projects)}
            else:
                # Retain exact existing current archives; do not rebuild or repack them.
                for package_id, item in run.packages.items():
                    source = Path(item["path"])
                    check(sha(source) == item["sha256"], "Current product archive changed")
                    target = distribution.feed / source.name
                    shutil.copy2(source, target)
                    distribution.packages[package_id] = package_identity(target)
                distribution.report["producer"] = {"source": revision, "backendConsumerOnly": True,
                                                     "retainedCurrentProducer": run.report["producer"]}
            distribution.report["packages"] = dict(distribution.packages)
            fixture_version = "1.0.0-upgrade." + name + ".g" + run.source[:12]
            protocol = _author(distribution, "protocol", "ProviderProtocolFixtures", "Acme.Weaver.ProtocolFixtures", fixture_version)
            resource = _author(distribution, "upgrade-resource", "Resource.LegacyUpgrade", RESOURCE_PACKAGE, fixture_version)
            prepare_consumers(distribution, names=("backend",), legacy=True)
            profile = distribution.profiles["backend"]
            consumer_closure = _closure(distribution, profile["source"], profile["env"])
            binaries = consumer_binary_provenance(profile, consumer_closure)
            operator = _prepare_operator(distribution)
            stage_native_archives(distribution, "backend", select_native_archives(
                distribution, "backend", [resource["packed"]["package"]], resource))
            # Root's runtime verifier supplies the canonical all-file manifest.
            from runtime import published_files, published_files_digest
            profile["publishedFiles"] = published_files(profile["published"])
            profile["productSource"] = identity
            profile["upgradeGeneration"] = name
            manifest = distribution.root / "published-files.json"
            write_json(manifest, profile["publishedFiles"])
            record = {"source": run.source, "productSource": identity, "version": distribution.version,
                      "status": "prepared", "qualified": False, "runtimeObserved": False,
                      "report": _ref(distribution.root / "report.json"),
                      "consumer": distribution.report["consumers"]["backend"],
                      "consumerClosure": consumer_closure, "consumerBinaryProvenance": binaries,
                      "operator": operator,
                      "publishedFiles": _ref(manifest), "publishedFilesSha256": published_files_digest(profile["publishedFiles"]),
                      "protocolAuthor": protocol, "resourceAuthor": resource}
            distribution.report["status"] = "prepared"
            distribution.save()
            record["report"] = _ref(distribution.root / "report.json")
            section["generations"][name] = record
            result[name] = {"profile": profile, "distribution": distribution, "record": record, "operator": operator}
            check(_product_snapshot(run) == current_products, "Old writer contaminated current candidate product metadata")
            run.save()
        section["status"] = "prepared"
    except BaseException as failure:
        section.update(status="failed", failure={"type": type(failure).__name__, "message": str(failure)[:1000]})
        raise
    finally:
        run.save()
    return result


def prepare_upgrade_content_root(run, prepared, path, *, encryption_key, lease_seconds=15, studio_host_id):
    """Create fresh runtime state once. Supply only a private, newly generated store key.

    No processes may be running. Native credentials/origins are configured later
    through configure_runtime after selecting the old profile. Binary publishes
    remain unchanged; no configuration containing secrets is copied back to them.
    """
    path = Path(path)
    check(path.is_absolute() and path.resolve().is_relative_to(run.root.resolve()) and not path.exists(),
          "Upgrade content root must be a new owned absolute path")
    check(set(prepared) == {"old", "current"} and isinstance(studio_host_id, str) and studio_host_id,
          "Both independently prepared generations and a fixed owner host are required")
    check(all(not ancestor.is_symlink() for ancestor in path.parents if ancestor.is_relative_to(run.root.resolve())),
          "Upgrade runtime content cannot traverse a symlink")
    check(isinstance(encryption_key, str) and len(encryption_key) >= 32 and type(lease_seconds) is int and 15 <= lease_seconds <= 86400,
          "Upgrade store needs a fresh private key and valid lease duration")
    for item in prepared.values():
        binary = item["profile"]["published"].resolve()
        check(not path.resolve().is_relative_to(binary) and not binary.is_relative_to(path.resolve()),
              "Upgrade runtime content cannot overlap immutable published binaries")
    path.mkdir(mode=0o700)
    for name in ("state", "runtime-feed", "native-store/installs", "protocol", "observations"):
        (path / name).mkdir(parents=True, exist_ok=True, mode=0o700)
    old = prepared["old"]["profile"]

    def rebase(value):
        if isinstance(value, str) and value.startswith(str(old["published"]) + "/"):
            return str(path) + value[len(str(old["published"])):]
        if isinstance(value, str) and value.startswith("Data Source=" + str(old["published"]) + "/"):
            return "Data Source=" + str(path) + value[len("Data Source=" + str(old["published"])):]
        if isinstance(value, dict):
            return {key: rebase(item) for key, item in value.items()}
        if isinstance(value, list):
            return [rebase(item) for item in value]
        return value

    for name in ("appsettings.json", "shells.json"):
        private_json(path / name, rebase(json.loads((old["published"] / name).read_text())))
    journal = path / "observations/execution-entries.jsonl"
    journal.write_text(JOURNAL_HEADER + "\n")
    journal.chmod(0o600)
    for item in prepared.values():
        profile = item["profile"]
        profile.update(contentRoot=path, runtimeFeed=path / "runtime-feed", nativeStore=path / "native-store",
                       appsettings=path / "appsettings.json", shells=path / "shells.json",
                       commandFile=path / "protocol/command.json",
                       traceFiles={provider: path / ("protocol/" + provider + ".jsonl") for provider in ("copilot", "anthropic")})
    private_json(old["commandFile"], {"commandId": "upgrade-readiness", "toolName": "no-tool", "arguments": {}})
    # Use existing durable-overlay validation; the 1.0 feature set contains no effects selection.
    previous = run.profiles.get("backend")
    run.profiles["backend"] = old
    try:
        selected = enable_durable_weaver_store(run, path / "state/weaver-records.db", encryption_key,
                                              lease_seconds=lease_seconds, effects=False)
    finally:
        if previous is None:
            run.profiles.pop("backend")
        else:
            run.profiles["backend"] = previous
    document = json.loads((path / "shells.json").read_text())
    document["CShells"]["Shells"]["Authoring"]["Features"][RESOURCE_FEATURE] = {
        "StudioHostId": studio_host_id, "ExecutionObservationFile": str(journal)}
    private_json(path / "shells.json", document)
    for item in prepared.values():
        item["profile"]["durableWeaverStore"] = dict(selected)
    return dict(selected, contentRoot=str(path), executionObservationFile=str(journal),
                resourceModule=RESOURCE_MODULE, ownerEndpoint=RESOURCE_OWNER, tools=dict(RESOURCE_TOOLS))


def select_upgrade_generation(run, prepared, name, *, effects=False):
    """Install a prepared profile/feed while stopped; never starts/stops a process.

    Caller must retain the actual restart receipt. This function does not attest
    process absence, successful admission, schema adoption or any upgrade outcome.
    """
    check(name in prepared and (name == "current" or not effects), "Old writer cannot select effects")
    profile = prepared[name]["profile"]
    check("contentRoot" in profile, "Prepare shared owned runtime content before selecting a generation")
    from runtime import owned_content_root, published_files
    check(published_files(profile["published"]) == profile["publishedFiles"], "Upgrade published inputs changed")
    root = owned_content_root(run, profile)
    feed = profile["runtimeFeed"]
    check(feed == root / "runtime-feed" and feed.is_dir() and not feed.is_symlink(), "Upgrade feed must be the owned runtime directory")
    expected = {}
    for generation in prepared.values():
        for entry in generation["profile"]["runtimeArchiveInputs"]:
            expected[Path(entry["path"]).name] = entry["sha256"]
    for entry in feed.iterdir():
        check(entry.is_file() and not entry.is_symlink() and expected.get(entry.name) == sha(entry),
              "Upgrade feed contains undeclared or changed files")
    inputs = []
    from consumers import _native_archive
    for entry in profile["runtimeArchiveInputs"]:
        source, identity, _ = _native_archive(run, dict(entry, path=entry["sourceArchive"]))
        inputs.append((entry, source, identity))
    settings = json.loads((root / "appsettings.json").read_text())
    shells = json.loads((root / "shells.json").read_text())
    features = shells["CShells"]["Shells"]["Authoring"]["Features"]
    check(DURABLE_STORE_FEATURE in features and COMMON_EFFECTS_FEATURE not in features,
          "Upgrade generation requires its real durable store and no memory effects selection")
    for entry in feed.iterdir():
        entry.unlink()
    staged = []
    for entry, source, identity in inputs:
        target = feed / source.name
        shutil.copy2(source, target)
        staged.append({"identity": identity, "sourceArchive": str(source), "path": str(target), "sha256": sha(target)})
    settings["Nuplane"]["HostProvidedPackages"] = list(profile["hostProvidedPackages"])
    private_json(root / "appsettings.json", settings)
    features.pop(DURABLE_EFFECTS_FEATURE, None)
    if effects:
        features[DURABLE_EFFECTS_FEATURE] = {}
    private_json(root / "shells.json", shells)
    profile["durableWeaverStore"]["effectsFeature"] = DURABLE_EFFECTS_FEATURE if effects else None
    run.profiles["backend"] = profile
    return {"generation": name, "productSource": profile["productSource"], "publishedDirectory": str(profile["published"]),
            "contentRoot": str(root), "runtimeArchiveInputs": staged, "effectsSelected": effects,
            "qualified": False, "runtimeObserved": False}
