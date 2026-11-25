"""Independent controls for the source-proved Publishing schema-presence migration."""
from pathlib import Path
import copy
import hashlib
import importlib.util
import json
import unittest

DIRECTORY = Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location('publishing_presence', DIRECTORY / 'generate-publishing-schema-presence-inputs.py')
generator = importlib.util.module_from_spec(spec)
spec.loader.exec_module(generator)


class PublishingPresenceTests(unittest.TestCase):
    def test_retained_vectors_reproduce_all_fourteen_prior_pins_before_deriving_current_pins(self):
        expected = json.loads((DIRECTORY / 'publishing-schema-presence-vectors.json').read_text())
        self.assertEqual(expected, generator.generate())
        self.assertEqual(14, len(expected))
        for row in expected:
            with self.subTest(row=row['name']):
                preimage = row.get('canonical',row.get('frame'))
                self.assertEqual(row['sha256'], hashlib.sha256(preimage.encode()).hexdigest())
                self.assertNotEqual(row['previousSha256'], row['sha256'])

    def test_each_structured_recipe_changes_only_source_proved_absent_descriptor_schemas(self):
        recipes = [(name,lambda old,name=name:generator.compiler_material(name,old)) for name in [
            'literal-input','declared-input-contract','dependency-set','javascript-expression',
            'nested-child-slots','sequence-with-container-variables']]
        recipes += [('whole',generator.whole_behavior),('whole-template',generator.template_material),
                    ('scoped-template',lambda old:generator.template_material(old,True)),
                    ('whole-binding',generator.whole_fingerprint),('activity-contract',generator.activity_contract),
                    ('reflected-unbound',generator.fixture_behavior),
                    ('reflected-literal',lambda old:generator.fixture_behavior(old,True))]

        def remove_absent_descriptor_schemas(value):
            if isinstance(value,list):
                return [remove_absent_descriptor_schemas(item) for item in value]
            if isinstance(value,dict):
                descriptor = {'alias','collectionKind','schemaVersion'} <= value.keys()
                return {key:remove_absent_descriptor_schemas(item) for key,item in value.items()
                        if not (descriptor and key == 'schema' and item is None)}
            return value

        for name,recipe in recipes:
            with self.subTest(name=name):
                before,after = recipe(True),recipe(False)
                self.assertNotEqual(before,after)
                self.assertEqual(after,remove_absent_descriptor_schemas(before))

    def test_installed_corpus_identities_match_the_independent_models(self):
        rows = {row['name']:row for row in generator.generate()}
        for name in ['literal-input','declared-input-contract','dependency-set','javascript-expression',
                     'nested-child-slots','sequence-with-container-variables','legacy-null-compatibility']:
            with self.subTest(name=name):
                fixture = json.loads((generator.FIXTURES / (name + '.json')).read_text())
                self.assertEqual('sha256:' + rows[name]['sha256'],fixture['identity']['artifactHash'])
                self.assertEqual('artifact-' + rows[name]['sha256'][:12],fixture['identity']['artifactId'])
        compiler_rows = json.loads((DIRECTORY / 'compiler-hash-vectors.json').read_text())
        self.assertEqual(rows['declared-input-contract']['sha256'],
                         next(row['sha256'] for row in compiler_rows if row['name']=='declared-input-contract'))

    def test_explicit_json_null_schema_is_a_distinct_hash_input_and_raw_null_fields_stay_present(self):
        absent = generator.whole_behavior(False)
        explicit = copy.deepcopy(absent)
        explicit['nodes'][0]['inputBindings'][0]['contract']['targetType']['schema'] = None
        self.assertNotEqual(generator.digest(absent),generator.digest(explicit))
        # RuntimeValueTypeDescriptor is another owner contract; it still writes
        # its schema:null. The migration never recursively drops arbitrary nulls.
        expression = generator.compiler_material('javascript-expression',False)['nodes'][0]['inputBindings'][0]['contract']['expression']
        self.assertIn('schema',expression['resultType'])
        self.assertIsNone(expression['resultType']['schema'])
        self.assertIn('literalValue',absent['nodes'][0]['inputBindings'][0]['contract'])


if __name__ == '__main__':
    unittest.main()
