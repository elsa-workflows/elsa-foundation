import type { ActivityNode, AuthoringCheckpointToken, AuthoringParticipantWork, BpmnDocumentAuthoring, BpmnDocumentCommand, BpmnScopeAddress } from "@elsa-workflows/studio-workflows";
import { bindBpmnNativeBaseline, projectBpmnNativeDocument, type BpmnNativeBaseline } from "./bpmnDocumentProjection";
import { mapBpmnNativeCommandPlan, type NativeCommandGroup } from "./bpmnDocumentCommands";
import { nativeRefuse } from "./bpmnDocumentXml";

/** Private modeler dependency. The future controlled host contract never receives this object. */
export interface NativeDocumentModeler {
  importXML(xml: string, diagramId: string): Promise<{ warnings: readonly unknown[] }>;
  /** Native view navigation, including generated subprocess planes; must succeed before readiness. */
  activateScope(scopeElementId: string): void;
  /** Capture at invocation, before a later gesture; qualify this with the pinned native writer in the browser. */
  saveXML(): Promise<{ xml: string }>;
  completeEditing(): void;
  clearHistory(): void;
}
export interface NativeDocumentSource { readonly owner: ActivityNode; readonly token: AuthoringCheckpointToken }
export interface NativeDocumentAcknowledgement extends NativeDocumentSource { readonly acceptedSequence: number }
export interface NativeDocumentCommit {
  readonly token: AuthoringCheckpointToken;
  readonly sourceXml: string;
  readonly importGeneration: number;
  readonly sequence: number;
  readonly commands: readonly BpmnDocumentCommand[];
}
export interface BpmnDocumentModelerSession {
  open(): Promise<void>;
  enqueue(group: NativeCommandGroup): void;
  /** Collector/command failure retains the actual native view until explicit host recovery. */
  refuse(error: unknown): void;
  readWork(): AuthoringParticipantWork;
  canMutate(): boolean;
  flush(): Promise<NativeDocumentAcknowledgement>;
  checkpoint(): Promise<{ readonly acknowledgement: NativeDocumentAcknowledgement; release(): void }>;
  retry(): Promise<NativeDocumentAcknowledgement>;
}
function sameToken(left: AuthoringCheckpointToken, right: AuthoringCheckpointToken) {
  return left.authorityId === right.authorityId && left.resourceId === right.resourceId && left.scopeId === right.scopeId && left.revision === right.revision;
}
function copySource(source: NativeDocumentSource): NativeDocumentSource {
  return Object.freeze({ owner: source.owner, token: Object.freeze({ ...source.token }) });
}
function sealRequest(request: NativeDocumentCommit): NativeDocumentCommit {
  const commands = structuredClone(request.commands);
  const freeze = (value: unknown) => {
    if (value && typeof value === "object") {
      for (const child of Object.values(value)) freeze(child);
      Object.freeze(value);
    }
  };
  freeze(commands);
  return Object.freeze({ ...request, token: Object.freeze({ ...request.token }), commands });
}

/** Unenabled schema2 session: ordered captured serializations, exact parent CAS, no optimistic baseline. */
export function createBpmnDocumentModelerSession(options: {
  authoring: BpmnDocumentAuthoring;
  modeler: NativeDocumentModeler;
  scope: BpmnScopeAddress;
  readLive(): NativeDocumentSource;
  /** Only the canonical owner issues this receipt. An uncertain admission stays retained until recovery. */
  commit(request: NativeDocumentCommit): Promise<NativeDocumentAcknowledgement>;
  /** Recover the exact prior request before considering another admission attempt. */
  lookup?(request: NativeDocumentCommit): Promise<NativeDocumentAcknowledgement | undefined>;
  /** Observation only; a failed observer cannot discard actual native work or canonical acknowledgement. */
  onWorkChanged?(): void;
}): BpmnDocumentModelerSession {
  const { authoring, modeler } = options;
  const scope = Object.freeze({ ...options.scope });
  const initial = copySource(options.readLive());
  let acknowledged: NativeDocumentAcknowledgement = Object.freeze({ ...initial, acceptedSequence: 0 });
  let baseline: BpmnNativeBaseline | undefined;
  let generation = 0, sequence = 0, fences = 0;
  let phase: "closed" | "importing" | "ready" | "failed" = "closed";
  let completing = false, externalPending = false;
  let failure: unknown;
  let hasFailure = false;
  let opening: Promise<void> | undefined, running: Promise<void> | undefined;
  const pending: { sequence: number; generation: number; group: NativeCommandGroup; next: Promise<{ xml: string }>;
    prepared?: { request: NativeDocumentCommit; candidateXml: string; baseline: BpmnNativeBaseline }; attempted?: boolean }[] = [];
  const sourceXml = (owner: ActivityNode) => authoring.readBpmnDocument(owner).payload.documentXml;
  const liveMatches = (source: NativeDocumentSource, expectedXml: string) => {
    const live = options.readLive();
    return sameToken(live.token, source.token) && sourceXml(live.owner) === expectedXml;
  };
  const work = (): AuthoringParticipantWork => ({ pending: phase === "importing" || externalPending || pending.length > 0 || sequence !== acknowledged.acceptedSequence,
    sequence, acceptedSequence: acknowledged.acceptedSequence });
  const notify = () => { try { options.onWorkChanged?.(); } catch { /* Observation cannot revoke retained work. */ } };
  const fail = (error: unknown) => { failure = error; hasFailure = true; phase = "failed"; notify(); };

  async function drain() {
    try {
      while (pending.length) {
        const item = pending[0];
        const saved = await item.next;
        if (!baseline || item.generation !== generation) nativeRefuse("source authority/revision changed before native admission");
        if (!item.prepared) {
          if (!liveMatches(acknowledged, baseline.projection.sourceXml)) nativeRefuse("source authority/revision changed before native admission");
          const mapped = mapBpmnNativeCommandPlan(authoring, baseline, item.group, saved.xml), commands = mapped.commands;
          // Use the owning transaction validator; no native XML is ever assigned as canonical source.
          const plan = authoring.planBpmnDocumentSyntaxChange(acknowledged.owner, commands);
          const candidate = authoring.applyBpmnDocumentTransaction(acknowledged.owner, plan.transaction);
          // Complete fallible local reconciliation before asking the parent to admit the edit.
          item.prepared = { candidateXml: sourceXml(candidate),
            baseline: bindBpmnNativeBaseline(authoring, projectBpmnNativeDocument(authoring, candidate, scope), saved.xml, baseline, mapped.diAliases),
            request: sealRequest({ token: acknowledged.token, sourceXml: baseline.projection.sourceXml,
              importGeneration: generation, sequence: item.sequence, commands }) };
        }
        const prepared = item.prepared;
        // A rejected transport may have followed an actual admission. Lookup is read-only and must
        // precede the old-baseline check, since that successful admission advanced canonical source.
        let receipt = item.attempted ? await options.lookup?.(prepared.request) : undefined;
        if (!receipt) {
          if (!liveMatches(acknowledged, baseline.projection.sourceXml)) nativeRefuse("source authority/revision changed before native admission");
          item.attempted = true;
          receipt = await options.commit(prepared.request);
        }
        if (item.generation !== generation || receipt.acceptedSequence !== item.sequence || !sameToken(receipt.token, options.readLive().token)
          || receipt.token.authorityId !== acknowledged.token.authorityId || receipt.token.resourceId !== acknowledged.token.resourceId
          || receipt.token.scopeId !== acknowledged.token.scopeId || receipt.token.revision < acknowledged.token.revision
          || sourceXml(receipt.owner) !== prepared.candidateXml || !liveMatches(receipt, prepared.candidateXml)) nativeRefuse("parent receipt does not acknowledge this exact source command group");
        // Advance raw authority and modeled baseline only together after exact parent acknowledgement.
        baseline = Object.freeze({ ...prepared.baseline, projection: Object.freeze({ ...prepared.baseline.projection, owner: receipt.owner }) });
        acknowledged = Object.freeze({ ...copySource(receipt), acceptedSequence: item.sequence });
        pending.shift();
        notify();
      }
      if (!hasFailure) phase = "ready";
    } catch (error) { fail(error); throw error; }
  }
  function start() {
    if (running || hasFailure || !pending.length) return;
    const task = drain();
    running = task;
    void task.finally(() => { if (running === task) running = undefined; }).catch(() => undefined);
  }
  const flush = async () => {
    if (opening) await opening;
    if (hasFailure) throw failure;
    if (!baseline) nativeRefuse("native document has not completed import");
    while (pending.length || running) {
      start();
      if (running) await running;
      if (hasFailure) throw failure;
    }
    if (!liveMatches(acknowledged, baseline.projection.sourceXml)) nativeRefuse("canonical source changed after native acknowledgement");
    return acknowledged;
  };
  return {
    open() {
      if (opening) return opening;
      if (work().pending || fences) return Promise.reject(new Error("Pending native work must be retained before importing another source"));
      const source = copySource(options.readLive());
      if (source.token.authorityId !== initial.token.authorityId || source.token.resourceId !== initial.token.resourceId || source.token.scopeId !== initial.token.scopeId)
        return Promise.reject(new Error("A new authoring identity/scope requires a new retained session registration"));
      const currentGeneration = ++generation;
      phase = "importing"; hasFailure = false; failure = undefined; notify();
      const task = (async () => {
        try {
          const projection = projectBpmnNativeDocument(authoring, source.owner, scope);
          const imported = await modeler.importXML(projection.xml, projection.scope.diagramId);
          if (imported.warnings.length) nativeRefuse("native import reported unexplained warnings");
          modeler.activateScope(projection.scope.scopeElementId);
          const saved = await modeler.saveXML();
          if (currentGeneration !== generation || !liveMatches(source, projection.sourceXml)) nativeRefuse("canonical source changed during native import");
          baseline = bindBpmnNativeBaseline(authoring, projection, saved.xml);
          acknowledged = Object.freeze({ ...source, acceptedSequence: acknowledged.acceptedSequence });
          phase = "ready";
          modeler.clearHistory();
          notify();
        } catch (error) { fail(error); throw error; }
      })();
      opening = task;
      void task.finally(() => { if (opening === task) opening = undefined; }).catch(() => undefined);
      return task;
    },
    enqueue(group) {
      if (!baseline || phase !== "ready" || hasFailure || fences && !completing) nativeRefuse("native mutation arrived outside its ready unfenced session");
      const copied = structuredClone(group);
      // Invoke saveXML immediately at the complete command boundary, not after an earlier acknowledgement.
      let next: Promise<{ xml: string }>;
      try { next = modeler.saveXML(); } catch (error) { next = Promise.reject(error); }
      void next.catch(() => undefined);
      pending.push({ sequence: ++sequence, generation, group: copied, next });
      notify();
      start();
    },
    refuse(error) { if (!externalPending) sequence++; externalPending = true; fail(error); },
    readWork: work,
    canMutate: () => {
      try { return !!baseline && phase === "ready" && !hasFailure && (!fences || completing) && liveMatches(acknowledged, baseline.projection.sourceXml); }
      catch { return false; }
    },
    flush,
    async checkpoint() {
      fences++;
      try {
        if (opening) await opening.catch(error => { if (pending.length || externalPending) throw error; });
        completing = true;
        try { modeler.completeEditing(); } finally { completing = false; }
        // A hidden clean view need not import a later inspector/history document merely to
        // acknowledge it. Invalidate stale visual correspondence before issuing the exact receipt.
        let acknowledgement: NativeDocumentAcknowledgement;
        if (!externalPending && !pending.length && sequence === acknowledged.acceptedSequence) {
          const source = copySource(options.readLive());
          if (source.token.authorityId !== initial.token.authorityId || source.token.resourceId !== initial.token.resourceId || source.token.scopeId !== initial.token.scopeId)
            nativeRefuse("clean native acknowledgement belongs to another authoring identity");
          if (baseline) {
            let sameSource = false;
            try { sameSource = sourceXml(source.owner) === baseline.projection.sourceXml; } catch { /* Detached legacy preparation is temporarily unavailable. */ }
            if (!sameSource) { baseline = undefined; phase = "closed"; }
          }
          acknowledged = Object.freeze({ ...source, acceptedSequence: acknowledged.acceptedSequence });
          acknowledgement = acknowledged;
        } else acknowledgement = await flush();
        let held = true;
        return { acknowledgement, release() { if (held) { held = false; fences--; } } };
      } catch (error) { fences--; throw error; }
    },
    retry() {
      if (externalPending) return Promise.reject(failure);
      if (!baseline || !pending.length) return flush();
      hasFailure = false; failure = undefined; phase = "ready";
      start(); return flush();
    }
  };
}
