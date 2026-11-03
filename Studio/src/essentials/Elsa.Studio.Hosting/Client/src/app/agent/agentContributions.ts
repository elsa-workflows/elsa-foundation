import type {
  ElsaStudioModuleApi,
  StudioAgentCapabilityContribution,
  StudioAgentPromptStarterContribution,
  StudioAgentSurface,
  StudioAgentToolContractContribution,
  StudioAgentToolSlotContribution
} from "../../sdk";

export interface AgentContributionFilterOptions {
  readonly moduleStates?: readonly AgentContributionModuleState[];
  readonly permissions?: readonly string[];
  readonly policy?: AgentContributionPolicy;
}

export interface AgentContributionModuleState {
  readonly id: string;
  readonly status: string;
  readonly compatibility?: string;
}

export interface AgentContributionPolicy {
  readonly allowedCapabilityIds?: readonly string[];
  readonly deniedCapabilityIds?: readonly string[];
  readonly allowedToolIds?: readonly string[];
  readonly deniedToolIds?: readonly string[];
  readonly disabledModuleIds?: readonly string[];
}

export function getActiveAgentCapabilities(
  api: ElsaStudioModuleApi,
  surface: StudioAgentSurface,
  options: AgentContributionFilterOptions = {}
) {
  return api.agent.capabilities
    .list()
    .filter(capability => matchesSurface(capability.surfaces, surface.route))
    .filter(capability => isModuleEnabled(capability.moduleId, options))
    .filter(capability => hasRequiredPermissions(capability.requiredPermissions, options.permissions))
    .filter(capability => isCapabilityAllowed(capability.id, options.policy));
}

export function getActivePromptStarters(
  api: ElsaStudioModuleApi,
  surface: StudioAgentSurface,
  capabilities: StudioAgentCapabilityContribution[] = getActiveAgentCapabilities(api, surface),
  options: AgentContributionFilterOptions = {}
) {
  const capabilityIds = new Set(capabilities.map(capability => capability.id));
  return api.agent.promptStarters
    .list()
    .filter(prompt => matchesSurface(prompt.surfaces, surface.route))
    .filter(prompt => isModuleEnabled(prompt.moduleId, options))
    .filter(prompt => !prompt.requiredCapabilities?.some(capability => !capabilityIds.has(capability)))
    .sort(byOrderThenLabel);
}

export function getActiveAgentToolSlots(
  api: ElsaStudioModuleApi,
  surface: StudioAgentSurface,
  options: AgentContributionFilterOptions = {}
) {
  return api.agent.toolSlots
    .list()
    .filter(slot => matchesSurface(slot.surfaces, surface.route))
    .filter(slot => isModuleEnabled(slot.moduleId, options))
    .sort(byOrderThenDisplayName);
}

export function getActiveAgentToolContracts(
  api: ElsaStudioModuleApi,
  surface: StudioAgentSurface,
  options: AgentContributionFilterOptions = {}
) {
  const slots = getActiveAgentToolSlots(api, surface, options);
  const slotIds = new Set(slots.map(slot => slot.id));
  const slotRank = new Map(slots.map((slot, index) => [slot.id, index]));

  return api.agent.toolContracts
    .list()
    .filter(contract => slotIds.has(contract.slotId))
    .filter(contract => matchesSurface(contract.surfaces, surface.route))
    .filter(contract => isModuleEnabled(contract.moduleId, options))
    .filter(contract => hasRequiredPermissions(contract.requiredPermissions, options.permissions))
    .filter(isToolAvailable)
    .filter(contract => isToolAllowed(contract.id, options.policy))
    .sort((left, right) => bySlotThenOrderThenDisplayName(left, right, slotRank));
}

export function matchesSurface(surfaces: readonly string[], route: string) {
  return surfaces.includes("*") || surfaces.some(surface => route === surface || route.startsWith(`${surface}/`));
}

function byOrderThenLabel(left: StudioAgentPromptStarterContribution, right: StudioAgentPromptStarterContribution) {
  return (left.order ?? 500) - (right.order ?? 500) || left.label.localeCompare(right.label);
}

function byOrderThenDisplayName(left: StudioAgentToolSlotContribution, right: StudioAgentToolSlotContribution) {
  return (left.order ?? 500) - (right.order ?? 500) || left.displayName.localeCompare(right.displayName);
}

function bySlotThenOrderThenDisplayName(
  left: StudioAgentToolContractContribution,
  right: StudioAgentToolContractContribution,
  slotRank: ReadonlyMap<string, number>
) {
  return (slotRank.get(left.slotId) ?? 500) - (slotRank.get(right.slotId) ?? 500)
    || (left.order ?? 500) - (right.order ?? 500)
    || left.displayName.localeCompare(right.displayName);
}

function isModuleEnabled(moduleId: string | undefined, options: AgentContributionFilterOptions) {
  if (!moduleId) {
    return true;
  }

  if (options.policy?.disabledModuleIds?.includes(moduleId)) {
    return false;
  }

  const moduleState = options.moduleStates?.find(module => module.id === moduleId);
  if (!moduleState) {
    return true;
  }

  return ["available", "loaded"].includes(moduleState.status)
    && moduleState.compatibility !== "incompatible";
}

function hasRequiredPermissions(requiredPermissions: readonly string[] | undefined, permissions: readonly string[] | undefined) {
  if (!requiredPermissions?.length) {
    return true;
  }

  const granted = new Set(permissions ?? []);
  return requiredPermissions.every(permission => granted.has(permission));
}

function isCapabilityAllowed(capabilityId: string, policy: AgentContributionPolicy | undefined) {
  if (!policy) {
    return true;
  }

  if (policy.deniedCapabilityIds?.includes(capabilityId)) {
    return false;
  }

  return !policy.allowedCapabilityIds || policy.allowedCapabilityIds.includes(capabilityId);
}

function isToolAvailable(contract: StudioAgentToolContractContribution) {
  return !contract.availability || contract.availability.status === "available";
}

function isToolAllowed(toolId: string, policy: AgentContributionPolicy | undefined) {
  if (!policy) {
    return true;
  }

  if (policy.deniedToolIds?.includes(toolId)) {
    return false;
  }

  return !policy.allowedToolIds || policy.allowedToolIds.includes(toolId);
}
