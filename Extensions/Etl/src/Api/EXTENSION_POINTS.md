# ETL API transport infrastructure

Internal transport helpers shared by independently composed design and runtime APIs. This assembly adds no public extension points, endpoints or feature activation. It centralizes trusted request scope, bounded signed cursors and safe problem rendering without a design-store or worker dependency.

See [ETL Core](../Core/EXTENSION_POINTS.md), [Design API](../Design/Api/EXTENSION_POINTS.md) and [Runtime API](../Runtime/Api/EXTENSION_POINTS.md) for their owned contracts.
