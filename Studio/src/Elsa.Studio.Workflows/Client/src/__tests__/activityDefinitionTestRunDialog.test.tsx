import React from "react";
import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { afterEach, describe, expect, it, vi } from "vitest";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { ActivityDefinitionTestRunDialog, type PreparedActivityTestRunRevision } from "../ActivityDefinitionTestRunDialog";
import type { ActivityDefinitionDraftView } from "../activityDefinitionTypes";

afterEach(() => {
  document.body.replaceChildren();
});

describe("ActivityDefinitionTestRunDialog", () => {
  it("prepares the exact revision once under React Strict Mode", async () => {
    const prepared: PreparedActivityTestRunRevision = {
      revision: 5,
      validation: {
        draftId: "activity-draft-1",
        revision: 5,
        isValid: true,
        validatedAt: "2026-07-20T00:00:00Z",
        diagnostics: []
      }
    };
    const prepareExactRevision = vi.fn(async (onPhase: (phase: "saving" | "validating") => void) => {
      onPhase("validating");
      return prepared;
    });
    const container = document.createElement("div");
    document.body.appendChild(container);
    const root = createRoot(container);

    flushSync(() => root.render(
      <React.StrictMode>
        <ActivityDefinitionTestRunDialog
          context={{} as StudioEndpointContext}
          draft={draft()}
          definitionLabel="Greeting"
          inputEditors={[]}
          prepareExactRevision={prepareExactRevision}
          onFocusDiagnostic={vi.fn()}
          onClose={vi.fn()}
          onOpenRun={vi.fn()}
        />
      </React.StrictMode>
    ));

    await waitFor(() => expect(container.textContent).toContain("Revision 5 validated"));
    expect(prepareExactRevision).toHaveBeenCalledTimes(1);

    flushSync(() => root.unmount());
  });
});

function draft(): ActivityDefinitionDraftView {
  return {
    draftId: "activity-draft-1",
    definitionId: "activity-def-1",
    tenantId: "default",
    revision: 5,
    sourceVersionId: null,
    status: "active",
    contract: {
      contractSchemaVersion: "1",
      inputs: [],
      outputs: [],
      outcomes: []
    },
    provider: {
      providerKey: "elsa.activity-graph",
      schemaVersion: "1",
      manifestFingerprint: "sha256:test",
      payload: {}
    },
    layout: [],
    validation: null,
    presentationLabel: null,
    createdAt: "2026-07-20T00:00:00Z",
    updatedAt: "2026-07-20T00:00:00Z"
  };
}

async function waitFor(assertion: () => void) {
  const deadline = Date.now() + 2_000;
  while (true) {
    try {
      assertion();
      return;
    } catch (error) {
      if (Date.now() >= deadline) throw error;
      await new Promise(resolve => setTimeout(resolve, 10));
    }
  }
}
