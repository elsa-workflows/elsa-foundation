# Support multiple outcomes for every Activity Definition

Every Activity Definition may expose multiple public outcomes regardless of its implementation provider, preserving the branching semantics available in Elsa 3. The Public Interface owns those provider-neutral outcomes; each provider owns how its implementation produces them, with Graph Activity Definitions using explicit Boundary Outcome Mappings.
