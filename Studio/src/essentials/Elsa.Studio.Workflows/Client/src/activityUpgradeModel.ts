import { StudioHttpError } from "@elsa-workflows/studio-sdk";
import type {
  ActivityUpgradeApplyReceipt,
  ActivityUpgradeDiagnostic,
  ActivityUpgradePlan,
  ActivityUpgradeRoot,
  ActivityUpgradeRootKind,
  ActivityUpgradeStage,
  CreateActivityUpgradePlanRequest
} from "./activityUpgradeTypes";

export interface ActivityUpgradeDraft {
  roots: ActivityUpgradeRoot[];
  fromVersionId: string;
  toVersionId: string;
  includeTransitiveDependents: boolean;
  createDraftsForPublishedDependents: boolean;
}

export interface ActivityUpgradeDraftSeed {
  rootKind?: string | null;
  rootId?: string | null;
  fromVersionId?: string | null;
  toVersionId?: string | null;
}

const rootKinds = new Set<ActivityUpgradeRootKind>([
  "ActivityDraft",
  "ActivityVersion",
  "WorkflowDraft",
  "WorkflowVersion"
]);

export function createActivityUpgradeDraft(seed: ActivityUpgradeDraftSeed = {}): ActivityUpgradeDraft {
  const root = normalizeRoot(seed.rootKind, seed.rootId);
  return {
    roots: root ? [root] : [],
    fromVersionId: seed.fromVersionId?.trim() ?? "",
    toVersionId: seed.toVersionId?.trim() ?? "",
    includeTransitiveDependents: true,
    createDraftsForPublishedDependents: false
  };
}

export function addExactUpgradeRoot(
  draft: ActivityUpgradeDraft,
  candidate: ActivityUpgradeRoot
): ActivityUpgradeDraft {
  const root = normalizeRoot(candidate.kind, candidate.id);
  if (!root || draft.roots.some(item => item.kind === root.kind && item.id === root.id)) return draft;
  return { ...draft, roots: [...draft.roots, root] };
}

export function removeExactUpgradeRoot(
  draft: ActivityUpgradeDraft,
  candidate: ActivityUpgradeRoot
): ActivityUpgradeDraft {
  return {
    ...draft,
    roots: draft.roots.filter(item => item.kind !== candidate.kind || item.id !== candidate.id)
  };
}

export function buildActivityUpgradePlanRequest(draft: ActivityUpgradeDraft): CreateActivityUpgradePlanRequest {
  const fromVersionId = draft.fromVersionId.trim();
  const toVersionId = draft.toVersionId.trim();
  if (!fromVersionId || !toVersionId || fromVersionId === toVersionId) {
    throw new Error("Choose two different exact Activity Definition Version identities.");
  }
  if (draft.roots.length === 0) throw new Error("Choose at least one explicit exact root.");
  return {
    replacements: [{ fromVersionId, toVersionId }],
    roots: draft.roots.map(root => ({ ...root })),
    includeTransitiveDependents: draft.includeTransitiveDependents,
    createDraftsForPublishedDependents: draft.createDraftsForPublishedDependents
  };
}

export function isActivityUpgradePlanReady(plan: ActivityUpgradePlan) {
  return normalizePlanStatus(plan.status) === "Ready";
}

export function nextReadyActivityUpgradeStage(plan: ActivityUpgradePlan): ActivityUpgradeStage | null {
  const applied = new Set(
    plan.stages
      .filter(stage => normalizeStageStatus(stage.status) === "Applied")
      .map(stage => stage.stageId)
  );
  return [...plan.stages]
    .sort((left, right) => left.order - right.order)
    .find(stage =>
      normalizeStageStatus(stage.status) === "Ready" &&
      stage.dependsOnStageIds.every(stageId => applied.has(stageId))
    ) ?? null;
}

export function normalizePlanStatus(status: string | number) {
  if (typeof status === "string") return status;
  return ["Ready", "Blocked", "AwaitingPublication", "Applied", "Superseded", "Expired"][status] ?? `Unknown (${status})`;
}

export function normalizeStageStatus(status: string | number) {
  if (typeof status === "string") return status;
  return ["Ready", "AwaitingPublication", "Applied", "Blocked"][status] ?? `Unknown (${status})`;
}

export function normalizeUpgradeAction(action: string | number) {
  if (typeof action === "string") return action;
  return ["UpdateDraft", "CloneActivityVersion", "CloneWorkflowVersion"][action] ?? `Unknown (${action})`;
}

export function normalizeDiagnosticSeverity(severity: string | number) {
  if (typeof severity === "string") return severity;
  return ["Info", "Warning", "Error"][severity] ?? `Unknown (${severity})`;
}

export function activityUpgradeOutcomeUnknown(error: unknown): {
  receiptId: string | null;
  diagnostic: ActivityUpgradeDiagnostic | null;
} | null {
  if (!(error instanceof StudioHttpError)) return null;
  const payload = error.payload as {
    errorCode?: string;
    diagnostics?: ActivityUpgradeDiagnostic[];
  } | null;
  if (payload?.errorCode !== "activity.upgrade.outcome-unknown") return null;
  const diagnostic = payload.diagnostics?.find(item => item.code === "activity.upgrade.outcome-unknown") ?? null;
  return { receiptId: diagnostic?.metadata?.receiptId ?? null, diagnostic };
}

export function receiptIsTerminal(receipt: ActivityUpgradeApplyReceipt) {
  const status = typeof receipt.status === "string"
    ? receipt.status
    : ["Preparing", "Applied", "Rejected"][receipt.status] ?? "";
  return status === "Applied" || status === "Rejected";
}

export async function createActivityUpgradeReceiptId(planId: string, idempotencyKey: string) {
  const keyHash = await sha256(idempotencyKey);
  return `activity-upgrade-receipt-${await sha256(`${planId}\u001f${keyHash}`)}`;
}

async function sha256(value: string) {
  const digest = await globalThis.crypto.subtle.digest("SHA-256", new TextEncoder().encode(value));
  return [...new Uint8Array(digest)]
    .map(byte => byte.toString(16).padStart(2, "0"))
    .join("");
}

function normalizeRoot(kind: string | null | undefined, id: string | null | undefined): ActivityUpgradeRoot | null {
  const trimmedId = id?.trim();
  if (!trimmedId || !kind || !rootKinds.has(kind as ActivityUpgradeRootKind)) return null;
  return { kind: kind as ActivityUpgradeRootKind, id: trimmedId };
}
