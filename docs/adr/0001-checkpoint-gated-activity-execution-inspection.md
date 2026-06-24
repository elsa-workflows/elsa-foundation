# Checkpoint-Gated Activity Execution Inspection

Status: proposed

Activity execution lifecycle changes that create or advance scheduler work will commit through `RuntimeCheckpointCommit`, and activity execution inspection projections will be included in the same checkpoint before dependent scheduler work is enqueued through post-commit scheduler intents. This deliberately replaces direct state writes followed by direct queue writes at scheduler boundaries, because instance inspection must reflect committed runtime evidence and recovery must not observe scheduler work for activity state that was never durably committed.
