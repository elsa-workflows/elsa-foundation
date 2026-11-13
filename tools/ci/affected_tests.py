#!/usr/bin/env python3
"""Choose the test projects a pull request's changes can affect (issue #3787).

On a pull request, test_tiers.py keeps only the shard units and EF container suites whose test project this script
calls affected. A push to main, the nightly run, a manual dispatch and a merge-queue entry never get here: they run
everything, and the merge queue is the full gate before a change lands.

A test project is affected when it, or a project it reaches through ProjectReference, owns a changed path. A path is
owned by every project whose directory encloses it, and also by every project that names it from outside its own
directory: an MSBuild Import, a linked Compile, a None/Content item copied to the output, a property holding a path, or
a repository-relative path a test's C# builds with Path.Combine (evidence under specs/, Studio fixtures, sibling apps).

Anything this script cannot place selects everything: a shared build input, CI or map tooling, a path outside every
project that is not documentation, a deleted project, or a diff it cannot read. An isolated prototype path with no server consumer selects no shard. A path only the jobs that always run
read (Markdown, a spec contract, a solution filter) and the separate Studio tree select no shard of their own. When
unsure it widens a PR's tests: a false positive costs runner minutes, a false negative is caught only by the merge
queue.

Usage:
  affected_tests.py --base <sha> --head <sha> [--rev <sha>]   explain the decision for one range
  affected_tests.py --self-test
"""

from __future__ import annotations

import argparse
import json
import os
import posixpath
import re
import subprocess
import sys
from collections import defaultdict
from collections.abc import Callable
from dataclasses import dataclass, field

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import change_scope  # noqa: E402

SOLUTION = "Elsa.Server.slnx"
CORE_FILTER = "Elsa.Server.Core.slnf"

# Every shard reads these before or while it builds, or they decide what a shard is. A change selects everything.
SHARED_PREFIXES = (".github/", "tools/ci/", "tools/maps/", "tools/architecture/", "tools/solution-filters/")
SHARED_FILES = ("tools/bpmn/restore_evidence.py",)
SHARED_NAMES = ("global.json", "nuget.config", ".editorconfig", "packages.lock.json")
SHARED_SUFFIXES = (".sln", ".slnx", ".globalconfig", ".ruleset")
# A spec's published contracts (change_scope EXCLUDE). The one test that reads one, ManagementApiOperationInventoryTests,
# is in Architecture guards.
CONTRACTS = "specs/*/contracts/**"
# The Studio tree is its own solution (Studio/Elsa.Studio.slnx), built and tested by the Studio jobs, and the
# tools/studio probes outside a project are run only by the Studio, Weaver and published-image jobs. No server project
# imports or references them, so such a path no test claims has no server test to select. Their shared build inputs
# (Studio/Directory.*, Studio/NuGet.config) still select everything: the Extensions' Studio projects import them.
SEPARATE_TREES = ("Studio/", "tools/studio/")
PROTOTYPES = "prototypes/"

# A relative path inside an MSBuild file: `../x`, `$(MSBuildThisFileDirectory)../x`, `$(MSBuildProjectDirectory)/../x`.
MSBUILD_PATH = re.compile(r"(?:\$\(MSBuild(?:ThisFile|Project)Directory\)[/\\]?)?(?:\.\.[/\\])+[^\"'<>;|\s]*")
# `$(MSBuildThisFileDirectory)VersionLines.props` in a Directory.* file: a sibling it imports.
MSBUILD_SIBLING = re.compile(r"\$\(MSBuildThisFileDirectory\)([^\"'<>;|\s$]+)")
CS_STRING = re.compile(r'(?<![$@\w])"((?:[^"\\\n]|\\.)*)"')
CS_PATH_CALL = re.compile(r"\bPath\.(?:Combine|Join|GetFullPath)\s*\(")


class Tree:
    """The files of one commit: the working tree (CI) or a git revision (replaying history)."""

    def __init__(self, files: list[str], read_many: Callable[[list[str]], dict[str, str]]):
        self.files = files
        self.file_set = set(files)
        self.read_many = read_many
        self.dirs = set()
        for path in files:
            directory = posixpath.dirname(path)
            while directory and directory not in self.dirs:
                self.dirs.add(directory)
                directory = posixpath.dirname(directory)

    def read(self, path: str) -> str:
        return self.read_many([path]).get(path, "")

    def exists(self, path: str) -> bool:
        return path in self.file_set or path in self.dirs

    @staticmethod
    def worktree(root: str = ".") -> "Tree":
        out = subprocess.run(["git", "-C", root, "ls-files", "-z"], capture_output=True, text=True, check=True).stdout

        def read_many(paths: list[str]) -> dict[str, str]:
            texts = {}
            for path in paths:
                try:
                    with open(os.path.join(root, path), encoding="utf-8", errors="replace") as handle:
                        texts[path] = handle.read()
                except OSError:
                    texts[path] = ""
            return texts
        return Tree([path for path in out.split("\0") if path], read_many)

    @staticmethod
    def revision(rev: str, root: str = ".") -> "Tree":
        out = subprocess.run(["git", "-C", root, "ls-tree", "-r", "-z", "--name-only", rev],
                             capture_output=True, text=True, check=True).stdout

        def read_many(paths: list[str]) -> dict[str, str]:
            if not paths:
                return {}
            batch = subprocess.run(["git", "-C", root, "cat-file", "--batch"], capture_output=True, check=True,
                                   input="".join(f"{rev}:{path}\n" for path in paths).encode()).stdout
            texts, offset = {}, 0
            for path in paths:
                header_end = batch.index(b"\n", offset)
                header = batch[offset:header_end].split()
                offset = header_end + 1
                if len(header) < 3 or header[1] != b"blob":
                    texts[path] = ""
                    continue
                size = int(header[2])
                texts[path] = batch[offset:offset + size].decode("utf-8", errors="replace")
                offset += size + 1
            return texts
        return Tree([path for path in out.split("\0") if path], read_many)


@dataclass
class Graph:
    projects: set[str]
    project_dirs: dict[str, list[str]]
    references: dict[str, set[str]]
    # (path, consuming project, places): a consumer reads that file, or the files under that directory, from outside
    # its own directory. A claim on a file, or on a directory that holds no project, places the paths it covers: they
    # select the consumer instead of everything. A broader claim (`Essentials/src`, `specs`) adds the consumer but never
    # stops a path it covers from selecting everything.
    claims: list[tuple[str, str, bool]]
    shared_imports: set[str]
    solution: set[str]
    always_built: set[str]
    top_level: set[str]
    # Projects whose csproj names a Markdown file (a packed README, AdditionalFiles). Markdown inside any other project
    # is not a build or test input of that project.
    markdown_projects: set[str] = field(default_factory=set)

    def owners(self, path: str) -> list[str]:
        return _owners(self.project_dirs, path)

    def claimants(self, path: str) -> tuple[set[str], bool]:
        """The projects that read this path from outside their directory, and whether one of them places it."""
        consumers, placed = set(), False
        for prefix, consumer, places in self.claims:
            if path == prefix or path.startswith(prefix + "/"):
                consumers.add(consumer)
                placed = placed or places
        return consumers, placed

    def dependents(self, seeds: set[str]) -> set[str]:
        reverse: dict[str, set[str]] = defaultdict(set)
        for project, refs in self.references.items():
            for ref in refs:
                reverse[ref].add(project)
        seen, stack = set(seeds), list(seeds)
        while stack:
            for dependent in reverse.get(stack.pop(), ()):
                if dependent not in seen:
                    seen.add(dependent)
                    stack.append(dependent)
        return seen

    def closure(self, roots: set[str]) -> set[str]:
        seen, stack = set(roots), list(roots)
        while stack:
            for ref in self.references.get(stack.pop(), ()):
                if ref not in seen:
                    seen.add(ref)
                    stack.append(ref)
        return seen


def _resolve(base_dir: str, token: str) -> str | None:
    """A repository-relative path for a relative token, cut before its first wildcard or property. None when it leaves
    the repository or names the repository root."""
    token = re.sub(r"^\$\(MSBuild(?:ThisFile|Project)Directory\)[/\\]?", "", token).replace("\\\\", "/").replace("\\", "/")
    parts = []
    for part in token.split("/"):
        if any(mark in part for mark in "*?$%@[{"):
            break
        parts.append(part)
    path = posixpath.normpath(posixpath.join(base_dir, *parts)) if parts else posixpath.normpath(base_dir)
    if path in (".", "") or path.startswith("../") or path == "..":
        return None
    return path


def _cs_claims(text: str, file_dir: str, top_level: set[str], tree: Tree) -> set[str]:
    """Repository paths a C# file builds: `Path.Combine(root, "specs", "1017-x", ...)`, a literal "Studio/src/...",
    or a `../../src/x` relative to the source file. Only paths that exist in the tree count."""
    found = set()

    def longest(parts: list[str], minimum: int) -> None:
        for end in range(len(parts), minimum - 1, -1):
            candidate = "/".join(parts[:end])
            if candidate and tree.exists(candidate):
                found.add(candidate)
                return

    for match in CS_PATH_CALL.finditer(text):
        depth, index, start = 1, match.end(), match.end()
        while index < len(text) and depth:
            depth += {"(": 1, ")": -1}.get(text[index], 0)
            index += 1
        args, buf, level = [], "", 0
        for char in text[start:index - 1]:
            if char == "," and level == 0:
                args.append(buf.strip())
                buf = ""
                continue
            level += {"(": 1, ")": -1}.get(char, 0)
            buf += char
        args.append(buf.strip())
        run: list[str] = []
        for arg in args:
            literal = re.fullmatch(r'"((?:[^"\\]|\\.)*)"', arg)
            if literal:
                run += [piece for piece in literal.group(1).replace("\\\\", "/").split("/") if piece]
            elif run:
                break
        if run and run[0] in top_level:
            longest(run, 1)
    for match in CS_STRING.finditer(text):
        value = match.group(1).replace("\\\\", "/")
        if value.startswith("../"):
            resolved = _resolve(file_dir, value)
            if resolved and tree.exists(resolved) and resolved.count("/") >= 1:
                found.add(resolved)
        elif "/" in value and value.split("/", 1)[0] in top_level:
            parts = [piece for piece in value.split("/") if piece]
            longest(parts, len(parts))  # a whole literal path: a test's synthetic "src/Tasks/README.md" is no claim
    return found


def build_graph(tree: Tree) -> Graph:
    projects = {path for path in tree.files if path.endswith(".csproj")}
    project_dirs: dict[str, list[str]] = defaultdict(list)
    for project in sorted(projects):
        project_dirs[posixpath.dirname(project)].append(project)
    top_level = {path.split("/", 1)[0] for path in tree.files}
    references: dict[str, set[str]] = {project: set() for project in projects}
    claims: list[tuple[str, str, bool]] = []
    holds_projects = set()
    for directory in project_dirs:
        while directory:
            holds_projects.add(directory)
            directory = posixpath.dirname(directory)

    def claim(consumer: str, path: str) -> None:
        if path in projects:
            references[consumer].add(path)
        elif path in project_dirs:
            references[consumer].update(project_dirs[path])
        elif not (path + "/").startswith(posixpath.dirname(consumer) + "/"):
            claims.append((path, consumer, path not in holds_projects))

    markdown_projects = set()
    for project, text in tree.read_many(sorted(projects)).items():
        if ".md" in text:
            markdown_projects.add(project)
        base = posixpath.dirname(project)
        for token in MSBUILD_PATH.findall(text):
            resolved = _resolve(base, token)
            if resolved:
                claim(project, resolved)
    msbuild_claims = list(claims)

    # Test code reaches outside its project at run time. Scan the C# under every tests/ directory, attributed to the
    # projects that compile it: the enclosing ones, and those that link it in (`<Compile Include="../Shared/*.cs" />`).
    test_projects = {project for project in projects if "/tests/" in f"/{project}"}
    sources = [path for path in tree.files if path.endswith(".cs") and "/tests/" in f"/{path}"]
    for path, text in tree.read_many(sources).items():
        compilers = set(_owners(project_dirs, path))
        compilers.update(consumer for prefix, consumer, _ in msbuild_claims
                         if path == prefix or path.startswith(prefix + "/"))
        consumers = sorted(compilers & test_projects)
        if not consumers:
            continue
        for target in _cs_claims(text, posixpath.dirname(path), top_level, tree):
            for consumer in consumers:
                claim(consumer, target)

    shared_imports = set()
    directory_files = [path for path in tree.files if posixpath.basename(path).startswith("Directory.")]
    for path, text in tree.read_many(directory_files).items():
        base = posixpath.dirname(path)
        # Prototype roots opt out of the repository build and own their MSBuild inputs. Attribute those inputs and
        # their imports to projects in that subtree, so an explicit server reference still reaches their consumers.
        scoped = {project for project in projects if project.startswith(base + "/")} if path.startswith(PROTOTYPES) else set()
        for project in scoped:
            claims.append((path, project, True))
        for token in MSBUILD_PATH.findall(text) + MSBUILD_SIBLING.findall(text):
            resolved = _resolve(base, token)
            if resolved and resolved in tree.file_set:
                if path.startswith(PROTOTYPES):
                    for project in scoped:
                        claims.append((resolved, project, True))
                else:
                    shared_imports.add(resolved)

    # Follow imported prototype MSBuild files too. An external project importing a prototype's props must keep its
    # tests when a nested import changes; a repository-wide Directory import still selects every shard.
    pending = [(prefix, consumer) for prefix, consumer, _ in claims if prefix.startswith(PROTOTYPES)]
    pending += [(path, None) for path in shared_imports if path.startswith(PROTOTYPES)]
    visited = set()
    while pending:
        path, consumer = pending.pop()
        if (path, consumer) in visited or not path.endswith((".props", ".targets")):
            continue
        visited.add((path, consumer))
        text = tree.read(path)
        for token in MSBUILD_PATH.findall(text) + MSBUILD_SIBLING.findall(text):
            resolved = _resolve(posixpath.dirname(path), token)
            if resolved and resolved in tree.file_set:
                if consumer is None:
                    shared_imports.add(resolved)
                else:
                    claim(consumer, resolved)
                pending.append((resolved, consumer))

    solution_text = tree.read(SOLUTION) if SOLUTION in tree.file_set else ""
    solution = set(re.findall(r'Path="([^"]+\.csproj)"', solution_text))
    core: set[str] = set()
    if CORE_FILTER in tree.file_set:
        try:
            core = {p.replace("\\", "/") for p in json.loads(tree.read(CORE_FILTER))["solution"]["projects"]}
        except (ValueError, KeyError, TypeError):
            core = set()
    return Graph(projects, dict(project_dirs), references, claims, shared_imports, solution, core, top_level,
                 markdown_projects)


def _owners(project_dirs: dict[str, list[str]], path: str) -> list[str]:
    owners, directory = [], posixpath.dirname(path)
    while True:
        owners += project_dirs.get(directory, [])
        if not directory:
            return owners
        directory = posixpath.dirname(directory)


@dataclass
class Decision:
    everything: bool
    reason: str
    changed_projects: set[str] = field(default_factory=set)
    affected: set[str] = field(default_factory=set)
    placed_elsewhere: list[str] = field(default_factory=list)
    unbuilt: set[str] = field(default_factory=set)
    known: set[str] = field(default_factory=set)

    def keeps(self, project: str) -> bool:
        """Whether a test project runs. A project the graph does not know is kept: it cannot be proven unaffected."""
        return self.everything or project in self.affected or project not in self.known


def shared_input(path: str, graph: Graph | None = None) -> str | None:
    """Why this path selects everything on its own, or None."""
    name = posixpath.basename(path)
    if path.startswith(PROTOTYPES):
        # A real shared-root import retains the conservative gate even when its target lives in a prototype.
        return "imported by a shared Directory.* file" if graph is not None and path in graph.shared_imports else None
    if path.startswith(SHARED_PREFIXES) or path in SHARED_FILES:
        return "CI, map or build tooling"
    if name.startswith("Directory.") and name.endswith((".props", ".targets", ".rsp")):
        return "a shared MSBuild file"
    if name.lower() in SHARED_NAMES and (name != "packages.lock.json" or graph is None or not graph.owners(path)):
        return "a shared build input"
    if name.endswith(SHARED_SUFFIXES):
        return "a solution or analyzer file"
    if graph is not None and path in graph.shared_imports:
        return "imported by a Directory.* file"
    return None


def read_only_by_always_run_jobs(path: str) -> bool:
    """A path outside every project that only jobs which run on every non-documentation pull request read, so no shard
    has to run for it. A test that reads one by path is still selected through the scan.

    - Markdown. change_scope keeps a module README or the root EXTENSION_POINTS.md out of the documentation fast path
      because the catalog link guards read them; they run in Architecture guards and in Plan tiers
      (check-catalog-links.py), and the extension-point map in Generated maps fresh.
    - A spec contract (specs/*/contracts/**). change_scope keeps them out of the fast path for
      ManagementApiOperationInventoryTests, which is in Architecture guards.
    - A solution filter (.slnf). No shard builds one: every shard builds Elsa.Server.slnx. Core-only builds
      Elsa.Server.Core.slnf, and Generated maps fresh regenerates and compares every filter."""
    return path.endswith((".md", ".slnf")) or change_scope._matches(path, CONTRACTS)


def decide(paths: list[str], graph: Graph, always_run: set[str] = frozenset()) -> Decision:
    """`always_run` are test projects with their own job (Architecture guards); their build closure always builds."""
    if not paths:
        return Decision(True, "the diff is empty or could not be read")
    changed: set[str] = set()
    placed = []
    for path in paths:
        why = shared_input(path, graph)
        if why:
            return Decision(True, f"`{path}` is {why}")
        if path.endswith(".csproj") and path not in graph.projects and not path.startswith(PROTOTYPES):
            return Decision(True, f"`{path}` is a project this change deletes")
        owners = graph.owners(path)
        if path.endswith(".md"):
            owners = [owner for owner in owners if owner in graph.markdown_projects]
        shared = next((owner for owner in owners if owner in graph.shared_imports), None)
        if shared:
            return Decision(True, f"`{path}` belongs to `{shared}`, which a Directory.* file gives every project")
        consumers, placed_by_claim = graph.claimants(path)
        changed.update(owners)
        changed.update(consumers)
        if consumers and not owners:
            placed.append(path)
        if owners or placed_by_claim:
            continue
        if path.startswith((*SEPARATE_TREES, PROTOTYPES)) or read_only_by_always_run_jobs(path):
            continue
        if change_scope.first_full_run_path([path]) is None:
            continue  # documentation no project reads
        return Decision(True, f"`{path}` is outside every project and is not documentation")
    affected = graph.dependents(changed)
    built = graph.closure(set(always_run) | graph.always_built)
    unbuilt = (affected & graph.solution) - built
    return Decision(False, "affected projects only", changed, affected, placed, unbuilt, set(graph.projects))


def changed_paths(base: str, head: str) -> list[str]:
    return change_scope.changed_paths(base, head)


def compute(base: str | None, head: str | None, tree: Tree | None = None,
            always_run: set[str] = frozenset()) -> Decision:
    """The decision for a pull request's range; any failure reads as select-everything."""
    if not base or not head:
        return Decision(True, "no base to diff against")
    try:
        paths = changed_paths(base, head)
        graph = build_graph(tree or Tree.worktree())
    except (subprocess.CalledProcessError, OSError, ValueError) as error:
        return Decision(True, f"the diff or the project graph could not be read ({error})")
    return decide(paths, graph, always_run)


# Self-test -----------------------------------------------------------------------------------------------------------

def _tree(files: dict[str, str]) -> Tree:
    return Tree(sorted(files), lambda paths: {path: files.get(path, "") for path in paths})


def _csproj(*refs: str, extra: str = "") -> str:
    items = "".join(f'<ProjectReference Include="{ref}" />' for ref in refs)
    return f'<Project Sdk="Microsoft.NET.Sdk"><ItemGroup>{items}{extra}</ItemGroup></Project>'


def fixture_tree() -> Tree:
    return _tree({
        "Directory.Build.props": ('<Project><Import Project="$(MSBuildThisFileDirectory)VersionLines.props" />'
                                  '<PropertyGroup><A>$(MSBuildThisFileDirectory)build/Analyzer/Analyzer.csproj</A>'
                                  '</PropertyGroup></Project>'),
        "build/Analyzer/Analyzer.csproj": _csproj(),
        "build/Analyzer/Rule.cs": "",
        "VersionLines.props": "<Project />",
        "Elsa.Server.slnx": "".join(f'<Project Path="{p}" />' for p in (
            "src/Core/Core.csproj", "src/Leaf/Leaf.csproj", "src/Mid/Mid.csproj", "src/Orphan/Orphan.csproj",
            "tests/Core/Core.Tests.csproj", "tests/Leaf/Leaf.Tests.csproj", "tests/Mid/Mid.Tests.csproj",
            "tests/Arch/Arch.Tests.csproj", "apps/Host/Host.csproj")),
        "Elsa.Server.Core.slnf": json.dumps({"solution": {"projects": ["src\\Core\\Core.csproj"]}}),
        "src/Core/Core.csproj": _csproj(),
        "src/Core/Thing.cs": "",
        "src/Leaf/Leaf.csproj": _csproj("../Core/Core.csproj"),
        "src/Leaf/Leaf.cs": "",
        "src/Leaf/EXTENSION_POINTS.md": "",
        "src/Mid/README.md": "",
        "src/Mid/Mid.csproj": _csproj("..\\Core\\Core.csproj", extra='<None Include="README.md" Pack="true" />'),
        "src/Mid/Mid.cs": "",
        "src/Orphan/Orphan.csproj": _csproj(),
        "src/Orphan/Orphan.cs": "",
        "apps/Shared.targets": "<Project />",
        "apps/Host/Host.csproj": _csproj("../../src/Mid/Mid.csproj", extra=(
            '<Compile Include="../../e2e/Probe.cs" />'
            '<Import Project="$(MSBuildThisFileDirectory)../Shared.targets" />')),
        "e2e/Probe.cs": "",
        "e2e/run.ps1": "",
        "tests/Core/Core.Tests.csproj": _csproj("../../src/Core/Core.csproj"),
        "tests/Leaf/Leaf.Tests.csproj": _csproj("../../src/Leaf/Leaf.csproj", extra=(
            '<None Include="../../specs/9-x/evidence/*.json" CopyToOutputDirectory="PreserveNewest" />')),
        "tests/Mid/Mid.Tests.csproj": _csproj("../../src/Mid/Mid.csproj"),
        "tests/Mid/Reads.cs": 'var p = Path.Combine(root, "Studio", "src", "fixture.json");',
        "tests/Host/Host.Tests.csproj": _csproj(extra=(
            '<PropertyGroup><HostProject>$([System.IO.Path]::GetFullPath(\'$(MSBuildProjectDirectory)/'
            '../../apps/Host/Host.csproj\'))</HostProject></PropertyGroup>')),
        "tests/Arch/Arch.Tests.csproj": _csproj("../../src/Core/Core.csproj"),
        "tests/Etl/Etl.Tests.csproj": _csproj(extra='<Compile Include="../Shared/*.cs" />'),
        "tests/Shared/Reader.cs": 'var db = Path.Combine(FindRoot(),\n    "fixtures", "etl", "v1.db");',
        "tests/Shared/Fixtures/runtime.db": "",
        "fixtures/etl/v1.db": "",
        "specs/9-x/evidence/a.json": "",
        "specs/9-x/spec.md": "",
        "docs/guide.md": "",
        "Studio/src/fixture.json": "",
        "Studio/src/Studio.Core/Studio.Core.csproj": _csproj(),
        "Studio/src/Studio.Core/Page.razor": "",
        "Studio/package.json": "",
        "Studio/Directory.Build.props": "<Project />",
        "tools/ci/test-tiers.json": "{}",
        "tools/bpmn/restore_evidence.py": "",
        "README.md": "",
        "website/index.html": "",
        "EXTENSION_POINTS.md": "",
        "src/README.md": "",
        "tools/studio/Probe/browser.mjs": "",
        "prototypes/kernel/Directory.Build.props": '<Project><Import Project="$(MSBuildThisFileDirectory)Version.props" /></Project>',
        "prototypes/kernel/Version.props": "<Project />",
        "prototypes/kernel/sim/Sim.csproj": _csproj(),
        "prototypes/kernel/sim/World.cs": "",
        "prototypes/kernel/run.sh": "",
        "prototypes/kernel/Shared.targets": "<Project />",
        "prototypes/kernel/Nested.props": '<Project><Import Project="$(MSBuildThisFileDirectory)Version.props" /></Project>',
        "prototypes/other/Other.csproj": _csproj(),
        "tests/Prototype/Prototype.Tests.csproj": _csproj("../../prototypes/kernel/sim/Sim.csproj"),
        "tests/Import/Import.Tests.csproj": _csproj(extra='<Import Project="../../prototypes/kernel/Shared.targets" />'),
        "tests/Nested/Nested.Tests.csproj": _csproj(extra='<Import Project="../../prototypes/kernel/Nested.props" />'),
    })


def self_test() -> int:
    failures: list[str] = []

    def check(name: str, condition: bool) -> None:
        if not condition:
            failures.append(name)

    graph = build_graph(fixture_tree())
    arch = {"tests/Arch/Arch.Tests.csproj"}

    def run(*paths: str) -> Decision:
        return decide(list(paths), graph, arch)

    d = run("src/Leaf/Leaf.cs")
    check("leaf: only its dependents", not d.everything and d.affected == {"src/Leaf/Leaf.csproj", "tests/Leaf/Leaf.Tests.csproj"})
    d = run("src/Core/Thing.cs")
    check("core: every dependent, backslash references included",
          not d.everything and {"tests/Core/Core.Tests.csproj", "tests/Leaf/Leaf.Tests.csproj", "tests/Mid/Mid.Tests.csproj",
                                "tests/Host/Host.Tests.csproj", "apps/Host/Host.csproj"} <= d.affected)
    d = run("src/Mid/Mid.cs")
    check("a path property to a project is a reference", "tests/Host/Host.Tests.csproj" in d.affected)
    d = run("specs/9-x/evidence/a.json", "src/Leaf/Leaf.cs")
    check("glob evidence maps to its consumer", not d.everything and "tests/Leaf/Leaf.Tests.csproj" in d.affected)
    d = run("e2e/Probe.cs")
    check("a linked Compile item is placed in its consumer", not d.everything and "apps/Host/Host.csproj" in d.affected
          and "tests/Host/Host.Tests.csproj" in d.affected)
    d = run("apps/Shared.targets")
    check("an imported .targets maps to its importer", not d.everything and "apps/Host/Host.csproj" in d.affected)
    d = run("Studio/src/fixture.json")
    check("a C# Path.Combine reader is a consumer", not d.everything and "tests/Mid/Mid.Tests.csproj" in d.affected)
    d = run("Studio/package.json", "Studio/src/Studio.Core/Page.razor")
    check("the Studio tree selects no server test", not d.everything and not (d.affected & {
        "tests/Core/Core.Tests.csproj", "tests/Leaf/Leaf.Tests.csproj", "tests/Mid/Mid.Tests.csproj"}))
    for path in ("Directory.Build.props", "VersionLines.props", "Studio/Directory.Build.props", "global.json",
                 "NuGet.config", "Elsa.Server.slnx", "tools/ci/test-tiers.json",
                 ".github/workflows/ci.yml", "tools/maps/x.cs", "tools/bpmn/restore_evidence.py", "packages.lock.json",
                 ".editorconfig"):
        check(f"shared input selects everything: {path}", run(path, "src/Leaf/Leaf.cs").everything)
    check("a project every project gets from Directory.Build.props selects everything",
          run("build/Analyzer/Rule.cs").everything)
    check("a lock file inside a project is that project's",
          not run("src/Leaf/packages.lock.json").everything)
    check("outside every project selects everything", run("e2e/run.ps1").everything)
    check("an unlisted root file selects everything", run("website/index.html").everything)
    check("a deleted project selects everything", run("src/Gone/Gone.csproj").everything)
    check("an empty diff selects everything", run().everything)
    d = run("docs/guide.md", "README.md", "specs/9-x/spec.md")
    check("documentation alone selects no project", not d.everything and not d.affected)
    d = run("EXTENSION_POINTS.md", "src/README.md", "src/Leaf/Leaf.cs")
    check("Markdown only the always-run guards read selects no extra project",
          not d.everything and d.affected == {"src/Leaf/Leaf.csproj", "tests/Leaf/Leaf.Tests.csproj"})
    d = run("tests/Shared/Fixtures/runtime.db")
    check("a file under a linked source directory is placed in the project that links it",
          not d.everything and d.affected == {"tests/Etl/Etl.Tests.csproj"})
    d = run("fixtures/etl/v1.db")
    check("a path that linked-in C# reads is placed in the project that links it",
          not d.everything and d.affected == {"tests/Etl/Etl.Tests.csproj"})
    d = run("specs/9-x/contracts/api.yaml", "src/Leaf/Leaf.cs")
    check("a spec contract selects no extra shard", not d.everything and d.affected == {
        "src/Leaf/Leaf.csproj", "tests/Leaf/Leaf.Tests.csproj"})
    d = run("Elsa.Server.Core.slnf", "src/Leaf/Leaf.cs")
    check("a solution filter selects no extra shard", not d.everything and d.affected == {
        "src/Leaf/Leaf.csproj", "tests/Leaf/Leaf.Tests.csproj"})
    d = run("src/Leaf/EXTENSION_POINTS.md")
    check("Markdown a project does not name is not that project's input", not d.everything and not d.affected)
    d = run("src/Mid/README.md")
    check("Markdown a project packs is that project's input", "src/Mid/Mid.csproj" in d.changed_projects)
    d = run("tools/studio/Probe/browser.mjs")
    check("a tools/studio probe selects no server test", not d.everything and not d.affected)
    d = run("src/Orphan/Orphan.cs")
    check("an untested solution project needs the solution build", d.unbuilt == {"src/Orphan/Orphan.csproj"})
    check("an always-built project alone is built", "src/Core/Core.csproj" not in run("src/Core/Thing.cs").unbuilt)

    d = run("prototypes/kernel/run.sh", "prototypes/kernel/Notes.txt")
    check("isolated prototype scripts select no server shard", not d.everything and not d.affected)
    d = run("prototypes/kernel/sim/World.cs")
    check("a real reference to a prototype still selects its consumer", not d.everything and d.affected == {
        "prototypes/kernel/sim/Sim.csproj", "tests/Prototype/Prototype.Tests.csproj"})
    d = run("prototypes/kernel/Shared.targets")
    check("an explicit prototype import still selects its consumer", not d.everything and
          d.affected == {"tests/Import/Import.Tests.csproj"})
    for path in ("prototypes/kernel/Directory.Build.props", "prototypes/kernel/Version.props"):
        d = run(path)
        expected = {"prototypes/kernel/sim/Sim.csproj", "tests/Prototype/Prototype.Tests.csproj"}
        if path.endswith("Version.props"):
            expected.add("tests/Nested/Nested.Tests.csproj")
        check(f"prototype build inputs select their own consumers: {path}", not d.everything and d.affected == expected)
    for path in ("prototypes/kernel/global.json", "prototypes/kernel/NuGet.config", "prototypes/kernel/Kernel.slnx"):
        check(f"prototype-local configuration does not select every shard: {path}", not run(path).everything)
    d = run("prototypes/kernel/run.sh", "src/Leaf/Leaf.cs")
    check("mixed prototype and server changes retain the server selection", not d.everything and d.affected == {
        "src/Leaf/Leaf.csproj", "tests/Leaf/Leaf.Tests.csproj"})
    check("a shared root input with prototypes still selects everything",
          run("Directory.Build.props", "prototypes/kernel/run.sh").everything)
    shared_files = {path: fixture_tree().read(path) for path in fixture_tree().files}
    shared_files["Directory.Build.props"] = '<Project><Import Project="$(MSBuildThisFileDirectory)prototypes/kernel/Version.props" /></Project>'
    check("a prototype file imported by the shared root still selects everything",
          decide(["prototypes/kernel/Version.props"], build_graph(_tree(shared_files)), arch).everything)
    shared_files["Directory.Build.props"] = '<Project><Import Project="$(MSBuildThisFileDirectory)prototypes/kernel/Nested.props" /></Project>'
    check("a nested prototype import from the shared root still selects everything",
          decide(["prototypes/kernel/Version.props"], build_graph(_tree(shared_files)), arch).everything)
    del shared_files["prototypes/kernel/sim/Sim.csproj"]
    shared_files["Directory.Build.props"] = "<Project />"
    d = decide(["prototypes/kernel/sim/Sim.csproj"], build_graph(_tree(shared_files)), arch)
    check("deleting a prototype project retains explicit consumers without selecting everything",
          not d.everything and d.affected == {"tests/Prototype/Prototype.Tests.csproj"})

    for failure in failures:
        print(f"affected_tests self-test: {failure}", file=sys.stderr)
    if failures:
        return 1
    print("affected_tests self-test: all cases pass")
    return 0


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--self-test", action="store_true")
    parser.add_argument("--base")
    parser.add_argument("--head")
    parser.add_argument("--rev", help="read the project graph from this revision instead of the working tree")
    args = parser.parse_args()
    if args.self_test:
        return self_test()
    tree = Tree.revision(args.rev) if args.rev else None
    decision = compute(args.base, args.head, tree)
    print(json.dumps({"everything": decision.everything, "reason": decision.reason,
                      "changed": sorted(decision.changed_projects), "affected": sorted(decision.affected),
                      "unbuilt": sorted(decision.unbuilt)}, indent=2))
    return 0


if __name__ == "__main__":
    sys.exit(main())
