#!/usr/bin/env python3
"""Generate the round-5 mailbox probe against explicitly supplied, coherent products.

No repository paths, package restore inputs, or old probe binaries are assumed.
The sibling mailbox-probe/Program.cs is copied byte-for-byte. Build the generated
project through the machine's normal dotnet/build-slot wrapper, then run its DLL
with a fresh evidence directory as its sole argument.
"""
from __future__ import annotations

import argparse
from datetime import datetime, timezone
import hashlib
import json
from pathlib import Path
import platform
import shlex
import shutil
import sys
import xml.etree.ElementTree as ET


def sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def local_runtime() -> tuple[str, str]:
    architecture = {"arm64": "arm64", "aarch64": "arm64", "amd64": "x64", "x86_64": "x64"}.get(platform.machine().lower())
    system = {"darwin": ("osx", "libe_sqlite3.dylib"), "linux": ("linux", "libe_sqlite3.so"), "win32": ("win", "e_sqlite3.dll")}.get(sys.platform)
    if architecture is None or system is None:
        raise SystemExit(f"Unsupported local runtime: {sys.platform}/{platform.machine()}; use a supported .NET 10 host.")
    return f"{system[0]}-{architecture}", system[1]


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--products-directory", required=True, type=Path, help="Explicit directory of the compiled production DLLs.")
    parser.add_argument("--harness-directory", type=Path, help="Directory containing Elsa.Activities.Testing.dll and its dependencies; defaults to the products directory.")
    parser.add_argument("--output-directory", required=True, type=Path, help="New or empty temporary directory for the generated project.")
    args = parser.parse_args()
    products = args.products_directory.expanduser().resolve(strict=True)
    harness = (args.harness_directory or products).expanduser().resolve(strict=True)
    output = args.output_directory.expanduser().resolve()
    program = Path(__file__).resolve().parent / "mailbox-probe" / "Program.cs"
    if not products.is_dir() or not harness.is_dir() or not program.is_file():
        raise SystemExit("Supply existing products/harness directories and retain the sibling mailbox-probe/Program.cs.")
    if output.exists() and (not output.is_dir() or any(output.iterdir())):
        raise SystemExit(f"The output directory must be new or empty: {output}")
    references: dict[str, Path] = {}
    for directory in dict.fromkeys([products, harness]):
        for file in sorted(directory.glob("*.dll")):
            previous = references.get(file.name)
            if previous is not None and sha256(previous) != sha256(file):
                raise SystemExit(f"Inconsistent products: different copies of {file.name} in the supplied directories; supply one coherent product/test-harness build.")
            references.setdefault(file.name, file)
    required = ["Bpmn.Model.dll", "Bpmn.Semantics.dll", "Elsa.Activities.Testing.dll", "Elsa.Activities.Bpmn.Core.dll", "Elsa.Activities.Bpmn.dll", "Elsa.Workflows.Runtime.dll", "Elsa.Workflows.Runtime.Core.dll", "Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.dll", "Microsoft.Data.Sqlite.dll"]
    missing = sorted(set(required) - references.keys())
    if missing:
        raise SystemExit("Missing compiled dependencies: " + ", ".join(missing))
    rid, native_name = local_runtime()
    candidates = [directory / suffix for directory in dict.fromkeys([products, harness]) for suffix in [Path("runtimes") / rid / "native" / native_name, Path(native_name)]]
    native = next((candidate for candidate in candidates if candidate.is_file()), None)
    if native is None:
        raise SystemExit(f"Cannot locate {native_name}; retain runtimes/{rid}/native under one supplied product directory.")
    output.mkdir(parents=True, exist_ok=True)
    shutil.copyfile(program, output / "Program.cs")
    native_output = output / "native" / native_name
    native_output.parent.mkdir()
    shutil.copyfile(native, native_output)
    project = ET.Element("Project", Sdk="Microsoft.NET.Sdk")
    properties = ET.SubElement(project, "PropertyGroup")
    for name, value in {"TargetFramework": "net10.0", "OutputType": "Exe", "ImplicitUsings": "enable", "Nullable": "enable"}.items():
        ET.SubElement(properties, name).text = value
    items = ET.SubElement(project, "ItemGroup")
    ET.SubElement(items, "FrameworkReference", Include="Microsoft.AspNetCore.App")
    for name, file in sorted(references.items()):
        reference = ET.SubElement(items, "Reference", Include=Path(name).stem)
        ET.SubElement(reference, "HintPath").text = str(file)
        ET.SubElement(reference, "Private").text = "true"
    content = ET.SubElement(items, "None", Include=str(native_output))
    ET.SubElement(content, "Link").text = native_name
    ET.SubElement(content, "CopyToOutputDirectory").text = "PreserveNewest"
    ET.indent(project, space="  ")
    ET.ElementTree(project).write(output / "Probe.csproj", encoding="utf-8", xml_declaration=True)
    provenance = {
        "kind": "pr3410-round5-mailbox-probe-generation",
        "generatedAtUtc": datetime.now(timezone.utc).isoformat(),
        "reviewedHead": "1e707d58fc2148be3715d50616dfdb69dc82ae65",
        "reviewedBase": "7b49513650665606e5f52fdc08c5831f60802dac",
        "recordedOriginalProductSource": "f6d3c370e5c3295bb9ea0499b0ea6adbc533b8a1",
        "sourceNote": "The probe result records the supplied products' assembly informational versions. Generation does not claim that arbitrary supplied products match the original reviewed head.",
        "productsDirectory": str(products), "harnessDirectory": str(harness),
        "programSha256": sha256(program), "runtimeId": rid,
        "sqliteNative": {"file": native_name, "source": str(native), "sha256": sha256(native)},
        "references": [{"file": name, "source": str(file), "sha256": sha256(file)} for name, file in sorted(references.items())],
    }
    (output / "generation.json").write_text(json.dumps(provenance, indent=2) + "\n")
    print(f"Generated {output / 'Probe.csproj'}")
    print("Build through the normal dotnet wrapper:")
    print(f"dotnet build {shlex.quote(str(output / 'Probe.csproj'))} --configuration Release --nologo --verbosity quiet")
    print("Run with a NEW result directory (the SQLite library is copied beside the executable by the project):")
    print(f"dotnet {shlex.quote(str(output / 'bin/Release/net10.0/Probe.dll'))} {shlex.quote(str(output / 'evidence'))}")
    print("Original counterexample: before=0, beforeModelRedrive=8, afterModelRedrive=8, afterExplicitPassivation=0; all eight starts are TerminalSettled/Completed with empty queues, and all eight replays retain their outcomes without activating mailboxes.")


if __name__ == "__main__":
    main()
