import type { StudioActivityInputDescriptor, StudioExpressionDescriptor } from "@elsa-workflows/studio-sdk";
import type { ActivityNode } from "./workflowTypes";
import {
  acceptsOnlySecretReference,
  getInputPropertyName,
  isEmptyExpressionValue,
  planExpressionModeTransition,
  secretSyntax,
  type ExpressionModeTransition
} from "./activityProperties";

// Rules for masked inputs: an input with a password hint, or one the activity declares sensitive or secret-only.
// Secret-only inputs are a subset of masked inputs that bind only a Secret Reference; the properties panel gives
// them their own branch, the secret picker. Kept out of `activityProperties.ts`, which ships in the Workflows entry
// chunk: only deferred surfaces (the properties panel, the canvas summary and the run inspector) use these.

/** Whether the input is masked: it carries a password hint, or the activity declares it sensitive or secret-only. */
export function isMaskedInput(descriptor: StudioActivityInputDescriptor) {
  return descriptor.uiHint?.toLowerCase() === "password" || descriptor.isSensitive === true || acceptsOnlySecretReference(descriptor);
}

/** A Secret Reference as the secret picker normalizes it: a name, and its type and scope when they are text. */
export interface SecretReferenceView {
  name: string;
  typeName: string | null;
  scope: string | null;
}

/**
 * Reads the Secret Reference an authored expression holds: the exact Secret syntax holding an object with a
 * non-empty name, as the secret picker writes it, or that object as JSON text on a wire-shaped node. The result is
 * normalized as the secret picker's own reader normalizes it (`toReference` in the Secrets extension, which this
 * module cannot import because the SDK boundary is type-only and an essentials module does not import an
 * extension's code; a shared test table holds the two to the same cases), so any other field is dropped. Anything else under the Secret syntax, such as
 * a plain string the backend accepts at save and refuses only at publish, names no secret: `null`.
 */
export function readSecretReference(expressionType: string | null | undefined, value: unknown): SecretReferenceView | null {
  if (expressionType !== secretSyntax) return null;
  const candidate = typeof value === "string" ? parseJson(value) : value;
  if (!candidate || typeof candidate !== "object") return null;
  const { name, typeName, scope } = candidate as Record<string, unknown>;
  if (typeof name !== "string" || !name.trim()) return null;
  return {
    name,
    typeName: typeof typeName === "string" ? typeName : null,
    scope: typeof scope === "string" ? scope : null
  };
}

function parseJson(text: string): unknown {
  try {
    return JSON.parse(text);
  } catch {
    return undefined;
  }
}

/**
 * Whether a masked input may show what it stores under an expression of this editing mode: only author code
 * (`text`: JavaScript, Liquid) and a reference by name (`reference`: Variable, Input, Secret). Under every other
 * mode, `literal`, `structured` such as Object, a syntax without a descriptor and any mode added later, the stored
 * value is the input's value itself and stays masked.
 */
export function showsMaskedValue(editingMode: StudioExpressionDescriptor["editingMode"] | undefined) {
  return editingMode === "text" || editingMode === "reference";
}

/**
 * Plans switching an input from one editing mode to another. A masked value is never carried into a syntax that
 * shows what it stores: the target starts from its default, and discarding a stored value asks first, like any
 * other lossy switch. Every other switch is planned by `planExpressionModeTransition`.
 */
export function planInputSyntaxTransition(
  descriptor: StudioActivityInputDescriptor,
  sourceMode: StudioExpressionDescriptor["editingMode"],
  targetMode: StudioExpressionDescriptor["editingMode"],
  value: unknown,
  targetDefaultValue: unknown
): ExpressionModeTransition {
  if (isMaskedInput(descriptor) && !showsMaskedValue(sourceMode) && showsMaskedValue(targetMode)) {
    return { requiresConfirmation: !isEmptyExpressionValue(value), nextValue: targetDefaultValue };
  }
  return planExpressionModeTransition(sourceMode, targetMode, descriptor.typeName, value, targetDefaultValue);
}

/**
 * Clearing a secret-only input unbinds it: the property is removed from the activity, so nothing is
 * serialized for it and no empty value pretends to be one. Returns the unbound activity, or `null` when
 * `nextValue` is not a clear of a secret-only input (the caller then writes it as usual).
 */
export function clearSecretOnlyInput(activity: ActivityNode, descriptor: StudioActivityInputDescriptor, nextValue: unknown): ActivityNode | null {
  if (!acceptsOnlySecretReference(descriptor) || !isEmptyExpressionValue(nextValue)) return null;
  const { [getInputPropertyName(descriptor)]: _unbound, ...rest } = activity;
  return rest as ActivityNode;
}
