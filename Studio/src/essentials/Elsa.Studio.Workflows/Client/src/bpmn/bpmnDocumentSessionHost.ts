import type { ActivityNode } from "../workflowTypes";
import { AuthoringCheckpointError, type AuthoringCheckpointCoordinator, type AuthoringCheckpointToken,
  type AuthoringParticipantWork, type AuthoringParentReceipt } from "../graph-authoring/authoringCheckpoint";
import { applyBpmnDocumentTransaction, readBpmnDocument, type BpmnDocumentCommand } from "./bpmnDocument";
import { planBpmnDocumentSyntaxChange, type BpmnDocumentSyntaxPlan } from "./bpmnDocumentSyntaxChange";

export interface BpmnDocumentSessionSource { readonly owner: ActivityNode; readonly token: AuthoringCheckpointToken }
export interface BpmnDocumentSessionAcknowledgement extends BpmnDocumentSessionSource { readonly acceptedSequence: number }
export interface BpmnDocumentSessionCommit {
  readonly token: AuthoringCheckpointToken;
  readonly sourceXml: string;
  readonly importGeneration: number;
  readonly sequence: number;
  readonly commands: readonly BpmnDocumentCommand[];
}
export interface BpmnDocumentSessionAdmission {
  readonly kind: "admitted" | "discarded";
  readonly acknowledgement: BpmnDocumentSessionAcknowledgement;
}

function sameToken(left: AuthoringCheckpointToken, right: AuthoringCheckpointToken) {
  return left.authorityId === right.authorityId && left.resourceId === right.resourceId && left.scopeId === right.scopeId && left.revision === right.revision;
}

/** Parent-owned admission and receipt ledger; retain this with the controller when its view detaches. */
export function createBpmnDocumentSessionHost(options: {
  checkpointOrder?: number;
  id: string;
  coordinator: Pick<AuthoringCheckpointCoordinator<unknown>, "register">;
  readLive(): BpmnDocumentSessionSource;
  /** Synchronous CAS into the resource document, including its normal history. */
  admit(expected: BpmnDocumentSessionSource, candidate: ActivityNode, plan: BpmnDocumentSyntaxPlan): boolean;
  /** The resource may supply its authorized exact native ownership catalog. */
  plan?(owner: ActivityNode, commands: readonly BpmnDocumentCommand[]): BpmnDocumentSyntaxPlan;
  canEdit(): boolean;
  onWorkChanged?(): void;
  participant: {
    readWork(): AuthoringParticipantWork;
    checkpoint(): Promise<{ acknowledgement: BpmnDocumentSessionAcknowledgement; release(): void }>;
  };
}) {
  interface Admission {
    kind: BpmnDocumentSessionAdmission["kind"];
    digest: string;
    acknowledgement: BpmnDocumentSessionAcknowledgement;
    receipt?: AuthoringParentReceipt;
  }
  const initial = options.readLive();
  const admissions = new Map<number, Admission>();
  let sequence = 0, retired = false;
  const fail = (message: string): never => { throw new AuthoringCheckpointError("invalid-receipt", message); };
  const current = (source: BpmnDocumentSessionSource) => {
    const live = options.readLive();
    return sameToken(source.token, live.token) && JSON.stringify(source.owner) === JSON.stringify(live.owner);
  };
  const registration = options.coordinator.register(options.id, {
    readWork: () => options.participant.readWork(),
    async checkpoint() {
      const lease = await options.participant.checkpoint();
      const acknowledgement = lease.acknowledgement;
      // A clean native re-import may have adopted a canonical inspector/history edit. The parent
      // still verifies the exact committed owner and its own accepted command sequence.
      const unissued = Object.freeze({ token: acknowledgement.token, acceptedSequence: acknowledgement.acceptedSequence });
      let receipt: AuthoringParentReceipt = unissued;
      try {
        if (options.canEdit() && acknowledgement.acceptedSequence === sequence && current(acknowledgement))
          receipt = registration.acknowledge(sequence, acknowledgement.token);
      } catch { /* Return the unissued receipt; the coordinator rejects it while retaining its lease. */ }
      return { receipt, release: lease.release };
    }
  }, options.checkpointOrder === undefined ? undefined : { order: options.checkpointOrder });
  const issue = (admission: Admission) => {
    if (!options.canEdit() || !current(admission.acknowledgement))
      fail("The admitted canvas receipt belongs to a different canonical source or authorization.");
    admission.receipt ??= registration.acknowledge(admission.acknowledgement.acceptedSequence, admission.acknowledgement.token);
    return admission.acknowledgement;
  };
  const readAdmission = (request: BpmnDocumentSessionCommit): BpmnDocumentSessionAdmission | undefined => {
    if (retired) fail("This canvas session has retired.");
    const live = options.readLive();
    if (!options.canEdit() || live.token.authorityId !== initial.token.authorityId || live.token.resourceId !== initial.token.resourceId
      || live.token.scopeId !== initial.token.scopeId || request.token.authorityId !== live.token.authorityId
      || request.token.resourceId !== live.token.resourceId || request.token.scopeId !== live.token.scopeId)
      fail("The canvas recovery belongs to another authoring authority or scope, or authorization is unavailable.");
    const admission = admissions.get(request.sequence);
    if (!admission) return undefined;
    if (admission.digest !== JSON.stringify(request)) fail("A native command sequence was reused for different work.");
    return Object.freeze({ kind: admission.kind, acknowledgement: admission.acknowledgement });
  };
  const lookup = (request: BpmnDocumentSessionCommit): BpmnDocumentSessionAcknowledgement | undefined => {
    if (retired) fail("This canvas session has retired.");
    const fact = readAdmission(request);
    if (fact?.kind === "discarded") fail("This native command was explicitly discarded; re-import the current canonical document.");
    const admission = admissions.get(request.sequence);
    if (!admission) return undefined;
    if (admission.digest !== JSON.stringify(request)) fail("A native command sequence was reused for different work.");
    return issue(admission);
  };
  return {
    initialReceipt: registration.initialReceipt,
    /** Historical admission fact; reading it never re-applies work or issues a receipt for stale XML. */
    readAdmission,
    /** Explicit user discard consumes only an unadmitted sequence; an admitted command is never undone. */
    abandon(request: BpmnDocumentSessionCommit): BpmnDocumentSessionAdmission {
      const prior = readAdmission(request);
      if (prior) return prior;
      if (!Number.isSafeInteger(request.sequence) || request.sequence !== sequence + 1)
        fail("Only the next unadmitted native sequence can be discarded.");
      const work = options.participant.readWork();
      if (!Number.isSafeInteger(work.sequence) || !Number.isSafeInteger(work.acceptedSequence) || work.acceptedSequence < 0
        || work.acceptedSequence > work.sequence || work.sequence < request.sequence || typeof work.pending !== "boolean")
        fail("The discarded command has no matching retained native work.");
      const live = options.readLive();
      const acknowledgement = Object.freeze({ owner: structuredClone(live.owner), token: Object.freeze({ ...live.token }), acceptedSequence: request.sequence });
      const admission: Admission = { kind: "discarded", digest: JSON.stringify(request), acknowledgement };
      admissions.set(request.sequence, admission); sequence = request.sequence;
      issue(admission);
      try { options.onWorkChanged?.(); } catch { /* Observation cannot change a recorded discard. */ }
      return Object.freeze({ kind: admission.kind, acknowledgement });
    },
    /** Recovery is a receipt lookup, never a second application of an uncertain command. */
    lookup,
    commit(request: BpmnDocumentSessionCommit): BpmnDocumentSessionAcknowledgement {
      const digest = JSON.stringify(request);
      const prior = lookup(request);
      if (prior) return prior;
      if (!options.canEdit() || !Number.isSafeInteger(request.sequence) || request.sequence !== sequence + 1
        || !Number.isSafeInteger(request.importGeneration) || request.importGeneration < 1)
        fail("The native command is out of order or its authoring authorization is unavailable.");
      const before = options.readLive();
      if (!sameToken(request.token, before.token) || before.token.authorityId !== initial.token.authorityId
        || before.token.resourceId !== initial.token.resourceId || before.token.scopeId !== initial.token.scopeId
        || readBpmnDocument(before.owner).payload.documentXml !== request.sourceXml)
        fail("The native command baseline no longer matches the canonical document.");
      const plan = (options.plan ?? planBpmnDocumentSyntaxChange)(before.owner, request.commands);
      if (plan.unqualifiedOwnershipIds.length)
        fail("Native ownership needs an exact catalog descriptor before this deletion can clean its side tables.");
      const candidate = applyBpmnDocumentTransaction(before.owner, plan.transaction);
      if (!options.admit(before, candidate, plan)) fail("The resource host refused the native command.");
      const after = options.readLive();
      if (after.token.authorityId !== before.token.authorityId || after.token.resourceId !== before.token.resourceId
        || after.token.scopeId !== before.token.scopeId || after.token.revision < before.token.revision
        || JSON.stringify(after.owner) !== JSON.stringify(candidate))
        fail("The resource host did not commit the requested native source.");
      const acknowledgement = Object.freeze({ owner: structuredClone(after.owner), token: Object.freeze({ ...after.token }), acceptedSequence: request.sequence });
      const admission: Admission = { kind: "admitted", digest, acknowledgement };
      // Persist the actual admission before issuing its receipt. A lost/failed receipt is recoverable
      // without applying the XML command a second time.
      admissions.set(request.sequence, admission);
      sequence = request.sequence;
      try { options.onWorkChanged?.(); } catch { /* Observation cannot revoke an actual canonical admission. */ }
      return issue(admission);
    },
    detach: registration.detach,
    reattach: registration.reattach,
    retryRelease: registration.retryRelease,
    retire() { registration.retire(); retired = true; }
  };
}
