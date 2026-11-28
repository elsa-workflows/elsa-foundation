import { useCallback, useLayoutEffect, useRef, useState } from "react";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { listWorkflowFolders } from "../api/workflowDesign";
import type { WorkflowFolder } from "../workflowTypes";

export const workflowFolderRootKey = "tree:root";

export function workflowFolderTreeKey(parentId?: string) {
  return parentId === undefined ? workflowFolderRootKey : `tree:folder:${parentId}`;
}

export interface WorkflowFolderTreeLoadOptions {
  append?: boolean;
  force?: boolean;
}

interface UseWorkflowFolderTreeOptions {
  context: StudioEndpointContext;
  unavailableMessage?: string;
  onPageLoaded?(items: WorkflowFolder[], parentId?: string): Promise<void> | void;
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
  const [children, setChildren] = useState<Map<string, WorkflowFolder[]>>(() => new Map());
  const [continuations, setContinuations] = useState<Map<string, string | null>>(() => new Map());
  const [loadedKeys, setLoadedKeys] = useState<Set<string>>(() => new Set());
  const [loadingKeys, setLoadingKeys] = useState<Set<string>>(() => new Set());
  const [loadFailures, setLoadFailures] = useState<Map<string, string>>(() => new Map());
  const inFlightGenerations = useRef<Map<string, number>>(new Map());
  const loadedPageCounts = useRef<Map<string, number>>(new Map());
  const nextLoadGeneration = useRef(0);
  const contextRef = useRef(context);
  const contextGeneration = useRef(0);

  useLayoutEffect(() => {
    contextRef.current = context;
    contextGeneration.current += 1;
    inFlightGenerations.current = new Map();
    loadedPageCounts.current = new Map();
    setRoots([]);
    setChildren(new Map());
    setContinuations(new Map());
    setLoadedKeys(new Set());
    setLoadingKeys(new Set());
    setLoadFailures(new Map());
    return () => {
      contextGeneration.current += 1;
      inFlightGenerations.current = new Map();
    };
  }, [context]);

  const invalidatePages = useCallback((parentIds: Iterable<string | undefined>) => {
    const entries = Array.from(parentIds, parentId => ({
      parentId,
      key: workflowFolderTreeKey(parentId)
    }));
    if (entries.length === 0) return;

    for (const { key } of entries) {
      inFlightGenerations.current.delete(key);
      loadedPageCounts.current.delete(key);
    }

    if (entries.some(({ parentId }) => parentId === undefined)) setRoots([]);
    setChildren(current => {
      const next = new Map(current);
      for (const { parentId } of entries) {
        if (parentId !== undefined) next.delete(parentId);
      }
      return next;
    });
    setContinuations(current => {
      const next = new Map(current);
      for (const { key } of entries) next.delete(key);
      return next;
    });
    setLoadedKeys(current => {
      const next = new Set(current);
      for (const { parentId } of entries) {
        if (parentId !== undefined) next.delete(parentId);
      }
      return next;
    });
    setLoadingKeys(current => {
      const next = new Set(current);
      for (const { key } of entries) next.delete(key);
      return next;
    });
    setLoadFailures(current => {
      const next = new Map(current);
      for (const { key } of entries) next.delete(key);
      return next;
    });
  }, []);

  const loadPage = useCallback(async (
    parentId?: string,
    continuationToken?: string | null,
    { append = false, force = false }: WorkflowFolderTreeLoadOptions = {}
  ) => {
    const key = workflowFolderTreeKey(parentId);
    if (inFlightGenerations.current.has(key) && !force) return false;

    const generation = ++nextLoadGeneration.current;
    const requestContextGeneration = contextGeneration.current;
    const isCurrent = () =>
      contextRef.current === context &&
      contextGeneration.current === requestContextGeneration &&
      inFlightGenerations.current.get(key) === generation;

    inFlightGenerations.current.set(key, generation);
    setLoadingKeys(current => new Set(current).add(key));
    setLoadFailures(current => {
      const next = new Map(current);
      next.delete(key);
      return next;
    });

    try {
      const pageCount = force ? loadedPageCounts.current.get(key) ?? 1 : 1;
      const items: WorkflowFolder[] = [];
      let nextToken = force ? undefined : continuationToken;
      let nextContinuationToken: string | null = null;
      let loadedPages = 0;

      for (let pageIndex = 0; pageIndex < pageCount; pageIndex += 1) {
        const page = await listWorkflowFolders(context, { parentId, continuationToken: nextToken });
        if (!isCurrent()) return false;
        if (!page) {
          if (unavailableMessage) {
            setLoadFailures(current => new Map(current).set(key, unavailableMessage));
          }
          return false;
        }
        items.push(...page.items);
        nextContinuationToken = page.nextContinuationToken;
        loadedPages += 1;
        if (!force || !nextContinuationToken) break;
        nextToken = nextContinuationToken;
      }

      await onPageLoaded?.(items, parentId);
      if (!isCurrent()) return false;

      if (parentId !== undefined) {
        setChildren(current => new Map(current).set(
          parentId,
          append ? [...(current.get(parentId) ?? []), ...items] : items
        ));
      } else {
        setRoots(current => append ? [...current, ...items] : items);
      }
      loadedPageCounts.current.set(key, append ? (loadedPageCounts.current.get(key) ?? 1) + loadedPages : loadedPages);
      setContinuations(current => new Map(current).set(key, nextContinuationToken));
      if (parentId !== undefined) setLoadedKeys(current => new Set(current).add(parentId));
      return true;
    } catch (caught) {
      if (!isCurrent()) return false;
      setLoadFailures(current => new Map(current).set(
        key,
        caught instanceof Error ? caught.message : "Couldn't load folders."
      ));
      return false;
    } finally {
      if (isCurrent()) {
        inFlightGenerations.current.delete(key);
        setLoadingKeys(current => {
          const next = new Set(current);
          next.delete(key);
          return next;
        });
      }
    }
  }, [context, onPageLoaded, unavailableMessage]);

  return { roots, children, continuations, loadedKeys, loadingKeys, loadFailures, loadPage, invalidatePages };
}
