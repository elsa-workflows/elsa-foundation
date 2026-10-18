import React from "react";
import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { afterEach, describe, expect, it } from "vitest";
import type { StudioExpressionDescriptor } from "@elsa-workflows/studio-sdk";
import { ActivityDefinitionContractEditor } from "../ActivityDefinitionContractEditor";
import type { ActivityAuthoringCapabilities, ActivityContract } from "../activityDefinitionTypes";

const mountedRoots: Array<{ root: ReturnType<typeof createRoot>; container: HTMLElement }> = [];

afterEach(() => {
  for (const mounted of mountedRoots.splice(0)) {
    flushSync(() => mounted.root.unmount());
    mounted.container.remove();
  }
});

describe("ActivityDefinitionContractEditor", () => {
  it("keeps common contract fields in the basic view and preserves advanced values when edited", () => {
    const container = document.createElement("div");
    document.body.append(container);
    const root = createRoot(container);
    mountedRoots.push({ root, container });
    let committed: ActivityContract | undefined;
    const advancedCapabilities = capabilities();
    advancedCapabilities.storageDriverKeys.push("elsa.memory");
    advancedCapabilities.types[0].compatibleStorageDriverKeys.push("elsa.memory");

    function Harness() {
      const [contract, setContract] = React.useState(() => contractWithDefault("hello"));
      const onChange = (next: ActivityContract) => {
        committed = next;
        setContract(next);
      };
      return <ActivityDefinitionContractEditor
        contract={contract}
        capabilities={advancedCapabilities}
        expressions={[{ type: "JavaScript", displayName: "JavaScript", editingMode: "text" } satisfies StudioExpressionDescriptor]}
        providerRequiredOutcomes={[]}
        sourceVersionId={null}
        baselineUnavailable={false}
        readOnly={false}
        capabilitiesUnavailable={false}
        onChange={onChange}
        onLocalValidityChange={() => undefined}
      />;
    }

    flushSync(() => root.render(<Harness />));
    const input = container.querySelector<HTMLElement>("[data-contract-kind='input']")!;
    const basicFields = input.querySelector<HTMLElement>(":scope > .ad-contract-fields")!;
    const basicLabels = [...basicFields.querySelectorAll(":scope label > span, :scope .ad-readonly-fact > span")].map(label => label.textContent?.trim());
    expect(basicLabels).toEqual(expect.arrayContaining(["Name", "Type", "Collection", "Required", "Allow null", "Default value"]));
    expect(basicLabels).not.toContain("Capability type");
    expect([...basicFields.querySelector<HTMLSelectElement>("[data-contract-field='default']")!.options].map(option => ({ text: option.textContent, value: option.value }))).toEqual([
      { text: "No default", value: "none" },
      { text: "Value (JSON)", value: "literal" },
      { text: "Expression", value: "expression" }
    ]);

    const advanced = input.querySelector<HTMLDetailsElement>(":scope > details.ad-contract-advanced")!;
    expect(advanced.open).toBe(false);
    expect(advanced.querySelector("[data-contract-field='referenceKey']")).toBeTruthy();
    expect(advanced.querySelector("[data-contract-field='storageDriverKey']")).toBeTruthy();
    expect(advanced.querySelector("[data-contract-field='type.alias']")).toBeTruthy();
    expect([...advanced.querySelectorAll("small")].some(copy => copy.textContent?.includes("CLR type names are not stored"))).toBe(true);
    expect(container.textContent).not.toContain("exact-revision draft queue");
    const schemaDetails = container.querySelector<HTMLDetailsElement>("details[data-contract-schema]")!;
    expect(schemaDetails.open).toBe(false);
    expect(schemaDetails.textContent).toContain("Schema version");
    expect(container.querySelector(".ad-badge[data-contract-schema]")).toBeNull();
    expect(basicFields.querySelector<HTMLSelectElement>("[data-contract-field='default']")?.options).toHaveLength(3);

    advanced.open = true;
    changeInput(controlByLabel<HTMLInputElement>(advanced, "Display name"), "Friendly value");
    changeSelect(controlByLabel<HTMLSelectElement>(advanced, "Durable storage driver"), "elsa.memory");
    const keyInput = advanced.querySelector<HTMLInputElement>("[data-contract-field='referenceKey']")!;
    changeInput(keyInput, "custom_reference");
    click(buttonByText(advanced, "Apply key"));

    expect(committed?.inputs[0]).toMatchObject({
      referenceKey: "custom_reference",
      displayName: "Friendly value",
      storageDriverKey: "elsa.memory"
    });
  });

  it("keeps the warning for a stored type missing from the current capability catalog", () => {
    const container = document.createElement("div");
    document.body.append(container);
    const root = createRoot(container);
    mountedRoots.push({ root, container });
    const unavailableCapabilities = capabilities();
    unavailableCapabilities.types = [];

    flushSync(() => root.render(<ActivityDefinitionContractEditor
      contract={contractWithDefault("hello")}
      capabilities={unavailableCapabilities}
      expressions={[]}
      providerRequiredOutcomes={[]}
      sourceVersionId={null}
      baselineUnavailable={false}
      readOnly={false}
      capabilitiesUnavailable={false}
      onChange={() => undefined}
      onLocalValidityChange={() => undefined}
    />));

    expect(container.querySelector("[data-contract-kind='input'] [role='alert']")?.textContent).toContain("Stored type alias String is not an authorable capability selection");
  });

  it("resynchronizes a clean default candidate but preserves an active staged edit across upstream replacement", async () => {
    const container = document.createElement("div");
    document.body.append(container);
    const root = createRoot(container);
    mountedRoots.push({ root, container });

    function Harness() {
      const [contract, setContract] = React.useState(() => contractWithDefault(1));
      const replaceDefault = (value: unknown) => setContract(current => ({
        ...current,
        inputs: [{ ...current.inputs[0], default: value === undefined ? null : { syntax: "Literal", value } }]
      }));
      return <>
        <button type="button" onClick={() => replaceDefault(2)}>Replace upstream default</button>
        <button type="button" onClick={() => replaceDefault(undefined)}>Reset upstream default</button>
        <ActivityDefinitionContractEditor
          contract={contract}
          capabilities={capabilities()}
          expressions={[]}
          providerRequiredOutcomes={[]}
          sourceVersionId={null}
          baselineUnavailable={false}
          readOnly={false}
          capabilitiesUnavailable={false}
          onChange={setContract}
          onLocalValidityChange={() => undefined}
        />
      </>;
    }

    flushSync(() => root.render(<Harness />));
    expect(controlByLabel<HTMLTextAreaElement>(container, "Value (JSON)").value).toBe("1");

    click(buttonByText(container, "Replace upstream default"));
    await waitFor(() => expect(controlByLabel<HTMLTextAreaElement>(container, "Value (JSON)").value).toBe("2"));

    change(controlByLabel<HTMLTextAreaElement>(container, "Value (JSON)"), "3");
    click(buttonByText(container, "Reset upstream default"));
    await waitFor(() => expect(controlByLabel<HTMLTextAreaElement>(container, "Value (JSON)").value).toBe("3"));
    expect(controlByLabel<HTMLSelectElement>(container, "Default value").value).toBe("literal");
  });
});

function contractWithDefault(value: unknown): ActivityContract {
  return {
    contractSchemaVersion: "1",
    inputs: [{
      referenceKey: "value",
      name: "Value",
      type: { alias: "String", collectionKind: "Single" },
      isRequired: false,
      isNullable: true,
      default: { syntax: "Literal", value },
      storageDriverKey: "elsa.json",
      durability: "Required"
    }],
    outputs: [],
    outcomes: []
  };
}

function capabilities(): ActivityAuthoringCapabilities {
  return {
    contractSchemaVersions: ["1"],
    activityTypeKeyRules: {
      serverGenerated: true,
      allowsPreCreationOverride: true,
      immutable: true,
      prefix: "elsa.user",
      pattern: "^elsa\\.user\\.",
      maximumLength: 160,
      collisionScope: "tenantId + activityTypeKey"
    },
    providers: [],
    types: [{
      alias: "String",
      displayName: "Text",
      category: "Primitive",
      defaultEditor: "text",
      supportedCollectionKinds: ["Single"],
      supportsNull: true,
      supportsDurability: true,
      compatibleStorageDriverKeys: ["elsa.json"]
    }],
    storageDriverKeys: ["elsa.json"],
    snapshotFingerprint: "sha256:test"
  };
}

function buttonByText(container: HTMLElement, text: string) {
  const button = [...container.querySelectorAll("button")].find(candidate => candidate.textContent === text);
  if (!button) throw new Error(`Button '${text}' not found.`);
  return button;
}

function click(element: Element) {
  flushSync(() => element.dispatchEvent(new MouseEvent("click", { bubbles: true })));
}

function change(element: HTMLTextAreaElement, value: string) {
  flushSync(() => {
    Object.getOwnPropertyDescriptor(HTMLTextAreaElement.prototype, "value")?.set?.call(element, value);
    element.dispatchEvent(new Event("input", { bubbles: true }));
    element.dispatchEvent(new Event("change", { bubbles: true }));
  });
}

function controlByLabel<T extends HTMLInputElement | HTMLTextAreaElement | HTMLSelectElement>(container: HTMLElement, text: string) {
  const label = [...container.querySelectorAll("label")].find(candidate =>
    [...candidate.querySelectorAll(":scope > span")].some(span => span.textContent?.trim() === text)
  );
  const control = label?.querySelector("input, textarea, select");
  if (!control) throw new Error(`Control labelled '${text}' not found.`);
  return control as T;
}

function changeInput(element: HTMLInputElement, value: string) {
  flushSync(() => {
    Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, "value")?.set?.call(element, value);
    element.dispatchEvent(new Event("input", { bubbles: true }));
    element.dispatchEvent(new Event("change", { bubbles: true }));
  });
}

function changeSelect(element: HTMLSelectElement, value: string) {
  flushSync(() => {
    Object.getOwnPropertyDescriptor(HTMLSelectElement.prototype, "value")?.set?.call(element, value);
    element.dispatchEvent(new Event("input", { bubbles: true }));
    element.dispatchEvent(new Event("change", { bubbles: true }));
  });
}

async function waitFor(assertion: () => void) {
  let lastError: unknown;
  for (let attempt = 0; attempt < 20; attempt += 1) {
    try {
      assertion();
      return;
    } catch (error) {
      lastError = error;
      await new Promise(resolve => setTimeout(resolve, 10));
    }
  }
  throw lastError;
}
