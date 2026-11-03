#!/usr/bin/env python3
"""Instantiate the M1 source-composition Studio fixture; never launch a host or claim package proof."""
from __future__ import annotations

import argparse
import hashlib
import json
import xml.etree.ElementTree as ET
from pathlib import Path
from xml.sax.saxutils import escape


def prepare(output: Path, version: str) -> dict:
    repository = Path(__file__).resolve().parents[3]
    templates = Path(__file__).resolve().parent / "fixtures"
    if output.exists() and any(output.iterdir()):
        raise ValueError("Use a new or empty owned output directory; existing fixture state is preserved.")
    output.mkdir(parents=True, exist_ok=True)
    source_core = repository / "Studio/src/essentials/Elsa.Studio.Core/Elsa.Studio.Core.csproj"
    if not source_core.is_file():
        raise ValueError(f"The public Studio Core project is unavailable: {source_core}")
    project = (templates / "WeaverJourney.Studio.csproj.in").read_text()
    packages = ET.parse(repository / "Studio/Directory.Packages.props").getroot()
    cshells = next(item.attrib["Version"] for item in packages.iter("PackageVersion") if item.attrib.get("Include") == "CShells.Abstractions")
    references = f'    <ProjectReference Include="{escape(str(source_core))}" />\n    <PackageReference Include="CShells.Abstractions" Version="{escape(cshells)}" />'
    project = project.replace("__REFERENCES__", references)
    project = project.replace("__FIXTURE_VERSION__", escape(version))
    (output / "Weaver.Journey.Studio.csproj").write_text(project)
    (output / "WeaverJourneyStudioFeature.cs").write_bytes((templates / "WeaverJourney.Studio.cs.in").read_bytes())
    module = output / "wwwroot/studio/modules/weaver-journey/module.js"
    module.parent.mkdir(parents=True)
    module.write_bytes((templates / "WeaverJourney.Studio.module.js.in").read_bytes())
    evidence = {
        "evidenceCategory": "deterministic-source-composition",
        "independentPackageWitness": False,
        "assemblyName": "Weaver.Journey",
        "packageId": "Weaver.Journey.Studio",
        "featureType": "Weaver.Journey.Studio.WeaverJourneyStudioFeature",
        "moduleId": "Weaver.Journey",
        "project": str(output / "Weaver.Journey.Studio.csproj"),
        "sourceProjectReferences": [str(source_core)],
        "publicPackageDependencies": {"CShells.Abstractions": cshells},
        "templateSha256": {
            name: hashlib.sha256((templates / name).read_bytes()).hexdigest()
            for name in ["WeaverJourney.Studio.csproj.in", "WeaverJourney.Studio.cs.in", "WeaverJourney.Studio.module.js.in"]
        },
        "moduleSha256": hashlib.sha256(module.read_bytes()).hexdigest(),
        "hostStarted": False,
        "qualificationComplete": False,
    }
    (output / "source-fixture-evidence.json").write_text(json.dumps(evidence, indent=2) + "\n")
    return evidence


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--version", default="0.0.1-weaver-candidate")
    options = parser.parse_args()
    print(json.dumps(prepare(options.output.resolve(), options.version), indent=2))


if __name__ == "__main__":
    main()
