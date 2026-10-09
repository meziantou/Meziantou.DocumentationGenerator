# Releasing

Releases are automated by [`.github/workflows/release.yml`](../.github/workflows/release.yml). Publishing requires the
repository owner's configuration: nothing is published from a pull request or a fork.

## Triggers and versions

- Push a tag `v{major}.{minor}.{patch}` or `v{major}.{minor}.{patch}-{prerelease}` (for example `v1.2.0`,
  `v1.3.0-preview.1`) to release that version.
- Or run the workflow manually with a version. Manual runs are dry runs unless `publish` is checked.

The version is validated as a semantic version and passed explicitly to the build and pack steps.

## Pipeline

1. `validate` (Linux): build, pack, install Chromium, run every test against the packed tool, verify the package
   (`eng/Test-ToolPackage.ps1`: identity, version, command name, .NET 11 runtime, packaged rendering dependencies, license,
   README, schema, no dependencies), install the package in a clean temporary tool manifest and generate the example
   site with it (`eng/New-SampleSite.ps1`), and write the release notes with the SHA-256 of the package
   (`eng/New-ReleaseNotes.ps1`). The package, notes and checksums are uploaded as the `release` artifact.
2. `test` (Windows, macOS): build and run every test.
3. `publish`: only for tag pushes or manual runs with `publish`, after `validate` and `test` succeed. It downloads the exact
   validated package and pushes it to nuget.org (`eng/Publish-NuGetPackage.ps1`). No package is rebuilt after validation.
4. `github-release`: creates the GitHub release with the package, notes and checksums.
5. `deploy-site`: deploys the example site to GitHub Pages when the repository variable `DOCGEN_DEPLOY_PAGES` is `true`.
   The site is generated with the repository name as base path and `https://{owner}.github.io` as site URL.

The CI workflow runs the same package verification and example site generation on every pull request, without any
secret and with read-only permissions.

## Permissions and credentials

- The workflow defaults to `contents: read`. `publish` adds `id-token: write`, `github-release` adds `contents: write`, and
  `deploy-site` adds `pages: write` and `id-token: write`.
- NuGet uses [trusted publishing](https://learn.microsoft.com/nuget/nuget-org/trusted-publishing): configure a trusted
  publishing policy on nuget.org for this repository, the `release.yml` workflow and the `nuget` environment, and set the
  repository variable `NUGET_USER` to the nuget.org account. No API key is stored in the repository.
- Protect the `nuget` environment with required reviewers and restrict it to tags `v*` to require an approval before
  anything is published.
- Pull request workflows never have access to these environments, and documentation generation never runs package build
  logic.

## Reruns

- Rerunning a release is safe. If the version is already on nuget.org with the same content (the repository signature
  added by nuget.org is ignored), the publish step succeeds without pushing. If it is published with different content,
  the step fails: a version can never be replaced.
- An existing GitHub release is updated with the artifacts instead of failing.
- A failure in `github-release` or `deploy-site` after a successful `publish` is reported as a failed run; rerun the failed
  jobs.

## Recovery

nuget.org versions cannot be deleted or replaced. To withdraw a bad release, unlist it on nuget.org (and optionally
deprecate it), then publish a new patch version. Delete or edit the GitHub release if needed.

## .NET 11 SDK policy

The SDK is pinned in `global.json`. While .NET 11 is in preview, update the pinned preview SDK deliberately (CI uses
exactly that SDK), keep the tool targeting `net11.0`, and mention the required runtime in the release notes. When .NET 11
is released, pin the stable SDK and release a new version built with it.
