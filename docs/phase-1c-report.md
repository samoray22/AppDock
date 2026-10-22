> Historical AppBox validation baseline. Current product: **AppDock**. See [Phase 2A](phase-2a-report.md) for current behavior.

# AppBox Phase 1C — interactive editing and adaptive launcher

Version remains 0.1.0. Baseline: `cece3ea80db8bf2c50ae4ebca0d989aa31822eff`. Implementation branch: `feature/phase-1c-interactive-edit`. The existing WPF application, local configuration, folder management, launch approval, single instance behavior, and taskbar identity were extended in place.

## Implemented

- Header Edit/Done control and transient interaction state. Edit Mode survives focus changes, suppresses icon launching, and never sets Topmost. Done restores the existing focus-loss and successful-launch dismissal rules, including the close-after-launch preference. Escape leaves Edit Mode before dismissing. Explicit dismissal resets Edit Mode.
- Shared import service for launcher browsing, drag-and-drop, and Settings additions. Local `.exe` and executable `.lnk` references preserve arguments and working directories. HTTP/HTTPS `.url` references use a validated destination and the Windows association icon. No import path calls the process launcher.
- Standard FileDrop and bounded, validated Shell IDList clipboard input. Known AppsFolder parsing references must match the installed discovery catalog; arbitrary shell commands, text, URLs masquerading as commands, and unsupported virtual formats are rejected. The IDList implementation follows Microsoft's [Shell clipboard format](https://learn.microsoft.com/en-us/windows/win32/shell/clipboard) and [CIDA](https://learn.microsoft.com/en-us/windows/win32/api/shlobj_core/ns-shlobj_core-cida) definitions.
- Multi-item imports with per-item errors and resolved invocation duplicate prevention. Equivalent copied shortcuts are rejected; differing arguments/directories can remain distinct. Existing duplicates are not automatically removed. No files are copied, run, deleted, or uninstalled during import/removal.
- Highlighted external drop area, Add applications indicator, Copy/Move effects, inline confirmation/errors, and a short icon fade respecting animation settings. Full messages are available in the status tooltip.
- Icon drag reordering, visible insertion bar, edge scrolling, and immediate persistence. Internal drags use an application-specific typed identity and never fall through to external import, even when malformed. Context menus offer display-name rename, reference removal, and file-location selection where applicable.
- Compact empty/small launchers; measured header/footer size plus viewport-dependent grid rows; a 770-DIP preferred maximum further constrained by the monitor's physical working area. Larger folders scroll. The grid extent follows current row/column counts even after hidden-window collection resets. Resizing retains the captured launch anchor and uses existing per-monitor DPI/boundary placement. No Explorer or taskbar internals were modified.
- Automatic atomic configuration writes through the existing store. Launcher operations roll back their in-memory changes if persistence fails, with a visible failure. Folder/application IDs and renamed display names survive reload. Edit state and drag feedback are not serialized.

## Automated and native Windows validation

Validation uses `scripts/validate.ps1 -Smoke` on Windows 11 build 26300 with .NET SDK 10.0.101. The Release build completes with zero warnings and zero errors. **39 regression tests** cover the 21 baseline checks plus edit state transitions, normal dismissal policy, real shortcut import, mixed/multiple imports, equivalent-copy duplicates, unsafe links/URLs, URL launch dispatch, discovery-backed Shell references, real Shell IDList decoding, ordering, persistence/reload, blocked edit-mode launching, failed-save rollback, stale/cross-folder drags, and adaptive grid geometry.

The opt-in native harness opens the actual WPF windows using temporary configuration and genuine Windows Shell icons. It passes the following checks:

| Check | Evidence |
| --- | --- |
| Compact empty and small normal folder | Visible content fits with no unnecessary vertical scrollbar; height below 400 DIP |
| Three real shortcuts imported | Standard WPF FileDrop data is passed through the same handlers used by the window; all three real icons appear |
| Edit focus changes | Three native focus-change cycles retain the launcher and Edit Mode with Topmost false |
| Drop feedback and reordering | Copy feedback and Move insertion are exercised; order is persisted |
| Invalid internal drag | A mixed object carrying invalid internal data cannot be imported as external files |
| Rename/remove and persistence | IDs, final order, and display name match a fresh configuration-store load; original shortcut remains |
| Done and normal dismissal | Focus change hides the launcher again |
| Launch after editing | A dedicated real executable is launched through a shortcut; its marker confirms arguments/working directory, and the popup closes |
| Large folder | 48 entries create a bounded, genuinely scrollable grid |
| Baseline Windows integration | Second-process activation/folder routing, real shell launch, themes, icons, and JSON roundtrip remain covered |

The harness supplies known diagnostic approval for its harmless launch probe only. Ordinary launches retain the original session approval dialog. Test output and fresh captures are generated under ignored `artifacts/smoke`; reviewed delivery copies are retained below. Captures show live WPF client content without OS window frames or desktop surroundings.

## Screenshots

| State | Capture |
| --- | --- |
| Normal Mode after edits | [launcher-normal.png](phase-1c-screenshots/launcher-normal.png) |
| Edit Mode with Done and Add Application | [launcher-edit.png](phase-1c-screenshots/launcher-edit.png) |
| External drop feedback | [edit-drop-feedback.png](phase-1c-screenshots/edit-drop-feedback.png) |
| Internal insertion indicator | [edit-insertion.png](phase-1c-screenshots/edit-insertion.png) |
| Compact empty state | [normal-empty.png](phase-1c-screenshots/normal-empty.png) |
| Scrollable large folder | [launcher-scrollable.png](phase-1c-screenshots/launcher-scrollable.png) |

## Manual acceptance status and limitations

The complete physical Windows 11 acceptance workflow is **not certified**. The computer-use runtime fails during initialization because its sandbox launcher enumerates the BitLocker-locked `F:` drive. Security settings were not changed. The native harness drives the real handlers with standard data objects; it does not perform a real cross-process OLE drag gesture from Explorer or Start.

| Requested manual action | Result |
| --- | --- |
| Pin and open AppBox from taskbar | Not performed; standard pin/grouping verification remains outstanding |
| Click Edit, switch to Explorer, drag three shortcuts | Handler import, icons, and focus policy passed; actual Explorer/Desktop gesture not performed |
| Drag icons, right-click rename/remove, click Done | Production handlers/state and persistence exercised programmatically; physical pointer/context-menu workflow not performed |
| Launch an ordinary application after Done | Dedicated real launch probe passed; physical approval/one-click workflow with a user app not performed |
| Restart AppBox and inspect saved changes | Fresh store/view-model rehydration passed; manual full close/reopen inspection not performed |
| Repeated edits and focus changes | Regression operations and three native focus cycles passed |
| Mixed DPI / multiple physical monitors / taskbar auto-hide | Existing geometry and DPI code retained; full physical matrix remains unperformed |

Start-menu drops depend on the formats exposed by Windows. The supported virtual format is Shell IDList Array; file descriptors/virtual file streams, arbitrary shell namespaces, URL text drops, and elevation-mismatched sources are not supported. `.lnk` targets that resolve to scripts, documents, or shell namespaces remain unsupported; use a catalog entry or a supported direct Shell app reference for packaged apps. `.url` support is limited to local text shortcuts with safe HTTP/HTTPS destinations; embedded credentials, other schemes, oversized files, and multiple destinations are rejected. Custom internet-shortcut IconFile directives are ignored.

Exact taskbar button alignment and independent folder pinning remain outside this phase. Existing pointer/working-area placement limitations are documented in [windows-integration.md](windows-integration.md). Editing does not keep the window in front of Explorer; bring AppBox into view if another window covers it. No installer, updater, release package, remote push, or independent-pinning feature was added.

## Git delivery

Changes are committed on the dedicated feature branch and merged into current `main` with a merge commit after validation. Release tests and native smoke checks are repeated on the merged tree. The final merge hash and clean status are reported in the delivery message.
