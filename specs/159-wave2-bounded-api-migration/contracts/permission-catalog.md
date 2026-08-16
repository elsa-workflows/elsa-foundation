# Permission Catalog Contract

Modularity's existing catalog contributes `module-management.read` and `module-management.manage`, with manage implying read. Execution Evidence adds one uniquely owned contributor for `execution-evidence.read`, `execution-evidence.delete`, and `execution-evidence.manage`; manage implies delete and read. Endpoint mappings name only the relevant catalog-owned action. Wildcard remains a Foundation evaluator grant and is never cataloged or included in endpoint any-permission policy metadata.
