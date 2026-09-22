import type { StudioActivityInputDescriptor } from "@elsa-workflows/studio-sdk";
import type { ActivityCatalogItem, ActivityNode } from "./workflowTypes";
import { getInputPropertyName, readWrappedInputValue } from "./activityProperties";
import { describeInferredSource } from "./conversionSource";

/**
 * A per-instance one-liner for a canvas node: a preview of the activity's most meaningful authored
 * input (the text for Write Line, the condition for If, the URL for HTTP Request). It is what
 * distinguishes two nodes of the same type, so it replaces the static `category · kind` subtitle.
 *
 * Returns `undefined` when nothing meaningful is authored yet — the node then falls back to its bare
 * kind (Action/Trigger/…). The result is intentionally NOT truncated: the caller truncates for the
 * visible line while keeping the full string for the tooltip.
 */
export function formatActivitySummary(activity: ActivityNode, catalogItem: ActivityCatalogItem): string | undefined {
  const headline = pickHeadlineInput(catalogItem);
  if (!headline) return undefined;

  const expression = readHeadlineExpression(activity, headline);
  return expression ? formatExpression(expression.type, expression.value) : undefined;
}

/**
 * The activity's primary input, mirroring the property panel's ordering (`groupInputs`): browsable
 * inputs sorted by an explicit `order`, falling back to catalog delivery order, ties keeping the
 * original index (never alphabetical) so the author-intended first field stays first.
 */
function pickHeadlineInput(catalogItem: ActivityCatalogItem): StudioActivityInputDescriptor | undefined {
  const inputs = (catalogItem.inputs ?? []) as StudioActivityInputDescriptor[];
  return inputs
    .map((input, index) => ({ input, index }))
    .filter(entry => entry.input.isBrowsable !== false)
    .sort((left, right) => (left.input.order ?? left.index) - (right.input.order ?? right.index) || left.index - right.index)
    .map(entry => entry.input)[0];
}

/**
 * Reads the authored expression for `descriptor` from either node shape: the editor keeps values as a
 * top-level property keyed by `referenceKey` (expanded form), while a wire-shaped node carries them in
 * `inputs[]` as `{ referenceKey, value: { value, expressionType } }`. Returns `undefined` when the
 * input was never authored (so the summary falls back to the kind rather than showing a default).
 */
function readHeadlineExpression(activity: ActivityNode, descriptor: StudioActivityInputDescriptor): { type: string; value: unknown } | undefined {
  const propertyName = getInputPropertyName(descriptor);
  const expanded = activity[propertyName];
  if (expanded !== undefined) return readWrappedInputValue(expanded, descriptor).expression;

  const wire = Array.isArray(activity.inputs)
    ? activity.inputs.find(entry => isRecord(entry) && entry.referenceKey === descriptor.referenceKey)
    : undefined;
  if (isRecord(wire) && isRecord(wire.value)) {
    return { type: typeof wire.value.expressionType === "string" ? wire.value.expressionType : "Literal", value: wire.value.value };
  }

  return undefined;
}

/**
 * Renders an authored `{ type, value }` as a compact display string:
 * - literals: strings quoted, numbers/booleans verbatim; empty/null/structured values yield `undefined`
 *   (nothing useful to show → fall back to kind, never "[object Object]").
 * - text expressions (JavaScript/Liquid/C#/…): the raw expression source, e.g. `total > 100`.
 * - reference expressions (Variable/Input): a source caption, since the value is a structured reference.
 */
function formatExpression(expressionType: string, value: unknown): string | undefined {
  const type = expressionType.trim().toLowerCase();

  if (type === "" || type === "literal" || type === "object") {
    if (typeof value === "string") {
      const trimmed = value.trim();
      return trimmed ? `"${trimmed}"` : undefined;
    }
    if (typeof value === "number" || typeof value === "boolean") return String(value);
    return undefined;
  }

  if (type === "variable" || type === "input") return describeInferredSource(type, value, "auto");

  if (typeof value === "string") {
    const trimmed = value.trim();
    return trimmed || undefined;
  }
  return undefined;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null;
}
