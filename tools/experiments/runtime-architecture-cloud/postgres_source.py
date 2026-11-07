"""Observe one host-native image and bind owned fixture sources before qualified builds."""
import json
from pathlib import Path
import platform
import re
import shutil

TAG = 'postgres:17.6'
FREEZER = 'tools/experiments/runtime-architecture/freeze_postgres_phase.py'
FIXTURE = ('Essentials/tests/Workflows/Runtime/Persistence/EntityFrameworkCore/ProviderTests/'
           'SnapshotPostgreSqlOwnedFixture.cs')
OLD_DIGEST = 'postgres@sha256:f4610795587492cd4fb887c2dc3de301b813278bb6f616992061141b5f15ac36'


def host_architecture():
    architecture = {'x86_64': 'amd64', 'aarch64': 'arm64', 'arm64': 'arm64'}.get(platform.machine())
    if architecture is None:
        raise ValueError('Unsupported hosted CPU architecture')
    return architecture


def image_identity(image, daemon, architecture):
    if architecture not in ('amd64', 'arm64') or image.get('Architecture') != architecture or image.get('Os') != 'linux':
        raise ValueError('Hosted fixture requires the observed native Linux image')
    if not re.fullmatch(r'sha256:[0-9a-f]{64}', image.get('Id', '')):
        raise ValueError('Observed image config identity is malformed')
    digests = [value for value in image.get('RepoDigests', [])
               if re.fullmatch(r'postgres@sha256:[0-9a-f]{64}', value)]
    if len(digests) != 1 or not isinstance(daemon, str) or not daemon or '\n' in daemon:
        raise ValueError('One exact repository digest and daemon identity are required')
    return {'image': digests[0], 'expectedImageId': image['Id'], 'expectedDaemonId': daemon,
            'architecture': architecture, 'os': 'linux', 'bootstrapReference': TAG}


def adapt(source, identity, snapshot):
    """Exact seams only; preserve the archived original trees and return changed-file hashes."""
    from runner import sha
    changes = []
    paths = [FREEZER] + ([FIXTURE] if snapshot else [])
    for relative in paths:
        path = source / relative
        before = path.read_bytes()
        text = before.decode()
        if relative == FREEZER:
            replacements = {
                "IMAGE = '" + OLD_DIGEST + "'": "IMAGE = '" + identity['image'] + "'",
                "IMAGE_ID = 'sha256:f4610795587492cd4fb887c2dc3de301b813278bb6f616992061141b5f15ac36'":
                    "IMAGE_ID = '" + identity['expectedImageId'] + "'",
                "DAEMON_ID = '63366764-a397-4546-a171-598c97f3e765'":
                    "DAEMON_ID = '" + identity['expectedDaemonId'] + "'"}
        else:
            replacements = {'private const string ExpectedImage = "' + OLD_DIGEST + '";':
                            'private const string ExpectedImage = "' + identity['image'] + '";'}
        for old, new in replacements.items():
            if text.count(old) != 1:
                raise ValueError('Reviewed PostgreSQL image adaptation seam differs: ' + relative)
            text = text.replace(old, new)
        path.write_text(text)
        changes.append({'path': relative, 'beforeSha256': sha(before), 'afterSha256': sha(path.read_bytes())})
    return changes


def prepare(runner):
    from runner import commit, git, write
    if 'postgres' in runner.state:
        raise ValueError('PostgreSQL platform/source preparation is create-only')
    if any(key in __import__('os').environ for key in ('DOCKER_HOST', 'DOCKER_CONTEXT', 'DOCKER_TLS_VERIFY', 'DOCKER_CERT_PATH',
            'ELSA_COMMAND_COST_POSTGRESQL_CONNECTION_STRING', 'ELSA_EFFICIENCY_POSTGRES_CONNECTION',
            'ELSA_RUNTIME_PROTOTYPE_POSTGRES_CONNECTION')):
        raise ValueError('Ambient Docker/database endpoint overrides are forbidden')
    docker = str(Path(shutil.which('docker') or '').resolve())
    if not Path(docker).is_file():
        raise ValueError('Docker is unavailable')
    for variant in ('baseline', 'runview', 'snapshot'):
        runner.refresh_source(variant)
    architecture = host_architecture()
    receipt = runner.phase('baseline', 'postgres-platform', [
        {'name': 'pull-pinned-version-native', 'argv': [docker, '--context', 'default', 'pull', '--platform', 'linux/' + architecture, TAG], 'timeoutSeconds': 240},
        {'name': 'image-identity', 'argv': [docker, '--context', 'default', 'image', 'inspect', '--format',
             '{"Id":{{json .Id}},"RepoDigests":{{json .RepoDigests}},"Architecture":{{json .Architecture}},"Os":{{json .Os}}}', TAG], 'timeoutSeconds': 10},
        {'name': 'daemon-identity', 'argv': [docker, '--context', 'default', 'info', '--format', '{{json .ID}}'], 'timeoutSeconds': 10}])
    output = Path(receipt['phasePath']).parent
    identity = image_identity(json.loads((output / 'image-identity.log').read_bytes()),
                              json.loads((output / 'daemon-identity.log').read_bytes()), architecture)
    for variant in ('baseline', 'runview', 'snapshot'):
        source = runner.refresh_source(variant)
        changes = adapt(source, identity, variant == 'snapshot')
        patch = git(source, 'diff', '--binary') + '\n'
        (runner.evidence / ('postgres-linux-' + variant + '.patch')).write_text(patch)
        commit(source, 'Bind hosted owned PostgreSQL fixture to observed native image and daemon')
        runner.refresh_source(variant)
        runner.state['variants'][variant]['postgresAdaptation'] = changes
    runner.state['postgres'] = dict(identity, platformReceipt=receipt, dockerContext='default', cpus=1, memoryMiB=1024)
    write(runner.evidence / 'postgres-platform.json', runner.state['postgres'])
    runner.save()
