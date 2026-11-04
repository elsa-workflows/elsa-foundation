import unittest
from studio_gate import PROOFS, failures


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

    def test_unrelated_change_does_not_need_studio(self):
        self.assertEqual([], failures('false', {}))

    def test_missing_scope_is_not_a_fast_path(self):
        for scope in (None, '', 'True', 'unknown'):
            self.assertTrue(failures(scope, {}))


if __name__ == '__main__':
    unittest.main()
