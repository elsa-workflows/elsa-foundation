import type { ChildSlot } from "./workflowAdapter";
import type { ActivityNode, ActivityNodeStructure } from "./workflowTypes";

// The native compensation scope (spec 250): ordered named steps, each a forward activity with an optional
// compensation handler. Its structure is `{ steps: [{ name, activity, compensation }] }`.
export const compensationScopeStructureKind = "elsa.compensation-scope.structure";

// The slot its designer opens: every forward and handler activity, so frames and lookups resolve them.
export const compensationStepsSlotId = `${compensationScopeStructureKind}:steps`;

export interface CompensationStep {
  name: string;
  activity: ActivityNode | null;
  compensation: ActivityNode | null;
}

export function isCompensationScope(activity: ActivityNode | null | undefined): boolean {
  return activity?.structure?.kind === compensationScopeStructureKind;
}

export function readCompensationSteps(activity: ActivityNode | null | undefined): CompensationStep[] {
  const steps = activity?.structure?.payload?.steps;
  if (!Array.isArray(steps)) return [];
  return steps.flatMap(step => {
    if (!isRecord(step)) return [];
    return [{
      name: typeof step.name === "string" ? step.name : "",
      activity: isActivityNode(step.activity) ? step.activity : null,
      compensation: isActivityNode(step.compensation) ? step.compensation : null
    }];
  });
}

// The "Steps" slot first, so a scope opened at the root or from a badge shows its designer, then a forward and a
// compensation slot per step. Per-step slots are collection slots, so writes through them touch only their item.
export function compensationScopeChildSlots(structure: ActivityNodeStructure): ChildSlot[] {
  const steps = readCompensationStepsFromStructure(structure);
  const stepsSlot: ChildSlot = {
    id: compensationStepsSlotId,
    label: "Steps",
    property: "steps",
    cardinality: "many",
    mode: "compensation",
    activities: steps.flatMap(step => [step.activity, step.compensation].filter((activity): activity is ActivityNode => activity !== null))
  };

  const perStep = steps.flatMap((step, index): ChildSlot[] => [
    stepSlot(index, step, "activity", step.name || `Step ${index + 1}`),
    stepSlot(index, step, "compensation", `${step.name || `Step ${index + 1}`} (compensation)`)
  ]);

  return [stepsSlot, ...perStep];
}

// Writes activities back by node id. The "Steps" slot is a view over the steps, never an array to store: an
// activity that is no longer present is removed from its step, and one that belongs to no step is ignored.
export function replaceCompensationScopeActivities(scope: ActivityNode, activities: ActivityNode[]): ActivityNode {
  const byNodeId = new Map(activities.map(activity => [activity.nodeId, activity]));
  const steps = readCompensationSteps(scope).map(step => ({
    ...step,
    activity: step.activity ? byNodeId.get(step.activity.nodeId) ?? null : null,
    compensation: step.compensation ? byNodeId.get(step.compensation.nodeId) ?? null : null
  }));
  return withCompensationSteps(scope, steps);
}

export function withCompensationSteps(scope: ActivityNode, steps: CompensationStep[]): ActivityNode {
  if (!scope.structure) return scope;
  return {
    ...scope,
    structure: {
      ...scope.structure,
      payload: {
        ...scope.structure.payload,
        steps: steps.map(step => ({ name: step.name, activity: step.activity, compensation: step.compensation }))
      }
    }
  };
}

const stepSlotPrefix = "CompensationScope.Step[";
const compensationSlotPrefix = "CompensationScope.Compensation[";

// Rebuilds the authored steps from a pinned executable's named slots, `CompensationScope.Step[{name}]` and
// `CompensationScope.Compensation[{name}]`, in the order the executable lists them. The run view draws from the
// executable, whose compiled payload holds node ids rather than activities.
export function compensationStepsFromExecutableSlots<TSlot extends { name: string; activities: TChild[] }, TChild>(
  slots: TSlot[],
  adapt: (child: TChild) => ActivityNode
): CompensationStep[] {
  const steps: CompensationStep[] = [];
  const byName = new Map<string, CompensationStep>();
  const stepFor = (name: string) => {
    let step = byName.get(name);
    if (!step) {
      step = { name, activity: null, compensation: null };
      byName.set(name, step);
      steps.push(step);
    }
    return step;
  };

  for (const slot of slots) {
    const child = slot.activities[0];
    const stepName = slotStepName(slot.name, stepSlotPrefix);
    const compensationName = slotStepName(slot.name, compensationSlotPrefix);
    if (stepName !== null) stepFor(stepName).activity = child === undefined ? null : adapt(child);
    else if (compensationName !== null) stepFor(compensationName).compensation = child === undefined ? null : adapt(child);
  }

  return steps;
}

function slotStepName(slotName: string, prefix: string): string | null {
  return slotName.startsWith(prefix) && slotName.endsWith("]") ? slotName.slice(prefix.length, -1) : null;
}

// The first free "Step N" name, so a new step never duplicates an existing name.
export function nextCompensationStepName(steps: CompensationStep[]): string {
  const names = new Set(steps.map(step => step.name.trim()));
  for (let index = steps.length + 1; ; index++) {
    const candidate = `Step ${index}`;
    if (!names.has(candidate)) return candidate;
  }
}

export function addCompensationStep(steps: CompensationStep[], index: number, activity: ActivityNode, name?: string): CompensationStep[] {
  const next = [...steps];
  next.splice(Math.max(0, Math.min(index, steps.length)), 0, { name: name ?? nextCompensationStepName(steps), activity, compensation: null });
  return next;
}

export function moveCompensationStep(steps: CompensationStep[], from: number, to: number): CompensationStep[] {
  if (from < 0 || from >= steps.length || to < 0 || to >= steps.length || from === to) return steps;
  const next = [...steps];
  const [moved] = next.splice(from, 1);
  next.splice(to, 0, moved);
  return next;
}

export function updateCompensationStep(steps: CompensationStep[], index: number, patch: Partial<CompensationStep>): CompensationStep[] {
  return steps.map((step, candidate) => candidate === index ? { ...step, ...patch } : step);
}

export function removeCompensationStep(steps: CompensationStep[], index: number): CompensationStep[] {
  return steps.filter((_, candidate) => candidate !== index);
}

// A scope row's validation path, `{scope}/steps/{index}`, lands on that row's forward activity, so "Go to" opens the
// scope designer with the row's cell selected. A row without an activity has nothing to select: the caller falls back
// to the scope itself, and the row shows its problem inline.
export function compensationValidationTarget(scope: ActivityNode | null | undefined, path: string | null | undefined): string | null {
  const [scopeId, segment, index] = String(path ?? "").split("/").filter(Boolean);
  if (!scope || !isCompensationScope(scope) || scope.nodeId !== scopeId || segment !== "steps" || !/^\d+$/.test(index ?? "")) return null;
  return readCompensationSteps(scope)[Number(index)]?.activity?.nodeId ?? null;
}

// Row-level problems the designer shows inline. The server's Draft validator reports the same rules on publish.
export function compensationStepProblems(steps: CompensationStep[]): Map<number, string> {
  const problems = new Map<number, string>();
  const counts = new Map<string, number>();
  for (const step of steps) {
    const name = step.name.trim();
    if (name) counts.set(name, (counts.get(name) ?? 0) + 1);
  }

  steps.forEach((step, index) => {
    const name = step.name.trim();
    if (!name) problems.set(index, "Step needs a name.");
    else if ((counts.get(name) ?? 0) > 1) problems.set(index, `Step "${name}" is defined more than once.`);
    else if (!step.activity) problems.set(index, `Step "${name}" has no activity.`);
  });
  return problems;
}

function stepSlot(index: number, step: CompensationStep, childProperty: "activity" | "compensation", label: string): ChildSlot {
  const value = step[childProperty];
  return {
    id: `${compensationScopeStructureKind}:steps:steps:${childProperty}:${index}`,
    label,
    property: "steps",
    cardinality: "single",
    mode: "compensation",
    activities: value ? [value] : [],
    collectionProperty: "steps",
    childProperty,
    labelProperty: "name",
    collectionIndex: index,
    collectionItemLabel: step.name || undefined
  };
}

function readCompensationStepsFromStructure(structure: ActivityNodeStructure): CompensationStep[] {
  return readCompensationSteps({ nodeId: "", activityVersionId: "", inputs: [], outputs: [], structure });
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null;
}

function isActivityNode(value: unknown): value is ActivityNode {
  return isRecord(value) && typeof value.nodeId === "string" && typeof value.activityVersionId === "string";
}
