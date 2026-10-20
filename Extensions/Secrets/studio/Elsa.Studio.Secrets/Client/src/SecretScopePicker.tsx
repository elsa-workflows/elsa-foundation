import React, { useId, useMemo, useState } from "react";
import type { SecretScopeDescriptor } from "./secretTypes";

const searchableScopeCount = 8;
export const SECRET_SCOPE_MATCHING_HINT = "Optional qualifier. A reference that specifies a scope must match it (ignoring case). Scope does not grant access.";

export function ScopeMatchingHint({ readOnly = false, id }: { readOnly?: boolean; id?: string }) {
  return (
    <p id={id} className="secrets-field-hint">
      {SECRET_SCOPE_MATCHING_HINT}{readOnly ? " Scope is read-only in this editor." : ""}
    </p>
  );
}

export function SecretScopePicker({
  value,
  options,
  loading = false,
  error = false,
  onChange,
  onRetry,
  externalMatchingHintId
}: {
  value: string;
  options?: SecretScopeDescriptor[];
  loading?: boolean;
  error?: boolean;
  onChange(value: string): void;
  onRetry?(): void;
  externalMatchingHintId?: string;
}) {
  const id = useId();
  const [customMode, setCustomMode] = useState(false);
  const [search, setSearch] = useState("");
  const scopeOptions = useMemo(() => uniqueScopes(options ?? []), [options]);
  const customOptionValue = useMemo(() => {
    let sentinel = `__custom_scope_${id}__`;
    while (scopeOptions.some(option => option.name === sentinel)) sentinel += "_";
    return sentinel;
  }, [id, scopeOptions]);
  const selectedScope = scopeOptions.find(option => option.name === value)
    ?? scopeOptions.find(option => ordinalIgnoreCaseEquals(option.name, value));
  const isCustom = customMode || (value.length > 0 && !selectedScope);
  const selectValue = isCustom ? customOptionValue : value;
  const searchable = scopeOptions.length >= searchableScopeCount;
  const query = search.trim().toLocaleLowerCase();
  const visibleOptions = scopeOptions.filter(option => {
    if (!query || option.name === value || option.name === selectedScope?.name) return true;
    return [option.name, option.displayName, option.description, option.source]
      .some(part => part?.toLocaleLowerCase().includes(query));
  });
  const selectedDescription = selectedScope?.description?.trim();
  const selectedSource = selectedScope?.source?.trim();
  const hintId = `scope-hint-${id}`;
  const describedBy = externalMatchingHintId ? `${externalMatchingHintId} ${hintId}` : hintId;

  return (
    <div className="secrets-scope-picker">
      <div className="secrets-scope-heading">
        <label htmlFor={`scope-select-${id}`}>Scope</label>
        {onRetry ? (
          <button type="button" onClick={onRetry} disabled={loading} aria-busy={loading}>
            Refresh scope suggestions
          </button>
        ) : null}
      </div>
      {searchable ? (
        <label htmlFor={`scope-search-${id}`}>
          Search scopes
          <input
            id={`scope-search-${id}`}
            type="search"
            value={search}
            onChange={event => setSearch(event.target.value)}
            placeholder="Search suggested scopes"
            autoComplete="off"
          />
        </label>
      ) : null}
      <select
        id={`scope-select-${id}`}
        value={selectValue}
        aria-describedby={describedBy}
        onChange={event => {
          if (event.target.value === customOptionValue) {
            setCustomMode(true);
            return;
          }

          setCustomMode(false);
          onChange(event.target.value);
        }}
      >
        <option value="">No scope</option>
        {isCustom ? <option value={customOptionValue}>{value ? `Custom: ${value}` : "Custom…"}</option> : null}
        {selectedScope && selectedScope.name !== value ? <option value={value}>Current value: {value}</option> : null}
        {visibleOptions.map(option => (
          <option key={option.name} value={option.name}>{option.displayName || option.name}</option>
        ))}
        {!isCustom ? <option value={customOptionValue}>Custom…</option> : null}
      </select>
      {isCustom ? (
        <label htmlFor={`scope-custom-${id}`}>
          Custom scope value
          <input
            id={`scope-custom-${id}`}
            value={value}
            onChange={event => onChange(event.target.value)}
            placeholder="Enter a scope value"
            autoComplete="off"
          />
        </label>
      ) : null}
      <div id={hintId}>
        {externalMatchingHintId ? null : <ScopeMatchingHint />}
        {selectedDescription ? <p className="secrets-field-hint">{selectedDescription}</p> : null}
        {selectedSource ? <p className="secrets-field-hint">Source: {selectedSource}</p> : null}
        {loading ? <p className="secrets-field-hint" role="status">Loading scope suggestions…</p> : null}
        {!loading && !error && scopeOptions.length === 0 ? (
          <p className="secrets-field-hint">No suggestions are available. You can still enter a custom scope.</p>
        ) : null}
        {!loading && !error && searchable && query && visibleOptions.length === 0 ? (
          <p className="secrets-field-hint">No suggested scopes match that search. You can enter a custom scope.</p>
        ) : null}
        {error ? (
          <p className="secrets-field-hint" role="alert">
            Scope suggestions could not be refreshed. Existing scope values are still available.
            {onRetry ? <> <button type="button" onClick={onRetry} disabled={loading}>Retry scope suggestions</button></> : null}
          </p>
        ) : null}
      </div>
    </div>
  );
}

function uniqueScopes(options: SecretScopeDescriptor[]) {
  const seen = new Set<string>();
  return options.filter(option => {
    if (!option.name || seen.has(option.name)) return false;
    seen.add(option.name);
    return true;
  });
}

function ordinalIgnoreCaseEquals(left: string, right: string) {
  const leftCharacters = Array.from(left);
  const rightCharacters = Array.from(right);
  if (leftCharacters.length !== rightCharacters.length) return false;

  return leftCharacters.every((character, index) => {
    const candidate = rightCharacters[index];
    if (character === candidate) return true;
    const upperCharacter = character.toUpperCase();
    const upperCandidate = candidate.toUpperCase();
    const simpleUpperCharacter = Array.from(upperCharacter).length === 1 ? upperCharacter : character;
    const simpleUpperCandidate = Array.from(upperCandidate).length === 1 ? upperCandidate : candidate;
    return simpleUpperCharacter === simpleUpperCandidate;
  });
}
