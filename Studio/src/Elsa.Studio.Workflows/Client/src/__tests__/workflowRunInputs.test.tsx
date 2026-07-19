import React from "react";
import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, describe, expect, it, vi } from "vitest";
import type { StudioWorkflowRunInputEditorContribution } from "@elsa-workflows/studio-sdk";
import type { WorkflowInput } from "../workflowTypes";
import {
  parseWorkflowRunInputs,
  resolveWorkflowRunInputEditor,
  serializeWorkflowExecutionPayload
} from "../workflowRunInputs";
import { WorkflowRunInputDialog } from "../workflow-editor/WorkflowRunInputDialog";
import { createEnumWorkflowRunInputEditorContribution } from "../workflowRunInputEditorContributions";

let active: { root: Root; container: HTMLElement } | null = null;

afterEach(() => {
  if (!active) return;
  flushSync(() => active!.root.unmount());
  active.container.remove();
  active = null;
});

describe("workflow run inputs", () => {
  it("resolves the highest-priority contribution supported by declared type metadata", () => {
    const input = workflowInput("status", "Status", "Contoso.OrderStatus", true);
    const fallback = editorContribution("fallback", 500, () => true);
    const enumEditor = editorContribution("enum", 100, candidate => candidate.type.alias === "Contoso.OrderStatus");

    expect(resolveWorkflowRunInputEditor([fallback, enumEditor], input)).toBe(enumEditor);
    expect(resolveWorkflowRunInputEditor([enumEditor], workflowInput("name", "Name", "String", true))).toBeUndefined();
  });

  it("delegates custom validation and wire serialization to the resolved contribution", () => {
    const input = workflowInput("order", "__proto__", "Contoso.OrderId", true);
    const editor = editorContribution("order-id", 100, candidate => candidate.type.alias === "Contoso.OrderId");
    editor.validate = ({ draft }) => /^order-\d+$/.test(draft) ? undefined : "Enter an order ID such as order-42.";
    editor.serialize = ({ draft }) => ({ id: Number(draft.slice("order-".length)) });

    expect(parseWorkflowRunInputs([input], { order: "wrong" }, [editor])).toEqual({
      values: {},
      errors: { order: "Enter an order ID such as order-42." }
    });

    const parsed = parseWorkflowRunInputs([input], { order: "order-42" }, [editor]);
    expect(parsed.errors).toEqual({});
    expect(Object.prototype.hasOwnProperty.call(parsed.values, "__proto__")).toBe(true);
    expect(serializeWorkflowExecutionPayload({ inputs: parsed.values })).toBe('{"inputs":{"__proto__":{"id":42}}}');
  });

  it("reports the failed contribution phase when callbacks fail or values disappear from the wire payload", () => {
    const input = workflowInput("order", "Order", "Contoso.OrderId", true);
    const throwingSupports = editorContribution("throwing-supports", 50, () => { throw new Error("supports failed"); });
    const throwingValidation = editorContribution("throwing-validation", 100, candidate => candidate.type.alias === "Contoso.OrderId");
    throwingValidation.validate = () => { throw new Error("validation failed"); };
    const invalidSerialization = editorContribution("invalid-serialization", 100, candidate => candidate.type.alias === "Contoso.OrderId");
    invalidSerialization.serialize = () => undefined;

    const consoleError = vi.spyOn(console, "error").mockImplementation(() => undefined);
    try {
      expect(resolveWorkflowRunInputEditor([throwingSupports], input)).toBeUndefined();
      expect(parseWorkflowRunInputs([input], { order: "order-42" }, [throwingValidation])).toEqual({
        values: {},
        errors: { order: "The Order editor failed while validating." },
        contributionFailures: { order: { phase: "validate", reason: "callback-error" } }
      });
      expect(parseWorkflowRunInputs([input], { order: "order-42" }, [invalidSerialization])).toEqual({
        values: {},
        errors: { order: "The Order editor returned a value that cannot be sent." },
        contributionFailures: { order: { phase: "serialize", reason: "invalid-wire-value" } }
      });
      expect(consoleError).toHaveBeenCalled();
    } finally {
      consoleError.mockRestore();
    }
  });

  it.each([
    {
      failure: "validation callback exception",
      configure: (editor: StudioWorkflowRunInputEditorContribution) => {
        editor.validate = () => { throw new Error("validation failed"); };
      },
      message: "The Payload editor failed while validating. Enter a JSON value instead."
    },
    {
      failure: "serialization callback exception",
      configure: (editor: StudioWorkflowRunInputEditorContribution) => {
        editor.serialize = () => { throw new Error("serialization failed"); };
      },
      message: "The Payload editor failed while serializing. Enter a JSON value instead."
    },
    {
      failure: "invalid serialized value",
      configure: (editor: StudioWorkflowRunInputEditorContribution) => {
        editor.serialize = () => undefined;
      },
      message: "The Payload editor returned a value that cannot be sent. Enter a JSON value instead."
    }
  ])("switches to the accessible fallback after a $failure and lets the next submit succeed", ({ configure, message }) => {
    const input = workflowInput("payload", "Payload", "Contoso.Order", true);
    const editor = editorContribution("unreliable-editor", 100, candidate => candidate.type.alias === "Contoso.Order");
    editor.component = ({ draft, disabled, controlProps, onChange }) => (
      <textarea
        {...controlProps}
        disabled={disabled}
        value={draft}
        data-contributed-editor="true"
        onChange={event => onChange(event.currentTarget.value)}
      />
    );
    configure(editor);
    const onSubmit = vi.fn();
    const consoleError = vi.spyOn(console, "error").mockImplementation(() => undefined);

    try {
      const container = render(
        <WorkflowRunInputDialog inputs={[input]} editors={[editor]} onSubmit={onSubmit} onCancel={vi.fn()} />
      );
      const contributedControl = container.querySelector<HTMLTextAreaElement>("[data-contributed-editor='true']")!;
      fill(contributedControl, '{"id":42}');

      submit(container);

      expect(onSubmit).not.toHaveBeenCalled();
      expect(container.querySelector("[data-contributed-editor='true']")).toBeNull();
      const fallback = container.querySelector<HTMLTextAreaElement>("textarea[aria-label='Payload']")!;
      expect(fallback.value).toBe('{"id":42}');
      expect(fallback.placeholder).toBe("Enter JSON");
      expect(container.querySelector("[role='alert']")?.textContent).toBe(message);

      submit(container);
      expect(onSubmit).toHaveBeenCalledWith({ Payload: { id: 42 } });
    } finally {
      consoleError.mockRestore();
    }
  });

  it("switches a failed primitive contribution to its standard typed input", () => {
    const input = workflowInput("count", "Count", "Int32", true);
    const editor = editorContribution("unreliable-counter", 100, candidate => candidate.type.alias === "Int32");
    editor.component = ({ draft, disabled, controlProps, onChange }) => (
      <input
        {...controlProps}
        disabled={disabled}
        value={draft}
        data-contributed-editor="true"
        onChange={event => onChange(event.currentTarget.value)}
      />
    );
    editor.validate = () => { throw new Error("validation failed"); };
    const onSubmit = vi.fn();
    const consoleError = vi.spyOn(console, "error").mockImplementation(() => undefined);

    try {
      const container = render(
        <WorkflowRunInputDialog inputs={[input]} editors={[editor]} onSubmit={onSubmit} onCancel={vi.fn()} />
      );
      fill(container.querySelector<HTMLInputElement>("[data-contributed-editor='true']"), "42");

      submit(container);

      const fallback = container.querySelector<HTMLInputElement>("input[aria-label='Count']")!;
      expect(fallback.type).toBe("number");
      expect(fallback.value).toBe("42");
      expect(container.querySelector("[role='alert']")?.textContent)
        .toBe("The Count editor failed while validating. Use the standard input instead.");

      submit(container);
      expect(onSubmit).toHaveBeenCalledWith({ Count: 42 });
    } finally {
      consoleError.mockRestore();
    }
  });

  it("falls back to honest JSON entry when a contributed editor component fails", () => {
    const input = workflowInput("payload", "Payload", "Contoso.Order", true);
    const editor = editorContribution("broken-component", 100, candidate => candidate.type.alias === "Contoso.Order");
    editor.component = () => { throw new Error("render failed"); };
    const onSubmit = vi.fn();
    const consoleError = vi.spyOn(console, "error").mockImplementation(() => undefined);

    try {
      const container = render(
        <WorkflowRunInputDialog inputs={[input]} editors={[editor]} onSubmit={onSubmit} onCancel={vi.fn()} />
      );

      const fallback = container.querySelector<HTMLTextAreaElement>("textarea[aria-label='Payload']")!;
      expect(fallback.placeholder).toBe("Enter JSON");
      expect(container.textContent).toContain("The Payload editor failed. Enter a JSON value instead.");
      fill(fallback, '{"id":42}');
      submit(container);
      expect(onSubmit).toHaveBeenCalledWith({ Payload: { id: 42 } });
    } finally {
      consoleError.mockRestore();
    }
  });

  it("renders declared enum values as a keyboard-focusable picker and serializes the selected wire value", () => {
    const input = workflowInput("status", "Status", "Contoso.OrderStatus", true);
    const enumEditor = createEnumWorkflowRunInputEditorContribution({
      id: "contoso.order-status",
      supports: candidate => candidate.type.alias === "Contoso.OrderStatus",
      options: [
        { value: "pending", label: "Pending", wireValue: 1 },
        { value: "approved", label: "Approved", wireValue: 2 }
      ]
    });
    const onSubmit = vi.fn();
    const container = render(
      <WorkflowRunInputDialog
        inputs={[input]}
        editors={[enumEditor]}
        onSubmit={onSubmit}
        onCancel={vi.fn()}
      />
    );

    const picker = container.querySelector<HTMLSelectElement>("select[aria-label='Status']")!;
    expect(picker).not.toBeNull();
    expect([...picker.options].map(option => [option.value, option.textContent])).toEqual([
      ["", "Not set"],
      ["pending", "Pending"],
      ["approved", "Approved"]
    ]);
    picker.focus();
    expect(document.activeElement).toBe(picker);

    fill(picker, "approved");
    submit(container);
    expect(onSubmit).toHaveBeenCalledWith({ Status: 2 });

    expect(parseWorkflowRunInputs([input], { status: "retired" }, [enumEditor])).toEqual({
      values: {},
      errors: { status: "Choose an available Status value." }
    });
  });

  it("keeps an honest JSON fallback when no contribution supports a custom declared type", () => {
    const input = workflowInput("payload", "Payload", "Contoso.Order", true);
    const enumEditor = createEnumWorkflowRunInputEditorContribution({
      id: "contoso.order-status",
      supports: candidate => candidate.type.alias === "Contoso.OrderStatus",
      options: [{ value: "pending", label: "Pending" }]
    });
    const onSubmit = vi.fn();
    const container = render(
      <WorkflowRunInputDialog
        inputs={[input]}
        editors={[enumEditor]}
        onSubmit={onSubmit}
        onCancel={vi.fn()}
      />
    );

    const fallback = container.querySelector<HTMLTextAreaElement>("textarea[aria-label='Payload']")!;
    expect(fallback.placeholder).toBe("Enter JSON");
    fill(fallback, "not-json");
    submit(container);
    expect(container.textContent).toContain("Enter valid JSON.");

    fill(fallback, '{"id":42}');
    submit(container);
    expect(onSubmit).toHaveBeenCalledWith({ Payload: { id: 42 } });
  });

  it("blocks a missing required value and serializes valid scalar values by input name", () => {
    const inputs = [
      workflowInput("greeting", "Greeting", "String", true),
      workflowInput("attempts", "Attempts", "Int32", false)
    ];

    expect(parseWorkflowRunInputs(inputs, {})).toEqual({
      values: {},
      errors: { greeting: "Greeting is required." }
    });

    expect(parseWorkflowRunInputs(inputs, { greeting: "Hello", attempts: "3" })).toEqual({
      values: { Greeting: "Hello", Attempts: 3 },
      errors: {}
    });
  });

  it("validates booleans, declared complex values, and every collection kind", () => {
    const inputs = [
      workflowInput("enabled", "Enabled", "Boolean", true),
      workflowInput("payload", "Payload", "Contoso.Order", false),
      workflowInput("array", "Array", "Int32", false, "Array"),
      workflowInput("list", "List", "String", false, "List"),
      workflowInput("set", "Set", "Boolean", false, "HashSet")
    ];

    expect(parseWorkflowRunInputs(inputs, {
      enabled: "yes",
      payload: "not json",
      array: "[1, \"two\"]",
      list: "{}",
      set: "[true, false]"
    }).errors).toEqual({
      enabled: "Choose true or false.",
      payload: "Enter valid JSON.",
      array: "Item 2 must be a whole number.",
      list: "Enter a JSON array.",
    });

    expect(parseWorkflowRunInputs(inputs, {
      enabled: "false",
      payload: "{\"id\":42}",
      array: "[1, 2]",
      list: "[\"one\", \"two\"]",
      set: "[true, false]"
    })).toEqual({
      values: {
        Enabled: false,
        Payload: { id: 42 },
        Array: [1, 2],
        List: ["one", "two"],
        Set: [true, false]
      },
      errors: {}
    });
  });

  it("omits blank optional values and reports declared scalar type errors", () => {
    const inputs = [
      workflowInput("optional", "Optional", "String", false),
      workflowInput("whole", "Whole", "Int32", false),
      workflowInput("when", "When", "DateTime", false),
      workflowInput("id", "ID", "Guid", false)
    ];

    expect(parseWorkflowRunInputs(inputs, {
      optional: "",
      whole: "3.5",
      when: "yesterday-ish",
      id: "not-a-guid"
    })).toEqual({
      values: {},
      errors: {
        whole: "Enter a whole number.",
        when: "Enter a valid date and time.",
        id: "Enter a valid GUID."
      }
    });
  });

  it("does not treat JSON null as a supplied required complex value", () => {
    expect(parseWorkflowRunInputs(
      [workflowInput("payload", "Payload", "Contoso.Order", true)],
      { payload: "null" }
    )).toEqual({ values: {}, errors: { payload: "Payload is required." } });
  });

  it("preserves the exact CLR Int64 and UInt64 boundaries as JSON numeric tokens", () => {
    const inputs = [
      workflowInput("minimum", "Minimum", "System.Int64", true),
      workflowInput("maximum", "Maximum", "Int64", true),
      workflowInput("unsigned", "Unsigned", "UInt64", true)
    ];

    const parsed = parseWorkflowRunInputs(inputs, {
      minimum: "-9223372036854775808",
      maximum: "9223372036854775807",
      unsigned: "18446744073709551615"
    });

    expect(parsed.errors).toEqual({});
    expect(serializeWorkflowExecutionPayload({ inputs: parsed.values })).toBe(
      '{"inputs":{"Minimum":-9223372036854775808,"Maximum":9223372036854775807,"Unsigned":18446744073709551615}}'
    );
  });

  it("rejects malformed, fractional, signed-unsigned, and overflowing 64-bit integers clearly", () => {
    const inputs = [
      workflowInput("fractional", "Fractional", "Int64", false),
      workflowInput("malformed", "Malformed", "Int64", false),
      workflowInput("signedOverflow", "Signed overflow", "Int64", false),
      workflowInput("negativeUnsigned", "Negative unsigned", "UInt64", false),
      workflowInput("unsignedOverflow", "Unsigned overflow", "System.UInt64", false)
    ];

    expect(parseWorkflowRunInputs(inputs, {
      fractional: "1.5",
      malformed: "12oops",
      signedOverflow: "9223372036854775808",
      negativeUnsigned: "-1",
      unsignedOverflow: "18446744073709551616"
    }).errors).toEqual({
      fractional: "Enter a whole number.",
      malformed: "Enter a whole number.",
      signedOverflow: "Enter an integer from -9223372036854775808 to 9223372036854775807.",
      negativeUnsigned: "Enter an integer from 0 to 18446744073709551615.",
      unsignedOverflow: "Enter an integer from 0 to 18446744073709551615."
    });
  });

  it("preserves exact 64-bit integers in typed input collections", () => {
    const parsed = parseWorkflowRunInputs(
      [workflowInput("ids", "IDs", "UInt64", true, "Array")],
      { ids: "[0, 9007199254740993, 18446744073709551615]" }
    );

    expect(parsed.errors).toEqual({});
    expect(serializeWorkflowExecutionPayload({ inputs: parsed.values })).toBe(
      '{"inputs":{"IDs":[0,9007199254740993,18446744073709551615]}}'
    );
  });

  it("reports the exact invalid item in a 64-bit integer collection", () => {
    const parsed = parseWorkflowRunInputs(
      [workflowInput("ids", "IDs", "UInt64", true, "Array")],
      { ids: "[1, -1, 18446744073709551616]" }
    );

    expect(parsed).toEqual({
      values: {},
      errors: { ids: "Item 2: Enter an integer from 0 to 18446744073709551615." }
    });
  });

  it("preserves CLR Decimal boundaries, precision, and scale as JSON numeric tokens", () => {
    const parsed = parseWorkflowRunInputs([
      workflowInput("minimum", "Minimum", "System.Decimal", true),
      workflowInput("maximum", "Maximum", "Decimal", true),
      workflowInput("precise", "Precise", "Decimal", true),
      workflowInput("scaled", "Scaled", "Decimal", true)
    ], {
      minimum: "-79228162514264337593543950335",
      maximum: "79228162514264337593543950335",
      precise: "0.1234567890123456789012345678",
      scaled: "1.2300"
    });

    expect(parsed.errors).toEqual({});
    expect(serializeWorkflowExecutionPayload({ inputs: parsed.values })).toBe(
      '{"inputs":{"Minimum":-79228162514264337593543950335,"Maximum":79228162514264337593543950335,"Precise":0.1234567890123456789012345678,"Scaled":1.2300}}'
    );
  });

  it("preserves exact CLR Decimal tokens in typed input collections", () => {
    const parsed = parseWorkflowRunInputs(
      [workflowInput("amounts", "Amounts", "Decimal", true, "Array")],
      { amounts: "[-79228162514264337593543950335, 0.1234567890123456789012345678, 1.2300]" }
    );

    expect(parsed.errors).toEqual({});
    expect(serializeWorkflowExecutionPayload({ inputs: parsed.values })).toBe(
      '{"inputs":{"Amounts":[-79228162514264337593543950335,0.1234567890123456789012345678,1.2300]}}'
    );
  });

  it("rejects malformed, exponent, over-scale, and overflowing CLR Decimal values clearly", () => {
    const parsed = parseWorkflowRunInputs([
      workflowInput("malformed", "Malformed", "Decimal", false),
      workflowInput("exponent", "Exponent", "Decimal", false),
      workflowInput("scale", "Scale", "Decimal", false),
      workflowInput("overflow", "Overflow", "Decimal", false),
      workflowInput("amounts", "Amounts", "Decimal", false, "Array")
    ], {
      malformed: "12oops",
      exponent: "1e2",
      scale: "0.12345678901234567890123456789",
      overflow: "79228162514264337593543950336",
      amounts: "[1.0, 1e2]"
    });

    expect(parsed.errors).toEqual({
      malformed: "Enter a decimal number without exponent notation.",
      exponent: "Enter a decimal number without exponent notation.",
      scale: "Enter a decimal from -79228162514264337593543950335 to 79228162514264337593543950335 with up to 28 decimal places.",
      overflow: "Enter a decimal from -79228162514264337593543950335 to 79228162514264337593543950335 with up to 28 decimal places.",
      amounts: "Item 2: Enter a decimal number without exponent notation."
    });
  });

  it("preserves exact integer tokens when JSON.rawJSON is unavailable", () => {
    const descriptor = Object.getOwnPropertyDescriptor(JSON, "rawJSON");
    Object.defineProperty(JSON, "rawJSON", { configurable: true, value: undefined });
    try {
      const parsed = parseWorkflowRunInputs(
        [workflowInput("id", "ID", "UInt64", true)],
        { id: "18446744073709551615" }
      );

      expect(serializeWorkflowExecutionPayload({ inputs: parsed.values })).toBe(
        '{"inputs":{"ID":18446744073709551615}}'
      );
    } finally {
      if (descriptor) Object.defineProperty(JSON, "rawJSON", descriptor);
      else Reflect.deleteProperty(JSON, "rawJSON");
    }
  });

  it("keeps prototype-shaped input names as ordinary own payload properties", () => {
    const parsed = parseWorkflowRunInputs([
      workflowInput("proto-ref", "__proto__", "String", true),
      workflowInput("constructor-ref", "constructor", "String", true),
      workflowInput("prototype-ref", "prototype", "String", true),
      workflowInput("collection-ref", "__proto__-collection", "String", true, "Array")
    ], Object.fromEntries([
      ["proto-ref", "safe"],
      ["constructor-ref", "also safe"],
      ["prototype-ref", "still safe"],
      ["collection-ref", '["one", "two"]']
    ]));

    expect(parsed.errors).toEqual({});
    expect(Object.prototype.hasOwnProperty.call(parsed.values, "__proto__")).toBe(true);
    expect(Object.getPrototypeOf(parsed.values)).toBe(Object.prototype);
    expect(Object.keys(parsed.values)).toEqual(["__proto__", "constructor", "prototype", "__proto__-collection"]);
    expect(serializeWorkflowExecutionPayload({ inputs: parsed.values })).toBe(
      '{"inputs":{"__proto__":"safe","constructor":"also safe","prototype":"still safe","__proto__-collection":["one","two"]}}'
    );
    expect(({} as Record<string, unknown>).polluted).toBeUndefined();
  });

  it("reports errors under a prototype-shaped reference key without mutating the error map", () => {
    const parsed = parseWorkflowRunInputs(
      [workflowInput("__proto__", "Count", "Int32", true)],
      Object.fromEntries([["__proto__", "not-an-integer"]])
    );

    expect(Object.prototype.hasOwnProperty.call(parsed.errors, "__proto__")).toBe(true);
    expect(parsed.errors["__proto__"]).toBe("Enter a whole number.");
    expect(Object.getPrototypeOf(parsed.errors)).toBe(Object.prototype);
  });

  it("does not mistake inherited prototype members for supplied dangerous-key drafts", () => {
    const parsed = parseWorkflowRunInputs([
      workflowInput("__proto__", "Proto", "String", false),
      workflowInput("constructor", "Constructor", "String", false),
      workflowInput("prototype", "Prototype", "String", false, "Array")
    ], {});

    expect(parsed).toEqual({ values: {}, errors: {} });
  });

  it("keeps required and type errors in the dialog until the form is valid", () => {
    const onSubmit = vi.fn();
    const container = render(
      <WorkflowRunInputDialog
        inputs={[
          workflowInput("greeting", "Greeting", "String", true),
          workflowInput("attempts", "Attempts", "Int32", false)
        ]}
        onSubmit={onSubmit}
        onCancel={vi.fn()}
      />
    );

    submit(container);
    expect(container.textContent).toContain("Greeting is required.");
    expect(onSubmit).not.toHaveBeenCalled();

    fill(container.querySelector("input[aria-label='Greeting']"), "Hello");
    fill(container.querySelector("input[aria-label='Attempts']"), "3.5");
    submit(container);
    expect(container.textContent).toContain("Enter a whole number.");
    expect(onSubmit).not.toHaveBeenCalled();

    fill(container.querySelector("input[aria-label='Attempts']"), "3");
    submit(container);
    expect(onSubmit).toHaveBeenCalledWith({ Greeting: "Hello", Attempts: 3 });
  });

  it("cancels without submitting input values", () => {
    const onSubmit = vi.fn();
    const onCancel = vi.fn();
    const container = render(
      <WorkflowRunInputDialog
        inputs={[workflowInput("greeting", "Greeting", "String", true)]}
        onSubmit={onSubmit}
        onCancel={onCancel}
      />
    );

    fill(container.querySelector("input[aria-label='Greeting']"), "Unsaved");
    button(container, "Cancel").click();

    expect(onCancel).toHaveBeenCalledOnce();
    expect(onSubmit).not.toHaveBeenCalled();
  });

  it("renders collection and boolean controls and omits an untouched optional input", () => {
    const onSubmit = vi.fn();
    const container = render(
      <WorkflowRunInputDialog
        inputs={[
          workflowInput("enabled", "Enabled", "Boolean", true),
          workflowInput("tags", "Tags", "String", true, "HashSet"),
          workflowInput("note", "Note", "String", false)
        ]}
        onSubmit={onSubmit}
        onCancel={vi.fn()}
      />
    );

    fill(container.querySelector("select[aria-label='Enabled']"), "false");
    fill(container.querySelector("textarea[aria-label='Tags']"), "not-json");
    submit(container);
    expect(container.textContent).toContain("Enter a valid JSON array.");

    fill(container.querySelector("textarea[aria-label='Tags']"), "[\"qa\", \"studio\"]");
    submit(container);
    expect(onSubmit).toHaveBeenCalledWith({ Enabled: false, Tags: ["qa", "studio"] });
  });

  it("focuses the first field, traps focus, and supports Escape cancellation", () => {
    const onCancel = vi.fn();
    const container = render(
      <WorkflowRunInputDialog
        inputs={[workflowInput("greeting", "Greeting", "String", true)]}
        onSubmit={vi.fn()}
        onCancel={onCancel}
      />
    );
    const first = container.querySelector<HTMLInputElement>("input[aria-label='Greeting']")!;
    const submitButton = button(container, "Run workflow");

    expect(document.activeElement).toBe(first);
    submitButton.focus();
    submitButton.dispatchEvent(new KeyboardEvent("keydown", { key: "Tab", bubbles: true, cancelable: true }));
    expect(document.activeElement).toBe(first);
    first.dispatchEvent(new KeyboardEvent("keydown", { key: "Escape", bubbles: true }));
    expect(onCancel).toHaveBeenCalledOnce();
  });
});

function render(ui: React.ReactElement) {
  const container = document.createElement("div");
  document.body.appendChild(container);
  const root = createRoot(container);
  flushSync(() => root.render(ui));
  active = { root, container };
  return container;
}

function fill(element: HTMLInputElement | HTMLTextAreaElement | HTMLSelectElement | null, value: string) {
  if (!element) throw new Error("Input not found");
  const prototype = element instanceof HTMLTextAreaElement
    ? HTMLTextAreaElement.prototype
    : element instanceof HTMLSelectElement
      ? HTMLSelectElement.prototype
      : HTMLInputElement.prototype;
  Object.getOwnPropertyDescriptor(prototype, "value")?.set?.call(element, value);
  flushSync(() => element.dispatchEvent(new Event(element instanceof HTMLSelectElement ? "change" : "input", { bubbles: true })));
}

function submit(container: HTMLElement) {
  flushSync(() => container.querySelector("form")!.dispatchEvent(new Event("submit", { bubbles: true, cancelable: true })));
}

function button(container: HTMLElement, label: string) {
  const result = [...container.querySelectorAll("button")].find(candidate => candidate.textContent?.trim() === label);
  if (!result) throw new Error(`Button ${label} not found`);
  return result;
}

function workflowInput(
  referenceKey: string,
  name: string,
  alias: string,
  isRequired: boolean,
  collectionKind: WorkflowInput["type"]["collectionKind"] = "Single"
): WorkflowInput {
  return {
    referenceKey,
    name,
    displayName: name,
    description: "",
    category: "",
    uiHint: "",
    storageDriverType: null,
    type: { alias, collectionKind },
    isRequired
  };
}

function editorContribution(
  id: string,
  order: number,
  supports: StudioWorkflowRunInputEditorContribution["supports"]
): StudioWorkflowRunInputEditorContribution {
  return {
    id,
    order,
    supports,
    component: () => null,
    validate: () => undefined,
    serialize: ({ draft }) => draft
  };
}
