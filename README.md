# Generated maps

Generated from [`86ca6a171c17d3e9f5a8a5ffe32a7c9a5384ba2c`](https://github.com/elsa-workflows/elsa-foundation/commit/86ca6a171c17d3e9f5a8a5ffe32a7c9a5384ba2c) of `main`. Links into the source tree are pinned to that commit.

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
