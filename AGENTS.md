# Runnable Build Outputs

- The latest complete TouchPilot application belongs in `artifacts/latest/`.
- Use `Build-TouchPilot.cmd` or `pwsh -NoProfile -File ./publish.ps1` to build the runnable application. A successful publish refreshes `artifacts/latest/`, including when an explicit output directory is used.
- `Start-TouchPilot.cmd` in the repository root is the user-facing launch entry. It opens the latest control panel, which starts the matching gesture daemon through the existing startup flow.
- `build-release.ps1` also refreshes the latest application from its Release distribution after both release packages have been verified. Versioned release archives keep their existing locations.
- Plain `dotnet build` and `dotnet test` are development checks, not complete application deliveries. Before handing off an application change, run the publish command and verify the root launcher.
- Use `publish.ps1 -SkipLatest` only for diagnostic or intermediate builds. Do not deliver a task-specific directory as the latest application.
- Preserve the previous latest build on a failed publish and preserve portable `AppData` when replacing it.
- When refreshing the latest build, Codex may exit the old TouchPilot daemon and control panel without asking the user again. Verify the executable paths belong to the old build being replaced, prefer normal IPC or Windows shutdown requests, and wait for exit before replacing files. If normal shutdown fails, terminate only those verified old-build processes as needed; leave unrelated applications alone.
- After replacing a running build, use `Start-TouchPilot.cmd` to restart it and verify that the control panel and daemon run from `artifacts/latest/`.
