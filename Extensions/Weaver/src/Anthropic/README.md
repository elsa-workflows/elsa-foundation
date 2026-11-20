# Anthropic Weaver provider

`Elsa.Weaver.Anthropic` binds Elsa's provider-neutral Weaver contracts to the Anthropic API through
`Microsoft.Extensions.AI`. It registers provider ID `anthropic` and implements the host-driven step loop: the Elsa
turn orchestrator owns the loop, the history and every tool dispatch, and the provider maps one step to one model
request.

Studio and module code call only the provider-neutral `/_elsa/weaver/*` endpoints.

## Configuration

The `WeaverAnthropic` feature registers the provider. It stays unavailable until it is enabled and has an API key.

```json
{
  "Elsa": {
    "Weaver": {
      "Anthropic": {
        "Enabled": true,
        "ApiKeyEnvironmentVariable": "ANTHROPIC_API_KEY",
        "Model": "claude-opus-4-8"
      }
    }
  }
}
```

Supported settings: `Enabled`, `ApiKey`, `ApiKeyEnvironmentVariable`, `Model`, `SystemMessage`, `MaxOutputTokens` and
`IncludeSensitiveContextContent`. Only one Weaver provider can be enabled at a time.

## Conversation state

The provider is stateless. Each step rebuilds the model input from Elsa's recorded history; no provider conversation,
runtime object or hidden reasoning is stored, by Elsa or by this provider.

## After an interrupted turn

Elsa's transcript keeps everything that happened, but a provider request includes only the accepted conversation:

- A prompt that was Cancelled is left out of later requests. That covers a turn interrupted by a host restart
  (reason `interrupted`), a prompt the owner withdrew before it started (`withdrawn`) and a turn the user stopped.
- The Failed partial assistant text of such a turn is left out as well.
- Tool receipts that the interrupted turn recorded stay in. They are real accepted outcomes.

A paused turn that waits for review is not interrupted. With a durable record store configured, it continues once
after a restart, from its recorded transcript, and reports the recorded receipt without executing the operation again.
The default in-memory store lasts only as long as the process.
