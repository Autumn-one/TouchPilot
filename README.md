# GestureSign

GestureSign is a gesture recognition software for Windows tablet. You can automate repetitive tasks by simply drawing a gesture with your fingers or mouse.

[![Release](https://img.shields.io/github/release/TransposonY/GestureSign.svg?style=flat-square)](https://github.com/TransposonY/GestureSign/releases/latest)

## Feature

- Activate Window
- Window Control
- Touch Keyboard Control
- Keyboard simulation
- Key Down/Up
- Mouse Simulation
- Send Keystrokes
- Open Default Browser
- Screen Brightness
- Volume Adjustment
- Run Command or Program
- Launch Windows Store App
- Send Message
- Toggle Window Topmost

## Automatic updates

Release, Portable, and uiAccessRelease builds check the repository's latest published GitHub Release shortly after startup. Debug and Microsoft Store/Centennial builds do not use the self-updater. Drafts and prereleases are not returned by GitHub's `releases/latest` endpoint and are therefore not offered automatically.

Each published runtime needs these two assets, using the release version without a leading `v`:

- `GestureSign-{version}-{runtime}.zip`
- `GestureSign-{version}-{runtime}.zip.sha256`

For example, version `8.2.0` on x64 uses `GestureSign-8.2.0-win-x64.zip`. The client verifies the package SHA-256 before and after elevation, then the updater verifies every file against `release-manifest.json`. Files not listed in the manifest, including Portable `AppData`, are preserved.

## Release manager

Run the standalone release manager from the repository root:

```powershell
dotnet run --project GestureSign.ReleaseManager/GestureSign.ReleaseManager.csproj -c Release
```

Enter the GitHub repository, source directory, personal access token, version, build configuration, runtime, and release notes, then select **Build and publish**. The manager runs `publish.ps1`, creates the ZIP and checksum in `artifacts/release-manager/packages`, creates or updates the matching GitHub Release, and replaces assets with the same names.

To remove a Release, enter its repository, token, and version, then select **Delete Release** and confirm the destructive action. This removes the GitHub Release and its assets but preserves the matching Git tag.

Use a fine-grained GitHub personal access token scoped to the target repository with **Contents: Read and write** permission. The token is kept in memory only and is not saved or passed to the build process. Publish a non-draft, non-prerelease Release when it should be offered to existing installations.
