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

// --- collection (list) literal authoring ----------------------------------
//
// A Literal-typed input whose .NET type is a collection (List<T>, ICollection<T>, T[], …) is authored
// as a repeater of per-element editors instead of a single text box, so values are structured rather
// than jammed into one ambiguous delimited string. Collection element types come through `typeName`
// only when the backend includes generic arguments; when they don't, rows fall back to text editors.

const collectionTypeNames = new Set([
  "icollection", "ireadonlycollection", "ilist", "ireadonlylist", "list",
  "collection", "ienumerable", "iset", "hashset", "sortedset", "observablecollection", "array"
]);

export interface CollectionTypeInfo {
  /** The element type name when it can be recovered from `typeName`, otherwise null (render text rows). */
  elementTypeName: string | null;
}

/** Recognize a collection-typed input from its .NET `typeName`; returns null for non-collection types. */
export function describeCollectionType(typeName: string): CollectionTypeInfo | null {
  const trimmed = typeName?.trim();
  if (!trimmed) return null;

  // Array form: "Namespace.Element[]" optionally followed by ", Assembly".
  const arrayMatch = /^([\w.+]+)\[\]/.exec(trimmed);
  if (arrayMatch) return { elementTypeName: arrayMatch[1] };

  // Generic form: "...Kind`N[...]" — the collection kind is the last segment before the arity backtick.
  const backtick = trimmed.indexOf("`");
  if (backtick < 0) return null;
  const base = trimmed.slice(0, backtick);
  const kind = (base.split(".").pop() ?? base).toLowerCase();
  if (!collectionTypeNames.has(kind)) return null;

  return { elementTypeName: extractFirstGenericArgument(trimmed.slice(backtick)) };
}

// Pull the first generic argument's type name from either the assembly-qualified "[[Type, Asm], …]"
// form or the short "[Type, …]" form; null when no arguments are present (e.g. a bare "ICollection`1").
function extractFirstGenericArgument(genericPart: string): string | null {
  const assemblyQualified = /\[\[([\w.+]+)/.exec(genericPart);
  if (assemblyQualified) return assemblyQualified[1];
  const shortForm = /\[([\w.+]+)/.exec(genericPart);
  return shortForm ? shortForm[1] : null;
}

/** Authors opt out of the repeater (back to a raw text/JSON box) via a hint or a uiSpecifications flag. */
export function isRepeaterOptOut(input: StudioActivityInputDescriptor): boolean {
  const hint = input.uiHint?.toLowerCase();
  if (hint === "json" || hint === "code") return true;
  return input.uiSpecifications?.repeater === false;
}

// Coerce a stored literal value into an array of elements without guessing delimiters of legacy strings.
// Deliberately parallel to `toValueArray` in the host's propertyEditors.tsx: the studio-sdk boundary is
// type-only (modules import no runtime SDK values), so sharing this would add load-time coupling for ~10
// lines. Keep the two in sync by hand rather than crossing that boundary.
export function toLiteralCollection(value: unknown): unknown[] {
  if (Array.isArray(value)) return value;
  if (value == null) return [];
  if (typeof value === "string") {
    const trimmed = value.trim();
    if (!trimmed) return [];
    if (trimmed.startsWith("[")) {
      try {
        const parsed = JSON.parse(trimmed);
        if (Array.isArray(parsed)) return parsed;
      } catch {
        // Not JSON — preserve the original text as a single item rather than splitting on a guess.
      }
    }
    return [value];
  }
  return [value];
}

/** A new row's starting value, typed from the element type so booleans start unchecked rather than "". */
export function defaultCollectionItem(elementTypeName: string | null): unknown {
  const type = (elementTypeName ?? "").toLowerCase();
  return type === "system.boolean" || type === "boolean" || type === "bool" ? false : "";
}

/** A synthetic descriptor describing one element, so a repeater row resolves the right element editor. */
export function makeCollectionElementDescriptor(
  input: StudioActivityInputDescriptor,
  elementTypeName: string | null
): StudioActivityInputDescriptor {
  return {
    ...input,
    typeName: elementTypeName ?? "System.String",
    isWrapped: false,
    // The description documents the collection as a whole; repeating it on every row is noise.
    description: null
  };
}

/** Immutably move an item between positions; out-of-range or no-op moves return the original array. */
export function moveCollectionItem<T>(items: T[], from: number, to: number): T[] {
  if (from === to || from < 0 || to < 0 || from >= items.length || to >= items.length) return items;
  const next = [...items];
  const [moved] = next.splice(from, 1);
  next.splice(to, 0, moved);
  return next;
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
