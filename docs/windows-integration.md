# AppDock Windows integration

AppDock uses supported Windows Shell `.lnk` files and window property stores. The main process retains `AppBox.Desktop` for compatibility. Container shortcuts and the currently addressed window use `AppDock.Container.<guid-without-hyphens>`. RelaunchCommand, RelaunchIconResource and RelaunchDisplayNameResource are set before AppUserModel.ID. The process keeps one popup and routes secondary launches through the existing current-user/session IPC channel.

Generated shortcuts use a stable GUID filename, quoted executable relaunch command, `--container <guid>`, working directory and persistent `.ico` reference. Updating an icon never changes the GUID or creates another shortcut for that container. Inactive shortcuts and icon resources remain available; routing checks activation state. Multiple mode synchronizes active containers and already-created inactive shortcuts. Single mode keeps existing files and uses the product icon for its main window.

Use Settings → Taskbar → Open Launcher Shortcuts, then Windows Pin to taskbar. AppDock does not claim automatic Windows 11 pinning. A pinned shortcut is managed/copied/cached by Windows, so updating the generated source may not immediately update an existing pin. Manually unpin/repin when necessary. Nothing writes to Explorer taskband internals or the user's pinned-shortcut directory. Exact pinned-button positioning and physical pin grouping across Windows builds remain unverified; popup positioning falls back to the cursor's taskbar edge and monitor working area.

For upgrades, keep `AppBox.App.exe` and its adjacent forwarding runtime files at the original shortcut target location together with the new AppDock output. The shim forwards arguments via ProcessStartInfo.ArgumentList, without a shell or string re-parsing. Old `--folder` arguments remain readable. A moved installation, old cached label/icon, or a shortcut to a different old install needs manual retargeting or repinning. The app does not search or rewrite arbitrary user shortcuts.

Official API references:

- [AppUserModelIDs](https://learn.microsoft.com/en-us/windows/win32/shell/appids)
- [RelaunchCommand](https://learn.microsoft.com/en-us/windows/win32/properties/props-system-appusermodel-relaunchcommand)
- [RelaunchIconResource](https://learn.microsoft.com/en-us/windows/win32/properties/props-system-appusermodel-relaunchiconresource)
- [Windows taskbar pinning workflow](https://support.microsoft.com/en-us/windows/experience/personalization/customize-the-taskbar-in-windows)

The automated suite checks native icon extraction and shortcut metadata/resource stability. WPF diagnostics exercise the current window icon/title, routing and rendering. These checks do not certify physical taskbar pin behavior or clear the Windows icon cache.


## Phase 2B: Jump List commands

`WindowsJumpLists` uses native `ICustomDestinationList.SetAppID`, `BeginList`, `AddUserTasks`, and `CommitList` for the main identity and each independent container identity. Each task is an `IShellLinkW` with executable path, working directory, nonempty action arguments, persistent icon location and `PKEY_Title`. An unfinished list is aborted and COM objects are released. No file associations or Explorer context-menu extension are installed. The three task labels remain static. Their container arguments use GUIDs, never display names.

An explicit AppID is essential: a WPF/process-wide Jump List alone would attach tasks to the main process rather than reliably maintain each dedicated container list. The shortcut and window AppID must match the target list. Task commands use `--settings`, `--edit-container` or `--manage-containers`, optionally followed by `--container <guid>`. One current-user/session primary receives validated versioned IPC. Bare-GUID Phase 2A activation remains accepted. Explicit missing IDs never resolve to another container. Existing settings windows switch section/selection rather than multiply.

Windows controls menu presentation, caching, policy, task ordering and pin identity. Publishing tasks successfully is not proof that every Windows build or existing pin displays them. A cached/old executable pin with a different inferred AppID may need manual repinning. There is no automatic pinning and no direct Explorer menu modification. Native diagnostic publication succeeds on the validation machine; physical taskbar right-click presentation has not been verified because the computer-use runtime is unavailable.

## Popup pointer and lifecycle

The popup remains a single WPF window. A union of rounded body and triangle shares one fill, stroke and shadow; transparent margins reserve room for the pointer and shadow. Bottom-taskbar alignment uses the taskbar-edge cursor x coordinate or popup center as a documented fallback, clamps within the body and converts physical pixels to DIPs using the target window's actual DPI. Top/side edges hide the pointer. Settings can disable it.

Closed → Opening → Open → Closing → Closed is the normal lifecycle; Editing suspends focus-loss dismissal. Repeated presentation of an already open popup does not restart its opening animation. Generation tokens prevent an old animation completion from reopening a closed or editing popup. Escape retains Phase 2A behavior (leave editing, then dismiss). Shutdown cancels animations, clears drag feedback and unsubscribes model handlers. HoverOpen is reserved; unsupported hover cannot enter it or schedule opening/closing timers.

## Hover feasibility

- `ITaskbarList3` and DWM iconic thumbnail/live-preview messages customize representations of running windows. They do not supply the validated pinned-button hover-to-container mapping needed to show an actual interactive popup.
- `ICustomDestinationList`/AppUserModelID provide command routing, not mouse-enter/leave events for a particular pinned shortcut.
- UI Automation is supported for accessibility/testing, but AutomationId is optional and not guaranteed stable across provider versions, while Name is localized and nonunique. No documented Explorer contract was found mapping those properties to AppDock container GUIDs. Production code therefore does not parse Explorer accessibility names/IDs or continuously poll taskbar elements.
- `Shell_NotifyIconGetRect` and notification callbacks address owned notification-area icons. They cannot be used as coordinates/events for independently pinned container buttons.

The tested fallback is normal click-to-open plus Jump List command routing to the real interactive popup. Hover and Click or Hover are disabled and labeled unavailable; opening/closing delay values are reserved, persisted and clamped to 100–2000 ms but do not claim working hover behavior. No global keyboard/mouse hooks, Explorer injection or taskbar-internal changes were added.

Recommended next step: keep click activation in production. Revisit hover if Microsoft provides a stable per-launcher hover/identity contract; alternatively scope a separately designed AppDock-owned dock or notification-area surface whose pointer events AppDock controls. Such a surface would be an explicit product change, not independent taskbar hover support.

Additional official references:

- [ICustomDestinationList.SetAppID](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-icustomdestinationlist-setappid)
- [ICustomDestinationList.AddUserTasks](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-icustomdestinationlist-addusertasks)
- [Taskbar Extensions and DWM previews](https://learn.microsoft.com/en-us/windows/win32/shell/taskbar-extensions)
- [UI Automation identification guarantees](https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-usefortesting)
- [Notification icon rectangles](https://learn.microsoft.com/en-us/windows/win32/api/shellapi/nf-shellapi-shell_notifyicongetrect)


## Phase 2C: official branding and window style

The supplied multi-resolution ICO is the executable icon for both `AppDock.exe` and the legacy forwarding executable. Embedded ICO/PNG resources supply owned WPF windows, Settings/About and the Single preview. A separate persistent output copy of the ICO supplies relaunch metadata, the main `Launchers/AppDock.lnk` and Jump List action icons. Container window/shortcut icon definitions remain unchanged. Existing Windows pins may retain a cached old image and require manual repinning; no icon-cache deletion or taskbar-internal mutation is performed.

General settings wraps the existing serialized LayoutMode, ShowApplicationNames, CloseAfterLaunch and StartWithWindows preferences. The additive UseWindows11Style boolean defaults to true. Preference failures roll back and report inline; startup changes invoke the existing registration service with rollback on save failure. Auto close now honors the existing saved CloseAfterLaunch preference in the production launch path (default true); Windows security prompts and target validation remain intact.

The style option uses documented `DWMWA_WINDOW_CORNER_PREFERENCE` (33), `DWMWA_USE_IMMERSIVE_DARK_MODE` (20), and `DWMWA_SYSTEMBACKDROP_TYPE` (38) with `DWMSBT_MAINWINDOW` (2). It extends the DWM frame into the client area and makes the normal WPF window/composition target transparent only after successful calls and read-back. Disabling/failure restores a solid dynamic theme brush, disables the system backdrop and resets frame margins. The API is supported from Windows 11 build 22621; the full option is disabled below that build. Windows can choose a solid fallback even when the material request is accepted. Transparent layered launcher windows retain their custom geometry and do not claim this native backdrop.

Official references: [DWM system backdrop types](https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/ne-dwmapi-dwm_systembackdrop_type), [window attributes](https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/ne-dwmapi-dwmwindowattribute), [WPF client-area composition](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/graphics-multimedia/extend-glass-frame-into-a-wpf-application), [Mica fallback behavior](https://learn.microsoft.com/en-us/windows/apps/design/style/mica).

WPF content renders cannot capture DWM's compositor-owned Mica. Phase 2C captures use the correct theme surface as an underlay, while separate native read-back records whether corners/backdrop were accepted. Physical taskbar/caption icon and compositor captures remain unverified because the computer-use runtime could not start. The supplied reference is compared against the client content, with intentional product terminology/branding and retained window-size/footer differences documented in the Phase 2C report.
