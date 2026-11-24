# Contributing to Gnoseis for Windows

## Building

You need Windows 10 version 1809 or later, the .NET 10 SDK, and Visual Studio 2022 or later with the **Windows application development** workload. From a Developer PowerShell:

```bash
msbuild Gnoseis.sln /restore /p:Configuration=Debug /p:Platform=x64
```

`dotnet build` can't build the WinUI project. See [Building and running](README.md#building-and-running).

## Tests

The tests need only the .NET SDK, and must pass before a change is merged:

```bash
dotnet test tests/Gnoseis.Core.Tests
```

## Branches and commits

See [Branches](RELEASING.md#branches). In short: every change is a short-lived branch from `main`, named `feature/<issue>-<summary>` or `fix/<issue>-<summary>`, or for what it does when there's no issue. Commit messages are `#<issue>: <imperative summary>`, or just the summary when there's no issue.

## Licence agreement

By contributing to Gnoseis, you agree that your contributions will be licensed under both the open-source license in the `LICENSE` file, with the additional permission in the `LICENSE_EXCEPTION.md` file, and the commercial license described in the `COMMERCIAL_LICENSE` file.

## Third-party packages

A change that adds a package which ships with the app adds its notice to `THIRD_PARTY_NOTICES.md` in the same change: its version, what it's used for, its licence and copyright as found in the package, and any NOTICE file (long texts go in `third-party/`). A new shipped package that isn't under an MIT, BSD or Apache license needs the maintainer's OK first.

## AI-assisted contributions

Gnoseis is developed with the help of AI coding tools, and contributions made with them are welcome under these rules:

- You are the author. You must have read, understood and tested every line you submit, and you must be able to explain and maintain it.
- You must have the right to license the contribution under both licenses above. Don't submit generated code that reproduces third-party code under an incompatible license.
- Commit messages and pull requests follow the same standards as any other contribution.
- Disclosure is optional. You may keep or remove attribution trailers such as `Co-Authored-By`.
