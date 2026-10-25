# Releasing

Releases are automated by [`.github/workflows/ci.yml`](../.github/workflows/ci.yml). Publishing requires the
repository owner's configuration: nothing is published from a pull request or a fork.

## Releasing a new version

The version is the `Version` property of
[`src/Meziantou.DocumentationGenerator/Meziantou.DocumentationGenerator.csproj`](../src/Meziantou.DocumentationGenerator/Meziantou.DocumentationGenerator.csproj).
To release, update it to `{major}.{minor}.{patch}` or `{major}.{minor}.{patch}-{prerelease}` (for example `1.2.0`,
`1.3.0-preview.1`) and merge the change to `main`. No tag is needed: when a build of `main` produces a version that is
not on nuget.org yet, it is published, then the GitHub release `v{version}` is created. Builds of `main` whose version is
already on nuget.org publish nothing.

The version is validated as a semantic version. A version with a prerelease suffix creates a GitHub prerelease.

## Pipeline

1. `build` (Linux, Windows, macOS): build, pack, install Chromium and run every test against the packed tool. On Linux,
   it also verifies the package (`eng/Test-ToolPackage.ps1`: identity, version, command name, .NET 11 runtime, packaged
   rendering dependencies, license, README, schema, no dependencies), installs the package in a clean temporary tool
   manifest and generates the example site with it (`eng/New-SampleSite.ps1`), and writes the release notes with the
   SHA-256 of the package (`eng/New-ReleaseNotes.ps1`). The package, notes and checksums are uploaded as the `release`
   artifact. This runs for every pull request, without any secret and with read-only permissions.
2. `release-check`: only on `main`, after every `build` job succeeds. It reads the version of the validated package and
   checks whether it is on nuget.org. It has no secret, so builds that publish nothing never wait for an approval.
3. `publish`: only when the version is not on nuget.org. It downloads the exact validated package and pushes it to
   nuget.org (`eng/Publish-NuGetPackage.ps1`). No package is rebuilt after validation. Releases run one at a time.
4. `github-release`: after a successful `publish`, creates the GitHub release with the package, notes and checksums.
5. `deploy-site`: after a successful `publish`, deploys the example site to GitHub Pages when the repository variable
   `DOCGEN_DEPLOY_PAGES` is `true`. The site is generated with the repository name as base path and
   `https://{owner}.github.io` as site URL.

## Permissions and credentials

- The workflow defaults to `contents: read`. `publish` adds `id-token: write`, `github-release` adds `contents: write`, and
  `deploy-site` adds `pages: write` and `id-token: write`.
- NuGet uses [trusted publishing](https://learn.microsoft.com/nuget/nuget-org/trusted-publishing): configure a trusted
  publishing policy on nuget.org for this repository, the `ci.yml` workflow and the `nuget` environment, and set the
  repository variable `NUGET_USER` to the nuget.org account. No API key is stored in the repository.
- Restrict the `nuget` environment to the `main` branch, and optionally protect it with required reviewers to require an
  approval before anything is published. The approval is only requested when the version is new.
- Pull request workflows never have access to these environments, and documentation generation never runs package build
  logic.

## Reruns

- Rerunning the failed jobs of a release is safe. If the version is already on nuget.org with the same content (the
  repository signature added by nuget.org is ignored), the publish step succeeds without pushing. If it is published
  with different content, the step fails: a version can never be replaced.
- An existing GitHub release is updated with the artifacts instead of failing.
- A failure in `github-release` or `deploy-site` after a successful `publish` is reported as a failed run; rerun the failed
  jobs. Rerunning all the jobs does not work: the version is now on nuget.org, so `release-check` skips the release.
- When two commits are pushed to `main` in quick succession after a version update, both runs may see the version as
  new (the nuget.org index is updated a few minutes after a push). The second `publish` then fails because the version
  is published with different content; this failure can be ignored.

## Recovery

nuget.org versions cannot be deleted or replaced. To withdraw a bad release, unlist it on nuget.org (and optionally
deprecate it), then publish a new patch version. Delete or edit the GitHub release if needed.

## .NET 11 SDK policy

The SDK is pinned in `global.json`. While .NET 11 is in preview, update the pinned preview SDK deliberately (CI uses
exactly that SDK), keep the tool targeting `net11.0`, and mention the required runtime in the release notes. When .NET 11
is released, pin the stable SDK and release a new version built with it.
