import { getExecutable } from "../api/runtime";
import { afterEach, describe, expect, it, vi } from "vitest";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import type { WorkflowExecutableDetails, WorkflowExecutableInputSources, WorkflowExecutableNode } from "../workflowTypes";
import { capabilityIds, clearApiCapabilityCache } from "../api/capabilities";
import {
  describeHttpEndpointAcceptedResponse,
  findPublishedHttpEndpoint,
  getWorkflowHttpBasePath,
  makeInitialHttpTestPath,
  publishedHttpEndpointInputsUnavailableMessage,
  readHttpEndpointResponseMetadata,
  resolveHttpTestEndpointPath,
  resolveHttpTestRequestPath,
  workflowHttpBasePathCapabilityRelation,
  workflowHttpBasePathUnavailableMessage
} from "../workflow-editor/httpTestRequest";

afterEach(clearApiCapabilityCache);

describe("published HTTP test request helpers", () => {
  it.each([
    ["orders/{id:int:min(1)}", "orders/1"],
    ["orders/{id:min(1):int}", "orders/1"],
    ["orders/{id:guid:required}", "orders/00000000-0000-0000-0000-000000000001"]
  ])("uses the type constraint in %s to choose the editable example", (template, example) => {
    expect(makeInitialHttpTestPath(template, "/workflows/http")).toBe(`/workflows/http/${example}`);
  });

  it("explains when a newly added draft endpoint is absent from the published workflow", () => {
    const executable = executableWithEndpoint({ authoredActivityId: "published-endpoint", inputBindings: [] });
    expect(() => findPublishedHttpEndpoint(executable, "new-draft-endpoint", inputSources([])))
      .toThrow("This endpoint is not in the latest published workflow. Publish your changes, then try again.");
  });

  it("uses permitted compiled endpoint inputs from the selected published reference", () => {
    const executable = executableWithEndpoint({
      authoredActivityId: "endpoint-1",
      inputBindings: [
        { inputName: "Path", inputKey: "Path", source: "Literal", literalValue: null },
        { inputName: "SupportedMethods", inputKey: "SupportedMethods", source: "Literal", literalValue: null },
        { inputName: "ResponseMode", inputKey: "ResponseMode", source: "Literal", literalValue: null }
      ]
    });

    const endpoint = findPublishedHttpEndpoint(executable, "endpoint-1", inputSources([
      binding("Path", "orders/{orderId:int}"),
      binding("SupportedMethods", ["GET", "POST"]),
      binding("ResponseMode", 0)
    ]));

    expect(endpoint).toEqual({
      authoredActivityId: "endpoint-1",
      routeTemplate: "orders/{orderId:int}",
      supportedMethods: ["GET", "POST"],
      responseMode: "async"
    });
    expect(makeInitialHttpTestPath(endpoint!.routeTemplate, "/workflows/http")).toBe("/workflows/http/orders/1");
    expect(makeInitialHttpTestPath(endpoint!.routeTemplate, "/tenant/api/workflows/")).toBe("/tenant/api/workflows/orders/1");
  });

  it("applies the engine's GET default only when no SupportedMethods input is authored", () => {
    const noMethod = executableWithEndpoint({
      authoredActivityId: "endpoint-1",
      inputBindings: [{ inputName: "Path", inputKey: "Path", source: "Literal", literalValue: null }]
    });
    const emptyMethods = executableWithEndpoint({
      authoredActivityId: "endpoint-1",
      inputBindings: [
        { inputName: "Path", inputKey: "Path", source: "Literal", literalValue: null },
        { inputName: "SupportedMethods", inputKey: "SupportedMethods", source: "Literal", literalValue: null }
      ]
    });

    expect(findPublishedHttpEndpoint(noMethod, "endpoint-1", inputSources([
      binding("Path", "health")
    ]))?.supportedMethods).toEqual(["GET"]);
    expect(findPublishedHttpEndpoint(emptyMethods, "endpoint-1", inputSources([
      binding("Path", "health"),
      binding("SupportedMethods", [])
    ]))?.supportedMethods).toEqual(["GET"]);
  });

  it("requires permission to read the input-source projection and never uses redacted summary literals", () => {
    const executable = executableWithEndpoint({
      authoredActivityId: "endpoint-1",
      inputBindings: [{ inputName: "Path", inputKey: "Path", source: "Literal", literalValue: "should-not-be-used" }]
    });
    const redactedInputs = inputSources([{
      executableNodeId: "endpoint-exec",
      accessState: "redacted",
      binding: { inputName: "Path", inputKey: "Path", source: "Literal", literalValue: null }
    }]);

    expect(() => findPublishedHttpEndpoint(executable, "endpoint-1", redactedInputs))
      .toThrow(publishedHttpEndpointInputsUnavailableMessage);
    expect(() => findPublishedHttpEndpoint(executable, "endpoint-1", {
      ...redactedInputs,
      accessState: "denied"
    })).toThrow(publishedHttpEndpointInputsUnavailableMessage);
  });

  it("keeps edited paths on the configured backend and beneath its workflow HTTP base path", () => {
    expect(resolveHttpTestRequestPath("https://backend.example/api/", "/workflows/http/orders/1?q=a", "/workflows/http"))
      .toBe("/workflows/http/orders/1?q=a");
    expect(resolveHttpTestRequestPath("https://backend.example/", "workflows/http/orders/1", "/workflows/http"))
      .toBe("/workflows/http/orders/1");
    expect(resolveHttpTestRequestPath("https://backend.example/", "/tenant/api/workflows/orders/1", "/tenant/api/workflows/"))
      .toBe("/tenant/api/workflows/orders/1");
    expect(() => resolveHttpTestRequestPath("https://backend.example/", "/api/admin/users", "/workflows/http"))
      .toThrow("workflow HTTP base path");
    expect(() => resolveHttpTestRequestPath("https://backend.example/", "/workflows/http", "/workflows/http"))
      .toThrow("workflow HTTP base path");
    expect(() => resolveHttpTestRequestPath("https://backend.example/", "/workflows/http/", "/workflows/http"))
      .toThrow("workflow HTTP base path");
    expect(() => resolveHttpTestRequestPath("https://backend.example/", "/workflows/http-admin/users", "/workflows/http"))
      .toThrow("workflow HTTP base path");
    expect(() => resolveHttpTestRequestPath("https://backend.example/", "https://attacker.example/collect", "/workflows/http"))
      .toThrow("configured workflow HTTP base path");
    expect(() => resolveHttpTestRequestPath("https://backend.example/", "//attacker.example/collect", "/workflows/http"))
      .toThrow("configured workflow HTTP base path");
    expect(() => resolveHttpTestRequestPath("https://backend.example/", "\\\\attacker.example\\collect", "/workflows/http"))
      .toThrow("workflow HTTP base path");
  });

  it.each(["orders/2?name=Ada%20Lovelace", "/orders/2?name=Ada%20Lovelace"])(
    "joins endpoint path %s to a shell-prefixed configured mount exactly once",
    route => {
      expect(resolveHttpTestEndpointPath("https://backend.example/engine/team-a/", route, "/engine/team-a/custom-http/"))
        .toBe("/engine/team-a/custom-http/orders/2?name=Ada%20Lovelace");
    }
  );

  it.each([
    "", "/", "?q=one", "https://other.example/orders", "//other.example/orders", "orders#fragment",
    "../admin", "/%2e%2e/admin", "/%252e%252e/admin", "orders%2fadmin", "orders%255cadmin", "orders\\admin"
  ])("rejects unsafe or empty endpoint path %j before joining the mount", route => {
    expect(() => resolveHttpTestEndpointPath("https://backend.example/", route, "/workflows/http")).toThrow();
  });

  it("rejects literal, encoded, and multiply encoded traversal or path separators before URL canonicalization", () => {
    for (const path of [
      "/workflows/http/../admin",
      "/workflows/http/%2e%2e/admin",
      "/workflows/http/.%2e/admin",
      "/workflows/http/%252e%252e/admin",
      "/workflows/http/orders%2f..%2fadmin",
      "/workflows/http/orders%5c..%5cadmin",
      "/workflows/http/orders%252fadmin"
    ]) {
      expect(() => resolveHttpTestRequestPath("https://backend.example/", path, "/workflows/http"), path)
        .toThrow();
    }
  });

  it("rejects invalid or unsafe configured workflow base paths", () => {
    for (const basePath of ["", "/", "//other.example/workflows", "/api/%2e%2e/admin", "/api/%252fadmin", "/api\\admin"]) {
      expect(() => resolveHttpTestRequestPath("https://backend.example/", "/workflows/http/orders/1", basePath), basePath)
        .toThrow();
    }
  });

  it.each([
    ["tenant/custom-http", "/engine/shell-a/tenant/custom-http"],
    ["api%3Av1/workflows", "/engine/shell-a/api%3Av1/workflows"],
    ["tenant/workflow%20endpoints", "/engine/shell-a/tenant/workflow%20endpoints"]
  ])("reads the advertised workflow mount %s relative to the backend shell path", async (href, expectedPath) => {
    const getJson = vi.fn().mockResolvedValue({ capabilities: [{
      id: capabilityIds.workflowHttpEndpoints,
      contractVersion: "1",
      links: [{ rel: workflowHttpBasePathCapabilityRelation, href }]
    }] });
    const context = {
      baseUrl: "https://backend.example/engine/shell-a",
      http: { getJson }
    } as unknown as StudioEndpointContext;

    await expect(getWorkflowHttpBasePath(context)).resolves.toBe(expectedPath);
    const requestPath = makeInitialHttpTestPath("probe", expectedPath);
    expect(resolveHttpTestRequestPath(context.baseUrl, requestPath, expectedPath)).toBe(`${expectedPath}/probe`);
    expect(getJson).toHaveBeenCalledWith("/engine/shell-a/capabilities");
  });

  it("loads the published executable from the same prefixed shell as the HTTP mount", async () => {
    const getJson = vi.fn().mockImplementation(async path => {
      if (path === "/engine/shell-a/capabilities") return { capabilities: [{
        id: capabilityIds.runtime,
        contractVersion: "1",
        links: [{ rel: "workflow-executable", href: "runtime/workflow-executables/{artifactId}" }]
      }, {
        id: capabilityIds.workflowHttpEndpoints,
        contractVersion: "1",
        links: [{ rel: workflowHttpBasePathCapabilityRelation, href: "tenant/custom-http" }]
      }] };
      if (path === "/engine/shell-a/runtime/workflow-executables/artifact-1") return { artifactId: "artifact-1" };
      throw new Error(`Unexpected path ${path}`);
    });
    const context = { baseUrl: "https://backend.example/engine/shell-a", http: { getJson } } as unknown as StudioEndpointContext;

    await expect(getExecutable(context, "artifact-1")).resolves.toEqual({ artifactId: "artifact-1" });
    await expect(getWorkflowHttpBasePath(context)).resolves.toBe("/engine/shell-a/tenant/custom-http");
    expect(getJson).toHaveBeenCalledWith("/engine/shell-a/runtime/workflow-executables/artifact-1");
  });

  it("fails closed when the server does not advertise a supported, dedicated workflow mount", async () => {
    const contextFor = (capability: unknown) => ({
      baseUrl: "https://backend.example/",
      http: { getJson: vi.fn().mockResolvedValue({ capabilities: capability ? [capability] : [] }) }
    } as unknown as StudioEndpointContext);

    await expect(getWorkflowHttpBasePath(contextFor(null))).rejects.toThrow(workflowHttpBasePathUnavailableMessage);
    await expect(getWorkflowHttpBasePath(contextFor({
      id: capabilityIds.workflowHttpEndpoints,
      contractVersion: "2",
      links: [{ rel: workflowHttpBasePathCapabilityRelation, href: "workflows/http" }]
    }))).rejects.toThrow(workflowHttpBasePathUnavailableMessage);
    await expect(getWorkflowHttpBasePath(contextFor({
      id: capabilityIds.workflowHttpEndpoints,
      contractVersion: "1",
      links: [{ rel: workflowHttpBasePathCapabilityRelation, href: "" }]
    }))).rejects.toThrow(workflowHttpBasePathUnavailableMessage);
    await expect(getWorkflowHttpBasePath(contextFor({
      id: capabilityIds.workflowHttpEndpoints,
      contractVersion: "1",
      links: [{ rel: workflowHttpBasePathCapabilityRelation, href: "workflows/http/../admin" }]
    }))).rejects.toThrow(workflowHttpBasePathUnavailableMessage);
  });

  it("reads additive 202 response metadata and gives a cautious fallback when it is absent", () => {
    const metadata = readHttpEndpointResponseMetadata(JSON.stringify({
      started: ["execution-1"],
      resumed: [],
      response: { mode: "sync", reason: "workflow-response-not-produced", message: "The workflow paused before writing a response." }
    }));

    expect(metadata).toEqual({ mode: "sync", reason: "workflow-response-not-produced", message: "The workflow paused before writing a response." });
    expect(describeHttpEndpointAcceptedResponse(metadata)).toBe("The workflow paused before writing a response.");
    expect(describeHttpEndpointAcceptedResponse(null)).toContain("may not have written a response");
  });
});

function executableWithEndpoint(endpoint: Partial<WorkflowExecutableNode> & Pick<WorkflowExecutableNode, "authoredActivityId" | "inputBindings">): WorkflowExecutableDetails {
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
          activityType: "Elsa.Activities.Http.Activities.HttpEndpoint",
          activityTypeVersion: "1",
          structureKind: null,
          childSlots: [],
          ...endpoint
        }]
      }]
    }
  };
}

function inputSources(compiledInputs: WorkflowExecutableInputSources["compiledInputs"]): WorkflowExecutableInputSources {
  return {
    artifactId: "artifact-1",
    sourceReferenceId: "reference-1",
    accessState: "allowed",
    authoredInputs: [],
    compiledInputs
  };
}

function binding(inputKey: string, literalValue: unknown): WorkflowExecutableInputSources["compiledInputs"][number] {
  return {
    executableNodeId: "endpoint-exec",
    accessState: "allowed",
    binding: { inputName: inputKey, inputKey, source: "Literal", literalValue }
  };
}
