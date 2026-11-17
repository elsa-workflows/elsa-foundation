import { afterEach, expect, it, vi } from "vitest";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { clearApiCapabilityCache } from "../api/capabilities";
import { ActivityPlaybackIdentityError, playbackOrdinal, playbackWindow, readActivityPlayback, safePlaybackRange,
  selectedPlaybackOccurrence, type PlaybackOccurrence, type PlaybackPage } from "../api/activityPlayback";

const relation = { rel: "workflow-history-occurrences", href: "recorded/{workflowExecutionId}/activities" };
function entry(position: number, changes: Partial<PlaybackOccurrence> = {}): PlaybackOccurrence {
  return { position: String(position), workflowExecutionId: "run", activityExecutionId: `activity-${position}`, historyEventId: `event-${position}`,
    sequence: String(position + 1), observationKind: "ActivityCompleted", occurredAt: "2026-11-14T06:00:00Z",
    recordedAt: "2026-11-14T06:00:01Z", previousActivityExecutionId: position ? `activity-${position - 1}` : null,
    nextActivityExecutionId: position < 2 ? `activity-${position + 1}` : null, ...changes };
}
function page(changes: Partial<PlaybackPage> = {}): PlaybackPage {
  return { state: "available", reason: null, upperSequence: "3", retainedAfterSequence: "0", totalCount: "3", selectedPosition: "0",
    sequenceToken: "opaque+/=", hasNewerHistory: false, readAt: "2026-11-14T06:00:02Z", items: [entry(0), entry(1), entry(2)],
    coverage: { state: "captured", capturedFromStart: true, hasGaps: false, supportedKinds: ["ActivityCompleted"],
      firstSequence: "1", lastSequence: "3", settledSequence: "3", purgedThroughSequence: "0" }, ...changes };
}
function setup(value: unknown, links = [relation]) {
  const getJson = vi.fn().mockResolvedValueOnce({ capabilities: [{ id: "elsa.api.runtime", contractVersion: "1", links }] }).mockResolvedValue(value);
  return { getJson, context: { baseUrl: "https://host.example/shell", http: { getJson } } as unknown as StudioEndpointContext };
}
afterEach(clearApiCapabilityCache);

it("discovers the occurrence relation and binds a fixed-size exact scope without guessing paths", async () => {
  const value = page({ items: [entry(0, { activityExecutionId: "owner", nextActivityExecutionId: null })], totalCount: "1" });
  const { context, getJson } = setup(value); const signal = new AbortController().signal;
  await readActivityPlayback(context, "run", { activityExecutionId: "owner", anchorActivityExecutionId: "owner" }, signal);
  const [path, options] = getJson.mock.calls[1]; const url = new URL(path, "https://host.example");
  expect(url.pathname).toBe("/shell/recorded/run/activities");
  expect(Object.fromEntries(url.searchParams)).toEqual({ limit: "50", activityExecutionId: "owner", anchorActivityExecutionId: "owner" });
  expect(options).toEqual({ signal });
});
it("uses the same opaque frozen window for a bounded distant ordinal read", async () => {
  const admitted = page(); const frozen = playbackWindow(admitted)!;
  const { context, getJson } = setup(page({ selectedPosition: "2" }));
  const result = await readActivityPlayback(context, "run", { window: frozen, position: "2", activityExecutionId: "parent", includeDescendants: true });
  expect(selectedPlaybackOccurrence(result)?.activityExecutionId).toBe("activity-2");
  const query = new URL(getJson.mock.calls[1][0], "https://host.example").searchParams;
  expect(Object.fromEntries(query)).toEqual({ limit: "50", sequenceToken: "opaque+/=", position: "2", activityExecutionId: "parent", includeDescendants: "true" });
  expect(getJson).toHaveBeenCalledTimes(2);
});
it("never uses the legacy browser when the facet is absent", async () => {
  const { context, getJson } = setup(null, []);
  await expect(readActivityPlayback(context, "run")).rejects.toMatchObject({ name: "ApiCapabilityUnavailableError" });
  expect(getJson).toHaveBeenCalledTimes(1);
});
it.each([401, 403, 404, 409])("preserves an actual access/context refusal (%s) without exposing a fallback sequence", async status => {
  const { context, getJson } = setup(null); getJson.mockRejectedValueOnce({ status });
  await expect(readActivityPlayback(context, "run")).rejects.toMatchObject({ status });
  expect(getJson).toHaveBeenCalledTimes(2);
});
it("keeps unknown totals unknown while preserving selected ordinal and adjacent identities", async () => {
  const value = page({ totalCount: null }); const { context } = setup(value);
  const result = await readActivityPlayback(context, "run");
  expect(result.totalCount).toBeNull(); expect(safePlaybackRange(result)).toBeNull();
  expect(selectedPlaybackOccurrence(result)?.nextActivityExecutionId).toBe("activity-1");
});
it("accepts a real empty segment without turning unavailable admission into empty success", async () => {
  const empty = page({ totalCount: "0", selectedPosition: null, items: [] });
  const { context, getJson } = setup(empty);
  expect((await readActivityPlayback(context, "run")).totalCount).toBe("0");
  expect(safePlaybackRange(empty)).toBeNull();
  getJson.mockResolvedValue({ ...empty, state: "unavailable", reason: "occurrence-recording-unavailable", totalCount: null, sequenceToken: null });
  const unsupported = await readActivityPlayback(context, "run");
  expect(unsupported.state).toBe("unavailable"); expect(playbackWindow(unsupported)).toBeNull();
});
it.each(["range-invalidated", "target-unavailable"] as const)("preserves the owner's %s boundary without silently re-admitting", async state => {
  const value = page({ state, reason: "retained-range-changed", items: [], totalCount: null, selectedPosition: null, sequenceToken: null });
  const { context, getJson } = setup(value);
  expect((await readActivityPlayback(context, "run", { window: playbackWindow(page())! })).state).toBe(state);
  expect(getJson).toHaveBeenCalledTimes(2);
});
it("preserves values above JavaScript's safe range, and disables rounded native scrubbing", async () => {
  const position = "9007199254740993", sequence = "9007199254740994", count = "9007199254740995";
  const value = page({ totalCount: count, selectedPosition: position, upperSequence: sequence,
    coverage: { ...page().coverage, lastSequence: sequence, settledSequence: sequence },
    items: [entry(1, { position, sequence, nextActivityExecutionId: "next" })] });
  const { context, getJson } = setup(value);
  const result = await readActivityPlayback(context, "run", { position });
  expect(result.selectedPosition).toBe(position); expect(playbackOrdinal(result.selectedPosition)).toBe(9007199254740993n);
  expect(String(getJson.mock.calls[1][0])).toContain(`position=${position}`); expect(safePlaybackRange(result)).toBeNull();
  expect(safePlaybackRange(page())).toEqual({ min: 0, max: 2 });
});
it("preserves unknown occurrence time and first-completion semantics without fabricating a start", async () => {
  const value = page({ items: [entry(0, { occurredAt: null, occurredAtKnown: false, nextActivityExecutionId: null })], totalCount: "1" });
  const { context } = setup(value);
  expect(selectedPlaybackOccurrence(await readActivityPlayback(context, "run"))).toMatchObject({ observationKind: "ActivityCompleted", occurredAt: null, recordedAt: "2026-11-14T06:00:01Z" });
});
it("ignores later cancellation-resistant responses after its signal retires", async () => {
  let finish!: (value: unknown) => void;
  const { context, getJson } = setup(null); getJson.mockImplementationOnce(() => new Promise(resolve => { finish = resolve; }));
  const controller = new AbortController(); const pending = readActivityPlayback(context, "run", {}, controller.signal);
  await vi.waitFor(() => expect(finish).toBeTypeOf("function"));
  controller.abort(); finish(page());
  await expect(pending).rejects.toMatchObject({ name: "AbortError" });
});
it("does not issue a read when cancellation happens during discovery", async () => {
  let finish!: (value: unknown) => void;
  const getJson = vi.fn(() => new Promise(resolve => { finish = resolve; }));
  const context = { baseUrl: "https://host.example", http: { getJson } } as unknown as StudioEndpointContext;
  const controller = new AbortController(), pending = readActivityPlayback(context, "run", {}, controller.signal);
  controller.abort(); finish({ capabilities: [{ id: "elsa.api.runtime", contractVersion: "1", links: [relation] }] });
  await expect(pending).rejects.toMatchObject({ name: "AbortError" }); expect(getJson).toHaveBeenCalledTimes(1);
});
it.each([
  ["foreign run", page({ items: [entry(0, { workflowExecutionId: "other", nextActivityExecutionId: null })], totalCount: "1" })],
  ["duplicate execution", page({ items: [entry(0), entry(1, { activityExecutionId: "activity-0" })] })],
  ["duplicate observation", page({ items: [entry(0), entry(1, { historyEventId: "event-0" })] })],
  ["noncontiguous positions", page({ items: [entry(0), entry(2)] })],
  ["wrong neighbor", page({ items: [entry(0, { nextActivityExecutionId: "other" }), entry(1)] })],
  ["nonmonotonic order", page({ items: [entry(0), entry(1, { sequence: "1" })] })],
  ["outside frozen bound", page({ items: [entry(0, { sequence: "4", nextActivityExecutionId: null })], totalCount: "1" })],
  ["lost target", page({ selectedPosition: "10" })],
  ["rounded numeric ordinal", page({ selectedPosition: 9007199254740992 as unknown as string })],
  ["negative ordinal", page({ items: [entry(0, { position: "-1" })] })],
  ["out of signed64 range", page({ totalCount: "9223372036854775808" })],
  ["unknown observation", page({ items: [entry(0, { observationKind: "NewUnsupportedFact" })] })],
  ["unlocated gap", page({ coverage: { ...page().coverage, hasGaps: true } })],
  ["invalid coverage", page({ coverage: { ...page().coverage, state: "invalid" } })],
  ["fabricated new-history flag", page({ hasNewerHistory: true })],
  ["rebased retention", page({ retainedAfterSequence: "1" })],
  ["missing coverage", { ...page(), coverage: null }],
  ["missing neighbor knowledge", page({ items: [entry(0, { nextActivityExecutionId: undefined as unknown as null })] })],
  ["invalid lineage metadata", page({ items: [entry(0, { iterationId: {} as unknown as string })] })],
  ["disabled admission", page({ coverage: { ...page().coverage, state: "disabled" } })],
  ["invalid time knowledge", page({ items: [entry(0, { occurredAtKnown: "unknown" as unknown as boolean })] })],
  ["oversized page", page({ items: Array.from({ length: 51 }, (_, i) => entry(i)) })],
  ["unavailable disclosure", page({ state: "unavailable" })]
])("rejects %s rather than confirming an unrelated sequence", async (_name, value) => {
  const { context } = setup(value); await expect(readActivityPlayback(context, "run")).rejects.toBeInstanceOf(ActivityPlaybackIdentityError);
});
it.each(["sequenceToken", "upperSequence", "retainedAfterSequence", "totalCount"] as const)("refuses changed frozen %s", async key => {
  const value = page(); const frozen = { ...playbackWindow(value)!, [key]: key === "sequenceToken" ? "other" : "2" };
  const { context } = setup(value); await expect(readActivityPlayback(context, "run", { window: frozen })).rejects.toBeInstanceOf(ActivityPlaybackIdentityError);
});
it.each([{ position: "1" }, { anchorActivityExecutionId: "activity-2" }, { activityExecutionId: "other" }])("refuses a page that misses the requested exact target/scope", async request => {
  const { context } = setup(page()); await expect(readActivityPlayback(context, "run", request)).rejects.toBeInstanceOf(ActivityPlaybackIdentityError);
});
it.each([{ position: "1", anchorActivityExecutionId: "activity-1" }, { includeDescendants: true }, { position: "01" }])("rejects malformed intent before disclosing a read", async request => {
  const { context, getJson } = setup(page());
  await expect(readActivityPlayback(context, "run", request)).rejects.toBeInstanceOf(ActivityPlaybackIdentityError);
  expect(getJson).not.toHaveBeenCalled();
});
