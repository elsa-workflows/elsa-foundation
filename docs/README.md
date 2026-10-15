# Elsa Foundation docs

Start here if you are a developer who wants to run, understand or contribute to Elsa 4. The pages are grouped by what you are trying to do.

> **Two terms you will see:** the **glossary** is where each concept is defined once; other pages summarise and link back. The **constitution** is the pair of rule files under `.specify/memory/` that hold the architecture gates a change must pass. Use these docs to learn; use the constitution when a review points you at a gate.

## Run it

- [Docker Hub quickstart](docker-hub-quickstart.md): run the prebuilt images with plain `docker run`, supply your own `shells.json`, and see how Studio feature toggles are saved.
- [Docker and compose](docker.md): the production image for Elsa.Workbench, and the PostgreSQL + Elsa.Workbench + Elsa Studio reference stack.

## Understand it

Read these roughly in order:

1. [Architecture tour](architecture-tour.md): what Elsa 4 is made of and the patterns that hold it together.
2. [How a workflow executes](how-a-workflow-executes.md): one request followed from the server's entry point to a saved checkpoint.
3. [Seams and bridges](seams.md): a worked example of how the activities and workflows domains connect.
4. [Runtime fault behaviour](runtime-fault-behavior.md): what happens when an activity throws versus when a runtime handler throws, and the defaults behind each.
5. [Recovering after a crash](runtime-durable-resumption.md): durable storage versus durable resumption, crash windows A/B/C, and the at-least-once delivery rule.
6. [Durable timers](runtime-durable-timers.md): the `Delay` activity, the durable timer store and pump, why firing is safe to repeat, and the at-least-once guarantee of Timer/Cron start triggers.
7. [Bookmark expiration](bookmark-expiration.md): the `ExpiresAt` cut-off, why it is not a timeout, and examples of finite and indefinite waits.

## Rules to follow

- [Serialization rule](serialization.md): how payloads must be serialised, and the exceptions.

## Operate it

- [Foundation host package feeds](foundation-host-feeds.md): pointing `Elsa.Foundation.Host` at a directory or remote NuGet feed, and why a wildcard include pattern finds nothing on a remote feed.
- [Secrets key rotation](secrets-key-rotation.md): rotating the key that protects stored secrets.

## Look things up

| You need… | Go to |
|---|---|
| A framework term | [Root glossary](glossary/root.md) |
| An Elsa term | [Elsa glossary](glossary/elsa.md) |
| Step-by-step task descriptions (tool-neutral, usable by people and AI assistants) | [Skill catalog](skills/catalog.md) |
| Ideas for a first question to ask about the repo | [First-user prompt options](reference/first-user-prompts.md) |
| Worked examples, case studies and walkthroughs | [Reference docs](reference/README.md) |
| Repo maps and what the generated maps should contain | [Maps index](maps/README.md) |
| Inventories, unfinished work and verification reports | [Reports index](reports/README.md) |

## How these docs are organised

- Define each concept once, in the glossary. Other pages may summarise briefly, then link to it.
- The constitution's draft history and open work go in reports, not in the gate text.
- Worked examples and long walkthroughs go in reference docs, not in the gate text.
