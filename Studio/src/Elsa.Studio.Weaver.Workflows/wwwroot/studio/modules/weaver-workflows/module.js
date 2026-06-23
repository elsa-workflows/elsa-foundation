var f = { exports: {} }, l = {};
var u;
function v() {
  if (u) return l;
  u = 1;
  var e = /* @__PURE__ */ Symbol.for("react.transitional.element"), t = /* @__PURE__ */ Symbol.for("react.fragment");
  function a(h, r, i) {
    var c = null;
    if (i !== void 0 && (c = "" + i), r.key !== void 0 && (c = "" + r.key), "key" in r) {
      i = {};
      for (var w in r)
        w !== "key" && (i[w] = r[w]);
    } else i = r;
    return r = i.ref, {
      $$typeof: e,
      type: h,
      key: c,
      ref: r !== void 0 ? r : null,
      props: i
    };
  }
  return l.Fragment = t, l.jsx = a, l.jsxs = a, l;
}
var k;
function g() {
  return k || (k = 1, f.exports = v()), f.exports;
}
var n = g();
const o = "Elsa.Studio.Weaver.Workflows";
function y(e) {
  e.ai.contextProviders.add({
    id: "weaver.workflows.definition-context",
    kind: "workflow-definition",
    label: "Workflow definition",
    description: "Attaches workflow definition metadata, draft state, versions, and validation diagnostics.",
    moduleId: o,
    createAttachment: (t) => p("workflow-definition", t)
  }), e.ai.contextProviders.add({
    id: "weaver.workflows.executable-context",
    kind: "workflow-executable",
    label: "Workflow executable",
    description: "Attaches runtime executable identity, source, root activity, and resume target metadata.",
    moduleId: o,
    createAttachment: (t) => p("workflow-executable", t)
  }), e.ai.contextProviders.add({
    id: "weaver.workflows.instance-context",
    kind: "workflow-instance",
    label: "Workflow instance",
    description: "Attaches workflow instance state, incidents, bookmarks, and recent execution history when available.",
    moduleId: o,
    createAttachment: (t) => p("workflow-instance", t)
  }), e.ai.promptActions.add({
    id: "weaver.workflows.suggest-create-metadata",
    label: "Suggest name and description",
    description: "Ask Weaver to propose a workflow display name and description for the draft being created.",
    moduleId: o,
    placement: "field-adornment",
    contextKind: "workflow-create-draft",
    createPrompt: (t) => ({
      message: [
        "Suggest a concise Elsa workflow display name and one sentence description for this new workflow.",
        "Return only the proposed name and description.",
        `Draft context: ${b(t)}`
      ].join(`
`),
      mode: "enqueue",
      attachments: [s("workflow-create-draft", "new-workflow", t)],
      source: { moduleId: o, actionId: "weaver.workflows.suggest-create-metadata", label: "Suggest workflow metadata" }
    })
  }), e.ai.promptActions.add({
    id: "weaver.workflows.explain-definition",
    label: "Explain workflow",
    description: "Ask Weaver to explain the selected workflow definition.",
    moduleId: o,
    placement: "selection",
    contextKind: "workflow-definition",
    createPrompt: (t) => ({
      message: "Explain what this workflow definition does, where it starts, and what a maintainer should inspect first.",
      mode: "enqueue",
      attachments: [s("workflow-definition", d(t), t)],
      source: { moduleId: o, actionId: "weaver.workflows.explain-definition", label: "Explain workflow" }
    })
  }), e.ai.promptActions.add({
    id: "weaver.workflows.explain-executable",
    label: "Explain executable",
    description: "Ask Weaver to explain a published workflow executable and its runtime start conditions.",
    moduleId: o,
    placement: "selection",
    contextKind: "workflow-executable",
    createPrompt: (t) => ({
      message: "Explain this workflow executable, its root activity, source version, and runtime execution implications.",
      mode: "enqueue",
      attachments: [s("workflow-executable", d(t), t)],
      source: { moduleId: o, actionId: "weaver.workflows.explain-executable", label: "Explain executable" }
    })
  }), e.ai.promptActions.add({
    id: "weaver.workflows.find-draft-risks",
    label: "Find draft risks",
    description: "Ask Weaver to inspect draft validation errors and graph structure for likely runtime issues.",
    moduleId: o,
    placement: "toolbar",
    contextKind: "workflow-definition",
    createPrompt: (t) => ({
      message: "Find correctness, validation, maintainability, and runtime risks in this workflow draft. Prefer concrete findings over generic advice.",
      mode: "enqueue",
      attachments: [s("workflow-definition", d(t), t)],
      source: { moduleId: o, actionId: "weaver.workflows.find-draft-risks", label: "Find workflow risks" }
    })
  }), e.ai.promptActions.add({
    id: "weaver.workflows.propose-update",
    label: "Propose update",
    description: "Ask Weaver for a reviewed workflow update proposal instead of applying mutations directly.",
    moduleId: o,
    placement: "toolbar",
    contextKind: "workflow-definition",
    createPrompt: (t) => ({
      message: "Propose a safe workflow update. Return rationale, warnings, validation diagnostics, and a graph diff. Do not apply changes directly.",
      mode: "enqueue",
      attachments: [s("workflow-definition", d(t), t)],
      source: { moduleId: o, actionId: "weaver.workflows.propose-update", label: "Propose workflow update" }
    })
  }), e.ai.promptActions.add({
    id: "weaver.workflows.explain-instance",
    label: "Explain instance",
    description: "Ask Weaver to explain workflow instance state and history when instance data is available.",
    moduleId: o,
    placement: "empty-state",
    contextKind: "workflow-instance",
    createPrompt: (t) => ({
      message: "Explain how to reason about workflow instances in this Studio view. If an instance is attached, summarize its state, incidents, bookmarks, and next actions.",
      mode: "enqueue",
      attachments: [s("workflow-instance", d(t), t)],
      source: { moduleId: o, actionId: "weaver.workflows.explain-instance", label: "Explain workflow instance" }
    })
  }), e.ai.tools.add({
    name: "workflow.definition.read",
    displayName: "Read workflow definition",
    description: "Reads workflow definition metadata, draft state, versions, and validation diagnostics.",
    mutability: "read-only",
    dangerLevel: "low",
    tenantBehavior: "tenant-scoped",
    moduleId: o,
    permissions: ["workflows:read"],
    agentScopes: ["workflow-author"]
  }), e.ai.tools.add({
    name: "workflow.proposal.create",
    displayName: "Create workflow proposal",
    description: "Creates a reviewed workflow create/update proposal that requires explicit user approval before apply.",
    mutability: "proposal",
    dangerLevel: "medium",
    tenantBehavior: "tenant-scoped",
    moduleId: o,
    permissions: ["workflows:write"],
    agentScopes: ["workflow-author"]
  }), e.ai.proposalRenderers.add({
    id: "weaver.workflows.workflow-proposal-renderer",
    kind: "WorkflowCreate",
    moduleId: o,
    component: m
  }), e.ai.proposalRenderers.add({
    id: "weaver.workflows.workflow-update-proposal-renderer",
    kind: "WorkflowUpdate",
    moduleId: o,
    component: m
  });
}
function m({ proposal: e }) {
  const t = e.graphDiff;
  return /* @__PURE__ */ n.jsxs("section", { "aria-label": "Workflow proposal", children: [
    /* @__PURE__ */ n.jsxs("h3", { children: [
      e.kind,
      " proposal"
    ] }),
    /* @__PURE__ */ n.jsx("p", { children: e.rationale || "No rationale provided." }),
    e.warnings?.length ? /* @__PURE__ */ n.jsx("ul", { children: e.warnings.map((a) => /* @__PURE__ */ n.jsx("li", { children: a }, a)) }) : null,
    e.diagnostics?.length ? /* @__PURE__ */ n.jsx("ul", { children: e.diagnostics.map((a) => /* @__PURE__ */ n.jsxs("li", { children: [
      a.severity ?? "info",
      ": ",
      a.message
    ] }, `${a.code ?? ""}-${a.message}`)) }) : null,
    t ? /* @__PURE__ */ n.jsxs("dl", { children: [
      /* @__PURE__ */ n.jsx("dt", { children: "Added" }),
      /* @__PURE__ */ n.jsx("dd", { children: t.addedActivityIds?.length ?? 0 }),
      /* @__PURE__ */ n.jsx("dt", { children: "Removed" }),
      /* @__PURE__ */ n.jsx("dd", { children: t.removedActivityIds?.length ?? 0 }),
      /* @__PURE__ */ n.jsx("dt", { children: "Changed" }),
      /* @__PURE__ */ n.jsx("dd", { children: t.changedActivityIds?.length ?? 0 })
    ] }) : null
  ] });
}
function p(e, t) {
  return s(e, d(t), t);
}
function s(e, t, a) {
  return {
    kind: e,
    referenceId: t,
    metadata: x(a) ? a : { value: a }
  };
}
function d(e) {
  if (!x(e)) return null;
  const t = e.id ?? e.definitionId ?? e.artifactId ?? e.instanceId;
  return typeof t == "string" ? t : null;
}
function b(e) {
  try {
    return JSON.stringify(e);
  } catch {
    return "{}";
  }
}
function x(e) {
  return typeof e == "object" && e !== null;
}
export {
  m as WorkflowProposalRenderer,
  y as register
};
