"""Two-host retained-boundary qualification for Runtime Efficiency #2531 (runs as root on the PostgreSQL host).

Owner A runs on one worker, recovery owner B on another; both share the PostgreSQL container on this host.
Controls the workers over a task-only SSH key; never prints the database password.
"""
import base64, json, pathlib, subprocess, sys, time

T = pathlib.Path('/root/elsa-eff-i68'); OUT = T / sys.argv[1]; OUT.mkdir()
HOSTS = {'A': '10.238.2.5', 'B': '10.238.2.6'}; RB = '/home/elsarunner/elsa-eff-i68'; BIN = RB + '/boundary-r8/Worker'
PG = 'elsa-efficiency-i68-pg'; PASSWORD = (T / 'pg-password').read_text().strip()
SSH = ['ssh', '-i', str(T / 'ssh-key'), '-o', 'StrictHostKeyChecking=accept-new', '-o', f'UserKnownHostsFile={T}/known_hosts',
       '-o', 'ControlMaster=auto', '-o', f'ControlPath={T}/cm-%h', '-o', 'ControlPersist=900', '-o', 'BatchMode=yes']
ledger = []
for _ip in HOSTS.values():
    subprocess.run(SSH + ['-fN', 'elsarunner@' + _ip], stdin=subprocess.DEVNULL, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, check=True, timeout=60)


def ssh(host, command, data=None, timeout=200):
    p = subprocess.run(SSH + ['elsarunner@' + HOSTS[host], command], input=data, capture_output=True, text=True, timeout=timeout)
    return p.returncode, p.stdout


def psql(db, sql):
    p = subprocess.run(['docker', 'exec', PG, 'psql', '-U', 'postgres', '-d', db, '-At', '-c', sql], capture_output=True, text=True, timeout=120)
    if p.returncode: raise RuntimeError('psql failed: ' + p.stderr[-300:])
    return p.stdout


def snapshot(db, tables):
    return {t: json.loads(psql(db, f"select coalesce(json_agg(to_jsonb(x) order by to_jsonb(x)::text), '[]') from {t} x")) for t in tables}


CORE = ['elsa_runtime_activity_execution_state', 'elsa_runtime_workflow_execution_state', 'elsa_runtime_incident_state',
        'elsa_runtime_scheduler_work_item', 'elsa_runtime_post_commit_outbox']
ROOTS = ['elsa_runtime_workflow_executable', 'elsa_runtime_workflow_executable_source_reference', 'elsa_runtime_workflow_executable_root_write_lease']


def prepare(case):
    db = ('i68_' + OUT.name + '_' + case).replace('-', '_').lower()
    psql('postgres', f'create database {db}')
    env = f"ELSA_EFFICIENCY_POSTGRES_CONNECTION='Host=10.238.2.4;Port=15432;Username=postgres;Password={PASSWORD};Database={db}'\n"
    for host in HOSTS:
        code, _ = ssh(host, f'umask 077; mkdir -p {RB}/cases/{OUT.name}-{case} && cat > {RB}/cases/{OUT.name}-{case}/pg.env', data=env)
        assert code == 0
    return db


def config(case, host, name, **values):
    d = f'{RB}/cases/{OUT.name}-{case}'
    cfg = dict(Database='postgres', Provider='PostgreSql', Reached=f'{d}/reached.json', Calls=f'{d}/{name}-calls.txt', Result=f'{d}/{name}-result.json', Cap=50, Point='none')
    cfg.update(values)
    assert ssh(host, f'cat > {d}/{name}.json', data=json.dumps(cfg))[0] == 0
    return cfg


def launch(host, case, name):
    d = f'{RB}/cases/{OUT.name}-{case}'
    script = f"cd {d} && set -a && . ./pg.env && set +a && {BIN} {name}.json > {name}.log 2>&1; echo $? > {name}.exit"
    code, out = ssh(host, f"setsid sh -c '{script}' < /dev/null > /dev/null 2>&1 & echo $!")
    assert code == 0
    return int(out.strip())


def run(host, case, name, timeout=180):
    d = f'{RB}/cases/{OUT.name}-{case}'
    code, out = ssh(host, f"cd {d} && set -a && . ./pg.env && set +a && timeout {timeout} {BIN} {name}.json > {name}.log 2>&1; echo $?", timeout=timeout + 30)
    return int(out.strip())


def read(host, path):
    code, out = ssh(host, f'cat {path} 2>/dev/null')
    return out if code == 0 else None


def wait_reached(case, pid):
    deadline = time.monotonic() + 120
    while True:
        code, out = ssh('A', f'test -f {RB}/cases/{OUT.name}-{case}/reached.json && echo yes; kill -0 {pid} 2>/dev/null && echo alive')
        if 'yes' in out: return
        if 'alive' not in out: raise RuntimeError('Owner A exited before its gate')
        if time.monotonic() > deadline: raise RuntimeError('Owner A gate timeout')
        time.sleep(.1)


def wait_exit(case, name, timeout=180):
    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        out = read('A', f'{RB}/cases/{OUT.name}-{case}/{name}.exit')
        if out and out.strip(): return int(out.strip())
        time.sleep(.5)
    raise RuntimeError('Owner A did not exit after release')


def initialize(case):
    config(case, 'A', 'init', Action='initialize', Mode='Balanced', Scenario='suspend')
    assert run('A', case, 'init') == 0, 'initialize failed'


def dec(s):
    try: return base64.b64decode(s, validate=True).decode('utf-16-le')
    except Exception: return s


def record(row):
    ledger.append(row); print(json.dumps(row), flush=True)
    (OUT / 'ledger.json').write_text(json.dumps(ledger, indent=1))


def guarded(kind, case, fn):
    started = time.time()
    try: row = fn()
    except Exception as e: row = dict(passed=False, error=type(e).__name__ + ': ' + str(e)[:300])
    record(dict(kind=kind, case=case, seconds=round(time.time() - started, 1), **row))


def crash(mode, scenario, point, cap):
    case = f'crash-{mode}-{scenario}-{point}'.lower(); db = prepare(case); initialize(case)
    config(case, 'A', 'a', Action='run', Mode=mode, Scenario=scenario, Point=point, Cap=cap)
    pid = launch('A', case, 'a'); wait_reached(case, pid)
    ssh('A', f'kill -9 -- -{pid}'); killed_at = time.time()
    before = snapshot(db, CORE); (OUT / f'{case}-before-recovery.json').write_text(json.dumps(before, indent=1))
    completed = []
    for row in before['elsa_runtime_activity_execution_state']:
        state = row['ContentJson']; state = json.loads(state) if isinstance(state, str) else state
        if state.get('status') == 'Completed': completed.append((dec(state['execution']['activityExecutionId']), dec(state['execution']['executableNodeId'])))
    config(case, 'B', 'b', Action='recover', Mode=mode, Scenario=scenario, Point=point, Cap=cap)
    code = run('B', case, 'b'); result = json.loads(read('B', f'{RB}/cases/{OUT.name}-{case}/b-result.json') or '{}')
    final = {(a['Execution']['ActivityExecutionId'], a['Execution']['ExecutableNodeId']) for a in result.get('activities', [])}
    replayed = (read('B', f'{RB}/cases/{OUT.name}-{case}/b-calls.txt') or '').splitlines()
    preserved = all(x in final for x in completed)
    not_repeated = all('parent|' + node not in replayed for _, node in completed if node.startswith('work-'))
    return dict(passed=code == 0 and bool(result.get('correct')) and preserved and not_repeated, recoveryExit=code, correct=result.get('correct'),
                durableCompletedBefore=len(completed), durableCompletedPreserved=preserved, persistedBodiesNotRepeated=not_repeated)


def takeover(mode, scenario, point, cap, reclaim=False, interrupt=False, retry=None):
    case = f"{('interrupt-' + retry.lower() if retry else 'interrupt') if interrupt else 'reclaim' if reclaim else 'takeover'}-{mode}-{scenario}-{point}".lower(); db = prepare(case); initialize(case)
    release = f'{RB}/cases/{OUT.name}-{case}/release'
    config(case, 'A', 'a', Action='run', Mode=mode, Scenario=scenario, Point=point, Cap=cap, Release=release, RetryTransient=bool(retry), TransientRetryMode=retry or 'RetryAfter')
    pid = launch('A', case, 'a'); wait_reached(case, pid)
    row = {}
    if interrupt:
        t0 = time.time(); subprocess.run(['docker', 'restart', '-t', '5', PG], check=True, capture_output=True)
        for _ in range(120):
            if subprocess.run(['docker', 'exec', PG, 'pg_isready', '-U', 'postgres', '-q']).returncode == 0: break
            time.sleep(.5)
        row['databaseRestartSeconds'] = round(time.time() - t0, 1)
        ssh('A', f'touch {release}'); a_exit = wait_exit(case, 'a')
        a_result = json.loads(read('A', f'{RB}/cases/{OUT.name}-{case}/a-result.json') or '{}')
        config(case, 'B', 'b', Action='recover', Mode=mode, Scenario=scenario, Point=point, Cap=cap, RetryTransient=bool(retry), TransientRetryMode=retry or 'RetryAfter')
        code = run('B', case, 'b'); result = json.loads(read('B', f'{RB}/cases/{OUT.name}-{case}/b-result.json') or '{}')
        a_log = read('A', f'{RB}/cases/{OUT.name}-{case}/a.log') or ''
        row.update(ownerExit=a_exit, ownerCompleted=bool(a_result.get('correct')), ownerError=next((w for w in ['NpgsqlException', 'PostgresException', 'RuntimeStaleFencingTokenException', 'TimeoutException'] if w in a_log), None),
                   recoveryExit=code, correct=result.get('correct'))
        activities = result.get('activities', [])
        keys = [(a['Execution']['WorkflowExecutionId'], a['Execution']['ExecutableNodeId']) for a in activities]
        work = [a for a in activities if a['Execution']['ExecutableNodeId'].startswith('work-')]
        row.update(activityRows=len(activities), duplicateActivityRows=len(keys) - len(set(keys)), workCompleted=sum(a['Status'] in ('Completed', 4) for a in work),
                   queue=result.get('queue'), pendingOutbox=result.get('pending'), incidentsAfterRecovery=result.get('incidents'))
        if retry:
            # A host transient-retry policy must turn the interruption into automatic completion.
            row['passed'] = code == 0 and bool(result.get('correct'))
        else:
            # The documented default (Noop -> DoNotRetry) parks the poisoned work as one blocking incident for an operator,
            # leaving the run non-terminal, with no lost or duplicated work and nothing left queued or undelivered.
            incidents = [json.loads(x) for x in psql(db, "select \"ContentJson\" from elsa_runtime_incident_state").splitlines() if x]
            row['incidentsDecoded'] = [{'failureType': dec(i.get('failureType', '')), 'status': i.get('status'), 'action': dec((i.get('resolutionOutcome') or {}).get('actionKind', '')),
                                        'message': dec(i.get('message', ''))[-160:]} for i in incidents]
            row['passed'] = (len(incidents) == 1 and row['incidentsDecoded'][0]['failureType'] == 'SchedulerWorkPoisoned' and '57P01' in row['incidentsDecoded'][0]['message']
                             and row['incidentsDecoded'][0]['action'] == 'WaitForIntervention' and row['duplicateActivityRows'] == 0
                             and row['workCompleted'] == 20 and row['queue'] == 0 and row['pendingOutbox'] == 0 and all(w.get('Status') not in ('Faulted', 4) for w in result.get('workflows', [])))
        return row
    config(case, 'B', 'b', Action='recover', Mode=mode, Scenario=scenario, Point=point, Cap=cap)
    code = run('B', case, 'b'); result = json.loads(read('B', f'{RB}/cases/{OUT.name}-{case}/b-result.json') or '{}')
    tables = CORE + (ROOTS if reclaim else [])
    if reclaim:
        config(case, 'B', 'c', Action='reclaim', Mode=mode, Scenario=scenario, Point=point, Cap=cap)
        c_code = run('B', case, 'c'); c_result = json.loads(read('B', f'{RB}/cases/{OUT.name}-{case}/c-result.json') or '{}')
        row.update(reclaimExit=c_code, reclaimed=c_result.get('correct'), deletions=c_result.get('deletions'), sweep=c_result.get('sweep'))
    before = snapshot(db, tables); (OUT / f'{case}-before-stale-owner.json').write_text(json.dumps(before, indent=1))
    ssh('A', f'touch {release}'); a_exit = wait_exit(case, 'a')
    after = snapshot(db, tables); (OUT / f'{case}-after-stale-owner.json').write_text(json.dumps(after, indent=1))
    a_log = read('A', f'{RB}/cases/{OUT.name}-{case}/a.log') or ''
    rejected = a_exit != 0 and 'RuntimeStaleFencingTokenException' in a_log
    row.update(recoveryExit=code, correct=result.get('correct'), staleOwnerExit=a_exit, staleFenceRejected=rejected, staleOwnerDidNotChangeDurableState=before == after)
    row['passed'] = code == 0 and bool(result.get('correct')) and rejected and before == after and (not reclaim or (row['reclaimExit'] == 0 and bool(row['reclaimed'])))
    if reclaim: row['rootsAfter'] = {t: len(after[t]) for t in ROOTS}
    return row


only = {a for a in sys.argv[2:] if not a.startswith('--retry=')}
RETRY_MODES = next((a.split('=', 1)[1].split(',') for a in sys.argv[2:] if a.startswith('--retry=')), ['RetryAfter'])
def wanted(kind): return not only or kind in only

if wanted('crash'):
    for parent in [False, True]:
        for mode in (['Durable', 'Balanced'] if parent else ['Durable', 'Balanced', 'Ephemeral']):
            for scenario in ['suspend', 'child']:
                for base in ['before-commit', 'after-commit', 'after-advance', 'after-body']:
                    point = ('parent-' if parent else '') + base
                    guarded('crash', f'{mode}-{scenario}-{point}', lambda: crash(mode, scenario, point, 1 if parent else 50))
if wanted('takeover'):
    for parent in [False, True]:
        for mode in (['Durable', 'Balanced'] if parent else ['Durable', 'Balanced', 'Ephemeral']):
            for scenario in ['suspend', 'child']:
                guarded('takeover', f'{mode}-{scenario}', lambda: takeover(mode, scenario, 'parent-after-advance' if parent else 'after-body', 1 if parent else 50))
if wanted('reclaim'):
    for mode in ['Durable', 'Balanced', 'Ephemeral']:
        for scenario in ['suspend', 'child']:
            guarded('reclaim', f'{mode}-{scenario}', lambda: takeover(mode, scenario, 'after-body', 50, reclaim=True))
if wanted('interrupt'):
    for mode in ['Durable', 'Balanced', 'Ephemeral']:
        for scenario in ['suspend', 'child']:
            guarded('interrupt', f'{mode}-{scenario}', lambda: takeover(mode, scenario, 'before-commit', 50, interrupt=True))
            for retry in RETRY_MODES:
                guarded('interrupt-' + retry.lower(), f'{mode}-{scenario}', lambda: takeover(mode, scenario, 'before-commit', 50, interrupt=True, retry=retry))
passed = sum(r['passed'] for r in ledger)
(OUT / 'finished').write_text('passed' if passed == len(ledger) else 'failed')
print(json.dumps(dict(total=len(ledger), passed=passed)), flush=True)
