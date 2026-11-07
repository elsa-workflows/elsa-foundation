import React from "react";
import { Layers } from "lucide-react";
import { useStudioRouteLocation, type ElsaStudioModuleApi, type StudioRouteComponentProps } from "@elsa-workflows/studio-sdk";
import { StudioPage, StudioResultState } from "@elsa-workflows/studio-ui";
import { OperationAuditPage } from "./OperationAuditPage";
import { JobsPage } from "./JobsPage";
import { JobDetail } from "./JobDetail";
import { ScheduleDetail, ScheduleEditor, SubmitPage } from "./Editors";
import { CapacityPage, CreateCapacityPage, HoldsPage, QueuesPage, SchedulesPage } from "./OperationsPages";
import { jobsCapability } from "./jobsApi";
import "./styles.css";

function ResourceRoute({ api, navigate, kind }: { api: ElsaStudioModuleApi; kind: "job" | "schedule" | "edit" } & StudioRouteComponentProps) {
  const location = useStudioRouteLocation(); let id: string;
  try {
    const parts = location.pathname.split("/"); id = decodeURIComponent(parts.at(kind === "edit" ? -2 : -1) ?? "");
    if (!id.trim() || id.length > 256) throw new Error();
  } catch { return <StudioPage><StudioResultState title="Invalid Jobs address" description="Return to Jobs and open a resource from its collection." /></StudioPage>; }
  return kind === "job" ? <JobDetail key={id} api={api} id={id} navigate={navigate} /> : kind === "schedule" ?
    <ScheduleDetail key={id} api={api} id={id} navigate={navigate} /> : <ScheduleEditor key={id} api={api} id={id} navigate={navigate} />;
}
export function register(api: ElsaStudioModuleApi) {
  api.featureAreas.add({ id: "jobs", title: "Jobs", description: "Durable jobs, saved results, recurring schedules and operational holds.",
    navGroup: "Workspace", ownedPaths: ["/jobs"], required: false, defaultEnabled: true, order: 35,
    nav: { title: "Jobs", path: "/jobs", icon: Layers, placement: "more", order: 35, requiresBackend: jobsCapability },
    routes: [
      { id: "jobs.submit", path: "/jobs/submit", label: "Submit job", requiresBackend: jobsCapability, component: ({ navigate }: StudioRouteComponentProps) => <SubmitPage api={api} navigate={navigate} /> },
      { id: "jobs.queues", path: "/jobs/queues", label: "Queues", requiresBackend: jobsCapability, component: ({ navigate }: StudioRouteComponentProps) => <QueuesPage api={api} navigate={navigate} /> },
      { id: "jobs.schedules.create", path: "/jobs/schedules/create", label: "Create schedule", requiresBackend: jobsCapability, component: ({ navigate }: StudioRouteComponentProps) => <ScheduleEditor api={api} navigate={navigate} /> },
      { id: "jobs.schedules.edit", path: "/jobs/schedules/:id/edit", label: "Edit schedule", requiresBackend: jobsCapability,
        workspace: { resourceKey: (url: URL) => `job-schedule:${url.pathname.split("/").at(-2)}` }, component: ({ navigate }: StudioRouteComponentProps) => <ResourceRoute api={api} navigate={navigate} kind="edit" /> },
      { id: "jobs.schedules.detail", path: "/jobs/schedules/:id", label: "Schedule", requiresBackend: jobsCapability,
        workspace: { resourceKey: (url: URL) => `job-schedule:${url.pathname.split("/").at(-1)}` }, component: ({ navigate }: StudioRouteComponentProps) => <ResourceRoute api={api} navigate={navigate} kind="schedule" /> },
      { id: "jobs.schedules", path: "/jobs/schedules", label: "Schedules", requiresBackend: jobsCapability, component: ({ navigate }: StudioRouteComponentProps) => <SchedulesPage api={api} navigate={navigate} /> },
      { id: "jobs.holds", path: "/jobs/holds", label: "Holds", requiresBackend: jobsCapability, component: ({ navigate }: StudioRouteComponentProps) => <HoldsPage api={api} navigate={navigate} /> },
      { id: "jobs.capacity.create", path: "/jobs/capacity/create", label: "Create capacity policy", requiresBackend: jobsCapability, component: ({ navigate }: StudioRouteComponentProps) => <CreateCapacityPage api={api} navigate={navigate} /> },
      { id: "jobs.capacity", path: "/jobs/capacity", label: "Capacity", requiresBackend: jobsCapability, component: ({ navigate }: StudioRouteComponentProps) => <CapacityPage api={api} navigate={navigate} /> },
      { id: "jobs.operations", path: "/jobs/operations", label: "Operation audit", requiresBackend: jobsCapability, component: ({ navigate }: StudioRouteComponentProps) => <OperationAuditPage api={api} navigate={navigate} /> },
      { id: "jobs.detail", path: "/jobs/:id", label: "Job", requiresBackend: jobsCapability,
        workspace: { resourceKey: (url: URL) => `job:${url.pathname.split("/").at(-1)}` }, component: ({ navigate }: StudioRouteComponentProps) => <ResourceRoute api={api} navigate={navigate} kind="job" /> },
      { id: "jobs", path: "/jobs", label: "Jobs", requiresBackend: jobsCapability, component: ({ navigate }: StudioRouteComponentProps) => <JobsPage api={api} navigate={navigate} /> }
    ] });
}
