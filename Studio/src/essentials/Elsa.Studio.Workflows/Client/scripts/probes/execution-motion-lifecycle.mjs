import assert from "node:assert/strict";
import React from "react";
import { JSDOM } from "jsdom";

// An isolated Node 22 process loads the actual hook with development React, outside Vite's production define.
const dom = new JSDOM("<!doctype html><html><body></body></html>", { url: "https://studio.example.test/" });
Object.assign(globalThis, { window: dom.window, document: dom.window.document, HTMLElement: dom.window.HTMLElement });
Object.defineProperty(globalThis, "navigator", { configurable: true, value: dom.window.navigator });
globalThis.IS_REACT_ACT_ENVIRONMENT = true;
const { createRoot } = await import("react-dom/client");
const { executionPulseDuration, useConfirmedExecutionMotion } = await import("../../src/workflow-editor/executionMotion.ts");
assert.equal(typeof React.act, "function", "The probe must use development React");

function Probe() {
  const motion = useConfirmedExecutionMotion({ identity: "scope", observationRevision: 1, confirmedRevision: 1, workflowExecutionId: "run", mode: "flowchart", enabled: true, ready: true, animateInitial: true,
    nodes: [{ id: "a", position: { x: 0, y: 0 }, data: { label: "A", activityVersionId: "1", childSlots: [], sourcePorts: [], acceptsInbound: true,
      runtime: { activityExecutionId: "occurrence-a", faultCount: 0, incidentCount: 0, selected: false, hasBlockingIncident: false } } }], edges: [],
    observations: [{ execution: { activityExecutionId: "occurrence-a", workflowExecutionId: "run", executableNodeId: "a", authoredActivityId: "a",
      activityType: "Elsa.WriteLine", activityTypeVersion: "1", status: "Completed", scheduledAt: "2026-10-27T00:00:00Z", executionSequence: 1, metadata: {} },
      position: 1, previousActivityExecutionId: null, nextActivityExecutionId: null, attempt: null }] });
  return React.createElement("output", null, Object.keys(motion.nodePulses).join(","));
}
const container = dom.window.document.createElement("div"); dom.window.document.body.appendChild(container);
const root = createRoot(container);
const render = revision => React.act(async () => root.render(React.createElement(React.StrictMode, null, React.createElement(Probe, { revision }))));
const originalNow = Date.now;
try {
  await render(0);
  assert.equal(container.textContent, "a", "The fresh dispatch must emit a pulse before expiry");
  if (process.argv[2] === "strict-remount") {
    await React.act(async () => { await new Promise(resolve => setTimeout(resolve, executionPulseDuration + 100)); });
  } else {
    // Move the presentation clock without delivering the queued timer, as with a delayed foreground render.
    Date.now = () => originalNow() + executionPulseDuration + 100;
    await render(1);
  }
  assert.equal(container.textContent, "", "Expired pulse state must not survive or replay on presentation remount");
  console.log(JSON.stringify({ mode: process.argv[2], expired: true }));
} finally {
  Date.now = originalNow;
  await React.act(async () => root.unmount());
  dom.window.close();
}
