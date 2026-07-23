# TouchPilot

TouchPilot is a Windows touchpad and gesture automation utility. It can automate repeated tasks with touchpad, touchscreen, pen, and mouse gestures.

[Releases](https://github.com/Autumn-one/TouchPilot/releases)

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

Each published x64 release contains these signed update assets, using the release version without a leading `v`:

- `TouchPilot-{version}-win-x64-setup.exe`
- `TouchPilot-{version}-win-x64-portable.zip`
- `TouchPilot-update.json`

For example, version `8.2.0` uses `TouchPilot-8.2.0-win-x64-setup.exe` and `TouchPilot-8.2.0-win-x64-portable.zip`. The client verifies the signed metadata and package SHA-256 before and after elevation, then the updater verifies every portable file against `release-manifest.json`. Files not listed in the manifest, including portable `AppData`, are preserved.

## Release manager

Run the standalone release manager from the repository root:

```powershell
dotnet run --project GestureSign.ReleaseManager/GestureSign.ReleaseManager.csproj -c Release
```

Enter the version and release notes, then select **Build and publish**. The manager reads the repository and personal access token from `TouchPilot.ReleaseManager.config.user`, runs the release build, creates the installer, portable ZIP, and signed update metadata, then publishes an immutable GitHub Release.

To remove a Release, enter its repository, token, and version, then select **Delete Release** and confirm the destructive action. This removes the GitHub Release and its assets but preserves the matching Git tag.

Use a fine-grained GitHub personal access token scoped to the target repository with **Contents: Read and write** permission. Keep `TouchPilot.ReleaseManager.config.user` local and untracked; the token is read by the release manager and is not passed to build subprocesses. Publish a non-draft, non-prerelease Release when it should be offered to existing installations.
