# Activities Change Variables As A Unit Of Work Committed With The Step

Status: proposed (2026-10-24). Sipke Schoorstra, the decision holder, chose this design in a brainstorming session
on 2026-10-24, started from the review of [spec 266](../../specs/266-capture-concurrency/spec.md)
([#3286](https://github.com/elsa-workflows/elsa-foundation/pull/3286)). Acceptance needs agreement with @j03y-nxxbz
and @FransVanEk, because it overturns parts of an accepted ADR (0045 §4 and §6) and changes what spec 266 analyses.

Part of epic #3052 and program #2310. Amends
[ADR 0045](0045-workflow-value-flow-uses-role-owned-bindings-and-immutable-invocation-records.md) §4, §5 and §6 and
[ADR 0089](0089-scripts-read-variables-by-name-and-publish-pins-them-as-declared-parameters.md) (writes). Builds on
[ADR 0048](0048-container-output-captures-resolve-through-producer-visible-variable-frames.md) (scoped frames and
captures), [ADR 0028](0028-loop-body-runs-in-a-per-iteration-variable-scope.md) (read-only iteration entries) and
[ADR 0083](0083-javascript-binding-grammar-is-pinned-at-publish.md) (statement bodies).

## Context

Today only two things write a workflow variable: an output capture (ADR 0045 §4, issue #1004) and the Set, Merge and
Reduce intrinsics. ADR 0045 made that deliberate: "Activity completion and expression evaluation do not silently
write variables", and "explicit `Set` nodes remain the only normal variable-write mechanism". Scripts are pure, and
a stateful script "returns typed results" that "can feed an explicit `Set`". On `main` this holds:
`IActivityExecutionContext` exposes no variable API, and the JavaScript engine (`IsolatedJintEngine`) is a closed
JSON value world with no .NET interop.

The model made every write visible in the graph, which keeps replay, value-flow validation and spec 266's ordering
analysis exact. It also costs authors:

- A script or activity that updates several variables must return an object and add a capture or a `Set` per
  variable.
- Reading an object and changing one property, which is ordinary in C#, is impossible: there is nothing to change.
- Elsa 3's `setVariable`, generated `set{Name}()` and `context.SetVariable` have no Elsa 4 form, so the converter
  refuses them (spec 247).
- Module authors cannot hand a script a live .NET object, such as a service or a client, to call methods on.

Elsa's principle is to provide guardrails and diagnostics, not to restrict what the engineer builds (review of
spec 266, 2026-10-23). The question is how activities can change variables without losing what made the write model
safe: atomic commits, replay, and a graph that shows who writes what.

## Decision

### 1. Activities may change variables; input-binding expressions stay pure

RunJavaScript and C# activities may read and change workflow variables while they execute. Input-binding
expressions (the JavaScript or Liquid that computes an activity input) stay pure, exactly as ADR 0045 §5 says.

*Why.* An activity step runs once per commit, so its effects can commit atomically with the step, as captures already
do. An input-binding expression can be evaluated more than once: on replay, on each `While` pass (#977), in
preflight or inspection. A write from an expression would run a variable number of times. That is the hazard ADR
0045 §5 exists to prevent, and it does not apply to activities.

### 2. An activity step is a unit of work over the variables it touches

The promise to authors, the same for scripts and C#: **change what you read, by assigning or by mutating; the
change is saved when the step commits, and nothing is saved if the step faults.**

- A read returns a *tracked* instance. Reading the same variable twice in one step returns the same instance, so
  aliasing behaves as it does in C#.
- Assigning a new value and mutating the tracked instance (`order.total = 1`, including through aliases and helper
  functions) are both changes.
- Later reads in the same step see the step's own changes (read-your-writes). They never see live state from other
  branches: the step works on one consistent snapshot.
- A *read-only read* skips tracking, like EF Core's `AsNoTracking()`, for large values the step does not change.

*Why.* This is what a .NET developer already expects from EF Core's `DbContext` and `SaveChanges`, so there is no new
rule to learn, and it gives the "change, then apply at the right time" behaviour without partial writes. It is
feasible because a durable variable is serializable by definition (that is how it is stored), so "did it change?" is
a comparison of serialized forms, which is how EF Core's snapshot change tracking works.

### 3. Scripts read and write variables by name, resolved at publish

In RunJavaScript, `variables.x`, `variables['x']` (a string literal) and `getVariable('x')` read; assigning to them,
`setVariable('x', v)`, and mutating a read value write. Compound forms (`variables.n += 1`, `variables.n++`) are a read
and a write.

At publish, the same parser and nearest-scope rule as ADR 0089 resolves every touched name to its declaring scope and
pins it. Publish refuses a computed name (`variables[k]`), an undeclared name, and a write to a read-only target (a
loop item or index, ADR 0028). The pinned *may-write set* is every variable the script assigns, mutates, or lets
escape into a local, a function argument or a closure, because each of those can mutate it.

*Why.* Resolving at publish keeps one resolver for reads and writes, keeps "which nodes touch this variable?" exact,
and gives spec 266 a sound write set. Counting escapes as possible writes is conservative: it can add warnings for a
script that only passes a variable to a helper, but it never misses a write.

### 4. C# activities use bound variable references by default and free names by opt-in

- **Bound references (default).** An activity declares an input of type `VariableReference`, which the workflow
  author binds in Studio as they bind a capture target. `context.Variables.Get<T>(target)` returns a tracked copy;
  `Set(target, value)` assigns. Publish knows the target.
- **Free names (opt-in).** `Get<T>("name")` and `Set("name", value)` work only on an activity type marked
  `[AccessesUndeclaredVariables]`. The marker is part of the activity descriptor, so publish, Studio and inspection
  show it. Names resolve at run time to the nearest visible scope. Spec 266 treats a marked activity as touching every
  visible variable.
- `GetReadOnly<T>(…)` is the read-only read.

*Why.* Bound references keep the workflow author in control of what an activity writes and keep the write set
static, like captures. Free names serve Elsa 3 habits and module authors; the marker makes the loss of static
knowledge visible instead of hidden. *Rejected:* free names only (static analysis would be blind to every C#
write); bound references only (unlike Elsa 3, and too strict for generic module activities).

### 5. Undeclared variables are refused

A script that names an undeclared variable fails publication. A free-name C# access to an undeclared name faults the
step before its commit. Nothing is created implicitly.

*Why.* An Elsa 4 variable has a declared type, storage driver and policy. Elsa 3's `setVariable` created variables
on first write; an auto-created Elsa 4 variable would have none of these.

### 6. Changes apply in the step's checkpoint commit

Each step gets a *variable session*: per touched variable, the snapshot value and the tracked instance. When the step
reaches a checkpoint commit (completion, suspension or yield), each tracked variable is serialized with its storage
driver and compared with the snapshot's serialized form. Each changed variable becomes a whole-value write intent.
Intents apply in the same checkpoint commit as the step's outputs and captures, through the scoped frame resolver and
type validation that `Set` and captures already use (ADR 0048). Within one commit, intents apply first and captures
last; when both target one variable, the capture wins and validation warns.

- **Faults.** A failed type check, an undeclared free name or a serialization error faults the step before its
  commit. Nothing is saved.
- **Retries and redelivery.** A new attempt starts a new session from the same pinned input snapshot. A committed
  step is never re-executed (ADR 0045 §3), so intents never apply twice.
- **Suspension.** Changes up to the suspension commit with it; the resumed step starts a new session from committed
  values. Pending intents are never persisted.

*Why.* Applying intents with the step's own commit means there is no moment where the step has completed but its
changes are missing, or the reverse. *Rejected:* turning each intent into a `Set` scheduled after the step (a
separate commit, so another branch can commit in between and a write can fail after the step succeeded); a
post-commit effect journal (eventually consistent, so the next activity can read stale values).

### 7. Background steps check read-then-write conflicts

An inline step runs inside the single drain, so nothing commits between its snapshot and its commit. A step placed
on a background worker (`ActivityExecutionPlacement.Background`) commits later, and other branches may commit in
between. For a variable the step read and then changed, the frame revision serves as a concurrency token: if it moved
since the snapshot, that is a conflict. The default is to fault the step with a message naming the variable. An
activity type may declare a different default (`[VariableConflictPolicy(LastWins)]`), and the workflow author may
override it per node. A blind write (assigned without being read) is never a conflict: the last commit wins, as spec
266 documents.

*Why.* A stale read-modify-write (`count++` on two background branches) loses data silently, which is worse than an
undefined order. Faulting is EF Core's default (`DbUpdateConcurrencyException`); some activities, such as
aggregators, tolerate last-wins, so the choice is per activity. *Rejected:* re-running the step automatically, which
is unsafe for activities with external side effects.

### 8. Live .NET objects reach scripts only through opt-in host interop

By default RunJavaScript keeps today's closed JSON world. A host may switch on host-interop mode, off by default.
In that mode a RunJavaScript activity can read transient variables as live .NET objects and call their methods. Such
an activity is marked `External` (not replay-safe) and runs under ADR 0045's transient execution policy, so it cannot
suspend or migrate. Publish and Studio show both marks. Transient variables are never tracked or saved: a live object
is changed in place and nothing is committed.

*Why.* The closed world exists for good reasons: sandbox limits (timeout, memory, statements) do not bound a .NET
call; live objects do not survive suspension; and method calls have side effects the engine must know about.
Forbidding the case outright would restrict a legitimate need. Opt-in interop, with the activity marked for what it
is, tells the truth about the consequences instead. *Rejected:* closed world only (restricts a legitimate need);
curated host functions only (useful later for portable functions, but they cannot call methods on the author's own
objects).

### 9. What does not change

Output captures, the Set, Merge and Reduce intrinsics, pure input bindings, outputs as typed results, and the rule
that a committed step is not re-executed.

## Consequences

- **ADR 0045.** §5 keeps input-binding purity. §4's "activity completion … do[es] not silently write variables"
  and §6's "explicit `Set` nodes remain the only normal variable-write mechanism" are superseded: a step's changes
  are a third write form beside captures and `Set`. They are not silent, because the may-write set (or the
  `[AccessesUndeclaredVariables]` marker) is part of the published executable and its inspection. §5's "stateful
  scripts … return typed results" stays true, and a script may now also change variables.
- **ADR 0089.** Its "Writes are unchanged: scripts do not write variables" and its open question on writes are
  superseded by this ADR. Publish pinning covers writes as well as reads.
- **Spec 266.** Its rule adds step writers through the may-write set. A marked free-name activity counts as
  touching every visible variable. Its rationale becomes precise: static analysis sees every write except those of
  marked free-name activities. Its ADR 0048 §4 amendment must leave room for the frame revision as the concurrency
  token in decision 7.
- **Converter (spec 247 follow-up).** Inside RunJavaScript, Elsa 3 `setVariable`, generated `set{Name}()` and
  mutation of a read value (`getVariable('o').x = 1`) become convertible. In expressions they stay refused.
- **Inspection.** Each commit records which variables a step changed, which is the data #3290 (variable write
  history and the winning write) needs.
- **Cost.** One serialization per tracked variable per commit. Read-only reads avoid it. The implementation spec
  measures it against a large payload.

## Alternatives considered

**Keep the current model (captures and `Set` only).** Exact and simple, but it keeps the costs listed in Context.
Rejected as the only path; it stays the right choice for most activities.

**Values: variables are immutable values, changed only by assigning.** Cheaper than tracking, and scripts would get
a clear error on mutation. Rejected because C# authors expect to mutate what they read, and a rule they must learn
is a source of confusion.

**Live store, as in Elsa 3.** Variables are shared live objects, written immediately. Impossible with durable,
distributed execution: the next step may run on another node from the serialized value.

**Field-level JSON patches instead of whole-value intents.** Would let two branches change different fields of one
variable. Rejected for now: array patches by index land in the wrong place after another branch changed the array.

## Required evidence for the implementing spec

- A faulted step saves nothing; a retry starts a fresh session; a committed step never reapplies its intents.
- Read-your-writes in scripts and C#, including nested mutation through an alias.
- An unchanged value writes nothing; a changed nested field writes the whole value; a read-only read never writes.
- Publish refuses computed, undeclared and read-only targets, and the may-write set feeds spec 266.
- A background read-then-write conflict faults by default; the type default and the node override work; blind
  writes stay last-wins.
- Host interop is off by default; when on, the activity is marked `External` and transient and cannot suspend.
- Elsa 3 `setVariable`, `set{Name}()` and nested mutation convert inside RunJavaScript and stay refused in
  expressions.
- Serialization cost is measured against a large payload.

## Open questions

1. **Script read-only read.** The JavaScript form of `GetReadOnly` (for example `readVariable('x')`).
2. **Studio.** Whether nodes show their may-write set, and how a marked free-name activity is flagged.
3. **Interop scope.** Whether host-interop mode needs per-type allow-lists, and whether it is switched per host or per
   workflow.
4. **Liquid.** Liquid templates stay read-only; whether Liquid needs any write form is out of scope.
