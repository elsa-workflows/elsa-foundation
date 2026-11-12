#!/usr/bin/env python3
"""Select focused CI jobs without giving untrusted PR code a write token."""
from __future__ import annotations

import json
import os
from pathlib import Path
import re
import subprocess

ROOT = Path(__file__).resolve().parents[2]
PATHS = ROOT / 'tools/ci/workflow-paths.json'


def matches(path: str, pattern: str) -> bool:
    # GitHub path filters: * stays within a segment, ** crosses directories.
    expression = re.escape(pattern).replace(r'\*\*', '.*').replace(r'\*', '[^/]*').replace(r'\?', '[^/]')
    return re.fullmatch(expression, path) is not None


# A change to any of these re-runs the heavy proofs on the PR that makes it, whatever the push (it changes the tiers).
TIER_FILES = frozenset({
    'tools/ci/test-tiers.json', 'tools/ci/test_tiers.py', 'tools/ci/test_results.py', 'tools/ci/run_shard.py',
    '.github/workflows/ci.yml', 'tools/ci/workflow_scope.py', 'tools/ci/workflow-paths.json'})
FULL_TIER_LABEL = 'full-tier'


def heavy_now(event_name: str, event: dict, changed: list[str] | None, every_push: bool = False) -> bool:
    """Whether this run carries the heavy proofs of a ready pull request: the full tier, the Chromium-driven Studio
    proofs and the Weaver witness. They run when a PR opens ready, reopens or is marked ready, in the merge queue and on
    main, and on a later push to a ready PR only when it has the `full-tier` label, changes the tiers, or
    CI_FULL_TIER_EVERY_PUSH is true. Every other push to a PR runs the PR tier alone."""
    if event_name != 'pull_request':
        return True
    pull_request = event.get('pull_request', {})
    labels = {label.get('name') for label in pull_request.get('labels', [])}
    return (event.get('action') in {'opened', 'reopened', 'ready_for_review'} or every_push
            or FULL_TIER_LABEL in labels or changed is None or any(path in TIER_FILES for path in changed))


def select(event_name: str, event: dict, changed: list[str] | None, patterns: dict,
           every_push: bool = False) -> dict[str, bool]:
    """None means the diff is unavailable: run every potentially affected proof."""
    # studio-heavy is the Chromium-driven part of the studio proofs (browser shards and composed UX acceptance). It follows
    # studio, except that a draft pull request skips it: the ready transition runs every proof on the same head.
    result = dict.fromkeys([*patterns, 'studio-order', 'studio-heavy'], False)
    if event_name == 'workflow_dispatch':
        suite = event.get('inputs', {}).get('suite', 'ci')
        if suite in result:
            result[suite] = True
        result['studio-heavy'] = result['studio']
        return result
    if event_name == 'schedule':
        result['studio-order'] = True
        return result
    if event_name not in {'push', 'pull_request', 'merge_group'}:
        return result
    # A merge-queue entry is validated as a pull request that just received a push: same scope, same proofs.
    pr_like = event_name in {'pull_request', 'merge_group'}
    draft = event.get('pull_request', {}).get('draft', False)
    heavy = heavy_now(event_name, event, changed, every_push)
    for suite, filters in patterns.items():
        affected = changed is None or any(matches(path, pattern) for path in changed for pattern in filters)
        if suite == 'studio':
            result[suite] = affected
        elif suite == 'weaver':
            result[suite] = affected and not draft and heavy
        else:
            result[suite] = pr_like and affected
    result['studio-heavy'] = result['studio'] and not draft and heavy
    result['studio-order'] = pr_like and (
        changed is None or any(path in {'.github/workflows/ci.yml', 'tools/ci/workflow_scope.py',
                                      'tools/ci/workflow-paths.json', 'tools/ci/test_workflow_scope.py',
                                      '.github/actions/setup-studio/action.yml'} for path in changed))
    return result


def changed_paths(event_name: str, event: dict, head: str) -> list[str] | None:
    if event_name not in {'push', 'pull_request', 'merge_group'}:
        return []
    # PR merge commit first parent is the base branch today (main or initiative/*), rather than the stale base.sha. A
    # merge-queue entry names its own parent: base_sha is the commit the entry was built on, and head (GITHUB_SHA) is the
    # entry's merge result.
    if event_name == 'pull_request':
        base = f'{head}^1'
    elif event_name == 'merge_group':
        base = event.get('merge_group', {}).get('base_sha', '')
    else:
        base = event.get('before', '')
    if not base or set(base) == {'0'}:
        return None
    diff_range = f'{base}..{head}' if event_name == 'push' else f'{base}...{head}'
    diff = subprocess.run(['git', 'diff', '--name-only', '--no-renames', '-z', diff_range],
                          cwd=ROOT, capture_output=True, text=True)
    if diff.returncode:
        print('::warning::Cannot read the change scope; selecting every focused proof.')
        return None
    return [path for path in diff.stdout.split('\0') if path]


def main() -> None:
    event = json.loads(Path(os.environ['GITHUB_EVENT_PATH']).read_text())
    event_name = os.environ['GITHUB_EVENT_NAME']
    changed = changed_paths(event_name, event, os.environ['GITHUB_SHA'])
    every_push = os.environ.get('CI_FULL_TIER_EVERY_PUSH') == 'true'
    selected = select(event_name, event, changed, json.loads(PATHS.read_text()), every_push)
    with open(os.environ['GITHUB_OUTPUT'], 'a') as output:
        for name, enabled in selected.items():
            output.write(f'{name}={str(enabled).lower()}\n')
        # Not a proof to select: whether this run carries the ready-PR heavy proofs (read by plan-full).
        output.write(f'heavy={str(heavy_now(event_name, event, changed, every_push)).lower()}\n')
    print(json.dumps(selected, sort_keys=True))


if __name__ == '__main__':
    main()
