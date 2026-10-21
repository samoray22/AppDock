# AppBox 0.1.0

A native Windows 11 application launcher: compact folders, real application icons, local settings, and a notification-area entry. Phase 1C adds interactive Edit Mode, external drops, internal reordering, and adaptive sizing to the existing 0.1.0 foundation. Built with C#, .NET 10, WPF, and MVVM. No third-party runtime packages, browser runtime, account, telemetry, installer, or updater.

## Build and launch

Requires Windows 11 and a .NET 10 SDK compatible with `global.json`. Open `AppBox.sln` in a Visual Studio installation with .NET 10 and Windows desktop development support, or run:

```powershell
dotnet build AppBox.sln -c Release
.\src\AppBox.App\bin\Release\net10.0-windows\AppBox.App.exe
```

This is a framework-dependent build; running it requires the .NET 10 Windows Desktop Runtime. Keep the executable and its generated output files together. It requests `asInvoker` privileges.

## Use

1. Open **Manage folders**, choose **New folder**, and enter a title.
2. Use **Browse files** for local `.exe` / `.lnk` files, or **Start menu / installed** for the Windows catalog.
3. Select an app to remove it or move it up/down. Removing references does not delete application files. Deleting a folder requires confirmation.
4. Close Settings with **Done** and select a folder in the launcher. Click an icon to launch. On first use in each session, review the resolved target, arguments, and working directory in the approval dialog.
5. Click outside the popup or press Escape to dismiss it. Open AppBox again, or left-click its notification-area icon, to reopen it. The notification-area context menu provides Settings and Exit.

### Edit directly in the launcher

Click **Edit** in the header. The launcher stays open while you use Explorer or the desktop, without becoming always-on-top. Drop one or more local `.exe`, `.lnk`, or supported `.url` files into it, or use **Add Application** to browse through the same import pipeline. If no folder exists, the first import creates **My apps**. Compatible Shell application references are accepted when the drag source supplies the standard Shell IDList format and the target matches Windows' installed AppsFolder catalog.

The drop area highlights with **Add applications** and a Copy cursor. Unsupported items receive a useful status message; mixed batches keep supported items and report skipped items individually. Full status details are available by hovering over the message. Importing never launches the referenced application. Duplicate checks include the resolved target, arguments, and working directory, so copying an equivalent shortcut cannot accidentally add the same invocation again. Distinct shortcut arguments remain distinct.

In Edit Mode, clicking an icon does not launch it. Drag icons to reorder them using the insertion bar; dragging near the grid's top/bottom scrolls a larger folder. Right-click an app to rename its display name, remove its folder reference, or open the reference file's location. Packaged apps do not expose a file-location action. Changes save after each operation; launcher changes are rolled back in memory if saving fails. File removal/uninstall is never performed.

Click **Done** to restore normal launching and focus-loss dismissal. Escape first exits Edit Mode; pressing it again dismisses the launcher. Closing the launcher also leaves Edit Mode. Edit state is transient and does not survive restart. Existing session launch approval and the automatic-close preference are preserved.

Empty and small folders now size to content. More rows increase the height up to a cap, then the grid scrolls. The actual viewport width determines column count, and height changes reuse the original monitor/taskbar anchor rather than following the pointer to Explorer.

General settings control names, automatic dismissal, animation, and layout mode. Appearance selects system/light/dark. Startup registers a quoted executable path with `--background` in the current user's Run key; turning it off removes that entry. Logon starts only the notification-area entry. Startup registration reflects the real current-user registry state when AppBox opens. Changing the executable's location requires re-enabling startup.

Windows' standard taskbar pinning UI can be used for the executable. This Phase 1 implementation does **not** certify pinned-button grouping or independent folder pins. See the [Windows integration notes](docs/windows-integration.md).

## Layout modes

**Single AppBox** opens the last selected folder and ignores folder-specific activation requests.

**Multiple AppBoxes** enables folder-specific routing in the one running application:

```powershell
.\src\AppBox.App\bin\Release\net10.0-windows\AppBox.App.exe --folder <folder-guid>
```

Folder GUIDs are stored in the JSON configuration. This is application-side mode handling; it does not create independent taskbar pins or multiple concurrent popups.

## Local configuration and launch policy

`%LOCALAPPDATA%\AppBox\settings.json` holds folder/application IDs, references, display names, preferences, and layout mode. Saving uses a same-directory temporary file, a flushed write, and replacement with `settings.json.bak` for the previous revision. Invalid JSON/schema/domain data is preserved as `settings.invalid-<timestamp>.json`, followed by a friendly recovery message and a fresh configuration. Unsupported schema versions are preserved, never silently migrated. Read/write access failures are reported. Missing application files stay in the grid so they can be removed; launching revalidates them.

Applications can be local absolute `.exe`, `.lnk`, `.url` references, or validated packaged application IDs from `shell:AppsFolder`. Shortcut resolution preserves executable arguments and working directories. The launch service rejects command interpreters/indirect command launchers, relative paths, UNC executable references, arbitrary URI schemes, and invalid packaged IDs. Internet shortcuts permit only absolute HTTP/HTTPS destinations without embedded credentials; they are revalidated at launch and opened by the default Windows handler. Their Windows association icon is used without honoring arbitrary `IconFile` directives. Packaged applications launch through the Windows Shell rather than an assumed executable path. `.lnk` files resolving to non-executables (including shell namespace links), documents, or command scripts are unsupported; add packaged apps from the catalog or a supported direct Shell reference instead.

Configuration does not contain arbitrary command-line fields for direct executable references. Target approval is session-only and keyed by app ID, resolved path, arguments, and working directory. Changed launch definitions prompt again. It is a review boundary, not a malware scanner or sandbox; approved binaries and the arguments they interpret still need to be trusted. AppBox does not execute configuration during loading. Phase 1C imports additionally compare resolved invocations within each folder; existing reference IDs and order are retained. JSON schema version 1 remains supported, with an additional `InternetShortcut` target kind. Older AppBox builds cannot read configurations containing that new target kind.

## Validation

```powershell
dotnet run --project tests/AppBox.Tests -c Release
.\scripts\validate.ps1 -Smoke
```

`AppBox.Tests` is a dependency-free console regression runner that exits nonzero on failures. Run it explicitly; `dotnet test` does not discover this runner. The smoke option requires an interactive Windows session and no running AppBox instance. It creates isolated temporary configuration, displays genuine WPF windows, captures their rendered client content to ignored `artifacts/smoke`, exercises a real secondary process and a harmless test executable, and exits. It does not change startup registration or pin anything. Diagnostic failures are written under ignored `artifacts/`. Reviewed delivery captures are retained in `docs/screenshots`.

See the [Phase 1C report and screenshots](docs/phase-1c-report.md) for current validation and the remaining manual acceptance matrix. The [Phase 1 report](docs/phase-1-report.md) records the original baseline.

## Solution

| Project | Responsibility |
| --- | --- |
| AppBox.App | WPF Views, ViewModels, Services, Resources, and Controls; tray entry and UI lifecycle |
| AppBox.Core | Folder/application models, validation, CRUD, launch orchestration, popup geometry |
| AppBox.Infrastructure | JSON store, shell COM discovery/icons, shortcut resolution, ShellExecute, registry, single instance IPC |
| AppBox.Tests | Regression and native Windows integration tests with no test framework dependency |

The approved visual reference was not supplied with the requirements. This version follows the written Fluent-inspired direction; it has not been compared with a separate approved design artifact.
