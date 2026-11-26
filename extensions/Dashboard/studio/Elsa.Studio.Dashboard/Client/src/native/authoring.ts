import type { QuestionDefinition } from "./types";

export interface DashboardAuthoringTarget {
  moduleId: "Elsa.Studio.Dashboard";
  resourceType: "question" | "question-draft";
  resourceId: string;
  revision: string;
  displayName?: string;
}

export interface DashboardAuthoringSnapshot {
  generationId: string;
  client: { clientId: string; generationId: string };
  target: DashboardAuthoringTarget;
  workingRevision: string;
  definition: QuestionDefinition;
  name: string;
  dirty: boolean;
  canEdit: boolean;
}

export interface DashboardAuthoringProposal {
  client: { clientId: string; generationId: string };
  target: DashboardAuthoringTarget;
  workingRevision: string;
  definition: QuestionDefinition;
  interpretation?: string;
  /** Optional native full-request JSON fingerprint, not an authorization or server query digest. */
  definitionFingerprint?: string;
}

export interface DashboardAuthoringApplyResult {
  outcome: "applied" | "refused";
  summary: string;
  workingRevision?: string;
}

export interface DashboardAuthoringController {
  clientId: string;
  snapshots: ReadonlyMap<string, DashboardAuthoringSnapshot>;
  subscribe(listener: () => void): () => void;
  publish(snapshot: DashboardAuthoringSnapshot, apply: (proposal: DashboardAuthoringProposal) => Promise<DashboardAuthoringApplyResult>): () => void;
  useProposal(generationId: string, proposal: DashboardAuthoringProposal): Promise<DashboardAuthoringApplyResult>;
  /** Optional module-owned integration; native authoring and saving remain usable without it. */
  weaver?: {
    status(): { available: boolean; reason?: string };
    subscribe(listener: () => void): () => void;
    request(generationId: string, message: string): { accepted: boolean; reason?: string };
  };
}

/** One module registration owns this registry; no state crosses module, editor or authenticated lifetimes. */
export function createDashboardAuthoringController(clientId: string): DashboardAuthoringController {
  const snapshots = new Map<string, DashboardAuthoringSnapshot>();
  const handlers = new Map<string, (proposal: DashboardAuthoringProposal) => Promise<DashboardAuthoringApplyResult>>();
  const listeners = new Set<() => void>();
  const notify = () => listeners.forEach(listener => listener());
  return {
    clientId,
    snapshots,
    subscribe(listener) { listeners.add(listener); return () => { listeners.delete(listener); }; },
    publish(snapshot, apply) {
      const retained = structuredClone(snapshot);
      snapshots.set(snapshot.generationId, retained);
      handlers.set(snapshot.generationId, apply);
      notify();
      return () => {
        if (snapshots.get(snapshot.generationId) !== retained) return;
        snapshots.delete(snapshot.generationId);
        handlers.delete(snapshot.generationId);
        notify();
      };
    },
    async useProposal(generationId, proposal) {
      const handler = handlers.get(generationId);
      if (!handler || proposal.client.clientId !== clientId || proposal.client.generationId !== generationId)
        return { outcome: "refused", summary: "The originating question editor is unavailable. Nothing was applied or saved." };
      return handler(structuredClone(proposal));
    }
  };
}

export function sameAuthoringTarget(left: DashboardAuthoringTarget, right: DashboardAuthoringTarget) {
  return left.moduleId === right.moduleId && left.resourceType === right.resourceType && left.resourceId === right.resourceId && left.revision === right.revision;
}
