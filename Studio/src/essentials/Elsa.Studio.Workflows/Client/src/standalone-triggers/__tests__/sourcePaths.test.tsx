import React, { useState, type ReactNode } from "react";
import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, describe, expect, it, vi } from "vitest";
import type { TriggerClient } from "../api";
import { MappingEditor, mappingDrafts, realizeMappings, type MappingDrafts } from "../MappingEditor";
import { parseLiteralJson, serializeTriggerPayload } from "../json";
import { formatSourcePath, parseSourcePath } from "../SourceFieldSelector";
import type { DeclaredInput, InputMapping, MappingContext, TargetView } from "../types";

const inputs: DeclaredInput[] = [
  { name: "Customer", type: { alias: "String", collectionKind: "Single" }, isRequired: true },
  { name: "Formula", type: { alias: "String", collectionKind: "Single" }, isRequired: false }
];
const target: TargetView = { definitionId: "workflow", slotName: "default", state: "Available", contractRevision: "contract-1", inputContract: { version: 1, inputs }, findings: [] };
const context: MappingContext = { sourceSchema: { fields: [{ path: ["payload"], label: "Payload", type: "JSON" }, { path: ["eventName"], label: "Event name", type: "String" }] }, target, supportedExpressions: [{ language: "JavaScript", displayName: "JavaScript", available: true }], findings: [] };
const roots: Root[] = [];
afterEach(() => { for (const root of roots) flushSync(() => root.unmount()); roots.length = 0; document.body.innerHTML = ""; vi.restoreAllMocks(); });

function mount(node: ReactNode) {
  const container = document.createElement("div"); document.body.appendChild(container);
  const root = createRoot(container); roots.push(root); flushSync(() => root.render(node));
  return container;
}
function control(container: HTMLElement, label: string) {
  return container.querySelector<HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement>(`[aria-label="${label}"]`)!;
}
function fill(element: HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement, value: string) {
  const prototype = element instanceof HTMLSelectElement ? HTMLSelectElement.prototype : element instanceof HTMLTextAreaElement ? HTMLTextAreaElement.prototype : HTMLInputElement.prototype;
  Object.getOwnPropertyDescriptor(prototype, "value")!.set!.call(element, value);
  flushSync(() => element.dispatchEvent(new Event(element instanceof HTMLSelectElement ? "change" : "input", { bubbles: true })));
}
function click(container: HTMLElement, text: string) {
  const element = [...container.querySelectorAll<HTMLButtonElement>("button")].find(item => item.textContent === text)!;
  flushSync(() => element.click());
}
function editor(mappings: InputMapping[] = [], client = { mappingPreview: vi.fn(async () => ({ advisory: true, fields: [{ inputName: "Customer", state: "Mapped", value: "customer-7" }], findings: [] })) } as unknown as TriggerClient) {
  let current: MappingDrafts = mappingDrafts(mappings);
  function Harness() {
    const [drafts, setDrafts] = useState(current); current = drafts;
    return <MappingEditor client={client} triggerId="trigger" triggerRevision={1} target={target} context={context} drafts={drafts} onChange={setDrafts} errors={{}} />;
  }
  return { container: mount(<Harness />), drafts: () => current, client };
}

describe("standalone source property paths", () => {
  it("keeps nested and special property names as exact string segments", () => {
    const path = ["payload", "customer.name", "postal/address", "~label", "0", "items[0]", ""];
    expect(formatSourcePath(path)).toBe("/payload/customer.name/postal~1address/~0label/0/items[0]/");
    expect(parseSourcePath(formatSourcePath(path))).toEqual(path);
    expect(parseSourcePath("/payload/customer/id")).toEqual(["payload", "customer", "id"]);
    expect(parseSourcePath("/payload/~01")).toEqual(["payload", "~1"]);
    expect(parseSourcePath("/")).toEqual([""]);
  });

  it.each(["", "payload/customer/id", "#/payload/customer/id", "/payload/bad~2", "/payload/~"])("refuses an invalid path %j", pathText => {
    const drafts = mappingDrafts([{ inputName: "Customer", binding: { kind: "SourceField", path: ["payload", "previous"] } }]);
    drafts.Customer.pathText = pathText;
    expect(() => parseSourcePath(pathText)).toThrow();
    expect(realizeMappings(inputs, drafts)).toEqual({ mappings: [], errors: { Customer: expect.any(String) } });
  });

  it("serializes only API segments and preserves selection of whole array values", () => {
    const arrayInput: DeclaredInput = { name: "Items", type: { alias: "Json", collectionKind: "Single" }, isRequired: false };
    const drafts = mappingDrafts([{ inputName: "Items", binding: { kind: "SourceField", path: ["payload"] } }]);
    drafts.Items.pathText = "/payload/items";
    const built = realizeMappings([arrayInput], drafts);
    expect(built.errors).toEqual({});
    expect(serializeTriggerPayload(built.mappings)).toBe('[{"inputName":"Items","binding":{"kind":"SourceField","path":["payload","items"]}}]');
    const sample = '{"payload":{"items":[{"id":18446744073709551615},null,["nested"]]}}';
    expect(serializeTriggerPayload(parseLiteralJson(sample))).toBe(sample);
    expect(parseSourcePath("/payload/0")).toEqual(["payload", "0"]);
  });
});

describe("mounted source path authoring", () => {
  it("authors a nested payload path, invalidates its preview on edits and corrects invalid text without using the old path", async () => {
    const { container, drafts, client } = editor();
    fill(control(container, "Value source for Customer"), "SourceField");
    fill(control(container, "Source field for Customer"), "custom");
    const custom = control(container, "Source path for Customer");
    expect(custom.value).toBe(""); expect(custom.getAttribute("aria-invalid")).toBe("true");
    const error = document.getElementById(custom.getAttribute("aria-describedby")!);
    expect(error?.getAttribute("role")).toBe("alert"); expect(error?.textContent).toContain("Enter a source path");
    fill(custom, "/payload/customer/id");
    expect(custom.getAttribute("aria-invalid")).toBe("false");
    expect(realizeMappings(inputs, drafts()).mappings).toEqual([{ inputName: "Customer", binding: { kind: "SourceField", path: ["payload", "customer", "id"] } }]);
    fill(control(container, "Sample source envelope"), '{"payload":{"customer":{"id":"customer-7"}}}');
    click(container, "Preview mapped inputs");
    await vi.waitFor(() => expect(container.textContent).toContain("Sample preview · advisory"));
    expect(client.mappingPreview).toHaveBeenCalledWith("trigger", 1, { definitionId: "workflow", slotName: "default" }, [{ inputName: "Customer", binding: { kind: "SourceField", path: ["payload", "customer", "id"] } }], { payload: { customer: { id: "customer-7" } } }, "contract-1");
    fill(custom, "/payload/bad~2");
    expect(custom.value).toBe("/payload/bad~2"); expect(custom.getAttribute("aria-invalid")).toBe("true");
    expect(container.textContent).toContain("Run preview again for the current values");
    expect(realizeMappings(inputs, drafts()).mappings).toEqual([]);
    click(container, "Preview mapped inputs");
    expect(client.mappingPreview).toHaveBeenCalledTimes(1);
    fill(custom, "/payload/customer.name/id");
    expect(realizeMappings(inputs, drafts()).errors).toEqual({});
    expect(realizeMappings(inputs, drafts()).mappings[0].binding).toEqual({ kind: "SourceField", path: ["payload", "customer.name", "id"] });
    fill(control(container, "Source field for Customer"), '["eventName"]');
    expect(control(container, "Source field for Customer").value).toBe('["eventName"]');
    expect(control(container, "Source path for Customer")).toBeNull();
    expect(realizeMappings(inputs, drafts()).mappings[0].binding).toEqual({ kind: "SourceField", path: ["eventName"] });
  });

  it("reopens custom input and expression paths, preserves literals and serializes corrected parameter paths", () => {
    const literal = '[18446744073709551615,0.1234567890123456789012345678,null,{"nested":[true]}]';
    const mappings: InputMapping[] = [
      { inputName: "Customer", binding: { kind: "SourceField", path: ["payload", "customer.name", "address/line", "~key", "0"] } },
      { inputName: "Formula", binding: { kind: "Expression", template: { language: "JavaScript", source: "args.customerId", resultType: { alias: "String", collectionKind: "Single" }, options: { strict: true }, capabilityProfile: "binding-pure-v1" }, parameters: {
        customerId: { kind: "SourceField", path: ["payload", "customer", "id"] }, configured: { kind: "Literal", value: parseLiteralJson(literal) }
      } } }
    ];
    const { container, drafts } = editor(mappings);
    expect(control(container, "Source field for Customer").value).toBe("custom");
    expect(control(container, "Source path for Customer").value).toBe("/payload/customer.name/address~1line/~0key/0");
    expect(control(container, "Parameter 1 source for Formula").value).toBe("custom");
    expect(control(container, "Parameter 1 source path for Formula").value).toBe("/payload/customer/id");
    expect(control(container, "Parameter 2 source for Formula").value).toBe("literal");
    expect(control(container, "Parameter 2 value for Formula").value).toBe(literal);
    fill(control(container, "Parameter 1 source path for Formula"), "payload/customer/id");
    expect(realizeMappings(inputs, drafts()).errors.Formula).toContain("Source for customerId: Start the source path with /");
    fill(control(container, "Parameter 1 source path for Formula"), "/payload/billing~1customer/~0id");
    const built = realizeMappings(inputs, drafts());
    expect(built.errors).toEqual({});
    const expected: InputMapping[] = [mappings[0], { ...mappings[1], binding: { ...(mappings[1].binding as Extract<InputMapping["binding"], { kind: "Expression" }>), parameters: { customerId: { kind: "SourceField", path: ["payload", "billing/customer", "~id"] }, configured: { kind: "Literal", value: parseLiteralJson(literal) } } } }];
    expect(serializeTriggerPayload(built.mappings)).toBe(serializeTriggerPayload(expected));
    const reopened = editor(built.mappings).container;
    expect(control(reopened, "Source field for Customer").value).toBe("custom");
    expect(control(reopened, "Parameter 1 source for Formula").value).toBe("custom");
    expect(control(reopened, "Parameter 1 source path for Formula").value).toBe("/payload/billing~1customer/~0id");
    expect(control(reopened, "Parameter 2 value for Formula").value).toBe(literal);
  });
});
