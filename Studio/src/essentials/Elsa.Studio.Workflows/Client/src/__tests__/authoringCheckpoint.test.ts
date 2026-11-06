// @vitest-environment node
import { describe, expect, it, vi } from "vitest";
import {
  createAuthoringCheckpoint,
  type AuthoringCheckpointCapture,
  type AuthoringCheckpointLease,
  type AuthoringCheckpointParticipant,
  type AuthoringParticipantWork
} from "../graph-authoring/authoringCheckpoint";

function deferred<T>() {
  let resolve!: (value: T) => void;
  let reject!: (error: unknown) => void;
  const promise = new Promise<T>((yes, no) => { resolve = yes; reject = no; });
  return { promise, resolve, reject };
}

interface Snapshot { name: string; configuration: { expression: string } }
interface Acquisition {
  request: Parameters<AuthoringCheckpointParticipant["checkpoint"]>[0];
  gate: ReturnType<typeof deferred<AuthoringCheckpointLease>>;
  release: ReturnType<typeof vi.fn>;
}

function host() {
  let live: AuthoringCheckpointCapture<Snapshot> = {
    token: { authorityId: "session", resourceId: "draft", scopeId: "root", revision: 0 },
    snapshot: { name: "before", configuration: { expression: "original" } }, lineage: ["before"]
  };
  const readLive = vi.fn(() => live);
  const coordinator = createAuthoringCheckpoint({ readLive });
  const events: string[] = [];
  const replace = (changes: Partial<AuthoringCheckpointCapture<Snapshot>>) => { live = { ...live, ...changes }; };
  function register(id = "canvas", initiallyPending = true) {
    const facts: { value: AuthoringParticipantWork } = { value: { pending: initiallyPending, sequence: initiallyPending ? 1 : 0, acceptedSequence: 0 } };
    const acquisitions: Acquisition[] = [];
    let fences = 0;
    const participant: AuthoringCheckpointParticipant & { dispose: ReturnType<typeof vi.fn> } = {
      readWork: vi.fn(() => facts.value),
      checkpoint: vi.fn(request => {
        events.push(`${id}:checkpoint:${request.intent}`);
        fences++;
        const gate = deferred<AuthoringCheckpointLease>();
        const release = vi.fn(() => { fences--; events.push(`${id}:release`); });
        acquisitions.push({ request, gate, release });
        return gate.promise;
      }),
      dispose: vi.fn()
    };
    const registration = coordinator.register(id, participant);
    return { id, facts, acquisitions, participant, registration, get fences() { return fences; } };
  }
  function acknowledge(control: ReturnType<typeof register>, acquisition: Acquisition, advance = true) {
    if (advance) replace({ token: { ...live.token, revision: live.token.revision + 1 } });
    const sequence = control.facts.value.sequence;
    const receipt = control.registration.acknowledge(sequence, live.token);
    control.facts.value = { pending: false, sequence, acceptedSequence: sequence };
    events.push(`${control.id}:parent-ack`);
    acquisition.gate.resolve({ receipt, release: acquisition.release });
    return receipt;
  }
  return { coordinator, readLive, register, acknowledge, replace, events, get live() { return live; } };
}

async function acquisition(control: ReturnType<ReturnType<typeof host>["register"]>, index = 0) {
  await vi.waitFor(() => expect(control.acquisitions).toHaveLength(index + 1));
  return control.acquisitions[index];
}

describe("resource-local authoring checkpoint", () => {
  it("captures the live canonical snapshot and lineage after delayed parent admission, then releases the fence", async () => {
    const h = host();
    const canvas = h.register();
    const settled = vi.fn();
    const result = h.coordinator.capture("save").then(value => { settled(value); return value; });
    const request = await acquisition(canvas);
    expect(request.request.token.revision).toBe(0);
    expect(settled).not.toHaveBeenCalled();
    expect(canvas.fences).toBe(1);
    h.replace({ snapshot: { name: "accepted native edit", configuration: { expression: "new shared inspector edit" } }, lineage: ["native", "inspector"] });
    h.acknowledge(canvas, request);
    const captured = await result;
    expect(captured).toEqual({ ...h.live, token: { ...h.live.token, revision: 1 } });
    expect(captured.snapshot).not.toBe(h.live.snapshot);
    expect(captured.lineage).not.toBe(h.live.lineage);
    h.live.snapshot.configuration.expression = "later mutable caller";
    expect(captured.snapshot.configuration.expression).toBe("new shared inspector edit");
    expect(request.release).toHaveBeenCalledOnce();
    expect(canvas.fences).toBe(0);
  });

  it.each(["undo", "redo", "switch-editor", "navigate"] as const)("runs %s synchronously after acknowledgement and before fence release", async intent => {
    const h = host(); const canvas = h.register();
    const action = vi.fn((capture: AuthoringCheckpointCapture<Snapshot>): undefined => {
      expect(canvas.fences).toBe(1);
      expect(capture.token.revision).toBe(1);
      expect(capture.snapshot.name).toBe("accepted");
      h.events.push(intent);
      h.replace({ token: { ...h.live.token, scopeId: "next", revision: 2 } });
    });
    const result = h.coordinator.transition(intent, action);
    const request = await acquisition(canvas);
    expect(action).not.toHaveBeenCalled();
    h.replace({ snapshot: { ...h.live.snapshot, name: "accepted" } });
    h.acknowledge(canvas, request);
    await result;
    expect(h.events).toEqual([`canvas:checkpoint:${intent}`, "canvas:parent-ack", intent, "canvas:release"]);
    expect(action).toHaveBeenCalledOnce();
  });

  it("coalesces adjacent automatic captures while preserving one native acknowledgement", async () => {
    const h = host(); const canvas = h.register();
    const first = h.coordinator.capture("autosave");
    const second = h.coordinator.capture("autosave");
    const request = await acquisition(canvas);
    h.acknowledge(canvas, request);
    const [a, b] = await Promise.all([first, second]);
    expect(a).toBe(b);
    expect(canvas.participant.checkpoint).toHaveBeenCalledOnce();
    expect(request.release).toHaveBeenCalledOnce();
  });

  it("starts a fresh autosave for a resumed edit requested reentrantly during fence release", async () => {
    const h = host(); const canvas = h.register();
    const first = h.coordinator.capture("autosave");
    const request = await acquisition(canvas);
    let resumed: Promise<AuthoringCheckpointCapture<Snapshot>> | undefined;
    const release = request.release.getMockImplementation()!;
    request.release.mockImplementation(() => {
      release();
      h.replace({ token: { ...h.live.token, revision: 2 }, snapshot: { ...h.live.snapshot, name: "resumed edit" }, lineage: ["resumed"] });
      resumed = h.coordinator.capture("autosave");
    });
    h.acknowledge(canvas, request);
    expect((await first).token.revision).toBe(1);
    const next = await acquisition(canvas, 1);
    h.acknowledge(canvas, next, false);
    expect(await resumed).toMatchObject({ token: { revision: 2 }, snapshot: { name: "resumed edit" }, lineage: ["resumed"] });
  });

  it("seals an abandoned autosave before release can request a new capture", async () => {
    const h = host(); const canvas = h.register(); const abort = new AbortController();
    const first = h.coordinator.capture("autosave", { signal: abort.signal });
    const cancelled = expect(first).rejects.toMatchObject({ name: "AbortError" });
    const request = await acquisition(canvas);
    abort.abort(); await cancelled;
    let resumed: Promise<AuthoringCheckpointCapture<Snapshot>> | undefined;
    const release = request.release.getMockImplementation()!;
    request.release.mockImplementation(() => {
      release();
      h.replace({ token: { ...h.live.token, revision: 2 }, snapshot: { ...h.live.snapshot, name: "after cancellation" } });
      resumed = h.coordinator.capture("autosave");
    });
    h.acknowledge(canvas, request);
    const next = await acquisition(canvas, 1);
    h.acknowledge(canvas, next, false);
    expect(await resumed).toMatchObject({ token: { revision: 2 }, snapshot: { name: "after cancellation" } });
  });

  it("does not coalesce autosave across an intervening history action", async () => {
    const h = host(); const canvas = h.register();
    const beforeUndo = h.coordinator.capture("autosave");
    const undo = h.coordinator.transition("undo", () => {
      h.events.push("undo");
      h.replace({ token: { ...h.live.token, revision: 2 }, snapshot: { ...h.live.snapshot, name: "undone" }, lineage: ["undone"] });
    });
    const afterUndo = h.coordinator.capture("autosave");
    const first = await acquisition(canvas);
    h.acknowledge(canvas, first);
    expect((await beforeUndo).token.revision).toBe(1);
    const second = await acquisition(canvas, 1);
    expect(first.release).toHaveBeenCalledOnce();
    h.acknowledge(canvas, second, false);
    await undo;
    const third = await acquisition(canvas, 2);
    expect(second.release).toHaveBeenCalledOnce();
    h.acknowledge(canvas, third, false);
    expect(await afterUndo).toMatchObject({ token: { revision: 2 }, snapshot: { name: "undone" }, lineage: ["undone"] });
    expect(canvas.acquisitions.map(item => item.request.intent)).toEqual(["autosave", "undo", "autosave"]);
  });

  it("aborts one automatic waiter without cancelling the shared parent acknowledgement", async () => {
    const h = host(); const canvas = h.register(); const abort = new AbortController();
    const first = h.coordinator.capture("autosave", { signal: abort.signal });
    const cancelled = expect(first).rejects.toMatchObject({ name: "AbortError" });
    const second = h.coordinator.capture("autosave");
    const request = await acquisition(canvas);
    abort.abort(); await cancelled;
    expect(request.request).not.toHaveProperty("signal");
    expect(canvas.facts.value.pending).toBe(true);
    expect(request.release).not.toHaveBeenCalled();
    h.acknowledge(canvas, request);
    expect((await second).token.revision).toBe(1);
    expect(request.release).toHaveBeenCalledOnce();
    expect(canvas.participant.checkpoint).toHaveBeenCalledOnce();
  });

  it("keeps a started native admission after its transition waiter aborts, without running the transition", async () => {
    const h = host(); const canvas = h.register(); const abort = new AbortController(); const action = vi.fn();
    const result = h.coordinator.transition("switch-editor", action, { signal: abort.signal });
    const cancelled = expect(result).rejects.toMatchObject({ name: "AbortError" });
    const request = await acquisition(canvas);
    abort.abort(); await cancelled;
    expect(h.coordinator.probe()).toBe(true);
    h.acknowledge(canvas, request);
    await vi.waitFor(() => expect(request.release).toHaveBeenCalledOnce());
    expect(action).not.toHaveBeenCalled();
    expect(h.live.token.revision).toBe(1);
    expect(h.coordinator.probe()).toBe(false);
  });

  it("skips an aborted queued waiter and continues later actions", async () => {
    const h = host(); const canvas = h.register(); const abort = new AbortController();
    const first = h.coordinator.capture("save");
    const request = await acquisition(canvas);
    const skipped = h.coordinator.capture("publish", { signal: abort.signal });
    const cancelled = expect(skipped).rejects.toMatchObject({ name: "AbortError" });
    abort.abort(); await cancelled;
    h.acknowledge(canvas, request); await first;
    await vi.waitFor(() => expect(h.coordinator.probe()).toBe(false));
    expect(canvas.participant.checkpoint).toHaveBeenCalledOnce();
  });

  it("retains detached pending work across a failed view and refuses silent retirement", async () => {
    const h = host(); const canvas = h.register();
    canvas.registration.detach();
    const probe = h.coordinator.probe;
    expect(probe()).toBe(true);
    expect(h.coordinator.pendingParticipantIds()).toEqual(["canvas"]);
    expect(canvas.participant.checkpoint).not.toHaveBeenCalled();
    expect(() => canvas.registration.retire()).toThrow(expect.objectContaining({ code: "registration-in-use" }));
    expect(() => h.coordinator.register("canvas", canvas.participant)).toThrow(expect.objectContaining({ code: "registration-in-use" }));
    const capture = h.coordinator.capture("save");
    const request = await acquisition(canvas);
    h.acknowledge(canvas, request); await capture;
    expect(probe()).toBe(false);
    canvas.registration.reattach();
    canvas.registration.detach();
    canvas.registration.retire();
    expect(canvas.participant.dispose).not.toHaveBeenCalled();
  });

  it("keeps serialization rejection and its local work visible without capturing or disposing it", async () => {
    const h = host(); const canvas = h.register(); const failure = new Error("Serialization failed"); const action = vi.fn();
    const result = h.coordinator.transition("navigate", action);
    const rejected = expect(result).rejects.toBe(failure);
    const request = await acquisition(canvas);
    canvas.registration.detach();
    request.gate.reject(failure); await rejected;
    expect(action).not.toHaveBeenCalled();
    expect(h.live.token.revision).toBe(0);
    expect(h.coordinator.pendingParticipantIds()).toEqual(["canvas"]);
    expect(h.coordinator.probe()).toBe(true);
    expect(canvas.participant.dispose).not.toHaveBeenCalled();
  });

  it("requires a real parent receipt rather than a child's optimistic claim of acceptance", async () => {
    const h = host(); const canvas = h.register(); const action = vi.fn();
    const result = h.coordinator.transition("undo", action);
    const rejected = expect(result).rejects.toMatchObject({ code: "invalid-receipt" });
    const request = await acquisition(canvas);
    canvas.facts.value = { pending: false, sequence: 1, acceptedSequence: 1 };
    // Structurally matching data is not the receipt issued by parent admission.
    request.gate.resolve({ receipt: { token: h.live.token, acceptedSequence: 1 }, release: request.release });
    await rejected;
    expect(action).not.toHaveBeenCalled();
    expect(request.release).toHaveBeenCalledOnce();
    expect(h.live.token.revision).toBe(0);
    expect(h.coordinator.probe()).toBe(true);
    expect(() => canvas.registration.retire()).toThrow(expect.objectContaining({ code: "registration-in-use" }));
  });

  it("retains known outstanding work when a rebound view resets all of its command facts", async () => {
    const h = host(); const canvas = h.register();
    canvas.registration.detach();
    canvas.facts.value = { pending: false, sequence: 0, acceptedSequence: 0 };
    expect(h.coordinator.probe()).toBe(true);
    expect(h.coordinator.pendingParticipantIds()).toEqual(["canvas"]);
    expect(() => canvas.registration.retire()).toThrow(expect.objectContaining({ code: "invalid-work" }));
    await expect(h.coordinator.capture("save")).rejects.toMatchObject({ code: "invalid-work" });
    expect(canvas.participant.checkpoint).not.toHaveBeenCalled();
    expect(canvas.participant.dispose).not.toHaveBeenCalled();
  });

  it("rejects a parent acknowledgement for a different live revision", () => {
    const h = host(); const canvas = h.register();
    expect(() => canvas.registration.acknowledge(1, { ...h.live.token, revision: 10 }))
      .toThrow(expect.objectContaining({ code: "invalid-receipt" }));
    expect(h.coordinator.probe()).toBe(true);
  });

  it.each(["authorityId", "resourceId", "scopeId"] as const)("rejects a delayed checkpoint after %s changes", async field => {
    const h = host(); const canvas = h.register("canvas", false); const action = vi.fn();
    const result = h.coordinator.transition("navigate", action);
    const rejected = expect(result).rejects.toMatchObject({ code: "stale-token" });
    const request = await acquisition(canvas);
    h.replace({ token: { ...h.live.token, [field]: "different" } });
    request.gate.resolve({ receipt: canvas.registration.initialReceipt, release: request.release });
    await rejected;
    expect(action).not.toHaveBeenCalled();
    expect(request.release).toHaveBeenCalledOnce();
  });

  it("refuses capture if canonical revision moves after a valid receipt was issued", async () => {
    const h = host(); const canvas = h.register();
    const result = h.coordinator.capture("publish");
    const rejected = expect(result).rejects.toMatchObject({ code: "invalid-receipt" });
    const request = await acquisition(canvas);
    h.replace({ token: { ...h.live.token, revision: 1 } });
    const receipt = canvas.registration.acknowledge(1, h.live.token);
    canvas.facts.value = { pending: false, sequence: 1, acceptedSequence: 1 };
    h.replace({ token: { ...h.live.token, revision: 2 }, snapshot: { ...h.live.snapshot, name: "newer inspector state" } });
    request.gate.resolve({ receipt, release: request.release });
    await rejected;
    expect(h.live.snapshot.name).toBe("newer inspector state");
    expect(request.release).toHaveBeenCalledOnce();
  });

  it("does not capture when an allegedly fenced participant still has a newer command", async () => {
    const h = host(); const canvas = h.register();
    const result = h.coordinator.capture("save");
    const rejected = expect(result).rejects.toMatchObject({ code: "pending-work" });
    const request = await acquisition(canvas);
    h.acknowledge(canvas, request);
    canvas.facts.value = { pending: true, sequence: 2, acceptedSequence: 1 };
    await rejected;
    expect(h.coordinator.pendingParticipantIds()).toEqual(["canvas"]);
    expect(request.release).toHaveBeenCalledOnce();
  });

  it("holds multiple participant fences until the final live capture and releases them in reverse order", async () => {
    const h = host(); const first = h.register("first"); const second = h.register("second");
    const result = h.coordinator.capture("test-run");
    const a = await acquisition(first); h.acknowledge(first, a);
    const b = await acquisition(second);
    expect(b.request.token.revision).toBe(1);
    expect(first.fences).toBe(1);
    expect(second.fences).toBe(1);
    h.replace({ snapshot: { ...h.live.snapshot, name: "both acknowledged" }, lineage: ["first", "second"] });
    h.acknowledge(second, b);
    expect(await result).toMatchObject({ token: { revision: 2 }, snapshot: { name: "both acknowledged" }, lineage: ["first", "second"] });
    expect(h.events.slice(-2)).toEqual(["second:release", "first:release"]);
  });

  it("releases an earlier acknowledged fence when a later participant fails, retaining that later work", async () => {
    const h = host(); const first = h.register("first"); const second = h.register("second");
    const failure = new Error("Second serializer failed");
    const result = h.coordinator.capture("publish");
    const rejected = expect(result).rejects.toBe(failure);
    const a = await acquisition(first); h.acknowledge(first, a);
    const b = await acquisition(second); b.gate.reject(failure);
    await rejected;
    expect(a.release).toHaveBeenCalledOnce();
    expect(h.live.token.revision).toBe(1);
    expect(h.coordinator.pendingParticipantIds()).toEqual(["second"]);
    expect(second.participant.dispose).not.toHaveBeenCalled();
  });

  it("rejects membership changes during preparation and retains the accepted source", async () => {
    const h = host(); const first = h.register("first");
    const result = h.coordinator.capture("save");
    const rejected = expect(result).rejects.toMatchObject({ code: "participants-changed" });
    const request = await acquisition(first);
    h.register("new participant");
    h.acknowledge(first, request);
    await rejected;
    expect(request.release).toHaveBeenCalledOnce();
    expect(h.live.token.revision).toBe(1);
    expect(h.coordinator.pendingParticipantIds()).toEqual(["new participant"]);
  });

  it("refuses retirement while a clean checkpoint fence is still held", async () => {
    const h = host(); const canvas = h.register("canvas", false);
    const result = h.coordinator.capture("export");
    const request = await acquisition(canvas);
    expect(() => canvas.registration.retire()).toThrow(expect.objectContaining({ code: "registration-in-use" }));
    h.acknowledge(canvas, request, false); await result;
    canvas.registration.retire();
    expect(canvas.participant.dispose).not.toHaveBeenCalled();
  });

  it("reports an applied transition separately from failed cleanup and retries only the retained fence", async () => {
    const h = host(); const canvas = h.register(); const failure = new Error("Fence release failed");
    const action = vi.fn((): undefined => {
      h.replace({ token: { ...h.live.token, revision: 2 }, snapshot: { ...h.live.snapshot, name: "undone" } });
    });
    const result = h.coordinator.transition("undo", action);
    const request = await acquisition(canvas);
    const release = request.release.getMockImplementation()!;
    request.release.mockImplementationOnce(() => { throw failure; });
    h.acknowledge(canvas, request);
    const outcome = await result;
    expect(outcome.applied).toBe(true);
    expect(outcome.cleanupFailure?.errors).toEqual([failure]);
    expect(h.live.snapshot.name).toBe("undone");
    expect(action).toHaveBeenCalledOnce();
    expect(h.coordinator.probe()).toBe(true);
    expect(() => canvas.registration.retire()).toThrow(expect.objectContaining({ code: "registration-in-use" }));
    await expect(h.coordinator.capture("save")).rejects.toMatchObject({ code: "fence-release" });
    expect(canvas.participant.checkpoint).toHaveBeenCalledOnce();
    request.release.mockImplementation(() => { expect(h.coordinator.probe()).toBe(true); release(); });
    canvas.registration.retryRelease();
    expect(canvas.fences).toBe(0);
    expect(h.coordinator.probe()).toBe(false);
    expect(action).toHaveBeenCalledOnce();
    canvas.registration.retire();
  });

  it("refuses a capture after cleanup failure and retains the fence for an explicit retry", async () => {
    const h = host(); const canvas = h.register(); const failure = new Error("Fence release failed");
    const result = h.coordinator.capture("publish");
    const rejected = expect(result).rejects.toMatchObject({ errors: [failure] });
    const request = await acquisition(canvas);
    request.release.mockImplementationOnce(() => { throw failure; });
    h.acknowledge(canvas, request); await rejected;
    expect(h.live.token.revision).toBe(1);
    expect(h.coordinator.pendingParticipantIds()).toEqual(["canvas"]);
    canvas.registration.retryRelease();
    expect(canvas.fences).toBe(0);
    expect(h.coordinator.probe()).toBe(false);
    expect(canvas.participant.checkpoint).toHaveBeenCalledOnce();
  });

  it("does not serialize clean detached views and keeps synchronous probes side-effect free", async () => {
    const h = host(); const canvas = h.register("canvas", false);
    canvas.registration.detach();
    const probe = h.coordinator.probe;
    expect(probe()).toBe(false); expect(probe()).toBe(false);
    expect(await h.coordinator.capture("save")).toEqual(h.live);
    expect(canvas.participant.checkpoint).not.toHaveBeenCalled();
    expect(canvas.participant.dispose).not.toHaveBeenCalled();
    canvas.registration.retire();
  });

  it("fails a navigation probe closed when a retained controller cannot report its work", () => {
    const h = host(); const canvas = h.register();
    vi.mocked(canvas.participant.readWork).mockImplementation(() => { throw new Error("View failed"); });
    expect(h.coordinator.probe()).toBe(true);
    expect(canvas.participant.checkpoint).not.toHaveBeenCalled();
    expect(canvas.participant.dispose).not.toHaveBeenCalled();
  });

  it("rejects an asynchronous transition callback and releases its fence", async () => {
    const h = host(); const canvas = h.register("canvas", false);
    const invalid = (() => Promise.resolve()) as unknown as (live: AuthoringCheckpointCapture<Snapshot>) => undefined;
    const result = h.coordinator.transition("undo", invalid);
    const rejected = expect(result).rejects.toMatchObject({ code: "async-transition" });
    const request = await acquisition(canvas);
    h.acknowledge(canvas, request, false); await rejected;
    expect(request.release).toHaveBeenCalledOnce();
  });

  it("does not report an already-entered synchronous transition as cancelled by its own unmount signal", async () => {
    const h = host(); const canvas = h.register("canvas", false); const abort = new AbortController();
    const action = vi.fn((): undefined => { h.events.push("transition applied"); abort.abort(); });
    const result = h.coordinator.transition("switch-editor", action, { signal: abort.signal });
    const request = await acquisition(canvas);
    h.acknowledge(canvas, request, false);
    await expect(result).resolves.toEqual({ applied: true });
    expect(action).toHaveBeenCalledOnce();
    expect(request.release).toHaveBeenCalledOnce();
  });
});
