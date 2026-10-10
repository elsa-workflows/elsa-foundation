# Mediate agent harness capabilities through Elsa tools

DeerFlow-class agent harness providers can own long-running runs, skills, tools, artifacts, memory, sandbox execution, and sub-agent progress, but Elsa Studio must not expose those raw harness capabilities directly to users. Elsa-owned tools and review-first proposals remain the product trust boundary so filesystem, shell, MCP, memory, and business operations are scoped, auditable, permissioned, and reversible where possible.
