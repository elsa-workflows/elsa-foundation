#!/usr/bin/env python3
"""Build Foundation and a Server Essentials profile from the current working tree."""

import argparse
import base64
import gzip
import importlib.util
import json
import os
from pathlib import Path
import shlex
import shutil
import subprocess
import sys
import tempfile
import xml.etree.ElementTree as ET


FOUNDATION = Path("server/apps/Elsa.Foundation.Host")
PROFILE_ROOT = Path("docker/profiles")


def run(*command, cwd=None, echo=True, env=None):
    if echo:
        print(f"+ {shlex.join(str(argument) for argument in command)}", flush=True)
    subprocess.run(command, cwd=cwd, check=True, env=env)


def git(cwd, *args, env=None):
    return subprocess.check_output(
        ["git", "-c", "core.fsmonitor=false", "-C", str(cwd), *args], env=env
    )


def checkout_environment():
    repository_variables = git(Path.cwd(), "rev-parse", "--local-env-vars").decode().splitlines()
    return {name: value for name, value in os.environ.items() if name not in repository_variables}


def snapshot(repo, source, env=None):
    env = checkout_environment() if env is None else env
    revision = git(repo, "rev-parse", "HEAD", env=env).decode().strip()
    patch = git(repo, "diff", "--binary", "--no-ext-diff", "--no-textconv", "--no-color",
                "--src-prefix=a/", "--dst-prefix=b/", "HEAD", "--", env=env)
    untracked = git(repo, "ls-files", "--others", "--exclude-standard", "-z", env=env).split(b"\0")
    run("git", "-c", "core.fsmonitor=false", "clone", "--quiet", "--shared",
        "--no-checkout", "--", repo, source, env=env)
    git(source, "config", "core.fsmonitor", "false", env=env)
    git(source, "checkout", "--quiet", "--detach", revision, env=env)
    if patch:
        subprocess.run(["git", "-c", "core.fsmonitor=false", "apply", "--index", "--binary", "-"],
                       cwd=source, input=patch, check=True, env=env)
    for raw_path in filter(None, untracked):
        relative = Path(os.fsdecode(raw_path))
        original = repo / relative
        target = source / relative
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(original, target, follow_symlinks=False)
    if patch or any(untracked):
        git(source, "add", "--all", env=env)
        if git(source, "diff", "--cached", "--name-only", "-z", env=env):
            git(source, "-c", "user.name=Elsa Local Profile", "-c", "user.email=local-profile@example.invalid",
                "-c", "core.hooksPath=/dev/null", "commit", "--no-gpg-sign", "--quiet", "-m",
                "Snapshot for local Docker build", env=env)
    return git(source, "rev-parse", "HEAD", env=env).decode().strip()


def prepare_profile(source, lab, profile, versions):
    map_path = source / "docs/maps/dependency-map.json"
    nodes = json.loads(map_path.read_text())["nodes"]
    by_path = {node["path"]: node for node in nodes}
    by_package = {node["package_id"].casefold(): node for node in nodes if node["packable"]}

    spec = importlib.util.spec_from_file_location("profile_assembler", source / PROFILE_ROOT / "prepare.py")
    assembler = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(assembler)
    profile_source = source / PROFILE_ROOT / profile
    roots = assembler.read_roots(profile_source / "packages.txt", assembler.read_versions(versions),
                                 source / PROFILE_ROOT / "packs")
    pending = [by_package[key]["path"] for key in roots if key in by_package]
    pending.append((FOUNDATION / "Elsa.Foundation.Host.csproj").as_posix())
    selected = set()
    while pending:
        path = pending.pop()
        if path in selected:
            continue
        selected.add(path)
        pending.extend(edge["path"] for edge in by_path[path]["edges"] if edge["type"] == "internal")
    package_paths = sorted(path for path in selected if by_path[path]["packable"])
    solution = os.path.relpath(source / "Elsa.Server.slnx", lab)
    solution_filter = lab / "local-packages.slnf"
    solution_filter.write_text(json.dumps({"solution": {"path": solution, "projects": package_paths}}, indent=2) + "\n")

    feed = lab / "local-feed"
    feed.mkdir()
    config = ET.parse(source / "NuGet.config")
    sources = config.find("packageSources")
    mappings = config.find("packageSourceMapping")
    ET.SubElement(sources, "add", key="Local checkout", value="/local-feed")
    local_mapping = ET.SubElement(mappings, "packageSource", key="Local checkout")
    for source_mapping in mappings:
        if source_mapping is local_mapping:
            continue
        for package in list(source_mapping):
            if package.get("pattern", "").casefold() in by_package:
                source_mapping.remove(package)
    for node in sorted(by_package.values(), key=lambda node: node["package_id"].casefold()):
        ET.SubElement(local_mapping, "package", pattern=node["package_id"])
    for package in ("Elsa.*", "Elsa3.*"):
        ET.SubElement(mappings.find("packageSource[@key='Elsa 4 Preview']"), "package", pattern=package)
    ET.SubElement(mappings.find("packageSource[@key='nuget.org']"), "package", pattern="*")
    config.write(feed / "NuGet.config", encoding="utf-8", xml_declaration=True)

    context = lab / "server-profile"
    shutil.copytree(profile_source, context)
    dockerfile = context / "Dockerfile"
    recipe = dockerfile.read_text()
    command = "RUN elsa-profile assemble "
    if recipe.count(command) != 2:
        raise RuntimeError("Expected one assembler command per locks/prepare stage in the profile Dockerfile.")
    dockerfile.write_text(recipe.replace(command,
        "RUN --mount=type=bind,from=localfeed,source=.,target=/local-feed "
        "elsa-profile assemble --nuget-config /local-feed/NuGet.config "))
    return solution_filter, feed, context, package_paths


def write_environment(lab, source, context, name, platform, encoded_versions):
    variables = {
        "ELSA_PROFILE_LAB": lab,
        "ELSA_SOURCE_DIR": source,
        "ELSA_PROFILE_PREFIX": name,
        "ELSA_PROFILE_DIR": context,
        "ELSA_PLATFORM": platform,
        "ELSA_PACKAGE_VERSIONS": encoded_versions,
        "ELSA_BASE_IMAGE": f"{name}:server-foundation",
        "ELSA_BUILDER_IMAGE": f"{name}:server-builder",
        "ELSA_PROFILE_IMAGE": f"{name}:server-essentials",
        "ELSA_STUDIO_FOUNDATION": f"{name}:studio-foundation",
        "ELSA_STUDIO_ESSENTIALS": f"{name}:studio-essentials",
        "ELSA_STUDIO_WORKBENCH": f"{name}:studio-workbench",
    }
    env_path = lab / "demo.env"
    env_path.write_text("".join(f"{key}={shlex.quote(str(value))}\n" for key, value in variables.items()))
    return env_path


def build(repo, profile, platform, output_root, env):
    lab = Path(tempfile.mkdtemp(prefix="elsa-docker-profiles.", dir=output_root))
    source = lab / "source"
    name = lab.name.lower()
    print(f"Lab directory: {lab}", flush=True)
    revision = snapshot(repo, source, env)

    versions_dir = lab / "versions"
    versions_dir.mkdir()
    empty_record = versions_dir / "empty-record.json"
    empty_record.write_text('{"schema_version":1,"last_publish_commit":null,"packages":[]}\n')
    versions = versions_dir / "package-versions.props"
    run("dotnet", "run", "--project", "tools/versioning/Elsa.Versioning.Calculator", "--",
        "--record", empty_record, "--branch", f"local-{name.rsplit('.', 1)[-1]}-{revision[:12]}",
        "--output", versions_dir / "computation.json", "--pack-properties", versions, cwd=source, env=env)
    run("dotnet", "run", "--project", "tools/maps/Elsa.Maps.Generator", "--", "dependency-map", cwd=source, env=env)
    solution, feed, context, package_paths = prepare_profile(source, lab, profile, versions)

    print(f"Packing {len(package_paths)} Elsa projects from the source snapshot.", flush=True)
    settings = (f"-p:CustomBeforeDirectoryBuildProps={versions}", "-p:WarningsNotAsErrors=NU1603")
    run("dotnet", "restore", solution, "--locked-mode", *settings, cwd=source, env=env)
    run("dotnet", "build", solution, "-m:4", "-c", "Release", "--no-restore", *settings, cwd=source, env=env)
    run("dotnet", "pack", solution, "-m:4", "-c", "Release", "--no-build", *settings,
        f"-p:PackageOutputPath={feed}", cwd=source, env=env)
    foundation, builder, image = (f"{name}:{tag}" for tag in
                                  ("server-foundation", "server-builder", "server-essentials"))
    docker = ("docker", "build", "--platform", platform)
    encoded_versions = base64.b64encode(gzip.compress(versions.read_bytes(), mtime=0)).decode("ascii")
    print("Building Foundation from the local source snapshot with the generated package versions.", flush=True)
    run(*docker, "--build-arg", f"ELSA_PACKAGE_VERSIONS={encoded_versions}", "-f",
        FOUNDATION / "Dockerfile", "-t", foundation, ".", cwd=source, echo=False, env=env)
    run(*docker, "--build-arg", f"FOUNDATION_IMAGE={foundation}", "--build-context",
        f"versions={versions_dir}", "-f", PROFILE_ROOT / "builder.Dockerfile", "-t", builder, ".", cwd=source, env=env)

    context_args = ("--build-arg", f"FOUNDATION_IMAGE={foundation}", "--build-arg",
                    f"PROFILE_BUILDER_IMAGE={builder}", "--build-context", f"localfeed={feed}")
    locks = lab / "locks"
    run(*docker, *context_args, "--target", "locks", "--output", f"type=local,dest={locks}", context, cwd=source, env=env)
    for lock in ("packages.lock.json", "nuplane.lock.json"):
        shutil.copy2(locks / lock, context / lock)
    run(*docker, *context_args, "-t", image, context, cwd=source, env=env)

    env = write_environment(lab, source, context, name, platform, encoded_versions)
    print(f"\nBuilt {image} from local snapshot {revision[:12]}.")
    print(f"Load the demo settings with: source {shlex.quote(str(env))}")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--profile", choices=("essentials", "essentials-curated"), default="essentials")
    parser.add_argument("--platform", choices=("linux/arm64", "linux/amd64"), default="linux/arm64")
    parser.add_argument("--output-root", type=Path, help="existing directory in which to keep the build inputs")
    args = parser.parse_args()
    try:
        env = checkout_environment()
        repo = Path(os.fsdecode(git(Path.cwd(), "rev-parse", "--show-toplevel", env=env).rstrip(b"\n")))
        if not (repo / FOUNDATION / "Dockerfile").is_file():
            raise ValueError("Run this command from your Elsa Foundation checkout.")
        output_root = args.output_root.resolve() if args.output_root else None
        if output_root is not None and (output_root == repo or repo in output_root.parents):
            raise ValueError("Choose an output directory outside the source checkout.")
        if output_root is None and sys.platform == "darwin":
            output_root = Path("/private/tmp")
        build(repo, args.profile, args.platform, output_root, env)
    except (RuntimeError, ValueError, OSError, subprocess.CalledProcessError) as error:
        print(f"Local profile build stopped: {getattr(error, 'returncode', '') or error}", file=sys.stderr)
        raise SystemExit(1)


if __name__ == "__main__":
    main()
