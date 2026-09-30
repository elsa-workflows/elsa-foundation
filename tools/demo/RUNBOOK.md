# Schema rollout demo: the presenter's runbook

The story: a module that keeps its data in a shared database is upgraded to a new release **while the host keeps running**. The
platform installs the release at runtime, refuses to switch to it until the database is migrated, says exactly what to run,
and only turns on the feature that needs the new schema once **every host** that shares the database can read it.

- **Act 1, one host, Sqlite (about 6 minutes).** Release 1.0.0 is running. Release 1.1.0 is published to the feed, the host
  installs it, refuses to switch and names the migration to apply. The operator applies it, the host switches, and the new
  `with-tags` endpoint works on the old notes.
- **Act 2, two hosts, PostgreSQL (about 8 minutes).** Two hosts share one database. Host B is upgraded first: its new feature
  stays dormant (409) because host A cannot read the new schema yet, and `persistence status` says so by name. Host A is
  upgraded in place, and the moment it can read the new schema the feature goes live on both hosts.

The whole thing is scripted in `tools/demo/rehearse.sh`. It runs this runbook end to end, and it uses the same helpers you type
(`tools/demo/helpers.sh`: `note`, `notes`, `withtags`, `tag`, `reload`, `status`, `waitfor`), so what it asserts is what you see: every status code
and every output line promised below, in setup and in both acts. The one thing it does not rehearse is `prepack.sh` (minutes long); it
requires that to have run. Run it before every presentation (see the last section).

## The cast

| | Host | Port | Database | Cluster id | Terminal tab | Log |
|---|---|---|---|---|---|---|
| Act 1 | `solo` | 5101 | Sqlite file `artifacts/demo/notes.db` | none | **S** | `artifacts/demo/logs/solo.log` |
| Act 2 | `a` | 5201 | PostgreSQL container `elsa-demo-pg` | `host-a` | **A** | `artifacts/demo/logs/a.log` |
| Act 2 | `b` | 5202 | the same PostgreSQL database | `host-b` | **B** | `artifacts/demo/logs/b.log` |

Two more tabs face the audience: **1** for the Act 1 commands and **2** for the Act 2 commands. Tabs **S**, **A** and **B** run the
hosts and stay off screen: their logs are long and are not written for an audience (see "Screen hygiene"). A sixth tab, **awake**, keeps
the laptop from sleeping.

Ports 5101, 5201 and 5202 and the container name `elsa-demo-pg` are the demo's. All commands run from the repository root.

Every `bash` block below is meant to be pasted into an interactive zsh: none contains a `#` comment or a backtick (zsh does not treat `#` as a
comment on the command line unless `interactive_comments` is set, so a pasted comment is run as a command and fails).

## Setup (before the audience, about 20 minutes, most of it waiting)

### S1. First, every time: reset

```bash
bash tools/demo/reset.sh
```

It stops leftover demo hosts, removes the `elsa-demo-pg` container and everything under `artifacts/demo` except the staged releases and the
closure feed, and prints what it removed. Nothing of an earlier run (a host, a container, a database) survives to confuse the demo.

- **Expect:** the last line is `demo state is clean`.
- **If it goes wrong:** `docker ps failed`: Docker is not running. Start Docker Desktop, wait until `docker info` answers, run `reset.sh` again. It exits 1 in that case even though
  it cleaned everything else, so a dead Docker is never mistaken for "no container".

### S2. The day before, and again after any code change: prepack

Everything slow happens here, not on stage: it builds the host and the `dotnet elsa` tool, warms the tool up, packs **both**
releases into `artifacts/demo/staging/`, and fills the closure feed the hosts resolve EF Core and the database engines from. On the day itself
it is needed only when the staged releases are missing (`ls artifacts/demo/staging/1 artifacts/demo/staging/2`).

```bash
bash tools/demo/prepack.sh
```

- **Expect:** the last lines are the two staged packages and `ready in N s`:
  `artifacts/demo/staging/1/Elsa.Samples.Nuplane.Notes.1.0.0.nupkg` and `artifacts/demo/staging/2/Elsa.Samples.Nuplane.Notes.1.1.0.nupkg`.
- **Time:** a few minutes on a quiet laptop; a quarter of an hour when other builds compete for the machine (measured: 16 min at a
  load average above 500). Never on stage. Check `uptime` first: a load average far above the core count means everything below is slow.
- **If it goes wrong:** the failing command's own output is printed. A missing `python3`, `rsync`, `jq` or `curl` is named by the scripts; install it and run again.
  A build that fails on a dirty checkout: `git status` and get back to the commit you mean to present.

### S3. Check the tools, and keep the laptop awake

```bash
command -v docker jq curl python3 rsync caffeinate
docker info >/dev/null && echo docker ok
docker image inspect postgres:16-alpine >/dev/null && echo image ok
```

- **Expect:** six paths, then `docker ok` and `image ok`. The image is used from the cache and nothing is pulled: with Wi-Fi off, an evicted image is a dead demo, so check it here.

Plug in the power adapter and open a new terminal tab, **awake**. Run this in it and leave it running until the demo is over:

```bash
caffeinate -dimsu
```

Ctrl-C ends it. A sleeping laptop stalls the hosts' heartbeats, which the other host reads as a crash. `caffeinate` cannot keep a laptop awake with its lid closed: keep the lid open
(see the troubleshooting table if it slept anyway).

### S4. Open the tabs

Six terminal tabs (1, 2, S, A, B, awake), all in the repository root, named as in the cast table. Paste this into **1** and **2** (it defines the helpers the acts use, and
it puts a bare `$ ` prompt on the screen instead of your user name, host name and folder; the audience never sees the paste):

```bash
source tools/demo/helpers.sh
DEMO_PROMPT=$PROMPT DEMO_RPROMPT=$RPROMPT
PROMPT='$ ' RPROMPT=''
```

To get the old prompt back: `PROMPT=$DEMO_PROMPT RPROMPT=$DEMO_RPROMPT`. A prompt theme that redraws the prompt by itself (powerlevel10k does) undoes the change: start those two tabs as
a plain shell instead, with `zsh -f`, and paste the three lines there.

The helpers, all typed from the repository root:

| Helper | What it does |
|---|---|
| `note PORT TEXT` | adds a note, prints it |
| `notes PORT` | lists the notes, one per line |
| `withtags PORT` | `GET /demo/notes/with-tags`: `HTTP` and the code, then the body |
| `tag PORT TAG` | tags the first note |
| `reload PORT` | `POST /_module-management/reload`: the code, then the answer, with paths shown relative to the repository |
| `status HOST` | `dotnet elsa persistence status` for host `solo` (Sqlite) or `a` or `b` (PostgreSQL, with the 2 s skew allowance the hosts' `--fast-membership` needs) |
| `waitfor PORT` | waits until the host on that port answers `with-tags` with anything but 404, and says so |
| `pgconn` | points this tab's `ELSA_EF_CONNECTION` at the PostgreSQL container (never printed) |

Tab **1** must not have a database connection in its environment (Act 1 uses the default Sqlite file):

```bash
unset ELSA_EF_CONNECTION
```

Tabs **2**, **A** and **B** get the PostgreSQL connection once the container runs (S6), with `pgconn`. The connection travels only in
`ELSA_EF_CONNECTION`; the scripts never print it or write it down.

Four browser tabs, each showing the raw response:

1. `http://127.0.0.1:5101/demo/notes/with-tags` (Act 1: blank 404, then the notes with `"tags":[]`)
2. `http://127.0.0.1:5201/demo/notes/with-tags` (Act 2, host A: blank 404, then the 409 reason, then the notes)
3. `http://127.0.0.1:5202/demo/notes/with-tags` (Act 2, host B: blank 404, then the 409 reason, then the notes)
4. `http://127.0.0.1:5101/demo/notes` (Act 1: the plain list)

### S5. Host `solo` (Sqlite), in tab 1, then start it in tab S

```bash
bash tools/demo/publish.sh 1 --host solo
bash tools/demo/run-host.sh solo --port 5101 --management-key-env DEMO_KEY --prepare-only
bash tools/demo/elsa.sh persistence apply --restore --host artifacts/demo/hosts/solo --environment Development --provider Sqlite --modules Samples.Notes
```

- **Expect:** `published Elsa.Samples.Nuplane.Notes.1.0.0.nupkg to artifacts/demo/hosts/solo/feed`, then
  `restore: 11 package(s) installed under ...`, then the table row `01  Samples.Notes  NotesSqliteDbContext ...`.
- **Time:** publish under a second; the apply 5 to 45 s (the first call after prepack is the fast end).

In tab **S**:

```bash
source tools/demo/helpers.sh
bash tools/demo/run-host.sh solo --port 5101 --management-key-env DEMO_KEY 2>&1 | tee artifacts/demo/logs/solo.log
```

Wait until it is ready, from tab 1: `curl -s -o /dev/null -w '%{http_code}\n' localhost:5101/health/ready` prints `200`
(10 to 15 s on a quiet laptop, up to 40 s under load). The host's log ends with `Now listening on: http://127.0.0.1:5101`.

### S6. The PostgreSQL container and hosts `a` and `b`, one after the other

In tab **2**:

```bash
docker run -d --name elsa-demo-pg -e POSTGRES_PASSWORD=demo -e POSTGRES_DB=elsa -p 127.0.0.1::5432 postgres:16-alpine
until docker exec elsa-demo-pg pg_isready -q -h 127.0.0.1 -U postgres -d elsa; do sleep 1; done; echo postgres up
pgconn
```

Docker picks a free port, so nothing on the laptop can collide with it. Only the cached image `postgres:16-alpine` is used; nothing is pulled.

Still in tab **2**:

```bash
bash tools/demo/publish.sh 1 --host a
bash tools/demo/publish.sh 1 --host b
bash tools/demo/run-host.sh a --port 5201 --provider PostgreSql --cluster host-a --fast-membership --management-key-env DEMO_KEY --prepare-only
bash tools/demo/elsa.sh persistence apply --restore --host artifacts/demo/hosts/a --environment Development --provider PostgreSql --modules Samples.Notes,Cluster.Membership
bash tools/demo/run-host.sh b --port 5202 --provider PostgreSql --cluster host-b --fast-membership --management-key-env DEMO_KEY --prepare-only
```

- **Expect:** the apply prints both modules, `Cluster.Membership` and `Samples.Notes`, one migration applied each. One `apply` creates the tables for both hosts: they share the database.

In tab **A** (starting both hosts in the same instant makes them race to create the cluster's identity row; see the troubleshooting table):

```bash
source tools/demo/helpers.sh
pgconn
bash tools/demo/run-host.sh a --port 5201 --provider PostgreSql --cluster host-a --fast-membership --management-key-env DEMO_KEY 2>&1 | tee artifacts/demo/logs/a.log
```

Then, only when host A is ready, in tab **B**. Host A is ready when `curl -s -o /dev/null -w '%{http_code}\n' localhost:5201/health/ready` says `200`, from tab 2:

```bash
source tools/demo/helpers.sh
pgconn
bash tools/demo/run-host.sh b --port 5202 --provider PostgreSql --cluster host-b --fast-membership --management-key-env DEMO_KEY 2>&1 | tee artifacts/demo/logs/b.log
```

`--fast-membership` shortens the cluster's timings for the demo: a 2 s heartbeat, a 10 s expiry and a 2 s skew allowance. Both hosts must
use it, and the `status` helper passes `--skew-allowance 00:00:02` to match.

- **Check before the audience arrives**, from tab 2 (both curl lines print `200`), then look at the fleet:

```bash
curl -s -o /dev/null -w '%{http_code}\n' localhost:5201/health/ready
curl -s -o /dev/null -w '%{http_code}\n' localhost:5202/health/ready
status a
```

  It shows `finalized at 1.0.0` and both `host-a: Active, live` and `host-b: Active, live`, each reading `1.0.0`.
- **Time:** the container 5 to 25 s; each host 10 to 15 s to be ready on a quiet laptop (25 to 50 s under load).

Setup is done. Open tab **1** full screen; the audience arrives.

---

## Act 1: one host, upgraded in place (Sqlite)

### 1.1 Release 1.0.0 is running: add and list notes

```bash
note 5101 "hello from release 1.0.0"
note 5101 "a second note"
notes 5101
withtags 5101
```

- **Audience sees:** two JSON notes created, the list, and `HTTP 404` for `with-tags`.
- **Say:** "This is a normal module in a running host: a table, two endpoints. Release 1.0.0 has no tags: that endpoint does not exist yet."
- **Expect:** each `note` prints `{"id":"...","text":"...","createdAt":"..."}`; `notes` prints two lines; `withtags` prints `HTTP 404` and nothing else.
- **Time:** under a second.
- **If it goes wrong:** nothing answers: the host is not up, look in tab S. `note` prints nothing: `curl -i localhost:5101/demo/notes` to see the status.

### 1.2 Show the change

```bash
bash tools/demo/show-change.sh | less -R
```

- **Audience sees:** a diff. Page with the space bar; `/V2 replaces` jumps to the two changed files; `q` leaves.
- **Say:** "Everything release 1.1.0 adds lives in one folder. A migration that adds one nullable column, a store that reads and
  writes tags, a feature with two new endpoints, and the module's declaration that its data moved from version 1.0.0 to 2.0.0, with an upcaster
  that reads old rows as 'no tags' and a rewriter that brings them up to date in the background." Land on the migration: "One `AddColumn`, nullable,
  so a host still running the old release keeps working against the migrated database. That matters in the second act."
- **Expect:** the first heading is `==== The V2 folder: this is the whole change ====`, the last is `==== The package version ====` with `<Version Condition="'$(DemoVersion)' == '2'">1.1.0</Version>`.
- **Time:** as long as you want; the script is instant.
- **If it goes wrong:** `less` missing: drop the `| less -R` and use `| head -40`.

### 1.3 Publish release 1.1.0 to the local feed

```bash
bash tools/demo/publish.sh 2 --host solo
```

- **Audience sees:** one line.
- **Say:** "That is publishing to the feed the host watches. Nothing is restarted."
- **Expect:** `published Elsa.Samples.Nuplane.Notes.1.1.0.nupkg to artifacts/demo/hosts/solo/feed`
- **Time:** under a second (the same step took 15 to 86 s when the package was built live under load, which is why it is staged).
- **If it goes wrong:** `Release 2 is not staged`: run `bash tools/demo/prepack.sh` (minutes; do it after the audience leaves, not now, and use the fallback below).
  `holds a copy of Elsa.Persistence.EntityFramework`: see the troubleshooting table.

### 1.4 The host installs it at runtime; `/reload` is refused with 409

Give the host about ten seconds to notice the new package and install it: talk, or show `notes 5101` still answering. Then:

```bash
reload 5101
```

- **Audience sees:** `HTTP 409` and the host's own explanation.
- **Say:** "The host found the release, installed it, and did **not** switch to it. The database is one migration behind, and the host refuses to
  migrate underneath the operator. It tells you the module, the migration, and the command to run. And it is still serving release 1.0.0:" then `notes 5101`.
- **Expect:**

  ```
  HTTP 409
  {
    "module": "Samples.Notes",
    "code": "pending-migrations",
    "pendingMigrations": [
      "…_AddTags"
    ],
    "command": "dotnet elsa persistence apply --host \"artifacts/demo/hosts/solo\" --modules Samples.Notes --provider Sqlite --connection-env ELSA_EF_CONNECTION"
  }
  ```
- **Time:** the host takes about 10 s (10 to 17 s under load) to install; the `reload` call itself 1 to 3 s.
- **If it goes wrong:** `HTTP 200` with `"features": 3`: the host has not seen the package yet; wait five seconds and `reload 5101` again. `4xx` other than 409:
  the host was started without `--management-key-env DEMO_KEY`, or the key in this tab differs (`echo $DEMO_KEY`). Never call the `/reconcile` endpoint (troubleshooting table).

### 1.5 Apply the migration

```bash
bash tools/demo/elsa.sh persistence apply --host artifacts/demo/hosts/solo --environment Development --provider Sqlite --modules Samples.Notes
```

- **Say:** "`dotnet elsa` is our command line, here run from source through a small wrapper. It is what the host just asked for: apply the pending migration, out of process, on purpose."
- **Expect:**

  ```
  provider: Sqlite   schema: (none)
  #   MODULE         CONTEXT               HISTORY TABLE                           APPLIED
  01  Samples.Notes  NotesSqliteDbContext  __EFMigrationsHistory_ElsaSamplesNotes  1
  ```
- **Time:** 2 to 5 s on a quiet laptop (5 to 20 s under load).
- **If it goes wrong:** `Samples.Notes` is not found: the tool reads what the host installed. Run it with `--restore` once, or wait until the host has reconciled its feed. A connection error on Sqlite:
  tab 1 has an `ELSA_EF_CONNECTION` set (`unset ELSA_EF_CONNECTION`).

### 1.6 `/reload` answers 200; `with-tags` works; old notes show `tags []`

```bash
reload 5101
```

Say the first sentence below, and only then, about two seconds later:

```bash
withtags 5101
tag 5101 demo
notes 5101
```

- **Audience sees:** `HTTP 200`, the same notes now with `"tags":[]`, then one note with its tag, and the original endpoint unchanged. In the browser, reload tab 1: blank 404 becomes the notes.
- **Say:** "The host switched to release 1.1.0 without a restart. The notes written by release 1.0.0 have no tags column value: the upcaster reads them as
  'no tags'. And the old endpoint carries on."
- **Expect:** `reload` prints `HTTP 200` and the four lines of JSON `{`, `"features": 4,`, `"reloaded": 1` and `}`; `withtags` prints `HTTP 200` and the notes with `"tags":[]`; `tag` prints the note with `"tags":["demo"]`.
- **Time:** the reload is under 3 s. Once the reload has answered, the new endpoint exists, so `withtags` answers 409 and then 200, never 404 again: the feature turns on when the host has
  re-evaluated the schema version, which it does every 2 s. A `withtags` right after the reload is sometimes `HTTP 409` (`schema-version-not-finalized`), for up to two seconds: that is why the first command is separate.
  If you see it, say "it is checking that this is safe", wait, and repeat.
- **If it goes wrong:** `HTTP 409` on `reload` again: the apply did not run (or ran against another database): repeat 1.5 and read its table. `withtags` still 409 after ten seconds:
  `status solo` says why.

### Act 1 fallback: stop, publish, start (use it when the in-place route misbehaves)

Same story, with a restart. In tab S press Ctrl-C (wait for the prompt), then in tab 1:

```bash
bash tools/demo/publish.sh 2 --host solo
```

Start the host again in tab S (the command of S5). It comes up but refuses the shell: `curl -s -o /dev/null -w '%{http_code}\n' localhost:5101/demo/notes` prints `500`, and
the host log says `EF module 'Samples.Notes' has pending migrations`. Then run 1.5. **The next real request activates the shell**:
`notes 5101` answers, and `withtags 5101` is `HTTP 200` (after a moment of 409, as in 1.6). `/health/ready` stays `503` until that first request; it does not activate a refused shell by itself.
If the package is not staged and there is no time: `bash tools/demo/pack.sh 2 --no-host --host solo` builds it live (15 to 86 s).

`bash tools/demo/rehearse.sh --act 1 --fallback` rehearses exactly this route.

---

## Act 2: two hosts, one database (PostgreSQL)

Switch to tab **2** (it holds `ELSA_EF_CONNECTION`). `docker ps --filter name=elsa-demo-pg` shows the container if anybody asks where the database is.

### 2.1 Both hosts on release 1.0.0

```bash
note 5201 "written on host A"
note 5202 "written on host B"
notes 5201
status a
```

- **Audience sees:** two notes, and both are listed from host A: one database. Then the fleet.
- **Say:** "Two hosts, one database, the same module. `status` is our `dotnet elsa persistence status`; it reads the cluster membership: every host says which schema versions it can read."
- **Expect:**

  ```
  SamplesNotes (Samples.Notes): finalized at 1.0.0; this host reads [1.0.0]
    complete from 1.0.0
  members: 2 in Cluster.Membership, judged at … with a skew allowance of 00:00:02
    host-a: Active, live, last heartbeat …
      SamplesNotes: reads 1.0.0
    host-b: Active, live, last heartbeat …
      SamplesNotes: reads 1.0.0
  ```
- **Time:** notes instant; `status` 3 to 12 s.
- **If it goes wrong:** only one member listed: the other host is not running, or joined a different database (its tab needs the same `ELSA_EF_CONNECTION`). A host `Active` but not `live`: its heartbeat is late, see "crash and restart" below.

### 2.2 Upgrade host B in place: refused, then apply

```bash
bash tools/demo/publish.sh 2 --host b
```

Wait about ten seconds, then:

```bash
reload 5202
bash tools/demo/elsa.sh persistence apply --host artifacts/demo/hosts/b --environment Development --provider PostgreSql --modules Samples.Notes,Cluster.Membership
reload 5202
```

- **Audience sees:** the same refusal as in Act 1 (`HTTP 409`, module, migration, apply command), the apply, then `HTTP 200`.
- **Say:** "Same drill on host B, but now the database is shared. Watch host A: it is untouched, and it is still on 1.0.0." (`note 5201 "A keeps writing"` if you want to show it.)
  "The migration only adds a nullable column, so A keeps working against it."
- **Expect:** `HTTP 409` with `"code": "pending-migrations"`; the apply prints `01  Cluster.Membership … 0` and `02  Samples.Notes … 1`; then `HTTP 200` and `{ "features": 4, "reloaded": 1 }`.
- **Time:** about 10 s for B to install, 2 s for the refusal, 3 to 15 s for the apply, 2 s for the reload.
- **If it goes wrong:** as in 1.4 and 1.5. The apply lists two modules because the database has two; this is right.

### 2.3 `with-tags` on host B is refused: 409

```bash
withtags 5202
note 5201 "host A, still on 1.0.0"
```

- **Audience sees:** `HTTP 409` and a reason; refresh browser tab 3 for the same reason in the raw response. Host A keeps taking writes.
- **Say:** "Host B runs release 1.1.0 and could serve tags, but host A cannot read them. If B wrote tags, A would meet rows it cannot understand. So the feature stays
  dormant, and the request is refused whole: nothing is half-saved."
- **Expect:**

  ```
  HTTP 409
  {"code":"schema-version-not-finalized","feature":"NotesWithTags","reason":"It becomes available once every host can read version '2.0.0' of schema family 'SamplesNotes'.","message":"Feature 'NotesWithTags' is dormant: ... The request was refused whole, and nothing it carried was saved."}
  ```
  and the `note` on A answers normally.
- **Time:** instant.
- **If it goes wrong:** `HTTP 200` here would mean 2.0.0 is already finalized: host A was upgraded earlier, or was stopped. `bash tools/demo/reset.sh` and rehearse again.

### 2.4 `persistence status` names the host it waits for

```bash
status b
```

- **Audience sees:** the pending version, the name of the blocker, and the fleet with what each host reads.
- **Say:** "It does not just say 'not yet': it says who it waits for and why. Host A reads only 1.0.0. Upgrade A, and it releases."
- **Expect:**

  ```
  SamplesNotes (Samples.Notes): finalized at 1.0.0; this host reads [1.0.0, 2.0.0]
    2.0.0: pending, held by nothing; waits for every counted member to read it
      waits for: host-a (reads 1.0.0)
    complete from 1.0.0
  members: 2 in Cluster.Membership, judged at … with a skew allowance of 00:00:02
    host-a: Active, live, last heartbeat …
      SamplesNotes: reads 1.0.0
    host-b: Active, live, last heartbeat …
      SamplesNotes: reads 1.0.0, 2.0.0
  ```
- **Time:** 3 to 12 s.
- **If it goes wrong:** no `waits for:` line: host A has already been upgraded. (The `status` helper always passes `--skew-allowance 00:00:02`; typing the long command by hand without it judges the members with the default 5 s, which hides the line.)

### 2.5 Upgrade host A in place

```bash
bash tools/demo/publish.sh 2 --host a
waitfor 5201
```

- **Audience sees:** one line, then a row of dots that ends when host A has switched. No restart, and nothing else to run: A's database was migrated by B's apply, so the host switches by itself.
  `waitfor` is the on-screen signal: it polls `with-tags` on A, which answers 404 while A still runs 1.0.0, and says `switched: with-tags answers HTTP 409 after N s` at the first answer
  that is not a 404. Refresh browser tab 2 meanwhile if you like: it too goes from the blank 404 to the 409 reason.
- **Say:** "Host A gets the same release the same way. Its database is already migrated, so there is no refusal: it installs, switches, and now reads 2.0.0 too. It never left the cluster."
- **Expect:** `published Elsa.Samples.Nuplane.Notes.1.1.0.nupkg to artifacts/demo/hosts/a/feed`, then `waiting for the host on port 5201 to switch....... switched: with-tags answers HTTP 409 after 11 s`
  (or `HTTP 200`, when the version finalized between two polls of the half second). About ten seconds after the publish host A's log says it reloaded its shell; A's tab shows nothing you need to look at.
- **Time:** 10 to 15 s until A has installed and switched (measured: 11.5 s under load).
- **If it goes wrong:** no `switched:` line after 40 s: is the file in the feed (`ls artifacts/demo/hosts/a/feed`)? Use the Act 2 fallback below. `waitfor` gives up after 180 s and says so; Ctrl-C ends it earlier.

**What actually happens, verified:** host A does **not** have to leave the fleet. When it unloads the 1.0.0 assembly, it publishes its readability report again
(now `1.0.0, 2.0.0`), the cluster counts every live member as able to read 2.0.0, and the version finalizes on its next evaluation (every 2 s), 0 to 3 s after A switched
(11 to 14 s after the publish in seven rehearsals, both hosts running the whole time). The same happens the other way round when a host is stopped: a host that leaves the cluster
stops being counted, which is why the older stop-publish-start route also finalizes, even before the restarted host is back.

### 2.6 The version finalizes: both hosts answer 200

```bash
withtags 5201
withtags 5202
status a
```

- **Audience sees:** `HTTP 200` on both, the notes written before the upgrade with `"tags":[]`. In the browser, reload tabs 2 and 3: 404 and the 409 reason are gone.
- **Say:** "The moment every host could read 2.0.0, the platform turned the feature on, everywhere, without anybody flipping anything. The old notes read as 'no tags' through the upcaster.
  In the background the host now rewrites the old rows into the new format; when none is left, the version is recorded complete."
- **Expect:** `HTTP 200` twice, then the status below. On host A the answers go **404** (A not switched yet, what `waitfor` waited out), then **409** (A has switched and is evaluating the schema version; the
  reason is the one from 2.3), then **200**: after `waitfor` you are already past the 404, so `withtags 5201` is a 409 for up to about two seconds or straight a 200. Host B has answered 409 since 2.2 and turns to 200 at the same moment.
  The same 409-then-200 follows every switch of a host to release 1.1.0 (after `reload` in 1.6, and after a restart in the fallbacks): if you see the 409, say "it is checking that this is safe", wait two seconds, repeat.

  ```
  SamplesNotes (Samples.Notes): finalized at 2.0.0; this host reads [1.0.0, 2.0.0]
    complete from 1.0.0        <- for about 20 s after finalization, then: complete from 2.0.0
  members: 2 in Cluster.Membership, ...
    host-a: Active, live, ...   SamplesNotes: reads 1.0.0, 2.0.0
    host-b: Active, live, ...   SamplesNotes: reads 1.0.0, 2.0.0
  ```
  Run `status` again after about 20 s (a good moment for questions): `complete from 2.0.0`.
- **Time:** finalization 0 to 3 s after A switched (repeat `withtags` if you get 409 for a moment); `complete from 2.0.0` 16 to 20 s after finalization (measured: 18 to 20 s; 30 to 34 s after the publish).
- **If it goes wrong:** still 409 after 30 s and `waits for: host-a` in `status a`: A has not switched (see 2.5). A `waits for:` line naming a host you did not start: a leftover member from an earlier rehearsal: `bash tools/demo/reset.sh` and set up again.

### Act 2 fallback: stop, publish, start host A

If A does not switch by itself: in tab **A** press Ctrl-C (a clean stop leaves the cluster at once), then in tab 2 `bash tools/demo/publish.sh 2 --host a`, then start A again in tab **A** with the same commands as in S6
(10 to 40 s; `pgconn` is needed again only in a tab that lost its environment). **`with-tags` on B answers 200 the moment A has left**, before A is back: the last host that cannot read 2.0.0 is gone, so the version is released. Say exactly that; it is the same rule seen from the other side.
A host that is killed instead of stopped stays counted until its membership expires: about 12 s with `--fast-membership`.

---

## After the demo, and between rehearsals

```bash
bash tools/demo/reset.sh
```

It stops the demo hosts it started (by the process ids `run-host.sh` recorded, and only a process that is still a demo host; a clean stop, killed only after 30 s),
removes the `elsa-demo-pg` container, and removes everything under `artifacts/demo` except the staged releases and the closure feed. It prints each host, container and
folder it stopped or removed. `bash tools/demo/reset.sh --all` removes the staged releases too: run `prepack.sh` again afterwards.

Nothing here uses `pkill`, `killall` or a process name.

## Rehearse it without an audience

Both acts, about 6 minutes; needs Docker and the staged releases:

```bash
bash tools/demo/rehearse.sh
```

Act 1 by its fallback route only:

```bash
bash tools/demo/rehearse.sh --act 1 --fallback
```

It begins with `reset.sh`, follows this runbook step by step (hosts started one after the other, in-place upgrades, no interaction), runs the very helpers of `tools/demo/helpers.sh` that you type,
asserts the status codes and output lines above, checks that nothing the audience sees shows the repository path or a home folder, prints the time of every step and the moments measured (install, finalization, completion), lists any spec or requirement numbers a host logged, and cleans up on exit,
success or failure. Its host logs are kept in `artifacts/demo-rehearsal/`. It uses the ports 5101, 5201 and 5202 (`DEMO_PORT_SOLO`, `DEMO_PORT_A`, `DEMO_PORT_B` change them): stop a live demo first.

## Troubleshooting

| Symptom | Cause | What to do |
|---|---|---|
| A host logs a **duplicate key** error at start, on the cluster's identity row | Two hosts started in the same instant both tried to create it; the loser logs the error and carries on (issue 2162) | Harmless if the host goes on to `Now listening`. Avoid it: start A, wait for `/health/ready` 200, then start B. If a host did not come up, Ctrl-C it and start it again |
| `/health/ready` says **503** after `apply`, and requests answer **500** | A host that was started already refused (the fallback route): the readiness probe does not activate a shell | Send a real request: `notes 5101`. It activates the shell; `/health/ready` follows |
| `reload` says **200** with `"features": 3` instead of 409 | The host has not installed 1.1.0 yet | Wait five seconds, repeat |
| `reload` answers **401**, **403** or **404** | Module management is off, or the key differs | The host must be started with `--management-key-env DEMO_KEY`; `echo $DEMO_KEY` in the tab must match the one in the host's tab |
| `POST /_module-management/reconcile` never answers | A known defect (issue 2159) | Never call it. The folder watcher reconciles by itself; `reload` is the only endpoint the demo uses |
| A host was **killed or crashed** (or the laptop slept: see the next row) and is restarted | Its membership row lingers until it expires: about 12 s with `--fast-membership` (up to 35 s with the defaults); `status` still shows it `live` meanwhile, and a version waits for it | Wait about 15 s, then start it. A host stopped with Ctrl-C leaves at once and needs no wait |
| The **laptop slept** or the lid was closed | The hosts' heartbeats stopped, so a host may have been dropped from the cluster (its membership expires after about 12 s), and a host that lapsed and rejoined has not adopted the finalized schema version yet. The audience would see a **409** on `withtags` whose reason reads `It becomes available once this host adopts version '2.0.0' ... until it has rejoined the cluster` (`NotYetAdopted`), or a fleet in `status` that lists a host that is not `live`, or a member missing | After waking wait about 15 s, then run `status a`. Both hosts `Active, live` and no odd `waits for:` line: carry on. If a host lapsed, restart both hosts one after the other: Ctrl-C in tab **A** (a clean stop leaves at once), start it again with the S6 commands, wait for `/health/ready` 200, then the same for tab **B**. Prevent it with `caffeinate -dimsu` (S3) and the lid open |
| **Docker**: `docker ps` or `docker exec` fails, `pgconn` says the container does not run, or hosts cannot reach PostgreSQL | The daemon is down, or the `postgres:16-alpine` image was evicted (and Wi-Fi is off, so it cannot be pulled), or Docker restarted and the container's dynamic port changed, so every tab's `ELSA_EF_CONNECTION` is stale | Start Docker, then `bash tools/demo/reset.sh` (it reports a dead daemon instead of saying "none") and redo the PostgreSQL setup (S6), the hosts included: `pgconn` in every tab that runs a host or a command |
| **Slow pack**: 15 to 86 s under load | `dotnet pack` competing for the CPU | Never pack on stage. `prepack.sh` beforehand; `publish.sh` is a file copy. `uptime`: a load average far above the core count makes everything slow, close other builds |
| `publish.sh` says the release is **not staged** | `prepack.sh` was not run, or `reset.sh --all` removed it | `bash tools/demo/prepack.sh` (minutes). Not on stage |
| **Port already in use** (`run-host.sh` says `Port 5201 is already in use`) | A host from an earlier run is still up, or something else listens | `lsof -nP -iTCP:5201 -sTCP:LISTEN`. A leftover demo host: `bash tools/demo/reset.sh`. Something else: stop it, or run the rehearsal with `DEMO_PORT_*` set |
| `docker run` says the **container name is in use** | An earlier rehearsal's container | `bash tools/demo/reset.sh` |
| `run-host.sh` says host `a` is **already running** | The same host name started twice | Stop it (Ctrl-C in its tab) or `bash tools/demo/reset.sh` |
| `publish.sh`, `pack.sh` or `run-host.sh` says the feed **holds a copy of `Elsa.Persistence.EntityFramework`** | An earlier pack put one in a feed; the host carries its own, and a second copy hides the module from `dotnet elsa persistence` (it then reports no `Samples.Notes`) | `rm artifacts/demo/hosts/NAME/feed/Elsa.Persistence.EntityFramework.*.nupkg` for that host, then repeat the step. Or start clean: `bash tools/demo/reset.sh` |
| `dotnet elsa persistence` lists **no `Samples.Notes`** | The tool reads what the host installed, and the host has not reconciled its feed yet | Add `--restore` on the first call (as in setup), or wait until the host is ready |
| `persistence status` shows `Cluster.Membership has migrations not applied` | The command lacks `--modules Samples.Notes,Cluster.Membership` | Use the `status` helper (it always passes both modules), not a hand-typed command |
| `status` shows no `waits for:` line while A is still on 1.0.0 | A hand-typed command lacks `--skew-allowance 00:00:02`, so members are judged with 5 s instead of the hosts' 2 s | Use the `status` helper, which passes it |
| Host takes over a minute to be ready | The machine is loaded | `uptime`; wait; start the presentation after `/health/ready` is 200 on all three hosts |

## Screen hygiene

Nothing the audience is meant to see carries an internal requirement number. The one exception found is a host log line, which is why the host tabs stay off screen:

- `Schema family SamplesNotes of EF module Samples.Notes is complete at 2.0.0: no row below it remains (spec 186, FR-014).` (host log, about 20 s after the version finalizes, on both acts)

The host logs are also very long (package resolution and catalog lines by the hundred per change); the answers in tab 1 and tab 2 tell the story. `rehearse.sh` reports these lines at
the end of every run, so a new one shows up there. No personal path or name is on the audience's screen: the `reload` helper shows the `command` of the 409 relative to the repository (`--host "artifacts/demo/hosts/solo"`), the
prompt of tabs 1 and 2 is a bare `$ ` (S4), and `rehearse.sh` fails when anything it runs for the audience prints the repository path or a home folder. The one command that does print the absolute path of the
checkout, with the user name, is `elsa.sh persistence apply --restore`, which is setup and stays off screen. The tab and window titles and the browser's history are yours to check.
