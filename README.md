# TouchPilot

TouchPilot is a Windows touchpad and gesture automation utility. It can automate repeated tasks with touchpad, touchscreen, pen, and mouse gestures.

[Releases](https://github.com/Autumn-one/TouchPilot/releases)

## Build and run locally

Double-click **`Build-TouchPilot.cmd`** in the repository root to build the complete,
self-contained Windows x64 application. This requires the .NET 10 SDK and
PowerShell 7. The equivalent command is `pwsh -NoProfile -File ./publish.ps1`.

Double-click **`Start-TouchPilot.cmd`** in the repository root to open the latest
control panel and start its gesture daemon. The complete runnable application is
always in **`artifacts/latest/`**, with `TouchPilot.exe` as its main executable.
The launcher works from any current directory and does not require the SDK.
Exit TouchPilot from the tray before replacing a running latest build.

The administrator startup setting applies to manual launches as well as sign-in.
The control panel reuses a matching elevated startup task when available; otherwise
Windows requests UAC elevation. Direct launches of `TouchPilot.exe` also honor the
setting. Changes apply after exiting TouchPilot from the tray and starting it again.
Cancelling UAC does not silently start the gesture service with normal permissions.

Successful publishes refresh this fixed location, including publishes with an
explicit `-OutputDirectory`. Compilation and packaging happen separately from
the latest build, so a failed build leaves the previous application available.
Portable `AppData` is preserved. The default publish work directory is
`artifacts/publish/`; `-SkipLatest` is reserved for diagnostic or intermediate
publishes. `dotnet build` and `dotnet test` alone are development checks and do
not assemble the complete runnable distribution.

`build-release.ps1 -Version <version>` also refreshes the latest application from
the Release distribution after creating and checking both release packages.
Release archives retain their versioned names and existing output locations.

Run the build-output regression checks with
`pwsh -NoProfile -File ./scripts/tests/LatestBuild.Tests.ps1`.

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

## Open-file permissions

The **Open File or Website** command has a **Permissions** selector. New commands
and existing settings without this field default to **Normal permissions**.
When TouchPilot is elevated, normal launches use the signed-in desktop user's
Explorer Shell. **Administrator permissions** uses Windows `runas`, requesting
UAC elevation when necessary. Cancelling UAC cancels that launch.

Normal launches require a non-elevated Windows desktop when TouchPilot is
elevated. If it is unavailable, the command fails instead of inheriting
TouchPilot's administrator token. Windows still enforces the target program's
own elevation manifest and shortcut settings. Administrator launches of
documents or URLs require an association that supports `runas`; unsupported
associations fail without retrying at normal permissions.

Run the permission regression tests with:

```powershell
dotnet test GestureSign.Tests/GestureSign.Tests.csproj -c Release --filter FullyQualifiedName~OpenFilePermissionTests
```

To include the real UAC matrix (normal to administrator, administrator to normal,
and administrator to administrator), set `$env:TOUCHPILOT_TEST_ELEVATION = "1"`
before running the command. The test launches temporary permission probes that
exit automatically. Remove the environment variable after testing.

## Automatic updates

Release, Portable, and uiAccessRelease builds check the repository's latest published GitHub Release at startup and every 10 minutes while running. A check can also be started immediately from **Options > System > Check for updates**. Debug builds do not use the self-updater. Drafts and prereleases are not returned by GitHub's `releases/latest` endpoint and are therefore not offered automatically.

Metadata and packages use an ordered mirror funnel. The client stops after the first source passes signature or package validation and contacts later mirrors only after a failure. Update metadata is signed, known pending versions never move backwards when a mirror is stale, and completed packages are verified before elevation. Download and installation run in dedicated progress windows centered on the primary screen; update windows do not expose a cancel action.

Each published x64 release contains these signed update assets, using the release version without a leading `v`:

- `TouchPilot-{version}-win-x64-setup.exe`
- `TouchPilot-{version}-win-x64-portable.zip`
- `TouchPilot-update.json`

For example, version `0.0.1` uses `TouchPilot-0.0.1-win-x64-setup.exe` and `TouchPilot-0.0.1-win-x64-portable.zip`. The client verifies the signed metadata and package SHA-256 before and after elevation, then the updater verifies every portable file against `release-manifest.json`. Files not listed in the manifest, including portable `AppData`, are preserved.

## Release manager

Run the standalone release manager from the repository root:

```powershell
dotnet run --project GestureSign.ReleaseManager/GestureSign.ReleaseManager.csproj -c Release
```

Enter the version and release notes, then select **Build and publish**. The manager reads the repository and personal access token from `TouchPilot.ReleaseManager.config.user`, runs the release build, creates the installer, portable ZIP, and signed update metadata, then publishes an immutable GitHub Release.

To remove a Release, enter its repository, token, and version, then select **Delete Release** and confirm the destructive action. This removes the GitHub Release and its assets but preserves the matching Git tag.

Use a fine-grained GitHub personal access token scoped to the target repository with **Contents: Read and write** permission. Keep `TouchPilot.ReleaseManager.config.user` local and untracked; the token is read by the release manager and is not passed to build subprocesses. Publish a non-draft, non-prerelease Release when it should be offered to existing installations.

## Telemetry service

The desktop client reads its signed endpoint configuration from
`distribution/telemetry-endpoint.json` in `Autumn-one/TouchPilot` at startup. It uses the same ordered mirror fallback as updates and keeps a verified last-known-good cache. The configuration has a monotonic revision and can list multiple endpoints in priority order, so a replacement server can be deployed before the old server is retired.

If configuration discovery or event delivery fails, the client retries after 10 minutes and refreshes the signed configuration. Healthy event delivery does not poll the repository. Pending events stay in a bounded memory queue while the application runs; retries retain their event IDs, so analytics should deduplicate by `eventId` when an acknowledgement is lost. Restarting the application clears the memory queue.

To verify the real client discovery, signature verification, and server ingestion together, run the explicit live test below. It sends one `deployment_check` event marked `source=integration_test`; ordinary test runs do not contact production.

```powershell
$env:TOUCHPILOT_LIVE_TELEMETRY_TEST = "1"
dotnet test GestureSign.Tests/GestureSign.Tests.csproj -c Debug --filter FullyQualifiedName~LiveRepositoryConfigurationDeliversDeploymentCheckToServer
Remove-Item Env:TOUCHPILOT_LIVE_TELEMETRY_TEST
```

The client sends only generated installation, session, and event IDs, the TouchPilot version, distribution, runtime, and bounded event properties. It does not send user names, file paths, window titles, input contents, or source IP fields. The server stores accepted events as daily JSONL files under `/var/lib/touchpilot-telemetry`.

Deploy or update the Linux service on an amd64 or arm64 systemd host with:

```bash
curl -fsSL https://raw.githubusercontent.com/Autumn-one/TouchPilot/main/deploy/install-telemetry-server.sh | sudo bash
```

The service listens on TCP port `4318`. Allow that port in the cloud firewall/security group before clients connect. Check it with `curl http://127.0.0.1:4318/healthz` and inspect it with `systemctl status touchpilot-telemetry`.

Re-running the deployment command upgrades the existing service. The installer verifies the binary before replacing it atomically, preserves the environment file and data, and restores the previous binary and service unit if startup or the health check fails. Concurrent installers are rejected. A configured listen address in `/etc/touchpilot-telemetry/environment` is used for the health check; unusual setups can set `TOUCHPILOT_TELEMETRY_HEALTH_URL` explicitly. Service diagnostics are available with `journalctl -u touchpilot-telemetry --since today --no-pager`.

The installer recovery and mirror fallback tests run without changing system services:

```bash
bash deploy/tests/install-telemetry-server.test.sh
```

To change the public endpoint, deploy the replacement server first, then generate the next signed configuration revision and commit it:

```powershell
dotnet run --project GestureSign.ReleaseManager/GestureSign.ReleaseManager.csproj -c Release -- --generate-telemetry-config . Autumn-one/TouchPilot http://203.0.113.10 4318
```

The repository keeps these distribution roles separate so GitHub's `releases/latest` remains unambiguous:

- Desktop update packages and `TouchPilot-update.json` are immutable GitHub Release assets.
- `distribution/telemetry-endpoint.json` is the signed, replaceable telemetry routing document.
- `distribution/telemetry-server/linux-*` contains the checked Linux service binaries and `checksums.sha256`.
- `deploy/install-telemetry-server.sh` installs those binaries without creating a competing GitHub Release.
