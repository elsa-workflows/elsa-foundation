# Agent execution-model spike

This is a deterministic fit test for the proposed agent model, not an agent framework, an LLM integration, or product code. It runs as one of the existing `WorkflowInstance` models with the real durable worker and in-memory store. It introduces no kernel guarantee or default. The conversation is a toy checkpoint, not a Microsoft Agent Framework thread. Its peer and approval key are fixed for the one demonstrated conversation; it is not a multi-tenant or concurrent approval design.

The coordinator checkpoints a user message, emits a live token, and sends itself a continuation. It starts a fake tool as a background job. On its result it records one tool entry, subscribes for human approval and asks a second agent instance to review by message. The first human decision wins: approval and the peer reply finish the conversation; a denial cancels it. A cancel while the job runs cancels its ticket. No external API or credentials are used.

Run from the repository root:

```sh
dotnet run --project prototypes/kernel-model/models/Agent.Run/Kernel.Agent.Run.csproj
```

The runner executes its Worker phases and continuations on one exclusive owner scheduler while tool bodies run on the default scheduler. It waits for the actual cancelled tool task before allowing the owner continuation to discard its result.

The runner kills the first owner logically by ceasing to pump it and expiring its lease after the checkpoint and an outside tool effect. The replacement repeats that effect and selects one result. Another lease expiry tests that the approval subscription survives a fresh owner; live output is observed on the first owner's transport and is not replayed from storage. The original tool result is released late; the test observes its completed body reach the old worker's post-job lease check through the supplied clock callback, confirms it is not queued for a model turn, then runs the owner and observes it stand down, and compares the entire selected row, journal and partition position. This follows the integrated local expiry guard; the earlier unguarded runner reached a turn and lost its commit fence. A separate journey waits for a held tool to start, observes its cancellation token, removes the ticket, then completes the non-cooperative tool and checks the terminal row and ticket. A queued approval then denial tests first-decision behavior before the peer reply. This is an in-process, deterministic crash simulation; it does not prove a real process kill, a production store, external effects, delivery of live output, or agent-framework interoperability. In particular tool effects require idempotency when repetition matters.

Two deliberately faulty options are negative controls; both commands must exit nonzero:

```sh
dotnet run --no-build --project prototypes/kernel-model/models/Agent.Run/Kernel.Agent.Run.csproj -- --mutate-result
dotnet run --no-build --project prototypes/kernel-model/models/Agent.Run/Kernel.Agent.Run.csproj -- --mutate-live
```

They respectively apply a result twice and put live output into the checkpoint. They are isolated to this spike, not kernel settings.
