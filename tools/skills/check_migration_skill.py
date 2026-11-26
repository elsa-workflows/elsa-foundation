#!/usr/bin/env python3
"""Keep the Elsa 3 migration skill's porting catalog equal to the compiled sample (spec 1238 FR-017).

Fails when:
1. a catalog block marked ``<!-- sample: case-NN -->`` differs from the sample region ``case-NN``;
2. a region has no catalog block, or a block has no region;
3. a case number is missing, duplicated or out of order, a block sits under another case's heading, or a case whose
   verdict has a *port* part has no marked block;
4. any file in the skill folder holds an Elsa 3 conversion or value-flow code literal (a token starting with the
   ``ELS3-`` or ``VF-`` prefix). Other code families and provider-neutral wording are left to review.

Sample regions are ``// catalog:case-NN begin`` ... ``// catalog:case-NN end`` in the sample's ``*.cs`` files. Regions
may nest or overlap; marker lines of other regions inside a region are dropped, and the text is compared after removing
the common leading indentation and trailing whitespace.

Run ``--self-test`` for the built-in good and bad inputs.
"""

from __future__ import annotations

import argparse
import re
import sys
import tempfile
import textwrap
import unittest
from pathlib import Path

CASE_COUNT = 19
SKILL_FOLDER = Path("plugins/elsa3-migration/skills/elsa3-workflows-migration")
CATALOG = SKILL_FOLDER / "porting-catalog.md"
SAMPLE_ROOT = Path("extensions/Elsa3/tests/Migration/Sample")

HEADING = re.compile(r"^## Case (\d{2}): \S")
VERDICT = re.compile(r"^\*\*Verdict\*\*:\s*(.*)$")
BLOCK_MARKER = re.compile(r"<!--\s*sample:\s*case-(\d{2})\s*-->")
REGION_MARKER = re.compile(r"^\s*// catalog:case-(\d{2}) (begin|end)\s*$")
CODE_LITERAL = re.compile(r"(?<![A-Za-z0-9_])(?:ELS3|VF)-[A-Za-z0-9]")


def normalize(lines: list[str]) -> str:
    """Dedent, strip trailing whitespace and drop leading and trailing blank lines."""
    text = textwrap.dedent("\n".join(line.rstrip() for line in lines))
    return "\n".join(line.rstrip() for line in text.split("\n")).strip("\n")


def read_regions(sample_root: Path, errors: list[str]) -> dict[str, str]:
    regions: dict[str, str] = {}
    for path in sorted(sample_root.rglob("*.cs")):
        if {"bin", "obj"} & set(path.relative_to(sample_root).parts):
            continue
        lines = path.read_text(encoding="utf-8").splitlines()
        open_regions: dict[str, int] = {}
        for index, line in enumerate(lines):
            match = REGION_MARKER.match(line)
            if not match:
                continue
            case, kind = match.groups()
            where = f"{path.as_posix()}:{index + 1}"
            if kind == "begin":
                if case in open_regions or case in regions:
                    errors.append(f"{where}: region case-{case} is defined twice")
                    continue
                open_regions[case] = index
            else:
                if case not in open_regions:
                    errors.append(f"{where}: region case-{case} ends without a begin")
                    continue
                start = open_regions.pop(case)
                body = [item for item in lines[start + 1:index] if not REGION_MARKER.match(item)]
                regions[case] = normalize(body)
        for case, start in open_regions.items():
            errors.append(f"{path.as_posix()}:{start + 1}: region case-{case} has no end")
    return regions


def read_catalog(catalog: Path, errors: list[str]) -> dict[str, str]:
    """Returns the marked "after" blocks by case, and checks the case headings."""
    lines = catalog.read_text(encoding="utf-8").splitlines()
    blocks: dict[str, str] = {}
    headings: list[str] = []
    ports: set[str] = set()
    current: str | None = None
    index = 0
    while index < len(lines):
        line = lines[index]
        heading = HEADING.match(line)
        if heading:
            current = heading.group(1)
            headings.append(current)
        verdict = VERDICT.match(line)
        if verdict and current is not None and re.match(r"port\b", verdict.group(1)):
            ports.add(current)
        marker = BLOCK_MARKER.search(line)
        if marker:
            case = marker.group(1)
            where = f"{catalog.as_posix()}:{index + 1}"
            if case != current:
                errors.append(f"{where}: block case-{case} sits under the heading of case {current or 'none'}")
            fence = index + 1
            while fence < len(lines) and not lines[fence].strip():
                fence += 1
            if fence >= len(lines) or not lines[fence].lstrip().startswith("```"):
                errors.append(f"{where}: block case-{case} is not followed by a code fence")
                index += 1
                continue
            end = fence + 1
            while end < len(lines) and not lines[end].lstrip().startswith("```"):
                end += 1
            if end >= len(lines):
                errors.append(f"{where}: the code fence of block case-{case} is not closed")
                break
            if case in blocks:
                errors.append(f"{where}: block case-{case} appears twice")
            blocks[case] = normalize(lines[fence + 1:end])
            index = end
        index += 1

    expected = [f"{number:02d}" for number in range(1, CASE_COUNT + 1)]
    for case in sorted(set(headings)):
        if headings.count(case) > 1:
            errors.append(f"{catalog.as_posix()}: case {case} is duplicated")
    for case in expected:
        if case not in headings:
            errors.append(f"{catalog.as_posix()}: case {case} is missing")
    for case in headings:
        if case not in expected:
            errors.append(f"{catalog.as_posix()}: case {case} is not one of 01-{CASE_COUNT:02d}")
    for case in sorted(ports - set(blocks)):
        errors.append(f"{catalog.as_posix()}: case {case} has a port verdict but no block marked <!-- sample: case-{case} -->")
    if len(set(headings)) == len(headings) and headings != sorted(headings):
        errors.append(f"{catalog.as_posix()}: cases are out of order: {', '.join(headings)}")
    return blocks


def find_code_literals(skill_folder: Path) -> list[str]:
    found = []
    for path in sorted(item for item in skill_folder.rglob("*") if item.is_file()):
        for number, line in enumerate(path.read_text(encoding="utf-8", errors="replace").splitlines(), start=1):
            for match in CODE_LITERAL.finditer(line):
                found.append(f"{path.as_posix()}:{number}: diagnostic code literal '{match.group(0)}...' (use a placeholder such as <code>)")
    return found


def check(root: Path) -> list[str]:
    errors: list[str] = []
    skill_folder, catalog, sample_root = root / SKILL_FOLDER, root / CATALOG, root / SAMPLE_ROOT
    for required in (skill_folder, catalog, sample_root):
        if not required.exists():
            return [f"{required.as_posix()} does not exist"]

    regions = read_regions(sample_root, errors)
    blocks = read_catalog(catalog, errors)
    for case in sorted(set(regions) | set(blocks)):
        if case not in blocks:
            errors.append(f"sample region case-{case} has no catalog block marked <!-- sample: case-{case} -->")
        elif case not in regions:
            errors.append(f"catalog block case-{case} has no sample region // catalog:case-{case} begin/end")
        elif blocks[case] != regions[case]:
            errors.append(f"catalog block case-{case} differs from its sample region; copy the region into the catalog")
    errors.extend(find_code_literals(skill_folder))
    return errors


class CheckMigrationSkillTests(unittest.TestCase):
    REGION = "    public int X { get; set; }\n"

    def tree(self, catalog: str, sources: dict[str, str], extra: dict[str, str] | None = None) -> Path:
        root = Path(self.directory.name)
        (root / SKILL_FOLDER).mkdir(parents=True, exist_ok=True)
        (root / CATALOG).write_text(catalog, encoding="utf-8")
        for name, text in {**sources, **(extra or {})}.items():
            path = root / name
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text(text, encoding="utf-8")
        return root

    def setUp(self):
        self.directory = tempfile.TemporaryDirectory()

    def tearDown(self):
        self.directory.cleanup()

    @staticmethod
    def catalog(blocks: dict[int, str], order: list[int] | None = None, ports: set[int] | None = None) -> str:
        """Cases with a block, or listed in ``ports``, get a port verdict; the others a redesign verdict."""
        parts = ["# Porting catalog\n"]
        for number in order or range(1, CASE_COUNT + 1):
            verdict = "port" if number in blocks or number in (ports or set()) else "redesign"
            parts.append(f"## Case {number:02d}: Title {number}\n\n**Verdict**: {verdict}\n")
            if number in blocks:
                parts.append(f"**After (Elsa 4)** <!-- sample: case-{number:02d} -->\n\n```csharp\n{blocks[number]}```\n")
        return "\n".join(parts)

    def sample(self, regions: dict[int, str]) -> dict[str, str]:
        text = "namespace Acme;\n\n" + "".join(
            f"// catalog:case-{number:02d} begin\n{body}// catalog:case-{number:02d} end\n" for number, body in regions.items())
        return {f"{SAMPLE_ROOT.as_posix()}/Acme.cs": text}

    def test_matching_catalog_and_sample_pass(self):
        root = self.tree(self.catalog({1: "public int X { get; set; }\n"}), self.sample({1: self.REGION}))
        self.assertEqual([], check(root))

    def test_nested_markers_are_dropped_and_indentation_ignored(self):
        source = ("// catalog:case-01 begin\nclass A\n{\n    // catalog:case-02 begin\n    int Y;\n    // catalog:case-02 end\n}\n"
                  "// catalog:case-01 end\n")
        root = self.tree(self.catalog({1: "class A\n{\n    int Y;\n}\n", 2: "int Y;\n"}),
                         {f"{SAMPLE_ROOT.as_posix()}/A.cs": source})
        self.assertEqual([], check(root))

    def test_build_output_is_ignored(self):
        root = self.tree(self.catalog({1: "public int X { get; set; }\n"}), self.sample({1: self.REGION}),
                         {f"{SAMPLE_ROOT.as_posix()}/obj/Debug/Copy.cs": "// catalog:case-01 begin\nx\n// catalog:case-01 end\n"})
        self.assertEqual([], check(root))

    def test_block_differing_from_region_fails(self):
        root = self.tree(self.catalog({1: "public int Y { get; set; }\n"}), self.sample({1: self.REGION}))
        self.assertIn("catalog block case-01 differs from its sample region; copy the region into the catalog", check(root))

    def test_region_without_block_fails(self):
        root = self.tree(self.catalog({}), self.sample({1: self.REGION}))
        self.assertTrue(any("sample region case-01 has no catalog block" in error for error in check(root)))

    def test_block_without_region_fails(self):
        root = self.tree(self.catalog({1: "x\n"}), self.sample({}))
        self.assertTrue(any("catalog block case-01 has no sample region" in error for error in check(root)))

    def test_missing_case_fails(self):
        root = self.tree(self.catalog({}, order=[n for n in range(1, CASE_COUNT + 1) if n != 7]), self.sample({}))
        self.assertTrue(any("case 07 is missing" in error for error in check(root)))

    def test_duplicated_case_fails(self):
        root = self.tree(self.catalog({}, order=[1, 1, *range(2, CASE_COUNT + 1)]), self.sample({}))
        self.assertTrue(any("case 01 is duplicated" in error for error in check(root)))

    def test_out_of_order_cases_fail(self):
        root = self.tree(self.catalog({}, order=[2, 1, *range(3, CASE_COUNT + 1)]), self.sample({}))
        self.assertTrue(any("out of order" in error for error in check(root)))

    def test_port_case_without_marked_block_fails(self):
        root = self.tree(self.catalog({}, ports={1}), self.sample({}))
        self.assertTrue(any("case 01 has a port verdict but no block" in error for error in check(root)))

    def test_port_part_of_a_two_part_verdict_needs_a_block(self):
        text = self.catalog({}).replace("## Case 08: Title 8\n\n**Verdict**: redesign",
                                        "## Case 08: Title 8\n\n**Verdict**: port (bookmarks); redesign (signals)")
        self.assertTrue(any("case 08 has a port verdict but no block" in error for error in check(self.tree(text, self.sample({})))))

    def test_redesign_and_interview_cases_need_no_block(self):
        text = self.catalog({}).replace("## Case 17: Title 17\n\n**Verdict**: redesign", "## Case 17: Title 17\n\n**Verdict**: interview")
        self.assertEqual([], check(self.tree(text, self.sample({}))))

    def test_block_under_another_heading_fails(self):
        text = self.catalog({}).replace("## Case 02: Title 2\n",
                                        "## Case 02: Title 2\n\n<!-- sample: case-01 -->\n\n```csharp\nx\n```\n")
        root = self.tree(text, self.sample({1: "x\n"}))
        self.assertTrue(any("sits under the heading of case 02" in error for error in check(root)))

    def test_duplicate_and_unclosed_regions_fail(self):
        source = ("// catalog:case-01 begin\nx\n// catalog:case-01 end\n// catalog:case-01 begin\ny\n// catalog:case-01 end\n"
                  "// catalog:case-02 begin\nz\n")
        errors = check(self.tree(self.catalog({1: "x\n"}), {f"{SAMPLE_ROOT.as_posix()}/A.cs": source}))
        self.assertTrue(any("region case-01 is defined twice" in error for error in errors))
        self.assertTrue(any("region case-02 has no end" in error for error in errors))

    def test_diagnostic_code_literals_in_the_skill_folder_fail(self):
        prefixes = ["ELS3", "VF"]
        literals = [f"{prefix}-X-1" for prefix in prefixes]
        root = self.tree(self.catalog({}), self.sample({}),
                         {f"{SKILL_FOLDER.as_posix()}/SKILL.md": f"Fix {literals[0]} first.\nThen `{literals[1]}`.\n"})
        errors = check(root)
        self.assertEqual(2, sum("diagnostic code literal" in error for error in errors))

    def test_placeholders_and_lookalikes_pass(self):
        root = self.tree(self.catalog({}), self.sample({}),
                         {f"{SKILL_FOLDER.as_posix()}/SKILL.md": "A finding `<code>`; the RVF-1 and xELS3-2 tokens are not codes.\n"})
        self.assertEqual([], check(root))


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--root", type=Path, default=Path(__file__).resolve().parents[2])
    parser.add_argument("--self-test", action="store_true", help="run the built-in good and bad inputs")
    arguments = parser.parse_args()
    if arguments.self_test:
        result = unittest.TextTestRunner(verbosity=1).run(unittest.defaultTestLoader.loadTestsFromTestCase(CheckMigrationSkillTests))
        return 0 if result.wasSuccessful() else 1

    errors = check(arguments.root)
    for error in errors:
        print(f"check_migration_skill: {error}", file=sys.stderr)
    if errors:
        return 1
    print("check_migration_skill: catalog matches the sample; no diagnostic code literals")
    return 0


if __name__ == "__main__":
    sys.exit(main())
