"""Offline tests for package read-back, recovery and the clean-reader CLI."""
import json
import os
import subprocess
import sys
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
import zipfile

import verify


def package(path, payload):
    with zipfile.ZipFile(path, 'w') as archive:
        archive.writestr('Example.nuspec', '<package><metadata><id>Example</id><version>1.2.3-preview</version>'
                         '<repository commit="' + 'a' * 40 + '"/></metadata></package>')
        archive.writestr('elsa-input-fingerprint.json', '{"schema_version":1,"fingerprint":"sample"}')
        archive.writestr('lib/net10.0/Example.dll', payload)


class DestinationTests(unittest.TestCase):
    def setUp(self):
        temp = tempfile.TemporaryDirectory()
        self.addCleanup(temp.cleanup)
        directory = Path(temp.name)
        self.file = directory / 'Example.1.2.3-preview.nupkg'
        package(self.file, 'original')
        feed = directory / 'feed'
        self.remote = feed / 'example/1.2.3-preview/example.1.2.3-preview.nupkg'
        self.remote.parent.mkdir(parents=True)
        self.remote.write_bytes(self.file.read_bytes())
        service = directory / 'index.json'
        service.write_text(json.dumps({'resources': [{'@type': 'PackageBaseAddress/3.0.0', '@id': feed.as_uri()}]}))
        self.service = service.as_uri()

    def test_same_bytes_on_first_push_and_retry(self):
        for recovered in (True, False):
            verify.verify_destination(self.file, recovered, self.service, verify=lambda _: self.fail('unexpected lookup'))

    def test_different_bytes_on_successful_push_are_refused(self):
        package(self.remote, 'different')
        with self.assertRaisesRegex(ValueError, 'differ'):
            verify.verify_destination(self.file, False, self.service)

    def test_recovered_prior_bytes_require_their_own_signatures(self):
        package(self.remote, 'prior build')
        with patch.object(verify, 'signed_inventory') as reader:
            verify.verify_destination(self.file, True, self.service, verify=reader)
            reader.assert_called_once()
        def missing(remote):
            self.assertEqual(verify.sha256(remote), verify.sha256(self.remote))
            raise verify.NoAttestations('no previous signature')
        with self.assertRaisesRegex(ValueError, 'manual repair'):
            verify.verify_destination(self.file, True, self.service, verify=missing)

    def test_lookup_timeout_requests_a_retry(self):
        package(self.remote, 'prior build')
        def timeout(_):
            raise verify.subprocess.TimeoutExpired('gh', 55)
        with self.assertRaisesRegex(ValueError, 'timed out; retry'):
            verify.verify_destination(self.file, True, self.service, verify=timeout)

    def test_reader_pairs_the_original_run_and_attempt(self):
        from copy import deepcopy
        def inventory(run, attempt):
            return {'bomFormat': 'CycloneDX', 'specVersion': '1.6',
                    'metadata': {'component': {'name': 'Example', 'version': '1.2.3-preview',
                                                'hashes': [{'alg': 'SHA-256', 'content': verify.sha256(self.file)}]},
                                 'properties': [{'name': 'elsa:commit', 'value': 'a' * 40},
                                                {'name': 'elsa:producer-run', 'value': str(run)},
                                                {'name': 'elsa:producer-attempt', 'value': str(attempt)}]}}
        original, later = inventory(12, 1), inventory(19, 2)
        records = [later, inventory(2, 3), original]
        invocations = [f"https://github.com/{verify.REPO}/actions/runs/{r}/attempts/{a}" for r, a in [(12, 1), (19, 2), (2, 1)]]
        def result(command, **kwargs):
            if command == ['gh', '--version']:
                return 'gh version 2.103.2 (2026-10-23)\n'
            self.assertIn(verify.WORKFLOW, command)
            self.assertIn('refs/heads/main', command)
            self.assertIn('a' * 40, command)
            if 'https://slsa.dev/provenance/v1' in command:
                predicates = [{'runDetails': {'metadata': {'invocationId': invocation}}} for invocation in invocations]
            else:
                self.assertIn('https://cyclonedx.org/bom', command)
                predicates = records
            results = []
            for predicate in predicates:
                if 'runDetails' in predicate:
                    invocation = predicate['runDetails']['metadata']['invocationId']
                else:
                    values = {p['name']: p['value'] for p in predicate['metadata']['properties']}
                    invocation = f"https://github.com/{verify.REPO}/actions/runs/{values['elsa:producer-run']}/attempts/{values['elsa:producer-attempt']}"
                results.append({'verificationResult': {'statement': {'predicate': predicate},
                                                       'signature': {'certificate': {'runInvocationURI': invocation}}}})
            return json.dumps(results)
        with patch.object(verify.subprocess, 'check_output', side_effect=result):
            self.assertEqual(original, verify.signed_inventory(self.file))
            for changed in (inventory(99, 1), deepcopy(original)):
                if changed == original:
                    changed['specVersion'] = '9.9'
                records[:] = [changed]
                with self.assertRaisesRegex(ValueError, 'expected package'):
                    verify.signed_inventory(self.file)
            records[:] = [original]
            package(self.file, 'changed')
            with self.assertRaisesRegex(ValueError, 'expected package'):
                verify.signed_inventory(self.file)

    def test_cli_missing_and_unavailable_responses_remain_distinguishable(self):
        endpoint = f"https://api.github.com/repos/{verify.REPO}/attestations/sha256:{verify.sha256(self.file)}"
        for stderr, expected, message in [
                ('no attestations found', verify.NoAttestations, 'No .* records were found'),
                ('Error: no attestations found', verify.NoAttestations, 'No .* records were found'),
                (f'Error: HTTP 404: Not Found ({endpoint}?per_page=30)', ValueError, 'Confirm repository attestation read access'),
                ('Error: HTTP 503', ValueError, 'GitHub could not verify')]:
            error = verify.subprocess.CalledProcessError(1, 'gh', stderr=stderr.encode())
            with self.subTest(stderr=stderr), patch.object(verify.subprocess, 'check_output',
                                                          side_effect=['gh version 2.104.0', error]):
                with self.assertRaisesRegex(expected, message):
                    verify.signed_inventory(self.file)

    def test_reader_requires_exact_workflow_matching_support(self):
        with patch.object(verify.subprocess, 'check_output', return_value='gh version 2.101.0 (2026-09-01)') as gh:
            with self.assertRaisesRegex(ValueError, '2.102.0'):
                verify.signed_inventory(self.file)
            gh.assert_called_once_with(['gh', '--version'], text=True, timeout=15)

    def test_output_aliases_preserve_the_package(self):
        hard_link = self.file.with_name("linked-package")
        os.link(self.file, hard_link)
        original = self.file.read_bytes()
        for output in (self.file, hard_link):
            with self.subTest(output=output):
                result = subprocess.run([sys.executable, verify.__file__, str(self.file), "--output", str(output)],
                                        capture_output=True, text=True)
                self.assertNotEqual(0, result.returncode)
                self.assertIn("Inventory output refers to the input package", result.stderr)
                self.assertEqual(original, self.file.read_bytes())

    def test_sealed_subjects_require_the_exact_set_and_bytes(self):
        plan = self.file.parent
        (plan / 'nuget').mkdir()
        (plan / 'sboms').mkdir()
        packed = plan / 'nuget' / self.file.name
        packed.write_bytes(self.file.read_bytes())
        manifest = plan / 'sboms' / 'subjects.json'
        manifest.write_text(json.dumps([{'name': packed.name, 'sha256': verify.sha256(packed)}]))
        seal = verify.sha256(manifest)
        verify.verify_subjects(plan, seal)
        with self.assertRaisesRegex(ValueError, 'manifest digest'):
            verify.verify_subjects(plan, '0' * 64)
        packed.write_bytes(b'swapped')
        with self.assertRaisesRegex(ValueError, 'signed subject'):
            verify.verify_subjects(plan, seal)
        packed.unlink()
        with self.assertRaisesRegex(ValueError, 'subject set'):
            verify.verify_subjects(plan, seal)
