from pathlib import Path
import hashlib
import json
import os
import sys
import tempfile
from urllib.error import HTTPError, URLError
from urllib.parse import urlparse
from urllib.request import Request, urlopen


root = Path(__file__).resolve().parent
manifest_path = root / "package-manifest.json"
manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
if manifest.get("schemaVersion") != 1:
    raise SystemExit("Unsupported package manifest schema.")

expected_ids = {
    "CShells": {
        "cshells", "cshells.abstractions", "cshells.aspnetcore",
        "cshells.aspnetcore.abstractions", "cshells.aspnetcore.testing",
        "cshells.fastendpoints", "cshells.fastendpoints.abstractions",
        "cshells.management.api", "cshells.nuplane",
        "cshells.providers.fluentstorage",
    },
    "Nuplane": {
        "nuplane", "nuplane.abstractions", "nuplane.admin",
        "nuplane.admin.api", "nuplane.loading", "nuplane.loading.abstractions",
        "nuplane.loading.api", "nuplane.sources.directory",
    },
}
packages = manifest.get("packages")
if not isinstance(packages, list) or len(packages) != 18:
    raise SystemExit("Manifest must contain exactly 18 accepted package archives.")
seen = {family: set() for family in expected_ids}
for package in packages:
    family = package.get("family")
    if family not in expected_ids:
        raise SystemExit(f"Unexpected package family: {family!r}")
    package_id = package.get("id")
    version = package.get("version")
    filename = package.get("file")
    digest = package.get("sha256")
    url = package.get("url")
    if version != ("0.0.30" if family == "CShells" else "0.0.11"):
        raise SystemExit(f"Unexpected version for {package_id}: {version!r}")
    if not isinstance(filename, str) or Path(filename).name != filename or not filename.lower().endswith(".nupkg"):
        raise SystemExit(f"Unsafe archive filename: {filename!r}")
    if not isinstance(digest, str) or len(digest) != 64 or any(c not in "0123456789abcdef" for c in digest):
        raise SystemExit(f"Invalid SHA-256 for {package_id}.")
    parsed = urlparse(url or "")
    expected_url = f"https://api.nuget.org/v3-flatcontainer/{package_id.lower()}/{version}/{filename.lower()}"
    if parsed.scheme != "https" or parsed.netloc != "api.nuget.org" or url != expected_url:
        raise SystemExit(f"Unexpected NuGet flat-container URL for {package_id}: {url!r}")
    folded_id = package_id.casefold()
    if folded_id in seen[family]:
        raise SystemExit(f"Duplicate package id: {package_id}")
    seen[family].add(folded_id)
if seen != expected_ids:
    raise SystemExit(f"Manifest package ids differ from the accepted families: {seen!r}")

for package in packages:
    family = package["family"]
    destination_dir = root / ("cshells-0.0.30" if family == "CShells" else "nuplane-0.0.11") / "nuget"
    destination_dir.mkdir(parents=True, exist_ok=True)
    destination = destination_dir / package["file"]
    if destination.exists():
        actual = hashlib.sha256(destination.read_bytes()).hexdigest()
        if actual != package["sha256"]:
            raise SystemExit(f"Existing archive hash mismatch; refusing to overwrite: {destination}")
        print(f"Verified existing {family} archive: {package['id']}")
        continue

    request = Request(package["url"], headers={"User-Agent": "portable-stable-consumer/1.0", "Accept": "application/octet-stream"})
    temp_path = None
    try:
        with urlopen(request, timeout=90) as response:
            if response.status != 200:
                raise SystemExit(f"Download returned HTTP {response.status} for {package['id']}.")
            with tempfile.NamedTemporaryFile(dir=destination_dir, prefix=".download-", delete=False) as temp:
                temp_path = Path(temp.name)
                hasher = hashlib.sha256()
                while True:
                    block = response.read(1024 * 1024)
                    if not block:
                        break
                    hasher.update(block)
                    temp.write(block)
        actual = hasher.hexdigest()
        if actual != package["sha256"]:
            raise SystemExit(f"SHA-256 mismatch for {package['id']}: expected {package['sha256']}, got {actual}")
        os.replace(temp_path, destination)
        temp_path = None
        print(f"Downloaded and verified {family} archive: {package['id']}")
    except (HTTPError, URLError, TimeoutError, OSError) as error:
        raise SystemExit(f"Download failed for {package['id']}: {error}") from error
    finally:
        if temp_path is not None:
            try:
                temp_path.unlink()
            except FileNotFoundError:
                pass

print("All 18 public NuGet archives match the accepted SHA-256 manifest.")
