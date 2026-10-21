# AppBox Phase 1 implementation report

Date: 2026-10-21. Version: 0.1.0. Platform exercised: Windows NT 10.0.26300 (Windows 11), .NET SDK 10.0.101.

## Delivered

The Visual Studio-compatible four-project solution provides a WPF launcher, folder picker, icon grid, automatic/foreground-loss dismissal, opening fade, Settings interface, local configuration, Start-menu and AppsFolder discovery, shell icons, executable/shortcut/packaged identity launch support, current-user startup registration, a notification-area entry, and single-instance reactivation. Folder create/rename/delete, adding/removing apps, and move-up/down ordering save automatically. Folder IDs remain stable.

Settings includes General, Folders, Appearance, Taskbar, Startup, and About. Layout selection is saved, and Multiple AppBoxes mode routes validated folder GUID activation to the existing process. Independent pins are expressly deferred.

JSON writes are flushed and atomically replaced with backup on the same volume. Malformed configuration is preserved for recovery. Launching uses a dedicated service with validation, session target approval, preserved shortcut arguments/directories, and friendly failures. No configuration is launched during startup. No administrator privilege, Explorer modification, remote service, or third-party runtime package is required.

The UI follows the written rounded, Fluent-inspired visual direction with system fonts and explicit light/dark resources. A separate approved design reference was not available, so visual-concept equivalence is not claimed.

## Validation

The Release solution builds with zero warnings and zero errors, with compiler warnings treated as errors. All **21 regression tests passed**. The regression executable covers folder CRUD and stable IDs, name validation, app CRUD/reordering, duplicate references, invalid/missing/remote paths, packaged IDs, atomic JSON persistence/backup, both layout modes and settings, malformed/null/unknown configuration recovery, actual Windows shortcut COM resolution, launch error handling, approval cancellation, argument/directory preservation, interpreter blocking, packaged shell dispatch, real executable launching, and popup geometry.

The opt-in smoke run uses isolated temporary configuration. It discovers eight installed applications and extracts eight real icons on this machine. It renders the running launcher in both themes and the actual Settings/folder-management views. A separate AppBox process activates the original and routes a folder GUID. A native focus probe verifies deactivation dismissal. A real test executable launches through a `.lnk`, receives its arguments and working directory, writes a marker, and the production launcher closes on the successful launch request. The test harness supplies approval only for this isolated, known test entry; ordinary app launches use the review dialog.

The precise command results are recorded in `screenshots/smoke-result.txt`. Screenshots are lossless captures of rendered client content from live WPF windows, excluding OS title bars and desktop surroundings; they are not design mockups. They were visually reviewed after correcting theme/background and folder-title template issues.

| Capture | File |
| --- | --- |
| Launcher, light | [launcher-light.png](screenshots/launcher-light.png) |
| Launcher, dark | [launcher-dark.png](screenshots/launcher-dark.png) |
| Settings, dark | [settings-dark.png](screenshots/settings-dark.png) |
| Folder management, light | [folders-light.png](screenshots/folders-light.png) |

## Limitations and acceptance status

Build, native launch, configuration/CRUD logic, real icons, real executable/shortcut dispatch, second-process reactivation, automatic closing, and persistence have automated evidence. The entire manual acceptance matrix is **not** certified. Physical taskbar launch/grouping, exact button alignment, real mixed-DPI monitor transitions, mouse-driven workflows, packaged app foreground activation, live OS theme switching, logon startup, and a separate standard-user session remain unperformed. The computer-use runtime was unavailable because its launcher encountered a locked BitLocker volume; no security settings were changed to work around it.

Discovery intentionally uses reliable Start-menu and Windows AppsFolder entries, not speculative uninstaller registry commands or a scan of every executable. Start-menu shortcuts that ultimately resolve to documents/scripts/shell namespaces are listed but fail safely when launched; use the packaged catalog entry where available. Folder references do not track application uninstall/relocation automatically. Network executable references are unsupported. Duplicate prevention operates on reference targets, allowing distinct shortcuts that represent different launch arguments.

Taskbar positioning is an approximation using the pointer and monitor working area. Independent pinning is unimplemented and unverified. Details, future design considerations, and the remaining manual test matrix are in [windows-integration.md](windows-integration.md). The startup toggle reflects actual HKCU state on application startup; external registry edits while the app is already open are not continuously monitored. A successful shell launch request does not guarantee the target app's eventual initialization.

No installer, updater, advanced independent-pinning feature, remote push, or Explorer/taskbar patch is included.

## Git delivery procedure

The repository was initialized with `main`; implementation was developed on `feature/phase-1-foundation`. Accepted files are committed on that feature branch and merged into `main` with a merge commit. The merged build, regression suite, and native smoke run are rerun; final status and the merge commit hash are reported in the delivery message. Generated `bin`, `obj`, and diagnostic artifacts are ignored. No remote push is performed.
