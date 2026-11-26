import { describe, expect, it } from "vitest";
import { httpWorkflowTestStimulusEditor } from "../workflowTestStimulusEditors";
import { defaultHttpTestRequest, defaultHttpTestRequestDraft, defaultStimulusDraft } from "../workflowTestRequestDefaults";

const httpOption = {
  optionId: "http-option",
  driverId: "Elsa.HttpEndpoint",
  nodeId: "http-node",
  label: "HTTP endpoint",
  modes: [{ modeId: "http-request", label: "Send HTTP request", isDefault: true }]
};

describe("HTTP Test Run request defaults", () => {
  it("rejects the empty object that Studio used as the initial HTTP request", () => {
    expect(httpWorkflowTestStimulusEditor.validate({
      option: httpOption,
      mode: httpOption.modes[0],
      draft: "{}"
    })).toBe("routeValues must be an object of string values.");
  });

  it("accepts the empty HTTP request shape that matches the backend contract", () => {
    expect(defaultStimulusDraft(httpOption, httpOption.modes[0])).toBe(defaultHttpTestRequestDraft);
    expect(JSON.parse(defaultHttpTestRequestDraft)).toEqual(defaultHttpTestRequest);
    expect(httpWorkflowTestStimulusEditor.validate({
      option: httpOption,
      mode: httpOption.modes[0],
      draft: defaultHttpTestRequestDraft
    })).toBeUndefined();
    expect(httpWorkflowTestStimulusEditor.serialize({
      option: httpOption,
      mode: httpOption.modes[0],
      draft: defaultHttpTestRequestDraft
    })).toEqual({ routeValues: {}, query: {}, headers: {}, body: "" });
  });

  it("keeps a generic start on an empty object and still rejects invalid HTTP maps", () => {
    expect(defaultStimulusDraft()).toBe("{}");
    expect(defaultStimulusDraft({ driverId: "Elsa.Event" }, { modeId: "event" })).toBe("{}");
    expect(httpWorkflowTestStimulusEditor.validate({
      option: httpOption,
      mode: httpOption.modes[0],
      draft: JSON.stringify({ routeValues: { id: 42 }, query: {}, headers: {}, body: "" })
    })).toBe("routeValues must be an object of string values.");
  });
});
