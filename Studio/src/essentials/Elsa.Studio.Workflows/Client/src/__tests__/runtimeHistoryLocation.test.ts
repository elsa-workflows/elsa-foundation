import { describe, expect, it, vi } from "vitest";
import { readRunHistoryLocation, withRunHistoryLocation, writeRunHistoryLocation } from "../workflow-editor/run-history/historyLocation";

describe("history investigation URLs", () => {
  it("defaults to the timeline while keeping target selection separate from explicit execution scope", () => {
    const state = readRunHistoryLocation("?selected=activity-1&historyEvent=event-2");
    expect(state.view).toBe("timeline"); expect(state.selection).toEqual({ kind: "event", id: "event-2" });
    expect(state.filters.activityExecutionId).toBeNull();
  });
  it("round-trips exact IDs and filter/view context without losing legacy selection, incident tabs or return destinations", () => {
    const input = "https://studio.example/workflows/instances/run?selected=activity-1&tab=issues&returnTo=%2Fworkflows%2Finstances%3Fstatus%3DFaulted";
    const state = readRunHistoryLocation("?view=diagram&bookmark=handle%2Fone&historyActivity=earlier-execution&historyDescendants=1&historyCategory=BookmarkLifecycle&historySearch=order+42&historyDetail=all");
    const output = new URL(withRunHistoryLocation(input, state));
    expect(output.searchParams.get("selected")).toBe("activity-1"); expect(output.searchParams.get("tab")).toBe("issues");
    expect(output.searchParams.get("returnTo")).toBe("/workflows/instances?status=Faulted");
    expect(readRunHistoryLocation(output.search)).toEqual(state);
  });
  it("replaces one exact target with another and removes obsolete filter parameters", () => {
    const state = readRunHistoryLocation("?historyEvent=event-9");
    const url = new URL(withRunHistoryLocation("https://studio.example/run?bookmark=old&historyActivity=old&historyDescendants=1&view=diagram", state));
    expect(url.searchParams.get("historyEvent")).toBe("event-9"); expect(url.searchParams.has("bookmark")).toBe(false);
    expect(url.searchParams.has("historyActivity")).toBe(false); expect(url.searchParams.has("historyDescendants")).toBe(false);
  });
  it("writes only changed state and preserves the workspace history object", () => {
    const state = readRunHistoryLocation("?bookmark=one"); const history = { state: { workspace: "one" }, replaceState: vi.fn() };
    writeRunHistoryLocation(state, { href: "https://studio.example/run?bookmark=one" }, history); expect(history.replaceState).not.toHaveBeenCalled();
    writeRunHistoryLocation({ ...state, view: "diagram" }, { href: "https://studio.example/run?bookmark=one" }, history);
    expect(history.replaceState).toHaveBeenCalledWith(history.state, "", "https://studio.example/run?bookmark=one&view=diagram");
  });
});
