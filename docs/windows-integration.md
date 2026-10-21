# Windows integration and taskbar limitations

## Phase 1 behavior

- The process uses the explicit AppUserModelID `AppBox.Desktop` and a custom executable icon.
- Only the visible launcher participates in the taskbar. Settings and owned dialogs have `ShowInTaskbar=false`; the app has no permanently visible main window.
- A user/session-specific named mutex allows one running instance. A current-user-only named pipe carries only a validated optional folder GUID. A secondary instance grants foreground permission to the primary process, forwards activation, and exits. The pipe uses BOM-free UTF-8 and explicit buffers to avoid simultaneous preamble-write deadlocks.
- The launcher shows and requests foreground activation. Deactivation hides it, except during app-owned Settings/launch confirmation dialogs. Escape also hides it. Successful launch requests hide it when automatic closing is enabled. A successful shell request does not prove that the launched application finished loading.
- A notification-area icon supplies Open, Settings, and Exit after the popup disappears. Idle work is a blocked asynchronous pipe listener plus Windows preference notifications; no polling timer or embedded runtime is used.
- Per-monitor-v2 DPI awareness is declared in the manifest. Placement uses `MonitorFromPoint`, `GetMonitorInfo`, `GetDpiForWindow`, and `SetWindowPos`. The HWND moves to the selected monitor before DPI is read. Geometry is calculated in physical pixels and constrained to that monitor's working area, including negative origins and simulated side/top taskbar reservations.
- When the pointer is in a reserved taskbar strip it supplies the approximate horizontal/vertical anchor. Otherwise the launcher uses the working-area edge, horizontally centered for the usual bottom taskbar. Auto-hidden taskbars may expose no reserved strip and use this fallback. Very small working areas reduce the popup size; the app grid scrolls.

## What is not certified

Windows offers no general supported public API to retrieve the exact screen rectangle of an arbitrary pinned taskbar button. Pointer-based positioning approximates “above the launcher” for mouse activation; keyboard launch, notification-area activation, and auto-hide cannot promise the exact pin location.

Standard taskbar pinning/grouping still needs hands-on Windows 11 verification. Setting a process AppUserModelID alone does not prove a pinned shortcut has matching identity metadata. Windows may group or retain a pin differently according to its shortcut properties, executable location, and shell cache. Phase 1 does not create, edit, or install taskbar pins automatically.

Multiple AppBoxes currently changes saved mode and accepts `--folder <guid>` routing. All folders still share one process and one standard taskbar identity. Independent folder pins are deliberately not implemented or advertised as functional.

## Future independent-pinning investigation

A supported candidate design would create per-folder shell links with stable `System.AppUserModel.ID`, relaunch command, display name, icon, and a folder GUID in the activation command. Each visible folder window would need a matching window-level AppUserModelID. This must be tested against Windows' grouping, pin persistence, relaunch behavior, process reuse, deleted folders, executable relocation, and taskbar shortcuts with arguments. It may require separate host identities/processes rather than the current one-popup model. A shared executable with distinct arguments alone is not evidence of independent pinning.

Future implementation must use documented Shell identity/shortcut mechanisms and user-driven pinning. Explorer patching, taskbar injection, undocumented pin verbs, and automatic taskbar modification are outside the design.

## Manual matrix still required

| Check | Current evidence / remaining work |
| --- | --- |
| Normal executable launch on Windows 11 | Genuine WPF process ran on build 26300; opt-in smoke run passed |
| Second process activation and folder routing | Automated native two-process smoke passed |
| Deactivation dismissal | Native focus-change smoke passed; physical outside-click sequence not exercised |
| Executable and shortcut launching | Real ShellExecute and resolved `.lnk` argument/working-directory path passed using the dedicated test executable |
| Packaged app launching | Discovery and icons exercised; real packaged foreground launch still requires manual check |
| Taskbar pin launch/grouping/no duplicate button | Requires manual pin and relaunch verification |
| Popup positioning | Geometry regression coverage includes boundaries, negative monitor origins, scaling, and taskbar edges; physical taskbar/button alignment unverified |
| Multiple monitors and mixed DPI | Geometry covered; actual monitor traversal and 100/125/150/200% scale matrix unverified |
| Theme switching | Explicit light/dark windows rendered and inspected; live system-setting change notification unverified |
| Startup | HKCU implementation present; logoff/logon behavior and a normal-user startup toggle still require manual verification |
| Minimum privilege | Manifest requests asInvoker, stores data per-user; separate standard-user account was not exercised |

The interactive computer-use runtime could not initialize because its sandbox launcher tried to enumerate a BitLocker-locked `F:` drive. The app's own isolated native smoke harness supplied the practical evidence above; it does not substitute for the unperformed mouse/taskbar/DPI checks.
