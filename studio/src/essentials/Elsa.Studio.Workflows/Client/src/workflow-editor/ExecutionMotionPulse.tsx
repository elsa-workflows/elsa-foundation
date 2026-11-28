import "./executionMotion.css";

export function ExecutionActivityPulse({ pulse, status }: { pulse?: string; status?: string }) {
  return pulse ? <span key={pulse} data-execution-status={status} className="wf-execution-node-pulse" data-execution-pulse="activity" aria-hidden="true" /> : null;
}
export function ExecutionSequencePulse({ pulse }: { pulse?: string }) {
  return pulse ? <span key={pulse} className="wf-execution-sequence-pulse" data-execution-pulse="connection" aria-hidden="true" /> : null;
}
