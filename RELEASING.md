# Releasing Gnoseis for Windows

## Version numbers

Versions follow `MAJOR.MINOR.PATCH`, with `-beta.N` for betas. They are set only in `src/Gnoseis.App/Gnoseis.App.csproj`, as `VersionPrefix` (`0.1.0`) and, for a beta, `VersionSuffix` (`beta.1`). Never set `Version` in the project: the SDK then ignores `VersionSuffix`. A beta and its final release share the file version `0.1.0.0`; the full version is the product version.

`main` always carries the version it is heading for. Right after a final release, `main` moves to the next version (see [Making a release](#making-a-release)), so nothing built from `main` shows an older version than the work it contains.

Debug builds add where they were built from to the version, using `git describe`: the number of commits since the last release tag, the commit, and `.dirty` if there were uncommitted changes. For example, `0.1.0-debug.12+a1b2c3d` is 12 commits after the last release tag, at commit `a1b2c3d`. Without git or a release tag, it is `0.1.0-debug`. **Settings › About** and the file properties of `Gnoseis.exe` show the full version. Release builds show the plain version.

## Branches

Gnoseis uses [GitHub Flow](https://docs.github.com/en/get-started/using-github/github-flow). `main` is the only long-lived branch.

- Every change is a short-lived branch from `main`. A change with an issue is named after it (`feature/45-home-dashboard`, `fix/42-filter-button`); one without is named for what it does (`release/0.3.0`, `chore/branch-rules`).
- Commit messages are `#<issue>: <imperative summary>`, one issue per commit, or just the summary when there's no issue.
- A finished branch is merged into `main` with a merge commit, so each change stays one unit in the history. If the change has an issue, the merge closes it (`Fixes #<issue>`).
- A release is a tag on `main`, or on a `hotfix/<version>` branch when fixing an older release. Builds for users are only ever made from a tag, never from the tip of `main`.

## Making a release

Builds for users are made only by GitHub Actions, from the tag ([release.yml](.github/workflows/release.yml)), never on a PC.

1. On a `release/<version>` branch from `main`, set the release version in `src/Gnoseis.App/Gnoseis.App.csproj`: for a beta, `VersionPrefix` `0.1.0` and `VersionSuffix` `beta.1`; for a final release, `VersionPrefix` only.
2. Run the tests:
   ```bash
   dotnet test tests/Gnoseis.Core.Tests
   ```
3. Merge the branch into `main`, with the merge commit named `Release <version>`.
4. Tag the merge commit `v<version>` and push `main` and the tag.
5. The workflow runs the tests, builds the self-contained x64 app, zips it as `Gnoseis-v<version>-win-x64.zip`, attests the build, and creates a **draft** GitHub release with the zip and its SHA-256. Versions with a label after the number, such as betas, are marked as pre-releases.
6. Download the zip from the draft and [verify it](#verifying-a-download).
7. Write the release notes under **What's new** in the draft, and publish it.
8. After a final release, on a `start/<next version>` branch from `main`, set the next version, then merge it into `main` with the merge commit named `Start <next version>`. After a beta, `main` keeps the beta's version until the next release.
9. Close the milestone.

Releases before 1.0 aren't code-signed. Signing goes in the place marked in the workflow, between the build and the zip.

### Fixing an older release

If a released version needs a fix while `main` already holds unreleased work, branch `hotfix/<version>` from that release's tag, set the patch version (for example `0.1.1`), fix it, and release from that branch the same way: tag, workflow, publish. Then merge the fix into `main`. Releases with nothing unreleased on `main` don't need this: they go through `main` as usual.

### Verifying a download

Compare the SHA-256 with the one in the release notes:

```powershell
Get-FileHash Gnoseis-v<version>-win-x64.zip -Algorithm SHA256
```

Check that GitHub Actions built the zip from this repository, with the [GitHub CLI](https://cli.github.com/):

```bash
gh attestation verify Gnoseis-v<version>-win-x64.zip --repo boredace/Gnoseis-Windows
```
