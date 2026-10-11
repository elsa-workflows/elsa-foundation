import React from "react";
import type { StudioAgentPromptStarterContribution } from "../../sdk";

export function AgentPromptStarters({
  prompts,
  disabled,
  onSelect
}: {
  prompts: StudioAgentPromptStarterContribution[];
  disabled?: boolean;
  onSelect(prompt: string, capabilityId?: string): void;
}) {
  if (prompts.length === 0) {
    return null;
  }

  return (
    <div className="agent-prompt-starters" aria-label="Suggested prompts">
      {prompts.map(prompt => (
        <button key={prompt.id} type="button" disabled={disabled} onClick={() => onSelect(prompt.prompt, prompt.requiredCapabilities?.[0])}>
          {prompt.label}
        </button>
      ))}
    </div>
  );
}
