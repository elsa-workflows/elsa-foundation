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

let mounted: { root: Root; container: HTMLElement; onClose: () => void } | null = null;
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
  context.headers = undefined;
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
    expect(container.querySelector("table[aria-label='Response headers']")?.textContent).toContain("content-type");
    expect(container.textContent).toContain("application/json");
    expect(container.textContent).toContain("The request was accepted.");
    expect(container.textContent).toContain("execution-1");
    expect(getExecutableInputSources).toHaveBeenCalledWith(context, "artifact-1", "reference-1");
  });

  it("offers every published method and sends the selected method", async () => {
    advertiseWorkflowHttpBasePath();
    vi.mocked(getExecutable).mockResolvedValue(publishedExecutable());
    const methods = ["GET", "POST", "PUT", "HEAD", "DELETE", "OPTIONS", "PATCH"];
    vi.mocked(getExecutableInputSources).mockResolvedValue(publishedEndpointInputs(methods));
    requestResponse.mockImplementation(async () => new Response("", { status: 200 }));
    const container = await renderDialog();
    await waitFor(() => expect(container.querySelector("select[aria-label='HTTP method']")).not.toBeNull());
    const select = container.querySelector<HTMLSelectElement>("select[aria-label='HTTP method']")!;
    expect([...select.options].map(option => option.value)).toEqual(methods);
    expect(container.textContent).toContain("Publish draft changes before testing them.");
    for (const [index, method] of methods.entries()) {
      flushSync(() => { select.value = method; select.dispatchEvent(new Event("change", { bubbles: true })); });
      await click(container.querySelector("button[type='submit']")!);
      await waitFor(() => expect(requestResponse).toHaveBeenCalledTimes(index + 1));
      expect(requestResponse.mock.calls[index][1]?.method).toBe(method);
      const carriesBody = method !== "GET" && method !== "HEAD";
      expect(requestResponse.mock.calls[index][1]?.body !== undefined).toBe(carriesBody);
      await waitFor(() => expect(container.querySelector<HTMLButtonElement>("button[type='submit']")!.disabled).toBe(false));
    }
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

  it.each(["POST", "GET", "HEAD"])("sends optional request headers with %s", async method => {
    const container = await renderHeaderDialog(["POST", "GET", "HEAD"]);
    await click(container.querySelector<HTMLButtonElement>("button.wf-http-test-add-header")!);
    await changeInput(container.querySelector<HTMLInputElement>("input[aria-label='Header 1 name']")!, "X-Correlation-ID");
    await changeInput(container.querySelector<HTMLInputElement>("input[aria-label='Header 1 value']")!, "test-123");

    const methodSelect = container.querySelector<HTMLSelectElement>("select[aria-label='HTTP method']")!;
    flushSync(() => {
      methodSelect.value = method;
      methodSelect.dispatchEvent(new Event("change", { bubbles: true }));
    });
    await click(container.querySelector<HTMLButtonElement>("button[type='submit']")!);
    await waitFor(() => expect(requestResponse).toHaveBeenCalledOnce());

    const [, init] = requestResponse.mock.calls[0];
    const headers = new Headers(init?.headers);
    expect(headers.get("X-Correlation-ID")).toBe("test-123");
    expect(headers.get("Accept")).toBe("*/*");
    expect(headers.has("Content-Type")).toBe(method === "POST");
    expect(init?.body !== undefined).toBe(method === "POST");
  });

  it("adds and removes request header rows before sending", async () => {
    const container = await renderHeaderDialog();
    const addHeader = container.querySelector<HTMLButtonElement>("button.wf-http-test-add-header")!;
    await click(addHeader);
    await click(addHeader);
    await changeInput(container.querySelector<HTMLInputElement>("input[aria-label='Header 1 name']")!, "X-Remove-Me");
    await changeInput(container.querySelector<HTMLInputElement>("input[aria-label='Header 1 value']")!, "removed");
    await changeInput(container.querySelector<HTMLInputElement>("input[aria-label='Header 2 name']")!, "X-Keep-Me");
    await changeInput(container.querySelector<HTMLInputElement>("input[aria-label='Header 2 value']")!, "kept");
    await click(container.querySelector<HTMLButtonElement>("button[aria-label='Remove header 1']")!);

    await click(container.querySelector<HTMLButtonElement>("button[type='submit']")!);
    await waitFor(() => expect(requestResponse).toHaveBeenCalledOnce());
    const headers = new Headers(requestResponse.mock.calls[0][1]?.headers);
    expect(headers.has("X-Remove-Me")).toBe(false);
    expect(headers.get("X-Keep-Me")).toBe("kept");
  });

  it("shows invalid header names and does not send the request", async () => {
    const container = await renderHeaderDialog();
    await click(container.querySelector<HTMLButtonElement>("button.wf-http-test-add-header")!);
    await changeInput(container.querySelector<HTMLInputElement>("input[aria-label='Header 1 name']")!, "X Invalid");
    await changeInput(container.querySelector<HTMLInputElement>("input[aria-label='Header 1 value']")!, "value");
    await click(container.querySelector<HTMLButtonElement>("button[type='submit']")!);

    await waitFor(() => expect(container.querySelector('[role="alert"]')?.textContent).toContain("Header 1 has an invalid name or value."));
    expect(requestResponse).not.toHaveBeenCalled();
  });

  it("shows invalid header values without exposing them or sending the request", async () => {
    const container = await renderHeaderDialog();
    await click(container.querySelector<HTMLButtonElement>("button.wf-http-test-add-header")!);
    await changeInput(container.querySelector<HTMLInputElement>("input[aria-label='Header 1 name']")!, "X-Note");
    await changeInput(container.querySelector<HTMLInputElement>("input[aria-label='Header 1 value']")!, "secret-😀");
    await click(container.querySelector<HTMLButtonElement>("button[type='submit']")!);

    await waitFor(() => expect(container.querySelector('[role="alert"]')?.textContent)
      .toContain("Header 1 has an invalid name or value."));
    expect(container.querySelector('[role="alert"]')?.textContent).not.toContain("secret-😀");
    expect(requestResponse).not.toHaveBeenCalled();
  });

  it("does not let a custom header override a host-configured value or expose either value", async () => {
    context.headers = { "X-Studio-Scope": "host-secret" };
    const container = await renderHeaderDialog();
    await click(container.querySelector<HTMLButtonElement>("button.wf-http-test-add-header")!);
    await changeInput(container.querySelector<HTMLInputElement>("input[aria-label='Header 1 name']")!, "x-studio-scope");
    await changeInput(container.querySelector<HTMLInputElement>("input[aria-label='Header 1 value']")!, "caller-secret");
    await click(container.querySelector<HTMLButtonElement>("button[type='submit']")!);

    await waitFor(() => expect(container.querySelector('[role="alert"]')?.textContent)
      .toContain("Header 1 is configured by Studio and cannot be overridden."));
    expect(container.querySelector('[role="alert"]')?.textContent).not.toContain("host-secret");
    expect(container.querySelector('[role="alert"]')?.textContent).not.toContain("caller-secret");
    expect(requestResponse).not.toHaveBeenCalled();
  });

  it("does not let the request content type override a host-configured value", async () => {
    context.headers = { "Content-Type": "application/vnd.host+json" };
    const container = await renderHeaderDialog();
    await waitFor(() => expect(container.querySelector("textarea[aria-label='Request body']")).not.toBeNull());
    await click(container.querySelector<HTMLButtonElement>("button[type='submit']")!);

    await waitFor(() => expect(container.querySelector('[role="alert"]')?.textContent)
      .toContain("Request Content-Type is configured by Studio and cannot be overridden."));
    expect(requestResponse).not.toHaveBeenCalled();
  });

  it.each(["Authorization", "Cookie", "Proxy-Authorization"])("rejects the %s header before sending", async name => {
    const container = await renderHeaderDialog();
    await click(container.querySelector<HTMLButtonElement>("button.wf-http-test-add-header")!);
    await changeInput(container.querySelector<HTMLInputElement>("input[aria-label='Header 1 name']")!, name);
    await changeInput(container.querySelector<HTMLInputElement>("input[aria-label='Header 1 value']")!, "caller-value");
    await click(container.querySelector<HTMLButtonElement>("button[type='submit']")!);

    await waitFor(() => expect(container.querySelector('[role="alert"]')?.textContent).toContain("Authentication headers are managed by the browser or Studio transport."));
    expect(requestResponse).not.toHaveBeenCalled();
  });

  it("reserves Content-Type for the request content type field", async () => {
    const container = await renderHeaderDialog();
    await click(container.querySelector<HTMLButtonElement>("button.wf-http-test-add-header")!);
    await changeInput(container.querySelector<HTMLInputElement>("input[aria-label='Header 1 name']")!, "content-type");
    await changeInput(container.querySelector<HTMLInputElement>("input[aria-label='Header 1 value']")!, "text/plain");
    await click(container.querySelector<HTMLButtonElement>("button[type='submit']")!);

    await waitFor(() => expect(container.querySelector('[role="alert"]')?.textContent).toContain("Set Content-Type using the Request content type field."));
    expect(requestResponse).not.toHaveBeenCalled();
  });

  it("clears custom headers when the dialog is closed and reopened", async () => {
    const container = await renderHeaderDialog();
    await click(container.querySelector<HTMLButtonElement>("button.wf-http-test-add-header")!);
    await changeInput(container.querySelector<HTMLInputElement>("input[aria-label='Header 1 name']")!, "X-Private-Test");
    await changeInput(container.querySelector<HTMLInputElement>("input[aria-label='Header 1 value']")!, "sensitive-value");

    await setDialogOpen(false);
    await new Promise(resolve => setTimeout(resolve, 0));
    await setDialogOpen(true);
    await waitFor(() => expect(container.querySelector("textarea[aria-label='Request body']")).not.toBeNull());
    await waitFor(() => expect(container.querySelector("summary")?.textContent).toBe("Request headers"));

    expect(container.querySelector("input[aria-label='Header 1 value']")).toBeNull();
    expect(container.textContent).not.toContain("sensitive-value");
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

  it("changes rich syntax without replacing the authored request body", async () => {
    advertiseWorkflowHttpBasePath();
    vi.mocked(getExecutable).mockResolvedValue(publishedExecutable());
    vi.mocked(getExecutableInputSources).mockResolvedValue(publishedEndpointInputs());
    const container = await renderDialog();
    await waitFor(() => expect(container.querySelector(".cm-content")).not.toBeNull());
    const editor = () => container.querySelector<HTMLElement>("[data-studio-code-editor]")!;
    const body = container.querySelector(".cm-content")!.textContent;
    expect(editor().dataset.language).toBe("json");
    expect(body).toContain("Ada Lovelace");
    const contentType = container.querySelector<HTMLInputElement>("input[role='combobox']")!;
    for (const [type, language] of [["application/vnd.acme+json; charset=utf-8", "json"], ["text/html", "html"], ["application/soap+xml", "xml"], ["application/unknown", "plaintext"]]) {
      await changeInput(contentType, type);
      await waitFor(() => expect(editor().dataset.language).toBe(language));
      expect(container.querySelector(".cm-content")!.textContent).toBe(body);
    }
    expect(requestResponse).not.toHaveBeenCalled();
  });

  it("lets the rich editor handle Escape without closing the request dialog", async () => {
    advertiseWorkflowHttpBasePath();
    vi.mocked(getExecutable).mockResolvedValue(publishedExecutable());
    vi.mocked(getExecutableInputSources).mockResolvedValue(publishedEndpointInputs());
    const onClose = vi.fn();
    const container = await renderDialog(onClose);
    await waitFor(() => expect(container.querySelector(".cm-content")).not.toBeNull());
    const content = container.querySelector<HTMLElement>(".cm-content")!;
    const escape = new KeyboardEvent("keydown", { key: "Escape", keyCode: 27, bubbles: true, cancelable: true });
    flushSync(() => content.dispatchEvent(escape));
    expect(escape.defaultPrevented).toBe(true);
    expect(onClose).not.toHaveBeenCalled();
    await click(container.querySelector<HTMLButtonElement>("button[aria-label='Close test request']")!);
    expect(onClose).toHaveBeenCalledOnce();
  });

  it.each(["GET", "HEAD"])("keeps the request body read-only and omits it for %s", async method => {
    advertiseWorkflowHttpBasePath();
    vi.mocked(getExecutable).mockResolvedValue(publishedExecutable());
    vi.mocked(getExecutableInputSources).mockResolvedValue(publishedEndpointInputs(method));
    requestResponse.mockResolvedValue(new Response("ok", { status: 200 }));
    const container = await renderDialog();
    await waitFor(() => expect(container.querySelector(".cm-content")).not.toBeNull());
    expect(container.querySelector("[data-studio-code-editor]")?.getAttribute("data-readonly")).toBe("true");
    expect(container.querySelector(".cm-content")?.getAttribute("contenteditable")).toBe("false");
    expect(container.querySelector<HTMLInputElement>("input[role='combobox']")!.disabled).toBe(true);
    await click(container.querySelector<HTMLButtonElement>("button[type='submit']")!);
    await waitFor(() => expect(requestResponse).toHaveBeenCalledOnce());
    expect(requestResponse.mock.calls[0][1]?.body).toBeUndefined();
    expect(new Headers(requestResponse.mock.calls[0][1]?.headers).has("Content-Type")).toBe(false);
  });

  it("shows real response headers with linked keyboard-accessible tabs and a read-only body", async () => {
    advertiseWorkflowHttpBasePath();
    vi.mocked(getExecutable).mockResolvedValue(publishedExecutable());
    vi.mocked(getExecutableInputSources).mockResolvedValue(publishedEndpointInputs());
    requestResponse.mockResolvedValue(new Response('{"message":"Hello"}', {
      status: 200,
      headers: { "content-type": "application/json; charset=utf-8", "x-request-id": "trace-123", "cache-control": "no-cache" }
    }));
    const container = await renderDialog();
    await waitFor(() => expect(container.querySelector("textarea[aria-label='Request body']")).not.toBeNull());
    await click(container.querySelector<HTMLButtonElement>("button[type='submit']")!);
    await waitFor(() => expect(container.querySelector("[role='tablist'][aria-label='HTTP response sections']")).not.toBeNull());
    const tabs = [...container.querySelectorAll<HTMLButtonElement>("[role='tab']")];
    const [bodyTab, headersTab] = tabs;
    expect(bodyTab.textContent).toBe("Body");
    expect(bodyTab.getAttribute("aria-selected")).toBe("true");
    expect(headersTab.textContent).toBe("Headers (3)");
    const headersPanel = document.getElementById(headersTab.getAttribute("aria-controls")!)!;
    expect(headersPanel.hidden).toBe(true);
    expect(headersPanel.getAttribute("aria-labelledby")).toBe(headersTab.id);
    await waitFor(() => expect(container.querySelector("[aria-label='Response body'].cm-content")).not.toBeNull());
    const responseEditor = container.querySelector("[aria-label='Response body'].cm-content")!;
    expect(responseEditor.getAttribute("contenteditable")).toBe("false");
    expect(responseEditor.closest("[data-studio-code-editor]")?.getAttribute("data-language")).toBe("json");
    flushSync(() => {
      bodyTab.focus();
      bodyTab.dispatchEvent(new KeyboardEvent("keydown", { key: "ArrowRight", bubbles: true, cancelable: true }));
    });
    expect(headersTab.getAttribute("aria-selected")).toBe("true");
    expect(document.activeElement).toBe(headersTab);
    expect(headersPanel.hidden).toBe(false);
    const rows = [...headersPanel.querySelectorAll("tbody tr")].map(row => [...row.children].map(cell => cell.textContent));
    expect(rows).toEqual([["cache-control", "no-cache"], ["content-type", "application/json; charset=utf-8"], ["x-request-id", "trace-123"]]);
    flushSync(() => headersTab.dispatchEvent(new KeyboardEvent("keydown", { key: "Home", bubbles: true, cancelable: true })));
    expect(bodyTab.getAttribute("aria-selected")).toBe("true");
    expect(headersPanel.hidden).toBe(true);
    expect(requestResponse).toHaveBeenCalledOnce();
  });

  it("shows clear empty body and header states for a 204 response", async () => {
    advertiseWorkflowHttpBasePath();
    vi.mocked(getExecutable).mockResolvedValue(publishedExecutable());
    vi.mocked(getExecutableInputSources).mockResolvedValue(publishedEndpointInputs());
    requestResponse.mockResolvedValue(new Response(null, { status: 204, statusText: "No Content" }));
    const container = await renderDialog();
    await waitFor(() => expect(container.querySelector("textarea[aria-label='Request body']")).not.toBeNull());
    await click(container.querySelector<HTMLButtonElement>("button[type='submit']")!);
    await waitFor(() => expect(container.textContent).toContain("204 No Content"));
    expect(container.textContent).toContain("The response body is empty.");
    const headersTab = [...container.querySelectorAll<HTMLButtonElement>("[role='tab']")].find(tab => tab.textContent === "Headers (0)")!;
    await click(headersTab);
    expect(document.getElementById(headersTab.getAttribute("aria-controls")!)!.textContent).toContain("No response headers are available.");
  });

  it("clears the previous response when a new attempt has an invalid endpoint path", async () => {
    advertiseWorkflowHttpBasePath();
    vi.mocked(getExecutable).mockResolvedValue(publishedExecutable());
    vi.mocked(getExecutableInputSources).mockResolvedValue(publishedEndpointInputs());
    requestResponse.mockResolvedValue(new Response("previous response", { status: 200 }));
    const container = await renderDialog();
    await waitFor(() => expect(container.querySelector("textarea[aria-label='Request body']")).not.toBeNull());
    const send = container.querySelector<HTMLButtonElement>("button[type='submit']")!;
    await click(send);
    await waitFor(() => expect(container.querySelector("[aria-label='HTTP response']")).not.toBeNull());
    await changeInput(container.querySelector<HTMLInputElement>("input[aria-label='Endpoint path']")!, "../api/admin");
    await click(send);
    expect(container.textContent).toContain("Dot segments are not allowed");
    expect(container.querySelector("[aria-label='HTTP response']")).toBeNull();
    expect(requestResponse).toHaveBeenCalledOnce();
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
    const onClose = vi.fn();
    const container = await renderHeaderDialog(["POST"], onClose);
    let resolveResponse!: (response: Response) => void;
    let requestSignal: AbortSignal | undefined;
    requestResponse.mockImplementation((_url, init) => {
      requestSignal = init?.signal as AbortSignal | undefined;
      return new Promise(resolve => { resolveResponse = resolve; });
    });

    await click(container.querySelector<HTMLButtonElement>("button.wf-http-test-add-header")!);
    await changeInput(container.querySelector<HTMLInputElement>("input[aria-label='Header 1 name']")!, "X-In-Flight");
    await changeInput(container.querySelector<HTMLInputElement>("input[aria-label='Header 1 value']")!, "locked");
    const send = container.querySelector<HTMLButtonElement>("button[type='submit']")!;
    await click(send);
    await click(send);

    expect(requestResponse).toHaveBeenCalledTimes(1);
    expect(requestSignal?.aborted).toBe(false);
    expect(container.querySelector<HTMLInputElement>("input[role='combobox']")!.disabled).toBe(true);
    expect(container.querySelector("[role='listbox']")).toBeNull();
    expect(container.querySelector<HTMLInputElement>("input[aria-label='Header 1 name']")!.disabled).toBe(true);
    expect(container.querySelector<HTMLInputElement>("input[aria-label='Header 1 value']")!.disabled).toBe(true);
    expect(container.querySelector<HTMLButtonElement>("button[aria-label='Remove header 1']")!.disabled).toBe(true);
    expect(container.querySelector<HTMLButtonElement>("button.wf-http-test-add-header")!.disabled).toBe(true);
    await click(container.querySelector<HTMLButtonElement>("button[aria-label='Close test request']")!);
    expect(onClose).toHaveBeenCalledTimes(1);
    expect(requestSignal?.aborted).toBe(true);

    resolveResponse(new Response("late response", { status: 200 }));
    await new Promise(resolve => setTimeout(resolve, 0));
    expect(container.textContent).not.toContain("late response");

    requestResponse.mockResolvedValue(new Response("reopened response", { status: 200 }));
    await setDialogOpen(false);
    await setDialogOpen(true);
    await waitFor(() => expect(container.querySelector("textarea[aria-label='Request body']")).not.toBeNull());
    await waitFor(() => expect(container.querySelector("summary")?.textContent).toBe("Request headers"));
    const reopenedSend = container.querySelector<HTMLButtonElement>("button[type='submit']")!;
    expect(reopenedSend.disabled).toBe(false);

    container.querySelector<HTMLDetailsElement>(".wf-http-test-request-headers")!.open = true;
    await click(container.querySelector<HTMLButtonElement>("button.wf-http-test-add-header")!);
    expect(container.querySelector<HTMLInputElement>("input[aria-label='Header 1 name']")!.disabled).toBe(false);
    expect(container.querySelector<HTMLInputElement>("input[aria-label='Header 1 value']")!.disabled).toBe(false);
    await click(reopenedSend);
    await waitFor(() => expect(requestResponse).toHaveBeenCalledTimes(2));
    await waitFor(() => expect(container.textContent).toContain("reopened response"));
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
  mounted = { root, container, onClose };
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

async function renderHeaderDialog(methods: string[] = ["POST"], onClose = () => undefined) {
  advertiseWorkflowHttpBasePath();
  vi.mocked(getExecutable).mockResolvedValue(publishedExecutable());
  vi.mocked(getExecutableInputSources).mockResolvedValue(publishedEndpointInputs(methods));
  requestResponse.mockResolvedValue(new Response("ok", { status: 200 }));

  const container = await renderDialog(onClose);
  await waitFor(() => expect(container.querySelector("textarea[aria-label='Request body']")).not.toBeNull());
  container.querySelector<HTMLDetailsElement>(".wf-http-test-request-headers")!.open = true;
  return container;
}

async function setDialogOpen(open: boolean) {
  const current = mounted;
  if (!current) throw new Error("The HTTP test dialog is not mounted.");
  flushSync(() => current.root.render(
    <HttpTestRequestDialog
      context={context}
      open={open}
      artifactId="artifact-1"
      authoredActivityId="endpoint-1"
      onClose={current.onClose}
    />
  ));
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

function publishedEndpointInputs(method: string | string[] = "POST"): WorkflowExecutableInputSources {
  return {
    artifactId: "artifact-1",
    sourceReferenceId: "reference-1",
    accessState: "allowed",
    authoredInputs: [],
    compiledInputs: [
      { executableNodeId: "endpoint-exec", accessState: "allowed", binding: { inputName: "Path", inputKey: "Path", source: "Literal", literalValue: "orders/{id:int}" } },
      { executableNodeId: "endpoint-exec", accessState: "allowed", binding: { inputName: "SupportedMethods", inputKey: "SupportedMethods", source: "Literal", literalValue: Array.isArray(method) ? method : [method] } },
      { executableNodeId: "endpoint-exec", accessState: "allowed", binding: { inputName: "ResponseMode", inputKey: "ResponseMode", source: "Literal", literalValue: 0 } }
    ]
  };
}
