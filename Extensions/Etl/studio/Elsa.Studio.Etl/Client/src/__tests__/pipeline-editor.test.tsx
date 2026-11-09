import React from "react";
import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, describe, expect, it, vi } from "vitest";
import { EtlPipelineEditor } from "../components/EtlPipelineEditor";
import { formatEtlJsonNumber, parseEtlJsonNumber, type EtlBindingSummary, type EtlPipeline } from "../etlTypes";

const mounted: Array<() => void> = [];

afterEach(() => mounted.splice(0).forEach(cleanup => cleanup()));

describe("ETL pipeline editor", () => {
  it("keeps the fixed stage order and offers only bindings for the matching role", () => {
    const editor = renderEditor();
    const order = editor.host.querySelector<HTMLOListElement>('[aria-label="ETL operation order"]')!;
    expect(Array.from(order.querySelectorAll("strong")).map(item => item.textContent)).toEqual([
      "Read and validate CSV", "Match lookup rows", "Map output fields", "Apply output filters", "Route JSONL records"
    ]);

    const source = control<HTMLSelectElement>(editor.host, "Source file");
    expect(Array.from(source.options).map(option => option.value)).toEqual(["", "source-v1", "source-v2"]);
    setSelect(source, "source-v1");
    editor.render();
    setSelect(control<HTMLSelectElement>(editor.host, "Source file"), "source-v2");
    editor.render();
    expect(editor.pipeline.bindings.source).toBe("source-v2");
    expect(editor.pipeline.bindings.lookup).toBe("lookup-v1");
    expect(editor.pipeline.bindings.output).toBe("main-v1");
    expect(editor.pipeline.bindings.errors).toBe("errors-v1");

    const lookup = control<HTMLSelectElement>(editor.host, "Lookup file");
    expect(Array.from(lookup.options).map(option => option.value)).toEqual(["", "lookup-v1"]);
  });

  it("edits schema, required lookup keys, mappings, typed filters, and preserves full Int64 precision", () => {
    const editor = renderEditor();
    clickButton(editor.host, "Add field", 0);
    editor.render();
    const newName = control<HTMLInputElement>(editor.host, "Source CSV schema field 4 name");
    setInput(newName, "active");
    editor.render();
    setSelect(control<HTMLSelectElement>(editor.host, "Source CSV schema field 4 type"), "boolean");
    editor.render();
    clickCheckbox(control<HTMLInputElement>(editor.host, "Source CSV schema field 4 required"));
    editor.render();
    expect(editor.pipeline.sourceSchema[3]).toEqual({ name: "active", type: "boolean", required: true });

    const sourceKey = control<HTMLSelectElement>(editor.host, "Source lookup field");
    expect(sourceKey.options[2].disabled).toBe(false);
    expect(sourceKey.options[3].disabled).toBe(true);
    setSelect(sourceKey, "status");
    editor.render();
    expect(editor.pipeline.sourceLookupField).toBe("status");
    const lookupKey = control<HTMLSelectElement>(editor.host, "Lookup key field");
    setSelect(lookupKey, "segment");
    editor.render();
    expect(editor.pipeline.lookupKeyField).toBe("segment");

    setSelect(control<HTMLSelectElement>(editor.host, "Output field 1 source"), "lookup");
    editor.render();
    expect(editor.pipeline.mapping[0]).toEqual({ name: "id", origin: "lookup", field: "" });
    const inputField = control<HTMLSelectElement>(editor.host, "Output field 1 input field");
    expect(Array.from(inputField.options).map(option => option.value)).toEqual(["", "customerId", "segment"]);
    setSelect(inputField, "customerId");
    editor.render();

    const exactInt64 = "9223372036854775807";
    setInput(control<HTMLInputElement>(editor.host, "Filter 1 value (64-bit integer)"), exactInt64);
    editor.render();
    expect(formatEtlJsonNumber(editor.pipeline.filters[0].value as ReturnType<typeof parseEtlJsonNumber>)).toBe(exactInt64);
    expect(typeof editor.pipeline.filters[0].value).toBe("object");
    setSelect(control<HTMLSelectElement>(editor.host, "Filter 1 comparison"), "notEqual");
    editor.render();
    expect(editor.pipeline.filters[0].comparison).toBe("notEqual");
  });

  it("preserves incomplete typed literals and surfaces safe server validation feedback", () => {
    const editor = renderEditor();
    setInput(control<HTMLInputElement>(editor.host, "Filter 1 value (64-bit integer)"), "-");
    editor.render();
    expect(editor.pipeline.filters[0].value).toBe("-");

    editor.render({ diagnostics: [{ code: "filter.value.incompatible", path: "filters[0].value", message: "The filter literal must match the mapped field type." }] });
    const feedback = editor.host.querySelector('[aria-live="polite"]')!;
    expect(feedback.textContent).toContain("filter.value.incompatible");
    expect(feedback.textContent).toContain("filters[0].value");
    expect(feedback.textContent).not.toContain("/private/");
  });

  it("edits finite limits and locks every control when viewing a published pipeline", () => {
    const editor = renderEditor();
    setInput(control<HTMLInputElement>(editor.host, "Requested partitions"), "4");
    editor.render();
    expect(editor.pipeline.limits.partitionCount).toBe(4);
    setInput(control<HTMLInputElement>(editor.host, "Maximum output bytes per batch"), "9000000");
    editor.render();
    expect(editor.pipeline.limits.maxOutputBatchBytes).toBe(9_000_000);

    editor.render({ readOnly: true });
    expect(Array.from(editor.host.querySelectorAll<HTMLInputElement | HTMLSelectElement | HTMLButtonElement>("input,select,button")).every(element => element.disabled)).toBe(true);
  });

  it("preserves Int64 limits and lets an author replace null after a field becomes required", () => {
    const initial = pipeline();
    initial.filters[0].value = null;
    const editor = renderEditor(initial);
    const nullToggle = control<HTMLInputElement>(editor.host, "Filter 1 compare with null");
    flushSync(() => nullToggle.click());
    expect(String(editor.pipeline.filters[0].value)).toBe("0");
    editor.render();
    const limit = control<HTMLInputElement>(editor.host, "Maximum bytes per input snapshot");
    setInput(limit, "9223372036854775807");
    expect(String(editor.pipeline.limits.maxSnapshotBytes)).toBe("9223372036854775807");
    editor.render();
    expect(limit.value).toBe("9223372036854775807");
  });

  it("retains an alias outside the loaded page instead of replacing it with another role's binding", () => {
    const initial = pipeline();
    initial.bindings.source = "retired-source";
    const editor = renderEditor(initial);
    const source = control<HTMLSelectElement>(editor.host, "Source file");
    expect(source.value).toBe("retired-source");
    expect(source.selectedOptions[0].textContent).toContain("not loaded for Source CSV");
    expect(editor.pipeline.bindings.source).toBe("retired-source");
    expect(Array.from(source.options).some(option => option.value === "lookup-v1")).toBe(false);
  });
});

function renderEditor(initial = pipeline()) {
  const host = document.createElement("div");
  document.body.appendChild(host);
  const root: Root = createRoot(host);
  let current = initial;
  let options: { readOnly?: boolean; diagnostics?: Array<{ code: string; path: string; message: string }> } = {};
  const onChange = vi.fn((value: EtlPipeline) => { current = value; });
  const render = (next: typeof options = options) => {
    options = next;
    flushSync(() => root.render(
      <EtlPipelineEditor pipeline={current} onChange={onChange} bindings={bindings} {...options} />
    ));
  };
  render();
  const cleanup = () => {
    flushSync(() => root.unmount());
    host.remove();
  };
  mounted.push(cleanup);
  return { host, render, onChange, get pipeline() { return current; } };
}

const bindings: EtlBindingSummary[] = [
  { alias: "source-v1", role: "Source" },
  { alias: "source-v2", role: "Source" },
  { alias: "lookup-v1", role: "Lookup" },
  { alias: "main-v1", role: "MainOutput" },
  { alias: "errors-v1", role: "ErrorOutput" }
];

function pipeline(): EtlPipeline {
  return {
    bindings: { source: "source-v1", lookup: "lookup-v1", output: "main-v1", errors: "errors-v1" },
    sourceSchema: [
      { name: "customerId", type: "int64", required: true },
      { name: "status", type: "string", required: true },
      { name: "memo", type: "string", required: false }
    ],
    lookupSchema: [
      { name: "customerId", type: "int64", required: true },
      { name: "segment", type: "string", required: true }
    ],
    sourceLookupField: "customerId",
    lookupKeyField: "customerId",
    mapping: [
      { name: "id", origin: "source", field: "customerId" },
      { name: "status", origin: "source", field: "status" },
      { name: "segment", origin: "lookup", field: "segment" }
    ],
    filters: [{ field: "id", comparison: "equal", value: parseEtlJsonNumber("9223372036854775807") }],
    limits: {
      partitionCount: 2,
      maxRecordBytes: 65_536,
      maxFields: 256,
      maxBatchRecords: 1_000,
      maxBatchBytes: 4_194_304,
      maxOutstandingRecords: 10_000,
      maxOutstandingBytes: 33_554_432,
      maxLookupRecords: 100_000,
      maxLookupBytes: 16_777_216,
      maxOutputBatchBytes: 8_388_608,
      maxSnapshotBytes: 1_073_741_824,
      maxArtifactBytes: 262_144
    }
  };
}

function control<T extends HTMLElement>(host: HTMLElement, labelText: string): T {
  const label = Array.from(host.querySelectorAll("label")).find(item => item.textContent?.trim() === labelText);
  const element = label?.htmlFor ? Array.from(host.querySelectorAll<HTMLElement>("input,select,textarea")).find(item => item.id === label.htmlFor) : label?.querySelector("input,select,textarea");
  const ariaLabelledElement = Array.from(host.querySelectorAll<HTMLElement>("input,select,textarea")).find(item => item.getAttribute("aria-label") === labelText);
  const control = element ?? ariaLabelledElement;
  if (!control) throw new Error(`Control not found for label: ${labelText}`);
  return control as T;
}

function clickButton(host: HTMLElement, text: string, occurrence = 0) {
  const result = Array.from(host.querySelectorAll<HTMLButtonElement>("button")).filter(item => item.textContent?.trim() === text)[occurrence];
  if (!result) throw new Error(`Button not found: ${text}`);
  flushSync(() => result.click());
}

function setInput(input: HTMLInputElement, value: string) {
  const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, "value")?.set;
  setter?.call(input, value);
  flushSync(() => input.dispatchEvent(new Event("input", { bubbles: true })));
}

function setSelect(select: HTMLSelectElement, value: string) {
  const setter = Object.getOwnPropertyDescriptor(HTMLSelectElement.prototype, "value")?.set;
  setter?.call(select, value);
  flushSync(() => select.dispatchEvent(new Event("change", { bubbles: true })));
}

function clickCheckbox(input: HTMLInputElement) {
  flushSync(() => input.click());
}
