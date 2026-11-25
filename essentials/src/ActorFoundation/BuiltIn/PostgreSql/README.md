# Elsa.ActorFoundation.BuiltIn.PostgreSql

Opt-in PostgreSQL node composition and Generic Host lifetime for the Built-in Actor Foundation.

- `PostgresPreviewHosting.StartAsync` and `PostgresPreviewHostedService` run one node. On a store with held replies
  (the `ReplyProtocol` capability) they take `requests`, the held callers of a Foundation opened with that node as its
  reply node, and refuse to start without them, or with another node's; on any other store they refuse them.
- `PostgresPreviewHosting.OpenFoundationAsync` opens the Foundation's ingress and reads (`PostgresPreviewFoundation`).
  With held requests from `CreateRequests`, on a store with held replies, its `RequestAsync` holds the caller until the owner answers or the
  request's deadline passes by database time:
  - `Target.Start` starts a root whose start carries the caller's reply route, and the owner answers it as it answers a
    held direct input, after the commit or, with `ReleaseBeforeCommit`, when the turn ends;
  - `Target.Ref` sends to an exact root reference.

  A start the Foundation does not run (another actor type, an ephemeral spec, a payload) is answered "missing". A repeat
  of an operation reaches what the first one admitted and attaches its caller to nothing: it ends at its own deadline
  with a timeout and the root's status. The node answers through `PostgresPreviewFoundation.Requests`, in the order it
  sends, so a turn's response reaches its caller before the turn's end.

- `PostgresPreviewHosting.CreateShippedContext` selects the shipped format 25: the format 17 operator chain, Native jobs
  with timed settlement, direct held replies, Park opening and inspection, and workflow history (which
  `InstallPreviewRootSchemaAsync` installs with it). It holds replies, so a node on it takes `requests`:
  `PostgresPreviewHosting.CreateRequests` makes them before the store is opened, and the host hands the same requests to
  `OpenFoundationAsync` for its edge. The earlier preview formats (2, 6, 18) stay for their qualification checks, and a
  database on one of them is recreated for format 25, not migrated.
- Format 25 runs no self-sends, timers or child starts yet. Self-sends wait on S6's input-scope union with timed jobs,
  and child workflows on PostgreSQL roots are their own slice. So a kernel host refuses Delay and Dispatch Workflow at
  publish.

The shipped format grows by composition. Each widening of its capability set takes a new protocol number, and a database
installed with an older set is refused. Before GA a preview database is recreated; after GA a widening migrates, with a
rollback floor (K12).

A change to the shipped format's functions, triggers or stored operator framing takes a new number too, since a host
reinstalls them only when it creates the database. Format 25 is format 24 with a plain Delete of a parked root (S5b-3b):
admission accepts a Delete that names the root's Park, a clearing control may carry the Delete kind, and the checkpoint,
journal and outcome triggers accept a parked retirement that keeps the Faulted status and a protected Delete's discard.
A database installed as format 24 is refused and recreated. Code refers to the shipped format as
`StoreFormat.RootShipped`, not by its number.
