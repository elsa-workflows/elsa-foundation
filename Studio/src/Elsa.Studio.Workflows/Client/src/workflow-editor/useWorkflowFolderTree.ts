import { useCallback, useLayoutEffect, useRef, useState } from "react";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { listWorkflowFolders } from "../api/workflowDesign";
import type { WorkflowFolder } from "../workflowTypes";

export const workflowFolderRootKey = "root";

export interface WorkflowFolderTreeLoadOptions {
  append?: boolean;
  force?: boolean;
}

interface UseWorkflowFolderTreeOptions {
  context: StudioEndpointContext;
  unavailableMessage?: string;
  onPageLoaded?(items: WorkflowFolder[]): Promise<void> | void;
}

/**
 * Owns the paged folder projection shared by folder trees and pickers.
 *
 * A key has at most one ordinary request in flight. Forced refreshes supersede
 * their older request and reload every page already displayed for that key, so
 * a late response can never replace newer tree state.
 */
export function useWorkflowFolderTree({ context, unavailableMessage, onPageLoaded }: UseWorkflowFolderTreeOptions) {
  const [roots, setRoots] = useState<WorkflowFolder[]>([]);
  const [children, setChildren] = useState<Record<string, WorkflowFolder[]>>({});
  const [continuations, setContinuations] = useState<Record<string, string | null>>({});
  const [loadedKeys, setLoadedKeys] = useState<Set<string>>(() => new Set());
  const [loadingKeys, setLoadingKeys] = useState<Set<string>>(() => new Set());
  const [loadFailures, setLoadFailures] = useState<Record<string, string>>({});
  const inFlightGenerations = useRef<Record<string, number>>({});
  const loadedPageCounts = useRef<Record<string, number>>({});
  const nextLoadGeneration = useRef(0);
  const contextRef = useRef(context);
  const contextGeneration = useRef(0);

  useLayoutEffect(() => {
    contextRef.current = context;
    contextGeneration.current += 1;
    inFlightGenerations.current = {};
    loadedPageCounts.current = {};
    setRoots([]);
    setChildren({});
    setContinuations({});
    setLoadedKeys(new Set());
    setLoadingKeys(new Set());
    setLoadFailures({});
    return () => {
      contextGeneration.current += 1;
      inFlightGenerations.current = {};
    };
  }, [context]);

  const loadPage = useCallback(async (
    parentId?: string,
    continuationToken?: string | null,
    { append = false, force = false }: WorkflowFolderTreeLoadOptions = {}
  ) => {
    const key = parentId ?? workflowFolderRootKey;
    if (inFlightGenerations.current[key] && !force) return false;

    const generation = ++nextLoadGeneration.current;
    const requestContextGeneration = contextGeneration.current;
    const isCurrent = () =>
      contextRef.current === context &&
      contextGeneration.current === requestContextGeneration &&
      inFlightGenerations.current[key] === generation;

    inFlightGenerations.current[key] = generation;
    setLoadingKeys(current => new Set(current).add(key));
    setLoadFailures(current => {
      const next = { ...current };
      delete next[key];
      return next;
    });

    try {
      const pageCount = force ? loadedPageCounts.current[key] ?? 1 : 1;
      const items: WorkflowFolder[] = [];
      let nextToken = force ? undefined : continuationToken;
      let nextContinuationToken: string | null = null;
      let loadedPages = 0;

      for (let pageIndex = 0; pageIndex < pageCount; pageIndex += 1) {
        const page = await listWorkflowFolders(context, { parentId, continuationToken: nextToken });
        if (!isCurrent()) return false;
        if (!page) {
          if (unavailableMessage) setLoadFailures(current => ({ ...current, [key]: unavailableMessage }));
          return false;
        }
        items.push(...page.items);
        nextContinuationToken = page.nextContinuationToken;
        loadedPages += 1;
        if (!force || !nextContinuationToken) break;
        nextToken = nextContinuationToken;
      }

      await onPageLoaded?.(items);
      if (!isCurrent()) return false;

      if (parentId) {
        setChildren(current => ({
          ...current,
          [parentId]: append ? [...(current[parentId] ?? []), ...items] : items
        }));
      } else {
        setRoots(current => append ? [...current, ...items] : items);
      }
      loadedPageCounts.current[key] = append ? (loadedPageCounts.current[key] ?? 1) + loadedPages : loadedPages;
      setContinuations(current => ({ ...current, [key]: nextContinuationToken }));
      setLoadedKeys(current => new Set(current).add(key));
      return true;
    } catch (caught) {
      if (!isCurrent()) return false;
      setLoadFailures(current => ({
        ...current,
        [key]: caught instanceof Error ? caught.message : "Couldn't load folders."
      }));
      return false;
    } finally {
      if (isCurrent()) {
        delete inFlightGenerations.current[key];
        setLoadingKeys(current => {
          const next = new Set(current);
          next.delete(key);
          return next;
        });
      }
    }
  }, [context, onPageLoaded, unavailableMessage]);

  return { roots, children, continuations, loadedKeys, loadingKeys, loadFailures, loadPage };
}
