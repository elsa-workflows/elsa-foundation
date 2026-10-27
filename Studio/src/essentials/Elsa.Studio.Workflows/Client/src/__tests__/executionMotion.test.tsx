import React from "react";
import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import type { Node, Edge } from "@xyflow/react";
import { afterEach, expect, it, vi } from "vitest";
import type { ActivityExecutionBrowseEntry } from "../api/runtime";
import type { WorkflowNodeData, WorkflowEdgeData } from "../workflowAdapter";
import { confirmedExecutionMotionFrame, executionPulseDuration, maxExecutionPulses, useConfirmedExecutionMotion } from "../workflow-editor/executionMotion";

function occurrence(nodeId: string, status = "Completed", scheduler?: string): ActivityExecutionBrowseEntry {
  return { execution: { activityExecutionId: `occurrence-${nodeId}`, workflowExecutionId: "run", executableNodeId: nodeId,
    authoredActivityId: nodeId, executionSequence: 1, activityType: "Elsa.WriteLine", activityTypeVersion: "1", status, scheduledAt: "2026-10-27T00:00:00Z", metadata: {},
    outcomeNames: status === "Completed" ? ["Done"] : [], provenance: { parentActivityExecutionId: "parent", schedulingActivityExecutionId: scheduler,
      schedulingCause: "continuation" } }, position: 1, previousActivityExecutionId: null, nextActivityExecutionId: null, attempt: null };
}
function node(id: string): Node<WorkflowNodeData> {
  return { id, position: { x: 0, y: 0 }, data: { label: id, activityVersionId: "1", childSlots: [], acceptsInbound: true, sourcePorts: [],
    runtime: { activityExecutionId: `occurrence-${id}`, faultCount: 0, incidentCount: 0, hasBlockingIncident: false, selected: false } } };
}
const edge: Edge<WorkflowEdgeData> = { id: "a-b", source: "a", target: "b", sourceHandle: "Done" };
function input() { return { workflowExecutionId: "run", mode: "flowchart", nodes: [node("a"), node("b")], edges: [edge],
  observations: [occurrence("a"), occurrence("b", "Running", "occurrence-a")] }; }

it("requires exact recorded causation, one parent, matching outcome and a unique connection", () => {
  const value = input();
  expect(confirmedExecutionMotionFrame(value).transitions).toEqual([{ edgeId: "a-b", targetNodeId: "b", targetOccurrenceId: "occurrence-b" }]);
  value.observations[1].execution.provenance!.schedulingActivityExecutionId = undefined;
  expect(confirmedExecutionMotionFrame(value).transitions).toEqual([]);
});

it.each(["join", "policy:inclusive", "OperatorRescheduleActivity", "unknown"])("does not turn %s scheduling into a traversed connection", cause => {
  const value = input(); value.observations[1].execution.provenance!.schedulingCause = cause;
  expect(confirmedExecutionMotionFrame(value).transitions).toEqual([]);
  expect(confirmedExecutionMotionFrame(value).nodes).toHaveLength(2);
});

it.each(["parent", "branch", "outcome", "ambiguous", "foreign", "duplicate", "duplicate-binding", "retry", "missing-attempt", "unsupported"])("falls back to node cues for %s evidence", scenario => {
  const value = input();
  if (scenario === "parent") value.observations[1].execution.provenance!.parentActivityExecutionId = "other-parent";
  if (scenario === "branch") value.observations[1].execution.provenance!.branchId = "other-branch";
  if (scenario === "duplicate-binding") value.nodes.push({ ...node("b"), id: "another-visible-binding" });
  if (scenario === "outcome") value.observations[0].execution.outcomeNames = ["Skipped"];
  if (scenario === "ambiguous") value.edges.push({ ...edge, id: "another-a-b" });
  if (scenario === "foreign") value.observations[1].execution.workflowExecutionId = "foreign-run";
  if (scenario === "duplicate") value.observations.push(value.observations[1]);
  if (scenario === "retry") value.observations[1].attempt = { attemptNumber: 2, firstAttemptActivityExecutionId: "earlier", previousAttemptActivityExecutionId: "earlier" };
  if (scenario === "missing-attempt") delete value.observations[1].attempt;
  if (scenario === "unsupported") value.mode = "bpmn";
  expect(confirmedExecutionMotionFrame(value).transitions).toEqual([]);
});

it("accepts exact causal loop occurrences without replacing them by a global latest execution", () => {
  const value = input();
  value.observations[0].execution.provenance!.iterationId = "loop-3";
  value.observations[1].execution.provenance!.iterationId = "loop-4";
  expect(confirmedExecutionMotionFrame(value).transitions).toHaveLength(1);
  value.observations[1].execution.provenance!.schedulingActivityExecutionId = "occurrence-a-from-another-iteration";
  expect(confirmedExecutionMotionFrame(value).transitions).toEqual([]);
});

it("only maps causal adjacent Sequence steps with the ordinary child scheduling cause", () => {
  const value = input(); value.mode = "sequence";
  value.observations[1].execution.provenance!.schedulingCause = "ActivityCompletion";
  expect(confirmedExecutionMotionFrame(value).transitions).toHaveLength(1);
  value.nodes.splice(1, 0, node("intermediate"));
  expect(confirmedExecutionMotionFrame(value).transitions).toEqual([]);
});

let root: Root | undefined;
let strictMode = false;
let container: HTMLDivElement;
let props: Parameters<typeof useConfirmedExecutionMotion>[0];
function Harness() {
  const motion = useConfirmedExecutionMotion(props);
  return <output>{JSON.stringify({ nodes: Object.keys(motion.nodePulses), edges: Object.keys(motion.edgePulses) })}</output>;
}
function render(overrides: Partial<typeof props> = {}) {
  props = { ...props, ...overrides };
  if (!root) { container = document.createElement("div"); document.body.appendChild(container); root = createRoot(container); }
  flushSync(() => root!.render(strictMode ? <React.StrictMode><Harness /></React.StrictMode> : <Harness />));
}
async function settled() { await new Promise(resolve => setTimeout(resolve, 0)); }
function shown() { return JSON.parse(container.textContent!) as { nodes: string[]; edges: string[] }; }
afterEach(() => { if (root) { flushSync(() => root!.unmount()); root = undefined; container.remove(); } strictMode = false; vi.useRealTimers(); });

it("baselines an existing run, pulses a subsequently confirmed route once, and never replays it on recovery", async () => {
  props = { ...input(), identity: "scope", enabled: true, ready: true, observations: [occurrence("a")] };
  render(); await settled(); expect(shown()).toEqual({ nodes: [], edges: [] });
  render({ observations: input().observations }); await settled();
  expect(shown()).toEqual({ nodes: ["b"], edges: ["a-b"] });
  render({ enabled: false }); await settled(); expect(shown()).toEqual({ nodes: [], edges: [] });
  render({ enabled: true }); await settled(); expect(shown()).toEqual({ nodes: [], edges: [] });
});

it("shows a fresh local run's fast first batch together, but baselines later scope changes", async () => {
  props = { ...input(), identity: "scope", enabled: true, ready: true, animateInitial: true };
  render(); await settled(); expect(shown()).toEqual({ nodes: ["a", "b"], edges: ["a-b"] });
  render({ identity: "another-scope" }); await settled(); expect(shown()).toEqual({ nodes: [], edges: [] });
});

it("bounds a burst and expires the presentation without queuing the skipped history", async () => {
  const nodes = Array.from({ length: 1000 }, (_, index) => node(`n${index}`));
  props = { ...input(), identity: "large", nodes, edges: [], observations: nodes.map(item => occurrence(item.id)),
    enabled: true, ready: true, animateInitial: true };
  vi.useFakeTimers();
  render(); await vi.waitFor(() => expect(shown().nodes).toHaveLength(maxExecutionPulses));
  flushSync(() => vi.advanceTimersByTime(executionPulseDuration));
  expect(shown()).toEqual({ nodes: [], edges: [] });
  render(); expect(shown()).toEqual({ nodes: [], edges: [] });
});

it("expires a fresh pulse after the StrictMode effect cleanup and remount", async () => {
  strictMode = true; vi.useFakeTimers();
  props = { ...input(), identity: "strict-scope", enabled: true, ready: true, animateInitial: true };
  render(); await vi.waitFor(() => expect(shown().nodes).toHaveLength(2));
  flushSync(() => vi.advanceTimersByTime(executionPulseDuration));
  expect(shown()).toEqual({ nodes: [], edges: [] });
});
