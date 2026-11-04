# Generated maps

Generated from [`f1c5ac33e70d20168cac50b9b01b31fb39e9798f`](https://github.com/elsa-workflows/elsa-foundation/commit/f1c5ac33e70d20168cac50b9b01b31fb39e9798f) of `main`. Links into the source tree are pinned to that commit.

This branch is written only by the `Publish generated maps` job in `.github/workflows/ci.yml`, after every push to `main`. Do not commit to it or open pull requests against it. To regenerate the same set locally from a checkout of `main`:

```bash
dotnet run --project tools/maps/Elsa.Maps.Generator -- all
```

What each map holds, and how to read them: [docs/maps/README.md on main](https://github.com/elsa-workflows/elsa-foundation/blob/main/docs/maps/README.md).

## Files

- [docs/maps/architecture-reference-map.md](docs/maps/architecture-reference-map.md)
- [docs/maps/dependency-map.json](docs/maps/dependency-map.json)
- [docs/maps/domain-map.md](docs/maps/domain-map.md)
- [docs/maps/extension-point-map.md](docs/maps/extension-point-map.md)
- [docs/maps/feature-dependency-map.md](docs/maps/feature-dependency-map.md)
- [docs/maps/feature-map.md](docs/maps/feature-map.md)
- [docs/maps/manifest.json](docs/maps/manifest.json)
- [docs/maps/package-map.md](docs/maps/package-map.md)
- [docs/maps/project-reference-map.md](docs/maps/project-reference-map.md)
- [docs/maps/spec-status-map.md](docs/maps/spec-status-map.md)
- [docs/maps/test-map.md](docs/maps/test-map.md)
- [docs/reports/maps-v1-findings.md](docs/reports/maps-v1-findings.md)
- [docs/reports/maps-v2-findings.md](docs/reports/maps-v2-findings.md)
