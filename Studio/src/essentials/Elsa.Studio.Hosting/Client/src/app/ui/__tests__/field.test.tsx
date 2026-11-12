import React, { createRef, useState } from "react";
import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { afterEach, expect, it, vi } from "vitest";
import * as fields from "../forms/Field";

let cleanup = () => {};
afterEach(() => cleanup());
function mount(node: React.ReactNode) {
  const container = document.createElement("div"); document.body.appendChild(container);
  const root = createRoot(container);
  const render = (next: React.ReactNode) => flushSync(() => root.render(next));
  render(node); cleanup = () => { flushSync(() => root.unmount()); container.remove(); };
  return { container, render };
}

it("associates native children with only their label and merges existing help", () => {
  const ref = createRef<HTMLInputElement>();
  const { container } = mount(<><p id="existing-help">Existing help</p><fields.StudioField label="Endpoint" description="Service address">
    <input id="caller-id" ref={ref} aria-describedby="existing-help" defaultValue="https://example.test" />
  </fields.StudioField></>);
  const input = container.querySelector("input")!;
  expect(input.id).toBe("caller-id"); expect(ref.current).toBe(input);
  expect([...input.labels!].map(label => label.textContent)).toEqual(["Endpoint"]);
  const descriptions = input.getAttribute("aria-describedby")!.split(" ");
  expect(descriptions).toEqual(["existing-help", "caller-id-help"]);
  expect(descriptions.map(id => document.getElementById(id)?.textContent)).toEqual(["Existing help", "Service address"]);
});

it("keeps a native child, its ref, focus and event handler through help and state changes", () => {
  const ref = createRef<HTMLInputElement>(); const onInput = vi.fn();
  const view = (hint: string) => <fields.StudioField label="Name" description={hint}><input ref={ref} defaultValue="draft" onInput={onInput} /></fields.StudioField>;
  const { container, render } = mount(view("First hint")); const input = container.querySelector("input")!;
  input.focus(); input.value = "retained draft"; input.dispatchEvent(new Event("input", { bubbles: true }));
  const id = input.id; render(view("New hint"));
  expect(ref.current).toBe(input); expect(document.activeElement).toBe(input); expect(input.value).toBe("retained draft");
  expect(input.id).toBe(id); expect(id).not.toBe(""); expect(onInput).toHaveBeenCalledOnce();
});

it("creates distinct associations for repeated fields and preserves output semantics", () => {
  const { container } = mount(<><fields.StudioField label="Name"><input /></fields.StudioField><fields.StudioField label="Name"><input /></fields.StudioField><fields.StudioField label="Version"><output>7</output></fields.StudioField></>);
  const controls = [...container.querySelectorAll<HTMLInputElement>("input,output")];
  expect(new Set(controls.map(control => control.id)).size).toBe(3);
  expect(controls.every(control => control.id && control.labels?.length === 1)).toBe(true);
  expect(container.querySelector("output")?.textContent).toBe("7");
});

it("retains nested native wrappers and sibling validation messages", () => {
  const { container } = mount(<fields.StudioField label="Timeout" description="Seconds"><div><input aria-invalid="true" aria-describedby="timeout-error" /></div><small id="timeout-error">Enter a number</small></fields.StudioField>);
  const input = container.querySelector("input")!;
  expect(input.labels?.[0].textContent).toBe("Timeout");
  expect(input.getAttribute("aria-invalid")).toBe("true");
  expect(input.getAttribute("aria-describedby")?.split(" ")).toEqual(["timeout-error", `${input.id}-help`]);
  expect(container.querySelector("#timeout-error")?.closest("fieldset")).toBeNull();
});

it("preserves individual checkbox labels in a real group", () => {
  const { container } = mount(<fields.StudioField label="Options" description="Choose options"><label><input type="checkbox" />One</label><label><input type="checkbox" />Two</label></fields.StudioField>);
  expect(container.querySelector("legend")?.textContent).toBe("Options");
  expect([...container.querySelectorAll("input")].map(input => input.labels?.[0].textContent)).toEqual(["One", "Two"]);
  expect(container.querySelectorAll("label label")).toHaveLength(0);
});

it("keeps the render control mounted through invalid recovery and leaves auxiliary actions usable", () => {
  function Example() {
    const [invalid, setInvalid] = useState(true);
    return <fields.StudioFormField label="Credential" error={invalid ? "Unavailable" : null} hint="Choose a credential" disabled trailing={<button onClick={() => setInvalid(false)}>Retry</button>}>
      {attributes => <input {...attributes} defaultValue="retained" />}
    </fields.StudioFormField>;
  }
  const { container } = mount(<Example />); const input = container.querySelector("input")!; const retry = container.querySelector("button")!;
  expect(input.disabled).toBe(true); expect(retry.disabled).toBe(false); expect(container.querySelector("fieldset")?.disabled).toBe(false);
  expect(input.getAttribute("aria-invalid")).toBe("true");
  flushSync(() => retry.click());
  expect(container.querySelector("input")).toBe(input); expect(input.value).toBe("retained");
  expect(input.hasAttribute("aria-invalid")).toBe(false);
  expect(document.getElementById(input.getAttribute("aria-describedby")!)?.textContent).toBe("Choose a credential");
});


it("names an opaque contributed control through its explicit contract without cloning it", () => {
  function ContributedEditor() { return <input id="contributed" data-studio-field-control="" defaultValue="draft" />; }
  const { container } = mount(<fields.StudioField controlId="contributed" label="Contributed value"><ContributedEditor /></fields.StudioField>);
  const input = container.querySelector("input")!;
  expect(input.labels?.[0].textContent).toBe("Contributed value");
  expect(input.closest(".studio-form-control")).not.toBeNull();
  expect(input.value).toBe("draft");
});

it("associates legacy sibling help without including it in the accessible name", () => {
  const { container } = mount(<fields.StudioField label="Address"><input /><small>Use an absolute URL</small></fields.StudioField>);
  const input = container.querySelector("input")!;
  expect(input.labels?.[0].textContent).toBe("Address");
  expect(document.getElementById(input.getAttribute("aria-describedby")!)?.textContent).toBe("Use an absolute URL");
});


it("keeps a standalone choice individually named and activates it from its caption", () => {
  const { container } = mount(<fields.StudioField label="Protect decision source and values"><input type="checkbox" defaultChecked={false} /></fields.StudioField>);
  const input = container.querySelector("input")!;
  expect(input.labels?.length).toBe(1);
  expect(input.labels?.[0].textContent).toBe("Protect decision source and values");
  flushSync(() => input.labels![0].click());
  expect(input.checked).toBe(true);
  expect(container.querySelector("fieldset")).toBeNull();
});
