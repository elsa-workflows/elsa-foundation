import type { StudioAgentRegistry, StudioAiPromptActionContribution } from "@elsa-workflows/studio-sdk";

export function pageItems<T>(items: T[], page: number, pageSize: number) {
  return items.slice((page - 1) * pageSize, page * pageSize);
}

export function getTotalPages(totalCount: number, pageSize: number) {
  return Math.max(1, Math.ceil(totalCount / pageSize));
}

export function findAiAction(ai: StudioAgentRegistry, id: string) {
  return ai.promptActions.list().find(action => action.id === id) ?? null;
}

export function dispatchAiAction<TContext>(ai: StudioAgentRegistry, action: StudioAiPromptActionContribution<TContext>, context: TContext) {
  const prompt = action.createPrompt(context);
  if (!prompt) return false;

  ai.dispatchPrompt(prompt);
  return true;
}
