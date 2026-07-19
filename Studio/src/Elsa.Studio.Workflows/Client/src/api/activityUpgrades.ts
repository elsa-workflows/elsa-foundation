import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import type {
  ActivityUpgradeApplyReceipt,
  ActivityUpgradeApplyResult,
  ActivityUpgradePlan,
  ActivityUpgradePublicationReceipt,
  CreateActivityUpgradePlanRequest
} from "../activityUpgradeTypes";
import { capabilityIds, resolveCapabilityLink } from "./capabilities";

export async function createActivityUpgradePlan(
  context: StudioEndpointContext,
  request: CreateActivityUpgradePlanRequest,
  signal?: AbortSignal
) {
  const path = await resolveCapabilityLink(context, capabilityIds.activityDesign, "activity-upgrade-plans");
  return context.http.postJson<ActivityUpgradePlan>(path, request, signal ? { signal } : undefined);
}

export async function getActivityUpgradePlan(
  context: StudioEndpointContext,
  planId: string,
  signal?: AbortSignal
) {
  const path = await resolveCapabilityLink(
    context,
    capabilityIds.activityDesign,
    "activity-upgrade-plan",
    { planId }
  );
  return context.http.getJson<ActivityUpgradePlan>(path, signal ? { signal } : undefined);
}

export async function applyActivityUpgradeStage(
  context: StudioEndpointContext,
  planId: string,
  stageId: string,
  idempotencyKey: string,
  signal?: AbortSignal
) {
  const path = await resolveCapabilityLink(
    context,
    capabilityIds.activityDesign,
    "activity-upgrade-plan-apply",
    { planId }
  );
  return context.http.postJson<ActivityUpgradeApplyResult>(
    path,
    { stageId, idempotencyKey },
    signal ? { signal } : undefined
  );
}

export async function getActivityUpgradeReceipt(
  context: StudioEndpointContext,
  planId: string,
  receiptId: string,
  signal?: AbortSignal
) {
  const path = await resolveCapabilityLink(
    context,
    capabilityIds.activityDesign,
    "activity-upgrade-apply-receipt",
    { planId, receiptId }
  );
  return context.http.getJson<ActivityUpgradeApplyReceipt>(path, signal ? { signal } : undefined);
}

export async function refreshActivityUpgradePlan(
  context: StudioEndpointContext,
  planId: string,
  publications: ActivityUpgradePublicationReceipt[],
  signal?: AbortSignal
) {
  const path = await resolveCapabilityLink(
    context,
    capabilityIds.activityDesign,
    "activity-upgrade-plan-refresh",
    { planId }
  );
  return context.http.postJson<ActivityUpgradePlan>(
    path,
    { publications },
    signal ? { signal } : undefined
  );
}
