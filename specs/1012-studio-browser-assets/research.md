# Research: Prebuilt Studio Browser Assets

## Ownership and source moves

Decision: use existing Hosting as the only Razor owner, moving Client, StudioSdk and StudioUi beside its project. Rationale: #3352 proved the SDK package/publish model and #3355 fixed this boundary; Web is a nonpackable distribution host. Separate shell-assets or re-export packages add no demonstrated consumer/lifecycle value. Keep private workspace package identities stable and update physical paths. The source inventory found active references in module tests, browser fixtures, SDK/UI exports, npm/package workflows, Docker and contributor tools; frozen consolidation maps and completed historical specs remain historical.

## URLs, caching and customization

Decision: preserve `/studio`, its actual import map and stable emitted bundle names, module `_content` URLs and current Hosting middleware. Rationale: current index and modules use absolute routes/imports, and the spike proved root-based Razor assets. Same-path overlay is rejected by the SDK; support separately named fallback and direct-reference build/buildTransitive opt-out. Reject a runtime prefix switch that relocates only some URLs. No embedded-resource server or copy-target framework.

## Versions and shared browser runtime

Decision: retain private workspace source versions and existing computed/injected package versions; qualify actual discovery compatibility against the candidate artifacts. Host/SDK defaults derive from StudioApi assembly informational version, module version from its declaring assembly, and default required ranges are unconstrained. SDK/UI workspace facades re-export the same Client sources bundled for the host. Moving paths needs no fixed version literal or public API change.

## Producer and isolated proof

Decision: fail packing incomplete producer output and qualify actual production packages from an owned source snapshot, not the spike's wrapper IDs. Copy full committed source/build context plus exact generated Hosting/Dashboard payload into that owned producer; retain provenance/hashes. Consumer has an isolated cache, candidate source mapping and failing frontend shims, then runs after owned producer/feed/source/cache are hidden. Rationale: other active programs share the real checkout, so renaming it would be disruptive. The owned actual-project snapshot provides the same missing-source proof without touching those programs. No unknown external technology choice remains; no research delegation or implementation delegation is needed.
