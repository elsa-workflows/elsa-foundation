#!/usr/bin/env python3
"""Reproduce the bounded editor/evaluator fixture outside the repository tree."""
import argparse, os, shutil, subprocess, xml.sax.saxutils
from pathlib import Path

parser = argparse.ArgumentParser()
parser.add_argument("--output", required=True, type=Path)
args = parser.parse_args()
fixture = Path(__file__).resolve().parent
repository = fixture.parents[3]
output = args.output.resolve()
if output.exists():
    raise SystemExit("Choose a new output directory; existing evidence is never overwritten.")
if repository == output or repository in output.parents:
    raise SystemExit("Evidence output must be outside the repository.")
output.mkdir(parents=True)
for name in ("browser", "evaluator", "matrix", "roundtrip"):
    shutil.copytree(fixture / name, output / name)
shutil.copyfile(fixture / "cases.json", output / "cases.json")
shutil.copyfile(fixture / "evaluator/packages.lock.json.template", output / "evaluator/packages.lock.json")
project = xml.sax.saxutils.escape(str(repository / "Extensions/Decisions/src/Elsa.Decisions/Elsa.Decisions.csproj"))
(output / "evaluator/evaluator.csproj").write_text(f'''<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable><RestorePackagesWithLockFile>true</RestorePackagesWithLockFile></PropertyGroup><ItemGroup><ProjectReference Include="{project}"/><PackageReference Include="ScratchyDisk.DmnEngine" Version="[2.0.1]"/></ItemGroup></Project>''')
env = dict(os.environ, DECISIONS_REPOSITORY=str(repository))
def run(*command):
    subprocess.run(command, cwd=repository, env=env, check=True)
run("npm", "ci", "--ignore-scripts", "--prefix", str(output / "browser"))
# Set CHROMIUM_EXECUTABLE for an existing browser; otherwise install Playwright's pinned browser once.
run("node", str(output / "browser/roundtrip.cjs"))
run("dotnet", "restore", str(output / "evaluator/evaluator.csproj"), "--locked-mode")
run("dotnet", "run", "--no-restore", "--project", str(output / "evaluator/evaluator.csproj"), "--", str(output))
print(f"Results retained at {output}")
