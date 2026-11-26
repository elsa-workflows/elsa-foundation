# Allow graph outcomes to converge at the public boundary

Boundary Outcome Mappings allow several graph outcomes to map to the same public outcome, while each graph outcome maps to at most one public outcome. This lets a Graph Activity Definition hide internal branching details—for example, both `TimedOut` and `Rejected` can expose `Failed`—without making mappings ambiguous in the graph-to-boundary direction.
