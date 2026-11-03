# Elsa.Agent.Specialists.Review

A generic human review step for user-defined agents. Agents are authored and published through Studio or
configuration and composed with ordinary Elsa control flow (Sequence, Flowchart, BPMN). `HumanReview` pins exact
content for a permitted person, typically an agent's output. Its outcomes are `Approved` and `Rejected`, and it
performs no effect. Decisions go through `IHumanReviews`; the HTTP endpoints are in
`Elsa.Agent.Specialists.Review.Api`. See [extension points](EXTENSION_POINTS.md).

Enable `SpecialistsReview` together with:
- a current-subject identity provider, such as Foundation ASP.NET Core Identity;
- a distributed lock provider. FileSystem locking is qualified for a single node only.

`DecisionLifetime` (default 15 minutes) bounds how long an issued decision may wait for admission.

The feature runs inside a native workflow runtime host, because it consumes the bookmark dispatcher, the scheduler
queue and the runtime stores. It registers one startup task, `HumanReviewTypesStartupTask`, which registers the
activity type alias.
