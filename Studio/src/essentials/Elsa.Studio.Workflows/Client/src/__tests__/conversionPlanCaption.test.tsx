import React from "react";
import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { afterEach, describe, expect, it } from "vitest";
import { ConversionPlanCaption } from "../ConversionPlanCaption";

let cleanup: (() => void) | null = null;

afterEach(() => {
  cleanup?.();
  cleanup = null;
});

describe("ConversionPlanCaption", () => {
  it("renders the pinned mode, profile@version, representation flow, and shortened fingerprint", () => {
    const host = mount(
      <ConversionPlanCaption
        plan={{
          mode: "Json",
          operation: "Profile",
          profile: { id: "elsa.json", version: "1" },
          sourceRepresentation: "FormattedContent",
          sourceType: { alias: "String", collectionKind: "Single" },
          targetType: { alias: "Any", collectionKind: "Single" },
          fingerprint: "sha256:8a80851546a1f1875cf5c1d78dbbc51ac6e42d8f8f47b3f77b6e5db34f01c3cb"
        }}
      />
    );

    expect(host.querySelector(".wf-conversion-plan-mode")?.textContent).toBe("Json");
    expect(host.querySelector(".wf-conversion-plan-profile")?.textContent).toBe("elsa.json@1");
    expect(host.querySelector(".wf-conversion-plan-flow")?.textContent).toBe("Formatted content → Any");
    const fingerprint = host.querySelector<HTMLElement>(".wf-conversion-plan-fingerprint")!;
    expect(fingerprint.textContent).toBe("sha256:8a80851546a1…");
    expect(fingerprint.title).toBe("sha256:8a80851546a1f1875cf5c1d78dbbc51ac6e42d8f8f47b3f77b6e5db34f01c3cb");
  });

  it("omits the profile segment for built-in non-profile operations", () => {
    const host = mount(
      <ConversionPlanCaption
        plan={{
          mode: "Auto",
          operation: "Identity",
          profile: null,
          sourceRepresentation: "TextValue",
          sourceType: { alias: "String", collectionKind: "Single" },
          targetType: { alias: "String", collectionKind: "Single" },
          fingerprint: "sha256:00aa"
        }}
      />
    );

    expect(host.querySelector(".wf-conversion-plan-mode")?.textContent).toBe("Auto");
    expect(host.querySelector(".wf-conversion-plan-profile")).toBeNull();
    expect(host.querySelector(".wf-conversion-plan-flow")?.textContent).toBe("Text → String");
  });

  it("renders nothing when the binding carries no plan (sensitive or legacy executables)", () => {
    expect(mount(<ConversionPlanCaption plan={undefined} />).innerHTML).toBe("");
    expect(mount(<ConversionPlanCaption plan={null} />).innerHTML).toBe("");
    expect(mount(<ConversionPlanCaption plan={{}} />).innerHTML).toBe("");
  });
});

function mount(node: React.ReactElement): HTMLElement {
  const host = document.createElement("div");
  document.body.appendChild(host);
  const root = createRoot(host);
  flushSync(() => root.render(node));
  const previousCleanup = cleanup;
  cleanup = () => {
    previousCleanup?.();
    flushSync(() => root.unmount());
    host.remove();
  };
  return host;
}
