# Decisions in Actor Foundation workflows

The `ActorDecisionsWorkflows` shell feature adds Evaluate Decision to the activity catalog. Select a published decision or choose its latest version at workflow publication time. Publishing verifies the author's decision source grant and stores the exact immutable decision material in the workflow executable.

The workflow start must carry a persisted authenticated user. The actor checks that user's current Evaluate grant in the original tenant before it invokes the decision. The workflow can route on **Done**, **NoMatch** or **Refused**, and read the whole activity result from a compatible typed binding. Artifact export and retained value inspection also check the corresponding decision grants.

Actor execution accepts plain literals and unprotected top-level workflow inputs for this activity. The chooser limits the available source kinds. Publication also checks lexical scope and effective protection, including inside reusable graphs. A protected decision source or a start without an authenticated subject is refused.
