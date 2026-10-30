"""External package-only hosts. Preparation never starts a process or claims runtime proof."""
from __future__ import annotations

import hashlib
import json
from pathlib import Path
import shutil
import xml.etree.ElementTree as ET
from xml.sax.saxutils import escape
import zipfile

from artifacts import BACKEND_PACKAGES, STUDIO_PACKAGES, check


BACKEND_EXTERNAL = (
    "CShells.AspNetCore", "CShells.Nuplane", "Nuplane", "Nuplane.Admin", "Nuplane.Loading",
    "Nuplane.Sources.Directory", "Microsoft.EntityFrameworkCore.Sqlite", "OpenIddict.EntityFrameworkCore",
)
STUDIO_EXTERNAL = ("CShells.Nuplane", "Nuplane", "Nuplane.Admin", "Nuplane.Loading", "Nuplane.Sources.Directory")
BACKEND_SHARED = (
    "Nuplane.Abstractions", "Nuplane.Loading.Abstractions", "CShells.Abstractions",
    "CShells.AspNetCore.Abstractions", "Elsa.Agent.Core", "Elsa.Foundation.Identity.Core",
    "Elsa.Studio.Preferences.Core", "Elsa.Agent.GitHubCopilot", "Elsa.Agent.Anthropic",
    "Microsoft.Extensions.AI.Abstractions",
)
STUDIO_SHARED = (
    "Nuplane.Abstractions", "Nuplane.Loading.Abstractions", "CShells.Abstractions",
    "CShells.AspNetCore.Abstractions", "Elsa.Studio.Core", "Elsa.Studio.Api",
)
PROTOCOL_FEATURES = {
    "copilot": ("GitHubCopilotAgent", "AcmeCopilotProtocolFixture"),
    "anthropic": ("AnthropicAgent", "AcmeAnthropicProtocolFixture"),
}


def sha(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def write_json(path, value):
    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, indent=2) + "\n")


def stage_native_archives(run, name, archives):
    """Admit explicit retained archive inputs to an owned native feed; no runtime cache probing.

    Each input is {path, sha256}; an optional identity must match the actual nuspec.
    The caller selects the needed closure from retained producer/author archive receipts.
    This does not reconcile, start a host, or write the run report.
    """
    profile = run.profiles[name]
    staged = profile.setdefault("runtimeArchiveInputs", [])
    for item in archives:
        source = Path(item["path"]).resolve()
        check(source.is_relative_to(run.root.resolve()) and source.is_file() and source.suffix.lower() == ".nupkg",
              "Native archive input must be an explicitly retained package under the owned run root")
        check(sha(source) == item["sha256"], "Native archive input hash differs from its retained receipt")
        with zipfile.ZipFile(source) as archive:
            specs = [entry for entry in archive.namelist() if entry.endswith(".nuspec")]
            check(len(specs) == 1, "Native archive has no unique nuspec")
            metadata = ET.fromstring(archive.read(specs[0]))
            values = {node.tag.rsplit("}", 1)[-1]: node.text for node in metadata.iter()
                      if node.tag.rsplit("}", 1)[-1] in {"id", "version"}}
        check(bool(values.get("id")) and bool(values.get("version")), "Native archive identity is incomplete")
        identity = values["id"] + "/" + values["version"]
        check(not item.get("identity") or item["identity"] == identity, "Native archive nuspec identity differs from its retained receipt")
        destination = profile["runtimeFeed"] / source.name
        check(not destination.exists() or sha(destination) == item["sha256"], "Native feed already contains different bytes for this archive")
        if not destination.exists():
            shutil.copy2(source, destination)
        record = {"identity": identity, "sourceArchive": str(source), "path": str(destination), "sha256": item["sha256"]}
        if record not in staged:
            staged.append(record)
    return staged


def references(run, products, externals):
    values = {}
    for package in products:
        check(package in run.packages, "Missing genuine consumer candidate: " + package)
        values[package] = run.packages[package]["version"]
    for package in externals:
        values[package] = run.pin(package)
    return "\n".join(f'    <PackageReference Include="{escape(package)}" Version="[{escape(version)}]" />'
                     for package, version in values.items())


def backend_features(published):
    published = Path(published)
    features = {name: {} for name in (
        "Primitives", "FileSystemDistributedLocking", "Events", "Mediator", "Tasks", "Serialization",
        "Expressions", "ExpressionsApi", "ApiCapabilities", "FoundationIdentityAbstractions", "FoundationIdentityApi",
        "StudioPreferences", "StudioPreferencesApi", "WorkflowsDesignApi", "WorkflowDesignValidations",
        "ActivitiesDesignApi", "ActivitiesDesignReconciliation", "ClrActivityReconciliation",
        "ActivitiesFlowchart", "ActivitiesPrimitives", "FoundationAgentAbstractions", "FoundationAgentApi",
        "FoundationWorkflowsAgent", "ModularityApi",
    )}
    for feature, database in (
        ("FoundationIdentityAspNetCoreIdentityEntityFrameworkCore", "iam.db"),
        ("StudioPreferencesEntityFrameworkCore", "preferences.db"),
        ("ActivitiesDesignEntityFrameworkCore", "activities.db"),
        ("WorkflowsDesignEntityFrameworkCore", "workflow-design.db"),
    ):
        features[feature] = {"Provider": "Sqlite", "ConnectionString": "Data Source=" + str(published / "state" / database)}
    features["FoundationIdentityAspNetCoreIdentityEntityFrameworkCore"].update(
        IsDevelopmentOrDemo=True, SeedAdminUserName="", SeedAdminPassword="", SeedAdminEmail="", SeedAdminRoleName="administrator")
    features["FoundationIdentityAspNetCoreIdentity"] = {"IsDefault": True, "AllowedReturnUrlOrigins": []}
    features["FoundationIdentityOpenIddict"] = {"IsDevelopmentOrDemo": True, "AutoMigrate": False}
    return features


def select_protocol_profile(run, name):
    """Select one actual adapter/fixture pair in runtime JSON, without rebuilding either consumer.

    The caller must stop its own backend before changing profiles. This helper never stops it.
    """
    check(name in PROTOCOL_FEATURES, "Unknown protocol profile: " + name)
    profile = run.profiles["backend"]
    path = profile["published"] / "shells.json"
    document = json.loads(path.read_text())
    features = document["CShells"]["Shells"]["Authoring"]["Features"]
    for provider, fixture in PROTOCOL_FEATURES.values():
        features.pop(provider, None)
        features.pop(fixture, None)
    provider, fixture = PROTOCOL_FEATURES[name]
    features[provider] = ({"Enabled": True, "RuntimeUrl": "http://127.0.0.1:1", "UseLoggedInUser": False,
                           "GitHubToken": "", "GitHubTokenEnvironmentVariable": "", "RuntimeConnectionToken": ""}
                          if name == "copilot" else
                          {"Enabled": True, "ApiKey": "protocol-fixture-only-not-a-credential", "ApiKeyEnvironmentVariable": ""})
    features[fixture] = {"CommandFile": str(profile["commandFile"]), "TraceFile": str(profile["traceFiles"][name])}
    write_json(path, document)
    profile["protocolProfile"] = name


def configure_runtime(run, *, backend_url, studio_url, admin_username, admin_password, provider="copilot", enroll=False):
    """Write private owned origins/seed settings before launch; never emit credentials to receipts."""
    from urllib.parse import urlparse
    for address in (backend_url, studio_url):
        parsed = urlparse(address)
        check(parsed.scheme == "http" and parsed.hostname in {"127.0.0.1", "localhost"}
              and parsed.port not in {None, 5089, 5095, 7030, 7243} and parsed.path in {"", "/"}
              and not parsed.username and not parsed.password and not parsed.query and not parsed.fragment,
              "Runtime URL must be an explicit owned loopback origin")
    check(backend_url.rstrip("/") != studio_url.rstrip("/"), "Backend and Studio require separate owned origins")
    check(bool(admin_username.strip()) and bool(admin_password), "Explicit private native administrator seed inputs are required")
    backend = run.profiles["backend"]["published"]
    studio = run.profiles["studio"]["published"]
    settings = json.loads((backend / "appsettings.json").read_text())
    settings["Qualification"].update(StudioOrigin=studio_url.rstrip("/"), EnrollOwnedStore=bool(enroll), DiagnosticGateDirectory="")
    write_json(backend / "appsettings.json", settings)
    document = json.loads((backend / "shells.json").read_text())
    features = document["CShells"]["Shells"]["Authoring"]["Features"]
    features["FoundationIdentityAspNetCoreIdentity"]["AllowedReturnUrlOrigins"] = [studio_url.rstrip("/")]
    features["FoundationIdentityAspNetCoreIdentityEntityFrameworkCore"].update(
        SeedAdminUserName=admin_username, SeedAdminPassword=admin_password, SeedAdminEmail="owner@weaver.invalid")
    features["FoundationIdentityOpenIddict"]["Issuer"] = backend_url.rstrip("/") + "/"
    write_json(backend / "shells.json", document)
    settings = json.loads((studio / "appsettings.json").read_text())
    settings["Studio"].update(BackendBaseUrl=backend_url.rstrip("/"), BackendServerBaseUrl=backend_url.rstrip("/"))
    settings["Qualification"]["EnrollOwnedStore"] = bool(enroll)
    settings["Qualification"]["DiagnosticGateDirectory"] = ""
    write_json(studio / "appsettings.json", settings)
    select_protocol_profile(run, provider)
    # These runtime files include the fixture's private native administrator seed.
    for path in (backend / "appsettings.json", backend / "shells.json", studio / "appsettings.json", studio / "shells.json"):
        path.chmod(0o600)


def consumer_files(profile):
    """Current common published files; native feeds/stores, resources and credentials are excluded."""
    published = profile["published"]
    files = [p for p in published.iterdir() if p.is_file() and p.suffix == ".dll"]
    files += [p for p in (published / "wwwroot").rglob("*") if p.is_file()]
    check(bool(files), "Published common payload is empty")
    return {p.relative_to(published).as_posix(): {"path": str(p), "sha256": sha(p)} for p in sorted(files)}


def freeze_consumer_baseline(run):
    """Call after real startup and before adding resource packages, not during preparation."""
    for name, profile in run.profiles.items():
        run.report["consumers"][name]["baseline"] = consumer_files(profile)
    run.save()


def capture_consumer_after(run):
    """Call only after final restored runtime observations. Missing/different common files refuse."""
    for name, profile in run.profiles.items():
        record = run.report["consumers"][name]
        check(bool(record.get("baseline")), "Freeze the actual runtime baseline before extension")
        after = consumer_files(profile)
        check(set(after) == set(record["baseline"]) and all(after[key]["sha256"] == record["baseline"][key]["sha256"] for key in after),
              "Common consumer payload changed during extension: " + name)
        record.update(after=after, status="passed")
    run.save()


def prepare_consumers(run):
    """Restore, locked-restore, build and publish two external application-owned compositions.

    Requires genuine product feed/SDK and the independently packed protocol fixture in run.feed.
    No application process is started and no resource package is referenced or staged at baseline.
    """
    protocol = list(run.feed.glob("Acme.Weaver.ProtocolFixtures.*.nupkg"))
    check(len(protocol) == 1, "Supply exactly one independently packed generic protocol fixture before preparing consumers")
    # A failed real restore/build remains intact. Retrying never overwrites its sources, cache or logs.
    attempt = 1
    while (run.root / ("backend" + ("" if attempt == 1 else "-attempt-" + str(attempt)) + "-consumer")).exists():
        attempt += 1
    suffix = "" if attempt == 1 else "-attempt-" + str(attempt)
    attempt_record = {"attempt": attempt, "status": "preparing", "profiles": {}}
    run.report.setdefault("consumerPreparationAttempts", []).append(attempt_record)
    run.save()
    for name, relative, products, externals, shared, token in (
        ("backend", "BackendConsumer", BACKEND_PACKAGES, BACKEND_EXTERNAL, BACKEND_SHARED, "__BACKEND_REFERENCES__"),
        ("studio", "StudioConsumer", STUDIO_PACKAGES, STUDIO_EXTERNAL, STUDIO_SHARED, "__STUDIO_REFERENCES__"),
    ):
        owned_name = name + suffix
        source = run.materialize(owned_name + "-consumer", relative, {token: references(run, products, externals)})
        attempt_record["profiles"][name] = {"source": str(source), "status": "preparing"}
        run.save()
        for ancestor in source.parents:
            check(not any((ancestor / file).exists() for file in ("Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props")),
                  "External consumer would inherit ambient build configuration: " + str(ancestor))
        projects = list(source.glob("*.csproj"))
        check(len(projects) == 1, "Expected one external consumer project")
        project = projects[0]
        check(not list(ET.parse(project).getroot().iter("ProjectReference")), "Consumer contains a source/project reference")
        published = run.root / (owned_name + "-published")
        settings = json.loads((source / "appsettings.json").read_text())
        settings["Nuplane"]["HostProvidedPackages"] = list(shared)
        if name == "backend":
            settings["ConnectionStrings"]["Tokens"] = "Data Source=" + str(published / "state" / "tokens.db")
            features = backend_features(published)
        else:
            features = {feature: {} for feature in ("StudioApi", "DashboardStudio", "WorkflowsStudio", "WeaverWorkflowsStudio")}
        write_json(source / "appsettings.json", settings)
        write_json(source / "shells.json", {"CShells": {"Shells": {"Authoring": {"Name": "Authoring", "Features": features,
                     "Configuration": {"WebRouting": {"Path": ""}}}}}})
        env, audit = run.consumer_environment(owned_name)
        audit.touch(exist_ok=True)
        check(not audit.read_text().strip(), "Consumer frontend audit was already nonempty")
        run.run(owned_name + "-restore", [run.dotnet, "restore", str(project), "--configfile", "NuGet.Config", "--no-http-cache"], source, env)
        run.run(owned_name + "-locked-restore", [run.dotnet, "restore", str(project), "--configfile", "NuGet.Config", "--locked-mode", "--no-http-cache"], source, env)
        assets = source / "obj/project.assets.json"
        graph = json.loads(assets.read_text())
        check(bool(graph["libraries"]) and all(library["type"] == "package" for library in graph["libraries"].values()), "Consumer reaches product source")
        cache = Path(env["NUGET_PACKAGES"]).resolve()
        check({Path(folder).resolve() for folder in graph["packageFolders"]} == {cache}, "Consumer used a different NuGet cache")
        identities = {identity.rsplit("/", 1)[0]: identity.rsplit("/", 1)[1] for identity in graph["libraries"]}
        check(not {"Acme.Home.Studio", "Acme.Home.Weaver", "Acme.Weaver.ProtocolFixtures"}.intersection(identities), "Baseline has a concrete resource/protocol reference")
        check(all(identities.get(package) == run.packages[package]["version"] for package in products), "Consumer did not resolve its exact product roots")
        check(set(shared).issubset(identities), "Shared contract is absent from actual published dependency closure")
        run.run(owned_name + "-build", [run.dotnet, "build", str(project), "-c", "Release", "--no-restore", "--nologo"], source, env)
        run.run(owned_name + "-publish", [run.dotnet, "publish", str(project), "-c", "Release", "--no-restore", "--no-build", "-o", str(published)], source, env)
        candidates = {}
        for identity, library in graph["libraries"].items():
            package, version = identity.rsplit("/", 1)
            if package not in run.packages:
                continue
            candidate = run.packages[package]
            check(candidate["version"] == version, "Consumer restored an unexpected candidate version: " + identity)
            cached = cache / library["path"] / (package.lower() + "." + version.lower() + ".nupkg")
            check(sha(cached) == candidate["sha256"], "Consumer cached different candidate bytes: " + identity)
            with zipfile.ZipFile(candidate["path"]) as archive:
                dlls = [entry for entry in archive.namelist() if entry.startswith("lib/net10.0/") and entry.endswith(".dll")]
                check(bool(dlls), "Candidate has no net10.0 assembly: " + identity)
                for entry in dlls:
                    deployed = published / Path(entry).name
                    check(deployed.is_file() and sha(deployed) == hashlib.sha256(archive.read(entry)).hexdigest(), "Published candidate DLL differs: " + identity)
            candidates[package] = {"version": version, "archiveSha256": candidate["sha256"]}
        for file in ("appsettings.json", "shells.json"):
            shutil.copy2(source / file, published / file)
        for folder in ("state", "runtime-feed", "native-store/installs", "protocol"):
            (published / folder).mkdir(parents=True, exist_ok=True)
        profile = {"source": source, "consumerDirectory": source, "published": published, "project": project,
                   "assembly": project.stem + ".dll", "env": env, "audit": audit,
                   "runtimeFeed": published / "runtime-feed", "nativeStore": published / "native-store",
                   "appsettings": published / "appsettings.json", "shells": published / "shells.json"}
        if name == "backend":
            profile.update(commandFile=published / "protocol/command.json",
                           traceFiles={provider: published / ("protocol/" + provider + ".jsonl") for provider in PROTOCOL_FEATURES})
            write_json(profile["commandFile"], {"commandId": "baseline-readiness", "toolName": "baseline-no-tool", "arguments": {}})
        run.profiles[name] = profile
        if name == "backend":
            stage_native_archives(run, name, [{"path": str(protocol[0]), "sha256": sha(protocol[0])}])
        check(not audit.read_text().strip(), "Consumer invoked frontend tools")
        lock = source / "packages.lock.json"
        check(lock.is_file(), "Consumer did not retain its lock file")
        run.report["consumers"][name] = {"source": run.source, "status": "published",
            "assets": {"path": str(assets), "sha256": sha(assets)}, "lock": {"path": str(lock), "sha256": sha(lock)},
            "frontendInvocations": 0, "frontendAudit": {"path": str(audit), "sha256": sha(audit)}, "sourceReferences": [],
            "candidatePackages": candidates, "hostProvidedPackages": {package: identities[package] for package in shared},
            "frameworkSharedAssemblies": ["Microsoft.Extensions.DependencyInjection.Abstractions"],
            "consumerDirectory": str(source), "publishedDirectory": str(published)}
        attempt_record["profiles"][name].update(status="published", published=str(published))
        run.save()
    select_protocol_profile(run, "copilot")
    attempt_record["status"] = "published"
    run.save()
