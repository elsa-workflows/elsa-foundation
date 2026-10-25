import { useEffect, useMemo, useState } from "react";
import { createRoot } from "react-dom/client";
import type { StudioAiContributionApi, StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { ThemeProvider } from "../../src/apps/Elsa.Studio.Web/Client/src/app/components/ThemeProvider";
import { WorkflowInstanceDetailsWorkbench, WorkflowInstances } from "../../src/essentials/Elsa.Studio.Workflows/Client/src/workflow-editor/WorkflowInstances";
import type { IncidentStateSummary, WorkflowInstanceSummary } from "../../src/essentials/Elsa.Studio.Workflows/Client/src/workflowTypes";
import "../../src/apps/Elsa.Studio.Web/Client/src/app/styles.css";
import "../../src/essentials/Elsa.Studio.Workflows/Client/src/styles.css";

type FixtureRoute = { workflowExecutionId: string | null; returnTo: string | null };

const definitionCatalog = [
  { id: "billing-definition-current", name: "Billing workflow" },
  { id: "billing-definition-legacy", name: "Billing workflow" },
  { id: "definition-quarterly-regional-reconciliation-with-a-long-stable-identity", name: "Quarterly regional reconciliation and partner settlement" }
];

function createRuns(): WorkflowInstanceSummary[] {
  return Array.from({ length: 14 }, (_, offset) => {
    const index = offset + 1;
    const longIdentity = index === 14;
    const definitionId = index < 7 ? "billing-definition-current"
      : index < 13 ? "billing-definition-legacy"
        : definitionCatalog[2].id;
    const blocking = index === 1;
    const active = (index >= 1 && index <= 10) || index === 12;
    const inactiveIncident = index === 13 || index === 14;
    const runId = longIdentity
      ? "run-14-quarterly-reconciliation-2026-10-20-long-execution-reference"
      : `run-${String(index).padStart(2, "0")}`;
    const createdAt = new Date(Date.UTC(2026, 9, 20, 9, index)).toISOString();

    return {
      workflowExecutionId: runId,
      artifactId: longIdentity ? "artifact-quarterly-regional-reconciliation-release-2026-10-20" : `artifact-billing-${index % 2 ? "current" : "legacy"}`,
      artifactVersion: index < 13 ? "2.4.0" : "2026.10.20-regional.4",
      artifactHash: `sha256:golden-${String(index).padStart(2, "0")}`,
      definitionId,
      definitionVersionId: longIdentity ? "billing-definition-very-long-version-2026-10-20-regional-partner-settlement-v14" : `${definitionId}-v${index % 3 + 1}`,
      status: index <= 11 ? "Running" : index === 14 ? "Faulted" : "Completed",
      runKind: index < 13 ? "PublishedRun" : "BackgroundWeaverRun",
      correlationId: `corr-${String(index).padStart(2, "0")}`,
      subStatus: null,
      createdAt,
      startedAt: createdAt,
      updatedAt: index <= 11 ? createdAt : null,
      completedAt: index <= 11 ? null : new Date(Date.UTC(2026, 9, 20, 9, index, 18)).toISOString(),
      activityCount: 8 + index,
      incidentCount: active || inactiveIncident ? 1 : 0,
      activeIncidentCount: active ? 1 : 0,
      blockingIncidentCount: blocking ? 1 : 0
    };
  });
}

function runInspectionFacts(workflowExecutionId: string) {
  if (workflowExecutionId === "run-13") return {};
  if (workflowExecutionId === "run-01") return { persistenceMode: "Durable", inspectionGranularity: "activity-level" };
  if (workflowExecutionId === "run-12") return { persistenceMode: "Balanced", inspectionGranularity: "boundary-level" };
  return { persistenceMode: "Ephemeral", inspectionGranularity: "boundary-level" };
}

function createIncidents(instance: WorkflowInstanceSummary): IncidentStateSummary[] {
  const incidentCount = instance.incidentCount ?? 0;
  const activeIncidentCount = instance.activeIncidentCount ?? 0;
  const blockingIncidentCount = instance.blockingIncidentCount ?? 0;

  return Array.from({ length: incidentCount }, (_, offset) => {
    const active = offset < activeIncidentCount;
    const status = active ? "Active" : instance.status === "Faulted" ? "Suppressed" : "Resolved";
    return {
      incidentId: `incident-${instance.workflowExecutionId}-${offset + 1}`,
      workflowExecutionId: instance.workflowExecutionId,
      severity: "Error",
      status,
      resolutionAction: active ? "None" : "Resolved",
      failureType: "ActivityFailure",
      message: `Fixture incident for ${instance.workflowExecutionId}`,
      createdAt: instance.startedAt ?? instance.createdAt,
      resolvedAt: active ? null : instance.completedAt ?? instance.updatedAt ?? instance.createdAt,
      isBlocking: active && offset < blockingIncidentCount,
      metadata: {}
    };
  });
}

function readRoute(): FixtureRoute {
  const match = window.location.pathname.match(/^\/workflows\/instances\/([^/]+)\/?$/);
  const parameters = new URLSearchParams(window.location.search);
  return {
    workflowExecutionId: match ? decodeURIComponent(match[1]) : parameters.get("directRun"),
    returnTo: parameters.get("returnTo")
  };
}

function Fixture() {
  const [route, setRoute] = useState(readRoute);
  const context = useMemo(() => {
    let runs = createRuns();
    let failInitialRunsRequest = new URLSearchParams(window.location.search).get("fixtureError") === "1";
    const healthSupported = new URLSearchParams(window.location.search).get("health") !== "unsupported";
    const delayRuns = new URLSearchParams(window.location.search).get("fixtureDelay") === "1";
    const runtimeLinks = [
      { rel: "workflow-instances-page", href: "runtime/workflows/instances/page" },
      { rel: "workflow-instance", href: "runtime/workflows/instances/{workflowExecutionId}", templated: true },
      { rel: "workflow-executable", href: "runtime/workflows/executables/{artifactId}", templated: true },
      { rel: "workflow-instance-delete", href: "runtime/workflows/instances/{workflowExecutionId}", templated: true },
      ...(healthSupported ? [{ rel: "workflow-instances-health-filter", href: "runtime/workflows/instances/health" }] : [])
    ];
    const definitionsPage = (path: string) => {
      const parameters = new URL(path, window.location.origin).searchParams;
      const query = parameters.get("searchTerm")?.trim().toLocaleLowerCase() ?? "";
      const matches = definitionCatalog.filter(definition => definition.name.toLocaleLowerCase().includes(query));
      return { items: matches, nextContinuationToken: null };
    };
    const fixtureContext = {
      baseUrl: "http://golden-runs.fixture/",
      http: {
        getJson: async (path: string) => {
          if (path === "/capabilities") return { capabilities: [
            { id: "elsa.api.runtime", contractVersion: "1", links: runtimeLinks },
            { id: "elsa.api.workflow-design", contractVersion: "1", links: [
              { rel: "workflow-definitions", href: "design/workflows/definitions" },
              { rel: "workflow-definitions-page", href: "design/workflows/definitions/page" }
            ] },
            { id: "elsa.api.activity-design", contractVersion: "1", links: [
              { rel: "activity-catalog", href: "design/activities" }
            ] }
          ] };

          if (path.startsWith("/runtime/workflows/instances/page") || path.startsWith("/runtime/workflows/instances/health")) {
            (window as Window & { __goldenRunRequests?: string[] }).__goldenRunRequests ??= [];
            (window as Window & { __goldenRunRequests?: string[] }).__goldenRunRequests!.push(path);
            if (delayRuns) await new Promise(resolve => window.setTimeout(resolve, 1_200));
            if (failInitialRunsRequest) {
              failInitialRunsRequest = false;
              throw new Error("Fixture runtime request failed");
            }
            const parameters = new URL(path, window.location.origin).searchParams;
            const matching = runs.filter(run => {
              if (parameters.has("status") && run.status !== parameters.get("status")) return false;
              if (parameters.has("runKind") && run.runKind !== parameters.get("runKind")) return false;
              if (parameters.has("definitionId") && run.definitionId !== parameters.get("definitionId")) return false;
              if (parameters.has("workflowExecutionId") && run.workflowExecutionId !== parameters.get("workflowExecutionId")) return false;
              if (parameters.has("artifactId") && run.artifactId !== parameters.get("artifactId")) return false;
              if (parameters.has("correlationId") && run.correlationId !== parameters.get("correlationId")) return false;
              const from = parameters.get("from");
              if (from && Date.parse(run.startedAt!) < Date.parse(from)) return false;
              const to = parameters.get("to");
              if (to && Date.parse(run.startedAt!) > Date.parse(to)) return false;
              const health = parameters.get("incidentHealth");
              if (health === "active" && !(run.activeIncidentCount! > 0)) return false;
              if (health === "blocking" && !(run.blockingIncidentCount! > 0)) return false;
              if (health === "none" && !(run.activeIncidentCount === 0)) return false;
              return true;
            });
            const pageSize = Number(parameters.get("take") ?? 10);
            const secondPage = parameters.get("cursor") === "golden-runs-page-2";
            const offset = secondPage ? pageSize : 0;
            const items = matching.slice(offset, offset + pageSize);
            const hasNext = offset + items.length < matching.length;
            return {
              items,
              previousCursor: secondPage ? "golden-runs-page-1" : null,
              nextCursor: hasNext ? "golden-runs-page-2" : null,
              hasPrevious: secondPage,
              hasNext,
              count: items.length,
              totalCount: matching.length
            };
          }

          if (path.startsWith("/runtime/workflows/instances/")) {
            const workflowExecutionId = decodeURIComponent(path.split("/").at(-1)!);
            const instance = runs.find(run => run.workflowExecutionId === workflowExecutionId);
            if (!instance) throw new Error(`Unknown fixture run ${workflowExecutionId}`);
            return { instance, activities: [], incidents: createIncidents(instance), ...runInspectionFacts(workflowExecutionId) };
          }

          if (path.startsWith("/runtime/workflows/executables/")) {
            const artifactId = decodeURIComponent(path.split("/").at(-1)!);
            return {
              artifactId,
              artifactHash: "sha256:golden-executable",
              createdAt: "2026-10-20T09:00:00Z",
              rootActivityType: "Elsa.Sequence",
              rootActivityVersion: "1",
              nodeCount: 1,
              resumeTargetCount: 0,
              rootActivity: {
                executableNodeId: "golden-root",
                authoredActivityId: "golden-root",
                activityType: "Elsa.Sequence",
                activityTypeVersion: "1",
                structureKind: "elsa.sequence.structure",
                inputBindings: [],
                childSlots: []
              },
              chosenReference: null,
              references: []
            };
          }

          if (path.startsWith("/design/workflows/definitions/page")) return definitionsPage(path);
          if (path.startsWith("/design/workflows/definitions/")) {
            const id = decodeURIComponent(path.split("/").at(-1)!);
            const definition = definitionCatalog.find(item => item.id === id);
            if (!definition) throw new Error(`Unknown fixture workflow ${id}`);
            return { definition: { ...definition, versions: [], latestVersion: "1", latestVersionId: `${id}-v1` } };
          }
          if (path.startsWith("/design/activities")) return { activities: [] };
          throw new Error(`Unexpected fixture read ${path}`);
        },
        deleteJson: async (path: string) => {
          const id = decodeURIComponent(path.split("/").at(-1)!);
          runs = runs.filter(run => run.workflowExecutionId !== id);
          return { workflowExecutionId: id, status: "Deleted" };
        }
      }
    };
    return fixtureContext as unknown as StudioEndpointContext;
  }, []);

  useEffect(() => {
    const handlePopState = () => setRoute(readRoute());
    window.addEventListener("popstate", handlePopState);
    return () => window.removeEventListener("popstate", handlePopState);
  }, []);

  const navigate = (path: string) => {
    window.history.pushState({}, "", path);
    const nextRoute = readRoute();
    setRoute(current => current.workflowExecutionId === nextRoute.workflowExecutionId ? current : nextRoute);
  };

  const ai = { promptActions: { list: () => [] } } as unknown as StudioAiContributionApi;
  const page = <ThemeProvider><main className={`wf-page${route.workflowExecutionId ? " wf-page--run-workbench" : ""} golden-runs-page`}>
    <header className="wf-page-header">
      <div><span className="wf-kicker">Workflow management</span><h2>{route.workflowExecutionId ? "Run" : "Runs"}</h2></div>
      {route.workflowExecutionId ? <div className="wf-page-context"><span>Workflow Instance ID</span><code>{route.workflowExecutionId}</code></div> : null}
    </header>
    {route.workflowExecutionId ? <WorkflowInstanceDetailsWorkbench
      context={context}
      ai={ai}
      workflowExecutionId={route.workflowExecutionId}
      initialReturnTo={route.returnTo}
      navigate={navigate}
    /> : <WorkflowInstances context={context} navigate={navigate} />}
  </main></ThemeProvider>;

  return new URLSearchParams(window.location.search).get("workbench-shell") === "1"
    ? <div className="content">{page}</div>
    : page;
}

createRoot(document.getElementById("root")!).render(<Fixture />);
