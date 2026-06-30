var u = { exports: {} }, c = {};
var m;
function x() {
  if (m) return c;
  m = 1;
  var t = /* @__PURE__ */ Symbol.for("react.transitional.element"), e = /* @__PURE__ */ Symbol.for("react.fragment");
  function o(l, i, r) {
    var f = null;
    if (r !== void 0 && (f = "" + r), i.key !== void 0 && (f = "" + i.key), "key" in i) {
      r = {};
      for (var p in i)
        p !== "key" && (r[p] = i[p]);
    } else r = i;
    return i = r.ref, {
      $$typeof: t,
      type: l,
      key: f,
      ref: i !== void 0 ? i : null,
      props: r
    };
  }
  return c.Fragment = e, c.jsx = o, c.jsxs = o, c;
}
var h;
function g() {
  return h || (h = 1, u.exports = x()), u.exports;
}
var a = g();
const n = "Elsa.Studio.Weaver.Workflows";
function b(t) {
  t.ai.contextProviders.add({
    id: "weaver.workflows.definition-context",
    kind: "workflow-definition",
    label: "Workflow definition",
    description: "Attaches workflow definition metadata, draft state, versions, and validation diagnostics.",
    moduleId: n,
    createAttachment: (e) => k("workflow-definition", e)
  }), t.ai.contextProviders.add({
    id: "weaver.workflows.executable-context",
    kind: "workflow-executable",
    label: "Workflow executable",
    description: "Attaches runtime executable identity, source, root activity, and resume target metadata.",
    moduleId: n,
    createAttachment: (e) => k("workflow-executable", e)
  }), t.ai.contextProviders.add({
    id: "weaver.workflows.instance-context",
    kind: "workflow-instance",
    label: "Workflow instance",
    description: "Attaches workflow instance state, incidents, bookmarks, and recent execution history when available.",
    moduleId: n,
    createAttachment: (e) => k("workflow-instance", e)
  }), t.ai.promptActions.add({
    id: "weaver.workflows.suggest-create-metadata",
    label: "Suggest name and description",
    description: "Ask Weaver to propose a workflow display name and description for the draft being created.",
    moduleId: n,
    placement: "field-adornment",
    contextKind: "workflow-create-draft",
    createPrompt: (e) => {
      const o = w(e) && w(e.draft) ? e.draft : {}, l = w(e) && typeof e.intent == "string" ? e.intent.trim() : "", i = typeof o.rootKind == "string" ? o.rootKind : void 0, r = { intent: l, rootKind: i, name: o.name ?? "", description: o.description ?? "" };
      return {
        message: [
          "Suggest a concise display name and a one-sentence description for a new Elsa workflow.",
          l ? `The user describes what the workflow should do as: "${l}".` : "The user has not described the workflow yet — infer a sensible, generic automation name and description.",
          i ? `The root activity is a ${i}.` : "",
          'Reply with one short friendly sentence, then a fenced ```json code block containing exactly {"name": string, "description": string} and nothing else in that block.'
        ].filter(Boolean).join(`
`),
        mode: "enqueue",
        attachments: [s("workflow-create-draft", "new-workflow", r)],
        source: { moduleId: n, actionId: "weaver.workflows.suggest-create-metadata", label: "Suggest workflow metadata" }
      };
    }
  }), t.ai.promptActions.add({
    id: "weaver.workflows.explain-definition",
    label: "Explain workflow",
    description: "Ask Weaver to explain the selected workflow definition.",
    moduleId: n,
    placement: "selection",
    contextKind: "workflow-definition",
    createPrompt: (e) => ({
      message: "Explain what this workflow definition does, where it starts, and what a maintainer should inspect first.",
      mode: "enqueue",
      attachments: [s("workflow-definition", d(e), e)],
      source: { moduleId: n, actionId: "weaver.workflows.explain-definition", label: "Explain workflow" }
    })
  }), t.ai.promptActions.add({
    id: "weaver.workflows.explain-executable",
    label: "Explain executable",
    description: "Ask Weaver to explain a published workflow executable and its runtime start conditions.",
    moduleId: n,
    placement: "selection",
    contextKind: "workflow-executable",
    createPrompt: (e) => ({
      message: "Explain this workflow executable, its root activity, source version, and runtime execution implications.",
      mode: "enqueue",
      attachments: [s("workflow-executable", d(e), e)],
      source: { moduleId: n, actionId: "weaver.workflows.explain-executable", label: "Explain executable" }
    })
  }), t.ai.promptActions.add({
    id: "weaver.workflows.find-draft-risks",
    label: "Find draft risks",
    description: "Ask Weaver to inspect draft validation errors and graph structure for likely runtime issues.",
    moduleId: n,
    placement: "toolbar",
    contextKind: "workflow-definition",
    createPrompt: (e) => ({
      message: "Find correctness, validation, maintainability, and runtime risks in this workflow draft. Prefer concrete findings over generic advice.",
      mode: "enqueue",
      attachments: [s("workflow-definition", d(e), e)],
      source: { moduleId: n, actionId: "weaver.workflows.find-draft-risks", label: "Find workflow risks" }
    })
  }), t.ai.promptActions.add({
    id: "weaver.workflows.propose-update",
    label: "Propose update",
    description: "Ask Weaver for a reviewed workflow update proposal instead of applying mutations directly.",
    moduleId: n,
    placement: "toolbar",
    contextKind: "workflow-definition",
    createPrompt: (e) => ({
      message: "Propose a safe workflow update. Return rationale, warnings, validation diagnostics, and a graph diff. Do not apply changes directly.",
      mode: "enqueue",
      attachments: [s("workflow-definition", d(e), e)],
      source: { moduleId: n, actionId: "weaver.workflows.propose-update", label: "Propose workflow update" }
    })
  }), t.ai.promptActions.add({
    id: "weaver.workflows.explain-instance",
    label: "Explain instance",
    description: "Ask Weaver to explain workflow instance state and history when instance data is available.",
    moduleId: n,
    placement: "empty-state",
    contextKind: "workflow-instance",
    createPrompt: (e) => ({
      message: "Explain how to reason about workflow instances in this Studio view. If an instance is attached, summarize its state, incidents, bookmarks, and next actions.",
      mode: "enqueue",
      attachments: [s("workflow-instance", d(e), e)],
      source: { moduleId: n, actionId: "weaver.workflows.explain-instance", label: "Explain workflow instance" }
    })
  }), t.ai.tools.add({
    name: "workflow.definition.read",
    displayName: "Read workflow definition",
    description: "Reads workflow definition metadata, draft state, versions, and validation diagnostics.",
    mutability: "read-only",
    dangerLevel: "low",
    tenantBehavior: "tenant-scoped",
    moduleId: n,
    permissions: ["workflows:read"],
    agentScopes: ["workflow-author"]
  }), t.ai.tools.add({
    name: "workflow.proposal.create",
    displayName: "Create workflow proposal",
    description: "Creates a reviewed workflow create/update proposal that requires explicit user approval before apply.",
    mutability: "proposal",
    dangerLevel: "medium",
    tenantBehavior: "tenant-scoped",
    moduleId: n,
    permissions: ["workflows:write"],
    agentScopes: ["workflow-author"]
  }), t.ai.proposalRenderers.add({
    id: "weaver.workflows.workflow-proposal-renderer",
    kind: "WorkflowCreate",
    moduleId: n,
    component: v
  }), t.ai.proposalRenderers.add({
    id: "weaver.workflows.workflow-update-proposal-renderer",
    kind: "WorkflowUpdate",
    moduleId: n,
    component: v
  });
}
function v({ proposal: t }) {
  const e = t.graphDiff;
  return /* @__PURE__ */ a.jsxs("section", { "aria-label": "Workflow proposal", children: [
    /* @__PURE__ */ a.jsxs("h3", { children: [
      t.kind,
      " proposal"
    ] }),
    /* @__PURE__ */ a.jsx("p", { children: t.rationale || "No rationale provided." }),
    t.warnings?.length ? /* @__PURE__ */ a.jsx("ul", { children: t.warnings.map((o) => /* @__PURE__ */ a.jsx("li", { children: o }, o)) }) : null,
    t.diagnostics?.length ? /* @__PURE__ */ a.jsx("ul", { children: t.diagnostics.map((o) => /* @__PURE__ */ a.jsxs("li", { children: [
      o.severity ?? "info",
      ": ",
      o.message
    ] }, `${o.code ?? ""}-${o.message}`)) }) : null,
    e ? /* @__PURE__ */ a.jsxs("dl", { children: [
      /* @__PURE__ */ a.jsx("dt", { children: "Added" }),
      /* @__PURE__ */ a.jsx("dd", { children: e.addedActivityIds?.length ?? 0 }),
      /* @__PURE__ */ a.jsx("dt", { children: "Removed" }),
      /* @__PURE__ */ a.jsx("dd", { children: e.removedActivityIds?.length ?? 0 }),
      /* @__PURE__ */ a.jsx("dt", { children: "Changed" }),
      /* @__PURE__ */ a.jsx("dd", { children: e.changedActivityIds?.length ?? 0 })
    ] }) : null
  ] });
}
function k(t, e) {
  return s(t, d(e), e);
}
function s(t, e, o) {
  return {
    kind: t,
    referenceId: e,
    metadata: w(o) ? o : { value: o }
  };
}
function d(t) {
  if (!w(t)) return null;
  const e = t.id ?? t.definitionId ?? t.artifactId ?? t.instanceId;
  return typeof e == "string" ? e : null;
}
function w(t) {
  return typeof t == "object" && t !== null;
}
export {
  v as WorkflowProposalRenderer,
  b as register
};
