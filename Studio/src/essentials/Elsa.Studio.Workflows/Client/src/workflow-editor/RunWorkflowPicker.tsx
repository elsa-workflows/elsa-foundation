import { useEffect, useId, useRef, useState, type KeyboardEvent } from "react";
import { CopyableIdentifier } from "@elsa-workflows/studio-ui";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { getDefinition, listDefinitions } from "../api/workflowDesign";
import type { WorkflowDefinitionSummary } from "../workflowTypes";

const definitionPageSize = 20;

type SearchState = "idle" | "loading" | "ready" | "failed";

export function RunWorkflowPicker({ context, definitionId, onChange }: {
  context: StudioEndpointContext;
  definitionId: string;
  onChange(definitionId: string): void;
}) {
  const inputId = useId();
  const listboxId = `${inputId}-definitions`;
  const rootRef = useRef<HTMLDivElement>(null);
  const inputRef = useRef<HTMLInputElement>(null);
  const searchGeneration = useRef(0);
  const identityGeneration = useRef(0);
  const inputEditGeneration = useRef(0);
  const resolvedIdentity = useRef<{ context: StudioEndpointContext; id: string } | null>(null);
  const optionRefs = useRef<Array<HTMLDivElement | null>>([]);
  const [identityState, setIdentityState] = useState<"idle" | "loading" | "ready" | "failed">("idle");
  const [identityError, setIdentityError] = useState("");
  const [inputValue, setInputValue] = useState("");
  const [searchText, setSearchText] = useState("");
  const [results, setResults] = useState<WorkflowDefinitionSummary[]>([]);
  const [searchState, setSearchState] = useState<SearchState>("idle");
  const [searchError, setSearchError] = useState("");
  const [nextContinuation, setNextContinuation] = useState<string | null>(null);
  const [nextPage, setNextPage] = useState<number | null>(null);
  const [totalCount, setTotalCount] = useState<number | null>(null);
  const [activeIndex, setActiveIndex] = useState(-1);
  const [open, setOpen] = useState(false);
  const [retryVersion, setRetryVersion] = useState(0);

  useEffect(() => {
    const generation = ++identityGeneration.current;
    if (!definitionId) {
      resolvedIdentity.current = null;
      setIdentityState("idle");
      setIdentityError("");
      setInputValue("");
      return;
    }

    if (resolvedIdentity.current?.context === context && resolvedIdentity.current.id === definitionId) return;
    setInputValue(definitionId);
    setIdentityState("loading");
    setIdentityError("");
    const editGeneration = inputEditGeneration.current;
    void getDefinition(context, definitionId).then(
      response => {
        if (identityGeneration.current !== generation) return;
        const name = response.definition.name?.trim() || definitionId;
        resolvedIdentity.current = { context, id: definitionId };
        if (inputEditGeneration.current === editGeneration) setInputValue(name);
        setIdentityState("ready");
      },
      error => {
        if (identityGeneration.current !== generation) return;
        resolvedIdentity.current = { context, id: definitionId };
        setIdentityError(error instanceof Error ? error.message : String(error));
        setIdentityState("failed");
      }
    );
    return () => {
      identityGeneration.current++;
    };
  }, [context, definitionId]);

  useEffect(() => {
    const query = searchText.trim();
    const generation = ++searchGeneration.current;
    setResults([]);
    setNextContinuation(null);
    setNextPage(null);
    setTotalCount(null);
    setSearchError("");
    setActiveIndex(-1);

    if (!query) {
      setSearchState("idle");
      return;
    }

    setOpen(true);
    setSearchState("loading");
    void listDefinitions(context, {
      search: query,
      state: "all",
      page: 1,
      pageSize: definitionPageSize
    }).then(response => {
      if (searchGeneration.current !== generation) return;
      setResults(response.definitions);
      setNextContinuation(response.nextContinuationToken ?? null);
      setNextPage(response.isPaged ? null : 2);
      setTotalCount(response.totalCount ?? null);
      setSearchState("ready");
    }, error => {
      if (searchGeneration.current !== generation) return;
      setSearchError(error instanceof Error ? error.message : String(error));
      setSearchState("failed");
    });

    return () => {
      searchGeneration.current++;
    };
  }, [context, searchText, retryVersion]);

  useEffect(() => {
    if (open && activeIndex >= 0) optionRefs.current[activeIndex]?.scrollIntoView?.({ block: "nearest" });
  }, [activeIndex, open]);

  const loadNextPage = async () => {
    const query = searchText.trim();
    if (!query || searchState === "loading") return;
    const continuationToken = nextContinuation ?? undefined;
    if (!continuationToken && nextPage === null) return;
    const generation = ++searchGeneration.current;
    setSearchState("loading");
    setSearchError("");
    try {
      const page = nextPage ?? 2;
      const response = await listDefinitions(context, {
        search: query,
        state: "all",
        page,
        pageSize: definitionPageSize,
        continuationToken
      });
      if (searchGeneration.current !== generation) return;
      setResults(current => [...current, ...response.definitions]);
      setNextContinuation(response.nextContinuationToken ?? null);
      setNextPage(response.isPaged ? null : page + 1);
      setTotalCount(response.totalCount ?? totalCount);
      setSearchState("ready");
    } catch (error) {
      if (searchGeneration.current !== generation) return;
      setSearchError(error instanceof Error ? error.message : String(error));
      setSearchState("failed");
    }
  };

  const canLoadMore = Boolean(nextContinuation)
    || (nextPage !== null && totalCount !== null && results.length < totalCount);
  const activeDefinition = results[activeIndex];
  const activeOptionId = activeDefinition ? `${listboxId}-${activeIndex}` : undefined;
  const choose = (definition: WorkflowDefinitionSummary) => {
    const name = definition.name?.trim() || definition.id;
    resolvedIdentity.current = { context, id: definition.id };
    setIdentityState("ready");
    setIdentityError("");
    setInputValue(name);
    setSearchText("");
    setResults([]);
    setOpen(false);
    setActiveIndex(-1);
    onChange(definition.id);
  };

  const handleKeyDown = (event: KeyboardEvent<HTMLInputElement>) => {
    if (event.key === "ArrowDown" && results.length > 0) {
      event.preventDefault();
      setOpen(true);
      setActiveIndex(current => Math.min(current + 1, results.length - 1));
    } else if (event.key === "ArrowUp" && results.length > 0) {
      event.preventDefault();
      setOpen(true);
      setActiveIndex(current => current <= 0 ? results.length - 1 : current - 1);
    } else if (event.key === "Enter") {
      event.preventDefault();
      if (open && activeDefinition) choose(activeDefinition);
    } else if (event.key === "Escape" && open) {
      event.preventDefault();
      setOpen(false);
      setActiveIndex(-1);
    }
  };

  const clearSelection = () => {
    setIdentityState("idle");
    setIdentityError("");
    setInputValue("");
    setSearchText("");
    setOpen(false);
    onChange("");
    inputRef.current?.focus();
  };

  return (
    <div className="wf-workflow-picker" ref={rootRef} onBlurCapture={event => {
      if (!event.relatedTarget || !rootRef.current?.contains(event.relatedTarget as Node)) {
        setOpen(false);
        setActiveIndex(-1);
      }
    }}>
      <label htmlFor={inputId}>Workflow</label>
      <div className="wf-workflow-picker-input">
        <input
          id={inputId}
          ref={inputRef}
          type="search"
          role="combobox"
          aria-label="Workflow"
          aria-autocomplete="list"
          aria-haspopup="listbox"
          aria-expanded={open && Boolean(searchText.trim())}
          aria-controls={open && searchText.trim() ? listboxId : undefined}
          aria-activedescendant={open ? activeOptionId : undefined}
          autoComplete="off"
          value={inputValue}
          placeholder="Search workflows by name"
          onFocus={() => { if (searchText.trim()) setOpen(true); }}
          onChange={event => {
            const value = event.target.value;
            inputEditGeneration.current++;
            setInputValue(value);
            setSearchText(value);
            setOpen(Boolean(value.trim()));
            setActiveIndex(-1);
          }}
          onKeyDown={handleKeyDown}
        />
        {definitionId ? <button type="button" aria-label="Clear workflow selection" onClick={clearSelection}>Clear</button> : null}
      </div>
      {definitionId ? (
        <CopyableIdentifier label="Workflow definition ID" value={definitionId} className="wf-workflow-picker-identity" />
      ) : null}
      {identityState === "loading" ? <div role="status">Loading selected workflow identity…</div> : null}
      {identityState === "failed" ? (
        <div className="wf-workflow-picker-error" role="alert">
          Could not resolve workflow {definitionId}: {identityError}. The exact selected ID is retained.
        </div>
      ) : null}
      {open && searchText.trim() ? (
        <div className="wf-workflow-picker-results">
          {searchState === "loading" ? <div role="status">Searching workflows…</div> : null}
          {searchState === "failed" ? (
            <div className="wf-workflow-picker-error" role="alert">
              Workflow search failed: {searchError}
              <button type="button" aria-label="Retry workflow search" onClick={() => setRetryVersion(value => value + 1)}>Retry</button>
            </div>
          ) : null}
          {searchState === "ready" && results.length === 0 ? <div role="status">No workflows found.</div> : null}
          <div id={listboxId} role="listbox" aria-label="Workflow search results" aria-busy={searchState === "loading"}>
            {results.map((definition, index) => (
              <div
                id={`${listboxId}-${index}`}
                key={definition.id}
                ref={element => { optionRefs.current[index] = element; }}
                role="option"
                aria-selected={index === activeIndex}
                className="wf-workflow-picker-option"
                onMouseDown={event => event.preventDefault()}
                onClick={() => choose(definition)}
              >
                <strong>{definition.name?.trim() || definition.id}</strong>
                <small>{definition.id}{definition.latestVersion ? ` · v${definition.latestVersion}` : ""}</small>
              </div>
            ))}
          </div>
          {canLoadMore ? (
            <button
              type="button"
              aria-label="Next workflow definitions"
              disabled={searchState === "loading"}
              onClick={() => void loadNextPage()}
            >Load more workflows</button>
          ) : null}
        </div>
      ) : null}
    </div>
  );
}
