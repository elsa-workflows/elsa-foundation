import React from "react";
import { useStudioRouteLocation, type ElsaStudioModuleApi } from "@elsa-workflows/studio-sdk";
import { CopyableIdentifier, StudioButton, StudioPage, StudioPageHeader, StudioTableContainer } from "@elsa-workflows/studio-ui";
import { JobsLink, JobsNavigation, PageFooter, ReadState, date, gateLabel, type Navigate } from "./Shared";
import { useJobsRead } from "./useJobs";
import { permissions, type OperationAudit, type Page } from "./types";

export function OperationAuditPage({ api, navigate }: { api: ElsaStudioModuleApi; navigate: Navigate }) {
  const after = useStudioRouteLocation().searchParams.get("after");
  const read = useJobsRead(api, "operation-audit:" + after,
    client => client.read<Page<OperationAudit>>("operations", [], { limit: 25, after }), permissions.audit);
  return <StudioPage className="jobs" aria-label="Job operation audit"><StudioPageHeader title="Operation audit"
    description="Inspect retained confirmations with the original actor, reason and recording time. Older records may have unavailable metadata."
    actions={<StudioButton onClick={() => void read.refresh()} disabled={read.loading}>Refresh</StudioButton>} /><JobsNavigation navigate={navigate} />
    <ReadState {...read} empty={read.value?.items.length === 0} retry={() => void read.refresh()}>
      <StudioTableContainer role="region" aria-label="Operation audit table" tabIndex={0}><table className="studio-table">
        <thead><tr><th scope="col">Operation</th><th scope="col">Actor and reason</th><th scope="col">Target</th><th scope="col">Recorded confirmation</th></tr></thead>
        <tbody>{read.value?.items.map(item => <tr key={item.operationId}>
          <td>{item.kind ?? "Metadata unavailable"}<CopyableIdentifier value={item.operationId} label="Operation ID" /></td>
          <td>{item.diagnostic ? "Original actor, reason and time unavailable" : <>{item.actor}<br />{item.reason}<br />{date(item.recordedAt)}</>}</td>
          <td>{item.target?.gate ? gateLabel(item.target.gate) : item.target?.jobId ?
            <JobsLink path={`/jobs/${encodeURIComponent(item.target.jobId)}`} navigate={navigate}>{item.target.jobId}</JobsLink> : item.target?.scheduleId ?
              <JobsLink path={`/jobs/schedules/${encodeURIComponent(item.target.scheduleId)}`} navigate={navigate}>{item.target.scheduleId}</JobsLink> : "Unavailable"}
            {item.target?.holdId && <CopyableIdentifier value={item.target.holdId} label="Hold ID" />}
            {item.kind === "setCapacity" && <p>Permission limit: {item.target?.capacityLimit ?? "Unlimited"}</p>}
            {item.kind === "setScheduleEnabled" && <p>{item.target?.enabled ? "Enabled" : "Disabled"}</p>}</td>
          <td>{item.confirmation?.status ?? "Unavailable"}{item.confirmation?.jobId && <><br /><JobsLink path={`/jobs/${encodeURIComponent(item.confirmation.jobId)}`} navigate={navigate}>Open confirmed job</JobsLink></>}</td>
        </tr>)}</tbody></table></StudioTableContainer>
    </ReadState>{read.value && <PageFooter count={read.value.items.length} next={read.value.next} after={after}
      change={cursor => navigate("/jobs/operations" + (cursor ? "?after=" + encodeURIComponent(cursor) : ""))} />}
  </StudioPage>;
}
