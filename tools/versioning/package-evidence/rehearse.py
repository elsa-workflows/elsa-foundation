"""Exercise one real, dependency-bearing package in PR CI; all publication inputs are fixture values."""
import hashlib
import json
import os
from pathlib import Path
import subprocess
import tempfile
import xml.etree.ElementTree as ET
import zipfile

from collect import collect

root = Path(__file__).resolve().parents[3]
project = Path("server/essentials/src/Caching/Memory/Elsa.Caching.Memory.csproj")
commit = subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=root, text=True).strip()
fingerprint = "sha256:" + hashlib.sha256(b"package evidence rehearsal").hexdigest()
with tempfile.TemporaryDirectory() as temporary:
    plan = Path(temporary)
    props = plan / "versions.props"
    props.write_text(f"""<Project><PropertyGroup>
    <ElsaVersionComputationCommit>{commit}</ElsaVersionComputationCommit>
    <ElsaComputedPackageVersion>4.0.1-preview</ElsaComputedPackageVersion>
    <ElsaInputFingerprint>{fingerprint}</ElsaInputFingerprint>
    </PropertyGroup></Project>""")
    settings = [f"-p:CustomBeforeDirectoryBuildProps={props}", "-p:WarningsNotAsErrors=NU1603"]
    for command in (["restore", "--locked-mode"], ["build", "-c", "Release", "--no-restore"],
                    ["pack", "-c", "Release", "--no-build", f"-p:PackageOutputPath={plan / 'nuget'}"]):
        subprocess.run(["dotnet", *command, str(project), *settings], cwd=root, check=True)
    row = {"package_id": "Elsa.Caching.Memory", "path": str(project), "affected": True, "fingerprint": fingerprint}
    (plan / "computation.json").write_text(json.dumps({"commit": commit, "affected": [row["package_id"]], "packages": [row]}))
    collect(root, plan, "1", "1")
    package, = (plan / "nuget").glob("*.nupkg")
    with zipfile.ZipFile(package) as archive:
        nuspec = ET.fromstring(archive.read("Elsa.Caching.Memory.nuspec"))
        declared = nuspec.find(".//{*}dependency[@id='CShells.Abstractions']").get("version")
    bom = json.loads((plan / "sboms" / (package.name + ".cdx.json")).read_bytes())
    components = {c["name"]: c for c in bom["components"]}
    direct, transitive = components["CShells.Abstractions"], components["JetBrains.Annotations"]
    assert {"name": "elsa:nuspec-version", "value": declared} in direct["properties"]
    assert {"name": "elsa:dependency-scope", "value": "producer-resolved transitive"} in transitive["properties"]
    edges = {edge["ref"]: edge.get("dependsOn", []) for edge in bom["dependencies"]}
    assert transitive["bom-ref"] in edges[direct["bom-ref"]]
    subprocess.run(["node", str(Path(__file__).with_name("attest.mjs")), plan, "validate"], check=True,
                   env={**os.environ, "GITHUB_RUN_ID": "1", "GITHUB_RUN_ATTEMPT": "1", "GITHUB_SHA": commit})
    print(f"Verified {package.name}: packed range {declared}; transitive JetBrains.Annotations {transitive['version']}")
