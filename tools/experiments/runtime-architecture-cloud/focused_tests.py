"""Focused prototype contracts through the runner's frozen owned-process phases."""
import hashlib
import json
from pathlib import Path
import xml.etree.ElementTree as ET


CORE = 'Essentials/tests/Workflows/Runtime/Tests/Elsa.Workflows.Runtime.Tests.csproj'
EF = ('Essentials/tests/Workflows/Runtime/Persistence/EntityFrameworkCore/Tests/'
      'Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests.csproj')
CORE_FILTER = 'FullyQualifiedName~Elsa.Workflows.Runtime.Tests.RunOwnedExecutionViewTests'
EF_FILTER = ('FullyQualifiedName~EfRuntimeCheckpointCommitStoreTests.Run_view_cannot_admit_a_baseline_while_a_suppressed_flow_raw_writer_is_in_flight'
             '|FullyQualifiedName~EfRuntimeCheckpointCommitStoreTests.Run_view_observes_raw_writers_from_a_fresh_DI_scope_or_direct_constructor')
SNAPSHOT_FILTER = 'FullyQualifiedName~EfRuntimeCheckpointCommitStoreTests.Snapshot_'
FLAGS = ['-m:1', '-nr:false', '-p:UseSharedCompilation=false']
NS = {'t': 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}


def save(path, value):
    temporary = path.with_suffix('.tmp')
    temporary.write_text(json.dumps(value, indent=2) + '\n')
    temporary.replace(path)


def read_trx(path, expected):
    """Keep actual counters and outcomes, including failed/skipped runs."""
    result = {'path': str(path), 'expectedPassed': expected, 'status': 'NOT_RUN'}
    if not path.is_file():
        return result
    content = path.read_bytes()
    result['sha256'] = hashlib.sha256(content).hexdigest()
    document = ET.fromstring(content)
    counters = document.find('.//t:Counters', NS)
    rows = document.findall('.//t:UnitTestResult', NS)
    result['counters'] = dict(counters.attrib) if counters is not None else {}
    result['outcomes'] = [{key: row.get(key) for key in ('testId', 'testName', 'outcome')} for row in rows]
    result['status'] = 'FAILED'
    if counters is not None and len(rows) == expected and all(row.get('outcome') == 'Passed' for row in rows):
        values = counters.attrib
        if (int(values.get('total', -1)) == int(values.get('executed', -1)) == int(values.get('passed', -1)) == expected
                and all(int(values.get(key, '0')) == 0 for key in
                        ('failed', 'error', 'timeout', 'aborted', 'inconclusive', 'notRunnable', 'notExecuted', 'disconnected', 'pending', 'inProgress'))):
            result['status'] = 'PASSED'
    return result


def run_focused_tests(runner, variant):
    """Run A core20+EF8 or B core20+EF8+Snapshot16. Never a timing cohort."""
    if variant not in ('runview', 'snapshot'):
        raise ValueError('Focused prototype contracts require runview or snapshot')
    from runner import commit, git

    item = runner.state['variants'][variant]
    source = Path(item['worktree'])
    receipt_path = runner.evidence / ('focused-' + variant + '.json')
    if receipt_path.exists():
        raise ValueError('Focused test receipt is create-only')
    groups = [('core', CORE, [('core20', CORE_FILTER, 20)]),
              ('ef', EF, [('ef8', EF_FILTER, 8)] + ([('snapshot16', SNAPSHOT_FILTER, 16)] if variant == 'snapshot' else []))]
    receipt = {'schema': 'runtime-prototype-focused-tests-v1', 'variant': variant, 'status': 'RUNNING',
               'timingCohort': False, 'groups': [
                   {'name': name, 'project': project, 'status': 'NOT_RUN',
                    'tests': [{'name': label, 'filter': selected, 'expectedPassed': count, 'status': 'NOT_RUN'}
                              for label, selected, count in tests]} for name, project, tests in groups],
               'sourceCommit': item['commit'], 'sourceTree': item['tree']}
    save(receipt_path, receipt)
    item['focusedTests'] = {'status': 'RUNNING', 'receiptPath': str(receipt_path)}
    runner.save()
    try:
        for (name, project, tests), group in zip(groups, receipt['groups']):
            save(receipt_path, receipt)
            lock = str(Path(project).parent / 'packages.lock.json')
            if not (source / lock).exists():
                # The generated lock is not qualification: freeze it in source before the actual build/tests.
                group['bootstrapReceipt'] = runner.phase(variant, 'focused-' + name + '-bootstrap', [
                    {'name': 'restore-create-test-lock', 'argv': ['dotnet', 'restore', project, '--use-lock-file', '--disable-parallel'] + FLAGS,
                     'timeoutSeconds': 540}], generated=[lock])
                if not (source / lock).is_file():
                    raise ValueError('Test restore did not generate the declared dependency lock')
                commit(source, 'Commit focused ' + name + ' test dependency lock before qualification')
                item.update(commit=git(source, 'rev-parse', 'HEAD'), tree=git(source, 'rev-parse', 'HEAD^{tree}'))
                if 'buildReceipt' in item:
                    item.setdefault('supersededBuildReceipts', []).append(item.pop('buildReceipt'))
                item.pop('binary', None)
                item.update(buildStatus='NOT_RUN', qualificationStatus='NOT_RUN')
                receipt.update(sourceCommit=item['commit'], sourceTree=item['tree'])
                runner.save()
            group['restoreReceipt'] = runner.phase(variant, 'focused-' + name + '-restore', [
                {'name': 'locked-test-restore', 'argv': ['dotnet', 'restore', project, '--locked-mode', '--disable-parallel'] + FLAGS,
                 'timeoutSeconds': 540}])
            phase_label = 'focused-' + name + '-contracts'
            phase_output = runner.evidence / 'phases' / (variant + '-' + phase_label) / 'output'
            binary_dir = source / Path(project).parent / 'bin/Release/net10.0'
            binary = binary_dir / (Path(project).stem + '.dll')
            commands = [{'name': 'build-test-project', 'argv': ['dotnet', 'build', project, '-c', 'Release', '--no-restore'] + FLAGS,
                         'timeoutSeconds': 360, 'captureArtifactDirectories': [str(binary_dir)]}]
            for label, selected, count in tests:
                trx = phase_output / (label + '.trx')
                commands.append({'name': label, 'argv': ['dotnet', 'vstest', str(binary), '--TestCaseFilter:' + selected,
                    '--Logger:trx;LogFileName=' + trx.name, '--ResultsDirectory:' + str(phase_output)],
                    'timeoutSeconds': 180 // len(tests), 'captureArtifacts': [str(trx)],
                    'trx': {'path': str(trx), 'expectedPassed': count}})
            group.update(status='RUNNING', sourceCommit=item['commit'], sourceTree=item['tree'])
            save(receipt_path, receipt)
            try:
                group['phaseReceipt'] = runner.phase(variant, phase_label, commands)
            finally:
                # Even a nonzero test process must retain its TRX outcomes and the remaining NOT_RUN entries.
                for row in group['tests']:
                    row.update(read_trx(phase_output / (row['name'] + '.trx'), row['expectedPassed']))
                group['status'] = 'PASSED' if 'phaseReceipt' in group and all(row['status'] == 'PASSED' for row in group['tests']) else 'FAILED'
                save(receipt_path, receipt)
            if group['status'] != 'PASSED':
                raise RuntimeError('Focused test outcomes differ from the required executed cases')
        receipt['status'] = 'PASSED'
        return receipt
    except BaseException as failure:
        if 'group' in locals() and group['status'] != 'PASSED':
            group['status'] = 'FAILED'
        receipt.update(status='FAILED', failureType=type(failure).__name__)
        raise
    finally:
        save(receipt_path, receipt)
        item['focusedTests'] = {'status': receipt['status'], 'receiptPath': str(receipt_path),
                                'sourceCommit': receipt['sourceCommit'], 'sourceTree': receipt['sourceTree']}
        runner.save()
