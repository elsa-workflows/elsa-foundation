"""Behavioral routing coverage for the consolidated validation entry point."""
import json
from pathlib import Path
import unittest
from unittest.mock import patch

import workflow_scope as scope


class WorkflowScopeTests(unittest.TestCase):
    patterns = json.loads(scope.PATHS.read_text())

    def selected(self, event='pull_request', paths=None, **payload):
        return scope.select(event, payload, paths, self.patterns)

    def test_docs_do_not_start_unrelated_proofs(self):
        self.assertFalse(any(self.selected(paths=['docs/glossary/root.md']).values()))

    def test_studio_draft_runs_studio_without_weaver(self):
        selected = self.selected(paths=['Studio/src/Feature.ts'], pull_request={'draft': True})
        self.assertTrue(selected['studio'])
        self.assertFalse(selected['weaver'])

    def test_composed_acceptance_inputs_and_deletions_select_studio(self):
        for path in ['tools/ux-acceptance/run.py', 'tools/ci/studio_gate.py',
                     'Studio/tests/acceptance/journeys.spec.ts',
                     'Essentials/apps/Elsa.Workbench/shells.json',
                     'Extensions/Agent/specialists/Core/Manifest.cs']:
            with self.subTest(path=path):
                self.assertTrue(self.selected(paths=[path], pull_request={'draft': True})['studio'])

    def test_ready_event_runs_the_same_proofs_as_a_ready_push(self):
        for paths in [['Studio/src/Feature.ts'], ['e2e-tests/Invoke-PlatformSourceJourney.ps1'],
                      ['tools/contributor-pair-proof/run.mjs'], ['.github/workflows/ci.yml'], None]:
            with self.subTest(paths=paths):
                selected = self.selected(paths=paths, action='ready_for_review', pull_request={'draft': False})
                pushed = self.selected(paths=paths, action='synchronize', pull_request={'draft': False})
                self.assertEqual(pushed, selected)
                self.assertTrue(any(selected.values()))

    def test_docs_ready_event_does_not_start_unrelated_proofs(self):
        self.assertFalse(any(self.selected(paths=['docs/README.md'], action='ready_for_review').values()))

    def test_push_always_validates_studio_but_scopes_weaver(self):
        self.assertEqual(['studio'], [name for name, enabled in self.selected('push', ['docs/README.md']).items() if enabled])
        self.assertTrue(self.selected('push', ['Essentials/src/Feature.cs'])['weaver'])

    def test_platform_and_published_images_keep_their_triggers(self):
        self.assertTrue(self.selected(paths=['e2e-tests/Invoke-PlatformSourceJourney.ps1'])['backend-source'])
        self.assertTrue(self.selected(paths=['tools/contributor-pair-proof/run.mjs'])['published-images'])
        self.assertFalse(self.selected('push', ['tools/contributor-pair-proof/run.mjs'])['published-images'])

    def test_shared_build_files_and_extension_depth(self):
        self.assertTrue(self.selected(paths=['Directory.Build.props'])['weaver'])
        self.assertTrue(self.selected(paths=['Extensions/Example/studio/Client/src/module.ts'])['studio'])
        self.assertFalse(scope.matches('Extensions/Example/other/studio/a.ts', 'Extensions/*/studio/**'))

    def test_unreadable_diff_selects_all_eligible_jobs(self):
        selected = self.selected()
        self.assertTrue(all(enabled for name, enabled in selected.items() if name != 'bpmn-document'))
        self.assertFalse(selected['bpmn-document'])
        with patch.object(scope.subprocess, 'run') as run:
            run.return_value.returncode = 128
            self.assertIsNone(scope.changed_paths('pull_request', {}, 'a' * 40))
        self.assertIsNone(scope.changed_paths('push', {'before': '0' * 40}, 'a' * 40))

    def test_deletion_is_not_hidden_by_rename_detection(self):
        with patch.object(scope.subprocess, 'run') as run:
            run.return_value.returncode = 0
            run.return_value.stdout = 'Studio/deleted.ts\0'
            self.assertEqual(['Studio/deleted.ts'], scope.changed_paths('pull_request', {}, 'a' * 40))
            self.assertIn('--no-renames', run.call_args.args[0])
            self.assertIn('-z', run.call_args.args[0])

    def test_manual_suites_are_isolated(self):
        for suite in [*self.patterns, 'studio-order', 'bpmn-document']:
            with self.subTest(suite=suite):
                selected = self.selected('workflow_dispatch', inputs={'suite': suite})
                self.assertEqual([suite], [name for name, enabled in selected.items() if enabled])
        self.assertFalse(any(self.selected('workflow_dispatch', inputs={'suite': 'ci'}).values()))

    def test_bpmn_document_is_never_selected_automatically(self):
        for event in ['pull_request', 'push', 'schedule']:
            for paths in [None, ['.github/workflows/ci.yml'], ['Studio/src/Feature.ts'],
                          ['tools/ux-acceptance/run-bpmn-document-editors.py']]:
                with self.subTest(event=event, paths=paths):
                    self.assertFalse(self.selected(event, paths)['bpmn-document'])

    def test_nightly_only_adds_shuffle(self):
        self.assertEqual(['studio-order'], [name for name, enabled in self.selected('schedule').items() if enabled])


if __name__ == '__main__':
    unittest.main()
