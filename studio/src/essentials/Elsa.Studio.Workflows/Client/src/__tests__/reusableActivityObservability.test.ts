import { describe, expect, it, vi } from "vitest";
import {
  observeReusableActivity,
  reusableActivityObservationEvent
} from "../reusableActivityObservability";

describe("reusable activity observability", () => {
  it("emits only allowlisted low-cardinality fields", () => {
    const listener = vi.fn();
    window.addEventListener(reusableActivityObservationEvent, listener);
    try {
      observeReusableActivity({
        event: "boundary-inspection",
        surface: "run-workbench",
        outcome: "ready",
        layoutMode: "pinned",
        hasUpgrade: true,
        definitionId: "definition-secret",
        version: "2.0.0",
        providerKey: "hidden.provider",
        diagnostic: "sensitive free text"
      } as never);

      const detail = (listener.mock.calls[0][0] as CustomEvent).detail;
      expect(detail).toEqual({
        event: "boundary-inspection",
        surface: "run-workbench",
        outcome: "ready",
        hasUpgrade: true,
        layoutMode: "pinned"
      });
      expect(JSON.stringify(detail)).not.toMatch(/definition-secret|2\.0\.0|hidden\.provider|sensitive/);
    } finally {
      window.removeEventListener(reusableActivityObservationEvent, listener);
    }
  });
});
