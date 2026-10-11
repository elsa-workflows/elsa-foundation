import React from "react";
import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { afterEach, describe, expect, it, vi } from "vitest";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { SecretPickerEditor, toReference } from "../SecretPickerEditor";

const rawSecret = "raw-secret-value-that-must-never-render";
const cleanup: Array<() => void> = [];

afterEach(() => {
  cleanup.splice(0).forEach(dispose => dispose());
});

describe("secret picker", () => {
  it("renders allowlisted metadata and serializes only an opaque Secret Reference", async () => {
    const onChange = vi.fn();
    const { container } = render({ onChange, initialFocus: true });

    await waitFor(() => document.activeElement === container.querySelector("select"));
    expect(container.textContent).toContain("Payments API");
    expect(container.textContent).toContain("tenant-a");
    expect(container.textContent).toContain("encrypted");
    expect(container.textContent).toContain("text");
    expect(container.textContent).toContain("Active");
    expect(container.textContent).not.toContain(rawSecret);
    const select = container.querySelector("select")!;
    select.value = "secret-1";
    select.dispatchEvent(new Event("change", { bubbles: true }));

    expect(onChange).toHaveBeenCalledWith({ name: "payments.api", typeName: "text", scope: "tenant-a" });
    expect(JSON.stringify(onChange.mock.calls)).not.toContain(rawSecret);
    expect(toReference({ name: "payments.api", typeName: "text", scope: "tenant-a", rawValue: rawSecret })).toEqual({
      name: "payments.api",
      typeName: "text",
      scope: "tenant-a"
    });
  });

  it("latches a focus request until asynchronous metadata makes the picker usable", async () => {
    const request = deferred<unknown>();
    const { container, rerender } = render({
      initialFocus: true,
      postJson: vi.fn(() => request.promise)
    });

    rerender({ initialFocus: false });
    expect(document.activeElement).not.toBe(container.querySelector("select"));
    request.resolve(pickerResponse());

    await waitFor(() => document.activeElement === container.querySelector("select"));
  });

  it("preserves an unavailable current reference while metadata loads and fails", async () => {
    const request = deferred<unknown>();
    const onChange = vi.fn();
    const { container } = render({
      value: { name: "retired.api", typeName: "text", scope: "tenant-a" },
      onChange,
      postJson: vi.fn(() => request.promise)
    });

    expect(container.textContent).toContain("retired.api");
    expect(container.textContent).toContain("Loading Secret metadata");
    expect(container.querySelector("select")?.disabled).toBe(true);
    request.reject(new Error(`provider failed: ${rawSecret}`));
    await waitFor(() => container.textContent?.includes("Secret metadata is unavailable") ?? false);

    expect(container.textContent).toContain("retired.api");
    expect(container.textContent).not.toContain(rawSecret);
    expect(onChange).not.toHaveBeenCalled();
  });

  it("retries after failure without mutating the current reference", async () => {
    const postJson = vi.fn()
      .mockRejectedValueOnce(new Error("provider unavailable"))
      .mockResolvedValueOnce(pickerResponse());
    const onChange = vi.fn();
    const { container } = render({
      value: { name: "retired.api", typeName: "text", scope: "tenant-a" },
      onChange,
      postJson
    });

    await waitFor(() => container.textContent?.includes("Secret metadata is unavailable") ?? false);
    click(container, "Retry");
    await waitFor(() => container.textContent?.includes("Payments API") ?? false);

    expect(postJson).toHaveBeenCalledTimes(2);
    expect(container.textContent).toContain("retired.api");
    expect(container.textContent).toContain("Unavailable");
    expect(onChange).not.toHaveBeenCalled();
  });

  it("clears prior-context metadata without claiming the reference is unavailable", async () => {
    const firstRequest = deferred<unknown>();
    const secondRequest = deferred<unknown>();
    const firstContext = endpointContext(vi.fn(() => firstRequest.promise));
    const secondContext = endpointContext(vi.fn(() => secondRequest.promise));
    const { container, rerender } = render({
      value: { name: "payments.api", typeName: "text", scope: "tenant-a" },
      endpointContext: firstContext
    });

    firstRequest.resolve(pickerResponse());
    await waitFor(() => container.textContent?.includes("Payments API") ?? false);
    expect(container.textContent).toContain("encrypted");

    rerender({ endpointContext: secondContext });
    expect(container.textContent).toContain("Loading Secret metadata");
    expect(container.textContent).toContain("payments.api");
    expect(container.textContent).not.toContain("Payments API");
    expect(container.textContent).not.toContain("encrypted");
    expect(container.textContent).not.toContain("Unavailable");

    secondRequest.reject(new Error("provider unavailable"));
    await waitFor(() => container.textContent?.includes("Secret metadata is unavailable") ?? false);
    expect(container.textContent).toContain("payments.api");
    expect(container.textContent).not.toContain("Payments API");
    expect(container.textContent).not.toContain("encrypted");
    expect(container.textContent).not.toContain("Unavailable");
  });
});

interface RenderOptions {
  value?: unknown;
  onChange?: (value: unknown) => void;
  postJson?: ReturnType<typeof vi.fn>;
  initialFocus?: boolean;
  endpointContext?: StudioEndpointContext;
}

function render(options: RenderOptions = {}) {
  const host = document.createElement("div");
  document.body.appendChild(host);
  const root = createRoot(host);
  const initialContext = options.endpointContext ?? endpointContext(options.postJson ?? vi.fn(async () => pickerResponse()));
  const onChange = options.onChange ?? vi.fn();
  const renderPicker = (overrides: Pick<RenderOptions, "initialFocus" | "endpointContext"> = {}) => {
    flushSync(() => root.render(
      <SecretPickerEditor
        descriptor={{ name: "token", typeName: "String", uiHint: "secret-picker" }}
        syntax="Secret"
        value={options.value ?? null}
        initialFocus={overrides.initialFocus ?? options.initialFocus}
        context={{ activity: {}, descriptor: { name: "token", typeName: "String" }, syntax: "Secret", surface: "inline", expressionDescriptors: [] }}
        onChange={onChange}
        endpointContext={overrides.endpointContext ?? initialContext}
      />
    ));
  };
  renderPicker();

  const dispose = () => {
    root.unmount();
    host.remove();
  };
  cleanup.push(dispose);
  return {
    container: host,
    unmount: dispose,
    rerender: renderPicker
  };
}

function endpointContext(postJson: ReturnType<typeof vi.fn>): StudioEndpointContext {
  return {
    baseUrl: "",
    http: {
      requestJson: vi.fn(),
      getJson: vi.fn(),
      postJson,
      putJson: vi.fn(),
      deleteJson: vi.fn(),
      postForm: vi.fn()
    }
  };
}

function pickerResponse() {
  return {
    items: [
      {
        id: "secret-1",
        name: "payments.api",
        displayName: "Payments API",
        typeName: "text",
        storeName: "encrypted",
        scope: "tenant-a",
        status: "Active",
        rawValue: rawSecret,
        value: rawSecret
      }
    ],
    canCreateInline: true,
    rawValue: rawSecret
  };
}

function click(container: HTMLElement, label: string) {
  const button = Array.from(container.querySelectorAll("button")).find(item => item.textContent?.trim() === label);
  if (!button) throw new Error(`Button not found: ${label}`);
  button.dispatchEvent(new MouseEvent("click", { bubbles: true }));
}

function deferred<T>() {
  let resolve!: (value: T) => void;
  let reject!: (reason?: unknown) => void;
  const promise = new Promise<T>((resolvePromise, rejectPromise) => {
    resolve = resolvePromise;
    reject = rejectPromise;
  });
  return { promise, resolve, reject };
}

async function waitFor(predicate: () => boolean) {
  for (let i = 0; i < 20; i++) {
    if (predicate()) return;
    await new Promise(resolve => setTimeout(resolve, 0));
  }
  throw new Error("Timed out waiting for predicate.");
}
