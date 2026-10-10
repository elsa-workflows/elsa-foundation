from pathlib import Path
import datetime
import hashlib
import json
import os
import re
import subprocess
import sys
from xml.sax.saxutils import quoteattr


if len(sys.argv) != 6:
    raise SystemExit(
        "Usage: python3 run-stable-consumer.py <unique-phase> <exact-cshells-version> <exact-nuplane-version> <cshells-nuget-index-url> <nuplane-nuget-index-url>"
    )

root = Path(__file__).resolve().parent
release_root = root.parent
phase, cshells_version, nuplane_version, cshells_source, nuplane_source = sys.argv[1:]
if not re.fullmatch(r"[A-Za-z0-9-]+", phase):
    raise SystemExit("Phase must contain only letters, digits, or hyphens.")
if not cshells_version or not nuplane_version:
    raise SystemExit("Both package versions must be exact and non-empty.")
if cshells_version != "0.0.30" or nuplane_version != "0.0.11":
    raise SystemExit("This prepared stable-family run is pinned to CShells 0.0.30 and Nuplane 0.0.11.")

expected_source = "https://api.nuget.org/v3/index.json"
for label, source in (("CShells", cshells_source), ("Nuplane", nuplane_source)):
    if source != expected_source:
        raise SystemExit(f"{label} source must be {expected_source}.")
if cshells_source != nuplane_source:
    raise SystemExit("Both public package families must restore from the same canonical NuGet.org index.")
cshells_archives = release_root / "cshells-0.0.30" / "nuget"
nuplane_archives = release_root / "nuplane-0.0.11" / "nuget"
fixture_archives = release_root / "fixture-feed-source"
project = root / "consumer" / "Consumer.csproj"
program = root / "consumer" / "Program.cs"
cache = root / "nuget-cache" / phase
evidence = root / "evidence" / phase
logs = evidence / "logs"
config = evidence / "NuGet.Config"
execution_path = evidence / "execution.json"

for path in (cshells_archives, nuplane_archives, fixture_archives):
    if not path.is_dir():
        raise SystemExit(f"Required audited archive directory is missing: {path}")
if cache.exists() or evidence.exists():
    raise SystemExit(f"Phase {phase!r} already has cache or evidence; choose a fresh phase name.")
if any((root / "runs").glob("*")):
    raise SystemExit("Existing scenario run roots are present; resolve them before qualification.")

cshells_package_ids = [
    "CShells",
    "CShells.Abstractions",
    "CShells.AspNetCore",
    "CShells.AspNetCore.Abstractions",
    "CShells.AspNetCore.Testing",
    "CShells.FastEndpoints",
    "CShells.FastEndpoints.Abstractions",
    "CShells.Management.Api",
    "CShells.Nuplane",
    "CShells.Providers.FluentStorage",
]
nuplane_package_ids = [
    "Nuplane",
    "Nuplane.Loading",
    "Nuplane.Loading.Abstractions",
    "Nuplane.Sources.Directory",
]


def find_archive(directory, package_id, version):
    expected_name = f"{package_id}.{version}.nupkg"
    matches = [path for path in directory.iterdir() if path.is_file() and path.name.casefold() == expected_name.casefold()]
    if len(matches) != 1:
        raise SystemExit(f"Expected exactly one audited archive matching {expected_name} in {directory}; found {len(matches)}.")
    return matches[0]


for package_id in cshells_package_ids:
    find_archive(cshells_archives, package_id, cshells_version)
for package_id in nuplane_package_ids:
    find_archive(nuplane_archives, package_id, nuplane_version)

evidence.mkdir(parents=True)
logs.mkdir()
config.write_text(
    f'''<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="nuget.org" value={quoteattr(cshells_source)} />
  </packageSources>
  <packageSourceMapping>
    <packageSource key="nuget.org">
      <package pattern="*" />
    </packageSource>
  </packageSourceMapping>
</configuration>
''',
    encoding="utf-8",
)

environment = dict(os.environ, NUGET_PACKAGES=str(cache))
program_sha256 = hashlib.sha256(program.read_bytes()).hexdigest()
cshells_property = f"-p:CShellsNuplanePackageVersion={cshells_version}"
nuplane_property = f"-p:NuplanePackageVersion={nuplane_version}"


def run(label, command):
    print(label, flush=True)
    with (logs / f"{label}.log").open("w", encoding="utf-8") as log:
        completed = subprocess.run(command, cwd=root, env=environment, stdout=log, stderr=subprocess.STDOUT)
    if completed.returncode:
        raise SystemExit(f"{label} failed with exit code {completed.returncode}; inspect {logs / f'{label}.log'}")


run(
    "restore",
    [
        "dotnet", "restore", str(project), "--configfile", str(config), "--packages", str(cache), "--no-cache", "--disable-parallel", "-m:1",
        cshells_property, nuplane_property,
    ],
)
run(
    "build",
    ["dotnet", "build", str(project), "--configuration", "Release", "--no-restore", "-m:1", cshells_property, nuplane_property],
)

for major in (8, 9, 10):
    run(
        f"net{major}",
        [
            "dotnet", "run", "--project", str(project), "--configuration", "Release", "--no-build", "--no-restore",
            "--framework", f"net{major}.0", cshells_property, nuplane_property, "--", str(major), cshells_version,
            nuplane_version, str(cshells_archives), str(nuplane_archives), str(fixture_archives),
        ],
    )

if hashlib.sha256(program.read_bytes()).hexdigest() != program_sha256:
    raise SystemExit("Consumer Program.cs changed during the run.")
if any((root / "runs").glob("*")):
    raise SystemExit("Owned scenario run roots remain; inspect them before acceptance.")

execution = {
    "phase": phase,
    "cshellsVersion": cshells_version,
    "nuplaneVersion": nuplane_version,
    "cshellsPublicSource": cshells_source,
    "nuplanePublicSource": nuplane_source,
    "cshellsAuditedArchiveDirectory": str(cshells_archives),
    "nuplaneAuditedArchiveDirectory": str(nuplane_archives),
    "fixtureArchiveDirectory": str(fixture_archives),
    "packageCache": str(cache),
    "consumerProgramSha256": program_sha256,
    "targetFrameworksPassed": ["net8.0", "net9.0", "net10.0"],
    "completedUtc": datetime.datetime.now(datetime.timezone.utc).isoformat(),
}
execution_path.write_text(json.dumps(execution, indent=2) + "\n", encoding="utf-8")
subprocess.run([sys.executable, str(root / "verify-stable-consumer-v2.py"), phase], cwd=root, env=environment, check=True)
print(json.dumps({"result": "PASS", **execution}), flush=True)
