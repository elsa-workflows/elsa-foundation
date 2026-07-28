import { describe, expect, it } from "vitest";
import {
  createStudioCodeEditorSession,
  getStudioCodeEditorSessionEntry,
  setStudioCodeEditorSessionEntry
} from "./studioCodeEditorSessions";

describe("studio code editor sessions", () => {
  it("purges and permanently rejects source-bearing entries in an explicitly supplied session after authorization revocation", () => {
    const session = createStudioCodeEditorSession("standalone-revocation");
    setStudioCodeEditorSessionEntry(session, "elsa://expressions/secret", { source: "sensitive expression" });

    window.dispatchEvent(new Event("elsa:auth-session-ended"));

    expect(getStudioCodeEditorSessionEntry(session, "elsa://expressions/secret")).toBeUndefined();
    setStudioCodeEditorSessionEntry(session, "elsa://expressions/secret", { source: "must not be restored by unmount cleanup" });
    expect(getStudioCodeEditorSessionEntry(session, "elsa://expressions/secret")).toBeUndefined();
  });
});
