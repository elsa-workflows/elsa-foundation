# Orleans Actor Foundation probe

This isolated executable composes `IIngress`, `IActorReads` and `IActorType` with Orleans 10.4.0. The existing WorkflowRuntime, WorkflowActor and Native execution model run an unchanged Sequence with Delay and WriteLine. The probe references the public Actor Foundation projects and has its own PostgreSQL checkpoint store.

Run with Docker from the repository root:

```bash
dotnet run --project tools/hosting/Orleans.ActorFoundation.Probe -c Release
```

The probe injects an interruption immediately before the checkpoint write and confirms that neither state nor a timer became visible. It then simulates a lost response after commit and confirms that the same start returns the original actor. After the first silo shuts down, a bounded dispatcher on the second silo reads the due timer and delivers it through Orleans. That silo commits completion, removes the timer and rejects its duplicate delivery.

The executable uses two in-process silos and disposable PostgreSQL. It exercises ordinary durable starts with Full history, one self-timer and direct actor/journal reads. The driver controls when it polls for timers. External messaging, jobs, operator actions and cross-silo crash recovery would require further implementation and qualification before a shipping host could select this foundation.
