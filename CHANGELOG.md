# Changelog

All notable changes to **AppDock** will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

---

## [2.9.2] - 2026-11-04

### Added
- **🌐 Dual Native & English Language Selector**:
  - Appended the English language name in parentheses beside every native language option (e.g., `Türkçe (Turkish)`, `Español (Spanish)`, `Русский (Russian)`), preventing users from getting trapped in an unfamiliar language or script.
  - Added the English `(Language)` label across all 10 non-English dictionaries for the `General.LanguageHeading` section header (e.g., `Dil (Language)`, `Sprache (Language)`, `Язык (Language)`).
  - Expanded Settings language ComboBox width to 320 px to cleanly fit dual-language names.
  - Added test assertions verifying dual-language format and English header persistence across all dictionaries.
  - Added full localization dictionaries for Dutch (`Nederlands` / `nl`), Turkish (`Türkçe` / `tr`), and Russian (`Русский` / `ru`).
  - Maintained 100% dictionary parity (434 keys per language) across all UI elements, tooltips, dialogs, tray menus, 41 container categories, and 10 container opening animations.
  - Added culture auto-detection and live hot-reloading support for all three languages.
  - Expanded automated test suite in `tests/AppBox.Tests` to verify all 11 supported languages.

---

## [2.9.1] - 2026-11-01

### Fixed
- **🔍 Missing UI Strings in Settings Window**:
  - Resolved missing string bindings in Settings Appearance tab ("Popup Grid Layout", "App Ordering", maximum column slider, and manual/recent ordering options).
  - Fixed 10 3-D container opening animation descriptions and Random animation tagline.
  - Corrected Taskbar behavior headings, hover delay descriptions, and Jump List pinning guidance strings.
  - Added dedicated Sidebar layout mode card strings (`Sidebar.Layout.Single.*` and `Sidebar.Layout.Multiple.*`) across all 8 supported languages.
  - Added automated test assertion verifying 100% of all XAML `DynamicResource` localization keys exist in baseline dictionary with zero missing keys.

---

## [2.9.0] - 2026-11-01

### Added
- **🌍 Multi-Language Support (LTR Languages + System Default)**:
  - Added comprehensive multi-language support for English, Spanish (`Español`), French (`Français`), German (`Deutsch`), Italian (`Italiano`), Portuguese (`Português`), Chinese Simplified (`中文 (简体)`), Japanese (`日本語`), and System Default (Auto-detect).
  - High-performance, zero-disk-overhead architecture using compiled string dictionary tables in `AppBox.Core`, fully compatible with single-file binary publishing.
  - 100% dictionary key coverage (430 keys per language) across all UI elements, tooltips, buttons, jump lists, tray menus, dialogs, and error messages.
- **⚡ Live In-Place Language Hot-Reloading**:
  - Implemented `LanguageResourceService` leveraging WPF `DynamicResource` mappings (`Str.*`) to provide instant hot-reloading when changing language without restarting AppDock.
  - Dynamic tray context menu and dialog prompt updates in sync with language selection.
- **⚙️ Language Selector in General Settings**:
  - Added dedicated **Language** section in **Settings > General**, situated between **Taskbar Layout Mode** and **Other Options**.
  - Widened Settings navigation tab headers to 172 px to accommodate localized headings cleanly across German, French, Italian, and Portuguese.
- **🎨 Localized Built-in Container Categories & Animations**:
  - All 41 built-in container categories now display in the user's selected language while preserving invariant canonical IDs for backwards configuration compatibility.
  - All 10 macOS-style 3-D container opening animation titles, taglines, and descriptions are fully localized.
- **🧪 Automated Multi-Language Test Suite**:
  - Recreated `tests/AppBox.Tests` with automated tests verifying 100% dictionary parity across all 8 languages, culture code resolution, formatting, category translation, and configuration persistence.

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
