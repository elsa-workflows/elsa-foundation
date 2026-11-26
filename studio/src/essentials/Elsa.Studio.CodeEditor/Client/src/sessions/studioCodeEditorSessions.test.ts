import { beforeEach, describe, expect, it } from "vitest";
import {
  createStudioCodeEditorSession,
  getStudioCodeEditorSession,
  getStudioCodeEditorSessionEntry,
  isStudioCodeEditorSessionRevoked,
  setStudioCodeEditorSessionEntry
} from "./studioCodeEditorSessions";

beforeEach(() => {
  window.dispatchEvent(new Event("elsa:auth-session-started"));
});

describe("studio code editor sessions", () => {
  it("purges and permanently rejects source-bearing entries in an explicitly supplied session after authorization revocation", () => {
    const session = createStudioCodeEditorSession("standalone-revocation");
    setStudioCodeEditorSessionEntry(session, "elsa://expressions/secret", { source: "sensitive expression" });

    window.dispatchEvent(new Event("elsa:auth-session-ended"));

    expect(getStudioCodeEditorSessionEntry(session, "elsa://expressions/secret")).toBeUndefined();
    setStudioCodeEditorSessionEntry(session, "elsa://expressions/secret", { source: "must not be restored by unmount cleanup" });
    expect(getStudioCodeEditorSessionEntry(session, "elsa://expressions/secret")).toBeUndefined();
  });

  it("disposes retained source and history when its workflow editor session ends", () => {
    const scope = "workflow-editor-1";
    const key = `${scope}\u001felsa://expressions/text`;
    const session = getStudioCodeEditorSession(key);
    setStudioCodeEditorSessionEntry(session, "elsa://expressions/text", { source: "workflow expression" });

    window.dispatchEvent(new CustomEvent("elsa:expression-editor-session-ended", {
      detail: { scope }
    }));

    expect(getStudioCodeEditorSessionEntry(session, "elsa://expressions/text")).toBeUndefined();
    expect(getStudioCodeEditorSession(key)).not.toBe(session);
  });

  it("retains scoped revocation for editors that mount after tooling authorization is denied", () => {
    const scope = "workflow-editor-revoked";
    const key = `${scope}\u001felsa://expressions/text`;

    window.dispatchEvent(new CustomEvent("elsa:expression-tooling-authorization-revoked", {
      detail: { scope }
    }));

    expect(isStudioCodeEditorSessionRevoked(key)).toBe(true);
    expect(isStudioCodeEditorSessionRevoked("another-workflow\u001felsa://expressions/text")).toBe(false);

    window.dispatchEvent(new Event("elsa:auth-session-started"));
    expect(isStudioCodeEditorSessionRevoked(key)).toBe(true);

    window.dispatchEvent(new CustomEvent("elsa:expression-tooling-authorization-restored", {
      detail: { scope }
    }));
    expect(isStudioCodeEditorSessionRevoked(key)).toBe(false);
  });
});
