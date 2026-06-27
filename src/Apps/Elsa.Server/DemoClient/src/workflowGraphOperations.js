export function getActivityStructurePayload(activity) {
  return activity?.structure?.payload ?? null;
}

export function getActivityChildSlots(activity) {
  const payload = getActivityStructurePayload(activity);
  const activities = Array.isArray(payload?.activities) ? payload.activities : [];
  if (activities.length === 0)
    return [];

  const kind = activity?.structure?.kind ?? "";
  if (kind === "elsa.flowchart.structure")
    return [{ name: "Flowchart.Activities", activities }];

  if (kind === "elsa.sequence.structure")
    return [{ name: "Sequence.Activities", activities }];

  return [{ name: "Activities", activities }];
}

export function getSlotActivities(slot) {
  return Array.isArray(slot?.activities) ? slot.activities : [];
}

export function normalizeDesignerPosition(position, fallback) {
  const x = Number(position?.x);
  const y = Number(position?.y);

  return {
    x: Number.isFinite(x) ? Math.max(40, Math.round(x)) : fallback.x,
    y: Number.isFinite(y) ? Math.max(40, Math.round(y)) : fallback.y
  };
}

export function getDesignerPosition(activity, fallback) {
  return normalizeDesignerPosition(activity?.designer?.position, fallback);
}

export function getActivityAvailabilityStateName(value) {
  if (typeof value === "string")
    return value;

  return [
    "Available",
    "BlockedByHostBaseline",
    "HiddenByManagementSettings",
    "RemovedFromCatalog",
    "UnresolvedReference"
  ][value] ?? "Available";
}

export function collectActivityVersionIds(activity, ids = new Set()) {
  if (!activity)
    return ids;

  if (activity.activityVersionId && !isTemplateToken(activity.activityVersionId))
    ids.add(activity.activityVersionId);

  for (const slot of getActivityChildSlots(activity)) {
    for (const child of getSlotActivities(slot))
      collectActivityVersionIds(child, ids);
  }

  return ids;
}

function isTemplateToken(value) {
  const text = String(value).trim();
  return text.startsWith("{{") && text.endsWith("}}");
}

export function findActivityAvailabilityDiagnostic(activity, diagnostics, versionDefinitions = {}) {
  const candidates = new Set([
    activity?.activityVersionId,
    activity?.activityTypeKey,
    activity?.type,
    activity?.typeName
  ].filter(Boolean));

  const versionDefinition = versionDefinitions[activity?.activityVersionId];
  if (versionDefinition) {
    candidates.add(versionDefinition.id);
    candidates.add(versionDefinition.activityTypeKey);
  }

  return (diagnostics?.items ?? []).find((item) => {
    if (getActivityAvailabilityStateName(item.state) === "Available")
      return false;

    return [
      item.activityDefinitionId,
      item.activityTypeKey,
      item.referenceName
    ].some((value) => value && candidates.has(value));
  }) ?? null;
}

function createLiteralInput(referenceKey, value) {
  return {
    referenceKey,
    value: {
      value,
      expressionType: "Literal"
    },
    autoEvaluate: null,
    evaluatorType: null,
    storageDriverType: null,
    isSensitive: null
  };
}

function normalizeActivityId(value, usedIds) {
  const base = String(value || "weaver-activity")
    .replace(/^temp:/, "")
    .replace(/[^a-zA-Z0-9_-]+/g, "-")
    .replace(/^-+|-+$/g, "")
    .toLowerCase() || "weaver-activity";
  let candidate = base;
  let index = 2;
  while (usedIds.has(candidate)) {
    candidate = `${base}-${index}`;
    index += 1;
  }

  usedIds.add(candidate);
  return candidate;
}

function collectActivityIds(activity, ids = new Set()) {
  if (!activity)
    return ids;

  if (activity.nodeId)
    ids.add(activity.nodeId);

  for (const slot of getActivityChildSlots(activity)) {
    for (const child of getSlotActivities(slot))
      collectActivityIds(child, ids);
  }

  return ids;
}

function findActivityByNodeId(activity, nodeId) {
  if (!activity || !nodeId)
    return null;

  if (activity.nodeId === nodeId)
    return activity;

  for (const slot of getActivityChildSlots(activity)) {
    for (const child of getSlotActivities(slot)) {
      const match = findActivityByNodeId(child, nodeId);
      if (match)
        return match;
    }
  }

  return null;
}

function createActivityFromOperation(operation, finalId) {
  const parameters = operation.parameters ?? {};
  return {
    nodeId: finalId,
    activityVersionId: parameters.activityVersionId ?? parameters.activityType ?? "Elsa.Workflows.Activity",
    inputs: [],
    outputs: [],
    designer: {
      position: normalizeDesignerPosition(parameters.position, { x: 280, y: 160 })
    }
  };
}

function ensureActivityInput(activity, referenceKey, value) {
  activity.inputs ??= [];
  let input = activity.inputs.find((item) => item.referenceKey === referenceKey);
  if (!input) {
    input = createLiteralInput(referenceKey, value);
    activity.inputs.push(input);
    return;
  }

  input.value = {
    ...(input.value ?? {}),
    value,
    expressionType: input.value?.expressionType ?? "Literal"
  };
}

function cloneWorkflow(workflow) {
  if (typeof structuredClone === "function")
    return structuredClone(workflow);

  return JSON.parse(JSON.stringify(workflow));
}

function replaceWorkflow(target, source) {
  for (const key of Object.keys(target))
    delete target[key];

  Object.assign(target, source);
}

export function applyWorkflowGraphOperationBatchToWorkflow(workflow, batch) {
  if (!batch || !Array.isArray(batch.operations))
    throw new Error("Weaver batch does not contain operations.");

  const working = cloneWorkflow(workflow);
  working.state ??= {};
  const usedIds = collectActivityIds(working.state.rootActivity);
  const activityMap = new Map();
  const temporaryReferences = new Map();
  const touchedIds = [];

  const resolveId = (value) => {
    if (!value)
      return "";

    if (temporaryReferences.has(value))
      return temporaryReferences.get(value);

    return String(value);
  };

  const getActivity = (value) => {
    const resolvedId = resolveId(value);
    return activityMap.get(resolvedId) ?? findActivityByNodeId(working.state.rootActivity, resolvedId);
  };

  for (const operation of batch.operations) {
    const kind = operation.kind;
    const parameters = operation.parameters ?? {};

    if (kind === "AddActivity") {
      const requestedId = parameters.activityId ?? operation.temporaryReferences?.[0];
      const finalId = normalizeActivityId(requestedId, usedIds);
      const activity = createActivityFromOperation(operation, finalId);
      activityMap.set(finalId, activity);
      touchedIds.push(finalId);
      if (requestedId)
        temporaryReferences.set(requestedId, finalId);
      continue;
    }

    if (kind === "SetRoot") {
      const activity = getActivity(parameters.activityId);
      if (!activity)
        throw new Error("Weaver batch referenced an unknown root activity.");

      working.state.rootActivity = activity;
      continue;
    }

    if (kind === "SetDesignerPosition") {
      const activity = getActivity(parameters.activityId);
      if (!activity)
        throw new Error("Weaver batch referenced an unknown activity position.");

      activity.designer ??= {};
      activity.designer.position = normalizeDesignerPosition({ x: parameters.x, y: parameters.y }, { x: 280, y: 160 });
      continue;
    }

    if (kind === "SetActivityProperty") {
      const activity = getActivity(parameters.activityId);
      if (!activity)
        throw new Error("Weaver batch referenced an unknown activity property target.");

      ensureActivityInput(activity, parameters.propertyName ?? "Value", parameters.value ?? "");
      continue;
    }

    if (kind === "UpdateActivity") {
      const activity = getActivity(parameters.activityId);
      if (!activity)
        throw new Error("Weaver batch referenced an unknown activity update target.");

      Object.assign(activity, parameters.patch ?? {});
      continue;
    }

    throw new Error(`Weaver batch operation '${kind || "unknown"}' is not supported by this designer apply path.`);
  }

  if (!working.state.rootActivity)
    throw new Error("Weaver batch did not produce a root activity.");

  replaceWorkflow(workflow, working);

  return {
    appliedCount: batch.operations.length,
    finalActivityIds: touchedIds,
    temporaryReferences: Object.fromEntries(temporaryReferences)
  };
}

export function createDemoWeaverGraphOperationBatch() {
  return {
    schemaVersion: "elsa.workflow-graph-operation-batch.v1",
    workflowDefinitionId: "active-draft",
    operations: [
      {
        id: "op-add-send-email",
        kind: "AddActivity",
        parameters: {
          activityId: "temp:activity:send-email-1",
          activityType: "Elsa.Email.SendEmail",
          displayName: "Send email"
        },
        temporaryReferences: ["temp:activity:send-email-1"],
        summary: "Add a send email activity."
      },
      {
        id: "op-set-root",
        kind: "SetRoot",
        parameters: {
          activityId: "temp:activity:send-email-1"
        },
        temporaryReferences: ["temp:activity:send-email-1"],
        summary: "Use the send email activity as the root."
      },
      {
        id: "op-set-position",
        kind: "SetDesignerPosition",
        parameters: {
          activityId: "temp:activity:send-email-1",
          x: 320,
          y: 180
        },
        temporaryReferences: ["temp:activity:send-email-1"],
        summary: "Place the activity on the canvas."
      },
      {
        id: "op-set-subject",
        kind: "SetActivityProperty",
        parameters: {
          activityId: "temp:activity:send-email-1",
          propertyName: "Subject",
          value: "Hello from Weaver"
        },
        temporaryReferences: ["temp:activity:send-email-1"],
        summary: "Set the email subject."
      }
    ]
  };
}
