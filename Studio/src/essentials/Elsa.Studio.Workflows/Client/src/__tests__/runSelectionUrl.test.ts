import { describe, expect, it, vi } from "vitest";
import { readRunSelection, withRunSelection, writeRunSelection } from "../workflow-editor/runSelectionUrl";

// Spec 260 T424: the selected occurrence lives in the URL, so a reload or a deep link reopens it.
describe("run selection in the URL", () => {
  it("reads ?selected= first and still reads the older ?activityExecutionId= deep link", () => {
    expect(readRunSelection("?selected=ae-11&tab=issues")).toBe("ae-11");
    expect(readRunSelection("?activityExecutionId=ae-7")).toBe("ae-7");
    expect(readRunSelection("?selected=ae-11&activityExecutionId=ae-7")).toBe("ae-11");
    expect(readRunSelection("?selected=%20")).toBeNull();
    expect(readRunSelection("")).toBeNull();
  });

  it("replaces the selection and keeps every other parameter", () => {
    expect(withRunSelection("https://studio.test/workflows/instances/wf-42?tab=issues&activityExecutionId=ae-7", "ae-11"))
      .toBe("https://studio.test/workflows/instances/wf-42?tab=issues&selected=ae-11");
    expect(withRunSelection("https://studio.test/workflows/instances/wf-42?selected=ae-11&tab=issues", null))
      .toBe("https://studio.test/workflows/instances/wf-42?tab=issues");
  });

  it("replaces the history entry only when the selection changes", () => {
    const history = { state: { key: "k" }, replaceState: vi.fn() };
    writeRunSelection("ae-11", { href: "https://studio.test/workflows/instances/wf-42" }, history);
    expect(history.replaceState).toHaveBeenCalledWith({ key: "k" }, "", "https://studio.test/workflows/instances/wf-42?selected=ae-11");

    history.replaceState.mockClear();
    writeRunSelection("ae-11", { href: "https://studio.test/workflows/instances/wf-42?selected=ae-11" }, history);
    expect(history.replaceState).not.toHaveBeenCalled();
  });
});
