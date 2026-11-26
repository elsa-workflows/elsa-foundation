import { useId } from "react";
import { StudioField } from "@elsa-workflows/studio-ui";
import type { MappingContext } from "./types";

export interface SourcePathDraft { path: string[]; pathText?: string }
interface SourceFieldSelection extends SourcePathDraft { kind: "SourceField" | "Literal" }

export function formatSourcePath(path: string[]): string {
  return path.map(segment => `/${segment.replaceAll("~", "~0").replaceAll("/", "~1")}`).join("");
}

export function parseSourcePath(text: string): string[] {
  if (!text) throw new Error("Enter a source path, for example /payload/customer/id.");
  if (!text.startsWith("/")) throw new Error("Start the source path with /, for example /payload/customer/id.");
  if (/~(?![01])/.test(text)) throw new Error("Use ~0 for ~ and ~1 for / in property names.");
  return text.slice(1).split("/").map(segment => segment.replaceAll("~1", "/").replaceAll("~0", "~"));
}

export function realizeSourcePath(draft: SourcePathDraft): string[] {
  if (draft.pathText !== undefined) return parseSourcePath(draft.pathText);
  if (!draft.path.length) throw new Error("Choose a source field or enter a custom path.");
  return draft.path;
}

export function SourceFieldSelector({ fields, value, onChange, label, pathLabel, allowLiteral = false }: {
  fields: MappingContext["sourceSchema"]["fields"]; value: SourceFieldSelection; onChange(value: SourceFieldSelection): void;
  label: string; pathLabel: string; allowLiteral?: boolean;
}) {
  const errorId = useId();
  const encodedPath = JSON.stringify(value.path);
  const custom = value.kind === "SourceField" && (value.pathText !== undefined || value.path.length > 0 && !fields.some(field => JSON.stringify(field.path) === encodedPath));
  const text = value.pathText ?? formatSourcePath(value.path);
  let error: string | undefined;
  if (custom) { try { parseSourcePath(text); } catch (cause) { error = (cause as Error).message; } }
  return <>
    <StudioField label="Source field"><select aria-label={label} value={value.kind === "Literal" ? "literal" : custom ? "custom" : encodedPath} onChange={event => {
      const selected = event.currentTarget.value;
      onChange(selected === "literal" ? { ...value, kind: "Literal" } : selected === "custom"
        ? { ...value, kind: "SourceField", pathText: text }
        : { kind: "SourceField", path: JSON.parse(selected) as string[], pathText: undefined });
    }}>
      <option value="[]">Choose a source field…</option>
      {allowLiteral ? <option value="literal">Configured JSON value</option> : null}
      {fields.map(field => <option key={JSON.stringify(field.path)} value={JSON.stringify(field.path)}>{field.label}</option>)}
      <option value="custom">Custom path…</option>
    </select></StudioField>
    {custom ? <StudioField label="Custom source path" description="Use /payload/customer/id. Each / separates a property name; dots stay in the name. Use ~1 for / and ~0 for ~. Arrays can be selected as whole values; array items cannot be selected.">
      <input aria-label={pathLabel} value={text} placeholder="/payload/customer/id" aria-invalid={!!error} aria-describedby={error ? errorId : undefined} onChange={event => {
        const pathText = event.currentTarget.value;
        let path: string[] = [];
        try { path = parseSourcePath(pathText); } catch { /* Keep invalid text in the draft for correction. */ }
        onChange({ kind: "SourceField", path, pathText });
      }} />
      {error ? <small id={errorId} role="alert">{error}</small> : null}
    </StudioField> : null}
  </>;
}
