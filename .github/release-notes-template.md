## What's new

<!-- Replace this comment with what changed in {{VERSION}}, before publishing. -->

## Installing

Download `Gnoseis-v{{VERSION}}-win-x64.zip`, unpack it anywhere and run `Gnoseis.exe`. To keep the data next to the app (for example on a USB stick), put a file named `portable` next to `Gnoseis.exe`.

Releases before 1.0 aren't code-signed, so Windows warns about them:

- **SmartScreen** shows "Windows protected your PC" the first time. Select **More info**, then **Run anyway**.
- **Smart App Control** (Windows 11) blocks unsigned apps where it's on.

## Verifying the download

SHA-256 of the zip:

```
{{SHA256}}
```

```powershell
Get-FileHash Gnoseis-v{{VERSION}}-win-x64.zip -Algorithm SHA256
```

The zip was built by GitHub Actions from this tag. To check its build attestation, with the [GitHub CLI](https://cli.github.com/):

```bash
gh attestation verify Gnoseis-v{{VERSION}}-win-x64.zip --repo boredace/Gnoseis-Windows
```

## Requirements

Windows 10 version 1809 (build 17763) or later, x64. Nothing else needs to be installed.
