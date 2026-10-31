# Changelog

All notable changes to **AppDock** will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

---

## [2.8.1] - 2026-10-31

### Fixed
- **🚀 Dynamic Executable Path Resolution**:
  - `AppPaths.Executable` automatically detects the running executable file name and location (including versioned executables such as `AppDock_v2.8.1.exe`).
  - Ensures taskbar jump lists, container shortcuts, and startup registrations point to the exact active binary instead of a hardcoded `AppDock.exe`.
- **🔄 Robust Windows Startup Registration**:
  - Added support for Windows `StartupApproved` registry keys, ensuring enabling "Start with Windows" in AppDock properly un-disables AppDock if previously disabled in Windows Task Manager or Settings.
  - Implemented self-healing startup repair (`StartupRegistration.RepairPath()`) that automatically updates broken startup paths if the AppDock executable was moved or renamed.
  - Promoted the "Start AppDock with Windows" setting to the top of Other Options in Settings.

---

## [2.8.0] - 2026-10-31

### Added
- **🎲 Random (Shuffle) Animation Choice**:
  - Added a new `Random (Shuffle)` container opening animation option in Settings under **Appearance > Container Opening Animations**.
  - Dynamically applies a random 3-D mesh effect (*Genie, Suck, Scale, Magic Lamp, Origami Fold, Page Curl, Black Hole, Stage Flip 3D, or Jelly Bounce*) each time a container is opened via hover or click.
  - Smart randomization prevents immediate back-to-back duplicate animations for a fresh and varied experience.
  - Closing seamlessly reverses the animation style chosen during open.
- **⚡ Configurable Animation Duration (Speed)**:
  - Added an **Animation duration (speed)** slider under Appearance settings.
  - Allows full customization from `100 ms` (super-fast & snappy) to `2000 ms` (cinematic slow-motion), with a balanced default of `320 ms`.
  - Proportional 3-D mesh physics scaling preserves the distinctive mathematical curve of each animation type across all speeds.
  - Instant live replay on slider adjustment in the interactive 3-D showcase stage.
- **🛠️ Robust Publishing Script**:
  - `publish.ps1` now automatically manages and closes running development instances before writing standalone binaries.

---

## [2.7.0] - 2026-10-31

### Added
- **macOS-Style 3-D Mesh Warp Animations**:
  - Added 9 pure WPF 3-D mesh deformation effects:
    - *Genie*: Classic stretch and pour into the taskbar or sidebar tile.
    - *Suck*: Asymmetric vacuum pull along a concave 3-D wave.
    - *Scale*: Smooth proportional macOS zoom and fade.
    - *Magic Lamp*: Sinusoidal S-wave ribbon sway.
    - *Origami Fold*: 3-D accordion pleats that fold into the launcher.
    - *Page Curl*: Cylindrical page peel rolling into the target icon.
    - *Black Hole*: Relativistic spiral vortex pulling into a gravity well.
    - *Stage Flip 3D*: macOS Stage Manager perspective swing and pitch.
    - *Jelly Bounce*: Elastic damped harmonic spring with squash, stretch, and wobble.
  - Interactive live preview stage in Settings Appearance tab with replay capability.
- **Lateral Edge Sidebar**:
  - Pin AppDock containers to the left or right screen edge with customizable handle height, width, opacity, and color.
  - Auto-hide on fullscreen apps and multi-monitor edge routing.
- **Interactive Micro-Animations**:
  - macOS dock magnification (fisheye icon zoom).
  - Elastic launch bounce on app start.
  - 3-D parallax card tilt on hover.
  - Removal poof cloud animation.
- **Customizable Layouts & Themes**:
  - Windows 11 Fluent dark and light styling with system follow option.
  - Container manager with custom icon selector, grid row/column controls, and drag-and-drop reordering.
  - Native Windows taskbar jump list integration.
