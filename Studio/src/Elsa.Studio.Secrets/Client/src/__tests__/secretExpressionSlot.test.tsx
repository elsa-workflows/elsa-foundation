import React from "react";
import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { afterEach, describe, expect, it, vi } from "vitest";
import type { StudioEndpointContext, StudioExpressionEditorContribution } from "@elsa-workflows/studio-sdk";
import { ActivityPropertiesPanel } from "../../../../Elsa.Studio.Workflows/Client/src/ActivityPropertiesPanel";
import type { ActivityNode } from "../../../../Elsa.Studio.Workflows/Client/src/workflowTypes";
import { SecretPickerEditor } from "../SecretPickerEditor";

let dispose: (() => void) | null = null;

afterEach(() => {
  dispose?.();
  dispose = null;
  vi.unstubAllGlobals();
});

describe("Secret Reference Activity Property Editor Slot", () => {
  it("keeps the requested focus through deferred metadata loading", async () => {
    vi.stubGlobal("ResizeObserver", class {
      observe() {}
      disconnect() {}
    });
    const request = deferred<unknown>();
    const endpoint = endpointContext(vi.fn(() => request.promise));
    const contribution: StudioExpressionEditorContribution = {
      id: "elsa.secret-reference-editor",
      supports: context => context.syntax === "Secret",
      surfaces: {
        inline: props => <SecretPickerEditor {...props} endpointContext={endpoint} />
      },
      createDefaultValue: () => null
    };
    const container = mountPanel(endpoint, contribution);

    flushSync(() => container.querySelector<HTMLButtonElement>(".wf-syntax-picker-trigger")?.click());
    const option = [...document.querySelectorAll<HTMLButtonElement>("[role='option']")]
      .find(candidate => candidate.textContent === "Secret");
    flushSync(() => option?.click());
    await nextFrame();
    expect(container.querySelector("select")?.disabled).toBe(true);

    request.resolve(pickerResponse());
    await waitFor(() => document.activeElement === container.querySelector("select"));
  });
});

function mountPanel(endpoint: StudioEndpointContext, contribution: StudioExpressionEditorContribution) {
  const host = document.createElement("div");
  document.body.appendChild(host);
  const root = createRoot(host);

  function Harness() {
    const [activity, setActivity] = React.useState<ActivityNode>({
      nodeId: "node-1",
      activityVersionId: "activity-1",
      inputs: [],
      outputs: [],
      credential: {
        typeName: "System.String",
        expression: { type: "Literal", value: "" }
      }
    });
    return (
      <ActivityPropertiesPanel
        context={endpoint}
        activity={activity}
        descriptor={{
          typeName: "TestActivity",
          inputs: [{ name: "Credential", displayName: "Credential", typeName: "System.String", isWrapped: true }],
          outputs: [],
          ports: []
        }}
        editors={[]}
        expressionEditors={[contribution]}
        expressionDescriptors={[
          { type: "Literal", displayName: "Literal", editingMode: "literal" },
          { type: "Secret", displayName: "Secret", editingMode: "reference" }
        ]}
        expressionDescriptorStatus="ready"
        descriptorStatus="ready"
        visibleVariables={[]}
        scopeStatus="ready"
        onChange={setActivity}
      />
    );
  }

  flushSync(() => root.render(<Harness />));
  dispose = () => {
    flushSync(() => root.unmount());
    host.remove();
  };
  return host;
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
    items: [{
      id: "secret-1",
      name: "payments.api",
      displayName: "Payments API",
      typeName: "text",
      storeName: "encrypted",
      scope: "tenant-a",
      status: "Active"
    }],
    canCreateInline: false
  };
}

function deferred<T>() {
  let resolve!: (value: T) => void;
  const promise = new Promise<T>(resolvePromise => {
    resolve = resolvePromise;
  });
  return { promise, resolve };
}

async function nextFrame() {
  await new Promise<void>(resolve => requestAnimationFrame(() => resolve()));
}

async function waitFor(predicate: () => boolean) {
  for (let attempt = 0; attempt < 20; attempt++) {
    if (predicate()) return;
    await new Promise(resolve => setTimeout(resolve, 0));
  }
  throw new Error("Timed out waiting for predicate.");
}
