"""Retain complete, independently checked current native-start/cancellation captures."""
import argparse
import hashlib
import importlib.util
import json
from pathlib import Path
import re
import shutil
import subprocess
import sys

REPO = Path(__file__).resolve().parents[3]
VERIFIERS = REPO / 'e2e-tests/etl'
WRITERS = {'Elsa.Workbench.dll', 'Elsa.Activities.Bpmn.Core.dll', 'Elsa.Activities.Bpmn.dll', 'Elsa.Activities.Bpmn.Api.dll',
           'Elsa.Serialization.Core.dll', 'Elsa.Activities.Runtime.Core.dll', 'Elsa.Workflows.Runtime.Core.dll',
           'Elsa.Workflows.Runtime.dll', 'Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.dll', 'Elsa.Etl.Core.dll',
           'Elsa.Etl.Runtime.dll', 'Elsa.Etl.Workflows.Runtime.dll', 'Elsa.Etl.Persistence.EntityFrameworkCore.dll', 'Elsa.Etl.Runtime.Api.dll'}


def sha(data):
    return hashlib.sha256(data).hexdigest()


def writers(process, source):
    products = process.get('products', {})
    rows = products.get('actualProductVersions', [])
    if (re.fullmatch('[0-9a-f]{40}', source) is None or products.get('sourceCommit') != source
            or len(rows) != len(WRITERS) or {row.get('name') for row in rows} != WRITERS):
        raise ValueError('Capture the complete canonical, schema, native and ETL writer inventory')
    for row in rows:
        if (not row.get('productVersion', '').endswith('+' + source)
                or re.fullmatch('[0-9a-f]{64}', row.get('sha256', '')) is None):
            raise ValueError('A captured writer lacks its exact source or binary digest')
    return rows


def load_verifier(name):
    sys.path.insert(0, str(VERIFIERS))
    spec = importlib.util.spec_from_file_location(name, VERIFIERS / (name + '.py'))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def inspect(folder, name):
    manifest_path = folder / 'manifest.json'
    manifest = json.loads(manifest_path.read_text())
    cancellation = name == 'native-cancellation'
    kind = 'actual-direct-bpmn-native-etl-worker-cancellation' if cancellation else 'actual-bpmn-cancellation-before-native-admission'
    if manifest.get('kind') != kind or cancellation and manifest.get('workflowStateSchemaVersion') != '2.1.0':
        raise ValueError('The current family must pin its actual scenario and worker schema')
    process_paths = [row['path'] for row in manifest['assets'] if Path(row['path']).name == 'process.json']
    if len(process_paths) != 1:
        raise ValueError('A native capture needs exactly one actual process receipt')
    process = json.loads((folder / process_paths[0]).read_text())
    products = writers(process, manifest['sourceRevision'])
    verifier = 'verify_http_recovery' if cancellation else 'verify_native_start_retirement'
    result = load_verifier(verifier).verify(manifest_path.resolve(strict=True))
    return manifest, products, result


def record(retirement, cancellation, output):
    if output.exists():
        raise ValueError('Use a new output directory; never overwrite retained captures')
    checked = [(name, folder, *inspect(folder, name)) for name, folder in
               [('native-start-retirement', retirement), ('native-cancellation', cancellation)]]
    source = subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=REPO, text=True).strip()
    bound_files = [Path(__file__).resolve(), VERIFIERS / 'verify_http_recovery.py', VERIFIERS / 'verify_native_start_retirement.py']
    verifier_hashes = {}
    for file in bound_files:
        relative = file.relative_to(REPO).as_posix()
        committed = subprocess.check_output(['git', 'show', source + ':' + relative], cwd=REPO)
        # Git's working-tree newline conversion is not part of the Python recipe.
        if committed.replace(b'\r\n', b'\n') != file.read_bytes().replace(b'\r\n', b'\n'):
            raise ValueError('Commit the exact packaging/verifier source before recording fixtures')
        verifier_hashes[relative] = sha(committed)
    for name, folder, manifest, products, result in checked:
        target = output / name
        target.mkdir(parents=True)
        for asset in ['manifest.json', *[row['path'] for row in manifest['assets']]]:
            origin = (folder / asset).resolve(strict=True)
            if not origin.is_relative_to(folder.resolve()):
                raise ValueError('Capture asset escaped its owned folder')
            destination = target / asset
            destination.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(origin, destination)
        receipt = dict(schemaVersion=1, writerSourceSha=manifest['sourceRevision'], packagingSourceSha=source,
                       manifestSha256=sha((target / 'manifest.json').read_bytes()), writers=products,
                       verifierSourceHashes=verifier_hashes, verification=result)
        (target / 'receipt.json').write_text(json.dumps(receipt, indent=2) + '\n')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--retirement', type=Path, required=True)
    parser.add_argument('--cancellation', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    record(args.retirement.resolve(strict=True), args.cancellation.resolve(strict=True), args.output.resolve())
