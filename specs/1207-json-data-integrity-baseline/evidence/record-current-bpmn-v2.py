"""Retain a real format-2 publication and independently pin its current canonical material hash.

This is a material witness, not execution recovery or an authentic pre-policy writer capture.
Original spec1017 JSON and its source-bound provenance are never rewritten.
"""
import argparse
import hashlib
import importlib.util
import json
from pathlib import Path
import re
import subprocess

module_spec = importlib.util.spec_from_file_location('model_vectors', Path(__file__).with_name('generate-model-material-hash-vectors.py'))
model = importlib.util.module_from_spec(module_spec)
module_spec.loader.exec_module(model)

ORIGINAL = 'specs/1017-direct-bpmn-hosting/evidence/pipeline/technical-fault-native-recovery/legacy-model-v2.json'
ORIGINAL_GIT_SHA256 = 'd14491ae0d95700966c544863b4c7183573c0313f23af362dc25f994c15b8ef9'
NAMES = {'Elsa.Activities.Bpmn.Tests.dll', 'Elsa.Activities.Bpmn.dll', 'Elsa.Workflows.Runtime.Core.dll',
         'Elsa.Workflows.Runtime.dll', 'Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.dll', 'Bpmn.Model.dll', 'Bpmn.Semantics.dll'}


def verify_capture(data):
    report = json.loads(data)
    raw = model.helper.load_json(data.decode('utf-8'))
    provenance = report.get('captureProvenance', {})
    source = provenance.get('sourceSha', '')
    if (report.get('schemaVersion') != 1 or report.get('kind') != 'current-writer-format2-material'
            or provenance.get('version') != 1 or provenance.get('sourceDirty') is not False or not re.fullmatch('[0-9a-f]{40}', source)):
        raise ValueError('A clean source-bound format-2 publication capture is required')
    hashes, versions = provenance.get('binaryHashes', {}), provenance.get('binaryVersions', {})
    if set(hashes) != NAMES or set(versions) != NAMES or any(not re.fullmatch('[0-9a-f]{64}', value) for value in hashes.values()):
        raise ValueError('Incomplete capture-time binary inventory')
    for name in NAMES - {'Bpmn.Model.dll', 'Bpmn.Semantics.dll'}:
        if not versions[name].endswith('+' + source):
            raise ValueError('Capture binary does not identify its claimed source: ' + name)
    definition = report['definition']
    if definition['formatVersion'] != 2 or any('faultPolicy' in binding for binding in definition['serviceBindings']):
        raise ValueError('This witness must keep the absent format-2 fault policy')
    if report['inputContract'] != dict(version=1, inputs=[]) or report['outputs'] or report['runtimeRequirements']:
        raise ValueError('Unsupported material shape: use this witness only for its exact closed publication')
    expected_dependencies = [dict(bindingId=binding['bindingId'], executable=binding['executable']) for binding in definition['serviceBindings']]
    if report['dependencies'] != expected_dependencies:
        raise ValueError('The definition and complete dependency pins differ')
    pin_fields = {'artifactId', 'definitionId', 'definitionVersionId', 'artifactVersion', 'artifactHash'}
    dependencies = []
    for dependency in report['dependencies']:
        if set(dependency['executable']) != pin_fields:
            raise ValueError('A worker dependency pin is incomplete')
        dependencies.append(dict(bindingId=model.opaque(dependency['bindingId']),
            executable={key: model.opaque(value) for key, value in dependency['executable'].items()}))
    projection = dict(domain=model.opaque('elsa.runtime.model-material/v1'), formatVersion=model.helper.Number('1'),
        protocol=dict(key=model.opaque(report['protocol']['key']), version=raw['protocol']['version']),
        profile=model.opaque(report['profile']), definition=raw['definition'], inputContract=raw['inputContract'],
        outputs=[], dependencies=dependencies, runtimeRequirements=[])
    canonical = model.helper.canonical(projection)
    expected = 'sha256:' + hashlib.sha256(canonical.encode()).hexdigest()
    if expected != report['expectedContentHash'] or expected != report['identity']['artifactHash']:
        raise ValueError('Independent canonical hash differs from the actual publication')
    return report, canonical, expected


def record(repo, capture, output):
    data = capture.read_bytes()
    report, canonical, expected = verify_capture(data)
    original = subprocess.check_output(['git', 'show', 'HEAD:' + ORIGINAL], cwd=repo)
    if hashlib.sha256(original).hexdigest() != ORIGINAL_GIT_SHA256:
        raise ValueError('The historical Git blob differs from its archived original')
    if output.exists():
        raise ValueError('Use a new output directory; retained captures are never overwritten')
    subprocess.run(['git', 'cat-file', '-e', report['captureProvenance']['sourceSha'] + '^{commit}'], cwd=repo, check=True)
    output.mkdir(parents=True)
    (output / 'current-model-v2.json').write_bytes(data)
    manifest = dict(spec=1207, kind='current-writer-format2-material-golden',
        writerSourceSha=report['captureProvenance']['sourceSha'],
        captureSha256=hashlib.sha256(data).hexdigest(), canonical=canonical, expectedContentHash=expected,
        packagerSha256=hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),
        historicalOriginal=dict(path=ORIGINAL, gitBlobSha256=ORIGINAL_GIT_SHA256,
            expectedContentHash=json.loads(original)['expectedContentHash']),
        scope=report['scope'], freeze='Frozen from Elsa 4 first stable; changing this recipe after that requires the agreed compatibility process.')
    (output / 'current-model-v2.golden.json').write_text(json.dumps(manifest, indent=2) + '\n', encoding='utf-8')
    print(json.dumps(dict(writerSourceSha=manifest['writerSourceSha'], expectedContentHash=expected, captureSha256=manifest['captureSha256'])))


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--repo', type=Path, required=True)
    parser.add_argument('--capture', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    record(args.repo.resolve(), args.capture.resolve(), args.output.resolve())
