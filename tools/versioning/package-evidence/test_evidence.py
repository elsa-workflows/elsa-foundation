"""Offline tests for package read-back, recovery and the clean-reader CLI."""
import json
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
            raise ValueError('no previous signature')
        with self.assertRaisesRegex(ValueError, 'signature'):
            verify.verify_destination(self.file, True, self.service, verify=missing)

    def test_reader_requires_both_predicates_and_exact_producer(self):
        bom = {'metadata': {'component': {'name': 'Example', 'version': '1.2.3-preview',
                                          'hashes': [{'alg': 'SHA-256', 'content': verify.sha256(self.file)}]},
                            'properties': [{'name': 'elsa:commit', 'value': 'a' * 40}]}}
        output = json.dumps([{'verificationResult': {'statement': {'predicate': bom}}}])
        with patch.object(verify.subprocess, 'run') as provenance, patch.object(verify.subprocess, 'check_output', return_value=output) as sbom:
            self.assertEqual(bom, verify.signed_inventory(self.file))
            for command in (provenance.call_args.args[0], sbom.call_args.args[0]):
                self.assertIn(verify.WORKFLOW, command)
                self.assertIn('refs/heads/main', command)
                self.assertIn('a' * 40, command)
            self.assertIn('https://slsa.dev/provenance/v1', provenance.call_args.args[0])
            self.assertIn('https://cyclonedx.org/bom', sbom.call_args.args[0])
            package(self.file, 'changed')
            with self.assertRaisesRegex(ValueError, 'expected package'):
                verify.signed_inventory(self.file)
