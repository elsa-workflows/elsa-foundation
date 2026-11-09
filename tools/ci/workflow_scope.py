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


def select(event_name: str, event: dict, changed: list[str] | None, patterns: dict) -> dict[str, bool]:
    """None means the diff is unavailable: run every potentially affected proof."""
    result = dict.fromkeys([*patterns, 'studio-order'], False)
    if event_name == 'workflow_dispatch':
        suite = event.get('inputs', {}).get('suite', 'ci')
        if suite in result:
            result[suite] = True
        return result
    if event_name == 'schedule':
        result['studio-order'] = True
        return result
    if event_name not in {'push', 'pull_request'}:
        return result
    draft = event.get('pull_request', {}).get('draft', False)
    for suite, filters in patterns.items():
        affected = changed is None or any(matches(path, pattern) for path in changed for pattern in filters)
        if suite == 'studio':
            result[suite] = event_name == 'push' or affected
        elif suite == 'weaver':
            result[suite] = affected and not draft
        else:
            result[suite] = event_name == 'pull_request' and affected
    result['studio-order'] = event_name == 'pull_request' and (
        changed is None or any(path in {'.github/workflows/ci.yml', 'tools/ci/workflow_scope.py',
                                      'tools/ci/workflow-paths.json', 'tools/ci/test_workflow_scope.py',
                                      '.github/actions/setup-studio/action.yml'} for path in changed))
    return result


def changed_paths(event_name: str, event: dict, head: str) -> list[str] | None:
    if event_name not in {'push', 'pull_request'}:
        return []
    # PR merge commit first parent is today's main, rather than the stale base.sha.
    base = f'{head}^1' if event_name == 'pull_request' else event.get('before', '')
    if not base or set(base) == {'0'}:
        return None
    diff_range = f'{base}...{head}' if event_name == 'pull_request' else f'{base}..{head}'
    diff = subprocess.run(['git', 'diff', '--name-only', '--no-renames', '-z', diff_range],
                          cwd=ROOT, capture_output=True, text=True)
    if diff.returncode:
        print('::warning::Cannot read the change scope; selecting every focused proof.')
        return None
    return [path for path in diff.stdout.split('\0') if path]


def main() -> None:
    event = json.loads(Path(os.environ['GITHUB_EVENT_PATH']).read_text())
    event_name = os.environ['GITHUB_EVENT_NAME']
    selected = select(event_name, event, changed_paths(event_name, event, os.environ['GITHUB_SHA']),
                      json.loads(PATHS.read_text()))
    with open(os.environ['GITHUB_OUTPUT'], 'a') as output:
        for name, enabled in selected.items():
            output.write(f'{name}={str(enabled).lower()}\n')
    print(json.dumps(selected, sort_keys=True))


if __name__ == '__main__':
    main()
