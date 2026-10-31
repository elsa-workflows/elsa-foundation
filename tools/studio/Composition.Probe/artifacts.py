"""Owned actual archives and external consumers for the composition qualifier."""

import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import xml.etree.ElementTree as ET
import zipfile

REPO = Path(__file__).resolve().parents[3]
FIXTURES = Path(__file__).with_name("fixtures")
CUSTOM_ID = "Acme.Studio.Composition"
PROFILES = {
    "minimal": (["Elsa.Studio.Hosting", CUSTOM_ID], ["STATIC_CUSTOM"]),
    "themes": (["Elsa.Studio.Hosting.Themes", CUSTOM_ID], ["STATIC_CUSTOM", "THEMES"]),
    "administration": (["Elsa.Studio.Hosting.Nuplane"], ["ADMINISTRATION"]),
    "reference": (["Elsa.Studio.Hosting.Nuplane", "Elsa.Studio.Hosting.Themes", "Elsa.Studio.ConsoleStream"],
                  ["ADMINISTRATION", "THEMES", "CONSOLE_STREAM"]),
}


def check(condition, message):
    if not condition:
        raise RuntimeError(message)


def digest(data):
    return hashlib.sha256(data).hexdigest()


def pinned_version(name):
    return next(node.attrib["Version"] for node in ET.parse(REPO / "Studio/Directory.Packages.props").getroot().iter("PackageVersion")
                if node.attrib.get("Include") == name)


def identity(path):
    with zipfile.ZipFile(path) as archive:
        specs = [name for name in archive.namelist() if name.endswith(".nuspec")]
        check(len(specs) == 1, "Candidate must contain one nuspec: " + str(path))
        root = ET.fromstring(archive.read(specs[0]))
        values = {node.tag.rsplit("}", 1)[-1]: node.text for node in root.iter()}
        return values["id"], values["version"]


class Artifacts:
    def __init__(self, output=None, packages=None, version="4.0.0-composition3364"):
        self.root = Path(output or tempfile.mkdtemp(prefix="studio-composition-")).resolve()
        if output:
            self.root.mkdir(parents=True, exist_ok=False)
        self.feed = self.root / "build-feed"
        self.feed.mkdir()
        self.source = subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=REPO, text=True).strip()
        self.version = version
        self.packages_input = packages
        self.packages = {}
        self.profiles = {}
        self.report = {"root": str(self.root), "toolSourceRevision": self.source, "version": version, "status": "preparing",
                       "toolSha256": digest(Path(__file__).read_bytes()), "profiles": {}}
        self.dotnet = shutil.which("dotnet")
        check(self.dotnet, "A .NET10 SDK is required")
        print("Artifacts: " + str(self.root), flush=True)

    def run(self, name, command, cwd, env=None):
        self.report["stage"] = name
        self.save()
        with (self.root / (name + ".log")).open("w") as log:
            result = subprocess.run(command, cwd=cwd, env=env, stdout=log, stderr=subprocess.STDOUT)
        check(result.returncode == 0, f"{name} exited {result.returncode}; see {self.root / (name + '.log')}")

    def save(self):
        (self.root / "report.json").write_text(json.dumps(self.report, indent=2) + "\n")

    def config(self, destination):
        root = ET.Element("configuration")
        sources = ET.SubElement(root, "packageSources")
        ET.SubElement(sources, "clear")
        mapping = ET.SubElement(root, "packageSourceMapping")
        entries = [
            ("candidate", str(self.feed), list(self.packages) + [CUSTOM_ID]),
            ("nuget.org", "https://api.nuget.org/v3/index.json", ["*"]),
            ("CShells", "https://f.feedz.io/valence-works/cshells/nuget/index.json", ["CShells", "CShells.*"]),
            ("Nuplane", "https://f.feedz.io/valence-works/nuplane/nuget/index.json", ["Nuplane", "Nuplane.*"]),
            ("Specifications", "https://f.feedz.io/elsa-workflows/elsa-4/nuget/index.json", ["Elsa.Specifications.*"]),
        ]
        for key, url, patterns in entries:
            ET.SubElement(sources, "add", key=key, value=url)
            group = ET.SubElement(mapping, "packageSource", key=key)
            for pattern in sorted(set(patterns)):
                ET.SubElement(group, "package", pattern=pattern)
        ET.ElementTree(root).write(destination / "NuGet.Config", encoding="unicode")
        shutil.copy2(REPO / "global.json", destination / "global.json")

    def produce(self):
        if self.packages_input:
            for path in Path(self.packages_input).resolve().glob("*.nupkg"):
                shutil.copy2(path, self.feed)
            self.report["producer"] = "supplied actual archives; producer revision is external provenance"
        else:
            producer = self.root / "producer"
            producer.mkdir()
            self.run("source-archive", ["git", "archive", "--format=tar", "--output=" + str(self.root / "source.tar"), self.source], REPO)
            self.run("source-extract", ["tar", "-xf", str(self.root / "source.tar"), "-C", str(producer)], REPO)
            inventory = json.loads((producer / "tools/ci/studio-package-identities.json").read_text())
            generated = {}
            for item in inventory["packable_projects"]:
                relative = Path(item["project"]).parent / "wwwroot"
                original = REPO / relative
                if original.exists():
                    shutil.copytree(original, producer / relative, dirs_exist_ok=True)
                    generated.update({str(path.relative_to(REPO)): digest(path.read_bytes()) for path in original.rglob("*") if path.is_file()})
            properties = ["-p:Version=" + self.version, "-p:SourceRevisionId=" + self.source]
            self.run("producer-build", [self.dotnet, "build", "Studio/Elsa.Studio.slnx", "-c", "Release", *properties], producer)
            self.run("producer-pack", [self.dotnet, "pack", "Studio/Elsa.Studio.slnx", "-c", "Release", "--no-build", *properties, "-o", str(self.feed)], producer)
            self.run("producer-inventory", ["python3", "tools/ci/verify_studio_nuget_packages.py", "--packages", str(self.feed), "--version", self.version], producer)
            for name in ["Core", "Nuplane"]:
                project = f"Essentials/src/Modularity/{name}/Elsa.Modularity.{name}.csproj"
                self.run("producer-modularity-" + name.lower(), [self.dotnet, "pack", project, "-c", "Release", *properties, "-o", str(self.feed)], producer)
            self.run("producer-preferences", [self.dotnet,"pack","Essentials/src/Studio/Preferences/Core/Elsa.Studio.Preferences.Core.csproj","-c","Release",*properties,"-o",str(self.feed)],producer)
            self.run("reference-web-publish", [self.dotnet, "publish", "Studio/src/apps/Elsa.Studio.Web/Elsa.Studio.Web.csproj", "-c", "Release", "--no-build", *properties, "-o", str(self.root / "reference-web-published")], producer)
            self.report.update(producer="owned committed source snapshot and actual production projects", producerSource=self.source,
                               generatedInputHashes=generated, referenceWebPublished=True)
        for path in self.feed.glob("*.nupkg"):
            name, version = identity(path)
            check(name not in self.packages, "Duplicate candidate: " + name)
            with zipfile.ZipFile(path) as archive:
                spec = ET.fromstring(archive.read(next(entry for entry in archive.namelist() if entry.endswith(".nuspec"))))
                dependencies = [node.attrib for node in spec.iter() if node.tag.rsplit("}", 1)[-1] == "dependency"]
            self.packages[name] = {"path": str(path), "version": version, "archiveSha256": digest(path.read_bytes()), "dependencies": dependencies}
        required = {"Elsa.Studio.Core", "Elsa.Studio.Api", "Elsa.Studio.Hosting", "Elsa.Studio.Hosting.Themes",
                    "Elsa.Studio.Hosting.Nuplane", "Elsa.Studio.ConsoleStream", "Elsa.Studio.Preferences.Core", "Elsa.Modularity.Core", "Elsa.Modularity.Nuplane"}
        check(required.issubset(self.packages), "Missing actual composition candidate packages")
        versions = {self.packages[name]["version"] for name in required}
        check(len(versions) == 1, "Composition product candidates must share one injected version")
        self.version = versions.pop()
        self.report.update(version=self.version, packages=self.packages)
        self.save()

    def module(self, version, marker):
        source = self.root / ("module-" + version)
        source.mkdir()
        self.config(source)
        replacements = {"__PRODUCT_VERSION__": self.version, "__CSHELLS_VERSION__": pinned_version("CShells.Abstractions"), "__MARKER__": marker}
        for template, target in [("CustomModule.cs.in", "CustomModule.cs"), ("CustomModule.csproj.in", CUSTOM_ID + ".csproj"),
                                 ("module.js.in", "wwwroot/studio/modules/composition/module.js"), ("module.css", "wwwroot/studio/modules/composition/module.css")]:
            data = (FIXTURES / template).read_text()
            for key, value in replacements.items():
                data = data.replace(key, value)
            destination = source / target
            destination.parent.mkdir(parents=True, exist_ok=True)
            destination.write_text(data)
        self.run("module-pack-" + version, [self.dotnet, "pack", "-c", "Release", "-p:Version=" + version, "-o", str(self.feed)], source)
        package = self.feed / f"{CUSTOM_ID}.{version}.nupkg"
        check(identity(package) == (CUSTOM_ID, version), "Wrong custom fixture identity")
        with zipfile.ZipFile(package) as archive:
            for name in ["lib/net10.0/" + CUSTOM_ID + ".dll", "staticwebassets/studio/modules/composition/module.js", "staticwebassets/studio/modules/composition/module.css"]:
                check(bool(archive.read(name)), "Missing custom module payload: " + name)
        self.report.setdefault("customPackages", {})[version] = {"path": str(package), "archiveSha256": digest(package.read_bytes()), "marker": marker}
        self.save()

    def consumer(self, name):
        references, constants = PROFILES[name]
        source = self.root / (name + "-consumer")
        source.mkdir()
        self.config(source)
        project = (FIXTURES / "Consumer.csproj.in").read_text().replace("__CONSTANTS__", ";".join(constants))
        entries = []
        for package in references:
            version = "1.0.0" if package == CUSTOM_ID else self.version
            entries.append(f'    <PackageReference Include="{package}" Version="{version}" />')
        (source / "Consumer.csproj").write_text(project.replace("__REFERENCES__", "\n".join(entries)))
        shutil.copy2(FIXTURES / "Program.cs.in", source / "Program.cs")
        features = {"StudioApi": {}, "AcmeComposition": {}}
        if "THEMES" in constants:
            features.update(ThemeManagement={}, ThemePicker={})
        if "CONSOLE_STREAM" in constants:
            features["ConsoleStream"] = {}
        (source / "shells.json").write_text(json.dumps({"CShells": {"Shells": {"Authoring": {"Features": features}}}}))
        settings = {"Logging": {"LogLevel": {"Default": "Warning", "Microsoft.Hosting.Lifetime": "Information"}},
                    "Studio": {"HostId": "composition-" + name, "Auth": {"Enabled": False, "SessionCacheSeconds": 0}},
                    "Nuplane": {"Setup": {"AutomaticReconciliation": False}, "Loading": {"Enabled": True}},
                    "Qualification": {"EnrollOwnedStore": True}}
        (source / "appsettings.json").write_text(json.dumps(settings))
        cache = self.root / (name + "-cache")
        denied = self.root / (name + "-denied")
        denied.mkdir()
        audit = self.root / (name + "-frontend-invocations.log")
        for command in ["node", "npm", "pnpm", "npx"]:
            shim = denied / command
            shim.write_text('#!/bin/sh\necho "$0" >> "$STUDIO_FRONTEND_DENIAL_AUDIT"\nexit 71\n')
            shim.chmod(0o755)
        env = {key: value for key, value in os.environ.items() if not key.lower().startswith(("studio__", "cshells__", "nuplane__", "aspnetcore_", "qualification__", "themes__"))}
        env.update(PATH=str(denied) + os.pathsep + env["PATH"], NUGET_PACKAGES=str(cache), NUGET_HTTP_CACHE_PATH=str(self.root / (name + "-http-cache")),
                   STUDIO_FRONTEND_DENIAL_AUDIT=str(audit), ASPNETCORE_ENVIRONMENT="Production", DOTNET_ENVIRONMENT="Production")
        self.run(name + "-restore", [self.dotnet, "restore", "--configfile", "NuGet.Config"], source, env)
        self.run(name + "-locked", [self.dotnet, "restore", "--configfile", "NuGet.Config", "--locked-mode"], source, env)
        graph = json.loads((source / "obj/project.assets.json").read_text())
        check(all(library["type"] == "package" for library in graph["libraries"].values()), name + " used a project/source dependency")
        check({Path(folder).resolve() for folder in graph["packageFolders"]} == {cache.resolve()}, name + " used an external NuGet cache")
        check(not any(key.startswith(("Elsa.Studio.Web/", "Elsa.Modularity.Api/", "Elsa.Identity", "Elsa.Clustering")) for key in graph["libraries"]), name + " has unintended Web/backend dependencies")
        if "ADMINISTRATION" not in constants:
            check(not any(key.startswith(("Nuplane", "Elsa.Modularity", "Elsa.Studio.Hosting.Nuplane/")) for key in graph["libraries"]), name + " unexpectedly requires administration")
        if "THEMES" not in constants:
            check(not any(key.startswith("Elsa.Studio.Hosting.Themes/") for key in graph["libraries"]), name + " unexpectedly requires Themes")
        if "CONSOLE_STREAM" in constants:
            for package in ["ConsoleLogStreaming.AspNetCore/", "ConsoleLogStreaming.Core/"]:
                check(any(key.startswith(package) for key in graph["libraries"]), "Console Stream omitted a host runtime dependency: " + package)
        published = self.root / (name + "-published")
        self.run(name + "-publish", [self.dotnet, "publish", "-c", "Release", "--no-restore", "-o", str(published)], source, env)
        if "CONSOLE_STREAM" in constants:
            check((published / "ConsoleLogStreaming.Core.dll").exists(), "Console Stream runtime dependency was not published")
        for filename in ["appsettings.json", "shells.json"]:
            shutil.copy2(source / filename, published / filename)
        (published / "sources").mkdir()
        (published / "sources/themes.json").write_text('{"themes":[]}')
        (published / "runtime-feed").mkdir()
        if "ADMINISTRATION" in constants:
            shutil.copy2(self.feed / f"{CUSTOM_ID}.1.0.0.nupkg", published / "runtime-feed")
        candidates = {}
        for key in graph["libraries"]:
            package, version = key.rsplit("/", 1)
            archive = self.feed / f"{package}.{version}.nupkg"
            if not archive.exists():
                continue
            cached = cache / package.lower() / version.lower() / f"{package.lower()}.{version.lower()}.nupkg"
            check(digest(cached.read_bytes()) == digest(archive.read_bytes()), name + " restored different candidate bytes: " + key)
            with zipfile.ZipFile(archive) as content:
                dll = "lib/net10.0/" + package + ".dll"
                check(digest((published / (package + ".dll")).read_bytes()) == digest(content.read(dll)), name + " published different candidate DLL: " + key)
            candidates[key] = digest(archive.read_bytes())
        check(not audit.exists() or not audit.read_text().strip(), name + " invoked consumer frontend tools")
        self.profiles[name] = {"published": published, "source": source, "cache": cache, "env": env, "audit": audit, "constants": constants}
        self.report["profiles"][name] = {"packageOnlyLibraries": len(graph["libraries"]), "candidateArchiveHashes": candidates,
                                         "directReferences": references, "constants": constants, "frontendInvocations": 0, "status": "published"}
        self.save()

    def hide_build_inputs(self):
        paths = [self.feed]
        if (self.root / "producer").exists():
            paths.append(self.root / "producer")
        paths.extend(self.root.glob("module-*"))
        for profile in self.profiles.values():
            paths.extend([profile["source"], profile["cache"]])
        for path in paths:
            path.rename(path.with_name("hidden-" + path.name))
        self.report["hiddenBuildInputs"] = ["hidden-" + path.name for path in paths]
        self.report["status"] = "prepared"
        self.save()
