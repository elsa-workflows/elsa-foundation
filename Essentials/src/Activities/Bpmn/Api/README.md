# BPMN hosting API

This package owns the HTTP boundary for [spec1017](../../../../../specs/1017-direct-bpmn-hosting/spec.md): publication, direct start, exact work completion and execution/command inspection. Authentication, tenant scope and authorization follow the existing Foundation API conventions. Semantic decisions remain in the BPMN implementation; durable coordination remains in the shared runtime.

Implementation is in progress. This initial project registers no feature or routes. Existing native workflow endpoints and embedded `BpmnProcess` execution remain available through their existing packages.
