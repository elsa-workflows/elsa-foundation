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
                     'Extensions/Agent/agents/Core/Manifest.cs']:
            with self.subTest(path=path):
                self.assertTrue(self.selected(paths=[path], pull_request={'draft': True})['studio'])

    def test_ready_event_runs_every_proof_and_a_label_or_variable_restores_it_for_a_push(self):
        for paths in [['Studio/src/Feature.ts'], ['e2e-tests/Invoke-PlatformSourceJourney.ps1'],
                      ['tools/contributor-pair-proof/run.mjs'], ['Extensions/Weaver/src/Feature.cs'], ['.github/workflows/ci.yml'], None]:
            with self.subTest(paths=paths):
                selected = self.selected(paths=paths, action='ready_for_review', pull_request={'draft': False})
                labelled = self.selected(paths=paths, action='synchronize',
                                         pull_request={'draft': False, 'labels': [{'name': 'full-tier'}]})
                forced = scope.select('pull_request', {'action': 'synchronize', 'pull_request': {'draft': False}},
                                      paths, self.patterns, every_push=True)
                self.assertEqual(labelled, selected)
                self.assertEqual(forced, selected)
                self.assertTrue(any(selected.values()))

    def test_later_push_to_a_ready_pr_skips_the_heavy_proofs_only(self):
        paths = ['Studio/src/Feature.ts', 'Extensions/Weaver/src/Feature.cs']
        push = self.selected(paths=paths, action='synchronize', pull_request={'draft': False})
        self.assertTrue(push['studio'])
        self.assertFalse(push['studio-heavy'] or push['weaver'])
        self.assertTrue(self.selected(paths=['e2e-tests/Invoke-PlatformSourceJourney.ps1'], action='synchronize',
                                      pull_request={'draft': False})['backend-source'])

    def test_heavy_runs_on_the_ready_transition_the_queue_the_label_and_tier_changes(self):
        ready = {'draft': False}
        for event_name, event in [
                ('pull_request', {'action': 'opened', 'pull_request': ready}),
                ('pull_request', {'action': 'reopened', 'pull_request': ready}),
                ('pull_request', {'action': 'ready_for_review', 'pull_request': ready}),
                ('pull_request', {'action': 'synchronize', 'pull_request': {'draft': False, 'labels': [{'name': 'full-tier'}]}}),
                ('merge_group', {'merge_group': {}}), ('push', {})]:
            with self.subTest(event=event_name, action=event.get('action')):
                self.assertTrue(scope.heavy_now(event_name, event, ['docs/x.md']))
        push = {'action': 'synchronize', 'pull_request': ready}
        self.assertFalse(scope.heavy_now('pull_request', push, ['Essentials/src/Feature.cs']))
        self.assertFalse(scope.heavy_now('pull_request', {'action': 'synchronize', 'pull_request': {'labels': [{'name': 'other'}]}}, []))
        self.assertTrue(scope.heavy_now('pull_request', push, ['tools/ci/test-tiers.json']))
        self.assertTrue(scope.heavy_now('pull_request', push, None))
        self.assertTrue(scope.heavy_now('pull_request', push, ['x'], every_push=True))

    def test_docs_ready_event_does_not_start_unrelated_proofs(self):
        self.assertFalse(any(self.selected(paths=['docs/README.md'], action='ready_for_review').values()))

    def test_push_scopes_studio_and_weaver_like_a_pull_request(self):
        self.assertFalse(any(self.selected('push', ['docs/README.md']).values()))
        self.assertTrue(self.selected('push', ['Essentials/src/Feature.cs'])['weaver'])
        pushed = self.selected('push', ['Studio/src/Feature.ts'])
        self.assertTrue(pushed['studio'] and pushed['studio-heavy'])

    def test_draft_skips_only_the_heavy_studio_proofs(self):
        draft = self.selected(paths=['Studio/src/Feature.ts'], action='synchronize', pull_request={'draft': True})
        ready = self.selected(paths=['Studio/src/Feature.ts'], action='ready_for_review', pull_request={'draft': False})
        self.assertTrue(draft['studio'] and not draft['studio-heavy'])
        self.assertTrue(ready['studio'] and ready['studio-heavy'])
        self.assertFalse(self.selected(paths=['docs/README.md'], action='ready_for_review', pull_request={'draft': False})['studio-heavy'])

    def test_platform_and_published_images_keep_their_triggers(self):
        self.assertTrue(self.selected(paths=['e2e-tests/Invoke-PlatformSourceJourney.ps1'])['backend-source'])
        self.assertTrue(self.selected(paths=['tools/contributor-pair-proof/run.mjs'])['published-images'])
        self.assertFalse(self.selected('push', ['tools/contributor-pair-proof/run.mjs'])['published-images'])

    def test_shared_build_files_and_extension_depth(self):
        self.assertTrue(self.selected(paths=['Directory.Build.props'], action='ready_for_review')['weaver'])
        self.assertTrue(self.selected(paths=['Extensions/Example/studio/Client/src/module.ts'])['studio'])
        self.assertFalse(scope.matches('Extensions/Example/other/studio/a.ts', 'Extensions/*/studio/**'))

    def test_unreadable_diff_selects_all_eligible_jobs(self):
        self.assertTrue(all(self.selected().values()))
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

    def test_merge_group_scopes_like_a_ready_pull_request(self):
        event = {'action': 'checks_requested', 'merge_group': {'base_sha': 'b' * 40, 'head_sha': 'a' * 40}}
        for paths in (['docs/glossary/root.md'], ['Studio/src/Feature.ts'], ['e2e-tests/Invoke-PlatformSourceJourney.ps1'],
                      ['tools/contributor-pair-proof/run.mjs'], ['.github/workflows/ci.yml'], None):
            with self.subTest(paths=paths):
                ready = self.selected('pull_request', paths, action='ready_for_review', pull_request={'draft': False})
                self.assertEqual(ready, self.selected('merge_group', paths, **event))
        self.assertFalse(any(self.selected('merge_group', ['docs/glossary/root.md'], **event).values()))

    def test_merge_group_diffs_its_base_to_its_head(self):
        event = {'merge_group': {'base_sha': 'b' * 40, 'head_sha': 'a' * 40}}
        with patch.object(scope.subprocess, 'run') as run:
            run.return_value.returncode = 0
            run.return_value.stdout = 'docs/x.md\0'
            self.assertEqual(['docs/x.md'], scope.changed_paths('merge_group', event, 'a' * 40))
            self.assertIn(f"{'b' * 40}...{'a' * 40}", run.call_args.args[0])
        self.assertIsNone(scope.changed_paths('merge_group', {}, 'a' * 40))

    def test_manual_suites_are_isolated(self):
        for suite in [*self.patterns, 'studio-order']:
            with self.subTest(suite=suite):
                selected = self.selected('workflow_dispatch', inputs={'suite': suite})
                expected = [suite, 'studio-heavy'] if suite == 'studio' else [suite]
                self.assertEqual(sorted(expected), sorted(name for name, enabled in selected.items() if enabled))
        self.assertFalse(any(self.selected('workflow_dispatch', inputs={'suite': 'ci'}).values()))

    def test_nightly_only_adds_shuffle(self):
        self.assertEqual(['studio-order'], [name for name, enabled in self.selected('schedule').items() if enabled])


if __name__ == '__main__':
    unittest.main()
