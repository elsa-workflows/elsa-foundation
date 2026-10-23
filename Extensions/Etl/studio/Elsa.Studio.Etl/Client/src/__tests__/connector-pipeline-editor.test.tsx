import React from "react";
import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, describe, expect, it, vi } from "vitest";
import { createDefaultEtlConnectorPipeline } from "../connectorAuthoringHelpers";
import { EtlConnectorPipelineEditor } from "../components/EtlConnectorPipelineEditor";
import { formatEtlJsonNumber, type EtlConnectorBindingSummary, type EtlJsonNumber, type EtlPipelineV2 } from "../etlTypes";

const mounted: Array<() => void> = [];

afterEach(() => mounted.splice(0).forEach(cleanup => cleanup()));

describe("connector pipeline editor", () => {
  it("starts from the v2 contract defaults without materializing omitted retained limits", () => {
    const pipeline = createDefaultEtlConnectorPipeline();
    expect(pipeline.source).toEqual({
      kind: "httpJson",
      binding: "source",
      recordsPointer: "/records",
      fields: [
        { name: "id", pointer: "/id", type: "int64", required: true },
        { name: "name", pointer: "/name", type: "string", required: true }
      ],
      pagination: { kind: "token", continuationPointer: "/next", queryParameter: "cursor" }
    });
    expect(pipeline.limits).toMatchObject({ filePartitionCount: 2, maxPartitions: 32, maxBatchBytes: 4_194_304, maxSnapshotBytes: 1_073_741_824 });
    expect(pipeline.http).toMatchObject({ maxPageBytes: 8_388_608, maxRetainedInputBytes: 1_073_741_824, maxAttempts: 5, deadlineSeconds: 900 });
    expect(pipeline.limits.maxRetainedOutputBytes).toBeUndefined();
    expect(pipeline.http.maxRetainedMetadataBytes).toBeUndefined();

    const editor = renderEditor(pipeline);
    const authoringLabels = Array.from(editor.host.querySelectorAll("label"), label => label.textContent ?? "").join(" ");
    expect(authoringLabels).not.toMatch(/endpoint|credential|password/i);
    expect(editor.host.querySelectorAll('input[type="url"],input[type="password"]')).toHaveLength(0);
    setInput(control<HTMLInputElement>(editor.host, "Maximum records per HTTP page"), "9999");
    expect(editor.pipeline.http.maxRetainedMetadataBytes).toBeUndefined();
    expect(editor.pipeline.limits.maxRetainedOutputBytes).toBeUndefined();
  });

  it("filters approved aliases by both role and connector kind while retaining unavailable values", () => {
    const initial = createDefaultEtlConnectorPipeline();
    initial.source.binding = "retired-source";
    const editor = renderEditor(initial, { bindings: [{ alias: "retired-source", role: "Source", kind: "csv" }, ...bindings] });
    const source = control<HTMLSelectElement>(editor.host, "Source HTTP binding");
    expect(Array.from(source.options).map(option => option.value)).toEqual(["", "retired-source", "source-http"]);
    expect(source.options[1].textContent).toContain("not approved for Source HTTP");

    setSelect(source, "source-http");
    editor.render();
    expect(editor.pipeline.source.binding).toBe("source-http");
    const accepted = control<HTMLSelectElement>(editor.host, "Accepted records binding");
    expect(Array.from(accepted.options).map(option => option.value)).toEqual(["", "output", "output-http"]);
    setSelect(control<HTMLSelectElement>(editor.host, "Accepted records destination kind"), "jsonl");
    editor.render();
    const acceptedJsonl = control<HTMLSelectElement>(editor.host, "Accepted records binding");
    expect(Array.from(acceptedJsonl.options).map(option => option.value)).toEqual(["", "output", "output-jsonl"]);
    expect(editor.pipeline.routes.errors).toEqual({ kind: "http", binding: "errors" });
  });

  it("edits JSON record fields and all pagination variants using the declared pointers", () => {
    const editor = renderEditor();
    setInput(control<HTMLInputElement>(editor.host, "Records JSON Pointer"), "/data/items");
    expect((editor.pipeline.source as Extract<EtlPipelineV2["source"], { kind: "httpJson" }>).recordsPointer).toBe("/data/items");

    clickButton(editor.host, "Add field");
    editor.render();
    setInput(control<HTMLInputElement>(editor.host, "HTTP JSON field 3 name"), "active");
    editor.render();
    setInput(control<HTMLInputElement>(editor.host, "HTTP JSON field 3 pointer"), "/profile/active");
    editor.render();
    setSelect(control<HTMLSelectElement>(editor.host, "HTTP JSON field 3 type"), "boolean");
    editor.render();
    clickCheckbox(control<HTMLInputElement>(editor.host, "HTTP JSON field 3 required"));
    expect((editor.pipeline.source as Extract<EtlPipelineV2["source"], { kind: "httpJson" }>).fields[2]).toEqual({ name: "active", pointer: "/profile/active", type: "boolean", required: true });

    setSelect(control<HTMLSelectElement>(editor.host, "Pagination strategy"), "nextLink");
    editor.render();
    setInput(control<HTMLInputElement>(editor.host, "Continuation JSON Pointer"), "/links/next");
    editor.render();
    expect((editor.pipeline.source as Extract<EtlPipelineV2["source"], { kind: "httpJson" }>).pagination).toEqual({ kind: "nextLink", continuationPointer: "/links/next" });
    setSelect(control<HTMLSelectElement>(editor.host, "Pagination strategy"), "token");
    editor.render();
    setInput(control<HTMLInputElement>(editor.host, "Continuation query parameter"), "page cursor");
    expect((editor.pipeline.source as Extract<EtlPipelineV2["source"], { kind: "httpJson" }>).pagination).toEqual({ kind: "token", continuationPointer: "/links/next", queryParameter: "page cursor" });

    const origin = control<HTMLSelectElement>(editor.host, "Output field 1 source");
    expect(Array.from(origin.options).map(option => option.textContent)).toContain("Source JSON");
  });

  it("supports switching to CSV and configuring an optional typed-key lookup", () => {
    const editor = renderEditor();
    setSelect(control<HTMLSelectElement>(editor.host, "Source kind"), "csv");
    editor.render();
    const source = editor.pipeline.source as Extract<EtlPipelineV2["source"], { kind: "csv" }>;
    expect(source.schema).toEqual([
      { name: "id", type: "int64", required: true },
      { name: "name", type: "string", required: true }
    ]);
    setSelect(control<HTMLSelectElement>(editor.host, "Source CSV binding"), "source-csv");
    editor.render();
    clickCheckbox(control<HTMLInputElement>(editor.host, "Use a CSV lookup"));
    editor.render();
    expect(editor.pipeline.lookup).toEqual({ binding: "lookup", schema: [{ name: "id", type: "int64", required: true }], sourceKeyField: "id", lookupKeyField: "id" });
    setSelect(control<HTMLSelectElement>(editor.host, "Lookup CSV binding"), "lookup-csv");
    editor.render();
    expect(control<HTMLSelectElement>(editor.host, "Source lookup field").value).toBe("id");
    expect(control<HTMLSelectElement>(editor.host, "Lookup key field").value).toBe("id");
    setSelect(control<HTMLSelectElement>(editor.host, "Output field 1 source"), "lookup");
    expect(editor.pipeline.mapping[0].origin).toBe("lookup");
  });

  it("edits all lossless byte controls without rounding and locks every control in read-only mode", () => {
    const editor = renderEditor();
    const exact = "9223372036854775807";
    setInput(control<HTMLInputElement>(editor.host, "Maximum retained HTTP input bytes"), exact);
    expect(formatEtlJsonNumber(editor.pipeline.http.maxRetainedInputBytes as EtlJsonNumber)).toBe(exact);
    expect(typeof editor.pipeline.http.maxRetainedInputBytes).toBe("object");
    expect(editor.pipeline.http.maxRetainedMetadataBytes).toBeUndefined();
    setInput(control<HTMLInputElement>(editor.host, "Maximum retained route bytes"), exact);
    expect(formatEtlJsonNumber(editor.pipeline.limits.maxRetainedOutputBytes as EtlJsonNumber)).toBe(exact);

    editor.render({ readOnly: true });
    expect(Array.from(editor.host.querySelectorAll<HTMLInputElement | HTMLSelectElement | HTMLButtonElement>("input,select,button")).every(element => element.disabled)).toBe(true);
  });

  it("shows loading and unavailable states without discarding the selected alias", () => {
    const initial = createDefaultEtlConnectorPipeline();
    const loading = renderEditor(initial, { bindings: [], bindingsLoading: true });
    expect(loading.host.querySelector('[role="status"]')?.textContent).toContain("Loading current approved connector aliases");
    expect(control<HTMLSelectElement>(loading.host, "Source HTTP binding").value).toBe("source");
    expect(Array.from(control<HTMLSelectElement>(loading.host, "Source HTTP binding").options).find(option => option.value === "")?.disabled).toBe(true);

    loading.render({ bindings: [], bindingsUnavailable: true });
    expect(loading.host.querySelector('[role="alert"]')?.textContent).toContain("could not be loaded");
    expect(control<HTMLSelectElement>(loading.host, "Source HTTP binding").value).toBe("source");
  });
});

const bindings: EtlConnectorBindingSummary[] = [
  { alias: "source-http", role: "Source", kind: "http" },
  { alias: "source-csv", role: "Source", kind: "csv" },
  { alias: "lookup-csv", role: "Lookup", kind: "csv" },
  { alias: "lookup-http", role: "Lookup", kind: "http" },
  { alias: "output-http", role: "MainOutput", kind: "http" },
  { alias: "output-jsonl", role: "MainOutput", kind: "jsonl" },
  { alias: "errors-http", role: "ErrorOutput", kind: "http" },
  { alias: "errors-jsonl", role: "ErrorOutput", kind: "jsonl" }
];

function renderEditor(initial = createDefaultEtlConnectorPipeline(), props: { readOnly?: boolean; diagnostics?: Array<{ code: string; path: string; message: string }>; bindings?: EtlConnectorBindingSummary[]; bindingsLoading?: boolean; bindingsUnavailable?: boolean } = {}) {
  const host = document.createElement("div");
  document.body.appendChild(host);
  const root: Root = createRoot(host);
  let current = initial;
  let options = props;
  const onChange = vi.fn((value: EtlPipelineV2) => { current = value; });
  const render = (next: typeof options = options) => {
    options = next;
    flushSync(() => root.render(
      <EtlConnectorPipelineEditor pipeline={current} onChange={onChange} bindings={options.bindings ?? bindings} {...options} />
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

function control<T extends HTMLElement>(host: HTMLElement, labelText: string): T {
  const label = Array.from(host.querySelectorAll("label")).find(item => item.textContent?.trim() === labelText);
  const element = label?.htmlFor ? Array.from(host.querySelectorAll<HTMLElement>("input,select,textarea")).find(item => item.id === label.htmlFor) : label?.querySelector("input,select,textarea");
  const ariaLabelledElement = Array.from(host.querySelectorAll<HTMLElement>("input,select,textarea")).find(item => item.getAttribute("aria-label") === labelText);
  const result = element ?? ariaLabelledElement;
  if (!result) throw new Error(`Control not found for label: ${labelText}`);
  return result as T;
}

function clickButton(host: HTMLElement, text: string, occurrence = 0) {
  const button = Array.from(host.querySelectorAll<HTMLButtonElement>("button")).filter(item => item.textContent?.trim() === text)[occurrence];
  if (!button) throw new Error(`Button not found: ${text}`);
  flushSync(() => button.click());
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
