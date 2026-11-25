from pathlib import Path
import base64
import hashlib
import hmac
import importlib.util
import json
import unittest

spec = importlib.util.spec_from_file_location('nested_bindings', Path(__file__).with_name('generate-nested-binding-hash-vectors.py'))
generator = importlib.util.module_from_spec(spec)
spec.loader.exec_module(generator)


class NestedBindingVectorsTests(unittest.TestCase):
    def test_checked_in_pins_preimages_and_frames_reproduce(self):
        expected = generator.generate()
        freeze = expected['freeze']
        self.assertEqual(expected, json.loads(Path(__file__).with_name('nested-binding-hash-vectors.json').read_text()), freeze)
        for view in expected['savedViews']:
            self.assertEqual(view['keyedHash'], hashlib.sha256(view['keyedCanonical'].encode()).hexdigest(), freeze)
            self.assertEqual(view['requestFrame'], generator.frame('workflow.view.save.v1', view['requestCanonical']), freeze)
            self.assertEqual(view['requestHash'], 'sha256:' + hashlib.sha256(view['requestFrame'].encode()).hexdigest(), freeze)
        for binding in expected['recoveryBindings']:
            self.assertEqual(binding['sha256'], hashlib.sha256(binding['canonical'].encode()).hexdigest(), freeze)
            key = hmac.new(b'k' * 32, b'Elsa.RuntimeRecoveryContinuation.JsonHashInput.v1', hashlib.sha256).digest()
            signature = hmac.new(key, b'json:ef-runtime-recovery-liveness-v1.' + binding['cursorCanonical'].encode(), hashlib.sha256).digest()
            self.assertEqual(binding['cursorSignature'], base64.urlsafe_b64encode(signature).decode().rstrip('='), freeze)

    def test_recovery_only_ignores_offset_and_page_limit_not_ticks_presence_or_ordinary_text(self):
        pins = {value['name']: value['sha256'] for value in generator.generate()['recoveryBindings']}
        self.assertEqual(pins['base'], pins['same-UTC-offset'])
        self.assertEqual(pins['base'], pins['different-page-limit'])
        for name in ['null-owner', 'other-scope', 'one-UTC-tick', 'one-lease-tick', 'one-heartbeat-tick', 'other-owner']:
            self.assertNotEqual(pins['base'], pins[name])
        self.assertNotEqual(pins['owner-text-a'], pins['owner-text-b'])

    def test_saved_view_columns_and_ordinary_text_remain_significant(self):
        views = {value['name']: value for value in generator.generate()['savedViews']}
        for field in ['keyedHash', 'requestHash']:
            self.assertNotEqual(views['base'][field], views['column-order'][field])
            self.assertNotEqual(views['base'][field], views['shared'][field])
            self.assertNotEqual(views['text-a'][field], views['text-b'][field])


if __name__ == '__main__':
    unittest.main()
