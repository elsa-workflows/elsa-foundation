"""Independent complete-store and real-receiver checks for the S9 current ETL captures."""
from collections import Counter
from contextlib import closing
import argparse
import base64
import hashlib
import importlib.util
import json
from pathlib import Path
import re
import sqlite3

_spec = importlib.util.spec_from_file_location('canonical_fixture', Path(__file__).with_name('generate-variable-hash-vectors.py'))
canonical = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(canonical)


def sha(data):
    return hashlib.sha256(data).hexdigest()


def json_hash(raw):
    return sha(canonical.canonical(canonical.load_json(raw)).encode('utf-8'))


def require(condition, message):
    if not condition:
        raise ValueError(message)


def text(value):
    require(isinstance(value, str) and value.startswith('u16:'), 'Expected the typed lossless UTF16 field')
    return base64.b64decode(value[4:], validate=True).decode('utf-16-le')


def writers(rows, source, required):
    require(re.fullmatch('[0-9a-f]{40}', source) is not None, 'Missing clean writer source')
    names = [row['name'] for row in rows]
    require(len(names) == len(set(names)) and set(required) <= set(names), 'Missing or duplicate actual writer')
    for row in rows:
        require(re.fullmatch('[0-9a-f]{64}', row['sha256']) is not None and row['productVersion'].endswith('+' + source), 'Actual binary source/digest differs')


def verify_files(folder, manifest):
    listed = set()
    for item in manifest['files']:
        path = (folder / item['path']).resolve(strict=True)
        require(path.is_relative_to(folder.resolve()) and item['path'] not in listed, 'Unbounded or duplicate captured file')
        listed.add(item['path'])
        data = path.read_bytes()
        require(len(data) == item['byteLength'] and sha(data) == item['sha256'], 'Captured file differs: ' + item['path'])
    actual = {path.relative_to(folder).as_posix() for path in folder.rglob('*') if path.is_file()}
    require(actual == listed | {'manifest.json'}, 'Captured file inventory differs from its manifest')
    return listed


def inspect_database(path):
    require(not any(Path(str(path) + suffix).exists() for suffix in ['-wal', '-shm', '-journal']), 'Unbound database sidecar')
    checks = []
    with closing(sqlite3.connect(path.resolve().as_uri() + '?mode=ro&immutable=1', uri=True)) as connection:
        connection.row_factory = sqlite3.Row
        require([tuple(row) for row in connection.execute('PRAGMA integrity_check')] == [('ok',)], 'Corrupt captured database')
        require(not connection.execute('PRAGMA foreign_key_check').fetchall(), 'Captured foreign-key violation')
        tables = [row[0] for row in connection.execute("SELECT name FROM sqlite_master WHERE type='table' ORDER BY name")]
        counts = {}
        batches = []
        for table in tables:
            quoted = '"' + table.replace('"', '""') + '"'
            rows = [dict(row) for row in connection.execute('SELECT * FROM ' + quoted)]
            counts[table] = len(rows)
            for row in rows:
                for payload, digest in [('PipelineJson', 'PipelineHash'), ('ArtifactJson', 'ArtifactEnvelopeHash'),
                                        ('FetchJson', 'FetchDigest'), ('PageJson', 'PageDigest'),
                                        ('DescriptorJson', 'DescriptorDigest'), ('OutputJson', 'OutputDigest'),
                                        ('HttpDeliveryJson', 'HttpDeliveryDigest')]:
                    if payload not in row or digest not in row:
                        continue
                    require((row[payload] is None) == (row[digest] is None), 'Missing JSON/digest partner')
                    if row[payload] is not None:
                        actual = json_hash(row[payload])
                        require(actual == row[digest], 'Stored JSON digest differs: ' + table + '/' + digest)
                        checks.append(dict(table=table, digest=digest, sha256=actual))
                if 'ArtifactJson' in row and 'PipelineHash' in row:
                    # Keep raw number tokens while selecting the nested current artifact pipeline.
                    artifact = canonical.load_json(row['ArtifactJson'])
                    actual = sha(canonical.canonical(artifact['pipeline']).encode())
                    require(actual == row['PipelineHash'], 'Published pipeline digest differs')
                    checks.append(dict(table=table, digest='PipelineHash', sha256=actual))
            if table == 'elsa_etl_connector_batches':
                batches = rows
        return counts, checks, batches


def verify_receiver(batches, deliveries):
    require(len(batches) == len(deliveries) == 5, 'Expected all five physical deliveries and retained batches')
    actual = {}
    names = []
    for delivery in deliveries:
        require(delivery['method'] == 'POST' and delivery['rawTarget'] == '/output', 'Unexpected physical output route')
        headers = {key.lower(): value for key, value in delivery['headers'].items()}
        body = base64.b64decode(delivery['body'], validate=True)
        key = headers['idempotency-key']
        require(key not in actual and int(headers['content-length']) == len(body), 'Duplicate output effect or wrong physical length')
        require(headers['host'] == '127.0.0.1:29196' and headers['content-type'] == 'application/json', 'Output destination/profile differs')
        values = json.loads(body)
        require(isinstance(values, list) and all(set(value) == {'name'} for value in values), 'Unexpected transformed output shape')
        names.extend(value['name'] for value in values)
        actual[key] = (body, len(values))
    require(Counter(names) == Counter(['Ada', 'Bram', 'Cleo', 'Dirk', 'Eva', 'Finn']), 'Transformed business rows differ or repeat')
    retained = set()
    for batch in batches:
        output = json.loads(batch['OutputJson'])
        require(batch['Settled'] == 1 and output['errors'] is None and output['rejectedRecords'] == output['filteredRecords'] == 0, 'Unexpected unsettled/error output')
        main = output['main']
        key = text(main['effectId'])
        require(main['kind'] == 'http' and key in actual and key not in retained, 'Receiver effect does not bind its original retained main output')
        retained.add(key)
        body, count = actual[key]
        require(main['recordCount'] == output['acceptedRecords'] == count, 'Physical/retained output count differs')
        require(main['body']['byteLength'] == len(body) and text(main['body']['contentHash']) == json_hash(body), 'Physical body differs from the retained HTTP route hash/length')
    require(retained == set(actual), 'An actual output lacks its original retained effect')


def verify(root):
    reports = {}
    required = ['Elsa.Etl.Core.dll', 'Elsa.Etl.Runtime.dll', 'Elsa.Etl.Persistence.EntityFrameworkCore.dll',
                'Elsa.Serialization.Core.dll', 'Elsa.Workflows.Runtime.Core.dll', 'Elsa.Activities.Runtime.Core.dll']
    for family in ['current-v1', 'current-continuous']:
        folder = root / family
        manifest = json.loads((folder / 'manifest.json').read_bytes())
        listed = verify_files(folder, manifest)
        expected_databases = ({'published/etl-design.db', 'published/workflow.db', 'runtime/runtime.db'}
                              if family == 'current-v1' else {'runtime.db'})
        databases_on_disk = {path.relative_to(folder).as_posix() for path in folder.rglob('*.db')}
        require(databases_on_disk == expected_databases and expected_databases <= listed,
                'Missing or unexpected complete-store database: ' + family)
        if family == 'current-v1':
            provenance = json.loads((folder / 'capture-provenance.json').read_bytes())
            source = provenance['sourceSha']
            require(provenance['sourceDirty'] is False and manifest['producerSourceBaseline'] == manifest['captureBaseCommit'] == source, 'V1 writer provenance differs')
            writers(provenance['workerWriters'], source, [*required, 'Elsa.Etl.WorkerHost.dll'])
            writers(provenance['hostWriters'], source, [*required, 'Elsa.Etl.Tests.dll'])
        else:
            source = manifest['sourceSha']
            require(manifest['sourceDirty'] is False and manifest['kind'] == 'current-store-continuous-peer-history', 'Continuous writer provenance differs')
            writers(manifest['actualWriters'], source, [*required, 'Elsa.Etl.Tests.dll'])
        databases = {}
        for path in sorted(folder.rglob('*.db')):
            counts, checks, batches = inspect_database(path)
            databases[path.relative_to(folder).as_posix()] = dict(rowCounts=counts, hashes=checks)
            if family == 'current-continuous':
                verify_receiver(batches, json.loads((folder / 'deliveries.json').read_bytes()))
        reports[family] = dict(sourceSha=source, databases=databases)
    return reports


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--root', type=Path, default=Path(__file__).parent / 'fixtures/etl')
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    require(not args.output.exists(), 'Preserve previous verification; choose a new report')
    result = verify(args.root)
    args.output.write_text(json.dumps(result, indent=2) + '\n', encoding='utf-8')
    print(json.dumps({family: sum(len(database['hashes']) for database in report['databases'].values()) for family, report in result.items()}))
