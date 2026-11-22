import unittest
from studio_gate import PROOFS, failures


class StudioGateTests(unittest.TestCase):
    def test_each_selected_proof_must_succeed(self):
        green = dict.fromkeys(PROOFS, 'success')
        self.assertEqual([], failures('true', green, 'true', 'true'))
        for proof in PROOFS:
            for status in ('failure', 'cancelled', 'skipped', '', None):
                with self.subTest(proof=proof, status=status):
                    self.assertTrue(failures('true', dict(green, **{proof: status}), 'true', 'true'))
            missing = dict(green)
            del missing[proof]
            self.assertTrue(failures('true', missing, 'true', 'true'))

    def test_draft_may_skip_only_the_heavy_proofs(self):
        green = dict.fromkeys(PROOFS, 'success')
        for proof in PROOFS:
            with self.subTest(proof=proof):
                skipped = dict(green, **{proof: 'skipped'})
                self.assertEqual(proof not in ('STUDIO_BROWSER', 'STUDIO_UX'), bool(failures('true', skipped, 'false', 'false')))
                self.assertTrue(failures('true', skipped, 'true', 'true'))
        for proof in ('STUDIO_BROWSER', 'STUDIO_UX'):
            self.assertTrue(failures('true', dict(green, **{proof: 'failure'}), 'false', 'false'))

    def test_missing_heavy_scope_is_not_a_fast_path(self):
        for heavy in (None, '', 'True', 'unknown'):
            self.assertTrue(failures('true', dict.fromkeys(PROOFS, 'success'), heavy, 'true'))

    def test_unrelated_change_does_not_need_studio(self):
        self.assertEqual([], failures('false', {}, 'false', 'false'))

    def test_missing_scope_is_not_a_fast_path(self):
        for scope in (None, '', 'True', 'unknown'):
            self.assertTrue(failures(scope, {}, 'false', 'false'))

    def test_routine_studio_run_can_skip_ux_while_browser_remains_required(self):
        results = dict.fromkeys(PROOFS, 'success')
        results['STUDIO_UX'] = 'skipped'
        self.assertEqual([], failures('true', results, 'true', 'false'))
        self.assertTrue(failures('true', dict(results, STUDIO_BROWSER='skipped'), 'true', 'false'))
        self.assertTrue(failures('true', dict(results, STUDIO_UX='failure'), 'true', 'false'))

    def test_nightly_ux_failure_cannot_hide_behind_unselected_studio(self):
        results = dict.fromkeys(PROOFS, 'skipped')
        results['STUDIO_UX'] = 'success'
        self.assertEqual([], failures('false', results, 'false', 'true'))
        for status in ('failure', 'cancelled', 'skipped', None):
            with self.subTest(status=status):
                self.assertTrue(failures('false', dict(results, STUDIO_UX=status), 'false', 'true'))

    def test_missing_ux_scope_is_not_a_fast_path(self):
        for ux in (None, '', 'True', 'unknown'):
            self.assertTrue(failures('false', {}, 'false', ux))


if __name__ == '__main__':
    unittest.main()
