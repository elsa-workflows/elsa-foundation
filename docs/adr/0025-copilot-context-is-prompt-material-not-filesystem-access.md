# Copilot context is prompt material not filesystem access

Elsa passes sanitized agent context to the GitHub Copilot provider as provider-neutral prompt material, not as Copilot SDK file attachments or direct server filesystem paths. This keeps workflow-authoring context under Elsa's sanitization and sensitivity rules while deferring richer attachment support until Elsa has a provider-neutral attachment contract that can describe safe content transfer without exposing backend paths.
