/** Local authoring identity, separate from the backend's persisted draft revision. */
export interface AuthoringCheckpointToken {
  readonly authorityId: string;
  readonly resourceId: string;
  readonly scopeId: string;
  readonly revision: number;
}

export interface AuthoringCheckpointCapture<TSnapshot> {
  readonly token: AuthoringCheckpointToken;
  readonly snapshot: TSnapshot;
  readonly lineage: readonly string[];
}

export interface AuthoringParticipantWork {
  readonly pending: boolean;
  readonly sequence: number;
  readonly acceptedSequence: number;
}

/** Issued by the resource host only after it acknowledges the canonical document. */
export interface AuthoringParentReceipt {
  readonly token: AuthoringCheckpointToken;
  readonly acceptedSequence: number;
}

export interface AuthoringCheckpointLease {
  readonly receipt: AuthoringParentReceipt;
  /** Idempotent cleanup: a throw retains the lease for an explicit retry. */
  release(): void;
}

export type AuthoringCheckpointIntent = "autosave" | "save" | "publish" | "test-run" | "export"
  | "undo" | "redo" | "switch-editor" | "navigate" | "scope-change" | "view-change";

export interface AuthoringCheckpointParticipant {
  /** Cached synchronous facts must remain available when a fallible view detaches. */
  readWork(): AuthoringParticipantWork;
  /** Complete active editing, obtain parent acknowledgement, and hold a mutation fence until release. */
  checkpoint(request: { readonly intent: AuthoringCheckpointIntent; readonly token: AuthoringCheckpointToken }): Promise<AuthoringCheckpointLease>;
}

export interface AuthoringCheckpointRegistration {
  readonly initialReceipt: AuthoringParentReceipt;
  /** Called by the resource host after canonical admission, never by an optimistic child view. */
  acknowledge(acceptedSequence: number, token: AuthoringCheckpointToken): AuthoringParentReceipt;
  /** Detaching a view retains its controller and outstanding work. */
  detach(): void;
  reattach(): void;
  /** Retry failed fence cleanup without applying a completed history/navigation action again. */
  retryRelease(): void;
  /** Explicitly remove a clean, idle registration. The controller is never disposed here. */
  retire(): void;
}

export class AuthoringCheckpointError extends Error {
  constructor(readonly code: "stale-token" | "participants-changed" | "pending-work" | "invalid-receipt"
    | "registration-in-use" | "invalid-work" | "async-transition" | "fence-release", message: string) {
    super(message);
    this.name = "AuthoringCheckpointError";
  }
}

export interface AuthoringTransitionOutcome {
  readonly applied: true;
  /** The transition already happened. Report/retry fence cleanup, never repeat the transition. */
  readonly cleanupFailure?: AggregateError;
}

export interface AuthoringCheckpointCoordinator<TSnapshot> {
  register(id: string, participant: AuthoringCheckpointParticipant, options?: { readonly order?: number }): AuthoringCheckpointRegistration;
  capture(intent: AuthoringCheckpointIntent, options?: { signal?: AbortSignal }): Promise<AuthoringCheckpointCapture<TSnapshot>>;
  /** The callback commits synchronously while participant fences are held; cleanup failure never means retry the action. */
  transition(intent: AuthoringCheckpointIntent, action: (live: AuthoringCheckpointCapture<TSnapshot>) => undefined,
    options?: { signal?: AbortSignal }): Promise<AuthoringTransitionOutcome>;
  /** Navigation probes never serialize, acknowledge, release, or dispose participant work. */
  probe(): boolean;
  pendingParticipantIds(): readonly string[];
}

function token(value: AuthoringCheckpointToken): AuthoringCheckpointToken {
  if (!value.authorityId || !value.resourceId || !value.scopeId || !Number.isSafeInteger(value.revision) || value.revision < 0)
    throw new AuthoringCheckpointError("stale-token", "The authoring document identity or revision is unavailable.");
  return Object.freeze({ ...value });
}

function sameIdentity(left: AuthoringCheckpointToken, right: AuthoringCheckpointToken) {
  return left.authorityId === right.authorityId && left.resourceId === right.resourceId && left.scopeId === right.scopeId;
}

function sameToken(left: AuthoringCheckpointToken, right: AuthoringCheckpointToken) {
  return sameIdentity(left, right) && left.revision === right.revision;
}

function work(participant: AuthoringCheckpointParticipant): AuthoringParticipantWork {
  const value = participant.readWork();
  if (typeof value.pending !== "boolean" || !Number.isSafeInteger(value.sequence) || value.sequence < 0
    || !Number.isSafeInteger(value.acceptedSequence) || value.acceptedSequence < 0 || value.acceptedSequence > value.sequence)
    throw new AuthoringCheckpointError("invalid-work", "The canvas reported invalid acknowledgement facts.");
  return value;
}

function pending(value: AuthoringParticipantWork) {
  return value.pending || value.sequence !== value.acceptedSequence;
}

function abortError() { return new DOMException("The authoring action was cancelled.", "AbortError"); }

/** One instance belongs to one Workflow/Activity Definition authoring resource; no global registry. */
export function createAuthoringCheckpoint<TSnapshot>(options: {
  readLive(): AuthoringCheckpointCapture<TSnapshot>;
}): AuthoringCheckpointCoordinator<TSnapshot> {
  interface Registration {
    id: string;
    participant: AuthoringCheckpointParticipant;
    identity: AuthoringCheckpointToken;
    receipt: AuthoringParentReceipt;
    attached: boolean;
    checkpoints: number;
    failedLeases: AuthoringCheckpointLease[];
    observedSequence: number;
    observedAcceptedSequence: number;
    order: number;
  }
  type Result = AuthoringCheckpointCapture<TSnapshot> | AuthoringTransitionOutcome | undefined;
  interface Waiter {
    active: boolean;
    resolve(value: Result): void;
    reject(error: unknown): void;
    cleanup(): void;
  }
  interface Job {
    intent: AuthoringCheckpointIntent;
    requested: AuthoringCheckpointToken;
    action?: (live: AuthoringCheckpointCapture<TSnapshot>) => undefined;
    waiters: Waiter[];
    completed?: boolean;
    sealed?: boolean;
  }
  const resourceId = token(options.readLive().token).resourceId;
  const registrations = new Map<string, Registration>();
  const jobs: Job[] = [];
  let membership = 0;
  let draining = false;

  const readLive = () => {
    const live = options.readLive();
    const current = token(live.token);
    if (current.resourceId !== resourceId)
      throw new AuthoringCheckpointError("stale-token", "The checkpoint belongs to a different authoring resource.");
    return { ...live, token: current };
  };
  const currentRegistration = (record: Registration) => {
    if (registrations.get(record.id) !== record)
      throw new AuthoringCheckpointError("registration-in-use", "This canvas registration has retired.");
  };
  const readRecordWork = (record: Registration) => {
    const facts = work(record.participant);
    if (facts.sequence < record.observedSequence || facts.acceptedSequence < record.observedAcceptedSequence)
      throw new AuthoringCheckpointError("invalid-work", "A retained canvas cannot reset its outstanding command or acknowledgement sequence.");
    record.observedSequence = facts.sequence;
    record.observedAcceptedSequence = facts.acceptedSequence;
    return facts;
  };
  const recordPending = (record: Registration) => {
    const facts = readRecordWork(record);
    return pending(facts) || facts.acceptedSequence !== record.receipt.acceptedSequence || record.checkpoints > 0;
  };
  const interested = (job: Job) => job.waiters.some(waiter => waiter.active);
  const copyCapture = (live: AuthoringCheckpointCapture<TSnapshot>): AuthoringCheckpointCapture<TSnapshot> => Object.freeze({
    token: live.token, snapshot: structuredClone(live.snapshot), lineage: Object.freeze([...live.lineage])
  });
  const applyTransition = (action: NonNullable<Job["action"]>, capture: AuthoringCheckpointCapture<TSnapshot>) => {
    const result: unknown = action(capture);
    if (result && (typeof result === "object" || typeof result === "function") && "then" in result) {
      void Promise.resolve(result).catch(() => undefined);
      throw new AuthoringCheckpointError("async-transition", "An authoring transition must finish synchronously while its canvas fence is held.");
    }
    return Object.freeze({ applied: true as const });
  };

  async function execute(job: Job): Promise<Result> {
    const start = readLive();
    if (!sameIdentity(start.token, job.requested) || start.token.revision < job.requested.revision)
      throw new AuthoringCheckpointError("stale-token", "The authoring authority or scope changed before this action could start.");
    const version = membership;
    const records = [...registrations.values()].filter(record => {
      const hasPending = recordPending(record);
      return record.attached || hasPending;
    }).sort((left, right) => left.order - right.order);
    const held: { record: Registration; lease: AuthoringCheckpointLease }[] = [];
    let latest = start.token;
    const prepare = async () => {
      for (const record of records) {
        if (!interested(job)) return;
        if (record.failedLeases.length)
          throw new AuthoringCheckpointError("fence-release", "A retained canvas fence needs cleanup before another authoring action.");
        if (!sameIdentity(record.identity, start.token))
          throw new AuthoringCheckpointError("stale-token", "Retained canvas work belongs to another authoring authority or scope.");
        const before = readLive();
        if (!sameIdentity(before.token, start.token) || before.token.revision < latest.revision)
          throw new AuthoringCheckpointError("stale-token", "The authoring document changed identity while preparing this action.");
        record.checkpoints++;
        let lease: AuthoringCheckpointLease;
        try { lease = await record.participant.checkpoint({ intent: job.intent, token: before.token }); }
        catch (error) { record.checkpoints--; throw error; }
        held.push({ record, lease });
        const live = readLive();
        const facts = readRecordWork(record);
        if (membership !== version)
          throw new AuthoringCheckpointError("participants-changed", "The active authoring canvases changed while preparing this action.");
        if (!sameIdentity(live.token, start.token) || live.token.revision < before.token.revision)
          throw new AuthoringCheckpointError("stale-token", "The authoring authority, scope or revision changed during its checkpoint.");
        if (lease.receipt !== record.receipt || !sameToken(lease.receipt.token, live.token))
          throw new AuthoringCheckpointError("invalid-receipt", "The resource host did not acknowledge this exact canvas checkpoint.");
        if (pending(facts) || facts.acceptedSequence !== lease.receipt.acceptedSequence)
          throw new AuthoringCheckpointError("pending-work", "The canvas still has unacknowledged edits.");
        latest = live.token;
      }
      if (!interested(job)) return;
      const live = readLive();
      if (membership !== version)
        throw new AuthoringCheckpointError("participants-changed", "The active authoring canvases changed before capture.");
      if (!sameToken(live.token, latest))
        throw new AuthoringCheckpointError("stale-token", "The authoring document changed after its checkpoint.");
      for (const { record, lease } of held) {
        const facts = readRecordWork(record);
        if (pending(facts) || record.receipt !== lease.receipt || facts.acceptedSequence !== lease.receipt.acceptedSequence)
          throw new AuthoringCheckpointError("pending-work", "A fenced canvas received another unacknowledged edit.");
      }
      job.sealed = true;
      const capture = copyCapture(live);
      job.completed = true;
      if (!job.action) return capture;
      return applyTransition(job.action, capture);
    };
    let result: Result;
    let failure: unknown;
    let failed = false;
    try { result = await prepare(); }
    catch (error) { failed = true; failure = error; }
    // Close every path, including cancellation/rejection, before release can re-enter the queue.
    job.sealed = true;
    const failures: unknown[] = [];
    for (const { record, lease } of held.reverse()) {
      try { lease.release(); record.checkpoints--; }
      catch (error) { record.failedLeases.push(lease); failures.push(error); }
    }
    if (failed) throw failure;
    if (failures.length) {
      const cleanupFailure = new AggregateError(failures, "Could not release an authoring canvas fence.");
      if (job.action) return Object.freeze({ applied: true, cleanupFailure });
      throw cleanupFailure;
    }
    return result;
  }

  async function drain() {
    if (draining) return;
    draining = true;
    try {
      while (jobs.length) {
        const job = jobs[0];
        let result: Result;
        let failure: unknown;
        let failed = false;
        try { if (interested(job)) result = await execute(job); }
        catch (error) { failed = true; failure = error; }
        job.sealed = true;
        jobs.shift();
        for (const waiter of job.waiters) {
          waiter.cleanup();
          if (!waiter.active) continue;
          waiter.active = false;
          if (failed) waiter.reject(failure);
          else waiter.resolve(result);
        }
      }
    } finally { draining = false; }
  }

  function enqueue(intent: AuthoringCheckpointIntent, action: Job["action"], signal?: AbortSignal) {
    if (signal?.aborted) return Promise.reject(abortError());
    let requested: AuthoringCheckpointToken;
    try { requested = readLive().token; } catch (error) { return Promise.reject(error); }
    if (action && !registrations.size && !jobs.length && !draining) {
      // The module-absent editor has nothing to checkpoint. Reserve its place before executing so
      // reentrant captures/transitions still queue behind this action, rather than running inside it.
      const job: Job = { intent, requested, action, waiters: [], sealed: true };
      jobs.push(job);
      try {
        const live = readLive();
        if (!sameIdentity(live.token, requested) || live.token.revision < requested.revision)
          throw new AuthoringCheckpointError("stale-token", "The authoring authority or scope changed before this action could start.");
        const capture = copyCapture(live);
        if (registrations.size) throw new AuthoringCheckpointError("participants-changed", "An authoring canvas registered before this transition.");
        if (signal?.aborted) throw abortError();
        job.completed = true;
        return Promise.resolve(applyTransition(action, capture));
      } catch (error) { return Promise.reject(error); }
      finally {
        jobs.shift();
        if (jobs.length) queueMicrotask(() => { void drain(); });
      }
    }
    const tail = jobs.at(-1);
    // Coalesce only adjacent automatic captures: never cross an intervening history/switch intent.
    const job = !action && intent === "autosave" && tail?.intent === "autosave" && !tail.action && !tail.sealed && sameIdentity(tail.requested, requested)
      ? tail : { intent, requested, action, waiters: [] };
    if (job !== tail) jobs.push(job);
    const promise = new Promise<Result>((resolve, reject) => {
      const cancel = () => { if (waiter.active && !job.completed) { waiter.active = false; waiter.cleanup(); reject(abortError()); } };
      const waiter: Waiter = { active: true, resolve, reject, cleanup: () => signal?.removeEventListener("abort", cancel) };
      job.waiters.push(waiter);
      signal?.addEventListener("abort", cancel, { once: true });
      if (signal?.aborted) cancel();
    });
    // A waiter's signal is never forwarded to a shared participant acknowledgement.
    queueMicrotask(() => { void drain(); });
    return promise;
  }

  const pendingParticipantIds = () => [...registrations.values()].filter(record => {
    try { return recordPending(record); } catch { return true; }
  }).map(record => record.id);

  return {
    register(id, participant, request) {
      if (!id || registrations.has(id)) throw new AuthoringCheckpointError("registration-in-use", "A canvas registration with this identity already exists.");
      const order = request?.order ?? 0;
      if (!Number.isSafeInteger(order)) throw new AuthoringCheckpointError("invalid-work", "The canvas checkpoint order must be a safe integer.");
      const initial = readLive().token;
      const facts = work(participant);
      if (facts.acceptedSequence !== 0)
        throw new AuthoringCheckpointError("invalid-work", "A new canvas registration cannot claim prior parent acknowledgements.");
      const initialReceipt = Object.freeze({ token: initial, acceptedSequence: 0 });
      const record: Registration = { id, participant, identity: initial, receipt: initialReceipt, attached: true, checkpoints: 0,
        failedLeases: [], observedSequence: facts.sequence, observedAcceptedSequence: facts.acceptedSequence, order };
      registrations.set(id, record); membership++;
      return {
        initialReceipt,
        acknowledge(acceptedSequence, expected) {
          currentRegistration(record);
          const live = readLive().token;
          const facts = readRecordWork(record);
          if (!sameIdentity(record.identity, live) || !sameToken(expected, live)
            || !Number.isSafeInteger(acceptedSequence) || acceptedSequence < record.receipt.acceptedSequence || acceptedSequence > facts.sequence
            || live.revision < record.receipt.token.revision)
            throw new AuthoringCheckpointError("invalid-receipt", "The parent acknowledgement does not match the current canonical authoring document.");
          if (acceptedSequence === record.receipt.acceptedSequence && sameToken(live, record.receipt.token)) return record.receipt;
          return record.receipt = Object.freeze({ token: live, acceptedSequence });
        },
        detach() { currentRegistration(record); record.attached = false; },
        reattach() { currentRegistration(record); if (!record.attached) { record.attached = true; membership++; } },
        retryRelease() {
          currentRegistration(record);
          const failures: unknown[] = [];
          const retry = record.failedLeases.splice(0);
          for (const lease of retry) {
            try { lease.release(); record.checkpoints--; }
            catch (error) { record.failedLeases.push(lease); failures.push(error); }
          }
          if (failures.length) throw new AggregateError(failures, "Could not release the retained authoring canvas fence.");
        },
        retire() {
          currentRegistration(record);
          if (record.checkpoints || recordPending(record))
            throw new AuthoringCheckpointError("registration-in-use", "Pending or fenced canvas work must be retained until it is acknowledged.");
          registrations.delete(id); membership++;
        }
      };
    },
    capture: (intent, request) => enqueue(intent, undefined, request?.signal) as Promise<AuthoringCheckpointCapture<TSnapshot>>,
    transition: (intent, action, request) => enqueue(intent, action, request?.signal) as Promise<AuthoringTransitionOutcome>,
    probe: () => jobs.length > 0 || pendingParticipantIds().length > 0,
    pendingParticipantIds
  };
}
