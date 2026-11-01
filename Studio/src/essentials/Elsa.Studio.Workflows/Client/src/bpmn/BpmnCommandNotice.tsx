import { StudioButton } from "@elsa-workflows/studio-ui";
import type { useBpmnCommand } from "./useBpmnCommand";

export function BpmnCommandNotice({ command, label }: { command: ReturnType<typeof useBpmnCommand>; label: "start" | "completion" | "retry" }) {
  const receipt = command.record;
  return <div className="wf-bpmn-command">
    {command.error ? <p role="alert">{command.error}</p> : null}
    {receipt ? <>
      <p role="status">{receipt.outcome?.pending ? "Pending: waiting for the command outcome."
        : command.confirmed ? `Last ${label}: ${String(receipt.outcome?.result?.status).toLowerCase()}${receipt.outcome?.replay ? " (replayed)" : ""}.`
          : `The ${label} outcome is unconfirmed. Resume uses the retained request.`}</p>
      <StudioButton type="button" variant="default" disabled={command.busy || command.blocked} onClick={() => void command.replay()}>
        {command.confirmed ? `Replay last ${label}` : `Resume ${label}`}
      </StudioButton>
      {(receipt.operation.kind === "complete-human" || receipt.operation.kind === "retry-service" || receipt.outcome?.executionId) && !command.confirmed
        ? <StudioButton type="button" variant="default" disabled={command.busy || command.blocked} onClick={() => void command.check()}>Check outcome</StudioButton> : null}
    </> : null}
  </div>;
}
