# Elsa 4 talk: teleprompter script

Read top to bottom. Lines in [brackets] are cues for the slide, not words to say. About 538 spoken words, roughly 4 to 5 minutes at a relaxed pace.

## Where we're heading

*[Title slide or the full diagram, whichever you prefer]*

Let me start with where we think Elsa is going.

Elsa started as a workflow engine for .NET. With Elsa 4, we want it to be the workflow engine and the BPMN engine for .NET, on a foundation that can also run data pipelines.

We could build a separate product for each of those. I don't think that's what you'd want. Each one would need its own storage, its own timers, its own way of recovering after a crash, and its own tooling.

So the direction is one platform with several execution models. The models stay different, because they should. The parts that don't need to be different, we share.

One thing before I show it. This is where we're heading. It's a target, not a list of what ships today.

## The picture

*[Show the diagram. Start at the top.]*

Here's how it fits together. I'll go from top to bottom.

## 1. Studio

*[Point at the top band and the tabs]*

At the top is Elsa Studio. One place to design, inspect and operate.

It isn't one editor for everything. Each model brings its own: a workflow designer, a BPMN modeler, a pipeline designer. We share the tooling around them, not the notation.

## 2. Execution models

*[Point at the three cards, left to right]*

Next, the execution models. There are three in this picture: BPMN, native workflows, and ETL.

*[Native workflows, the middle card]*

Native workflows are built from compositions: Sequence, Flowchart, and agent collaboration, with a state machine possible later. Each composition owns its own rules for ordering, routing and joining.

*[BPMN, the left card. Follow the amber arrow.]*

BPMN keeps its own semantics: tokens, events, gateways and scopes. It can run on its own, or a native workflow can embed it through an activity. Either way it's the same interpreter. We don't want two BPMN engines.

*[ETL, the right card. Follow the teal arrow.]*

ETL pipelines own partitions, checkpoints and backpressure. It works the same way in the other direction: a native workflow can start a pipeline through an activity, and the pipeline keeps its own rules.

*[The plus n slot, far right]*

And on the far right, the plus n. We want room for more models, as separate packages, without reworking the foundation for each one. I'll be upfront that this is the least proven part.

## 3. Shared foundation

*[Point at the gray band with four boxes]*

Under the models is the shared foundation. Four things. The identity and lifecycle of an execution. Durable state and delivery. Timers, correlation and recovery. And work dispatch and observability.

These are shared contracts and infrastructure. Each model still keeps its own state and its own meaning.

## 4. Hosting and distributed work

*[Point at the bottom band]*

At the bottom is hosting and distributed work. It has two jobs.

The first is execution coordination. There's one boundary per workflow or process execution, and it can be hosted as an actor or a virtual actor. Not one per activity, and not one per record.

The second is work execution: background workers, external tasks and ETL partition workers. The coordinator hands out work and gets results back.

Hosting is pluggable. There's a built-in host. Orleans and Proto.Actor are possible adapters, not something we've delivered. Hosting decides where things run. It never decides what a model means.

*[Optional, if someone asks about guarantees]*

One more honest note. Delivery is at least once, so external effects need to be idempotent. We're not promising exactly once.

## Wrap up

*[Back to the full diagram]*

So that's the idea. One platform, multiple execution models. Shared tooling on top, a shared durable runtime underneath, and each model keeps its own meaning in the middle.

Thanks for listening. I'd like to hear which of these you'd care about first.
