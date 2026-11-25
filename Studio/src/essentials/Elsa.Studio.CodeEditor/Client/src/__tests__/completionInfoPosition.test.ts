import { describe, expect, it } from "vitest";
import { positionCompletionInfo } from "../engines/codeMirrorCodeIntelligence";

const rect = (left: number, top: number, right: number, bottom: number) => ({ left, top, right, bottom });
const tooltip = (width: number, height: number) => ({ offsetWidth: width, offsetHeight: height }) as HTMLElement;

describe("completion documentation placement", () => {
  it("places a wide note beside the list when there is room", () => {
    const list = rect(1188, 380, 1438, 420);
    const placed = positionCompletionInfo(false, list, rect(1188, 380, 1438, 400), rect(0, 0, 300, 60), rect(0, 0, 1440, 1000), tooltip(250, 40));
    expect(placed.class).toBe("cm-completionInfo-left");
    expect(placed.style).toContain("top: 0px");
  });

  it("places a narrow note below the whole list, never over the following options", () => {
    const list = rect(130, 690, 300, 730);
    const placed = positionCompletionInfo(false, list, rect(130, 690, 300, 710), rect(0, 0, 220, 90), rect(0, 0, 390, 900), tooltip(170, 40));
    expect(placed.class).toBe("cm-completionInfo-right-narrow");
    expect(placed.style).toContain("top: 40px");
  });

  it("places a narrow note above the whole list when it does not fit below", () => {
    const list = rect(130, 800, 300, 860);
    const placed = positionCompletionInfo(false, list, rect(130, 820, 300, 840), rect(0, 0, 220, 90), rect(0, 0, 390, 900), tooltip(170, 60));
    expect(placed.class).toBe("cm-completionInfo-right-narrow");
    expect(placed.style).toContain("bottom: 60px");
  });
});
