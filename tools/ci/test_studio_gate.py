import unittest
from studio_gate import HEAVY_PROOFS, PROOFS, failures


class StudioGateTests(unittest.TestCase):
    def test_each_selected_proof_must_succeed(self):
        green = dict.fromkeys(PROOFS, 'success')
        self.assertEqual([], failures('true', green))
        for proof in PROOFS:
            for status in ('failure', 'cancelled', 'skipped', '', None):
                with self.subTest(proof=proof, status=status):
                    self.assertTrue(failures('true', dict(green, **{proof: status})))
            missing = dict(green)
            del missing[proof]
            self.assertTrue(failures('true', missing))

    def test_draft_may_skip_only_the_heavy_proofs(self):
        green = dict.fromkeys(PROOFS, 'success')
        for proof in PROOFS:
            with self.subTest(proof=proof):
                skipped = dict(green, **{proof: 'skipped'})
                self.assertEqual(proof not in HEAVY_PROOFS, bool(failures('true', skipped, 'false')))
                self.assertTrue(failures('true', skipped, 'true'))
        for proof in HEAVY_PROOFS:
            self.assertTrue(failures('true', dict(green, **{proof: 'failure'}), 'false'))

    def test_missing_heavy_scope_is_not_a_fast_path(self):
        for heavy in (None, '', 'True', 'unknown'):
            self.assertTrue(failures('true', dict.fromkeys(PROOFS, 'success'), heavy))

    def test_unrelated_change_does_not_need_studio(self):
        self.assertEqual([], failures('false', {}))

    def test_missing_scope_is_not_a_fast_path(self):
        for scope in (None, '', 'True', 'unknown'):
            self.assertTrue(failures(scope, {}))


if __name__ == '__main__':
    unittest.main()
