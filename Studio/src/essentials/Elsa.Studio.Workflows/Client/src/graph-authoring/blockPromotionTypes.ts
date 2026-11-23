import type { ActivityAuthoringCapabilities, ActivityContract, ActivityDefinitionLayoutRecord, ActivityProviderManifest } from "../activityDefinitionTypes";
import type { ActivityNode, ActivityPresentationRecord, ArgumentType } from "../workflowTypes";
import type { NativeBlockContext, NativeBlockDiagnostic, NativeBlockDocument } from "./nativeBlocks";
import type { GraphAuthoringResourceKind } from "./graphDocumentAdapter";
import type { PromotionExpressionSupport } from "./promotionExpressionSupport";

export interface BlockPromotionContext extends NativeBlockContext {
  resourceKind: GraphAuthoringResourceKind;
  /** The owning resource's immutable request declarations, not labels inferred from consumers. */
  publicInputs: readonly { referenceKey: string; name: string; type: ArgumentType; isRequired?: boolean; isNullable?: boolean; default?: unknown }[];
  capabilities: ActivityAuthoringCapabilities;
  expressionBoundary?: PromotionExpressionSupport;
}

export interface BlockPromotionCandidate {
  contract: ActivityContract;
  provider: ActivityProviderManifest;
  layout: ActivityDefinitionLayoutRecord[];
  activityPresentation: ActivityPresentationRecord[];
}

export interface BlockPromotionBinding {
  referenceKey: string;
  name: string;
  type: { alias: string; collectionKind: string };
  source: unknown;
  uses: string[];
}

export interface ReadyBlockPromotion {
  status: "ready";
  /** Entire owning resource/catalog/capability/language snapshot, including outside bindings. */
  source: string;
  blockNodeId: string;
  name: string;
  candidate: BlockPromotionCandidate;
  inputs: BlockPromotionBinding[];
  outputs: BlockPromotionBinding[];
  /** The source proposal is materialized only after a reviewed exact published version exists. */
  callSite: Pick<ActivityNode, "nodeId" | "inputs" | "outputs">;
  rewrittenSource: NativeBlockDocument;
  referenceContext: unknown[];
  candidateIdentity: string;
}

export type BlockPromotionPlan = ReadyBlockPromotion | { status: "refused"; diagnostics: NativeBlockDiagnostic[] };

export class PromotionRefusal extends Error {
  constructor(readonly diagnostic: NativeBlockDiagnostic) { super(diagnostic.message); }
}

export function refusePromotion(code: string, path: string, message: string): never {
  throw new PromotionRefusal({ code, path, message });
}

export const promotionRecord = (value: unknown): value is Record<string, unknown> => value !== null && typeof value === "object" && !Array.isArray(value);
