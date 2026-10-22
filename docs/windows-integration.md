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
