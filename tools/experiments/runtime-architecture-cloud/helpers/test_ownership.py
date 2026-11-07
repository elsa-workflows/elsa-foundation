"""Pure ownership controls; live proc/command controls run only on Linux."""
import errno
import json
import os
from pathlib import Path
import signal
import sys
import tempfile
import unittest
from unittest.mock import patch

from owned_resources import OwnedResources, ProcessInspectionUnknown, parse_stat, parse_uids
from process_scope import ProcessScope


def identity(pid=4242, ticks=123, **changes):
    return dict(pid=pid, uid=501, ppid=1, pgid=pid, sid=pid, startTicks=ticks,
                bootId='boot-a', comm='python', **changes)


def fake_scope(item=None):
    result = OwnedResources.__new__(OwnedResources)
    result.state = {'owned': {}, 'actions': [], 'errors': [], 'uncertainties': []}
    result._pidfds = {}
    result._pidfd_supported = True
    if item:
        result.state['owned'][result.key(item)] = item
    return result


class OwnershipControls(unittest.TestCase):
    def test_stat_uses_field_22_after_complete_comm(self):
        tail = ['S', '10', '20', '30'] + ['0'] * 15 + ['123456'] + ['0'] * 8
        actual = parse_stat('42 (a (b) name) ' + ' '.join(tail), 42)
        self.assertEqual((actual['ppid'], actual['pgid'], actual['sid'], actual['startTicks']), (10, 20, 30, 123456))
        self.assertEqual(actual['comm'], 'a (b) name')

    def test_stat_rejects_wrong_pid_and_truncation(self):
        for raw in ('43 (cmd) S ' + '0 ' * 19, '42 (cmd) S 1 2 3', '42 (cmd S 1 2 3'):
            with self.assertRaises(ValueError):
                parse_stat(raw, 42)

    def test_uids_require_all_four_values(self):
        self.assertEqual(parse_uids('Name:\tx\nUid:\t1\t2\t3\t4\n'), (1, 2, 3, 4))
        for raw in ('Uid: 1 2 3', 'Uid: 1 2 3 4\nUid: 1 2 3 4', 'Uid: 1 -2 3 4'):
            with self.assertRaises(ValueError):
                parse_uids(raw)

    def test_birth_key_captures_boot_and_ticks_but_survives_group_change(self):
        item = identity()
        self.assertEqual(OwnedResources.key(item), OwnedResources.key({**item, 'pgid': 9, 'sid': 8}))
        for changed in ({**item, 'bootId': 'boot-b'}, {**item, 'startTicks': 124}, {**item, 'uid': 0}):
            self.assertNotEqual(OwnedResources.key(item), OwnedResources.key(changed))

    def test_pidfd_signal_never_calls_numeric_kill(self):
        item = identity()
        scope = fake_scope(item)
        scope._pidfds[scope.key(item)] = 99
        with patch.object(scope, 'birth', return_value=item), patch('signal.pidfd_send_signal', create=True) as send, patch('os.kill') as numeric:
            self.assertIs(scope.signal_exact(item, signal.SIGKILL), True)
        send.assert_called_once_with(99, signal.SIGKILL, None, 0)
        numeric.assert_not_called()

    def test_reused_birth_is_preserved_and_recorded_unknown(self):
        item = identity()
        scope = fake_scope(item)
        with patch.object(scope, 'birth', return_value={**item, 'startTicks': 124}), patch('os.kill') as numeric:
            with self.assertRaises(ProcessInspectionUnknown):
                scope.signal_exact(item, signal.SIGKILL)
        numeric.assert_not_called()
        self.assertIn(str(item['pid']), scope.state['pendingProcessInspectionUnknowns'])

    def test_unowned_or_self_cannot_be_signalled(self):
        for item in (identity(), identity(pid=os.getpid())):
            scope = fake_scope(item if item['pid'] == os.getpid() else None)
            with patch('os.kill') as numeric:
                with self.assertRaises(ProcessInspectionUnknown):
                    scope.signal_exact(item, signal.SIGTERM)
            numeric.assert_not_called()

    def test_absent_target_does_not_credit_signal(self):
        item = identity()
        scope = fake_scope(item)
        with patch.object(scope, 'birth', return_value=None), patch('os.kill') as numeric:
            self.assertIs(scope.signal_exact(item, signal.SIGKILL), False)
        numeric.assert_not_called()
        self.assertEqual(scope.state['actions'], [])

    def test_pidfd_permission_error_is_unknown_not_fallback(self):
        item = identity()
        scope = fake_scope(item)
        with patch('os.pidfd_open', side_effect=OSError(errno.EPERM, 'denied'), create=True), patch('os.kill') as numeric:
            with self.assertRaises(ProcessInspectionUnknown):
                scope.pin_owned(item)
        numeric.assert_not_called()
        self.assertTrue(scope._pidfd_supported)

    def test_explicit_unsupported_fallback_signals_single_pid(self):
        item = identity()
        scope = fake_scope(item)
        with patch.object(scope, 'birth', return_value=item), patch('os.pidfd_open', side_effect=OSError(errno.ENOSYS, 'unsupported'), create=True), patch('os.kill') as numeric:
            self.assertIs(scope.signal_exact(item, signal.SIGTERM), True)
        numeric.assert_called_once_with(item['pid'], signal.SIGTERM)
        self.assertFalse(scope._pidfd_supported)

    def test_historical_sid_without_live_birth_does_not_claim_process(self):
        root, candidate = identity(), identity(pid=4243)
        candidate['sid'] = root['sid']
        scope = fake_scope(root)
        scope.state.update(baselineProcesses=[], roots=[root])
        with patch.object(scope, 'processes', return_value={candidate['pid']: candidate}), patch.object(scope, 'pin_owned') as pin:
            scope.observe()
        self.assertNotIn(scope.key(candidate), scope.state['owned'])
        self.assertEqual(scope.state['unanchoredHistoricalSidCandidates'], [candidate])
        pin.assert_not_called()

    @unittest.skipUnless(sys.platform.startswith('linux'), 'requires actual Linux /proc and gated child')
    def test_actual_gated_root_registered_before_exec_and_clean_exit(self):
        with tempfile.TemporaryDirectory() as directory:
            scope = ProcessScope(directory, 'control-nonce')
            scope.baseline()
            command = [sys.executable, '-c', 'import json,os,sys; s=json.load(open(sys.argv[1])); assert any(r["pid"]==os.getpid() for r in s["roots"])', str(Path(directory) / 'resources.json')]
            self.assertEqual(scope.bounded('gate-control', command, directory, os.environ.copy(), timeout=10), 0)
            root = scope.state['commands'][0]['root']
            self.assertEqual(root['bootId'], scope.boot_id)
            self.assertGreater(root['startTicks'], 0)
            self.assertIs(scope.prove_quiescence('control'), True)

    @unittest.skipUnless(sys.platform.startswith('linux'), 'requires actual Linux /proc')
    def test_actual_current_birth_and_boot_scope(self):
        with tempfile.TemporaryDirectory() as directory:
            scope = OwnedResources(directory, 'control-nonce')
            birth = scope.birth(os.getpid())
            self.assertEqual(birth['uid'], os.geteuid())
            self.assertEqual(birth['ppid'], os.getppid())
            self.assertEqual(birth['pgid'], os.getpgid(0))
            self.assertEqual(birth['sid'], os.getsid(0))
            self.assertEqual(birth['bootId'], scope.boot_id)


if __name__ == '__main__':
    unittest.main()
