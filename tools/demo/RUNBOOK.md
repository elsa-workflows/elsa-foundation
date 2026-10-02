# Schema rollout demo: the presenter's runbook

On stage, [CHEATSHEET.md](CHEATSHEET.md) has only the commands, in order, with what to expect; this runbook explains them.

The story: a module that keeps its data in a shared database is upgraded to a new release **while the host keeps running**. The
platform installs the release at runtime, refuses to switch to it until the database is migrated, says exactly what to run,
and only turns on the feature that needs the new schema once **every host** that shares the database can read it.

- **Act 1, one host, Sqlite (about 6 minutes).** Release 1.0.0 is running. Release 1.1.0 is published to the feed, the host
  installs it, refuses to switch and names the migration to apply. The operator applies it, the host switches, and the new
  `with-tags` endpoint works on the old notes.
- **Act 2, two hosts, PostgreSQL (about 8 minutes).** Two hosts share one database. Host B is upgraded first: its new feature
  stays dormant (409) because host A cannot read the new schema yet, and `persistence status` says so by name. Host A is
  upgraded in place, and the moment it can read the new schema the feature goes live on both hosts.
- **Act 3, the same upgrade from the designer (about 7 minutes).** The Workbench, the host that runs workflows, has the Notes
  module too, with an **Add note** activity released in step with it. In Studio, the workflow designer, a workflow adds a note
  with Add note 1.0.0. Release 1.1.0 is published, the Workbench installs it, refuses to switch until the migration is applied,
  then switches without a restart; in the designer the node is moved to the exact version 1.1.0, which has a Tags input.

The whole thing is scripted in `tools/demo/rehearse.sh`. It runs this runbook end to end, and it uses the same helpers you type
(`tools/demo/helpers.sh`: `note`, `notes`, `withtags`, `tag`, `reload`, `status`, `waitfor`, `rows`, `wbreload`, `addnote`), so what it asserts is what you see: the status codes
and the output lines the **Expect** items below promise, in setup and in all three acts, and the cells of the package board at the steps that name them. The one thing it does not rehearse is `prepack.sh` (minutes long); it
requires that to have run. Run it before every presentation (see the last section). It does not drive Studio either: what you do in
the designer in Act 3 it does through the same API calls Studio makes (`addnote`, the Act 3 fallback), so Studio's own screens are yours to check by eye.

The demo also answers the three questions the customer asked at the demo of 2026-09-21: see "Your three questions from last time" below. Each act carries a
boxed line to say at the step that answers a question, Act 2 ends with a recap, and Act 3 shows two of the answers again from the designer.

## The cast

| | Host | Port | Database | Cluster id | Terminal tab | Log |
|---|---|---|---|---|---|---|
| Act 1 | `solo` | 5101 | Sqlite file `artifacts/demo/notes.db` | none | **S** | `artifacts/demo/logs/solo.log` |
| Act 2 | `a` | 5201 | PostgreSQL container `elsa-demo-pg` | `host-a` | **A** | `artifacts/demo/logs/a.log` |
| Act 2 | `b` | 5202 | the same PostgreSQL database | `host-b` | **B** | `artifacts/demo/logs/b.log` |
| Act 3 | `wb` (Elsa.Workbench) | 5301 | its own Sqlite file `artifacts/demo/hosts/wb/elsa.db` | none | **W** | `artifacts/demo/logs/wb.log` |
| Act 3 | Studio (the designer) | 5302 | none: it calls the Workbench | none | **Studio** | `artifacts/demo/logs/studio.log` |

Four more tabs face the audience: **1** for the Act 1 commands, **2** for the Act 2 commands, **3** for the Act 3 commands, and **P** (packages), the package board, which sits beside whichever of them is on screen (a split pane at half width) and shows each host's
feed, installed release and served release as they change. Tabs **S**, **A**, **B**, **W** and **Studio** run the hosts and stay off screen: their logs are long and are not written for an audience (see "Screen hygiene"). A tenth tab, **awake**, keeps
the laptop from sleeping. In Act 3 the browser faces the audience too: Studio, at `http://localhost:5302`, with tab 3 and the board beside it.

Ports 5101, 5201, 5202, 5301 and 5302 and the container name `elsa-demo-pg` are the demo's. All commands run from the repository root.

Every `bash` block below is meant to be pasted into an interactive zsh: none contains a `#` comment or a backtick (zsh does not treat `#` as a
comment on the command line unless `interactive_comments` is set, so a pasted comment is run as a command and fails).

## Your three questions from last time

At the previous demo the customer asked three things. This demo answers each one live, on the running system, so the answers below are what the platform does **now**, not what we
promised then. Each step that answers a question carries a boxed line to say, marked with the question; the recap at the end of Act 2 (2.8) closes all three.

| Question | Where it is answered | What the audience sees |
|---|---|---|
| **1. "How about hot reload, can it still run?"** | Act 1, steps 1.3 to 1.6; Act 2, step 2.5 | A new module version is installed by a host that keeps running. It is held back while its database migration is pending. Host A then switches with no restart and no reload |
| **2. "What when we have multiple pods?"** | Act 2, steps 2.1 to 2.6 | Two hosts on one database. The new version is turned on only once every live host can read it, and `status` names the host that holds it back |
| **3. "Multiple module versions needing different schemas"** | Act 2, steps 2.2 to 2.7 | Version 1 and version 2 run side by side against one table, and `rows` shows each row's stored version. Version 2 reads old rows, keeps its new feature dormant with a reason, and, for a family whose author wrote the upcaster and the rewriter, a background pass brings the old rows up to date |
| **1 and 3 again, from the designer** | Act 3, steps 3.2 to 3.6 | The Workbench installs a new activity release while it runs, refuses to switch while its migration is pending, then switches with no restart; the designer keeps both versions of the activity, and a workflow moves to the new one only when someone changes its exact version |

What has changed since the last demo, in one line each (presenter's reference, not for the screen):

- Hot reload no longer stops at "the host reloads whatever arrived": a module that needs a newer host than the one running is refused when the feed reconciles, and one whose migration is pending is refused at the reload, with the command that resolves it.
- Hosts that share a database now know each other (cluster membership). A schema version is finalized only when every live host reports that it can read it, so the order the hosts upgrade in no longer matters. Last time this was a stated gap.
- A new version reads the rows of its predecessor through an upcaster, writes the old format until it is finalized, and rewrites the old rows afterwards. Last time the rule was "one writer version at a time".

The limits that remain. Say them plainly; the customer will respect it, and each has a plain answer:

- **Concurrent versions need expand-only (additive) migrations, and that is the module author's rule.** The platform refuses a migration that removes or renames something only when its author declares it contracting (an
  `ExpandOnlyMigrationOptOut` naming the SchemaFamily and the FinalizedVersion), and only for tables that a stamped schema family covers; the refusal lasts until that version is finalized. An undeclared destructive migration in a customer module
  is not detected at apply time. The build enforces additive migrations for the modules we ship; a module of the customer's own can run the same check in its own tests.
- **The migration must be applied before the new version activates.** By design. Under the policy the demo runs with (`Validate`) the host refuses to migrate underneath the operator and says which command to run: it is the one manual step of the demo. Under the
  library's default policy (`AutoMigrate`) the host applies the migration itself at that point; either way the new version never runs against a schema it has not got.
- **Every host must be configured as a cluster member.** A host that is not counts only itself, and nothing warns about it.
- **Finalization is one way.** After a version is finalized, a host still on the older release is refused, and a finalized version only ever moves forward: there is no rollback of it.
- **The Workbench switches at a shell reload, not by itself.** It installs a package the moment it arrives in its feed and refreshes its catalog of features at once, but it keeps running the old release until its shell is
  reloaded (`POST /_admin/shells/reload/default`, the `wbreload` helper of Act 3); the Foundation host reloads by itself (`Elsa:Shells:ReloadOnPackageChange`, which the Workbench can turn on too but leaves off). A replaced release stays in memory until the host restarts.
  When the shell is swapped, the previous generation is drained: CShells waits for its open scopes (in-flight requests) to finish, and gives up after 30 seconds by default, so a request still running then is cut off. Elsa adds nothing above that:
  it registers no drain handler and no workflow-level quiescence, so work that does not hold a scope of the old shell is not waited for.
- **An activity version is a catalog entry, not a separate copy of the code.** The designer keeps Add note 1.0.0 beside 1.1.0, and a workflow pinned to 1.0.0 keeps its inputs and keeps running, but it runs the code of the release the host has loaded, 1.1.0: the host loads one release of a package at a time.
- **The background rewrite covers only the families that ask for it.** It runs for a family that declares an upcaster and a rewriter, and the module author writes both. Rows that are content-addressed are never rewritten, and they keep the family from being recorded as complete.
- **A narrow timing window.** A shell that is being built while the platform decides whether every host can read the new version is invisible to that decision until it reaches its first initializer. It needs an upstream CShells change, planned after this demo.

Presenter only (do not screen-share this block; the issue numbers are for you):

- Issue 2164: the reload bridge skips its catalog refresh while no shell is active, so a host whose start-up activation failed can keep serving the old release after an upgrade until a reload. The narrow timing window above is also 2164.

## Setup (before the audience, about 25 minutes, most of it waiting)

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
releases into `artifacts/demo/staging/`, and fills the closure feed the hosts resolve EF Core and the database engines from. For Act 3 it then
builds the Workbench and packs both releases of the Add note activity beside the Notes ones (`--no-act3` skips that part, and leaves Acts 1 and 2 exactly as they were). On the day itself
it is needed only when the staged releases are missing (`ls artifacts/demo/staging/1 artifacts/demo/staging/2`).

```bash
bash tools/demo/prepack.sh
```

- **Expect:** the last lines are the four staged packages and `ready in N s`:
  `artifacts/demo/staging/1/Elsa.Samples.Nuplane.Notes.1.0.0.nupkg`, `artifacts/demo/staging/2/Elsa.Samples.Nuplane.Notes.1.1.0.nupkg`,
  `artifacts/demo/staging/1/Elsa.Samples.Nuplane.Notes.Activities.1.0.0.nupkg` and `artifacts/demo/staging/2/Elsa.Samples.Nuplane.Notes.Activities.1.1.0.nupkg`.
- **Time:** a few minutes on a quiet laptop; a quarter of an hour when other builds compete for the machine (measured: 16 min at a
  load average above 500). Act 3's part, the Workbench build and the two packs, adds about two minutes (it says how long it took). Never on stage. Check `uptime` first: a load average far above the core count means everything below is slow.
- **If it goes wrong:** the failing command's own output is printed. A missing `python3`, `rsync`, `jq` or `curl` is named by the scripts; install it and run again.
  A build that fails on a dirty checkout: `git status` and get back to the commit you mean to present.

### S3. Check the tools, and keep the laptop awake

```bash
command -v docker jq curl python3 rsync sqlite3 caffeinate
docker info >/dev/null && echo docker ok
docker image inspect postgres:16-alpine >/dev/null && echo image ok
ls ../elsa-foundation-studio-demo/src/apps/Elsa.Studio.Web/bin/Release/net10.0/Elsa.Studio.Web.dll
```

- **Expect:** seven paths, then `docker ok` and `image ok`, then the path of the Studio build (Act 3; a checkout elsewhere is named by `DEMO_STUDIO_DIR`). The image is used from the cache and nothing is pulled: with Wi-Fi off, an evicted image is a dead demo, so check it here.
- **If it goes wrong:** no Studio build: build the Studio checkout (its `README.md`: `pnpm install`, `pnpm build`, then `dotnet build -c Release src/apps/Elsa.Studio.Web`), or present Acts 1 and 2 and do Act 3 by its fallback.

Plug in the power adapter and open a new terminal tab, **awake**. Run this in it and leave it running until the demo is over:

```bash
caffeinate -dimsu
```

Ctrl-C ends it. A sleeping laptop stalls the hosts' heartbeats, which the other host reads as a crash. `caffeinate` cannot keep a laptop awake with its lid closed: keep the lid open
(see the troubleshooting table if it slept anyway).

### S4. Open the tabs

Ten terminal tabs (1, 2, 3, P, S, A, B, W, Studio, awake), all in the repository root, named as in the cast table. Paste this into **1**, **2** and **3** (it defines the helpers the acts use, and
it puts a bare `$ ` prompt on the screen instead of your user name, host name and folder; the audience never sees the paste):

```bash
source tools/demo/helpers.sh
DEMO_PROMPT=$PROMPT DEMO_RPROMPT=$RPROMPT
PROMPT='$ ' RPROMPT=''
```

To get the old prompt back: `PROMPT=$DEMO_PROMPT RPROMPT=$DEMO_RPROMPT`. A prompt theme that redraws the prompt by itself (powerlevel10k does) undoes the change: start those three tabs as
a plain shell instead, with `zsh -f`, and paste the three lines there.

The helpers, and the two scripts that show packages, all typed from the repository root:

| Helper | What it does |
|---|---|
| `note PORT TEXT` | adds a note, prints it |
| `notes PORT` | lists the notes, one per line |
| `withtags PORT` | `GET /demo/notes/with-tags`: `HTTP` and the code, then the body |
| `tag PORT TAG` | tags the first note |
| `reload PORT` | `POST /_module-management/reload`: the code, then the answer, with paths shown relative to the repository |
| `status HOST` | `dotnet elsa persistence status` for host `solo` or `wb` (Sqlite) or `a` or `b` (PostgreSQL, with the 2 s skew allowance the hosts' `--fast-membership` needs) |
| `waitfor PORT` | waits until the host on that port answers `with-tags` with anything but 404, and says so |
| `pgconn` | points this tab's `ELSA_EF_CONNECTION` at the PostgreSQL container (never printed) |
| `bash tools/demo/board.sh HOST... [--watch]` | the package board, in tab **P**: one row per host (`solo`, `a`, `b`, `wb`) with the Notes versions **in the feed**, **installed** (Nuplane's own record) and **serving** (what `with-tags` answers: 404 is 1.0.0, 409 or 200 is 1.1.0, with the reason when it is dormant). The Workbench serves the Notes endpoints too, and its Add note activity is released in step with Notes, so its row stands for both. Read-only; `--watch` redraws every second and marks a cell that just changed |
| `bash tools/demo/show-package.sh 1\|2` | opens a staged release, which is a zip: its id, version, description and dependencies, the files inside, and its `nuplane.json` |
| `rows HOST` | the Notes table itself, one line per note: the version stamp each row was written in, and its tags as stored. `solo` reads the Sqlite file, `a` and `b` ask the PostgreSQL container (`docker exec` and `psql`; one database, so both show the same rows), `wb` reads the Workbench's own Sqlite file |
| `wbreload PORT` | Act 3: `POST /_admin/shells/reload/default` on the Workbench, which rebuilds its shell from the packages it installed. The Workbench answers `HTTP 200` either way; the helper shows the answer as `"reloaded": false` with the module, the pending migrations and the command it names, or `"reloaded": true` with the shell's generation and the Notes release it now serves (asked of the host: `with-tags` is 404 on 1.0.0) |
| `addnote PORT VERSION TEXT [TAGS]` | Act 3's fallback (`tools/demo/addnote.sh`): runs the workflow "Add note (API)" with its Add note node pinned to that exact version, through the calls Studio makes: it creates the workflow the first time, changes the node's exact version when it is pinned to another, sets Text (and Tags), runs it, and says how the run ended. It signs in as the Workbench's development admin without printing anything of it |
| `bash tools/demo/run-workbench.sh`, `run-studio.sh` | Act 3's two servers, in tabs **W** and **Studio** (S8, S9) |

Tabs **1** and **3** must not have a database connection in its environment (Act 1 uses the default Sqlite file, and Act 3's Workbench its own file, which `elsa.sh` finds by itself and refuses to replace with a connection set in the tab):

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

and, for Act 3, Studio (`http://localhost:5302`), opened and signed in at S9.

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

In tab **A** (start the hosts one after the other: a build from before #2162 logs a duplicate key error when they start together, see the troubleshooting table):

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

### S7. The package board, in tab P

Split tab **1** at half width (or put **P** beside it) and run, in **P**:

```bash
bash tools/demo/board.sh solo --watch
```

- **Expect:** a frame at most 50 columns wide, redrawn every second, with a clock: one row for `solo` that reads `1.0.0` in the feed, `1.0.0` installed and `1.0.0` serving. It only reads (a folder, the host's Nuplane record and `GET with-tags`); it never calls `/reload`.
- **If it goes wrong:** `down` under serving: the host is not up yet, wait for `/health/ready` 200 (S5). `-` under installed: the host has not written its Nuplane record yet, which a running host has. Ctrl-C leaves the board.

### S8. The Workbench (Act 3), in tab 3, then start it in tab W

In tab **3** (the paste of S4, and no `ELSA_EF_CONNECTION`):

```bash
bash tools/demo/publish.sh 1 --host wb
bash tools/demo/run-workbench.sh wb --port 5301 --management-key-env DEMO_KEY --prepare-only
bash tools/demo/elsa.sh persistence apply --restore --host artifacts/demo/hosts/wb --environment Development --provider Sqlite --from-host
```

- **Expect:** two `published` lines, one per package of release 1: `Elsa.Samples.Nuplane.Notes.1.0.0.nupkg` and `Elsa.Samples.Nuplane.Notes.Activities.1.0.0.nupkg`, both to `artifacts/demo/hosts/wb/feed`. Then
  `restore: 21 package(s) installed under ...` and a table of thirteen modules, every one with migrations applied: `Samples.Notes` with `1`, and the Workbench's own (`Workflows.Runtime`, `Workflows.Design`, `Identity.Iam` and the rest).
- **Why:** the Workbench runs under `Validate` like the other hosts, so it will not start on a database that is not migrated. `--from-host` applies every module its shell enables, the Notes module included, in one go: the Workbench keeps all of them in one
  Sqlite file, `artifacts/demo/hosts/wb/elsa.db`. No connection is needed in the tab: `elsa.sh` gives the tool that file for this host directory (and refuses if the tab has `ELSA_EF_CONNECTION` set).
- **Time:** publish under a second; the apply 30 to 90 s.
- **If it goes wrong:** `is a Workbench host ... ELSA_EF_CONNECTION set`: this is tab 2, or tab 3 kept a connection; `unset ELSA_EF_CONNECTION` and repeat. The restore line shows the checkout's absolute path: this is setup, off screen.

In tab **W**:

```bash
source tools/demo/helpers.sh
bash tools/demo/run-workbench.sh wb --port 5301 --management-key-env DEMO_KEY 2>&1 | tee artifacts/demo/logs/wb.log
```

Wait until it is ready, from tab 3: `curl -s -o /dev/null -w '%{http_code}\n' localhost:5301/health/ready` prints `200` (20 to 30 s on a quiet laptop, 70 to 90 s under load). The log says `Default shell default generation 1 is ready after ...`.

### S9. Studio, in tab Studio, and the browser

In tab **Studio** (after S8, so the logs folder exists):

```bash
bash tools/demo/run-studio.sh 2>&1 | tee artifacts/demo/logs/studio.log
```

- **Expect:** `studio: http://localhost:5302`, `workbench: http://localhost:5301`, then the host's own `Now listening on: http://localhost:5302`. It runs the Studio checkout `../elsa-foundation-studio-demo` (`DEMO_STUDIO_DIR` names another). On the presenter's machine that checkout is built in place, on Studio main `86a99789`, which contains the designer fix (PR #547), so no `assets: ...` line should appear. `run-studio.sh` still carries a workaround for a checkout that was built in another folder and then moved (it runs a re-based copy of the static web asset manifest, kept under `artifacts/demo/hosts/studio`, and says so in an `assets: ...` line): it is now a no-op safety net, and the line, if it ever appears, is harmless. To rebuild the checkout after pulling Studio, run `dotnet build -c Release src/apps/Elsa.Studio.Web` in it (`pnpm install && pnpm build` first only when the front end changed), never on stage.
- **If it goes wrong:** `There is no Studio checkout` or `is not built`: the message gives the build commands. Never run them on stage.

In the browser, open a new tab on `http://localhost:5302`. Studio sends you to the Workbench's sign-in page (`localhost:5301/_elsa/identity/login`) and, once signed in, back to Studio. Sign in with the Workbench's **development admin**: the
user name and the password are the values of `SeedAdminUserName` and `SeedAdminPassword` in `artifacts/demo/hosts/wb/shells.json`, under the feature `FoundationIdentityAspNetCoreIdentityEntityFrameworkCore` (the Workbench's own development seed, copied from `src/apps/Elsa.Workbench/shells.json`).
Read them in an editor, off screen; never print that file on the screen, the password is in it. Sign in now, before the audience arrives: the sign-in page shows nothing sensitive, but it is a wasted minute on stage, and nothing is then typed but the demo. Then open **Workflows**, and leave the tab there.

Then look at the bottom of the Studio page. Its bottom panel (**Console** / **Structured Logs**) may be open, and it shows the Studio checkout's absolute path, with your user name. Collapse it with the chevron at the panel's right edge before the audience sees the screen.

- **Expect:** back on Studio, signed in; **Workflows** lists no workflow. The session lasts through the shell reloads of Act 3 (checked: the same sign-in and the same token still work after a reload), so you do not sign in again on stage.
- **If it goes wrong:** the sign-in page does not appear, or the browser console shows a CORS error: the Workbench was started for another Studio port (`run-workbench.sh --studio-port`, default `DEMO_PORT_STUDIO`, else 5302); stop it (Ctrl-C in tab W) and start it again with the S8 command.

Setup is done. Open tab **1** full screen, board beside it; the audience arrives.

---

## Act 1: one host, upgraded in place (Sqlite)

### 1.1 Release 1.0.0 is running: add and list notes

```bash
note 5101 "hello from release 1.0.0"
note 5101 "a second note"
notes 5101
withtags 5101
rows solo
```

- **Audience sees:** two JSON notes created, the list, `HTTP 404` for `with-tags`, and then what is stored: both notes stamped `1.0.0`, and no tags column yet (`(no column yet)`). This is the "before" shot for question 3.
- **Say:** "This is a normal module in a running host: a table, two endpoints. Release 1.0.0 has no tags: that endpoint does not exist yet."
- **Expect:** each `note` prints `{"id":"...","text":"...","createdAt":"..."}`; `notes` prints two lines; `withtags` prints `HTTP 404` and nothing else; `rows solo` prints the header `note schema tags` and two lines, each `1.0.0` with `(no column yet)`.
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

### 1.2b Open the package

```bash
bash tools/demo/show-package.sh 2
```

- **Audience sees:** a short listing: the package id and version `1.1.0`, what it depends on, the files inside (the module's `.dll`, `nuplane.json`, `elsa-package.json`) and the content of `nuplane.json`.
- **Say:** "This is an ordinary NuGet package: a zip with a manifest. The module's assembly, a small declaration of what it needs from the host, and its dependencies. There is nothing proprietary about the shape; it is what you would push to your own feed."
- **Expect:** the first line is `Elsa.Samples.Nuplane.Notes 1.1.0`; the list of files includes `nuplane.json` and `elsa-package.json`.
- **Time:** instant.
- **If it goes wrong:** `Release 2 is not staged`: `bash tools/demo/prepack.sh` (minutes; not on stage).

### 1.3 Publish release 1.1.0 to the local feed

```bash
bash tools/demo/publish.sh 2 --host solo
```

> **Your question 1: "How about hot reload, can it still run?"**
> "Yes, and this is it. The host is running right now and serves traffic. I am dropping a new module version into its feed, and I will not restart anything."

- **Audience sees:** one line.
- **Board shows:** the `in the feed` cell of `solo` goes from `1.0.0` to `1.0.0, 1.1.0` at once, with the line "1.1.0 in the feed, not installed yet" under the row. `installed` and `serving` still read `1.0.0`.
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
- **Board shows:** about ten seconds after the publish, `installed` turns to `1.1.0` while `serving` stays `1.0.0`, with the line "1.1.0 installed, not switched" under the row: the host has the package and is held back. It does not change at the `reload`; only the 409 appears on the screen of tab 1.
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
  the host was started without `--management-key-env DEMO_KEY`, or the key in this tab differs (`echo $DEMO_KEY`).

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

> **Your question 1, answered:**
> "That was hot reload: a new module version, picked up by a running host with no restart. It was held back until its database migration was applied, and the host told us so; for up to two seconds after the reload it also answered 409 while it re-checked that the new version was safe. Then it switched."

- **Audience sees:** `HTTP 200`, the same notes now with `"tags":[]`, then one note with its tag, and the original endpoint unchanged. In the browser, reload tab 1: blank 404 becomes the notes.
- **Board shows:** `serving` turns from `1.0.0` to `1.1.0`, first with the line "tags dormant: not every host reads 2.0.0 yet" for up to two seconds, then "tags live". The three cells now read `1.0.0, 1.1.0`, `1.1.0`, `1.1.0`.
- **Say:** "The host switched to release 1.1.0 without a restart. The notes written by release 1.0.0 have no tags column value: the upcaster reads them as
  'no tags'. And the old endpoint carries on."
- **Expect:** `reload` prints `HTTP 200` and the four lines of JSON `{`, `"features": 4,`, `"reloaded": 1` and `}`; `withtags` prints `HTTP 200` and the notes with `"tags":[]`; `tag` prints the note with `"tags":["demo"]`.
- **Optional:** `rows solo` again shows what is stored: the note that was just tagged is stamped `2.0.0` with `["demo"]` (the rehearsal asserts it). The other note is `1.0.0` or, once the host's background pass has reached it, `2.0.0`.
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
the host log says `EF module 'Samples.Notes' has pending migrations`. `/health/ready` is `503` with `"reason": {"code": "activation-refused", ...}`. Then run 1.5. **The host activates the shell by itself**: it checks a refused
shell again every minute or a little less (`Elsa:Boot:EagerShellActivation:Retry:MaxDelay`), so within about a minute of the apply `/health/ready` is `200` with no request sent
(the rehearsal waits up to 75 s for it). Then `notes 5101` answers, and `withtags 5101` is `HTTP 200` (after a moment of 409, as in 1.6). If you do not want to wait, a real request (`notes 5101`) activates the shell at once.
If the package is not staged and there is no time: `bash tools/demo/pack.sh 2 --no-host --host solo` builds it live (15 to 86 s).

`bash tools/demo/rehearse.sh --act 1 --fallback` rehearses exactly this route.

---

## Act 2: two hosts, one database (PostgreSQL)

Switch to tab **2** (it holds `ELSA_EF_CONNECTION`). `docker ps --filter name=elsa-demo-pg` shows the container if anybody asks where the database is. In tab **P**, Ctrl-C the Act 1 board and start the Act 2 one, which shows both hosts:

```bash
bash tools/demo/board.sh a b --watch
```

It reads `1.0.0` in all six cells.

> **Your question 2: "What when we have multiple pods?"**
> "Here are two hosts on one database. Watch what the platform does when they are not on the same release: we upgrade them one at a time, on purpose."

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

> **Your question 3: "Multiple module versions needing different schemas"**
> "Version 2 needs a new column. The migration only adds it, and nothing is dropped or changed, so host A, still on version 1, carries on against the migrated database. That is the rule that lets two versions share one table."

- **Audience sees:** the same refusal as in Act 1 (`HTTP 409`, module, migration, apply command), the apply, then `HTTP 200`.
- **Board shows:** host `b` only: `in the feed` gets `1.1.0` at the publish, `installed` turns to `1.1.0` about ten seconds later with "1.1.0 installed, not switched" under it (held back by the pending migration), and after the second `reload` `serving` turns to `1.1.0` with "tags dormant: not every host reads 2.0.0 yet". Host `a` does not move.
- **Say:** "Same drill on host B, but now the database is shared. Watch host A: it is untouched, and it is still on 1.0.0." (`note 5201 "A keeps writing"` if you want to show it.)
  "The migration only adds a nullable column, so A keeps working against it."
- **Expect:** `HTTP 409` with `"code": "pending-migrations"`; the apply prints `01  Cluster.Membership … 0` and `02  Samples.Notes … 1`; then `HTTP 200` and `{ "features": 4, "reloaded": 1 }`.
- **Time:** about 10 s for B to install, 2 s for the refusal, 3 to 15 s for the apply, 2 s for the reload.
- **If it goes wrong:** as in 1.4 and 1.5. The apply lists two modules because the database has two; this is right.

**Optional: Claude operates this step.** After the publish, an AI agent can do the rest of 2.2 while you narrate: it reads the refusal, applies the migration the host names, reloads, and confirms that host B's new feature
is dormant until host A is upgraded. It shows that a host which says exactly what it needs can be operated by a pipeline or an agent.

- **Before the audience:** start a Claude Code session in the repository root, with a permission mode that runs shell commands without asking, and have it read [OPERATOR.md](OPERATOR.md): "Read tools/demo/OPERATOR.md and reply with one word: ready."
- **On stage**, once the board shows `b` installed `1.1.0`, type in that session: "Host B refuses to switch to the new release. Find out why and fix it."
- **Audience sees:** five commands with a sentence before and after each: `reload 5202` (409, the pending migration), the apply, `reload 5202` (200), `withtags 5202` (409, `schema-version-not-finalized`) and `status b` (`waits for: host-a`). It stops there and does not touch host A.
- **Say:** "Claude was given a one-page brief with this demo's house rules. The diagnosis comes from the host's own answer."
- **Time:** one to two minutes (a dry run took about two, the brief included).
- **If it goes wrong:** it needs the network. If it stalls for half a minute, run the three commands above yourself: applying twice does no harm. Then go on with 2.3, whose first command it has already shown.

### 2.3 `with-tags` on host B is refused: 409

```bash
withtags 5202
note 5201 "written by A on release 1.0.0"
note 5202 "written by B on release 1.1.0"
rows a
```

> **Your question 3, live:** "Host B runs the new release, yet the row it just wrote is stamped with the old schema version, 1.0.0, exactly like host A's. A host writes the old format until every host can read the new one, so no row exists that any host cannot read, and the new feature stays dormant, with its reason, until then."

- **Audience sees:** `HTTP 409` and a reason; refresh browser tab 3 for the same reason in the raw response. Host A keeps taking writes. Then `rows`: four notes, all stamped `1.0.0`, and an empty (`NULL`) tags column.
- **Board shows:** nothing moves, and that is the picture: `b` serves `1.1.0` with "tags dormant: not every host reads 2.0.0 yet", `a` serves `1.0.0` and has `1.0.0` installed. Point at the two rows side by side.
- **Say:** "Host B runs release 1.1.0 and could serve tags, but host A cannot read them. If B wrote tags, A would meet rows it cannot understand. So the feature stays
  dormant, and the request is refused whole: nothing is half-saved." Then, for `rows`: "This is the table itself, not an answer from a host. Every row carries the schema version it was written in.
  Both hosts wrote 1.0.0, including B, which runs the new release: it will not write the new format until A can read it. The tags column exists, because the migration ran, but nothing has filled it."
- **Expect:**

  ```
  HTTP 409
  {"code":"schema-version-not-finalized","feature":"NotesWithTags","reason":"It becomes available once every host can read version '2.0.0' of schema family 'SamplesNotes'.","message":"Feature 'NotesWithTags' is dormant: ... The request was refused whole, and nothing it carried was saved."}
  ```
  and both `note` calls answer normally, then:

  ```
  note                           schema  tags
  written on host A              1.0.0   NULL
  written on host B              1.0.0   NULL
  written by A on release 1.0.0  1.0.0   NULL
  written by B on release 1.1.0  1.0.0   NULL
  ```
- **Time:** instant.
- **If it goes wrong:** `HTTP 200` here would mean 2.0.0 is already finalized: host A was upgraded earlier, or was stopped. `bash tools/demo/reset.sh` and rehearse again. `rows` says `No such container`: Docker is not running or the container is gone (see the troubleshooting table).

### 2.4 `persistence status` names the host it waits for

```bash
status b
```

- **Audience sees:** the pending version, the name of the blocker, and the fleet with what each host reads.

> **Your question 2, answered:** "The platform is designed not to race. A new version is turned on only once every live host can read it, and `status` names the host holding it back. If a host crashes instead of stopping, it stops counting when its membership expires: 12 seconds in this demo, 35 by default."

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

> **Your question 1, once more:** "Host A gets the new version the same way: dropped into its feed, no restart, and this time no reload command either. Its database is already migrated, so nothing holds it back."

- **Board shows:** host `a`'s `in the feed` gets `1.1.0` at the publish; about ten seconds later `installed` turns to `1.1.0` and, within a second or two, `serving` follows it. There is no "not switched" line this time: A's database is already migrated, so nothing holds it back.
- **Audience sees:** one line, then a row of dots that ends when host A has switched. No restart, and nothing else to run: A's database was migrated by B's apply, so the host switches by itself.
  `waitfor` is the on-screen signal: it polls `with-tags` on A twice a second, which answers 404 while A still runs 1.0.0, and says `switched: with-tags answers HTTP 200 after N s` at the first answer
  that is not a 404. Refresh browser tab 2 meanwhile if you like: it too goes from the blank 404 to the notes (or, for a moment, to the 409 reason).
- **Say:** "Host A gets the same release the same way. Its database is already migrated, so there is no refusal: it installs, switches, and now reads 2.0.0 too. It never left the cluster."
- **Expect:** `published Elsa.Samples.Nuplane.Notes.1.1.0.nupkg to artifacts/demo/hosts/a/feed`, then `waiting for the host on port 5201 to switch.................. switched: with-tags answers HTTP 200 after 9 s`.
  It says `HTTP 409` instead when a poll lands in the short moment between A's switch and its evaluation of the schema version (see 2.6): both are right, the point is that the 404 is over. About ten seconds after the publish host A's log says it reloaded its shell; A's tab shows nothing you need to look at.
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
note 5202 "written after finalization on B"
note 5201 "written after finalization on A"
rows a
```

> **Your question 3, the rest:** "Both hosts serve tags now. Version 2 reads the old rows through an upcaster, so they show no tags rather than an error, and rows written from now on are stamped 2.0.0. The host is rewriting the old rows in the background, so within a few seconds the table reads 2.0.0 throughout."
>
> **Your question 2, the rest:** "Nobody flipped a switch on either host. The moment the last live host could read the new version, it turned on everywhere."

- **Board shows:** the "tags dormant" line under `b` (and for a moment under `a`) turns to "tags live" under both rows, and each host reads `1.0.0, 1.1.0`, `1.1.0`, `1.1.0`: the fleet is on one release, and `serving` has just changed on `b`.
- **Audience sees:** `HTTP 200` on both, the notes written before the upgrade with `"tags":[]`. In the browser, reload tabs 2 and 3: 404 and the 409 reason are gone. Then `rows`: the two notes just written are stamped `2.0.0` with `[]`. The four from before are either still `1.0.0` with `NULL` or already rewritten to `2.0.0` with `[]`: the host starts its background pass within seconds of the finalization, so which one you see is a matter of timing, and both are right.
- **Say:** "The moment every host could read 2.0.0, the platform turned the feature on, everywhere, without anybody flipping anything. The old notes read as 'no tags' through the upcaster.
  In the background the host rewrites the old rows into the new format; when none is left, the version is recorded complete." For `rows`: "The two notes I just wrote are stamped 2.0.0: from now on, a host writes the new format.
  The older four were 1.0.0 a minute ago. The host rewrites them itself, within seconds of the finalization; if any still says 1.0.0, run `rows` again in a moment."
- **Expect:** `HTTP 200` twice, then the status below. On host A the answers go **404** (A not switched yet, what `waitfor` waited out), then **409** (A has switched and is evaluating the schema version; the
  reason is the one from 2.3), then **200**. The 409 is short on A, often too short to be seen (the evaluation runs every 2 s, and A has just re-published what it reads), so after `waitfor` `withtags 5201` is a 200 or, for
  a moment, a 409. Host B has answered 409 since 2.2 and turns to 200 up to two seconds after A: if `withtags 5202` is still a 409, say "it is checking that this is safe", wait two seconds, repeat.
  The same 409-then-200 follows every switch of a host to release 1.1.0 (after `reload` in 1.6, and after a restart in the fallbacks).

  ```
  SamplesNotes (Samples.Notes): finalized at 2.0.0; this host reads [1.0.0, 2.0.0]
    complete from 1.0.0        <- for about 20 s after finalization, then: complete from 2.0.0
    backfill to 2.0.0 claimed by host-a (...) until ...    <- while the backfill runs, a line like this
  members: 2 in Cluster.Membership, ...
    host-a: Active, live, ...   SamplesNotes: reads 1.0.0, 2.0.0
    host-b: Active, live, ...   SamplesNotes: reads 1.0.0, 2.0.0
  ```
  and the `rows` of the two new notes, with the older four beside them:

  ```
  note                             schema  tags
  written on host A                2.0.0   []
  ...
  written after finalization on B  2.0.0   []
  written after finalization on A  2.0.0   []
  ```
  Run `status` again after about 20 s (a good moment for questions): `complete from 2.0.0`. 2.7 shows it, with `rows` once more.
- **Time:** finalization 0 to 3 s after A switched, on B up to 2 s later (repeat `withtags` if you get 409 for a moment); the old rows are rewritten within a few seconds of it (measured in five rehearsals: in four the old rows were still at `1.0.0` when both hosts first answered 200 and all were rewritten 2 to 4 s after the finalization, in one they had been rewritten already;
  so the in-between state, new rows at `2.0.0` beside old rows at `1.0.0`, lasts a few seconds at most, and the runbook does not promise it); `complete from 2.0.0` 16 to 20 s after finalization (measured: 18 to 20 s; 27 to 34 s after the publish).
- **If it goes wrong:** still 409 after 30 s and `waits for: host-a` in `status a`: A has not switched (see 2.5). A `waits for:` line naming a host you did not start: a leftover member from an earlier rehearsal: `bash tools/demo/reset.sh` and set up again.

### 2.7 The old rows are brought up to date

About twenty seconds after the version finalized, from tab 2 (the rewrite itself is over within seconds; the twenty seconds are the platform making sure that no row of the old version is left, and recording the version complete):

```bash
status a
rows a
```

> **Your question 3, closed:** "Every row, old and new, is stamped 2.0.0 now. The two versions ran side by side on one table, and nobody had to stop anything or rewrite the data by hand."

- **Audience sees:** `complete from 2.0.0`, and `rows` with every note stamped `2.0.0` and a tag list (`[]`): the four notes written before the finalization were rewritten by the host in the background.
- **Say:** "The host did that itself, one row at a time, once every host could read the new version. When no row of the old version is left, the platform records the version complete: that is this line. Until then, the old rows stayed readable through the upcaster."
- **Expect:**

  ```
  note                             schema  tags
  written on host A                2.0.0   []
  written on host B                2.0.0   []
  written by A on release 1.0.0    2.0.0   []
  written by B on release 1.1.0    2.0.0   []
  written after finalization on B  2.0.0   []
  written after finalization on A  2.0.0   []
  ```
- **Time:** `complete from 2.0.0` 16 to 20 s after finalization, as in 2.6. If `rows` still shows `1.0.0`, run it again in a few seconds; `status a` says `complete from 2.0.0` when the last old row is gone.
- **If it goes wrong:** rows still at `1.0.0` after a minute: `status a` shows what the family waits for, and the log of tab A or B says why the backfill is not moving.

### 2.8 Recap: your three questions

Read this out, or show it. Two sentences per question, the limits included.

> **1. "How about hot reload, can it still run?"**
> Yes: a running host installs a new module version from its feed and switches to it in place, with no restart; it is held back while its database migration is pending, a contracting migration is refused until its version is finalized, the new feature stays dormant until the schema version is finalized, and a package that needs a newer host is refused when the host declares that package as its own and carries it in its deps.json.
> The limits are that the Foundation host switches by itself while the Workbench installs the package at once and switches at its next shell reload (Act 3 shows it), that a replaced release stays in memory until the host restarts, and that the old shell is drained, not cut off: in-flight requests are waited for up to 30 seconds by default, and Elsa adds no workflow-level quiescence on top.
>
> **2. "What when we have multiple pods?"**
> Hosts that share a database form a cluster and a new module version is turned on only once every live host can read it, so the order you upgrade in does not matter, `persistence status` names the host still holding it back, and a crashed host stops counting once its membership expires (12 seconds here, 35 by default).
> The limits are that every host must be configured as a cluster member (one that is not counts only itself, and nothing warns you), and that a host that sleeps or stalls for longer than its membership lasts is dropped and has to rejoin.
>
> **3. "Multiple module versions needing different schemas"**
> Yes, as long as the versions' migrations only add: versions 1 and 2 run side by side on one table, version 2 reads old rows through an upcaster and keeps its new feature dormant, with the reason, until every host can read it, and for a family whose author wrote an upcaster and a rewriter a background pass then brings the old rows up to 2.0.0.
> The limits are that keeping migrations additive while versions overlap is the module author's rule: the platform refuses a migration that removes or renames something only when its author declares it contracting (an `ExpandOnlyMigrationOptOut` naming the family and the finalized version), and only for tables a stamped schema family covers, so an undeclared destructive migration is not detected at apply time. Also, content-addressed rows are never rewritten and keep the family from being recorded complete, the migration must be applied before the new version activates (by design: the host tells you the command), and once a version is finalized a host on the older release is refused.
>
> **A rough edge we are closing:** a very narrow timing window while a host builds its new shell.

### Act 2 fallback: stop, publish, start host A

If A does not switch by itself: in tab **A** press Ctrl-C (a clean stop leaves the cluster at once), then in tab 2 `bash tools/demo/publish.sh 2 --host a`, then start A again in tab **A** with the same commands as in S6
(10 to 40 s; `pgconn` is needed again only in a tab that lost its environment). **`with-tags` on B answers 200 the moment A has left**, before A is back: the last host that cannot read 2.0.0 is gone, so the version is released. Say exactly that; it is the same rule seen from the other side.
A host that is killed instead of stopped stays counted until its membership expires: about 12 s with `--fast-membership`.

---

## Act 3: the same upgrade, from the designer

Put the browser on screen, on the Studio tab (signed in at S9, on **Workflows**), with tab **3** beside it. In tab **P**, Ctrl-C the Act 2 board and start the Act 3 one:

```bash
bash tools/demo/board.sh wb --watch
```

It reads `1.0.0` in all three cells.

> "So far the module was upgraded under an API. The same thing happens under a workflow designer. This is the Workbench, the host that runs workflows, and Studio, its designer. The Notes module is installed here too, with a workflow activity, **Add note**, that ships in a package of its own and is released in step with the module."

### 3.1 Release 1.0.0 in the designer: a workflow with Add note

In Studio:

1. **Workflows**, **Create**: name it `Add note demo`, and create it. The designer opens on the new, empty workflow.
2. In the palette, category **Notes**, drag **Add note** onto the canvas. Select it: the inspector shows one input, **Text**. Type `hello from the designer`.
3. **Run**, in the editor's toolbar (a test run of the current design). The runtime panel opens and shows the run completed.

Then in tab 3:

```bash
rows wb
```

- **Audience sees:** the activity and its one input, the run, and the Workbench's Notes table: the note, stamped `1.0.0`, and no tags column yet.
- **Say:** "Version 1 of the activity has one input, Text. The note it wrote is stamped with schema version 1.0.0, as on the first host."
- **Expect:** `rows wb` prints the header `note schema tags` and `hello from the designer  1.0.0  (no column yet)`.
- **Time:** a minute in Studio; `rows` is instant. To save the minute, create the workflow at the end of setup (steps 1 and 2) and start here at **Run**. A run started from the designer takes a few seconds to store its note: if `rows wb` prints only the header and `(no notes yet)`, say that the run is still writing and repeat `rows wb` until the note is there.
- **If it goes wrong:** no **Notes** category in the palette: the Workbench did not load release 1 (the board says `-` or nothing under installed; the log in tab W says why), or the page was opened before the host was ready (refresh it). The run fails, or the
  designer misbehaves: the Act 3 fallback below.

### 3.2 Publish release 1.1.0 into the Workbench's feed

```bash
bash tools/demo/publish.sh 2 --host wb
```

> **Your question 1, from the designer:** "The Workbench is running, with a designer open on it. I drop the next release into its feed: the module and its activity, version 1.1.0. Nothing is restarted."

- **Audience sees:** two lines, one per package.
- **Board shows:** `in the feed` reads `1.0.0, 1.1.0` at once; about ten seconds later `installed` turns to `1.1.0` while `serving` stays `1.0.0`, with "1.1.0 installed, not switched" under the row.
- **Say:** "The host installs both packages while it runs, and reads the features they bring at once. It does not switch the running workflows to them by itself: that is an operator's decision here, and the next step."
- **Expect:** `published Elsa.Samples.Nuplane.Notes.1.1.0.nupkg to artifacts/demo/hosts/wb/feed` and `published Elsa.Samples.Nuplane.Notes.Activities.1.1.0.nupkg to artifacts/demo/hosts/wb/feed`.
- **Time:** the publish under a second; the install about 11 s (12 s measured under load; the host's log says `Refreshed runtime feature catalog after a Nuplane reconcile` when it is done).
- **If it goes wrong:** `installed` stays `1.0.0` after 40 s: `ls artifacts/demo/hosts/wb/feed`, and the log in tab W.

### 3.3 Reload the Workbench's shell: refused, the migration is pending

Once the board shows `installed` `1.1.0`:

```bash
wbreload 5301
```

- **Audience sees:** `HTTP 200` and the refusal: `"reloaded": false`, the module, the pending migration and the command to run.
- **Say:** "The same rule as on the first host. The new version needs a column the database does not have, so the Workbench refuses to switch, and it refuses to migrate underneath us. It names the module, the migration and the command. Everything keeps running on release 1, the designer included."
- **Expect:**

  ```
  HTTP 200
  {
    "reloaded": false,
    "error": "EfPendingMigrationsException",
    "module": "Samples.Notes",
    "pendingMigrations": [
      "…_AddTags"
    ],
    "command": "dotnet elsa persistence apply --host \"<host directory>\" --modules Samples.Notes --provider Sqlite --connection-env ELSA_EF_CONNECTION"
  }
  ```

  The Workbench's management API answers `200` with `"success": false` and the error where the Foundation host answers `409`; the helper shows that answer in this form. The command is the Workbench's own message, which names the host directory with a placeholder.
- **Time:** 1 to 3 s.
- **If it goes wrong:** `"reloaded": true` with `"serving": "Notes 1.0.0"`: the reload came before the host had installed 1.1.0, and rebuilt release 1; wait until the board shows `installed` `1.1.0`, and `wbreload 5301` again. `HTTP 401`: the key differs (`echo $DEMO_KEY` in tabs 3 and W), or the Workbench was started without `--management-key-env DEMO_KEY` and expects its own development key; restart it with the S8 command.

### 3.4 Apply the migration; reload again

```bash
bash tools/demo/elsa.sh persistence apply --host artifacts/demo/hosts/wb --environment Development --provider Sqlite --modules Samples.Notes
wbreload 5301
```

- **Audience sees:** the apply table with one migration applied, then `"reloaded": true` and `"serving": "Notes 1.1.0"`.
- **Board shows:** `serving` turns to `1.1.0`, "tags dormant" for a second or two, then "tags live".
- **Say:** "The same command as before, pointed at the Workbench's folder. Now the reload goes through, and the host runs release 1.1.0. Still no restart."
- **Expect:**

  ```
  provider: Sqlite   schema: (none)
  #   MODULE         CONTEXT               HISTORY TABLE                           APPLIED
  01  Samples.Notes  NotesSqliteDbContext  __EFMigrationsHistory_ElsaSamplesNotes  1
  ```

  then `HTTP 200` and `{ "reloaded": true, "generation": N, "serving": "Notes 1.1.0" }` (four lines of JSON; the generation counts the shell's rebuilds, refused ones included).
- **Time:** the apply 3 to 20 s, the reload 2 to 5 s; 2.0.0 finalizes within about a second after it (this host is a cluster of one).
- **If it goes wrong:** `is a Workbench host ... ELSA_EF_CONNECTION set`: you are in tab 2; use tab 3. The reload refused again: the apply did not run against the Workbench's file; read its table and repeat.

### 3.5 In the designer: move the node to Add note 1.1.0

In Studio:

1. Refresh the page (the designer reads the activity catalog when it loads).
2. Select the **Add note** node, then its **Version** tab: it is pinned to the exact version `1.0.0`.
3. **Change exact version**. The review dialog shows `v1.0.0` to `v1.1.0`, **Compatible**, **Minor**, and "Input 'Tags' was added".
4. Scroll the dialog to the bottom and click **Apply to this occurrence**.
5. The inspector's inputs are now **Text** and **Tags (comma-separated)**.

> **Your question 3, from the designer:** "Both versions of the activity are there side by side, 1.0.0 and 1.1.0. A workflow is pinned to an exact version and moves only when someone decides it should, after the designer has shown what changes: here, one input added, a compatible change."

- **Say:** "The workflows that use version 1 keep working: nothing moved them. This one I move on purpose."
- **Known quirks (presenter only):** after the upgrade the **Version** tab also says "Recommended v1.0.0 available", and a node freshly dragged from the palette is still `1.0.0`: a version a package adds becomes the activity's newest version, not its recommended one. Ignore both; use **Change exact version**, never the recommendation.
  And a workflow left pinned to `1.0.0` keeps running, but on the code of release 1.1.0 (the host loads one release of a package at a time): if asked, "it keeps its inputs; the code underneath is the release the host runs".
- **Time:** under a minute.
- **If it goes wrong:** the dialog offers no `1.1.0`: the page was not refreshed after the reload, or the reload did not go through (3.4); refresh. **Apply** does nothing: scroll the dialog to its bottom and click it once more; still nothing, the Act 3 fallback.

### 3.6 Run it with tags

In Studio: **Text** `tagged from the designer`, **Tags** `demo, designer`, then **Run**. In tab 3:

```bash
rows wb
```

- **Audience sees:** the run completes; `rows` shows the new note stamped `2.0.0` with `["demo","designer"]`, and the note from 3.1 rewritten to `2.0.0` with `[]`.
- **Say:** "Version 1.1.0 wrote the new format, tags included. The note version 1 wrote has been brought up to date by the host itself, in the background, the way the two hosts did it."
- **Expect:**

  ```
  note                      schema  tags
  hello from the designer   2.0.0   []
  tagged from the designer  2.0.0   ["demo","designer"]
  ```

  By the time you are back from the designer the host has rewritten the first row; if it still reads `1.0.0` and `NULL`, run `rows wb` again in a few seconds. The new note takes a few seconds to be stored after **Run**: if it is not there yet, repeat `rows wb`.
- **Time:** the run a second or two, its note a few seconds more; the old row is rewritten within seconds of the reload, long before 3.6.
- **If it goes wrong:** the run faults with a reason that names a dormant feature: the reload was a second ago and 2.0.0 is not finalized yet; run again. Anything else: the fallback.

> **Recap, if there is time:** "That was questions one and three again, from the designer. A running host took a new version of a module and of an activity, held it back until the database was ready, and switched without a restart. Two versions of the activity live side by side, and a workflow moves to the new one when someone decides it should."

### Act 3 fallback: the API instead of the designer

When the designer misbehaves (a page that does not load, a dialog that does not apply), do its part in tab 3 with `addnote`, which makes the calls Studio makes: it runs a workflow of its own, `Add note (API)`, created on first use. Instead of 3.1:

```bash
addnote 5301 1.0.0 "hello from the API"
rows wb
```

and instead of 3.5 and 3.6:

```bash
addnote 5301 1.1.0 "tagged from the API" "demo, api"
rows wb
```

- **Expect:** the first prints `workflow "Add note (API)": created, its Add note node pinned to 1.0.0`, `inputs: Text "hello from the API"` and `run: Completed`; the second
  `workflow "Add note (API)": its Add note node changed from exact version 1.0.0 to 1.1.0`, the two inputs and `run: Completed`. `rows wb` as in 3.1 and 3.6.
- **Say:** "This is what the designer does underneath: it changes the exact version the node is pinned to, and runs it."
- **If it goes wrong:** `Add note 1.1.0 is not in the activity catalog`: the reload of 3.4 has not gone through. `nothing answers on port 5301`: the Workbench is down (tab W).

`bash tools/demo/rehearse.sh --act 3` rehearses Act 3 this way, through the same helper.

---

## After the demo, and between rehearsals

```bash
bash tools/demo/reset.sh
```

It stops the demo hosts it started (by the process ids `run-host.sh`, `run-workbench.sh` and `run-studio.sh` recorded, and only a process that is still a demo host; a clean stop, killed only after 30 s),
removes the `elsa-demo-pg` container, and removes everything under `artifacts/demo` except the staged releases and the closure feed: the Workbench's database and the workflows made in Studio go with it. It prints each host, container and
folder it stopped or removed. `bash tools/demo/reset.sh --all` removes the staged releases too: run `prepack.sh` again afterwards. Nothing is written into the Studio checkout, so there is nothing to clean there.

Nothing here uses `pkill`, `killall` or a process name.

## Rehearse it without an audience

All three acts, about 12 minutes; needs Docker, the staged releases and the Workbench build (both from `prepack.sh`), but no Studio:

```bash
bash tools/demo/rehearse.sh
```

One act (`--act 1`, `2` or `3`), or Acts 1 and 2 only after a `prepack.sh --no-act3`:

```bash
bash tools/demo/rehearse.sh --act 3
bash tools/demo/rehearse.sh --no-act3
```

Act 1 by its fallback route only:

```bash
bash tools/demo/rehearse.sh --act 1 --fallback
```

It begins with `reset.sh`, follows this runbook step by step (hosts started one after the other, in-place upgrades, no interaction), runs the very helpers of `tools/demo/helpers.sh` that you type,
asserts the status codes and output lines above, the cells of the package board at the steps that move it (`board.sh`, once per step instead of `--watch`), that `show-package.sh 2` prints the package id and `1.1.0`, and the schema version stored on each row (`rows`) before the upgrade, after the finalization and after the backfill, checks that nothing the audience sees shows the repository path or a home folder, prints the time of every step and the moments measured (install, finalization, completion), lists any spec or requirement numbers a host logged, and cleans up on exit,
success or failure. Act 3's designer steps are done with `addnote` (the fallback, which makes Studio's calls), and the rehearsal also runs a second workflow left pinned to `1.0.0` after the upgrade, to hold the quirk of 3.5 to what it says. Its host logs are kept in `artifacts/demo-rehearsal/`. It uses the ports 5101, 5201, 5202 and 5301 (`DEMO_PORT_SOLO`, `DEMO_PORT_A`, `DEMO_PORT_B`, `DEMO_PORT_WB` change them): stop a live demo first.

## Troubleshooting

| Symptom | Cause | What to do |
|---|---|---|
| A host built before #2162 logs a **duplicate key** error at start, on the cluster's identity row | Two hosts started in the same instant both tried to create it; the loser logged the error and carried on. A current build logs nothing for it | Harmless if the host goes on to `Now listening`. Avoid it: start A, wait for `/health/ready` 200, then start B. If a host did not come up, Ctrl-C it and start it again |
| `/health/ready` says **503** after `apply`, and requests answer **500** | A host that was started already refused (the fallback route): the readiness probe does not activate a shell, and the host checks a refused one again only every minute or a little less | Wait up to a minute: the host activates the shell itself and `/health/ready` follows. Or send a real request, `notes 5101`, which activates it at once |
| `reload` says **200** with `"features": 3` instead of 409 | The host has not installed 1.1.0 yet | Wait five seconds, repeat |
| `reload` answers **401**, **403** or **404** | Module management is off, or the key differs | The host must be started with `--management-key-env DEMO_KEY`; `echo $DEMO_KEY` in the tab must match the one in the host's tab |
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
| `rows a` or `rows b` says `No such container`, or prints nothing | The PostgreSQL container is not running (`rows a` and `rows b` ask it through `docker exec`), or `DEMO_PG_CONTAINER` renamed it | `docker ps --filter name=elsa-demo-pg`; start Docker and redo S6 if it is gone. `rows solo` reads the Sqlite file `artifacts/demo/notes.db` and needs no container |
| `persistence status` shows `Cluster.Membership has migrations not applied` | The command lacks `--modules Samples.Notes,Cluster.Membership` | Use the `status` helper (it always passes both modules), not a hand-typed command |
| `status` shows no `waits for:` line while A is still on 1.0.0 | A hand-typed command lacks `--skew-allowance 00:00:02`, so members are judged with 5 s instead of the hosts' 2 s | Use the `status` helper, which passes it |
| The board (tab **P**) shows **down** under serving | The host is not up (yet), or listens on another port (the board reads `DEMO_PORT_SOLO`, `DEMO_PORT_A`, `DEMO_PORT_B` and `DEMO_PORT_WB` like the rehearsal) | `curl -s -o /dev/null -w '%{http_code}\n' localhost:5101/health/ready`; start the host (S5, S6, S8). The board needs no restart: it picks the host up when it answers |
| The board shows **-** under installed, or `installed` stays `1.0.0` while the feed has `1.1.0` | `-`: the host has not written its Nuplane record (`.nuplane/store-state.json`) yet. Otherwise the host has not installed the package yet (about ten seconds), or its watcher missed it | Wait ten seconds. Still `1.0.0` after 40 s: `ls artifacts/demo/hosts/solo/feed` and look at the host's log in tab S, A or B |
| Host takes over a minute to be ready | The machine is loaded | `uptime`; wait; start the presentation after `/health/ready` is 200 on all four hosts (the Workbench takes longest: 70 to 90 s under load) |
| The Workbench's `/health/ready` says **503** `shell_activation_failed`, and its log says `EF module '...' has pending migrations` | The database was not migrated before the start (S8's `apply --restore ... --from-host` skipped, or run in a tab with `ELSA_EF_CONNECTION` set, which `elsa.sh` refuses) | Ctrl-C in tab W, run S8's apply in tab 3, start the Workbench again |
| `wbreload` says `"reloaded": true` but `"serving": "Notes 1.0.0"` | The reload came before the Workbench had installed 1.1.0, so it rebuilt release 1 | Wait until the board shows `installed` `1.1.0` (about ten seconds after the publish), then `wbreload 5301` again: it is refused for the migration, as in 3.3 |
| `wbreload` answers **401** | The key differs, or the Workbench was started without `--management-key-env DEMO_KEY` (then it expects its own development key) | `echo $DEMO_KEY` in tabs 3 and W; restart the Workbench with the S8 command |
| `elsa.sh` says `artifacts/demo/hosts/wb is a Workbench host ... ELSA_EF_CONNECTION set` | The command was run in a tab with a database connection (tab 2, after `pgconn`) | Run it in tab 3, or `unset ELSA_EF_CONNECTION` first: the Workbench's database is its own file, which `elsa.sh` finds by itself |
| Studio: `run-studio.sh` says **no Studio checkout** or **not built** | `DEMO_STUDIO_DIR` (default `../elsa-foundation-studio-demo`) is missing or unbuilt | The message has the build commands; never on stage. Without Studio, Act 3 runs by its fallback (`addnote`) |
| Studio does not reach the sign-in page, or the browser console shows a **CORS** error | The Workbench was started for another Studio port | Start the Workbench with the Studio's port (`--studio-port`, default `DEMO_PORT_STUDIO`, else 5302) |
| Studio asks to **sign in again** on stage | The session lapsed or was lost | Sign in with the development admin (S9); the values are in the Workbench's `shells.json`, never on screen: read them on the laptop's other screen, or switch to the Act 3 fallback |
| The designer's **Change exact version** offers no `1.1.0` | The page was loaded before the reload, or the reload did not go through | Refresh the page; check `wbreload 5301` says `"reloaded": true` and `"serving": "Notes 1.1.0"` |
| An Add note run with tags **faults**, naming a dormant feature | 2.0.0 is not finalized yet: the reload was a second or two ago | Run again. `status wb` says what the version waits for |

## Screen hygiene

Nothing the audience is meant to see carries an internal requirement number. The one exception found is a host log line, which is why the host tabs stay off screen:

- `Schema family SamplesNotes of EF module Samples.Notes is complete at 2.0.0: no row below it remains (spec 186, FR-014).` (host log, about 20 s after the version finalizes, in every act)

The host logs are also very long (package resolution and catalog lines by the hundred per change); the answers in tab 1 and tab 2 tell the story. `rehearse.sh` reports these lines at
the end of every run, so a new one shows up there. No personal path or name is on the audience's screen: the `reload` helper shows the `command` of the 409 relative to the repository (`--host "artifacts/demo/hosts/solo"`), the
prompt of tabs 1 and 2 is a bare `$ ` (S4), and `rehearse.sh` fails when anything it runs for the audience prints the repository path or a home folder (the package board and `show-package.sh` print relative paths only, and are covered by it). The one command that does print the absolute path of the
checkout, with the user name, is `elsa.sh persistence apply --restore`, which is setup and stays off screen. The tab and window titles and the browser's history are yours to check.

Act 3 adds four things to check. Studio's bottom panel (**Console** / **Structured Logs**) can be open and shows the Studio checkout's absolute path with your user name: collapse it with the chevron at the panel's right edge (S9). Tab **Studio** prints the Studio checkout's absolute path (`Content root path: ...`): it stays off screen like the host tabs. The Workbench's refusal names its command with a placeholder, `--host "<host directory>"`, so `wbreload` shows no path at all. And the
development admin's password is in `artifacts/demo/hosts/wb/shells.json`: never open that file on screen, and sign in before the audience arrives (S9). The browser's address bar shows only `localhost:5302` (and `localhost:5301` on the sign-in page).
