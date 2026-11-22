# Elsa3.Activities.Design.Import.Publishing

Publishes imported Elsa 3 reusable activity versions through the host's regular activity publisher
(`IActivityDefinitionPublisher`), so they run as graph activities (spec 246).

It is its own project because it is the adapter at the Elsa 3 migration boundary. Merging it into
`Elsa3.Activities.Design.Import` would make the import reference the Workflows Publishing API implementation
(framework §2.1, §2.16.1 classes 4–5).

- Feature: `Elsa3ImportActivitiesPublishing` (depends on `Elsa3ImportJsonActivities` and `WorkflowsPublishingApi`).
- Implements the import's replacement contract `IReusableActivityImportPublisher`. Without this feature, applying
  an import that selects a reusable version is refused with `ELS3-REUSABLE-PUBLISHING-UNAVAILABLE`.
