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

- `PostgresPreviewHosting.CreateShippedContext` selects the shipped format: the format 17 operator chain, Native jobs
  with timed settlement, direct held replies, Park opening, inspection, arrivals and one-page Recover.
  `InstallPreviewRootSchemaAsync` also installs workflow history. The node takes `requests` for held replies.
  `PostgresPreviewHosting.CreateRequests` makes them before the store is opened, and the host hands the same requests to
  `OpenFoundationAsync` for its edge. A preview database on an earlier format is recreated before opening it with the
  current shipped profile.
- `PostgresPreviewHosting.OpenFoundationWithOperatorsAsync` lets a host authorize one-way operator commands for a
  bound scope and exact actor reference. Recover uses the selection from the Park read. A suspended Park refuses Recover
  and still accepts a selected Terminate.
- `PostgresPreviewHosting.InstallShippedAsync` readies a dedicated database for the shipped format in a single-process
  host (the Workbench calls it before its node starts): an empty database is installed, and one whose store marker is the
  shipped format is installed again, which completes a first install that was cut short and changes nothing in a whole
  one. Any other is refused, naming what it holds, with the advice to recreate it empty. Objects of PostgreSQL extensions
  do not count, and hosts starting at once install one after the other (an advisory lock). A multi-node host keeps
  installation an explicit setup step (`EfStore.CreateAsync` and `InstallPreviewRootSchemaAsync`), run while no node is.
- The shipped format takes self-sends, actor-owned Delay timers, and attached or detached child starts. Hosts read the
  selected profile through `TakesSelfSends`, `TakesTimers` and `TakesChildren`. Timer and Cron start triggers require a
  schedule-actor host.

The shipped format grows by composition. Each widening of its capability set takes a new protocol number, and a database
installed with an older set is refused. Before GA a preview database is recreated; after GA a widening migrates, with a
rollback floor (K12).

An engine advances its definitions revision when its installed functions, triggers, or stored framing change.
The shared format advances when the supported capabilities grow. Code refers to the current shared format through
`StoreFormat.RootShipped`.

## PostgreSQL operations

Install into a dedicated database and keep the owner, request ingress and reads on the same installed format. Run setup
before the owners start. Keep PostgreSQL's durability settings, including `fsync`, `synchronous_commit` and
`full_page_writes`, enabled. The installer leaves server and table tuning to the deployment.

After an owner exits, its open transaction can still hold the partition row after the lease expires. Check
`client_connection_check_interval` on the actual PostgreSQL owner session. The prototype's TLS fixture observed an
enabled connection check abort a blocked query after PostgreSQL detected the lost socket and release the row lock.
Measure network-partition recovery separately with the deployment's TCP keepalive settings.
[Owner-exit evidence](https://github.com/elsa-workflows/elsa-foundation/blob/549b116bbc44a5e3e8123d22953d7f810624fdc6/prototypes/kernel-model/ENGINES.md#postgresql-process-exit-while-holding-a-fence)

Start with default table settings. Under representative sustained and burst load, compare inbox backlog with WAL per
commit, checkpoint activity, dead tuples and HOT-update share. Watch the age of open transactions and replication slot
lag when vacuum falls behind. Tune the vacuum cadence or fill factor on a staging copy and measure the change in retained
size, tail latency and recovery time before carrying it into production. The earlier fill-factor measurement used a
prototype schema and short runs.
[Measurement method and tuning trade-offs](https://github.com/elsa-workflows/elsa-foundation/blob/549b116bbc44a5e3e8123d22953d7f810624fdc6/prototypes/kernel-model/POSTGRES.md#tuning-guidance)
