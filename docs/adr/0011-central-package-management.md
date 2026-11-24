# 0011 — Central Package Management for NuGet versions

- **Status:** Accepted
- **Date:** 2026-11-18

## Context

Six projects share a small set of Microsoft, EF Core, Serilog, Scalar and test packages. Keeping
a `Version` on every `PackageReference` makes version drift easy: a security patch can land in
the API but not in the integration tests, and a reviewer has to grep six files to answer "which
EF Core version is this?". The repository also runs `dotnet list package --vulnerable` in CI, so
one authoritative version list makes the audit meaningful.

## Decision

Use NuGet Central Package Management (CPM):

- `Directory.Packages.props` at the repository root sets
  `ManagePackageVersionsCentrally=true` and holds one alphabetically sorted `PackageVersion`
  per package.
- Projects keep bare `<PackageReference Include="..." />` items; non-version metadata
  (`PrivateAssets`, `IncludeAssets`) stays with the reference.
- A missing central version is a build error (NU1010), so a new package cannot be added
  without recording its version in one place.
- Transitive pinning is deliberately **off**. Instead, `RestorePackagesWithLockFile=true`
  commits each project's resolved graph in `packages.lock.json`; CI and Docker restore with
  `--locked-mode`. This preserves reproducibility without freezing every indirect package in
  `Directory.Packages.props`.

## Consequences

- Bumping a package is a one-line change; every project moves together.
- CI, `dotnet list package` and Dependabot/Renovate all read the same source of truth.
- Project files are shorter and diffs no longer repeat version strings.
- Lock files make the same commit restore the same transitive versions on every machine.
- The conversion was validated as version-neutral: 277 resolved package entries were identical
  before and after, so this is a build-tooling change, not a dependency upgrade.

## Alternatives considered

- **Leave versions on `PackageReference`** — rejected: drift and noisy diffs.
- **`Directory.Build.props` version properties** (`$(EfCoreVersion)`) — workable, but it
  re-implements what the SDK now supports natively and loses the NU1010 guard.
