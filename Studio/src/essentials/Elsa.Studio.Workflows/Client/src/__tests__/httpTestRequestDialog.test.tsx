import React from "react";
import { flushSync } from "react-dom";
import { afterEach, describe, expect, it, vi } from "vitest";
import { createRoot, type Root } from "react-dom/client";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { capabilityIds, clearApiCapabilityCache } from "../api/capabilities";
import { getExecutable, getExecutableInputSources } from "../api/runtime";
import type { WorkflowExecutableDetails, WorkflowExecutableInputSources } from "../workflowTypes";
import { HttpTestRequestDialog } from "../workflow-editor/HttpTestRequestDialog";
import { workflowHttpBasePathCapabilityRelation, workflowHttpBasePathUnavailableMessage } from "../workflow-editor/httpTestRequest";

vi.mock("../api/runtime", () => ({ getExecutable: vi.fn(), getExecutableInputSources: vi.fn() }));

let mounted: { root: Root; container: HTMLElement } | null = null;
const requestResponse = vi.fn<NonNullable<StudioEndpointContext["http"]["requestResponse"]>>();
const getJson = vi.fn();
const context = {
  baseUrl: "https://backend.example/",
  http: { requestResponse, getJson }
} as unknown as StudioEndpointContext;

afterEach(async () => {
  if (mounted) {
    flushSync(() => mounted!.root.unmount());
    mounted.container.remove();
    mounted = null;
  }
  vi.mocked(getExecutable).mockReset();
  vi.mocked(getExecutableInputSources).mockReset();
  requestResponse.mockReset();
  getJson.mockReset();
  clearApiCapabilityCache();
});

describe("HTTP test request dialog", () => {
  it("sends only after an explicit click and displays the raw 202 status, content type, and body", async () => {
    advertiseWorkflowHttpBasePath();
    vi.mocked(getExecutable).mockResolvedValue(publishedExecutable());
    vi.mocked(getExecutableInputSources).mockResolvedValue(publishedEndpointInputs());
    requestResponse.mockResolvedValue(new Response(JSON.stringify({
      started: ["execution-1"],
      resumed: [],
      response: {
        mode: "async",
        reason: "background-accepted",
        message: "The request was accepted."
      }
    }), { status: 202, statusText: "Accepted", headers: { "content-type": "application/json" } }));

    const container = await renderDialog();
    await waitFor(() => expect(container.querySelector("textarea[aria-label='Request body']")).not.toBeNull());
    expect(requestResponse).not.toHaveBeenCalled();

    const mount = container.querySelector<HTMLInputElement>("input[aria-label='Workflow HTTP mount']");
    expect(mount).not.toBeNull();
    expect(mount!.value).toBe("/workflows/http");
    expect(mount!.readOnly).toBe(true);
    expect(mount!.disabled).toBe(false);
    const path = container.querySelector<HTMLInputElement>("input[aria-label='Endpoint path']")!;
    expect(path.value).toBe("/orders/1");
    const body = container.querySelector<HTMLTextAreaElement>("textarea[aria-label='Request body']")!;
    expect(body.value).toContain("Ada Lovelace");
    expect(body.disabled).toBe(false);

    await click(container.querySelector<HTMLButtonElement>("button[type='submit']")!);
    await waitFor(() => expect(container.textContent).toContain("202 Accepted"));

    expect(requestResponse).toHaveBeenCalledTimes(1);
    const [requestPath, init] = requestResponse.mock.calls[0];
    expect(requestPath).toBe("/workflows/http/orders/1");
    expect(init).toEqual(expect.objectContaining({
      method: "POST",
      body: expect.stringContaining("Ada Lovelace"),
      cache: "no-store",
      retryOnUnauthorized: false,
      timeoutMs: 36_000,
      redirect: "manual"
    }));
    expect(new Headers(init?.headers).get("Content-Type")).toBe("application/json");
    expect(container.textContent).toContain("202 Accepted");
    expect(container.textContent).toContain("Content type");
    expect(container.textContent).toContain("application/json");
    expect(container.textContent).toContain("The request was accepted.");
    expect(container.textContent).toContain("execution-1");
    expect(getExecutableInputSources).toHaveBeenCalledWith(context, "artifact-1", "reference-1");
  });

  it("offers common content types and sends a custom type unchanged", async () => {
    advertiseWorkflowHttpBasePath();
    vi.mocked(getExecutable).mockResolvedValue(publishedExecutable());
    vi.mocked(getExecutableInputSources).mockResolvedValue(publishedEndpointInputs());
    requestResponse.mockResolvedValue(new Response("ok", { status: 200 }));
    const container = await renderDialog();
    await waitFor(() => expect(container.querySelector("input[role='combobox']")).not.toBeNull());
    const input = container.querySelector<HTMLInputElement>("input[role='combobox']")!;
    flushSync(() => input.focus());
    expect(container.querySelector("[role='listbox']")?.textContent).toContain("text/plain");
    await click([...container.querySelectorAll<HTMLElement>("[role='option']")].find(option => option.textContent === "text/plain")!);
    expect(input.value).toBe("text/plain");
    expect(requestResponse).not.toHaveBeenCalled();
    await changeInput(input, "application/vnd.acme.order+json; charset=utf-8");
    expect(input.value).toBe("application/vnd.acme.order+json; charset=utf-8");
    expect(container.querySelector("[role='listbox']")).toBeNull();
    await click(container.querySelector<HTMLButtonElement>("button[type='submit']")!);
    await waitFor(() => expect(requestResponse).toHaveBeenCalledOnce());
    expect(new Headers(requestResponse.mock.calls[0][1]?.headers).get("Content-Type"))
      .toBe("application/vnd.acme.order+json; charset=utf-8");
  });

  it("closes content type suggestions before closing the dialog with Escape", async () => {
    advertiseWorkflowHttpBasePath();
    vi.mocked(getExecutable).mockResolvedValue(publishedExecutable());
    vi.mocked(getExecutableInputSources).mockResolvedValue(publishedEndpointInputs());
    const onClose = vi.fn();
    const container = await renderDialog(onClose);
    await waitFor(() => expect(container.querySelector("input[role='combobox']")).not.toBeNull());
    const input = container.querySelector<HTMLInputElement>("input[role='combobox']")!;
    flushSync(() => input.focus());
    expect(input.getAttribute("aria-expanded")).toBe("true");
    flushSync(() => input.dispatchEvent(new KeyboardEvent("keydown", { key: "Escape", bubbles: true })));
    expect(input.getAttribute("aria-expanded")).toBe("false");
    expect(onClose).not.toHaveBeenCalled();
    flushSync(() => input.dispatchEvent(new KeyboardEvent("keydown", { key: "Escape", bubbles: true })));
    expect(onClose).toHaveBeenCalledOnce();
  });

  it("preserves an error response body instead of turning non-2xx status into a client exception", async () => {
    advertiseWorkflowHttpBasePath();
    vi.mocked(getExecutable).mockResolvedValue(publishedExecutable());
    vi.mocked(getExecutableInputSources).mockResolvedValue(publishedEndpointInputs());
    requestResponse.mockResolvedValue(new Response("route not found", {
      status: 404,
      statusText: "Not Found",
      headers: { "content-type": "text/plain" }
    }));

    const container = await renderDialog();
    await waitFor(() => expect(container.querySelector("textarea[aria-label='Request body']")).not.toBeNull());
    await click(container.querySelector<HTMLButtonElement>("button[type='submit']")!);
    await waitFor(() => expect(container.textContent).toContain("404 Not Found"));

    expect(container.textContent).toContain("404 Not Found");
    expect(container.textContent).toContain("text/plain");
    expect(container.textContent).toContain("route not found");
  });

  it("blocks duplicate sends and aborts the active request when the dialog closes", async () => {
    advertiseWorkflowHttpBasePath();
    vi.mocked(getExecutable).mockResolvedValue(publishedExecutable());
    vi.mocked(getExecutableInputSources).mockResolvedValue(publishedEndpointInputs());
    let resolveResponse!: (response: Response) => void;
    let requestSignal: AbortSignal | undefined;
    requestResponse.mockImplementation((_url, init) => {
      requestSignal = init?.signal as AbortSignal | undefined;
      return new Promise(resolve => { resolveResponse = resolve; });
    });
    const onClose = vi.fn();

    const container = await renderDialog(onClose);
    await waitFor(() => expect(container.querySelector("textarea[aria-label='Request body']")).not.toBeNull());
    const send = container.querySelector<HTMLButtonElement>("button[type='submit']")!;
    await click(send);
    await click(send);

    expect(requestResponse).toHaveBeenCalledTimes(1);
    expect(requestSignal?.aborted).toBe(false);
    expect(container.querySelector<HTMLInputElement>("input[role='combobox']")!.disabled).toBe(true);
    expect(container.querySelector("[role='listbox']")).toBeNull();
    await click(container.querySelector<HTMLButtonElement>("button[aria-label='Close test request']")!);
    expect(onClose).toHaveBeenCalledTimes(1);
    expect(requestSignal?.aborted).toBe(true);

    resolveResponse(new Response("late response", { status: 200 }));
    await new Promise(resolve => setTimeout(resolve, 0));
    expect(container.textContent).not.toContain("late response");
  });

  it("explains when published input settings are unavailable and does not send", async () => {
    advertiseWorkflowHttpBasePath();
    vi.mocked(getExecutable).mockResolvedValue(publishedExecutable());
    vi.mocked(getExecutableInputSources).mockRejectedValue(new Error("forbidden"));

    const container = await renderDialog();
    await waitFor(() => expect(container.textContent).toContain("Check your permission to inspect published workflow inputs"));

    expect(container.querySelector("button[type='submit']")).toBeNull();
    expect(requestResponse).not.toHaveBeenCalled();
  });

  it("keeps the advertised custom mount read-only while editing and validating the endpoint route", async () => {
    advertiseWorkflowHttpBasePath("engine/team-a/workflows");
    vi.mocked(getExecutable).mockResolvedValue(publishedExecutable());
    vi.mocked(getExecutableInputSources).mockResolvedValue(publishedEndpointInputs());
    requestResponse.mockResolvedValue(new Response("workflow response", { status: 200 }));

    const container = await renderDialog();
    await waitFor(() => expect(container.querySelector("textarea[aria-label='Request body']")).not.toBeNull());
    const mount = container.querySelector<HTMLInputElement>("input[aria-label='Workflow HTTP mount']")!;
    expect(mount.value).toBe("/engine/team-a/workflows");
    expect(mount.readOnly).toBe(true);
    const path = container.querySelector<HTMLInputElement>("input[aria-label='Endpoint path']")!;
    expect(path.value).toBe("/orders/1");

    await changeInput(path, "../api/admin/users");
    await click(container.querySelector<HTMLButtonElement>("button[type='submit']")!);
    await waitFor(() => expect(container.textContent).toContain("Dot segments are not allowed"));
    expect(requestResponse).not.toHaveBeenCalled();

    await changeInput(path, "/orders/2?customer=Ada");
    await click(container.querySelector<HTMLButtonElement>("button[type='submit']")!);
    await waitFor(() => expect(requestResponse).toHaveBeenCalledOnce());
    expect(requestResponse).toHaveBeenCalledWith(
      "/engine/team-a/workflows/orders/2?customer=Ada",
      expect.objectContaining({ method: "POST" })
    );
  });

  it("does not render a sendable request when the server omits the workflow mount capability", async () => {
    getJson.mockResolvedValue({ capabilities: [] });
    vi.mocked(getExecutable).mockResolvedValue(publishedExecutable());

    const container = await renderDialog();
    await waitFor(() => expect(container.textContent).toContain(workflowHttpBasePathUnavailableMessage));

    expect(container.querySelector("button[type='submit']")).toBeNull();
    expect(requestResponse).not.toHaveBeenCalled();
  });
});

function advertiseWorkflowHttpBasePath(basePath = "workflows/http") {
  getJson.mockResolvedValue({ capabilities: [{
    id: capabilityIds.workflowHttpEndpoints,
    contractVersion: "1",
    links: [{ rel: workflowHttpBasePathCapabilityRelation, href: basePath }]
  }] });
}

async function renderDialog(onClose = () => undefined) {
  const container = document.createElement("div");
  document.body.appendChild(container);
  const root = createRoot(container);
  mounted = { root, container };
  flushSync(() => root.render(
    <HttpTestRequestDialog
      context={context}
      open
      artifactId="artifact-1"
      authoredActivityId="endpoint-1"
      onClose={onClose}
    />
  ));
  return container;
}

async function click(element: HTMLElement) {
  flushSync(() => {
    element.dispatchEvent(new MouseEvent("click", { bubbles: true }));
  });
}

async function changeInput(element: HTMLInputElement, value: string) {
  flushSync(() => {
    const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, "value")?.set;
    setter?.call(element, value);
    element.dispatchEvent(new Event("input", { bubbles: true }));
  });
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

function publishedExecutable(): WorkflowExecutableDetails {
  return {
    artifactId: "artifact-1",
    artifactHash: "hash",
    createdAt: "2026-10-16T00:00:00Z",
    rootActivityType: "Elsa.Sequence",
    rootActivityVersion: "1",
    nodeCount: 1,
    resumeTargetCount: 0,
    chosenReference: { sourceReferenceId: "reference-1", selection: "newest-live", layout: [] },
    references: [],
    rootActivity: {
      executableNodeId: "root",
      authoredActivityId: "root",
      activityType: "Elsa.Sequence",
      activityTypeVersion: "1",
      inputBindings: [],
      childSlots: [{
        name: "Activities",
        activities: [{
          executableNodeId: "endpoint-exec",
          authoredActivityId: "endpoint-1",
          activityType: "Elsa.Activities.Http.Activities.HttpEndpoint",
          activityTypeVersion: "1",
          structureKind: null,
          childSlots: [],
          inputBindings: [
            { inputName: "Path", inputKey: "Path", source: "Literal", literalValue: null },
            { inputName: "SupportedMethods", inputKey: "SupportedMethods", source: "Literal", literalValue: null },
            { inputName: "ResponseMode", inputKey: "ResponseMode", source: "Literal", literalValue: null }
          ]
        }]
      }]
    }
  };
}

function publishedEndpointInputs(): WorkflowExecutableInputSources {
  return {
    artifactId: "artifact-1",
    sourceReferenceId: "reference-1",
    accessState: "allowed",
    authoredInputs: [],
    compiledInputs: [
      { executableNodeId: "endpoint-exec", accessState: "allowed", binding: { inputName: "Path", inputKey: "Path", source: "Literal", literalValue: "orders/{id:int}" } },
      { executableNodeId: "endpoint-exec", accessState: "allowed", binding: { inputName: "SupportedMethods", inputKey: "SupportedMethods", source: "Literal", literalValue: ["POST"] } },
      { executableNodeId: "endpoint-exec", accessState: "allowed", binding: { inputName: "ResponseMode", inputKey: "ResponseMode", source: "Literal", literalValue: 0 } }
    ]
  };
}
