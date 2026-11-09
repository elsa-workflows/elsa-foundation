# Streaming protocol research

Pinned SSE experiment for [Elsa Agents M3 #3523](https://github.com/elsa-workflows/elsa-foundation/issues/3523).
Uses MAF 1.27.0's dependency graph, MEAI 10.10.1, Anthropic 12.32.0, Azure OpenAI 2.1.0 and
OpenAI 2.14.0. This experiment calls the actual `IChatClient.GetStreamingResponseAsync` adapters;
the production socket/native suites separately exercise MAF `RunStreamingAsync`.

```bash
bash tools/agents/research/streaming/run.sh --output /absolute/new/external/directory
bash tools/agents/research/streaming/run.sh --output /another/new/external/directory --drop-terminal-guard
```

The runner copies a complete locked graph to an external project with import barriers, requires
normal and locked restore plus a successful build, then retains reports, logs and hashes. The second
command must exit 1 at the missing-terminal counterexample, after successful setup. Fixtures inject
SSE HTTP responses through a handler with no socket fallback. Credentials and all result/reasoning
markers are synthetic; no live model, connection, inference charge or Elsa recovery proof is involved.
Reports keep signal names, content types, counts and booleans, not business/private reasoning values.

Observed boundaries:

- Both adapters can return normally after visible valid JSON with no terminal event. Enumerating to
  EOF alone cannot authorize result/session acceptance.
- Anthropic maps `pause_turn` and unknown stop reasons to coarse `Stop`. Its raw `message_delta`
  reason and an actual `message_stop` are needed. Missing stop, refusal and truncation are distinct.
- Azure's streaming refusal is present in `StreamingChatCompletionUpdate.RefusalUpdate`, without
  `ErrorContent`. The adapter must surface that signal before discarding provider representations.
- Anthropic emits private thinking as `TextReasoningContent`; it must never enter accepted history.
- With input11 at message start and only output7 in the final delta, the pinned Anthropic adapter
  emits null input and total7. Production retains the observed cumulative input count before the SDK
  overwrites it and computes total only when both counts are present. These are observed counts;
  they establish no hard input/total token limit or billable-cache accounting guarantee.

This fixture qualifies SDK signal projection only. Production tests own incremental visible bounds,
physical reservation/auth, cancellation/response loss, atomic history/result acceptance and native
continuation. It establishes no live provider schema enforcement or power-loss behavior.

Primary protocol/API references (installed pins remain the executable authority):
[Anthropic streaming](https://platform.claude.com/docs/en/build-with-claude/streaming),
[MEAI updates](https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.ai.chatresponseupdate),
[MAF streaming](https://learn.microsoft.com/en-us/dotnet/api/microsoft.agents.ai.chatclientagent.runstreamingasync).
