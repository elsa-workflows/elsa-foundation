"""Narrow, hash-recorded Linux adaptation of the immutable macOS proof sources."""
import ast
import hashlib
from pathlib import Path


COMPAT_FIELDS = "('pid', 'uid', 'startSec', 'startUsec', 'pgid', 'sid')"
LINUX_FIELDS = "('pid', 'uid', 'startSec', 'startUsec', 'pgid', 'sid', 'bootId', 'startTicks')"
ROOT_PROJECTION = "'root': {key: crash['root'][key] for key in "
CHECK_ANCHOR = "    require(kill.get('signalSent') is True and kill.get('killedExitCode') == -9 and\n"
CHECK_CALL = "    verify_linux_kill_identity(kill, manifest, worker_command)\n"
HELPER_ANCHOR = '\ndef read_calls(path, config, run_id):\n'
CHECKER_HELPER = '''
def verify_linux_kill_identity(kill, manifest, worker_command):
    """Bind authoritative Linux birth to the persisted gated command and exact signal."""
    import uuid
    root = kill['root']
    fields = ('pid', 'uid', 'startSec', 'startUsec', 'pgid', 'sid', 'bootId', 'startTicks')
    require(all(key in root for key in fields), 'Missing authoritative Linux kill identity')
    for key in ('pid', 'uid', 'startSec', 'startUsec', 'pgid', 'sid', 'startTicks'):
        require(type(root[key]) is int and root[key] >= (0 if key in ('uid', 'startUsec') else 1),
                'Invalid Linux kill identity field: ' + key)
    require(root['startUsec'] < 1000000 and isinstance(root['bootId'], str) and
            str(uuid.UUID(root['bootId'])) == root['bootId'], 'Invalid Linux boot scope or timestamp')
    output = Path(manifest['output'])
    require(output.is_absolute() and output.resolve() == output, 'Noncanonical Linux ownership output')
    ledger_path = output / 'resources.json'
    require(ledger_path.resolve() == ledger_path, 'Linux ownership ledger path changed')
    ledger = json.loads(ledger_path.read_bytes())
    require(ledger['nonce'] == manifest['nonce'] and ledger['bootId'] == root['bootId'] and
            ledger.get('identityKind') == 'linux-proc-boot-id-start-ticks', 'Linux ownership scope differs')
    commands = [row for row in ledger['commands'] if row['argv'] == worker_command['argv']]
    require(len(commands) == 1 and commands[0]['exit'] == -9 and
            all(commands[0]['root'].get(key) == root[key] for key in fields),
            'Linux kill root differs from its persisted gated command')
    birth = f"{root['bootId']}:{root['pid']}:{root['uid']}:{root['startTicks']}"
    owned = ledger['owned'].get(birth, {})
    require(owned.get('attribution') == 'gated-direct-child' and
            all(owned.get(key) == root[key] for key in fields), 'Linux kill root lacks gated ownership')
    require(any(row.get('birth') == birth and row.get('pid') == root['pid'] and row.get('signal') == 9 and
                row.get('mechanism') in ('pidfd', 'identity-rechecked-pid') for row in ledger['actions']),
            'Linux kill lacks a successful exact-birth signal action')

'''


def digest(content):
    return hashlib.sha256(content).hexdigest()


def adapt_linux_crash_identity(source):
    """Patch only materialized trees; caller commits and records returned per-file hashes.

    The signal_exact adaptation is owned by the runner and is left untouched.
    Both original seams must match before either file is written. Exact repeat
    calls are idempotent; partial or unknown adaptations fail closed.
    """
    source = Path(source).resolve()
    scripts = source / 'tools/experiments/runtime-architecture'
    paths = [scripts / 'launch_phase.py', scripts / 'check_crash_receipts.py']
    original = [path.read_bytes() for path in paths]
    texts = [content.decode('utf-8') for content in original]
    old_root, new_root = ROOT_PROJECTION + COMPAT_FIELDS, ROOT_PROJECTION + LINUX_FIELDS
    launcher, checker = texts
    applied = (launcher.count(new_root) == 1, checker.count(CHECK_CALL) == 1 and checker.count(CHECKER_HELPER) == 1)
    if any(applied):
        if not all(applied) or old_root in launcher or checker.count(CHECK_ANCHOR) != 1:
            raise ValueError('Partial or unknown Linux crash identity adaptation')
        patched = texts
    else:
        if (launcher.count(old_root) != 1 or launcher.count(new_root) != 0 or
                checker.count(CHECK_ANCHOR) != 1 or checker.count(HELPER_ANCHOR) != 1 or
                'def verify_linux_kill_identity(' in checker):
            raise ValueError('Reviewed crash identity source seams differ')
        patched = [launcher.replace(old_root, new_root),
                   checker.replace(HELPER_ANCHOR, '\n' + CHECKER_HELPER + HELPER_ANCHOR)
                          .replace(CHECK_ANCHOR, CHECK_CALL + CHECK_ANCHOR)]
    for path, content in zip(paths, patched):
        ast.parse(content, filename=str(path))
    records = []
    for path, before, after in zip(paths, original, patched):
        encoded = after.encode('utf-8')
        records.append({'path': str(path.relative_to(source)), 'beforeSha256': digest(before),
                        'afterSha256': digest(encoded)})
        if before != encoded:
            path.write_bytes(encoded)
    return {'schema': 'runtime-prototype-linux-crash-identity-v1', 'alreadyApplied': all(applied), 'files': records}
