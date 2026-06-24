import type {
  StudioActivityInputDescriptor,
  StudioExpressionDescriptor
} from "@elsa-workflows/studio-sdk";
import type { ActivityNode } from "./workflowTypes";

export interface ActivityExpression {
  type: string;
  value: unknown;
}

export interface WrappedActivityInputValue {
  typeName: string;
  expression: ActivityExpression;
  memoryReference?: unknown;
}

export const defaultExpressionDescriptors: StudioExpressionDescriptor[] = [
  { type: "Literal", displayName: "Literal" },
  { type: "JavaScript", displayName: "JavaScript" },
  { type: "Liquid", displayName: "Liquid" },
  { type: "Object", displayName: "Object" },
  { type: "Variable", displayName: "Variable" },
  { type: "Input", displayName: "Input" }
];

export function camelize(value: string) {
  const trimmed = value.trim();
  return trimmed ? trimmed.charAt(0).toLowerCase() + trimmed.slice(1) : value;
}

export function getInputPropertyName(descriptor: StudioActivityInputDescriptor) {
  return camelize(descriptor.name);
}

export function readInputValue(activity: ActivityNode, descriptor: StudioActivityInputDescriptor) {
  const propertyName = getInputPropertyName(descriptor);
  const value = activity[propertyName];
  return descriptor.isWrapped === false ? value ?? descriptor.defaultValue ?? "" : readWrappedInputValue(value, descriptor);
}

export function readWrappedInput(activity: ActivityNode, descriptor: StudioActivityInputDescriptor) {
  return readWrappedInputValue(activity[getInputPropertyName(descriptor)], descriptor);
}

export function withLiteralValue(previous: WrappedActivityInputValue, value: unknown): WrappedActivityInputValue {
  return {
    ...previous,
    expression: {
      type: previous.expression.type || "Literal",
      value
    }
  };
}

export function withSyntax(previous: WrappedActivityInputValue, syntax: string): WrappedActivityInputValue {
  return {
    ...previous,
    expression: {
      type: syntax,
      value: previous.expression.value
    }
  };
}

export function writeInputValue(activity: ActivityNode, descriptor: StudioActivityInputDescriptor, value: unknown): ActivityNode {
  return {
    ...activity,
    [getInputPropertyName(descriptor)]: value
  };
}

export function getLiteralEditorValue(activity: ActivityNode, descriptor: StudioActivityInputDescriptor) {
  if (descriptor.isWrapped === false) return readInputValue(activity, descriptor);
  return readWrappedInput(activity, descriptor).expression.value;
}

export function readWrappedInputValue(value: unknown, descriptor: StudioActivityInputDescriptor): WrappedActivityInputValue {
  if (isWrappedInputValue(value)) {
    return {
      typeName: value.typeName || descriptor.typeName,
      expression: {
        type: value.expression.type || descriptor.defaultSyntax || "Literal",
        value: value.expression.value
      },
      ...(value.memoryReference ? { memoryReference: value.memoryReference } : {})
    };
  }

  return {
    typeName: descriptor.typeName,
    expression: {
      type: descriptor.defaultSyntax || "Literal",
      value: value ?? descriptor.defaultValue ?? ""
    }
  };
}

function isWrappedInputValue(value: unknown): value is WrappedActivityInputValue {
  if (!value || typeof value !== "object") return false;
  const record = value as Record<string, unknown>;
  const expression = record.expression;
  return typeof record.typeName === "string" &&
    !!expression &&
    typeof expression === "object" &&
    typeof (expression as Record<string, unknown>).type === "string";
}
