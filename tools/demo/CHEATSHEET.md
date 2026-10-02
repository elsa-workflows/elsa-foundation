# Demo cheat sheet

Only the commands, in order, with what to expect. Why each step matters, the lines to say and the troubleshooting table are in
[RUNBOOK.md](RUNBOOK.md); the section numbers here are its sections.

Every tab works from the repository root of the demo checkout. A new terminal tab can open somewhere else: `cd` there first and
check with `pwd`, or a host starts from the wrong checkout.

## Before the audience

**Tab 1.** Clean slate, and check that the releases are staged:

```bash
bash tools/demo/reset.sh
ls artifacts/demo/staging/1 artifacts/demo/staging/2
docker info >/dev/null && echo docker ok
docker image inspect postgres:16-alpine >/dev/null && echo image ok
```

Expect `demo state is clean`, two `.nupkg` files in each folder (Notes and Notes.Activities), `docker ok`, `image ok`. A release missing: `bash tools/demo/prepack.sh` (minutes).

**Tab awake.** Leave it running; power in, lid open:

```bash
caffeinate -dimsu
```

**Tab 1.** Helpers, a bare prompt, then prepare host `solo` (Sqlite):

```bash
source tools/demo/helpers.sh
DEMO_PROMPT=$PROMPT DEMO_RPROMPT=$RPROMPT
PROMPT='$ ' RPROMPT=''
unset ELSA_EF_CONNECTION
bash tools/demo/publish.sh 1 --host solo
bash tools/demo/run-host.sh solo --port 5101 --management-key-env DEMO_KEY --prepare-only
bash tools/demo/elsa.sh persistence apply --restore --host artifacts/demo/hosts/solo --environment Development --provider Sqlite --modules Samples.Notes
```

**Tab S.** Start `solo`:

```bash
source tools/demo/helpers.sh
bash tools/demo/run-host.sh solo --port 5101 --management-key-env DEMO_KEY 2>&1 | tee artifacts/demo/logs/solo.log
```

**Tab 2.** Helpers, a bare prompt, the PostgreSQL container, then prepare hosts `a` and `b`:

```bash
source tools/demo/helpers.sh
DEMO_PROMPT=$PROMPT DEMO_RPROMPT=$RPROMPT
PROMPT='$ ' RPROMPT=''
docker run -d --name elsa-demo-pg -e POSTGRES_PASSWORD=demo -e POSTGRES_DB=elsa -p 127.0.0.1::5432 postgres:16-alpine
until docker exec elsa-demo-pg pg_isready -q -h 127.0.0.1 -U postgres -d elsa; do sleep 1; done; echo postgres up
pgconn
bash tools/demo/publish.sh 1 --host a
bash tools/demo/publish.sh 1 --host b
bash tools/demo/run-host.sh a --port 5201 --provider PostgreSql --cluster host-a --fast-membership --management-key-env DEMO_KEY --prepare-only
bash tools/demo/elsa.sh persistence apply --restore --host artifacts/demo/hosts/a --environment Development --provider PostgreSql --modules Samples.Notes,Cluster.Membership
bash tools/demo/run-host.sh b --port 5202 --provider PostgreSql --cluster host-b --fast-membership --management-key-env DEMO_KEY --prepare-only
```

**Tab A.** Start `a`:

```bash
source tools/demo/helpers.sh
pgconn
bash tools/demo/run-host.sh a --port 5201 --provider PostgreSql --cluster host-a --fast-membership --management-key-env DEMO_KEY 2>&1 | tee artifacts/demo/logs/a.log
```

**Tab B.** Only once `a` is ready (the 5201 line below prints `200`). Start `b`:

```bash
source tools/demo/helpers.sh
pgconn
bash tools/demo/run-host.sh b --port 5202 --provider PostgreSql --cluster host-b --fast-membership --management-key-env DEMO_KEY 2>&1 | tee artifacts/demo/logs/b.log
```

**Tab 3.** Helpers, a bare prompt, no database connection, then prepare the Workbench `wb`:

```bash
source tools/demo/helpers.sh
DEMO_PROMPT=$PROMPT DEMO_RPROMPT=$RPROMPT
PROMPT='$ ' RPROMPT=''
unset ELSA_EF_CONNECTION
bash tools/demo/publish.sh 1 --host wb
bash tools/demo/run-workbench.sh wb --port 5301 --management-key-env DEMO_KEY --prepare-only
bash tools/demo/elsa.sh persistence apply --restore --host artifacts/demo/hosts/wb --environment Development --provider Sqlite --from-host
```

Expect two `published` lines, then thirteen modules applied, `Samples.Notes` among them.

**Tab W.** Start `wb`:

```bash
source tools/demo/helpers.sh
bash tools/demo/run-workbench.sh wb --port 5301 --management-key-env DEMO_KEY 2>&1 | tee artifacts/demo/logs/wb.log
```

**Tab Studio.** Start Studio:

```bash
bash tools/demo/run-studio.sh 2>&1 | tee artifacts/demo/logs/studio.log
```

**Tab 2.** All four hosts ready, and the fleet:

```bash
curl -s -o /dev/null -w '%{http_code}\n' localhost:5101/health/ready
curl -s -o /dev/null -w '%{http_code}\n' localhost:5201/health/ready
curl -s -o /dev/null -w '%{http_code}\n' localhost:5202/health/ready
curl -s -o /dev/null -w '%{http_code}\n' localhost:5301/health/ready
status a
```

Expect `200` four times, then `finalized at 1.0.0` with `host-a` and `host-b` both `Active, live`.

**Browser.** Open `http://localhost:5302`, sign in on the Workbench's page with its development admin (`SeedAdminUserName` and
`SeedAdminPassword` in `artifacts/demo/hosts/wb/shells.json`: read them off screen), open **Workflows**. Leave the tab there.
Sign in before the audience arrives. Collapse Studio's bottom panel (Console / Structured Logs, chevron at its right edge): it shows
the checkout's absolute path with your user name.

**Tab P**, beside tab 1. The package board:

```bash
bash tools/demo/board.sh solo --watch
```

Expect one row: `solo  1.0.0  1.0.0  1.0.0`. Open tab 1 full screen with the board beside it.

## Act 1: tab 1

**1.1** The running release:

```bash
note 5101 "hello from release 1.0.0"
note 5101 "a second note"
notes 5101
withtags 5101
rows solo
```

Expect `HTTP 404` for `withtags`; `rows` shows both notes at `1.0.0` with `(no column yet)`.

**1.2** The change (space pages, `q` leaves):

```bash
bash tools/demo/show-change.sh | less -R
```

**1.2b** The package:

```bash
bash tools/demo/show-package.sh 2
```

**1.3** Publish release 1.1.0:

```bash
bash tools/demo/publish.sh 2 --host solo
```

Board: the feed gets `1.1.0`; about ten seconds later `installed` is `1.1.0`, `serving` stays `1.0.0`.

**1.4** Once the board shows it installed:

```bash
reload 5101
```

Expect `HTTP 409` with `pending-migrations`. `HTTP 200` with `"features": 3`: not installed yet, wait five seconds and repeat.

**1.5** Apply the migration:

```bash
bash tools/demo/elsa.sh persistence apply --host artifacts/demo/hosts/solo --environment Development --provider Sqlite --modules Samples.Notes
```

Expect the row `01  Samples.Notes  NotesSqliteDbContext ... 1`. Never paste the command out of the 409.

**1.6** Switch:

```bash
reload 5101
```

Expect `HTTP 200`. Two seconds later:

```bash
withtags 5101
tag 5101 demo
notes 5101
```

Expect `HTTP 200` with `"tags":[]`, then the tagged note. A `409` for a moment: repeat `withtags`.

**Act 1 fallback**, if the in-place route misbehaves: Ctrl-C in tab S, `bash tools/demo/publish.sh 2 --host solo` in tab 1 if not done yet,
start tab S again with its command, run 1.5, then `notes 5101` (the first request activates the host).

## Act 2: tab 2

**Tab P.** Ctrl-C, then:

```bash
bash tools/demo/board.sh a b --watch
```

**2.1** Both hosts on 1.0.0:

```bash
note 5201 "written on host A"
note 5202 "written on host B"
notes 5201
status a
```

Expect both notes listed from host A; `finalized at 1.0.0`, both hosts `reads 1.0.0`.

**2.2** Upgrade host B:

```bash
bash tools/demo/publish.sh 2 --host b
```

Once the board shows `b` installed `1.1.0`:

```bash
reload 5202
bash tools/demo/elsa.sh persistence apply --host artifacts/demo/hosts/b --environment Development --provider PostgreSql --modules Samples.Notes,Cluster.Membership
reload 5202
```

Expect `HTTP 409`, the apply table (`Cluster.Membership ... 0`, `Samples.Notes ... 1`), then `HTTP 200`.

**2.3** B's new feature is dormant:

```bash
withtags 5202
note 5201 "written by A on release 1.0.0"
note 5202 "written by B on release 1.1.0"
rows a
```

Expect `HTTP 409` with `schema-version-not-finalized`; `rows` shows every note at `1.0.0` with `NULL`.

**2.4** Who it waits for:

```bash
status b
```

Expect `waits for: host-a (reads 1.0.0)`.

**2.5** Upgrade host A:

```bash
bash tools/demo/publish.sh 2 --host a
waitfor 5201
```

Expect `switched: with-tags answers HTTP 200 after N s` (or `HTTP 409`; both are right).

**2.6** Live on both hosts:

```bash
withtags 5201
withtags 5202
status a
note 5202 "written after finalization on B"
note 5201 "written after finalization on A"
rows a
```

Expect `HTTP 200` twice (a `409` on B: wait two seconds, repeat) and `finalized at 2.0.0`; the two new notes are at `2.0.0`.

**2.7** About twenty seconds later:

```bash
status a
rows a
```

Expect `complete from 2.0.0`, and every note at `2.0.0` with `[]`.

**2.8** Read the recap from RUNBOOK.md, section 2.8.

**Act 2 fallback**, if A does not switch: Ctrl-C in tab A, `bash tools/demo/publish.sh 2 --host a` in tab 2 if not done yet, start tab A
again with its command. B answers `200` as soon as A has left.

## Act 3: the browser (Studio) and tab 3

**Tab P.** Ctrl-C, then:

```bash
bash tools/demo/board.sh wb --watch
```

**3.1** In Studio: **Workflows**, **Create**, name `Add note demo`. Palette, **Notes**: drag **Add note**. Text `hello from the designer`. **Run**. Then:

```bash
rows wb
```

Expect the note at `1.0.0` with `(no column yet)`. A designer run takes a few seconds to store its note: on `(no notes yet)`, repeat `rows wb`.

**3.2** Publish release 1.1.0, both packages:

```bash
bash tools/demo/publish.sh 2 --host wb
```

Board: the feed gets `1.1.0`; about ten seconds later `installed` is `1.1.0`, `serving` stays `1.0.0`.

**3.3** Once the board shows it installed:

```bash
wbreload 5301
```

Expect `HTTP 200`, `"reloaded": false`, `"module": "Samples.Notes"`, the `..._AddTags` migration. `"reloaded": true` with `"serving": "Notes 1.0.0"`: too early, wait and repeat.

**3.4** Apply, reload:

```bash
bash tools/demo/elsa.sh persistence apply --host artifacts/demo/hosts/wb --environment Development --provider Sqlite --modules Samples.Notes
wbreload 5301
```

Expect `01  Samples.Notes ... 1`, then `"reloaded": true` and `"serving": "Notes 1.1.0"`.

**3.5** In Studio: refresh the page. Select the Add note node, **Version** tab, **Change exact version**. The dialog: `v1.0.0` to `v1.1.0`,
**Compatible**, **Minor**, "Input 'Tags' was added". Scroll to the bottom, **Apply to this occurrence**. Inputs: Text and Tags.
Ignore "Recommended v1.0.0 available"; never drag a new node for this.

**3.6** Text `tagged from the designer`, Tags `demo, designer`, **Run**. Then:

```bash
rows wb
```

Expect the new note at `2.0.0` with `["demo","designer"]`, the 3.1 note at `2.0.0` with `[]`. A designer run takes a few seconds to store its note: if it is not there yet, repeat `rows wb`. A run that faults as dormant: run again.

**Act 3 fallback**, if the designer misbehaves: in tab 3, `addnote 5301 1.0.0 "hello from the API"` instead of 3.1, and
`addnote 5301 1.1.0 "tagged from the API" "demo, api"` instead of 3.5 and 3.6, each followed by `rows wb`.

## Afterwards

Ctrl-C in tabs P, S, A, B, W, Studio and awake, then in tab 1:

```bash
bash tools/demo/reset.sh
```

Never call `POST /_module-management/reconcile`: it does not answer on this release. `reload` is the only management call (`wbreload` on the Workbench).
