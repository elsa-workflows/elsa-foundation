"""Synthetic aggregate admission controls, never old-writer or native execution evidence."""
import copy
from pathlib import Path
from types import SimpleNamespace
import tempfile
import unittest
from unittest.mock import Mock, patch

import effects_journeys as effects
from upgrade_journeys import LEGACY_RESTART_CASES


def synthetic_rows(ids, source="a" * 40):
    return [{"id": key, "source": source, "status": "passed", "evidenceCategory": effects.EVIDENCE_CATEGORY,
             "liveProvider": False, "qualified": False, "independentPackageWitness": False,
             "record": {"path": "/unexecuted/" + key.replace("/", "-") + ".json", "sha256": "b" * 64}}
            for key in ids]


class EffectRestartInventoryTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.run = SimpleNamespace(root=Path(self.temporary.name), source="a" * 40, report={}, save=Mock())
        self.bindings = {"synthetic-source": "c" * 64}
        self.binding_patch = patch.object(effects, "restart_source_bindings", return_value=self.bindings)
        self.binding_patch.start()
        self.addCleanup(self.binding_patch.stop)

    def section(self):
        effects.append_effect_restarts(self.run, synthetic_rows(effects.CLASSED_RESTART_CASES))
        return effects.append_effect_restarts(self.run, synthetic_rows(LEGACY_RESTART_CASES))

    def test_readers_partition_the_exact_thirty_cases_on_each_adapter(self):
        self.assertEqual(30, len(effects.RESTART_REQUIREMENTS))
        self.assertEqual(14, len(effects.CLASSED_RESTART_CASES))
        self.assertEqual(16, len(LEGACY_RESTART_CASES))
        self.assertTrue(set(effects.CLASSED_RESTART_CASES).isdisjoint(LEGACY_RESTART_CASES))
        self.assertEqual(set(effects.RESTART_REQUIREMENTS), set(effects.CLASSED_RESTART_CASES) | set(LEGACY_RESTART_CASES))

    def test_partial_and_failed_children_remain_visible_without_promotion(self):
        rows = synthetic_rows(effects.CLASSED_RESTART_CASES)
        rows[0].update(status="failed", failure={"cause": "native.unknown"})
        section = effects.append_effect_restarts(self.run, rows)
        self.assertEqual("failed", section["status"])
        self.assertEqual(rows, section["records"])
        self.assertTrue(effects.restart_manifest_gaps(section, self.run.source))
        self.assertNotIn("independentPackageWitness", self.run.report)
        self.assertFalse(section["independentPackageWitness"])
        with self.assertRaises(RuntimeError):
            effects.append_effect_restarts(self.run, synthetic_rows(LEGACY_RESTART_CASES), complete=True)
        self.assertEqual(30, len(section["records"]))
        self.assertEqual("native.unknown", section["records"][0]["failure"]["cause"])
        self.assertEqual("failed", section["status"])

    def test_missing_or_duplicate_records_cannot_complete_or_replace_a_partial_lane(self):
        with self.assertRaises(RuntimeError):
            effects.append_effect_restarts(self.run, synthetic_rows(effects.CLASSED_RESTART_CASES), complete=True)
        section = self.run.report[effects.RESTART_SECTION]
        self.assertEqual("failed", section["status"])
        before = copy.deepcopy(section)
        with self.assertRaises(RuntimeError):
            effects.append_effect_restarts(self.run, synthetic_rows(effects.CLASSED_RESTART_CASES))
        self.assertEqual(before, section)

    def test_aggregate_orders_both_native_subsets_but_requires_their_actual_retained_bytes(self):
        section = self.section()
        self.assertEqual(list(effects.RESTART_REQUIREMENTS), [row["id"] for row in section["records"]])
        self.assertEqual("running", section["status"])
        section["status"] = "passed"
        self.assertEqual([], effects.restart_manifest_gaps(section, self.run.source))
        with self.assertRaises(RuntimeError):
            effects.require_effect_restarts(section, self.run.root, source=self.run.source)

    def test_every_structural_identity_and_evidence_classification_is_required(self):
        section = self.section()
        section["status"] = "passed"
        mutations = (lambda x: x.update(source="d" * 40), lambda x: x.update(scope="diagnostic-only"),
                     lambda x: x.update(qualified=True), lambda x: x.update(independentPackageWitness=True),
                     lambda x: x.update(liveProvider=True), lambda x: x["caseIDs"].reverse(),
                     lambda x: x["records"].pop(), lambda x: x["records"].append(x["records"][0]),
                     lambda x: x["records"][0].update(status="skipped"),
                     lambda x: x["records"][0].update(source="d" * 40),
                     lambda x: x["records"][0].update(qualified=True),
                     lambda x: x["records"][0].update(independentPackageWitness=True),
                     lambda x: x["records"][0].update(liveProvider=True),
                     lambda x: x["records"][0].pop("record"))
        for mutate in mutations:
            changed = copy.deepcopy(section)
            mutate(changed)
            with self.subTest(changed=changed.get("status")):
                self.assertTrue(effects.restart_manifest_gaps(changed, self.run.source))

    def test_complete_dispatch_reads_both_subsets_in_their_declared_order(self):
        # These mocks isolate only dispatch. Other controls exercise actual missing/changed refs.
        section = self.section()
        section["status"] = "passed"
        with patch.object(effects, "require_classed_effect_restarts") as classed, \
                patch("upgrade_journeys.require_legacy_effect_restarts") as legacy:
            effects.require_effect_restarts(section, self.run.root, source=self.run.source)
        self.assertEqual(list(effects.CLASSED_RESTART_CASES), [row["id"] for row in classed.call_args.args[0]])
        self.assertEqual(list(LEGACY_RESTART_CASES), [row["id"] for row in legacy.call_args.args[0]])
        self.assertEqual({"source": self.run.source}, classed.call_args.kwargs)
        self.assertEqual({"source": self.run.source}, legacy.call_args.kwargs)

    def test_deep_refusal_cannot_leave_a_passed_aggregate_or_promote_the_parent(self):
        effects.append_effect_restarts(self.run, synthetic_rows(effects.CLASSED_RESTART_CASES))
        with patch.object(effects, "require_classed_effect_restarts"), \
                patch("upgrade_journeys.require_legacy_effect_restarts", side_effect=RuntimeError("native writer bytes changed")):
            with self.assertRaisesRegex(RuntimeError, "native writer bytes changed"):
                effects.append_effect_restarts(self.run, synthetic_rows(LEGACY_RESTART_CASES), complete=True)
        section = self.run.report[effects.RESTART_SECTION]
        self.assertEqual("failed", section["status"])
        self.assertEqual("native writer bytes changed", section["failure"]["message"])
        self.assertNotIn("independentPackageWitness", self.run.report)

    def test_source_binding_change_and_post_acceptance_append_are_refused(self):
        section = self.section()
        section["status"] = "passed"
        with self.assertRaises(RuntimeError):
            effects.append_effect_restarts(self.run, [])
        section["status"] = "running"
        section["bindings"] = {"changed": "0" * 64}
        with self.assertRaises(RuntimeError):
            effects.append_effect_restarts(self.run, [])


if __name__ == "__main__":
    unittest.main()
