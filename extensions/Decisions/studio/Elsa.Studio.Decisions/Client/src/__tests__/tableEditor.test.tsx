import React, { useEffect, useState } from "react";
import { flushSync } from "react-dom";
import { describe, expect, it, vi } from "vitest";
import { TableEditor } from "../TableEditor";
import type { Diagnostic } from "../decisionsApi";
import { editTable, newDecisionXml, parseTable } from "../decisionXml";
import { button, click, render, type } from "./helpers";

const defaultXml = newDecisionXml();

const key = (element: HTMLElement, value: string, options: KeyboardEventInit = {}) => flushSync(() => element.dispatchEvent(new KeyboardEvent("keydown", { key: value, bubbles: true, cancelable: true, ...options })));
const paste = (element: HTMLElement, text: string) => {
  const event = new Event("paste", { bubbles: true, cancelable: true });
  Object.defineProperty(event, "clipboardData", { value: { getData: () => text } });
  flushSync(() => element.dispatchEvent(event));
};
const copy = (element: HTMLElement) => {
  let text = "";
  const event = new Event("copy", { bubbles: true, cancelable: true });
  Object.defineProperty(event, "clipboardData", { value: { setData: (_type: string, value: string) => { text = value; } } });
  flushSync(() => element.dispatchEvent(event));
  return text;
};
function Editor({ initial = defaultXml, readOnly = false, onChange = vi.fn(), onError = vi.fn(), diagnostics }: { initial?: string; readOnly?: boolean; onChange?: (xml: string) => void; onError?: (message: string) => void; diagnostics?: Diagnostic[] }) {
  const [xml, setXml] = useState(initial);
  useEffect(() => setXml(initial), [initial]);
  return <TableEditor xml={xml} readOnly={readOnly} diagnostics={diagnostics} onChange={next => { setXml(next); onChange(next); }} onError={onError} />;
}

describe("decision table grid editing", () => {
  it("selects a cell, starts editing with Enter, commits with Enter, and cancels with Escape", async () => {
    const changed = vi.fn(), xml = newDecisionXml(), table = parseTable(xml);
    const { host } = render(<Editor initial={xml} onChange={changed} />);
    const cell = host.querySelector<HTMLElement>(`#cell-${table.rules[0].id}-${table.inputs[0].id}`)!;
    click(cell);
    expect(document.activeElement).toBe(cell);
    key(cell, "Enter");
    const input = host.querySelector<HTMLInputElement>('[aria-label="Edit selected cell"]')!;
    expect(input).not.toBeNull();
    type(input, "<= 9007199254740993");
    key(input, "Enter");
    expect(parseTable(changed.mock.calls[0][0]).rules[0].inputs[0]).toBe("<= 9007199254740993");
    await vi.waitFor(() => expect(document.activeElement?.id).toBe(cell.id));
    key(host.querySelector<HTMLElement>(`#${cell.id}`)!, "F2");
    const cancelled = host.querySelector<HTMLInputElement>('[aria-label="Edit selected cell"]')!;
    type(cancelled, "discard this");
    key(cancelled, "Escape");
    expect(changed).toHaveBeenCalledOnce();
  });

  it("moves focus between cells and pastes a rectangular range without coercing source", () => {
    const changed = vi.fn(), xml = newDecisionXml(), table = parseTable(xml);
    const { host } = render(<Editor initial={xml} onChange={changed} />);
    const first = host.querySelector<HTMLElement>(`#cell-${table.rules[0].id}-${table.inputs[0].id}`)!;
    click(first);
    key(first, "ArrowRight");
    const output = host.querySelector<HTMLElement>(`#cell-${table.rules[0].id}-${table.outputs[0].id}`)!;
    expect(document.activeElement).toBe(output);
    click(first);
    paste(first, "<= 9007199254740993\t\"Approved\"");
    const updated = parseTable(changed.mock.calls[0][0]);
    expect(updated.rules[0].inputs).toEqual(["<= 9007199254740993"]);
    expect(updated.rules[0].outputs).toEqual(['"Approved"']);
  });

  it("copies selected rectangular ranges as tab-separated text and supports multi-row paste", () => {
    const changed = vi.fn(), xml = editTable(newDecisionXml(), { kind: "add-rule" });
    const { host } = render(<Editor initial={xml} onChange={changed} />);
    const table = parseTable(xml), first = host.querySelector<HTMLElement>(`#cell-${table.rules[0].id}-${table.inputs[0].id}`)!;
    click(first); key(first, "ArrowDown", { shiftKey: true });
    const second = host.querySelector<HTMLElement>(`#cell-${table.rules[1].id}-${table.inputs[0].id}`)!;
    expect(copy(second)).toBe("-\n-");
    const expanded = render(<Editor initial={xml} onChange={changed} />);
    const originalTable = parseTable(xml);
    const originalCell = expanded.host.querySelector<HTMLElement>(`#cell-${originalTable.rules[0].id}-${originalTable.inputs[0].id}`)!;
    click(originalCell);
    paste(originalCell, "<= 10\t\"Allowed\"\n> 10\t\"Denied\"");
    const changedTable = parseTable(changed.mock.calls[changed.mock.calls.length - 1][0]);
    expect(changedTable.rules).toHaveLength(2);
    expect(changedTable.rules.map(rule => [rule.inputs[0], rule.outputs[0]])).toEqual([["<= 10", '"Allowed"'], ["> 10", '"Denied"']]);
  });

  it("undoes and redoes cell edits as bounded XML snapshots", () => {
    const xml = newDecisionXml(), table = parseTable(xml);
    const { host } = render(<Editor initial={xml} />);
    const cell = host.querySelector<HTMLElement>(`#cell-${table.rules[0].id}-${table.inputs[0].id}`)!;
    click(cell); key(cell, "Enter");
    const editor = host.querySelector<HTMLInputElement>('[aria-label="Edit selected cell"]')!;
    type(editor, "> 100"); key(editor, "Enter");
    click(button(host, "Undo"));
    expect(host.querySelector(`#${cell.id}`)?.textContent).toBe("-");
    click(button(host, "Redo"));
    expect(host.querySelector(`#${cell.id}`)?.textContent).toBe("> 100");
  });

  it("duplicates from the row menu and resets history when the source document is replaced", async () => {
    const xml = newDecisionXml(), source = parseTable(xml); let current = xml;
    const { host, update } = render(<Editor initial={xml} onChange={next => { current = next; }} />);
    const trigger = host.querySelector<HTMLButtonElement>('button[aria-label="Actions for rule 1"]')!;
    click(trigger);
    const menu = document.getElementById(trigger.getAttribute("aria-controls")!)!;
    click(Array.from(menu.querySelectorAll<HTMLButtonElement>('[role="menuitem"]')).find(item => item.textContent === "Duplicate below")!);
    expect(host.querySelectorAll("tbody tr")).toHaveLength(2);
    const duplicated = parseTable(current).rules;
    expect(duplicated).toHaveLength(2);
    expect(duplicated[1].id).not.toBe(source.rules[0].id);
    expect(duplicated[1].inputs).toEqual(source.rules[0].inputs);
    click(button(host, "Undo"));
    expect(host.querySelectorAll("tbody tr")).toHaveLength(1);
    click(button(host, "Redo"));
    expect(host.querySelectorAll("tbody tr")).toHaveLength(2);
    const external = newDecisionXml();
    update(<Editor initial={external} />);
    await vi.waitFor(() => expect(host.querySelectorAll("tbody tr")).toHaveLength(1));
    await vi.waitFor(() => expect(button(host, "Undo").disabled).toBe(true));
    expect(parseTable(external).rules[0].id).not.toBe(source.rules[0].id);
  });
  it("keeps column settings closed until requested and dismisses them with focus return", async () => {
    const xml = newDecisionXml(), table = parseTable(xml);
    const { host } = render(<Editor initial={xml} />);
    const trigger = host.querySelector<HTMLButtonElement>('[aria-label="Settings for input 1"]')!;
    expect(trigger.getAttribute("aria-expanded")).toBe("false");
    expect(document.querySelectorAll(".decision-column-popover")).toHaveLength(0);

    click(trigger);
    expect(trigger.getAttribute("aria-expanded")).toBe("true");
    const popover = document.getElementById(trigger.getAttribute("aria-controls")!)!;
    expect(popover).not.toBeNull();
    expect(popover.closest(".decision-table-scroll")).toBeNull();
    expect(popover.querySelector('input[aria-label="Input 1 name"]')).not.toBeNull();

    key(popover.querySelector<HTMLElement>("input")!, "Escape");
    expect(document.getElementById(popover.id)).toBeNull();
    await vi.waitFor(() => expect(document.activeElement).toBe(trigger));
    expect(table.inputs).toHaveLength(1);
  });
  it("dismisses column settings on outside pointer without stealing focus", () => {
    const { host } = render(<Editor />);
    const trigger = host.querySelector<HTMLButtonElement>('[aria-label="Settings for input 1"]')!;
    click(trigger);
    const popover = document.getElementById(trigger.getAttribute("aria-controls")!)!;
    const outside = host.querySelector<HTMLElement>(".decision-grid-cell")!;
    outside.focus();
    flushSync(() => document.dispatchEvent(new Event("pointerdown", { bubbles: true })));
    expect(document.getElementById(popover.id)).toBeNull();
    expect(document.activeElement).toBe(outside);
  });
  it("keeps row actions narrow and renders their menu outside the scrolling grid", () => {
    const { host } = render(<Editor />);
    const grid = host.querySelector('[role="grid"]')!;
    expect(grid.querySelectorAll("tbody tr")).toHaveLength(1);
    const trigger = host.querySelector<HTMLButtonElement>('[aria-label="Actions for rule 1"]')!;
    expect(trigger.closest("td")?.classList.contains("decision-grid-actions")).toBe(true);
    click(trigger);
    const menu = document.querySelector<HTMLElement>('[role="menu"][aria-label="Actions for rule 1"]')!;
    expect(menu).not.toBeNull();
    expect(menu.closest(".decision-table-scroll")).toBeNull();
    expect(menu.querySelectorAll('[role="menuitem"]')).toHaveLength(6);
  });
  it("commits an active cell edit when focus leaves the editor", () => {
    const changed = vi.fn(), xml = newDecisionXml(), table = parseTable(xml);
    const { host } = render(<Editor initial={xml} onChange={changed} />);
    const cell = host.querySelector<HTMLElement>(`#cell-${table.rules[0].id}-${table.inputs[0].id}`)!;
    click(cell);
    key(cell, "Enter");
    const editor = host.querySelector<HTMLInputElement>('[aria-label="Edit selected cell"]')!;
    type(editor, "> 21");
    const target = button(host, "Add rule");
    flushSync(() => target.focus());
    expect(parseTable(changed.mock.calls[0][0]).rules[0].inputs[0]).toBe("> 21");
    expect(host.querySelector('[aria-label="Edit selected cell"]')).toBeNull();
    expect(document.activeElement).toBe(target);
  });

  it("associates diagnostics with the affected cell", () => {
    const xml = newDecisionXml(), table = parseTable(xml), diagnostic: Diagnostic = { code: "InvalidCell", message: "Review this condition.", ruleId: table.rules[0].id, clauseId: table.inputs[0].id };
    const { host } = render(<Editor initial={xml} diagnostics={[diagnostic]} />);
    const cell = host.querySelector<HTMLElement>(`#cell-${table.rules[0].id}-${table.inputs[0].id}`)!;
    expect(cell.getAttribute("aria-describedby")).toBe(`diagnostic-${table.rules[0].id}-${table.inputs[0].id}`);
    expect(host.querySelector(`#diagnostic-${table.rules[0].id}-${table.inputs[0].id}`)?.textContent).toBe("Review this condition.");
  });

  it("expands the table workspace and makes annotations available in read-only mode", () => {
    const xml = newDecisionXml(), editable = render(<Editor initial={xml} />);
    const section = editable.host.querySelector(".decision-table-section")!;
    expect(section.classList.contains("is-expanded")).toBe(false);
    click(button(editable.host, "Expand table"));
    expect(section.classList.contains("is-expanded")).toBe(true);
    expect(button(editable.host, "Collapse table").getAttribute("aria-pressed")).toBe("true");
    click(button(editable.host, "Show annotations"));
    expect(editable.host.querySelector("thead")?.textContent).toContain("Annotation");

    const readOnly = render(<Editor initial={xml} readOnly />);
    click(button(readOnly.host, "Show annotations"));
    expect(readOnly.host.querySelector("thead")?.textContent).toContain("Annotation");
    expect(button(readOnly.host, "Expand table").disabled).toBe(false);
  });

  it("rejects out-of-bounds paste without publishing a partial change", () => {
    const changed = vi.fn(), error = vi.fn(), xml = newDecisionXml(), table = parseTable(xml);
    const { host } = render(<Editor initial={xml} onChange={changed} onError={error} />);
    const first = host.querySelector<HTMLElement>(`#cell-${table.rules[0].id}-${table.inputs[0].id}`)!;
    click(first);
    paste(first, "one\ttwo\nthree");
    expect(changed).not.toHaveBeenCalled();
    expect(error).toHaveBeenCalledWith(expect.stringContaining("not rectangular"));
    paste(first, "one\ttwo\tthree");
    expect(changed).not.toHaveBeenCalled();
    expect(error).toHaveBeenCalledWith(expect.stringContaining("outside the decision table"));
  });

  it("keeps read-only cells navigable while preventing editing and row mutations", () => {
    const xml = newDecisionXml(), table = parseTable(xml);
    const { host } = render(<Editor initial={xml} readOnly />);
    const cell = host.querySelector<HTMLElement>(`#cell-${table.rules[0].id}-${table.inputs[0].id}`)!;
    click(cell);
    key(cell, "Enter");
    expect(host.querySelector('[aria-label="Edit selected cell"]')).toBeNull();
    expect(host.textContent).not.toContain("Add rule");
    expect(document.activeElement).toBe(cell);
  });
});