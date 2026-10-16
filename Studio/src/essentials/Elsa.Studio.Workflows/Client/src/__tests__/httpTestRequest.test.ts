import { describe, expect, it } from "vitest";
import type { WorkflowExecutableDetails, WorkflowExecutableInputSources, WorkflowExecutableNode } from "../workflowTypes";
import {
  describeHttpEndpointAcceptedResponse,
  findPublishedHttpEndpoint,
  makeInitialHttpTestPath,
  publishedHttpEndpointInputsUnavailableMessage,
  readHttpEndpointResponseMetadata,
  resolveHttpTestRequestPath
} from "../workflow-editor/httpTestRequest";

describe("published HTTP test request helpers", () => {
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
    expect(makeInitialHttpTestPath(endpoint!.routeTemplate)).toBe("/workflows/http/orders/1");
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

  it("keeps edited paths on the configured backend and rejects absolute or network-path URLs", () => {
    expect(resolveHttpTestRequestPath("https://backend.example/api/", "/workflows/http/orders/1?q=a"))
      .toBe("/workflows/http/orders/1?q=a");
    expect(resolveHttpTestRequestPath("https://backend.example/", "workflows/http/orders/1"))
      .toBe("/workflows/http/orders/1");
    expect(() => resolveHttpTestRequestPath("https://backend.example/", "https://attacker.example/collect"))
      .toThrow("configured backend");
    expect(() => resolveHttpTestRequestPath("https://backend.example/", "//attacker.example/collect"))
      .toThrow("configured backend");
    expect(() => resolveHttpTestRequestPath("https://backend.example/", "\\\\attacker.example\\collect"))
      .toThrow("configured backend");
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
