"""Keep the browser matrix a complete partition, without changing Playwright's test selection."""
import ast
from pathlib import Path
import re
import unittest

import test_tiers


WORKFLOW = Path(__file__).resolve().parents[2] / ".github/workflows/ci.yml"


def violations(workflow: str) -> list[str]:
    block = test_tiers._jobs(workflow).get("studio-browser-tests", "")
    matrix = re.search(r"^        shard: (\[[^\n]+\])$", block, re.M)
    command = re.search(r"^      - name: Run browser tests\n        run: (.+)$", block, re.M)
    if not matrix or not command:
        return ["Browser shards need a literal matrix and the complete browser command."]
    try:
        shards = ast.literal_eval(matrix.group(1))
    except (SyntaxError, ValueError):
        return ["The browser shard matrix is not a literal integer list."]
    invocation = re.fullmatch(r"pnpm test:browser --shard=\$\{\{ matrix\.shard \}\}/([1-9][0-9]*)",
                              command.group(1))
    if not invocation:
        return ["Run the complete browser suite with only the matrix shard selector."]
    total = int(invocation.group(1))
    problems = []
    if not isinstance(shards, list) or any(type(value) is not int for value in shards) or shards != list(range(1, total + 1)):
        problems.append("The browser matrix must name every shard exactly once, matching the command denominator.")
    if f"Studio browser tests (Chromium ${{{{ matrix.shard }}}}/{total})" not in block:
        problems.append("The browser job name must report the actual partition.")
    if not re.search(r"^      fail-fast: false$", block, re.M):
        problems.append("A failing browser shard must not suppress the remaining shards.")
    if re.search(r"^\s+continue-on-error:", block, re.M):
        problems.append("Browser failures must remain failures for the required aggregate.")
    return problems


class BrowserShardTests(unittest.TestCase):
    def setUp(self):
        self.workflow = WORKFLOW.read_text()

    def test_current_workflow_preserves_a_complete_partition(self):
        self.assertEqual([], violations(self.workflow))

    def rejected(self, old, new):
        changed = self.workflow.replace(old, new, 1)
        self.assertNotEqual(self.workflow, changed, "The causal control must change the actual workflow.")
        self.assertTrue(violations(changed))

    def test_missing_shard_is_rejected(self):
        self.rejected("shard: [1, 2, 3, 4]", "shard: [1, 2, 3]")

    def test_duplicate_shard_is_rejected(self):
        self.rejected("shard: [1, 2, 3, 4]", "shard: [1, 2, 3, 3]")

    def test_mismatched_denominator_is_rejected(self):
        self.rejected("run: pnpm test:browser --shard=${{ matrix.shard }}/4",
                      "run: pnpm test:browser --shard=${{ matrix.shard }}/5")

    def test_hidden_test_filter_is_rejected(self):
        self.rejected("run: pnpm test:browser --shard=${{ matrix.shard }}/4",
                      "run: pnpm test:browser --shard=${{ matrix.shard }}/4 --grep smoke")

    def test_browser_fail_fast_is_rejected(self):
        start = self.workflow.index("  studio-browser-tests:")
        changed = self.workflow[:start] + self.workflow[start:].replace("fail-fast: false", "fail-fast: true", 1)
        self.assertTrue(violations(changed))

    def test_ignored_browser_failure_is_rejected(self):
        self.rejected("  studio-browser-tests:\n", "  studio-browser-tests:\n    continue-on-error: true\n")


if __name__ == "__main__":
    unittest.main()
