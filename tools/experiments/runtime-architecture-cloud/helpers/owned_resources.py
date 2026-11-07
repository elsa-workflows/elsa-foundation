"""Attempt-scoped supervision. No operations occur on import/compile."""
import errno
import json
import os
from pathlib import Path
import re
import signal
import subprocess
import sys
import time
import uuid


class Cancelled(BaseException):
    pass


class ProcessInspectionUnknown(RuntimeError):
    def __init__(self, details):
        self.details = details
        super().__init__('Process identity could not be verified')


def parse_stat(raw, requested_pid):
    """Linux proc_pid_stat: comm can contain spaces and parentheses; starttime is field 22."""
    opening, closing = raw.find('('), raw.rfind(')')
    if opening < 1 or closing <= opening:
        raise ValueError('Malformed proc stat framing')
    pid = int(raw[:opening].strip())
    tail = raw[closing + 1:].split()
    if pid != requested_pid or len(tail) < 20 or len(tail[0]) != 1:
        raise ValueError('Malformed proc stat identity')
    ppid, pgid, sid, ticks = (int(tail[index]) for index in (1, 2, 3, 19))
    if pid <= 0 or min(ppid, pgid, sid, ticks) < 0:
        raise ValueError('Invalid proc stat dimensions')
    return {'pid': pid, 'ppid': ppid, 'pgid': pgid, 'sid': sid, 'startTicks': ticks,
            'comm': raw[opening + 1:closing], 'state': tail[0]}


def parse_uids(raw):
    matches = [line.split()[1:] for line in raw.splitlines() if line.startswith('Uid:')]
    if len(matches) != 1 or len(matches[0]) != 4:
        raise ValueError('Missing or malformed proc UID identity')
    result = tuple(int(value) for value in matches[0])
    if min(result) < 0:
        raise ValueError('Invalid proc UID')
    return result


def read_at(directory_fd, name):
    descriptor = os.open(name, os.O_RDONLY | os.O_CLOEXEC, dir_fd=directory_fd)
    with os.fdopen(descriptor, 'r') as stream:
        return stream.read()



class OwnedResources:
    def __init__(self, out, nonce, recover=False):
        self.out, self.nonce = Path(out), nonce
        if not sys.platform.startswith('linux'):
            raise RuntimeError('Linux ownership helpers require the real /proc filesystem')
        self.proc = Path('/proc')
        self.boot_id = str(uuid.UUID((self.proc / 'sys/kernel/random/boot_id').read_text().strip()))
        boot = [line.split()[1] for line in (self.proc / 'stat').read_text().splitlines() if line.startswith('btime ')]
        if len(boot) != 1:
            raise RuntimeError('Cannot establish Linux boot timestamp')
        self.boot_seconds, self.clock_ticks = int(boot[0]), os.sysconf('SC_CLK_TCK')
        if self.clock_ticks <= 0:
            raise RuntimeError('Cannot establish Linux scheduler clock units')
        self._pidfds = {}
        self._pidfd_supported = hasattr(os, 'pidfd_open') and hasattr(signal, 'pidfd_send_signal')
        self.cancelled = None
        self.closing = False
        self.previous_handlers = {}
        self.children = []
        self.state = json.loads((self.out / 'resources.json').read_text()) if recover else {
            'nonce': nonce, 'bootId': self.boot_id, 'identityKind': 'linux-proc-boot-id-start-ticks', 'roots': [], 'owned': {}, 'commands': [], 'actions': [], 'errors': [], 'uncertainties': [],
            'pollingLimit': 'Polling is not a kernel-complete fork/reparent trace. Unobserved escaping descendants cannot be ruled out. Never claim full lifecycle coverage from this ledger alone.',
            'fullLifecycleCoverageVerified': False}
        if self.state['nonce'] != nonce or self.state.get('bootId') != self.boot_id:
            raise ProcessInspectionUnknown({'pid': os.getpid(), 'operation': 'recover', 'classification': 'nonce_or_boot_scope_changed'})
        self.state['signalMechanism'] = 'pidfd' if self._pidfd_supported else 'identity-rechecked-per-pid-fallback'
        self.state['startTimestampMeaning'] = 'startSec/startUsec derive from kernel btime/startTicks for compatibility; authoritative identity is bootId/startTicks'
        if recover:
            self.state.setdefault('priorCloseouts', []).append({
                'errors': self.state.get('errors', []), 'uncertainties': self.state.get('uncertainties', []),
                'disposition': self.state.get('closeoutDisposition'), 'cancelSignal': self.state.get('cancelSignal'),
                'ownedProcessSurvivors': self.state.get('ownedProcessSurvivors', []),
                'ownedContainerSurvivors': self.state.get('ownedContainerSurvivors', [])})
            self.state['errors'], self.state['uncertainties'] = [], []

    def save(self):
        staged = self.out / 'resources.json.tmp'
        staged.write_text(json.dumps(self.state, indent=2))
        staged.replace(self.out / 'resources.json')

    def error(self, where, error):
        record = {'operation': where, 'error': type(error).__name__}
        if isinstance(error, ProcessInspectionUnknown):
            record['inspection'] = error.details
        self.state['errors'].append(record)

    def unknown_process(self, details):
        self.state['processQuiescent'] = False
        self.state.setdefault('processInspectionUnknowns', []).append(details)
        self.state.setdefault('pendingProcessInspectionUnknowns', {})[str(details['pid'])] = details
        raise ProcessInspectionUnknown(details)

    def resolve_inspection(self, pid, proof):
        previous = self.state.setdefault('pendingProcessInspectionUnknowns', {}).pop(str(pid), None)
        if previous is not None:
            self.state.setdefault('processInspectionResolutions', []).append({'pid': pid, 'proof': proof})

    def birth(self, pid):
        if not isinstance(pid, int) or pid <= 0:
            self.unknown_process({'pid': pid, 'operation': 'proc-identity', 'classification': 'invalid_pid'})
        directory = self.proc / str(pid)
        descriptor = None
        try:
            # Pin the proc directory while reading: PID pathname reuse cannot mix old stat and new status files.
            descriptor = os.open(directory, os.O_RDONLY | os.O_DIRECTORY | os.O_CLOEXEC)
            first = parse_stat(read_at(descriptor, 'stat'), pid)
            first_uids = parse_uids(read_at(descriptor, 'status'))
            second = parse_stat(read_at(descriptor, 'stat'), pid)
            second_uids = parse_uids(read_at(descriptor, 'status'))
            if first['startTicks'] != second['startTicks'] or first_uids != second_uids:
                self.unknown_process({'pid': pid, 'operation': 'proc-identity', 'classification': 'identity_changed_during_read'})
            second.update(uid=second_uids[1], realUid=second_uids[0], savedUid=second_uids[2], fsUid=second_uids[3],
                          bootId=self.boot_id, clockTicks=self.clock_ticks,
                          startSec=self.boot_seconds + second['startTicks'] // self.clock_ticks,
                          startUsec=(second['startTicks'] % self.clock_ticks) * 1_000_000 // self.clock_ticks)
            self.resolve_inspection(pid, 'verified_pinned_proc_stat_status_and_boot_scope')
            return second
        except OSError as failure:
            if failure.errno in (errno.ENOENT, errno.ESRCH):
                try:
                    directory.stat()
                except OSError as absent:
                    if absent.errno in (errno.ENOENT, errno.ESRCH):
                        self.resolve_inspection(pid, 'proc_directory_absent_in_current_boot_scope')
                        return None
                self.unknown_process({'pid': pid, 'operation': 'proc-identity', 'classification': 'proc_files_missing_but_pid_path_not_proved_absent', 'errno': failure.errno})
            self.unknown_process({'pid': pid, 'operation': 'proc-identity', 'classification': 'unknown_errno', 'errno': failure.errno})
        except (ValueError, UnicodeError) as failure:
            self.unknown_process({'pid': pid, 'operation': 'proc-identity', 'classification': 'malformed_proc_identity', 'error': type(failure).__name__})
        finally:
            if descriptor is not None:
                os.close(descriptor)

    @staticmethod
    def key(item):
        return f"{item['bootId']}:{item['pid']}:{item['uid']}:{item['startTicks']}"

    def processes(self):
        pids = set()
        try:
            for directory in self.proc.iterdir():
                if not directory.name.isdigit():
                    continue
                try:
                    if directory.stat().st_uid == os.geteuid():
                        pids.add(int(directory.name))
                except FileNotFoundError:
                    continue
            pids.update(int(pid) for pid in self.state.get('pendingProcessInspectionUnknowns', {}))
            pids.update(item['pid'] for item in self.state['owned'].values())
        except OSError as failure:
            self.unknown_process({'pid': os.getpid(), 'operation': 'proc-enumeration', 'classification': 'unknown_errno', 'errno': failure.errno})
        result = {}
        for pid in sorted(pids):
            item = self.birth(pid)
            if item is not None:
                result[pid] = item
        return result

    def pin_owned(self, expected):
        key = self.key(expected)
        if key in self._pidfds or not self._pidfd_supported:
            return True
        try:
            descriptor = os.pidfd_open(expected['pid'], 0)
        except OSError as failure:
            if failure.errno == errno.ESRCH:
                return False
            if failure.errno == errno.ENOSYS:
                self._pidfd_supported = False
                self.state['signalMechanism'] = 'identity-rechecked-per-pid-fallback'
                return True
            self.unknown_process({'pid': expected['pid'], 'operation': 'pidfd_open', 'classification': 'unknown_errno', 'errno': failure.errno})
        try:
            observed = self.birth(expected['pid'])
            if observed is None or self.key(observed) != key:
                self.unknown_process({'pid': expected['pid'], 'operation': 'pidfd-registration', 'classification': 'identity_changed'})
            self._pidfds[key] = descriptor
            descriptor = None
            return True
        finally:
            if descriptor is not None:
                os.close(descriptor)

    def signal_exact(self, expected, signum, allow_controller=False):
        key = self.key(expected)
        controller = self.state.get('controller')
        authorized = key in self.state['owned'] or (allow_controller and controller is not None and self.key(controller) == key)
        if not authorized or expected['pid'] == os.getpid():
            self.unknown_process({'pid': expected['pid'], 'operation': 'targeted-signal', 'classification': 'not_owned_or_self'})
        now = self.birth(expected['pid'])
        if now is None:
            return False
        if self.key(now) != key:
            self.unknown_process({'pid': expected['pid'], 'operation': 'targeted-signal', 'classification': 'birth_changed_preserved'})
        if not self.pin_owned(expected):
            return False
        again = self.birth(expected['pid'])
        if again is None:
            return False
        if self.key(again) != key:
            self.unknown_process({'pid': expected['pid'], 'operation': 'targeted-signal', 'classification': 'birth_changed_preserved'})
        try:
            if self._pidfd_supported:
                signal.pidfd_send_signal(self._pidfds[key], signum, None, 0)
            else:
                os.kill(expected['pid'], signum)
            self.state['actions'].append({'pid': expected['pid'], 'birth': key, 'signal': int(signum),
                                          'mechanism': 'pidfd' if self._pidfd_supported else 'identity-rechecked-pid'})
            return True
        except ProcessLookupError:
            return False
        except OSError as failure:
            self.unknown_process({'pid': expected['pid'], 'operation': 'targeted-signal', 'classification': 'unknown_errno', 'errno': failure.errno})




    def observe(self):
        current = self.processes()
        baseline = set(self.state['baselineProcesses'])
        owned = self.state['owned']
        changed = True
        while changed:
            changed = False
            # A persisted numeric SID is not an identity. Only a currently live
            # exact-birth owned member can establish ownership through its session.
            live_owned_sessions = {p['sid'] for p in current.values() if self.key(p) in owned}
            for item in current.values():
                key = self.key(item)
                if key in baseline or key in owned:
                    continue
                parent = current.get(item['ppid'])
                known_parent = parent and self.key(parent) in owned
                session = item['sid'] in live_owned_sessions
                if known_parent or session:
                    self.pin_owned(item)
                    owned[key] = {**item, 'attribution': 'observed-parent-birth' if known_parent else 'owned-command-session'}
                    changed = True
        live_owned_sessions = {p['sid'] for p in current.values() if self.key(p) in owned}
        historical_sessions = {r['sid'] for r in self.state['roots']}
        self.state['unanchoredHistoricalSidCandidates'] = [p for p in current.values()
            if self.key(p) not in owned and self.key(p) not in baseline
            and p['sid'] in historical_sessions and p['sid'] not in live_owned_sessions]
        return current

    def install_signals(self):
        def cancel(signum, _):
            if self.cancelled is None:
                self.cancelled = signum
                if not self.closing:
                    raise Cancelled()
        for signum in (signal.SIGINT, signal.SIGTERM, signal.SIGHUP):
            self.previous_handlers[signum] = signal.signal(signum, cancel)

    def run(self, label, command, cwd, env):
        # Gate the child before executing the actual command; persist its kernel birth first.
        if 'baselineProcesses' not in self.state:
            raise RuntimeError('A verified baseline must precede command launch')
        gate_read, gate_write = os.pipe()
        shim = 'import os,sys,json; f=int(sys.argv[1]); b=os.read(f,1); os.close(f); c=json.loads(sys.argv[2]); sys.exit(125) if b!=b"x" else os.execvpe(c[0],c,os.environ)'
        process = None
        with (self.out / (label + '.log')).open('w') as log:
            try:
                process = subprocess.Popen([sys.executable, '-c', shim, str(gate_read), json.dumps(command)],
                    pass_fds=(gate_read,), start_new_session=True, cwd=cwd, env=env, stdout=log, stderr=subprocess.STDOUT)
                self.children.append(process)
                os.close(gate_read)
                gate_read = -1
                identity = self.birth(process.pid)
                if not identity or identity['sid'] != process.pid or identity['pgid'] != process.pid:
                    raise RuntimeError('Cannot establish gated command identity')
                if not self.pin_owned(identity):
                    raise RuntimeError('Gated command disappeared before registration')
                self.state['roots'].append(identity)
                self.state['owned'][self.key(identity)] = {**identity, 'attribution': 'gated-direct-child'}
                entry = {'label': label, 'argv': command, 'root': identity, 'exit': None}
                self.state['commands'].append(entry)
                self.save()  # If this fails the gate closes without executing the command.
                os.write(gate_write, b'x')
                os.close(gate_write)
                gate_write = -1
                while True:
                    self.observe()
                    self.save()
                    try:
                        code = process.wait(timeout=0.15)
                        entry['exit'] = code
                        self.observe()
                        self.save()
                        return code
                    except subprocess.TimeoutExpired:
                        pass
            finally:
                if gate_read != -1:
                    os.close(gate_read)
                if gate_write != -1:
                    os.close(gate_write)
                # Outer closeout performs identity-checked signals; never kill the launcher.
                if process and process.poll() is None:
                    self.state['unfinishedCommandPid'] = process.pid

    def close_processes(self):
        self.closing = True
        self.state['processQuiescent'] = False  # Inspection/closeout exceptions leave this false.
        for sig, grace in ((signal.SIGTERM, 10), (signal.SIGKILL, 3)):
            for child in self.children:
                child.poll()  # Reap terminated direct children; a zombie is not closed out.
            current = self.observe()
            for key, old in list(self.state['owned'].items()):
                now = current.get(old['pid'])
                if not now or self.key(now) != key or now['pid'] == os.getpid():
                    continue
                # Recheck exact kernel birth immediately before signalling a PID.
                again = self.birth(now['pid'])
                if again and self.key(again) == key:
                    self.signal_exact(again, sig)
            until = min(time.monotonic() + grace, getattr(self, 'closeout_deadline', float('inf')))
            while time.monotonic() < until:
                for child in self.children:
                    child.poll()
                current = self.observe()
                if not any(self.key(p) in self.state['owned'] for p in current.values()):
                    break
                time.sleep(0.15)
        for child in self.children:
            child.poll()
        current = self.observe()
        self.state['ownedProcessSurvivors'] = [p for p in current.values() if self.key(p) in self.state['owned']]
        potential = [p for p in current.values() if self.key(p) not in self.state['owned']
            and self.key(p) not in self.state['baselineProcesses']
            and re.search(r'dotnet|MSBuild|VBCSCompiler|testhost|vstest', p['comm'], re.I)]
        self.state['unattributedRuntimeCandidates'] = potential
        if potential:
            self.state['uncertainties'].append('New runtime candidates are not proved owned; preserved, not signalled')
        if self.state['unanchoredHistoricalSidCandidates']:
            self.state['uncertainties'].append('Historical SID has no live same-birth owned anchor; candidates preserved, not signalled')
        self.state['processQuiescent'] = not (self.state['ownedProcessSurvivors'] or potential
            or self.state['unanchoredHistoricalSidCandidates'] or self.state.get('pendingProcessInspectionUnknowns'))
        if self.state['processQuiescent']:
            for descriptor in self._pidfds.values():
                os.close(descriptor)
            self._pidfds.clear()
        return self.state['processQuiescent']
