import { lazy, Suspense, useEffect, useRef, useState } from "react";
import {
  StudioCodeEditor,
  type StudioCodeDiagnostic,
  type StudioCodeDocument,
  type StudioCodeLanguageAdapter
} from "@elsa-workflows/studio-code-editor";
import type {
  StudioActivityPropertyEditorContribution,
  StudioExpressionEditorContribution,
  StudioExpressionEditorProps
} from "@elsa-workflows/studio-sdk";
import {
  conflictsWithStructuredEditor,
  describeCollectionForInput,
  describeDictionaryForInput,
  isRepeaterOptOut
} from "./activityProperties";
import { CollectionValueEditor } from "./CollectionValueEditor";

const DictionaryValueEditor = lazy(() => import("./DictionaryValueEditor").then(module => ({ default: module.DictionaryValueEditor })));

const objectSyntax = "Object";
const jsonLanguageAdapter: StudioCodeLanguageAdapter = { language: "json", displayName: "JSON" };
const objectDrafts = new WeakMap<object, Map<string, string>>();

export function ObjectInlineEditor({
  descriptor,
  value,
  disabled = false,
  initialFocus,
  context,
  onChange,
  propertyEditors
}: StudioExpressionEditorProps & { propertyEditors: StudioActivityPropertyEditorContribution[] }) {
  const summaryRef = useRef<HTMLDivElement>(null);
  // Shape-checked: an array on a dictionary input (or an object on a collection input) must stay in the
  // JSON editor below, or the structured editor would coerce the authored value away on first edit.
  const structured = !isRepeaterOptOut(descriptor);
  const dictionaryType = structured && !conflictsWithStructuredEditor("dictionary", value)
    ? describeDictionaryForInput(descriptor)
    : null;
  const collectionType = structured && !conflictsWithStructuredEditor("collection", value)
    ? describeCollectionForInput(descriptor)
    : null;

  useEffect(() => {
    if (initialFocus && !dictionaryType && !collectionType) summaryRef.current?.focus();
  }, [collectionType, dictionaryType, initialFocus]);

  if (dictionaryType) {
    return (
      <Suspense fallback={<DictionaryEditorFallback variant="inline" />}>
        <DictionaryValueEditor
          input={descriptor}
          valueTypeName={dictionaryType.valueTypeName}
          value={value}
          editors={propertyEditors}
          context={{
            activity: context.activity,
            expressionDescriptors: context.expressionDescriptors,
            readOnly: disabled
          }}
          disabled={disabled}
          onChange={onChange}
        />
      </Suspense>
    );
  }

  if (collectionType) {
    return (
      <CollectionValueEditor
        input={descriptor}
        elementTypeName={collectionType.elementTypeName}
        value={value}
        editors={propertyEditors}
        context={{
          activity: context.activity,
          expressionDescriptors: context.expressionDescriptors,
          readOnly: disabled
        }}
        disabled={disabled}
        onChange={onChange}
      />
    );
  }

  return (
    <div
      ref={summaryRef}
      className="wf-object-expression-summary"
      role="status"
      tabIndex={-1}
      aria-label={`${descriptor.displayName || descriptor.name} object summary`}
    >
      <span>{summarizeValue(value)}</span>
      <small>Open the expanded editor to edit JSON.</small>
    </div>
  );
}

export function ObjectExpandedEditor({
  descriptor,
  value,
  disabled = false,
  context,
  onChange,
  propertyEditors = []
}: StudioExpressionEditorProps & { propertyEditors?: StudioActivityPropertyEditorContribution[] }) {
  const dictionaryType = !isRepeaterOptOut(descriptor) && !conflictsWithStructuredEditor("dictionary", value)
    ? describeDictionaryForInput(descriptor)
    : null;
  if (dictionaryType) {
    return (
      <Suspense fallback={<DictionaryEditorFallback variant="expanded" />}>
        <DictionaryValueEditor
          input={descriptor}
          valueTypeName={dictionaryType.valueTypeName}
          value={value}
          editors={propertyEditors}
          context={{
            activity: context.activity,
            expressionDescriptors: context.expressionDescriptors,
            readOnly: disabled
          }}
          disabled={disabled}
          variant="expanded"
          onChange={onChange}
        />
      </Suspense>
    );
  }
  return (
    <GenericObjectExpandedEditor
      descriptor={descriptor}
      syntax={objectSyntax}
      value={value}
      disabled={disabled}
      context={context}
      onChange={onChange}
    />
  );
}

export function GenericObjectExpandedEditor({ descriptor, value, disabled = false, context, onChange, rawJson = false, wireJson = false }: StudioExpressionEditorProps & { rawJson?: boolean; wireJson?: boolean }) {
  const uri = `elsa://expressions/object/${encodeURIComponent(descriptor.name || "value")}.json`;
  const parse = rawJson ? parseJson : parseStructuredJson;
  const externalKey = rawJson ? wireJson && typeof value === "string" ? value : JSON.stringify(value) : serializeValue(value);
  const [draft, setDraft] = useState(() => readDraft(context.activity, descriptor.name) ?? formatJsonEditorValue(value, rawJson, wireJson));
  const [diagnostic, setDiagnostic] = useState<StudioCodeDiagnostic | null>(() => diagnoseJson(draft, uri, rawJson));
  const emittedKey = useRef<string | null>(null);
  const invalidRawDraft = useRef<string | null>(null);
  const previousKey = useRef(externalKey);
  const document: StudioCodeDocument = { uri, language: "json", value: draft };

  useEffect(() => {
    const sameValue = previousKey.current === externalKey;
    previousKey.current = externalKey;
    if (rawJson && invalidRawDraft.current != null && sameValue) {
      writeDraft(context.activity, descriptor.name, invalidRawDraft.current);
      return;
    }
    if (rawJson && !sameValue) invalidRawDraft.current = null;
    if (emittedKey.current === externalKey) {
      emittedKey.current = null;
      return;
    }
    const retainedDraft = readDraft(context.activity, descriptor.name);
    if (retainedDraft != null) {
      setDraft(retainedDraft);
      setDiagnostic(diagnoseJson(retainedDraft, uri, rawJson));
      return;
    }
    const nextDraft = formatJsonEditorValue(value, rawJson, wireJson);
    setDraft(nextDraft);
    setDiagnostic(diagnoseJson(nextDraft, uri, rawJson));
  }, [context.activity, descriptor.name, externalKey, rawJson, uri, value, wireJson]);

  const handleChange = (nextDocument: StudioCodeDocument) => {
    setDraft(nextDocument.value);
    const parsed = parse(nextDocument.value);
    if (parsed.ok) {
      emittedKey.current = rawJson ? nextDocument.value : serializeValue(parsed.value);
      invalidRawDraft.current = null;
      writeDraft(context.activity, descriptor.name, null);
      setDiagnostic(null);
      onChange(rawJson ? nextDocument.value : parsed.value);
    } else {
      if (rawJson) invalidRawDraft.current = nextDocument.value;
      writeDraft(context.activity, descriptor.name, nextDocument.value);
      setDiagnostic(toDiagnostic(parsed.error, uri));
    }
  };

  return (
    <div className="wf-object-json-editor">
      <StudioCodeEditor
        ariaLabel={`${descriptor.displayName || descriptor.name} JSON value`}
        document={document}
        diagnostics={diagnostic ? [diagnostic] : []}
        languageAdapter={jsonLanguageAdapter}
        minHeight="280px"
        readOnly={disabled}
        theme="studio"
        onChange={handleChange}
      />
    </div>
  );
}

function formatJsonEditorValue(value: unknown, rawJson: boolean, wireJson: boolean) {
  if (!rawJson) return formatValue(value);
  if (wireJson && typeof value === "string") return value;
  return JSON.stringify(value === undefined ? {} : value, null, 2) ?? "{}";
}

function formatValue(value: unknown): string {
  let normalized = value;
  if (typeof value === "string") {
    const parsedWireValue = parseJson(value);
    if (!parsedWireValue.ok) return value;
    normalized = parsedWireValue.value;
  }
  try {
    return JSON.stringify(normalized ?? {}, null, 2) ?? "{}";
  } catch {
    return typeof value === "string" ? value : "{}";
  }
}

function serializeValue(value: unknown) {
  const normalized = normalizeWireValue(value);
  try {
    return JSON.stringify(normalized) ?? "undefined";
  } catch {
    return "unserializable";
  }
}

function summarizeValue(value: unknown) {
  const normalized = normalizeWireValue(value);
  if (Array.isArray(normalized)) return `${normalized.length} ${normalized.length === 1 ? "item" : "items"}`;
  if (normalized && typeof normalized === "object") {
    const count = Object.keys(normalized).length;
    return `${count} ${count === 1 ? "property" : "properties"}`;
  }
  if (normalized == null) return "Empty object";
  if (typeof value === "string" && !parseJson(value).ok) return "Invalid JSON value";
  return "JSON value";
}

function normalizeWireValue(value: unknown) {
  if (typeof value !== "string") return value;
  const parsed = parseJson(value);
  return parsed.ok ? parsed.value : value;
}

function diagnoseJson(value: string, uri: string, rawJson = false) {
  const parsed = rawJson ? parseJson(value) : parseStructuredJson(value);
  return parsed.ok ? null : toDiagnostic(parsed.error, uri);
}

function toDiagnostic(error: unknown, uri: string): StudioCodeDiagnostic {
  return {
    uri,
    severity: "error",
    code: "INVALID_JSON",
    message: `Invalid JSON: ${error instanceof Error ? error.message : "Unable to parse the document."}`
  };
}

function parseJson(value: string): { ok: true; value: unknown } | { ok: false; error: unknown } {
  try {
    return { ok: true, value: JSON.parse(value) as unknown };
  } catch (error) {
    return { ok: false, error };
  }
}

function parseStructuredJson(value: string): { ok: true; value: object } | { ok: false; error: unknown } {
  const parsed = parseJson(value);
  if (!parsed.ok) return parsed;
  if (parsed.value == null || typeof parsed.value !== "object") {
    return { ok: false, error: new Error("Top-level JSON must be an object or array.") };
  }
  return { ok: true, value: parsed.value };
}

function readDraft(activity: unknown, propertyName: string) {
  if (!activity || typeof activity !== "object") return null;
  return objectDrafts.get(activity)?.get(propertyName) ?? null;
}

function writeDraft(activity: unknown, propertyName: string, draft: string | null) {
  if (!activity || typeof activity !== "object") return;
  if (draft == null) {
    objectDrafts.get(activity)?.delete(propertyName);
    return;
  }
  const drafts = objectDrafts.get(activity) ?? new Map<string, string>();
  drafts.set(propertyName, draft);
  objectDrafts.set(activity, drafts);
}

function DictionaryEditorFallback({ variant }: { variant: "inline" | "expanded" }) {
  return (
    <div
      className={`wf-dictionary-editor wf-dictionary-editor-loading ${variant}`}
      role="status"
      aria-live="polite"
      aria-busy="true"
    >
      Loading dictionary editor…
    </div>
  );
}

export const objectExpressionEditorId = "elsa.object-expression-editor";

export function createObjectExpressionEditorContribution(
  getPropertyEditors: () => StudioActivityPropertyEditorContribution[]
): StudioExpressionEditorContribution {
  return {
    id: objectExpressionEditorId,
    order: 90,
    supports: context => context.syntax === objectSyntax,
    surfaces: {
      inline: props => <ObjectInlineEditor {...props} propertyEditors={getPropertyEditors()} />,
      expanded: props => <ObjectExpandedEditor {...props} propertyEditors={getPropertyEditors()} />
    },
    createDefaultValue: context => describeCollectionForInput(context.descriptor) ? [] : {}
  };
}
