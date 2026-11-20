import React from "react";
import type { StudioWeaverPromptStarterContribution } from "../../sdk";

export function WeaverPromptStarters({
  prompts,
  disabled,
  onSelect
}: {
  prompts: StudioWeaverPromptStarterContribution[];
  disabled?: boolean;
  onSelect(prompt: string, capabilityId?: string): void;
}) {
  if (prompts.length === 0) {
    return null;
  }

  return (
    <div className="weaver-prompt-starters" aria-label="Suggested prompts">
      {prompts.map(prompt => (
        <button key={prompt.id} type="button" disabled={disabled} onClick={() => onSelect(prompt.prompt, prompt.requiredCapabilities?.[0])}>
          {prompt.label}
        </button>
      ))}
    </div>
  );
}
