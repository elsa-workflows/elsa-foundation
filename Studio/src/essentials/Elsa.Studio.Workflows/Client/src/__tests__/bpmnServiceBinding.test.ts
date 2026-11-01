import { expect, it } from "vitest";
import { directBindingProperty, directHumanMode, directHumanPatch, directServiceBinding, directServicePatch, type DirectServiceBinding } from "../bpmn/bpmnDirectBinding";
import type { BpmnElement } from "../bpmn/bpmnTypes";

const serviceBinding: DirectServiceBinding = {
  formatVersion: 1, kind: "nativeService", sourceReferenceId: "source-1",
  executable: { artifactId: "artifact-1", definitionId: "definition-1", definitionVersionId: "version-1", artifactVersion: "1.0.0", artifactHash: "a".repeat(64) },
  businessErrorCode: "service-failure", faultPolicy: { formatVersion: 1, disposition: "HoldForRetry", maxAttempts: 3 }
};

it("recognizes an exact authored service binding without rewriting its JSON", () => {
  const element: BpmnElement = { elementId: "service", elementType: "serviceTask", properties: { [directBindingProperty]: JSON.stringify(serviceBinding) } };
  const before = JSON.stringify(element);
  expect(directHumanMode(element)).toBe("service");
  expect(JSON.stringify(element)).toBe(before);
});

it("retains native listener intent before any direct task rebinding", () => {
  const element: BpmnElement = { elementId: "service", elementType: "task", listenerNodeId: "native-listener" };
  expect(directHumanMode(element)).toBe("native");
  expect(directHumanPatch(element, "human")).toEqual({});
});

it.each(["duplicate", "escaped-duplicate", "pin-duplicate", "policy-duplicate", "future", "unknown", "blank", "budget", "policy-case", "native", "listener"])("retains unsupported service metadata without enabling destructive rebinding: %s", invalid => {
  const binding = structuredClone(serviceBinding);
  if (invalid === "future") Object.assign(binding, { formatVersion: 99 });
  if (invalid === "unknown") Object.assign(binding.executable, { futureMeaning: true });
  if (invalid === "blank") binding.sourceReferenceId = " ";
  if (invalid === "budget") binding.faultPolicy.maxAttempts = 33;
  if (invalid === "policy-case") Object.assign(binding.faultPolicy, { disposition: "holdForRetry" });
  let raw = JSON.stringify(binding);
  if (invalid === "duplicate") raw = raw.replace('"kind":', '"kind":"nativeService","kind":');
  if (invalid === "escaped-duplicate") raw = raw.replace('"kind":', '"\\u006bind":"nativeService","kind":');
  if (invalid === "pin-duplicate") raw = raw.replace('"artifactId":', '"artifactId":"other","artifactId":');
  if (invalid === "policy-duplicate") raw = raw.replace('"maxAttempts":', '"maxAttempts":1,"maxAttempts":');
  const element: BpmnElement = { elementId: "service", elementType: "serviceTask", properties: { [directBindingProperty]: raw },
    ...(invalid === "native" ? { childNodeId: "native" } : {}), ...(invalid === "listener" ? { listenerNodeId: "native" } : {}) };
  expect(directHumanMode(element)).toBe("unsupported");
  expect(directServiceBinding(element)).toBeNull();
  expect(directServicePatch(element, serviceBinding)).toEqual({});
  expect(directHumanPatch(element, "human")).toEqual({});
  expect(element.properties?.[directBindingProperty]).toBe(raw);
});

it("accepts escaped quotes in business error values and preserves unrelated properties on an explicit edit", () => {
  const binding = { ...serviceBinding, businessErrorCode: 'error "name": value' };
  const element: BpmnElement = { elementId: "service", elementType: "task", properties: { vendor: "keep" } };
  const patch = directServicePatch(element, binding);
  const service = { ...element, ...patch };
  expect(directServiceBinding(service)?.businessErrorCode).toBe(binding.businessErrorCode);
  expect(service.properties?.vendor).toBe("keep");
  expect(directHumanPatch(service, "unbound")).toEqual({ elementType: "task", properties: { vendor: "keep" } });
});
