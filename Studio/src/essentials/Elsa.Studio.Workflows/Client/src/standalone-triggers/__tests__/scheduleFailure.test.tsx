import React from "react";
import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { expect, it } from "vitest";
import { ScheduleFailure } from "../shared";

it("distinguishes a recorded empty cohort from unavailable admission and clears old or unknown reasons", () => {
  const container = document.createElement("div");
  document.body.appendChild(container);
  const root = createRoot(container);
  try {
    flushSync(() => root.render(<ScheduleFailure reason="NoEligibleSubscribers" />));
    expect(container.textContent).toBe("Waiting for eligible subscribers. The due occurrence is retained and will be retried.");
    expect(container.textContent).not.toContain("admission is unavailable");
    flushSync(() => root.render(<ScheduleFailure reason="AdmissionUnavailable" />));
    expect(container.textContent).toBe("Schedule admission is unavailable. The due occurrence is retained for retry.");
    expect(container.textContent).not.toContain("Waiting for eligible subscribers");
    for (const reason of [undefined, null, "FutureUnrecognizedReason"]) {
      flushSync(() => root.render(<ScheduleFailure reason={reason} />));
      expect(container.textContent).toBe("");
    }
  } finally {
    flushSync(() => root.unmount());
    container.remove();
  }
});
