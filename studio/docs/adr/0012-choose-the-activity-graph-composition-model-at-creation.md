# Choose the Activity Graph Composition Model at creation

A Graph Activity Definition chooses Flowchart, Sequence, or BPMN when it is created, defaulting to Flowchart, and opens that graph in the shared graph-authoring experience. Studio does not expose the provider manifest schema or an everyday root-activity replacement control; changing the composition model after authoring has begun is an explicit destructive migration because it can invalidate the existing graph structure.

Existing schema 1 graphs remain editable in the shared designer and are identified as a legacy graph format. Studio never migrates them implicitly on open or save; capabilities that require schema 2, including multiple public graph outcomes, remain unavailable until the user confirms an advertised migration.

The selected composition root is the graph workspace's scope owner rather than an ordinary node on its own canvas. The canvas shows its child activities; root settings remain available through scope navigation and inspection without a persistent root selector.

Creation persists the Activity Definition identity and its initial draft atomically with the selected authorized composition root already present. If Studio cannot resolve the chosen root from the authorized activity catalog, it blocks creation rather than leaving an incomplete identity to be patched afterward.
