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
        selected = self.selected(paths=['studio/src/Feature.ts'], pull_request={'draft': True})
        self.assertTrue(selected['studio'])
        self.assertFalse(selected['weaver'])

    def test_composed_acceptance_inputs_and_deletions_select_studio(self):
        for path in ['tools/ux-acceptance/run.py', 'tools/ci/studio_gate.py',
                     'studio/tests/acceptance/journeys.spec.ts',
                     'server/apps/Elsa.Workbench.Host/shells.json',
                     'extensions/Agent/agents/Core/Manifest.cs']:
            with self.subTest(path=path):
                self.assertTrue(self.selected(paths=[path], pull_request={'draft': True})['studio'])

    def test_ready_event_runs_every_proof_and_a_label_or_variable_restores_it_for_a_push(self):
        for paths in [['studio/src/Feature.ts'], ['e2e-tests/Invoke-PlatformSourceJourney.ps1'],
                      ['tools/contributor-pair-proof/run.mjs'], ['extensions/Weaver/src/Feature.cs'], ['.github/workflows/ci.yml'], None]:
            with self.subTest(paths=paths):
                selected = self.selected(paths=paths, action='ready_for_review', pull_request={'draft': False})
                labelled = self.selected(paths=paths, action='synchronize',
                                         pull_request={'draft': False, 'labels': [{'name': 'full-tier'}]})
                forced = scope.select('pull_request', {'action': 'synchronize', 'pull_request': {'draft': False}},
                                      paths, self.patterns, every_push=True)
                self.assertEqual(labelled, dict(selected, **{'studio-ux': selected['studio']}))
                self.assertEqual(forced, dict(selected, **{'studio-ux': selected['studio']}))
                self.assertTrue(any(selected.values()))

    def test_later_push_to_a_ready_pr_skips_the_heavy_proofs_only(self):
        paths = ['studio/src/Feature.ts', 'extensions/Weaver/src/Feature.cs']
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
        self.assertFalse(scope.heavy_now('pull_request', push, ['server/essentials/src/Feature.cs']))
        self.assertFalse(scope.heavy_now('pull_request', {'action': 'synchronize', 'pull_request': {'labels': [{'name': 'other'}]}}, []))
        self.assertTrue(scope.heavy_now('pull_request', push, ['tools/ci/test-tiers.json']))
        self.assertTrue(scope.heavy_now('pull_request', push, None))
        self.assertTrue(scope.heavy_now('pull_request', push, ['x'], every_push=True))

    def test_docs_ready_event_does_not_start_unrelated_proofs(self):
        self.assertFalse(any(self.selected(paths=['docs/README.md'], action='ready_for_review').values()))

    def test_release_evidence_uses_the_full_changed_set_on_push_and_merge_queue(self):
        for event in ('push', 'pull_request', 'merge_group'):
            self.assertTrue(self.selected(event, ['tools/versioning/package-evidence/generate.mjs',
                                                  'docs/README.md'])['package-evidence'])
            self.assertFalse(self.selected(event, ['docs/README.md'])['package-evidence'])
            self.assertTrue(self.selected(event, None)['package-evidence'])

    def test_push_scopes_studio_and_weaver_like_a_pull_request(self):
        self.assertFalse(any(self.selected('push', ['docs/README.md']).values()))
        self.assertTrue(self.selected('push', ['server/essentials/src/Feature.cs'])['weaver'])
        pushed = self.selected('push', ['studio/src/Feature.ts'])
        self.assertTrue(pushed['studio'] and pushed['studio-heavy'])

    def test_weaver_skips_foundation_test_sources_but_keeps_product_build_and_project_inputs(self):
        for event_name, event in [('push', {}), ('merge_group', {'merge_group': {}}),
                                  ('pull_request', {'action': 'ready_for_review', 'pull_request': {'draft': False}})]:
            for path in ['server/essentials/tests/Cluster/Readability/Tests/EfSchemaReadabilitySourceTests.cs',
                         'server/essentials/tests/Cluster/Readability/Tests/README.md']:
                with self.subTest(event=event_name, path=path):
                    self.assertFalse(self.selected(event_name, [path], **event)['weaver'])
            for path in ['server/essentials/src/Workflows/Runtime/Core/Runtime.cs',
                         'server/essentials/build/Analyzers/Analyzer.cs',
                         'server/essentials/Directory.Build.props', 'server/essentials/Directory.Build.targets',
                         'server/essentials/Directory.Packages.props', 'server/essentials/Example.csproj',
                         'server/essentials/tests/Cluster/Readability/Tests/Elsa.Cluster.Readability.Tests.csproj',
                         'studio/src/Feature.ts', 'extensions/Weaver/src/Feature.cs',
                         'extensions/Dashboard/src/Core/Dataset.cs',
                         'extensions/Dashboard/tests/QueryTests/Elsa.Studio.Dashboard.QueryTests.csproj',
                         'extensions/Git/src/Elsa.Git/GitClient.cs',
                         'extensions/Http/src/Activities/ActivitiesHttpFeature.cs',
                         'extensions/Http/tests/Tests/Elsa.Http.Tests.csproj',
                         'extensions/Secrets/src/Elsa.Secrets/Features/SecretsFeature.cs',
                         'extensions/Secrets/tests/Tests/Elsa.Secrets.Tests.csproj',
                         'extensions/Directory.Build.props',
                         'extensions/Diagnostics.OpenTelemetry/studio/Elsa.Studio.Diagnostics.OpenTelemetry/Client/src/module.ts',
                         'Directory.Build.props', 'tools/studio/Weaver.Conformance.Probe/browser.mjs']:
                with self.subTest(event=event_name, path=path):
                    self.assertTrue(self.selected(event_name, [path], **event)['weaver'])

    def test_draft_skips_only_the_heavy_studio_proofs(self):
        draft = self.selected(paths=['studio/src/Feature.ts'], action='synchronize', pull_request={'draft': True})
        ready = self.selected(paths=['studio/src/Feature.ts'], action='ready_for_review', pull_request={'draft': False})
        self.assertTrue(draft['studio'] and not draft['studio-heavy'])
        self.assertTrue(ready['studio'] and ready['studio-heavy'])
        self.assertFalse(self.selected(paths=['docs/README.md'], action='ready_for_review', pull_request={'draft': False})['studio-heavy'])

    def test_platform_and_published_images_keep_their_triggers(self):
        self.assertTrue(self.selected(paths=['e2e-tests/Invoke-PlatformSourceJourney.ps1'])['backend-source'])
        self.assertTrue(self.selected(paths=['tools/contributor-pair-proof/run.mjs'])['published-images'])
        self.assertFalse(self.selected('push', ['tools/contributor-pair-proof/run.mjs'])['published-images'])

    def test_shared_build_files_and_extension_depth(self):
        self.assertTrue(self.selected(paths=['Directory.Build.props'], action='ready_for_review')['weaver'])
        self.assertTrue(self.selected(paths=['extensions/Example/studio/Client/src/module.ts'])['studio'])
        self.assertFalse(scope.matches('extensions/Example/other/studio/a.ts', 'extensions/*/studio/**'))

    def test_unreadable_diff_selects_all_eligible_jobs(self):
        self.assertTrue(all(enabled for name, enabled in self.selected().items() if name != 'studio-ux'))
        self.assertFalse(self.selected()['studio-ux'])
        with patch.object(scope.subprocess, 'run') as run:
            run.return_value.returncode = 128
            self.assertIsNone(scope.changed_paths('pull_request', {}, 'a' * 40))
        self.assertIsNone(scope.changed_paths('push', {'before': '0' * 40}, 'a' * 40))

    def test_deletion_is_not_hidden_by_rename_detection(self):
        with patch.object(scope.subprocess, 'run') as run:
            run.return_value.returncode = 0
            run.return_value.stdout = 'studio/deleted.ts\0'
            self.assertEqual(['studio/deleted.ts'], scope.changed_paths('pull_request', {}, 'a' * 40))
            self.assertIn('--no-renames', run.call_args.args[0])
            self.assertIn('-z', run.call_args.args[0])

    def test_merge_group_scopes_like_a_ready_pull_request(self):
        event = {'action': 'checks_requested', 'merge_group': {'base_sha': 'b' * 40, 'head_sha': 'a' * 40}}
        for paths in (['docs/glossary/root.md'], ['studio/src/Feature.ts'], ['e2e-tests/Invoke-PlatformSourceJourney.ps1'],
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

    PUSH_ENV = {'GITHUB_REPOSITORY': 'o/r', 'GITHUB_REF_NAME': 'main', 'GITHUB_RUN_ID': '9',
                'GITHUB_WORKFLOW_REF': 'o/r/.github/workflows/ci.yml@refs/heads/main'}

    def history(self, runs, plan_scope=None, ancestors=None, head='h' * 40, pages=None):
        """Runs newest first, as the API lists them; plan_scope maps a run id to its plan-scope conclusion."""
        calls = []

        def fetch(path):
            calls.append(path)
            if '/jobs?' in path:
                run_id = int(path.split('/runs/')[1].split('/')[0])
                return {'jobs': [{'name': 'Plan tiers', 'conclusion': 'success'},
                                 {'name': scope.PLAN_SCOPE_JOB, 'conclusion': (plan_scope or {}).get(run_id, 'success')}]}
            if pages is not None:
                return {'workflow_runs': pages[int(path.rsplit('page=', 1)[1]) - 1]}
            return {'workflow_runs': runs}

        with patch.dict(scope.os.environ, self.PUSH_ENV):
            base = scope.last_validated_push(head, fetch, lambda commit, _: commit in (ancestors or {r['head_sha'] for r in runs}))
        return base, calls

    @staticmethod
    def ci_run(run_id, sha, conclusion):
        return {'id': run_id, 'head_sha': sha * 40, 'conclusion': conclusion}

    def test_push_diffs_from_the_last_run_that_validated_skipping_cancelled_runs(self):
        # #4686: its merge commit's run was replaced while pending, so the next push's `before` hid its Studio change.
        base, calls = self.history([self.ci_run(8, 'c', 'cancelled'), self.ci_run(7, 'b', 'cancelled'),
                                    self.ci_run(6, 'a', 'success')])
        self.assertEqual('a' * 40, base)
        self.assertIn('/repos/o/r/actions/workflows/ci.yml/runs?event=push&branch=main&status=completed', calls[0])
        self.assertEqual(['/repos/o/r/actions/runs/6/jobs?filter=latest&per_page=100'], [c for c in calls if '/jobs?' in c])
        with patch.object(scope.subprocess, 'run') as run:
            run.return_value.returncode = 0
            run.return_value.stdout = 'studio/src/Feature.ts\0docs/x.md\0'
            changed = scope.changed_paths('push', {'before': 'c' * 40}, 'h' * 40, base)
            self.assertIn(f"{'a' * 40}..{'h' * 40}", run.call_args.args[0])
        self.assertTrue(self.selected('push', changed)['studio-heavy'])

    def test_a_failed_run_counts_as_validated(self):
        self.assertEqual('a' * 40, self.history([self.ci_run(8, 'a', 'failure'), self.ci_run(7, 'b', 'success')])[0])

    def test_runs_that_validated_nothing_are_skipped(self):
        runs = [self.ci_run(9, 'z', 'success'),           # this run itself (a re-run attempt)
                self.ci_run(8, 'y', 'success'),           # not an ancestor of this head
                self.ci_run(7, 'x', 'skipped'),
                self.ci_run(6, 'w', 'failure'),           # plan-scope itself failed: nothing was selected
                self.ci_run(5, 'v', 'timed_out'),
                self.ci_run(4, 'u', 'success')]
        base, _ = self.history(runs, plan_scope={6: 'failure'}, ancestors={'z' * 40, 'x' * 40, 'w' * 40, 'v' * 40, 'u' * 40})
        self.assertEqual('u' * 40, base)

    def test_the_search_reads_later_pages_and_stops_at_the_last(self):
        full = [self.ci_run(100 + i, 'c', 'cancelled') for i in range(100)]
        base, calls = self.history([], pages=[full, [self.ci_run(5, 'a', 'success')]], ancestors={'c' * 40, 'a' * 40})
        self.assertEqual('a' * 40, base)
        self.assertEqual(['page=1', 'page=2'], [c.rsplit('&', 1)[1] for c in calls if '/workflows/' in c])
        base, calls = self.history([], pages=[[self.ci_run(8, 'c', 'cancelled')]], ancestors={'c' * 40})
        self.assertIsNone(base)
        self.assertEqual(1, len([c for c in calls if '/workflows/' in c]))

    def test_unreadable_or_missing_history_selects_every_proof(self):
        def broken(_):
            raise OSError('HTTP 503')
        with patch.dict(scope.os.environ, self.PUSH_ENV):
            self.assertIsNone(scope.last_validated_push('h' * 40, broken, lambda *_: True))
        self.assertIsNone(self.history([self.ci_run(8, 'c', 'cancelled')])[0])
        self.assertIsNone(scope.changed_paths('push', {'before': 'c' * 40}, 'h' * 40, None))
        self.assertTrue(self.selected('push', None)['studio-heavy'])

    def test_the_event_before_is_never_the_push_base(self):
        with patch.object(scope.subprocess, 'run') as run:
            run.return_value.returncode = 0
            run.return_value.stdout = ''
            scope.changed_paths('push', {'before': 'b' * 40}, 'h' * 40, 'a' * 40)
            self.assertIn(f"{'a' * 40}..{'h' * 40}", run.call_args.args[0])

    def test_main_reads_the_history_for_a_push_only(self):
        import tempfile
        for event_name, calls in (('push', 1), ('pull_request', 0), ('merge_group', 0), ('schedule', 0)):
            with self.subTest(event=event_name), tempfile.TemporaryDirectory() as tmp:
                event_path, output_path = Path(tmp, 'event.json'), Path(tmp, 'output')
                event_path.write_text(json.dumps({'before': 'b' * 40}))
                env = {'GITHUB_EVENT_PATH': str(event_path), 'GITHUB_EVENT_NAME': event_name,
                       'GITHUB_SHA': 'h' * 40, 'GITHUB_OUTPUT': str(output_path)}
                with patch.dict(scope.os.environ, env), \
                        patch.object(scope, 'last_validated_push', return_value=None) as history, \
                        patch.object(scope.subprocess, 'run') as run:
                    run.return_value.returncode = 0
                    run.return_value.stdout = ''
                    scope.main()
                self.assertEqual(calls, history.call_count)
                if event_name == 'push':
                    self.assertIn('studio-heavy=true', output_path.read_text())

    def test_plan_scope_can_read_runs_and_only_a_push_gets_the_token(self):
        workflow = (scope.ROOT / '.github/workflows/ci.yml').read_text()
        job = workflow.split('\n  plan-scope:\n', 1)[1].split('\n  plan-tiers:\n', 1)[0]
        self.assertIn('    permissions:\n      contents: read\n      actions: read\n', job)
        self.assertIn("GH_TOKEN: ${{ github.event_name == 'push' && github.token || '' }}", job)
        self.assertEqual(1, job.count('GH_TOKEN'))

    def test_manual_suites_are_isolated(self):
        for suite in [*self.patterns, 'studio-order']:
            with self.subTest(suite=suite):
                selected = self.selected('workflow_dispatch', inputs={'suite': suite})
                expected = [suite, 'studio-heavy', 'studio-ux'] if suite == 'studio' else [suite]
                self.assertEqual(sorted(expected), sorted(name for name, enabled in selected.items() if enabled))
        self.assertFalse(any(self.selected('workflow_dispatch', inputs={'suite': 'ci'}).values()))

    def test_nightly_adds_shuffle_and_composed_ux(self):
        self.assertEqual(['studio-order', 'studio-ux'], [name for name, enabled in self.selected('schedule').items() if enabled])

    def test_composed_ux_requires_explicit_opt_in_outside_nightly(self):
        for event in ('push', 'pull_request', 'merge_group'):
            with self.subTest(event=event):
                selected = self.selected(event, ['studio/src/Feature.ts'], action='ready_for_review',
                                         pull_request={'draft': False})
                self.assertTrue(selected['studio'] and selected['studio-heavy'])
                self.assertFalse(selected['studio-ux'])
                forced = scope.select(event, {'action': 'ready_for_review', 'pull_request': {'draft': False}},
                                      ['studio/src/Feature.ts'], self.patterns, every_push=True)
                self.assertEqual(event == 'pull_request', forced['studio-ux'])
        opted_in = self.selected(paths=['studio/src/Feature.ts'], action='synchronize',
                                 pull_request={'draft': False, 'labels': [{'name': 'full-tier'}]})
        self.assertTrue(opted_in['studio-ux'])
        draft = self.selected(paths=['studio/src/Feature.ts'],
                              pull_request={'draft': True, 'labels': [{'name': 'full-tier'}]})
        self.assertFalse(draft['studio-ux'])


if __name__ == '__main__':
    unittest.main()
