import React from "react";
import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { afterEach, describe, expect, it } from "vitest";
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
    expect(controlByLabel<HTMLTextAreaElement>(container, "Literal JSON value").value).toBe("1");

    click(buttonByText(container, "Replace upstream default"));
    await waitFor(() => expect(controlByLabel<HTMLTextAreaElement>(container, "Literal JSON value").value).toBe("2"));

    change(controlByLabel<HTMLTextAreaElement>(container, "Literal JSON value"), "3");
    click(buttonByText(container, "Reset upstream default"));
    await waitFor(() => expect(controlByLabel<HTMLTextAreaElement>(container, "Literal JSON value").value).toBe("3"));
    expect(controlByLabel<HTMLSelectElement>(container, "Default").value).toBe("literal");
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

function controlByLabel<T extends HTMLTextAreaElement | HTMLSelectElement>(container: HTMLElement, text: string) {
  const label = [...container.querySelectorAll("label")].find(candidate =>
    [...candidate.querySelectorAll(":scope > span")].some(span => span.textContent?.trim() === text)
  );
  const control = label?.querySelector("textarea, select");
  if (!control) throw new Error(`Control labelled '${text}' not found.`);
  return control as T;
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
