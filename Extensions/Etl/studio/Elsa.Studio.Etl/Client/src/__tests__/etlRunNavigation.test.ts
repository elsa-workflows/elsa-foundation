import { expect, it } from "vitest";
import type { StudioWorkflowRunRelatedLinkContext } from "@elsa-workflows/studio-sdk";
import { buildEtlRunPath, etlNativeRunLinks, readEtlRunLocation } from "../etlRunNavigation";

const origin = "https://studio.example";
const back = "/workflows/instances/native-run?backend=backend-1&selected=activity-1&tab=activity";
function context(): StudioWorkflowRunRelatedLinkContext {
  return { workflowExecutionId: "native-run", activityExecutionId: "activity-1", executableNodeId: "node-1", returnTo: back,
    stateView: { kind: "etl.run-reference", version: 1, data: { runId: "etl-run-1", resultAddress: {
      workflowExecutionId: "native-run", activityExecutionId: "activity-1", nodeId: "node-1", registrationId: "registration-1", resumeTargetId: "resume-1"
    } } } };
}

it("links the exact projected ETL run and preserves the native return occurrence/backend", () => {
  const links = etlNativeRunLinks(context(), "backend-1"); expect(links).toHaveLength(1);
  expect(links[0].label).toBe("Open ETL run");
  expect(readEtlRunLocation(origin + links[0].path, "backend-1")).toEqual({ runId: "etl-run-1", returnTo: back, error: null });
});

it.each(["kind", "version", "workflow", "activity", "node", "registration", "run", "private-fields", "missing-address", "missing-backend"])("does not fabricate a link from an unsupported or inconsistent reference: %s", invalid => {
  const value = context(), data = value.stateView.data as { runId: string; resultAddress: Record<string, unknown> };
  if (invalid === "kind") value.stateView.kind = "private-state";
  if (invalid === "version") value.stateView.version = 2;
  if (invalid === "workflow") data.resultAddress.workflowExecutionId = "other";
  if (invalid === "activity") data.resultAddress.activityExecutionId = "other";
  if (invalid === "node") data.resultAddress.nodeId = "other";
  if (invalid === "registration") data.resultAddress.registrationId = " ";
  if (invalid === "run") data.runId = "";
  if (invalid === "private-fields") Object.assign(data, { capturedValues: { private: "must not become a link" } });
  if (invalid === "missing-address") delete data.resultAddress.resumeTargetId;
  expect(etlNativeRunLinks(value, invalid === "missing-backend" ? undefined : "backend-1")).toEqual([]);
});

it("retains opaque run and backend identifiers through URL encoding", () => {
  const path = buildEtlRunPath("run/with ?#&", "backend/with ?#&");
  expect(readEtlRunLocation(origin + path, "backend/with ?#&")).toEqual({ runId: "run/with ?#&", returnTo: null, error: null });
});

it.each(["wrong", "missing", "duplicate", "unavailable"])("refuses ambiguous or foreign backend selection: %s", backend => {
  const params = new URLSearchParams({ runId: "etl-run-1" });
  if (backend !== "missing") params.append("backend", backend === "wrong" ? "other" : "backend-1");
  if (backend === "duplicate") params.append("backend", "backend-1");
  const result = readEtlRunLocation(origin + "/etl/runs?" + params, backend === "unavailable" ? undefined : "backend-1");
  expect(result.runId).toBeNull(); expect(result.error).toContain("backend");
});

it.each(["", " ", "etl-run-1&runId=other"])("refuses blank or ambiguous run selection: %s", run => {
  const query = run.includes("&") ? `backend=backend-1&runId=${run}` : new URLSearchParams({ backend: "backend-1", runId: run });
  expect(readEtlRunLocation(`${origin}/etl/runs?${query}`, "backend-1").runId).toBeNull();
});

it.each(["https://other.example/workflows/instances/native-run?backend=backend-1", "//other.example/path", "/workflows/instances/native-run?backend=other",
  "/workflows/instances/native-run?backend=backend-1&backend=backend-1", "/etl/runs?backend=backend-1", "/\\other.example/path"])("does not retain an unsafe or unrelated return route: %s", returnTo => {
  const result = readEtlRunLocation(origin + buildEtlRunPath("etl-run-1", "backend-1", returnTo), "backend-1");
  expect(result.runId).toBe("etl-run-1"); expect(result.returnTo).toBeNull();
});
