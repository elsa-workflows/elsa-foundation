# Elsa.ActorFoundation.BuiltIn.PostgreSql

Opt-in PostgreSQL node composition and Generic Host lifetime for the Built-in Actor Foundation.

- `PostgresPreviewHosting.StartAsync` and `PostgresPreviewHostedService` run one node. On a store with held replies
  (the `ReplyProtocol` capability) they take `replies`, the transport the node answers held callers through, and refuse
  to start without it; on any other store they refuse it.
- `PostgresPreviewHosting.OpenFoundationAsync` opens the Foundation's ingress and reads (`PostgresPreviewFoundation`).
  With a `replyNode`, on a store with held replies, its `RequestAsync` holds the caller until the owner answers or the
  request's deadline passes by database time:
  - `Target.Start` starts a root whose start carries the caller's reply route, and the owner answers it as it answers a
    held direct input, after the commit or, with `ReleaseBeforeCommit`, when the turn ends;
  - `Target.Ref` sends to an exact root reference.

  A repeat of an operation reaches what the first one admitted and attaches its caller to nothing: it ends at its own
  deadline with a timeout and the root's status. `PostgresPreviewFoundation.Requests` is the transport for a node in the
  same process.
