"""Small synthetic source/binary retention checks; never execute an engine or container."""
import hashlib
import json
from pathlib import Path
import subprocess
import tempfile
import unittest
from unittest.mock import patch

import evidence_exports as exports


class EvidenceExportControls(unittest.TestCase):
    def test_exact_source_bundle_patch_and_committed_lock_survive_worktree_removal(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary).resolve()
            source = root / 'source'
            source.mkdir()
            def git(*args):
                return subprocess.check_output(['git', '-C', str(source), *args], stderr=subprocess.DEVNULL).decode().strip()
            git('init', '-q')
            (source / 'base.txt').write_text('production base\n')
            git('add', '.')
            git('-c', 'user.name=Synthetic', '-c', 'user.email=synthetic@example.invalid', 'commit', '-qm', 'base')
            base = git('rev-parse', 'HEAD')
            lock = b'{"version":1,"dependencies":{}}\n'
            (source / 'packages.lock.json').write_bytes(lock)
            (source / 'bytes.bin').write_bytes(bytes(range(256)))
            git('add', '.')
            git('-c', 'user.name=Synthetic', '-c', 'user.email=synthetic@example.invalid', 'commit', '-qm', 'owned adaptation')
            item = {'worktree': str(source), 'commit': git('rev-parse', 'HEAD'), 'tree': git('rev-parse', 'HEAD^{tree}')}
            with patch.object(exports, 'BASE', base):
                result = exports.source(item, root / 'evidence', 'baseline')
            path = Path(result['manifestPath'])
            record = json.loads(path.read_bytes())
            self.assertEqual(item['commit'], record['sourceCommit'])
            self.assertEqual(item['tree'], record['sourceTree'])
            self.assertEqual(lock, (path.parent / record['committedLocks'][0]['exportPath']).read_bytes())
            self.assertIn(b'GIT binary patch', (path.parent / 'source.patch').read_bytes())
            git('bundle', 'verify', str(path.parent / 'source.bundle'))
            listing = git('bundle', 'list-heads', str(path.parent / 'source.bundle'))
            self.assertIn(item['commit'], listing)
            self.assertEqual(hashlib.sha256(path.read_bytes()).hexdigest(), result['manifestSha256'])

    def test_full_binary_bytes_are_retained_and_changed_input_rejected(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary).resolve()
            binary = root / 'bin'
            binary.mkdir()
            content = b'\x00synthetic-dll\xff'
            (binary / 'probe.dll').write_bytes(content)
            manifest = root / 'manifest.json'
            manifest.write_text(json.dumps({'head': 'source-head', 'tree': 'source-tree'}))
            closure = {'path': str(binary), 'inventory': ['probe.dll'], 'files': [{'path': str(binary / 'probe.dll'),
                'relativePath': 'probe.dll', 'bytes': len(content), 'sha256': exports.sha(content)}]}
            phase = root / 'phase.json'
            phase.write_text(json.dumps({'status': 'COMMANDS_PASSED', 'processQuiescent': True,
                'manifestSha256': exports.sha(manifest.read_bytes()), 'commands': [{'name': 'build', 'artifactDirectories': [closure]}]}))
            receipt = {'phasePath': str(phase), 'manifestPath': str(manifest)}
            result = exports.build(receipt, root / 'evidence', 'baseline', 'probe')
            path = Path(result['manifestPath'])
            self.assertEqual(content, (path.parent / 'build-0/probe.dll').read_bytes())
            (binary / 'probe.dll').write_bytes(b'changed')
            # An already-retained independent copy remains usable; a new export must reject changed inputs.
            self.assertEqual(content, (path.parent / 'build-0/probe.dll').read_bytes())
            with self.assertRaises(ValueError):
                exports.build(receipt, root / 'evidence', 'baseline', 'different-export')

    def test_incomplete_phase_cannot_export_qualified_binaries(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary).resolve()
            manifest, phase = root / 'manifest.json', root / 'phase.json'
            manifest.write_text('{}')
            phase.write_text(json.dumps({'status': 'INCOMPLETE', 'processQuiescent': True,
                                        'manifestSha256': exports.sha(manifest.read_bytes())}))
            with self.assertRaises(ValueError):
                exports.build({'phasePath': str(phase), 'manifestPath': str(manifest)}, root / 'evidence', 'baseline', 'failed')


if __name__ == '__main__':
    unittest.main()
