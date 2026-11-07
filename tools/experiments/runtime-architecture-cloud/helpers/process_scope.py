"""Process-only adapter; base code is injected from captured, hash-verified bytes."""
import os
import signal
import time

if 'OwnedResources' not in globals():
    from owned_resources import OwnedResources, Cancelled


class OperationalTimeout(BaseException):
    pass


class ProcessScope(OwnedResources):
    def __init__(self, out, nonce):
        super().__init__(out, nonce)
        self.started = time.monotonic()
        self.command_deadline = self.started + 540
        self.closeout_deadline = self.started + 600
        self.state['controller'] = self.birth(os.getpid())
        assert self.state['controller'] is not None
        self.state['operationalCloseoutSeconds'] = 600
        self.state['commandBudgetSeconds'] = 540
        self.state['slotReleased'] = False
        self.state['deputyWitnessRequiredBeforeSlotRelease'] = True
        self.save()

    def baseline(self):
        # Container lifecycle is inspected by the controller, never inferred or removed here.
        self.state['baselineProcesses'] = [self.key(p) for p in self.processes().values()]
        self.save()

    def bounded(self, label, argv, cwd, env, timeout=180, closeout=False):
        if self.cancelled is not None and not closeout:
            raise Cancelled()
        remaining = (self.closeout_deadline if closeout else self.command_deadline) - time.monotonic()
        if remaining <= 0:
            raise OperationalTimeout('Operational deadline reached; do not start another command')
        self.closing = closeout
        old_handler = signal.getsignal(signal.SIGALRM)
        def expire(_signum, _frame):
            raise OperationalTimeout('Owned command exceeded its operational bound')
        signal.signal(signal.SIGALRM, expire)
        self.state.setdefault('bounds', []).append({'label': label, 'seconds': min(timeout, remaining), 'closeout': closeout})
        self.save()
        try:
            signal.setitimer(signal.ITIMER_REAL, min(timeout, remaining))
            return self.run(label, argv, cwd, env)
        finally:
            signal.setitimer(signal.ITIMER_REAL, 0)
            signal.signal(signal.SIGALRM, old_handler)

    def prove_quiescence(self, where):
        result = False
        try:
            result = self.close_processes() is True
        except BaseException as failure:
            self.error(where, failure)
        self.state['processQuiescent'] = result
        self.state.setdefault('quiescenceProofs', []).append({'where': where, 'verified': result})
        self.save()
        return result


def stop_owned_controller(out, scope_class, nonce):
    """External 600s closeout action: only the recorded exact controller, never a group."""
    import json
    from pathlib import Path
    directory = Path(out)
    ledger = json.loads((directory / 'resources.json').read_text())
    assert ledger['nonce'] == nonce
    expected = ledger['controller']
    result = {'nonce': nonce, 'expected': expected, 'signalSent': False, 'slotReleased': False,
              'requiresControllerAndResourceReceiptInspection': True}
    try:
        inspector = OwnedResources(directory, nonce, recover=True)
        current = inspector.birth(expected['pid'])
        if current is None:
            result['result'] = 'RECORDED_CONTROLLER_ABSENT'
        elif scope_class.key(current) != scope_class.key(expected):
            result['result'] = 'IDENTITY_CHANGED_PRESERVED'
        else:
            # Repeat the full kernel identity immediately before a per-PID signal.
            again = inspector.birth(expected['pid'])
            if again is not None and scope_class.key(again) == scope_class.key(expected):
                try:
                    sent = inspector.signal_exact(expected, signal.SIGTERM, allow_controller=True)
                    result.update(result='CONTROLLED_STOP_REQUESTED' if sent else 'CONTROLLER_EXITED_BEFORE_SIGNAL', signalSent=sent)
                except ProcessLookupError:
                    result['result'] = 'CONTROLLER_EXITED_BEFORE_SIGNAL'
            else:
                result['result'] = 'IDENTITY_CHANGED_OR_ABSENT_PRESERVED'
    except BaseException as failure:
        result.update(result='INSPECTION_UNKNOWN_NO_SIGNAL',inspectionError=type(failure).__name__,inspection=getattr(failure,'details',None))
    (directory / 'external-stop-receipt.json').write_text(json.dumps(result, indent=2)+'\n')
    print(json.dumps(result))
