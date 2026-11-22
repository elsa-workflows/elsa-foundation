"""Assemble immutable sanitized runs with an explicit canonical 54-case selection."""
from pathlib import Path
import argparse, hashlib, json, shutil, struct, subprocess, re

TRANSPORT = 'recorded activity playback transport'
CODEC = '29611a1c2029a831e913a1249b93a76e5cd0303950afa3224147190a704b7752'
PROJECTS = {'desktop-light', 'desktop-dark', 'narrow-light', 'narrow-dark', 'narrow-390-light', 'narrow-390-dark'}
def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()
def read(path):
    return json.loads(path.read_text())
def specs(suites):
    for suite in suites:
        yield from suite.get('specs', [])
        yield from specs(suite.get('suites', []))

def run(raw, shared, count):
    report, browser, observer = (read(raw / name) for name in ('report.json', 'playwright.json', 'store-observation.json'))
    assert report['status'] == 'passed' and report['stage'] == 'complete' and not report['dirty']
    assert browser['stats']['expected'] == count and all(browser['stats'][key] == 0 for key in ('skipped', 'unexpected', 'flaky'))
    assert all(observer[key] for key in ('accepted', 'runnerReturned', 'sourceFence', 'driverFence', 'cleanupFence'))
    assert observer['inspectionErrors'] == 0
    assert observer['sourceBefore'] == observer['sourceAfter'] == [report['sourceSha'], '']
    assert all(not row['groupExists'] and row['returncode'] == 0 for row in observer['ownedProcessesAfterStop'])
    redaction = read(shared / 'credential-redaction.json')
    assert redaction['exporterSha256'] == CODEC
    manifest = {row['path']: row for row in redaction['fileManifest']}
    cases = {}
    for item in specs(browser['suites']):
        for test in item['tests']:
            assert test['expectedStatus'] == 'passed' and test['status'] == 'expected' and len(test['results']) == 1
            result = test['results'][0]
            assert result['status'] == 'passed' and result['retry'] == 0 and not result['errors']
            trace = [Path(row['path']).resolve() for row in result['attachments'] if row['name'] == 'trace']
            assert len(trace) == 1 and trace[0].is_relative_to(raw) and trace[0].name == 'trace.zip'
            directory = trace[0].parent
            pngs = sorted(directory.glob('*.png')); arias = sorted(directory.glob('*.aria.yml'))
            assert pngs and {p.stem for p in pngs} == {p.name[:-len('.aria.yml')] for p in arias}
            assert not any(p.name.startswith('test-failed-') for p in pngs)
            files = []
            for path in sorted([*pngs, *arias, *directory.glob('*oracle.json'), trace[0]]):
                relative = str(path.relative_to(raw)); target = shared / relative; record = manifest[relative]
                assert target.is_file() and record['originalSha256'] == digest(path) and record['sharedSha256'] == digest(target)
                row = {'path': relative, 'originalSha256': record['originalSha256'], 'sharedSha256': record['sharedSha256']}
                if path.suffix == '.png':
                    assert row['originalSha256'] == row['sharedSha256']
                    data = path.read_bytes(); assert data.startswith(b'\x89PNG\r\n\x1a\n')
                    row['pixelWidth'], row['pixelHeight'] = struct.unpack('>II', data[16:24])
                files.append(row)
            key = (test['projectName'], item['title']); assert key not in cases
            cases[key] = {'project': key[0], 'journey': key[1], 'actualTestHead': report['sourceSha'], 'files': files}
    assert len(cases) == count and {key[0] for key in cases} == PROJECTS
    return cases

def assemble(arguments):
    full, retake, full_shared, retake_shared, supplemental, destination = [Path(value).resolve() for value in (
        arguments.full, arguments.retake, arguments.full_shared, arguments.retake_shared, arguments.supplemental, arguments.destination)]
    diagnostics_raw, diagnostics_shared = Path(arguments.diagnostics).resolve(), Path(arguments.diagnostics_shared).resolve()
    assert not destination.exists() and all(not destination.is_relative_to(path) and not path.is_relative_to(destination) for path in (full, retake, full_shared, retake_shared, supplemental, diagnostics_raw, diagnostics_shared))
    original = run(full, full_shared, 48); corrected = run(retake, retake_shared, 6)
    diagnostics = run(diagnostics_raw, diagnostics_shared, 6)
    original_heads = {row['actualTestHead'] for row in original.values()}
    corrected_heads = {row['actualTestHead'] for row in corrected.values()}
    assert len(original_heads) == len(corrected_heads) == 1
    before, after = next(iter(original_heads)), next(iter(corrected_heads))
    assert all(re.fullmatch('[0-9a-f]{40}', value) for value in (before, after))
    repository = Path(arguments.repo).resolve()
    def git(*args):
        return subprocess.check_output(['git', '-c', 'core.fsmonitor=false', *args], cwd=repository)
    selector_path = 'Studio/tests/acceptance/activityPlaybackJourney.ts'
    old, new = (git('show', ref + ':' + selector_path) for ref in (before, after))
    assert old.count(b'Loading visible execution evidence') == old.count(b'Some visible execution evidence could not be loaded') == 1
    assert new == old.replace(b'Loading visible execution evidence', b'Loading visible run data').replace(b'Some visible execution evidence could not be loaded', b'Some visible run data could not be loaded')
    assert git('diff', '--name-only', before, after).decode().splitlines() == [selector_path]
    def other_tree(ref):
        return b'\0'.join(row for row in git('ls-tree', '-r', '-z', ref).split(b'\0') if row and row.split(b'\t', 1)[1] != selector_path.encode()) + b'\0'
    assert other_tree(before) == other_tree(after)
    equivalence = {'fullRunHead': before, 'retakeHead': after, 'onlyChangedPath': selector_path,
                   'change': 'Exactly two readiness text selectors; all other tracked modes, paths and blob IDs identical',
                   'beforeSelectorFileSha256': hashlib.sha256(old).hexdigest(), 'afterSelectorFileSha256': hashlib.sha256(new).hexdigest(),
                   'allOtherTrackedTreeSha256': hashlib.sha256(other_tree(before)).hexdigest(),
                   'note': 'Derived build commit stamps retain each actual head; no binary identity or new-head execution is invented'}
    diagnostic_heads = {row['actualTestHead'] for row in diagnostics.values()}; assert len(diagnostic_heads) == 1
    diagnostic_head = next(iter(diagnostic_heads))
    assert not git('diff', '--name-only', after, diagnostic_head, '--', 'Studio/src', 'Studio/package.json', 'Studio/pnpm-lock.yaml')
    equivalence['newDiagnosticsHead'] = diagnostic_head
    equivalence['newDiagnosticsAttribution'] = 'New authored optional Event deadline and opt-in test/runner controls; all Studio product source and dependency inputs remain identical. Earlier actual runs retain their original heads and native baseline.'
    assert set(diagnostics) == {(project, 'timeline durable diagnostics and process restart') for project in PROJECTS}
    assert set(corrected) == {(project, TRANSPORT) for project in PROJECTS}
    chosen = []
    for key in sorted(original):
        namespace = 'transport-retake' if key[1] == TRANSPORT else 'full-run'
        case = dict(corrected[key] if key[1] == TRANSPORT else original[key]); case['runNamespace'] = namespace
        for file in case['files']:
            file['bundlePath'] = namespace + '/' + file['path']
        chosen.append(case)
    for key in sorted(diagnostics):
        case = dict(diagnostics[key]); case['runNamespace'] = 'native-diagnostics'
        for file in case['files']: file['bundlePath'] = 'native-diagnostics/' + file['path']
        chosen.append(case)
    files = [file for case in chosen for file in case['files']]
    counts = {'cases': len(chosen), 'png': sum(file['path'].endswith('.png') for file in files),
              'aria': sum(file['path'].endswith('.aria.yml') for file in files),
              'oracles': sum(file['path'].endswith('oracle.json') for file in files),
              'traces': sum(file['path'].endswith('trace.zip') for file in files)}
    assert counts == {'cases': 54, 'png': 420, 'aria': 420, 'oracles': 48, 'traces': 54}
    assert read(supplemental / 'credential-redaction.json')['exporterSha256'] == CODEC
    supplemental_source = read(supplemental / 'source.json'); assert len(supplemental_source['files']) == 4
    assert all(digest(supplemental / row['name']) == row['sha256'] for row in supplemental_source['files'])
    metadata = {'kind': 'explicit same-product composite; original reports are not restamped', 'canonicalCounts': counts,
                'selection': '42 original nontransport cases plus all 6 complete transport retakes plus all 6 new native diagnostics/restart cases; all six original transport visual sets are superseded',
                'rawExecutedCases': 60, 'rawCasePngAriaPairs': 492, 'inputEquivalence': equivalence,
                'supersededVisualSets': [{'project': project, 'journey': TRANSPORT, 'runNamespace': 'full-run'} for project in sorted(PROJECTS)],
                'cases': chosen, 'supplemental': {'count': 4, 'namespace': 'supplemental-typography', 'kind': 'isolated component fixtures, not composed runtime proof'}}
    destination.mkdir()
    try:
        for source, name in ((full_shared, 'full-run'), (retake_shared, 'transport-retake'), (supplemental, 'supplemental-typography'), (diagnostics_shared, 'native-diagnostics')):
            assert not any(path.is_symlink() for path in source.rglob('*'))
            shutil.copytree(source, destination / name)
        (destination / 'ACCEPTANCE-MANIFEST.json').write_text(json.dumps(metadata, indent=2) + '\n')
    except BaseException:
        shutil.rmtree(destination); raise
    print(json.dumps(counts))

if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    for key in ('full', 'retake', 'full-shared', 'retake-shared', 'supplemental', 'destination', 'repo', 'diagnostics', 'diagnostics-shared'):
        parser.add_argument('--' + key, required=True)
    assemble(parser.parse_args())
