# Data Model: Runtime Elsa 3 Migration Boundary

## Elsa3MigrationInputKind

Identifies the Elsa 3 source shape being migrated. Authored workflow definition shapes are accepted; persisted workflow instance/runtime state is rejected by this slice.

## Elsa3WorkflowDefinitionImportInput

An accepted authored-definition import request. It carries the input kind, source name, and parsed `Elsa3WorkflowDefinition`.

## Elsa3MigrationDiagnostic

A machine-readable migration diagnostic with severity, code, message, optional source path, guidance, and metadata.

## Elsa3MigrationResult

Success/failure envelope that carries either a mapped value or diagnostics. Failure requires at least one error diagnostic; success cannot carry error diagnostics.

## Elsa3WorkflowDefinitionImporter

Mapping boundary that converts accepted Elsa 3 authored definitions to Elsa 4 workflow definition versions and reports diagnostics for unsupported or invalid input.
