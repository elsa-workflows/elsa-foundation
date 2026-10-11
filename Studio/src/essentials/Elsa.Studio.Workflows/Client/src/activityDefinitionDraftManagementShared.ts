import {
  StudioHttpError,
  type StudioActivityDefinitionImplementationEditorContribution,
  type StudioEndpointContext
} from "@elsa-workflows/studio-sdk";
import type {
  ActivityAuthoringCapabilities,
  ActivityDefinitionDraftView,
  ActivityProviderAuthoringCapability
} from "./activityDefinitionTypes";
import { createActivityDefinitionDraft } from "./api/activityDesign";

export interface ProviderChoice {
  capability: ActivityProviderAuthoringCapability;
  providerKey: string;
  schemaVersion: string;
  migratableFromSchemaVersions: string[];
  contribution?: StudioActivityDefinitionImplementationEditorContribution;
}

export async function createBlankDraft(
  context: StudioEndpointContext,
  definitionId: string,
  capabilities: ActivityAuthoringCapabilities,
  choice: ProviderChoice,
  presentationLabel: string | null
): Promise<ActivityDefinitionDraftView> {
  const contractSchemaVersion = capabilities.contractSchemaVersions[0];
  if (!choice.contribution || !contractSchemaVersion) throw new Error("No exact authoring contribution is available.");
  const implementation = choice.contribution.createInitialImplementation();
  return createActivityDefinitionDraft(context, definitionId, {
    sourceVersionId: null,
    presentationLabel,
    provider: {
      providerKey: choice.providerKey,
      schemaVersion: choice.schemaVersion,
      payload: implementation.payload
    },
    contract: {
      contractSchemaVersion,
      inputs: [],
      outputs: [],
      outcomes: choice.capability.requiredOutcomes.map(outcome => ({ ...outcome, description: outcome.description ?? null }))
    },
    layout: implementation.layout
  });
}

export function authorableProviderChoices(
  capabilities: ActivityAuthoringCapabilities | undefined,
  contributions: StudioActivityDefinitionImplementationEditorContribution[]
) {
  return (capabilities?.providers ?? []).flatMap(capability => capability.manifestSchemas
    .filter(schema => schema.isAuthorable)
    .map(schema => ({
      capability,
      providerKey: capability.providerKey,
      schemaVersion: schema.schemaVersion,
      migratableFromSchemaVersions: schema.migratableFromSchemaVersions,
      contribution: contributions.find(item => item.providerKey === capability.providerKey && item.providerSchemaVersion === schema.schemaVersion)
    })));
}

export function migrationProviderChoices(
  capabilities: ActivityAuthoringCapabilities | undefined,
  contributions: StudioActivityDefinitionImplementationEditorContribution[],
  sourceSchemaVersion: string | undefined
) {
  if (!sourceSchemaVersion) return [];
  return authorableProviderChoices(capabilities, contributions)
    .filter(choice => choice.migratableFromSchemaVersions.includes(sourceSchemaVersion));
}

export function choiceValue(choice: Pick<ProviderChoice, "providerKey" | "schemaVersion">) {
  return `${choice.providerKey}|${choice.schemaVersion}`;
}

export function contractSummary(contract: { inputs: unknown[]; outputs: unknown[]; outcomes: unknown[] }) {
  return `${contract.inputs.length} inputs · ${contract.outputs.length} outputs · ${contract.outcomes.length} outcomes`;
}

export function draftMutationError(error: unknown, fallback: string) {
  if (error instanceof StudioHttpError) {
    const payload = error.payload as { errorCode?: string } | null;
    if (payload?.errorCode === "activity.provider.migration-unsupported") return "The exact provider conversion is unsupported. No draft or definition was created.";
    if (payload?.errorCode === "activity.definition.content-authority") return "This source-owned definition cannot receive mutable drafts. Fork an exact version instead.";
    if (payload?.errorCode === "activity.contract.capability-rejected") return "The historical contract is not authorable under the current capability catalog. No mutable draft was created.";
    if (payload?.errorCode === "activity.draft.stale-revision" || payload?.errorCode === "activity.definition.stale-head") return "The source changed before the atomic command committed. Refresh and review again; nothing was created.";
    if (error.status === 403) return "This account is not authorized for the requested authoring operation.";
  }
  return fallback;
}

export function isActionAllowed(actions: { action: string; allowed: boolean }[], action: string) {
  return actions.some(candidate => candidate.action === action && candidate.allowed);
}
