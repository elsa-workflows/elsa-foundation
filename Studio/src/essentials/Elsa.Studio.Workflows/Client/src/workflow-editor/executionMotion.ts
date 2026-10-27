import { useEffect, useRef, useState } from "react";
import type { Edge, Node } from "@xyflow/react";
import type { ActivityExecutionBrowseEntry } from "../api/runtime";
import type { WorkflowEdgeData, WorkflowNodeData } from "../workflowAdapter";
import type { ActivityExecutionStateSummary } from "../workflowTypes";

const maxVisibleMotionNodes = 512;
export const maxExecutionPulses = 48;
export const executionPulseDuration = 900;
interface MotionNode { nodeId: string; occurrenceId: string; signature: string }
interface MotionTransition { edgeId: string; targetNodeId: string; targetOccurrenceId: string }
interface MotionFrame { nodes: MotionNode[]; transitions: MotionTransition[] }
interface MotionInput {
  workflowExecutionId: string;
  mode: string | undefined;
  nodes: Node<WorkflowNodeData>[];
  edges: Edge<WorkflowEdgeData>[];
  observations: ActivityExecutionBrowseEntry[];
}
function parentOf(execution: ActivityExecutionStateSummary) {
  return execution.parentActivityExecutionId ?? execution.provenance?.parentActivityExecutionId;
}
function isFirstAttempt(entry: ActivityExecutionBrowseEntry) {
  // Absence on an older host does not establish attempt fidelity. Current browsing projects null for no retry lineage.
  return entry.attempt === null || !!entry.attempt && entry.attempt.attemptNumber === 1
    && entry.attempt.firstAttemptActivityExecutionId === entry.execution.activityExecutionId && !entry.attempt.previousAttemptActivityExecutionId;
}

/** A conservative mapping of committed exact occurrences to visible nodes and supported connections. */
export function confirmedExecutionMotionFrame({ workflowExecutionId, mode, nodes, edges, observations }: MotionInput): MotionFrame {
  const occurrences = new Map<string, ActivityExecutionBrowseEntry>();
  const duplicates = new Set<string>();
  for (const entry of observations) {
    const id = entry.execution.activityExecutionId;
    if (occurrences.has(id)) duplicates.add(id);
    if (entry.execution.workflowExecutionId === workflowExecutionId) occurrences.set(id, entry);
  }
  const visibleOccurrences = new Map<string, number>();
  for (const node of nodes) if (node.data.runtime?.activityExecutionId) {
    const id = node.data.runtime.activityExecutionId;
    visibleOccurrences.set(id, (visibleOccurrences.get(id) ?? 0) + 1);
  }
  const visible = new Map<string, { nodeId: string; entry: ActivityExecutionBrowseEntry }>();
  const frame: MotionFrame = { nodes: [], transitions: [] };
  for (const node of nodes.slice(0, maxVisibleMotionNodes)) {
    const id = node.data.runtime?.activityExecutionId;
    const entry = id && !duplicates.has(id) && visibleOccurrences.get(id) === 1 ? occurrences.get(id) : undefined;
    const runtimeId = node.data.runtimeNodeId ?? node.id;
    if (!entry || entry.execution.executableNodeId !== runtimeId && entry.execution.authoredActivityId !== runtimeId) continue;
    const execution = entry.execution;
    visible.set(execution.activityExecutionId, { nodeId: node.id, entry });
    frame.nodes.push({ nodeId: node.id, occurrenceId: execution.activityExecutionId,
      signature: JSON.stringify([execution.activityExecutionId, execution.status, execution.subStatus, entry.attempt?.attemptNumber]) });
  }
  if (mode !== "sequence" && mode !== "flowchart") return frame;
  const candidatesByPair = new Map<string, Edge<WorkflowEdgeData>[]>();
  for (const edge of edges) {
    const key = JSON.stringify([edge.source, edge.target]);
    candidatesByPair.set(key, [...candidatesByPair.get(key) ?? [], edge]);
  }
  const nodePositions = new Map(nodes.map((node, index) => [node.id, index]));
  for (const target of visible.values()) {
    const execution = target.entry.execution;
    const scheduler = execution.schedulingActivityExecutionId ?? execution.provenance?.schedulingActivityExecutionId;
    const source = scheduler ? visible.get(scheduler) : undefined;
    if (!source || source.entry.execution.status !== "Completed" || !isFirstAttempt(source.entry) || !isFirstAttempt(target.entry)
      || !parentOf(execution) || parentOf(execution) !== parentOf(source.entry.execution)) continue;
    const sourceBranch = source.entry.execution.branchId ?? source.entry.execution.provenance?.branchId ?? null;
    const targetBranch = execution.branchId ?? execution.provenance?.branchId ?? null;
    if (sourceBranch !== targetBranch) continue;
    const cause = execution.provenance?.schedulingCause;
    if (cause !== (mode === "sequence" ? "ActivityCompletion" : "continuation")) continue;
    const candidates = candidatesByPair.get(JSON.stringify([source.nodeId, target.nodeId])) ?? [];
    if (candidates.length !== 1) continue;
    if (mode === "sequence") {
      const sourceIndex = nodePositions.get(source.nodeId) ?? -1;
      if (sourceIndex < 0 || nodes[sourceIndex + 1]?.id !== target.nodeId) continue;
    } else if (!source.entry.execution.outcomeNames?.includes(candidates[0].sourceHandle ?? "Done")) continue;
    frame.transitions.push({ edgeId: candidates[0].id, targetNodeId: target.nodeId, targetOccurrenceId: execution.activityExecutionId });
  }
  return frame;
}

type MotionState = { identity: string; token: number; nodePulses: Record<string, string>; edgePulses: Record<string, string>; incomingPulses: Record<string, string> };
const emptyMotion: MotionState = { identity: "", token: 0, nodePulses: {}, edgePulses: {}, incomingPulses: {} };

/** Pulses newly confirmed updates; it is neither a history queue nor a clock for workflow execution. */
export function useConfirmedExecutionMotion({ identity, enabled, ready, animateInitial = false, workflowExecutionId, ...input }: MotionInput & {
  identity: string; enabled: boolean; ready: boolean; animateInitial?: boolean;
}) {
  const frame = JSON.stringify(confirmedExecutionMotionFrame({ ...input, workflowExecutionId }));
  const tracker = useRef<{ identity: string; signatures: Map<string, string>; occurrences: Set<string>; eligibleTargets: Set<string> } | null>(null);
  const [state, setState] = useState(emptyMotion);
  const timer = useRef<ReturnType<typeof setTimeout> | undefined>(undefined);
  const sequence = useRef(0);
  const initialRun = useRef<string | null>(null);
  useEffect(() => {
    if (!enabled || tracker.current?.identity !== identity) {
      tracker.current = null;
      clearTimeout(timer.current);
      setState(previous => previous.identity ? emptyMotion : previous);
    }
    if (!enabled || !ready) return;
    const current = JSON.parse(frame) as MotionFrame;
    const previous = tracker.current;
    const firstBatch = animateInitial && initialRun.current !== workflowExecutionId;
    initialRun.current = workflowExecutionId;
    const eligibleTargets = new Set([...(previous?.eligibleTargets ?? [])].filter(id => current.nodes.some(node => node.occurrenceId === id)));
    const changedNodes = current.nodes.filter(node => previous ? previous.signatures.get(node.nodeId) !== node.signature : firstBatch);
    for (const node of current.nodes) if (previous ? !previous.occurrences.has(node.occurrenceId) : firstBatch) eligibleTargets.add(node.occurrenceId);
    const changedEdges = current.transitions.filter(edge => eligibleTargets.has(edge.targetOccurrenceId));
    // Consume the whole confirmed batch, including entries beyond the visual cap; never queue a later replay.
    for (const edge of changedEdges) eligibleTargets.delete(edge.targetOccurrenceId);
    tracker.current = { identity, signatures: new Map(current.nodes.map(node => [node.nodeId, node.signature])),
      occurrences: new Set(current.nodes.map(node => node.occurrenceId)), eligibleTargets };
    if (!changedNodes.length && !changedEdges.length) return;
    const token = ++sequence.current;
    const pulse = `${identity}:${token}`;
    clearTimeout(timer.current);
    setState({ identity, token,
      nodePulses: Object.fromEntries(changedNodes.slice(0, maxExecutionPulses).map(node => [node.nodeId, pulse])),
      edgePulses: Object.fromEntries(changedEdges.slice(0, maxExecutionPulses).map(edge => [edge.edgeId, pulse])),
      incomingPulses: Object.fromEntries(changedEdges.slice(0, maxExecutionPulses).map(edge => [edge.targetNodeId, pulse])) });
    timer.current = setTimeout(() => setState(value => value.identity === identity && value.token === token ? emptyMotion : value), executionPulseDuration);
  }, [animateInitial, enabled, frame, identity, ready, workflowExecutionId]);
  useEffect(() => () => clearTimeout(timer.current), []);
  return state.identity === identity && enabled ? state : emptyMotion;
}

export function decorateExecutionMotion(nodes: Node<WorkflowNodeData>[], edges: Edge<WorkflowEdgeData>[], motion: ReturnType<typeof useConfirmedExecutionMotion>) {
  const decoratedNodes = nodes.map(node => node.data.executionPulse === motion.nodePulses[node.id] && node.data.incomingExecutionPulse === motion.incomingPulses[node.id] ? node
    : { ...node, data: { ...node.data, executionPulse: motion.nodePulses[node.id], incomingExecutionPulse: motion.incomingPulses[node.id] } });
  const decoratedEdges = edges.map(edge => edge.data?.executionPulse === motion.edgePulses[edge.id] ? edge
    : { ...edge, data: { ...edge.data, executionPulse: motion.edgePulses[edge.id] } });
  return { nodes: decoratedNodes.every((node, index) => node === nodes[index]) ? nodes : decoratedNodes,
    edges: decoratedEdges.every((edge, index) => edge === edges[index]) ? edges : decoratedEdges };
}
