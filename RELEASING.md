# Releasing Gnoseis for Windows

## Branches

Gnoseis uses [GitHub Flow](https://docs.github.com/en/get-started/using-github/github-flow). `main` is the only long-lived branch.

- Every change is a short-lived branch from `main`. A change with an issue is named after it (`feature/45-home-dashboard`, `fix/42-filter-button`); one without is named for what it does (`release/0.3.0`, `chore/branch-rules`).
- Commit messages are `#<issue>: <imperative summary>`, one issue per commit, or just the summary when there's no issue.
- A finished branch is merged into `main` with a merge commit, so each change stays one unit in the history. If the change has an issue, the merge closes it (`Fixes #<issue>`).
- A release is a tag on `main`. Builds for users are only ever made from a tag, never from the tip of `main`.

## Making a release

Not written yet: Gnoseis for Windows hasn't had a release.
