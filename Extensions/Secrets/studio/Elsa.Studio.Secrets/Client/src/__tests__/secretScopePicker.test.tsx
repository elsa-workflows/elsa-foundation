import React, { useState } from "react";
import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { afterEach, describe, expect, it, vi } from "vitest";
import { SecretScopePicker } from "../SecretScopePicker";
import type { SecretScopeDescriptor } from "../secretTypes";

const cleanup = new Set<() => void>();
afterEach(() => {
  for (const dispose of cleanup) dispose();
  cleanup.clear();
});

describe("secret scope picker", () => {
  it("defaults to no scope and offers custom entry when no suggestions exist", () => {
    const { container } = renderPicker({ options: undefined });
    const select = container.querySelector<HTMLSelectElement>("select")!;

    expect(select.value).toBe("");
    expect(select.options[0].textContent).toBe("No scope");
    expect(container.textContent).toContain("No suggestions are available.");
    expect(container.textContent).toContain("A reference that specifies a scope must match it (ignoring case).");
    expect(container.textContent).toContain("Scope does not grant access.");

    selectOption(select, "Custom…");
    expect(customScopeInput(container)).not.toBeNull();
  });

  it("keeps the authored value exact when suggestions are removed or match only by case", () => {
    const { container, render } = renderPicker({
      value: "Payments:Production",
      options: [{ name: "Payments:Production", displayName: "Production" }]
    });
    expect(container.querySelector<HTMLSelectElement>("select")?.value).toBe("Payments:Production");

    render({ value: "Payments:Production", options: [] });
    expect(customScopeInput(container)?.value).toBe("Payments:Production");

    render({ value: "Payments:Production", options: [{ name: "payments:production", displayName: "Production" }] });
    const select = container.querySelector<HTMLSelectElement>("select")!;
    expect(select.value).toBe("Payments:Production");
    expect(select.selectedOptions[0].textContent).toBe("Current value: Payments:Production");
    expect([...select.options].some(option => option.value === "payments:production" && option.textContent === "Production")).toBe(true);
    expect(customScopeInput(container)).toBeNull();
    expect(container.querySelector("output")?.textContent).toBe("Payments:Production");
  });

  it("adds search for larger catalogs and displays selected module details", () => {
    const options = Array.from({ length: 9 }, (_, index) => ({
      name: `module.scope.${index}`,
      displayName: `Module Scope ${index}`,
      description: index === 8 ? "Credentials for a module" : undefined,
      source: index === 8 ? "Payments module" : undefined
    }));
    const { container } = renderPicker({ options });
    const search = container.querySelector<HTMLInputElement>("input[type='search']")!;
    change(search, "module.scope.8");
    const select = container.querySelector<HTMLSelectElement>("select")!;
    expect([...select.options].map(option => option.value)).toContain("module.scope.8");

    selectOption(select, "Module Scope 8");
    expect(container.textContent).toContain("Credentials for a module");
    expect(container.textContent).toContain("Source: Payments module");
  });

  it("keeps custom entry usable during loading and provides a retry action on failure", () => {
    const onRetry = vi.fn();
    const { container, render } = renderPicker({ loading: true, error: true, onRetry });
    expect(container.querySelector("[role='status']")?.textContent).toContain("Loading scope suggestions");
    expect([...container.querySelectorAll<HTMLButtonElement>("button")].every(button => button.disabled)).toBe(true);

    render({ loading: false, error: true });
    expect(container.querySelector("[role='alert']")?.textContent).toContain("could not be refreshed");
    selectOption(container.querySelector<HTMLSelectElement>("select")!, "Custom…");
    expect(customScopeInput(container)).not.toBeNull();
    const retry = container.querySelector<HTMLButtonElement>("button")!;
    expect(retry.textContent).toBe("Refresh scope suggestions");
    retry.click();
    expect(onRetry).toHaveBeenCalledOnce();
  });
});

function renderPicker({
  value = "",
  options,
  loading,
  error,
  onRetry = vi.fn()
}: {
  value?: string;
  options?: SecretScopeDescriptor[];
  loading?: boolean;
  error?: boolean;
  onRetry?: () => void;
}) {
  const container = document.createElement("div");
  document.body.appendChild(container);
  const root = createRoot(container);
  const render = (props: { value?: string; options?: SecretScopeDescriptor[]; loading?: boolean; error?: boolean; onRetry?: () => void }) => {
    flushSync(() => root.render(<ScopeHarness {...props} onRetry={props.onRetry ?? onRetry} />));
  };
  const unmount = () => {
    flushSync(() => root.unmount());
    container.remove();
    cleanup.delete(unmount);
  };
  cleanup.add(unmount);
  render({ value, options, loading, error, onRetry });
  return { container, render, unmount };
}

function ScopeHarness({
  value: initialValue = "",
  ...props
}: {
  value?: string;
  options?: SecretScopeDescriptor[];
  loading?: boolean;
  error?: boolean;
  onRetry?(): void;
}) {
  const [value, setValue] = useState(initialValue);
  return (
    <>
      <SecretScopePicker {...props} value={value} onChange={setValue} />
      <output>{value}</output>
    </>
  );
}

function selectOption(select: HTMLSelectElement, label: string) {
  const option = [...select.options].find(item => item.textContent === label);
  if (!option) throw new Error(`Option not found: ${label}`);
  flushSync(() => {
    Object.getOwnPropertyDescriptor(HTMLSelectElement.prototype, "value")?.set?.call(select, option.value);
    select.dispatchEvent(new Event("change", { bubbles: true }));
  });
}

function change(input: HTMLInputElement, value: string) {
  flushSync(() => {
    Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, "value")?.set?.call(input, value);
    input.dispatchEvent(new Event("input", { bubbles: true }));
  });
}

function customScopeInput(container: HTMLElement) {
  const label = [...container.querySelectorAll<HTMLLabelElement>("label")]
    .find(item => item.textContent?.trim() === "Custom scope value");
  return label ? document.getElementById(label.htmlFor) as HTMLInputElement | null : null;
}
