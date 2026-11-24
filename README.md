# Generated maps

Generated from [`e85f0a3ff6f75f8a5cd5c552a6a8b6266d5a3abb`](https://github.com/elsa-workflows/elsa-foundation/commit/e85f0a3ff6f75f8a5cd5c552a6a8b6266d5a3abb) of `main`. Links into the source tree are pinned to that commit.

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
