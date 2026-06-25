import React from "react";
import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { describe, expect, it, vi } from "vitest";
import { SecretPickerEditor, toReference } from "../SecretPickerEditor";

describe("secret picker", () => {
  it("serializes selected secret as a Secret expression reference", async () => {
    const onChange = vi.fn();
    const { container, unmount } = render(onChange);

    await waitFor(() => container.querySelectorAll("option").length > 1);
    expect(container.textContent).not.toContain("Revoked API");
    const select = container.querySelector("select")!;
    select.value = "payments.api";
    select.dispatchEvent(new Event("change", { bubbles: true }));

    expect(onChange).toHaveBeenCalledWith({ type: "Secret", value: { name: "payments.api", typeName: "text" } });
    expect(toReference({ type: "Secret", value: { name: "payments.api", typeName: "text" } })).toEqual({ name: "payments.api", typeName: "text", scope: null });
    unmount();
  });
});

function render(onChange: (value: unknown) => void) {
  const host = document.createElement("div");
  document.body.appendChild(host);
  const root = createRoot(host);
  flushSync(() => root.render(
    <SecretPickerEditor
      descriptor={{ name: "token", typeName: "String", uiHint: "secret-picker" }}
      value={null}
      context={{ activity: {}, expressionDescriptors: [] }}
      onChange={onChange}
      endpointContext={{
        baseUrl: "",
        http: {
          requestJson: vi.fn(),
          getJson: vi.fn(),
          postJson: vi.fn(async () => ({
            items: [
              { name: "payments.api", displayName: "Payments API", typeName: "text", status: "Active" },
              { name: "revoked.api", displayName: "Revoked API", typeName: "text", status: "Revoked" }
            ],
            canCreateInline: true
          })),
          putJson: vi.fn(),
          deleteJson: vi.fn(),
          postForm: vi.fn()
        }
      }}
    />
  ));

  return {
    container: host,
    unmount: () => {
      root.unmount();
      host.remove();
    }
  };
}

async function waitFor(predicate: () => boolean) {
  for (let i = 0; i < 20; i++) {
    if (predicate()) return;
    await new Promise(resolve => setTimeout(resolve, 0));
  }
  throw new Error("Timed out waiting for predicate.");
}
