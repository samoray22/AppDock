using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using AppBox.App.Services;
using AppBox.App.ViewModels;
using AppBox.Core;
using AppBox.Infrastructure;

namespace AppBox.Tests;

internal static class Program
{
    private static int passed;
    private static int failures;
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "AppBox-Tests-" + Guid.NewGuid());
    private static string Exe => Path.Combine(Environment.SystemDirectory, "notepad.exe");
    private static AppEntry Entry(string? target = null) => new() { Name = "Notepad", Target = target ?? Exe };

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "--launch-probe")
        {
            File.WriteAllText(args[1] + ".writing", Environment.CurrentDirectory);
            File.Move(args[1] + ".writing", args[1], true);
            return 0;
        }

        Directory.CreateDirectory(Root);

        Console.WriteLine("========================================");
        Console.WriteLine(" AppDock Multi-Language & Core Test Suite");
        Console.WriteLine("========================================\n");

        // 1. Localization Dictionary Integrity Tests
        Test("Baseline English dictionary contains required keys", () =>
        {
            var baseline = LocalizationService.GetDictionary("en");
            Check(baseline.Count > 100, $"Expected >100 baseline keys, found {baseline.Count}");
            Check(baseline.ContainsKey("Settings.Title"));
            Check(baseline.ContainsKey("General.LanguageHeading"));
            Check(baseline.ContainsKey("General.LanguageDescription"));
            Check(baseline.ContainsKey("Common.Done"));
            Check(baseline.ContainsKey("Common.Save"));
            Check(baseline.ContainsKey("Common.Cancel"));
        });

        Test("All 13 supported language dictionaries contain 100% of baseline keys", () =>
        {
            var baseline = LocalizationService.GetDictionary("en");
            var languages = new[] { "es", "fr", "de", "it", "pt", "nl", "tr", "ru", "zh", "zh-Hant", "ja", "ko" };

            foreach (var lang in languages)
            {
                var dict = LocalizationService.GetDictionary(lang);
                Check(dict != null, $"Dictionary for {lang} is null");
                var missing = new List<string>();
                foreach (var key in baseline.Keys)
                {
                    if (!dict!.ContainsKey(key) || string.IsNullOrWhiteSpace(dict[key]))
                    {
                        missing.Add(key);
                    }
                }
                Check(missing.Count == 0, $"Language '{lang}' is missing {missing.Count} keys: {string.Join(", ", missing.Take(5))}");
                Console.WriteLine($"   ✔ Language '{lang}': {dict!.Count} keys verified (100% parity)");
            }
        });

        Test("All XAML DynamicResource Str keys exist in baseline dictionary", () =>
        {
            var baseline = LocalizationService.GetAllBaselineKeys().ToHashSet();
            var dir = AppDomain.CurrentDomain.BaseDirectory;
            string? viewsDir = null;
            while (!string.IsNullOrEmpty(dir))
            {
                var candidate = Path.Combine(dir, "src", "AppBox.App", "Views");
                if (Directory.Exists(candidate))
                {
                    viewsDir = candidate;
                    break;
                }
                var parent = Directory.GetParent(dir);
                dir = parent?.FullName;
            }

            Check(viewsDir != null, "Could not find src/AppBox.App/Views directory");

            var xamlFiles = Directory.GetFiles(viewsDir!, "*.xaml", SearchOption.AllDirectories);
            Check(xamlFiles.Length > 0, "No XAML files found in views directory");

            var regex = new System.Text.RegularExpressions.Regex(@"\{DynamicResource\s+(Str\.[A-Za-z0-9\._]+)\}");
            var missing = new List<string>();
            int totalChecked = 0;

            foreach (var file in xamlFiles)
            {
                var content = File.ReadAllText(file);
                var matches = regex.Matches(content);
                foreach (System.Text.RegularExpressions.Match match in matches)
                {
                    totalChecked++;
                    var fullKey = match.Groups[1].Value;
                    var key = fullKey.Substring(4); // strip "Str."
                    if (!baseline.Contains(key))
                    {
                        missing.Add($"{Path.GetFileName(file)}: {fullKey}");
                    }
                }
            }

            Check(missing.Count == 0, $"Found {missing.Count} missing XAML localization keys out of {totalChecked} checked: {string.Join(", ", missing)}");
            Console.WriteLine($"   ✔ Verified {totalChecked} XAML DynamicResource bindings across {xamlFiles.Length} files (0 missing)");
        });

        // 2. Language Switching and Fallback Tests
        Test("LocalizationService language resolution and switching", () =>
        {
            LocalizationService.SetLanguage(AppLanguage.English);
            Check(LocalizationService.EffectiveCultureCode == "en");
            Check(LocalizationService.Get("Common.Done") == "Done");

            LocalizationService.SetLanguage(AppLanguage.Spanish);
            Check(LocalizationService.EffectiveCultureCode == "es");
            Check(LocalizationService.Get("Common.Done") == "Listo");

            LocalizationService.SetLanguage(AppLanguage.French);
            Check(LocalizationService.EffectiveCultureCode == "fr");
            Check(LocalizationService.Get("Common.Done") == "Terminé");

            LocalizationService.SetLanguage(AppLanguage.German);
            Check(LocalizationService.EffectiveCultureCode == "de");
            Check(LocalizationService.Get("Common.Done") == "Fertig");

            LocalizationService.SetLanguage(AppLanguage.Italian);
            Check(LocalizationService.EffectiveCultureCode == "it");
            Check(LocalizationService.Get("Common.Done") == "Fatto");

            LocalizationService.SetLanguage(AppLanguage.Portuguese);
            Check(LocalizationService.EffectiveCultureCode == "pt");
            Check(LocalizationService.Get("Common.Done") == "Concluído");

            LocalizationService.SetLanguage(AppLanguage.Dutch);
            Check(LocalizationService.EffectiveCultureCode == "nl");
            Check(LocalizationService.Get("Common.Done") == "Klaar");

            LocalizationService.SetLanguage(AppLanguage.Turkish);
            Check(LocalizationService.EffectiveCultureCode == "tr");
            Check(LocalizationService.Get("Common.Done") == "Bitti");

            LocalizationService.SetLanguage(AppLanguage.Russian);
            Check(LocalizationService.EffectiveCultureCode == "ru");
            Check(LocalizationService.Get("Common.Done") == "Готово");

            LocalizationService.SetLanguage(AppLanguage.Chinese);
            Check(LocalizationService.EffectiveCultureCode == "zh");
            Check(LocalizationService.Get("Common.Done") == "完成");

            LocalizationService.SetLanguage(AppLanguage.TraditionalChinese);
            Check(LocalizationService.EffectiveCultureCode == "zh-Hant");
            Check(LocalizationService.Get("Common.Done") == "完成");

            LocalizationService.SetLanguage(AppLanguage.Japanese);
            Check(LocalizationService.EffectiveCultureCode == "ja");
            Check(LocalizationService.Get("Common.Done") == "完了");

            LocalizationService.SetLanguage(AppLanguage.Korean);
            Check(LocalizationService.EffectiveCultureCode == "ko");
            Check(LocalizationService.Get("Common.Done") == "완료");

            // Culture auto-detection for Traditional Chinese
            var originalCulture = System.Globalization.CultureInfo.CurrentUICulture;
            try
            {
                foreach (var code in new[] { "zh-TW", "zh-HK", "zh-MO", "zh-Hant" })
                {
                    System.Globalization.CultureInfo.CurrentUICulture = new System.Globalization.CultureInfo(code);
                    Check(LocalizationService.ResolveCultureCode(AppLanguage.System) == "zh-Hant",
                        $"Expected zh-Hant for system culture {code}, got {LocalizationService.ResolveCultureCode(AppLanguage.System)}");
                }
                System.Globalization.CultureInfo.CurrentUICulture = new System.Globalization.CultureInfo("zh-CN");
                Check(LocalizationService.ResolveCultureCode(AppLanguage.System) == "zh",
                    $"Expected zh for system culture zh-CN, got {LocalizationService.ResolveCultureCode(AppLanguage.System)}");
            }
            finally
            {
                System.Globalization.CultureInfo.CurrentUICulture = originalCulture;
            }

            // Reset to English
            LocalizationService.SetLanguage(AppLanguage.English);
        });

        Test("LocalizationService.LanguageChanged event triggers properly", () =>
        {
            bool eventFired = false;
            Action handler = () => eventFired = true;
            LocalizationService.LanguageChanged += handler;
            try
            {
                LocalizationService.SetLanguage(AppLanguage.Spanish);
                Check(eventFired, "LanguageChanged event did not fire");
            }
            finally
            {
                LocalizationService.LanguageChanged -= handler;
                LocalizationService.SetLanguage(AppLanguage.English);
            }
        });

        Test("LocalizationService format handles parameters safely across languages", () =>
        {
            foreach (var lang in LocalizationService.SupportedLanguages)
            {
                LocalizationService.SetLanguage(lang.Language);
                var formatted = LocalizationService.Format("Launcher.Badge.Apps.Multiple", 5);
                Check(formatted.Contains("5"), $"Formatted string for {lang.Language} does not contain count: {formatted}");
            }
            LocalizationService.SetLanguage(AppLanguage.English);
        });

        // 3. Built-in Container Category Names
        Test("All 41 built-in container categories localize cleanly without altering canonical IDs", () =>
        {
            var categories = ContainerIcons.Categories;
            Check(categories.Length == 41, $"Expected 41 categories, got {categories.Length}");

            foreach (var lang in new[] { AppLanguage.English, AppLanguage.German, AppLanguage.Japanese, AppLanguage.Spanish, AppLanguage.Dutch, AppLanguage.Turkish, AppLanguage.Russian, AppLanguage.Korean, AppLanguage.TraditionalChinese })
            {
                LocalizationService.SetLanguage(lang);
                foreach (var cat in categories)
                {
                    var localized = LocalizationService.GetCategoryDisplayName(cat);
                    Check(!string.IsNullOrWhiteSpace(localized), $"Category '{cat}' localized to empty in {lang}");
                }
            }
            LocalizationService.SetLanguage(AppLanguage.English);
        });

        // 4. macOS-Style 3D Animations Localization
        Test("All 10 container animation styles have localized title, tagline, and description", () =>
        {
            var styles = Enum.GetValues<ContainerAnimationStyle>();
            Check(styles.Length == 10, $"Expected 10 animation styles, found {styles.Length}");

            foreach (var style in styles)
            {
                var title = ContainerWarpMesh.GetTitle(style);
                var tagline = ContainerWarpMesh.GetTagline(style);
                var desc = ContainerWarpMesh.GetDescription(style);

                Check(!string.IsNullOrWhiteSpace(title), $"Style {style} has empty title");
                Check(!string.IsNullOrWhiteSpace(tagline), $"Style {style} has empty tagline");
                Check(!string.IsNullOrWhiteSpace(desc), $"Style {style} has empty description");
            }
        });

        // 5. Configuration Persistence with AppLanguage
        Test("ConfigurationStore persists and reloads AppLanguage", () =>
        {
            var storeDir = Path.Combine(Root, "config-lang-test");
            var store = new ConfigurationStore(storeDir);
            var config = new Configuration();

            config.Settings.Language = AppLanguage.German;
            store.Save(config);

            var loaded = store.Load();
            Check(loaded.Settings.Language == AppLanguage.German, $"Expected German, got {loaded.Settings.Language}");

            config.Settings.Language = AppLanguage.Japanese;
            store.Save(config);

            loaded = store.Load();
            Check(loaded.Settings.Language == AppLanguage.Japanese, $"Expected Japanese, got {loaded.Settings.Language}");
        });

        Test("ConfigurationStore handles invalid or missing language enum gracefully", () =>
        {
            var storeDir = Path.Combine(Root, "config-fallback-test");
            Directory.CreateDirectory(storeDir);
            var json = @"{ ""Settings"": { ""Language"": 999 } }";
            File.WriteAllText(Path.Combine(storeDir, "settings.json"), json);

            var store = new ConfigurationStore(storeDir);
            var loaded = store.Load();
            Check(loaded.Settings.Language == AppLanguage.System, $"Invalid enum should fallback to System, got {loaded.Settings.Language}");
        });

        // 6. SettingsViewModel Integration Test
        Test("SettingsViewModel language list and selection", () =>
        {
            var config = new Configuration();
            var vm = new SettingsViewModel(config, () => { });
            Check(vm.AvailableLanguages.Count == LocalizationService.SupportedLanguages.Length);
            Check(vm.AppVersionText.Contains("3.1.0"), $"Expected version 3.1.0, got {vm.AppVersionText}");

            var deOption = vm.AvailableLanguages.FirstOrDefault(l => l.Language == AppLanguage.German);
            Check(deOption != null, "German option missing in AvailableLanguages");
            Check(deOption!.DisplayName == "Deutsch (German)", $"Expected 'Deutsch (German)', got '{deOption.DisplayName}'");

            // Verify dual-language English name in parentheses for all non-English options
            foreach (var opt in vm.AvailableLanguages)
            {
                if (opt.Language is not AppLanguage.System and not AppLanguage.English)
                {
                    Check(opt.DisplayName.Contains('(') && opt.DisplayName.EndsWith(')'),
                        $"Expected dual-language name with English in parentheses for {opt.Language}, got '{opt.DisplayName}'");
                }
            }

            // Verify General.LanguageHeading contains '(Language)' in all non-English dictionaries
            foreach (var lang in new[] { "es", "fr", "de", "it", "pt", "nl", "tr", "ru", "zh", "zh-Hant", "ja", "ko" })
            {
                var dict = LocalizationService.GetDictionary(lang);
                Check(dict["General.LanguageHeading"].Contains("(Language)"),
                    $"General.LanguageHeading for {lang} should contain '(Language)', got '{dict["General.LanguageHeading"]}'");
            }

            vm.SelectedLanguageOption = deOption;
            Check(config.Settings.Language == AppLanguage.German, "SettingsViewModel did not update config language");
            Check(LocalizationService.CurrentLanguage == AppLanguage.German, "LocalizationService did not switch to German");

            // Reset
            var enOption = vm.AvailableLanguages.FirstOrDefault(l => l.Language == AppLanguage.English);
            vm.SelectedLanguageOption = enOption;
            Check(config.Settings.Language == AppLanguage.English);
        });

        // 7. Core Regression Tests
        Test("Folder create, rename, delete and stable ID", () =>
        {
            var config = new Configuration();
            var manager = new FolderManager(config);
            var folder = manager.Create("Tools");
            var id = folder.Id;
            manager.Rename(folder, "Work");
            Check(folder.Id == id && folder.Name == "Work");
            manager.Delete(folder);
            Check(config.Folders.Count == 0 && config.SelectedFolderId is null);
        });

        Test("Folder names validated", () =>
        {
            var manager = new FolderManager(new());
            Throws<ArgumentException>(() => manager.Create("  "));
            Throws<ArgumentException>(() => manager.Create(new string('a', 61)));
        });

        Test("Application add, remove and ordering", () =>
        {
            var config = new Configuration();
            var manager = new FolderManager(config);
            var folder = manager.Create("Tools");
            var a = Entry();
            var b = Entry(Path.Combine(Environment.SystemDirectory, "mspaint.exe"));
            manager.Add(folder, a, _ => true);
            manager.Add(folder, b, _ => true);
            manager.Move(folder, b, -1);
            Check(folder.Applications[0] == b);
            manager.Move(folder, b, -1);
            Check(folder.Applications[0] == b);
            manager.Remove(folder, a);
            Check(folder.Applications.Count == 1);
        });

        Test("Duplicate target prevention is case insensitive", () =>
        {
            var config = new Configuration();
            var manager = new FolderManager(config);
            var folder = manager.Create("Tools");
            manager.Add(folder, Entry());
            Throws<ArgumentException>(() => manager.Add(folder, Entry(Exe.ToUpperInvariant())));
        });

        Test("Invalid, relative, remote and missing paths rejected", () =>
        {
            foreach (var value in new[] { "notepad.exe", @"\\server\share\app.exe", @"C:\missing\not-here.exe", @"C:\Windows\script.cmd", "https://example.com/app.exe" })
                Throws<ArgumentException>(() => TargetPolicy.Validate(Entry(value)));
        });

        Test("Packaged identity validation", () =>
        {
            TargetPolicy.Validate(new() { Name = "Calculator", Kind = TargetKind.Packaged, Target = "Microsoft.WindowsCalculator_8wekyb3d8bbwe!App" });
            Throws<ArgumentException>(() => TargetPolicy.Validate(new() { Name = "Bad", Kind = TargetKind.Packaged, Target = "shell:AppsFolder\\bad & command" }));
        });

        Test("Popup follows bottom taskbar pointer and clamps at edges", () =>
        {
            var bounds = PopupPlacement.Calculate(new(0, 0, 1920, 1080), new(0, 0, 1920, 1032), new(1900, 1050), 1);
            Check(bounds.X == 1920 - 468 - 8 && bounds.Y + bounds.Height < 1032);
        });

        // 8. Portable Mode & Storage Migration Tests
        Test("Portable mode migration copies settings and custom icons and deactivates source settings", () =>
        {
            var sourceDir = Path.Combine(Root, "storage-appdata");
            var portableDir = Path.Combine(Root, "storage-portable", "AppDock_Data");
            Directory.CreateDirectory(Path.Combine(sourceDir, "Icons"));

            var store = new ConfigurationStore(sourceDir);
            var config = new Configuration();
            var manager = new FolderManager(config);
            var folder = manager.Create("DevTools");
            var customIconPath = Path.Combine(sourceDir, "Icons", "mycustom.ico");
            File.WriteAllBytes(customIconPath, [0, 0, 1, 0]);
            folder.Icon = new ContainerIcon { Kind = ContainerIconKind.Custom, ResourcePath = customIconPath };
            store.Save(config);

            Check(ConfigurationStore.HasSettingsFile(sourceDir), "Source settings should exist before migration");

            var migratedStore = store.MigrateTo(portableDir, config);
            Check(ConfigurationStore.HasSettingsFile(portableDir), "Target portable settings should exist after migration");
            Check(!ConfigurationStore.HasSettingsFile(sourceDir), "Source settings should be deactivated after migration");
            Check(File.Exists(Path.Combine(sourceDir, "settings.migrated.json")), "Source settings should be preserved as settings.migrated.json");
            Check(File.Exists(Path.Combine(portableDir, "Icons", "mycustom.ico")), "Custom icon should be copied to portable Icons folder");
            Check(string.Equals(folder.Icon.ResourcePath, Path.Combine(portableDir, "Icons", "mycustom.ico"), StringComparison.OrdinalIgnoreCase),
                $"Custom icon ResourcePath was not updated to portable folder: {folder.Icon.ResourcePath}");

            // Simulate moving the portable folder to another drive/directory and reloading
            var movedPortableDir = Path.Combine(Root, "moved-usb-drive", "AppDock_Data");
            Directory.CreateDirectory(Path.Combine(movedPortableDir, "Icons"));
            File.Copy(Path.Combine(portableDir, "settings.json"), Path.Combine(movedPortableDir, "settings.json"));
            File.Copy(Path.Combine(portableDir, "Icons", "mycustom.ico"), Path.Combine(movedPortableDir, "Icons", "mycustom.ico"));

            var movedStore = new ConfigurationStore(movedPortableDir);
            var reloaded = movedStore.Load();
            Check(reloaded.Folders.Count == 1);
            Check(string.Equals(reloaded.Folders[0].Icon.ResourcePath, Path.Combine(movedPortableDir, "Icons", "mycustom.ico"), StringComparison.OrdinalIgnoreCase),
                $"Custom icon path did not self-heal after moving portable folder: {reloaded.Folders[0].Icon.ResourcePath}");
        });

        Test("ConfigurationStore persists and reloads Taskbar Magnification settings", () =>
        {
            var dir = Path.Combine(Root, "test-taskbar-mag");
            var testStore = new ConfigurationStore(dir);
            var initial = testStore.Load();
            Check(!initial.Settings.EnableTaskbarMagnification, "Default EnableTaskbarMagnification should be false");
            Check(initial.Settings.TaskbarMagnificationScope == TaskbarMagnificationScope.AllTaskbarIcons, "Default Scope should be AllTaskbarIcons");
            Check(Math.Abs(initial.Settings.TaskbarMagnificationScale - 1.55) < 0.01, "Default Scale should be 1.55");
            Check(initial.Settings.ShowTaskbarMagnificationLabel, "Default ShowTaskbarMagnificationLabel should be true");

            initial.Settings.EnableTaskbarMagnification = true;
            initial.Settings.TaskbarMagnificationScope = TaskbarMagnificationScope.AppDockOnly;
            initial.Settings.TaskbarMagnificationScale = 1.70;
            initial.Settings.ShowTaskbarMagnificationLabel = false;
            testStore.Save(initial);

            var loaded = testStore.Load();
            Check(loaded.Settings.EnableTaskbarMagnification, "EnableTaskbarMagnification was not persisted");
            Check(loaded.Settings.TaskbarMagnificationScope == TaskbarMagnificationScope.AppDockOnly, "TaskbarMagnificationScope was not persisted");
            Check(Math.Abs(loaded.Settings.TaskbarMagnificationScale - 1.70) < 0.01, "TaskbarMagnificationScale was not persisted");
            Check(!loaded.Settings.ShowTaskbarMagnificationLabel, "ShowTaskbarMagnificationLabel was not persisted");

            loaded.Settings.TaskbarMagnificationScale = 5.0;
            testStore.Save(loaded);
            var reloaded = testStore.Load();
            Check(reloaded.Settings.TaskbarMagnificationScale <= 2.0, "Scale was not clamped to max 2.0");
        });

        Test("TaskbarIconResolver clean name extraction", () =>
        {
            Check(TaskbarIconResolver.ExtractCleanName("Google Chrome - 1 running window") == "Google Chrome", "Failed on Google Chrome suffix");
            Check(TaskbarIconResolver.ExtractCleanName("Visual Studio Code - 3 running windows") == "Visual Studio Code", "Failed on VS Code suffix");
            Check(TaskbarIconResolver.ExtractCleanName("Explorateur de fichiers - 2 fenêtres actives") == "Explorateur de fichiers", "Failed on French suffix");
            Check(TaskbarIconResolver.ExtractCleanName("File Explorer") == "File Explorer", "Failed on unsuffixed name");
            Check(TaskbarIconResolver.ExtractCleanName("AppDock - Utilities") == "AppDock - Utilities", "Failed on AppDock container name");
        });

        Test("ContainerWarpMesh.ComputeDockMagnificationScale bell curve behavior", () =>
        {
            double peak = ContainerWarpMesh.ComputeDockMagnificationScale(0.0, influenceRadiusDip: 100.0, maxExtraScale: 0.55);
            Check(Math.Abs(peak - 1.55) < 0.001, $"Peak scale at d=0 should be 1.55, got {peak}");

            double mid = ContainerWarpMesh.ComputeDockMagnificationScale(50.0, influenceRadiusDip: 100.0, maxExtraScale: 0.55);
            Check(mid > 1.0 && mid < 1.55, $"Mid scale should be between 1.0 and 1.55, got {mid}");

            double edge = ContainerWarpMesh.ComputeDockMagnificationScale(100.0, influenceRadiusDip: 100.0, maxExtraScale: 0.55);
            Check(Math.Abs(edge - 1.0) < 0.001, $"Edge scale at d=influence should be 1.0, got {edge}");

            double far = ContainerWarpMesh.ComputeDockMagnificationScale(150.0, influenceRadiusDip: 100.0, maxExtraScale: 0.55);
            Check(Math.Abs(far - 1.0) < 0.0001, $"Far scale beyond influence should be 1.0, got {far}");
        });

        Test("ConfigurationStore persists and reloads Desktop Containers settings and Separate DesktopFolders", () =>
        {
            var dir = Path.Combine(Root, "test-desktop-containers");
            var testStore = new ConfigurationStore(dir);
            var initial = testStore.Load();
            Check(!initial.Settings.EnableDesktopContainers, "Default EnableDesktopContainers should be false");
            Check(!initial.Settings.HideDesktopIcons, "Default HideDesktopIcons should be false");
            Check(initial.Settings.RestoreDesktopIconsOnExit, "Default RestoreDesktopIconsOnExit should be true");
            Check(initial.Settings.DesktopContentSource == DesktopContentSource.SharedWithSidebar, "Default DesktopContentSource should be SharedWithSidebar");
            Check(initial.Settings.DesktopDisplayStyle == DesktopDisplayStyle.CompactFlyout, "Default DesktopDisplayStyle should be CompactFlyout");

            var desktopManager = new FolderManager(initial, useDesktopFolders: true);
            var folder = desktopManager.Create("Desktop Tools");
            desktopManager.Add(folder, Entry());
            initial.Settings.EnableDesktopContainers = true;
            initial.Settings.DesktopContentSource = DesktopContentSource.SeparateDesktopContainers;
            initial.Settings.DesktopDisplayStyle = DesktopDisplayStyle.ExpandedBoxes;
            initial.Settings.DesktopContainerOpacity = 0.80;
            initial.Settings.LockDesktopContainers = true;
            testStore.Save(initial);

            var loaded = testStore.Load();
            Check(loaded.Settings.EnableDesktopContainers, "EnableDesktopContainers was not persisted");
            Check(loaded.Settings.DesktopContentSource == DesktopContentSource.SeparateDesktopContainers, "DesktopContentSource was not persisted");
            Check(loaded.Settings.DesktopDisplayStyle == DesktopDisplayStyle.ExpandedBoxes, "DesktopDisplayStyle was not persisted");
            Check(Math.Abs(loaded.Settings.DesktopContainerOpacity - 0.80) < 0.01, "DesktopContainerOpacity was not persisted");
            Check(loaded.Settings.LockDesktopContainers, "LockDesktopContainers was not persisted");
            Check(loaded.DesktopFolders.Count == 1 && loaded.DesktopFolders[0].Name == "Desktop Tools", "DesktopFolders was not persisted");
            Check(loaded.DesktopFolders[0].Applications.Count == 1, "DesktopFolders applications were not persisted");
        });

        Test("SettingsViewModel manages Shared vs Separate Desktop Containers and CopyFromSidebarContainers", () =>
        {
            var config = new Configuration();
            var sidebarManager = new FolderManager(config);
            var devFolder = sidebarManager.Create("Dev");
            sidebarManager.Add(devFolder, Entry());

            var vm = new SettingsViewModel(config, () => { });
            Check(vm.IsDesktopSharedContent, "Should start in SharedWithSidebar mode");

            vm.IsDesktopSeparateContent = true;
            Check(config.Settings.DesktopContentSource == DesktopContentSource.SeparateDesktopContainers, "Should switch to SeparateDesktopContainers");
            Check(vm.DesktopFolders.Count == 1, "Switching to empty SeparateDesktopContainers should auto-create a default desktop container");

            int copied = vm.CopyFromSidebarContainers();
            Check(copied == 1, $"Expected 1 copied container, got {copied}");
            Check(vm.DesktopFolders.Any(f => f.Name == "Dev" && f.Applications.Count == 1), "Copied container should include applications");

            var bounds = PopupPlacement.CalculateDesktopContainers(new(0, 0, 1920, 1040), 0.99, 0.99, 1.0, 320, 240);
            Check(bounds.X + bounds.Width <= 1920 && bounds.Y + bounds.Height <= 1040, "Desktop container bounds should clamp inside work area");
        });

        Test("DesktopIconsService.EvaluateTransition safeguards Stardock Fences and external desktop icon state", () =>
        {
            // 1. If Fences or Explorer already hid desktop icons (currentlyVisible: false), AppDock should not mark hiddenByApp=true
            var alreadyHidden = DesktopIconsService.EvaluateTransition(
                hideRequested: true,
                currentlyVisible: false,
                wasHiddenByApp: false,
                explicitUserAction: false,
                fencesRunning: true);
            Check(alreadyHidden.Action == DesktopIconsService.DesktopVisibilityAction.None && !alreadyHidden.NewHiddenByApp,
                "Should not claim ownership when desktop icons were already hidden externally");

            // 2. On automatic startup/sync (explicitUserAction: false), if Fences hid icons and AppDock has HideDesktopIcons=false, AppDock must NOT unhide Fences' icons
            var doNotOverrideExternalHide = DesktopIconsService.EvaluateTransition(
                hideRequested: false,
                currentlyVisible: false,
                wasHiddenByApp: false,
                explicitUserAction: false,
                fencesRunning: true);
            Check(doNotOverrideExternalHide.Action == DesktopIconsService.DesktopVisibilityAction.None && !doNotOverrideExternalHide.NewHiddenByApp,
                "Background sync with HideDesktopIcons=false must not unhide icons hidden by Fences");

            // 3. On automatic startup/sync when Fences is running, do not forcefully hide SysListView32 behind Fences' back
            var avoidAutoHideUnderFences = DesktopIconsService.EvaluateTransition(
                hideRequested: true,
                currentlyVisible: true,
                wasHiddenByApp: false,
                explicitUserAction: false,
                fencesRunning: true);
            Check(avoidAutoHideUnderFences.Action == DesktopIconsService.DesktopVisibilityAction.None && !avoidAutoHideUnderFences.NewHiddenByApp,
                "Automatic startup sync should not forcefully hide SysListView32 when Fences is running");

            // 4. Explicit user toggle in AppDock always works and tracks ownership properly
            var explicitHide = DesktopIconsService.EvaluateTransition(
                hideRequested: true,
                currentlyVisible: true,
                wasHiddenByApp: false,
                explicitUserAction: true,
                fencesRunning: true);
            Check(explicitHide.Action == DesktopIconsService.DesktopVisibilityAction.Hide && explicitHide.NewHiddenByApp,
                "Explicit user toggle should hide icons and mark hiddenByApp=true");

            // 5. Restore on exit unhides icons when hiddenByApp=true
            var restoreOwned = DesktopIconsService.EvaluateTransition(
                hideRequested: false,
                currentlyVisible: false,
                wasHiddenByApp: true,
                explicitUserAction: false,
                fencesRunning: false);
            Check(restoreOwned.Action == DesktopIconsService.DesktopVisibilityAction.Show && !restoreOwned.NewHiddenByApp,
                "RestoreOnExit should unhide icons when AppDock was the one that hid them");
        });

        Test("AppFolder per-container DesktopX/DesktopY clamping, IsDesktopCollapsed, and ResetDesktopPosition", () =>
        {
            var store = new ConfigurationStore(Path.Combine(Root, "desktop_box_pos.json"));
            var folder1 = new AppFolder
            {
                Name = "Tools",
                DesktopX = 1.5,
                DesktopY = -0.2,
                IsDesktopCollapsed = false
            };
            var folder2 = new AppFolder
            {
                Name = "Media",
                DesktopX = 0.42,
                DesktopY = 0.65
            };
            var cfg = new Configuration
            {
                Folders = new List<AppFolder> { folder1 },
                DesktopFolders = new List<AppFolder> { folder2 }
            };
            store.Save(cfg);
            var loaded = store.Load();
            Check(loaded.Folders[0].DesktopX == 1.0 && loaded.Folders[0].DesktopY == 0.0,
                "DesktopX/DesktopY should clamp to [0.0, 1.0]");
            Check(!loaded.Folders[0].IsDesktopCollapsed,
                "IsDesktopCollapsed=false should persist");
            Check(loaded.DesktopFolders[0].IsDesktopCollapsed,
                "IsDesktopCollapsed should default to true");
            Check(Math.Abs((loaded.DesktopFolders[0].DesktopX ?? 0) - 0.42) < 1e-6,
                "DesktopFolders DesktopX should persist");

            loaded.Folders[0].DesktopX = null;
            loaded.Folders[0].DesktopY = null;
            loaded.DesktopFolders[0].DesktopX = null;
            loaded.DesktopFolders[0].DesktopY = null;
            store.Save(loaded);
            var resetLoaded = store.Load();
            Check(resetLoaded.Folders[0].DesktopX == null && resetLoaded.Folders[0].DesktopY == null,
                "Cleared per-container DesktopX/DesktopY should persist as null");
            Check(resetLoaded.DesktopFolders[0].DesktopX == null && resetLoaded.DesktopFolders[0].DesktopY == null,
                "Cleared DesktopFolders DesktopX/DesktopY should persist as null");
        });

        Test("SingleInstance WaitOne acquisition and secondary disposal do not block primary re-acquisition", () =>
        {
            var scope = Guid.NewGuid().ToString();
            var primary = new SingleInstance(scope);
            Check(primary.IsPrimary, "First instance should acquire primary lock");

            SingleInstance? secondary = null;
            Task.Run(() =>
            {
                secondary = new SingleInstance(scope);
            }).Wait();
            Check(secondary is not null && !secondary.IsPrimary,
                "Concurrent instance on another thread should not become primary");

            // Even if secondary still holds its handle when primary releases, a new instance can become primary
            primary.Dispose();
            primary.Dispose(); // idempotent

            SingleInstance? nextPrimary = null;
            Task.Run(() =>
            {
                nextPrimary = new SingleInstance(scope);
                nextPrimary.Dispose();
            }).Wait();
            Check(nextPrimary is not null && nextPrimary.IsPrimary,
                "Disposed secondary should not block subsequent primary acquisition");

            secondary!.Dispose();
            secondary.Dispose(); // idempotent
        });

        Test("DesktopRecycleBinX/Y persistence, clamping, Recycle Bin status and icons", () =>
        {
            var store = new ConfigurationStore(Path.Combine(Root, "recycle_bin_pos.json"));
            var cfg = new Configuration
            {
                Settings = new Preferences
                {
                    DesktopRecycleBinX = 1.4,
                    DesktopRecycleBinY = -0.3
                }
            };
            store.Save(cfg);
            var clamped = store.Load();
            Check(clamped.Settings.DesktopRecycleBinX == 1.0 && clamped.Settings.DesktopRecycleBinY == 0.0,
                "DesktopRecycleBinX/Y should clamp to [0.0, 1.0]");

            clamped.Settings.DesktopRecycleBinX = 0.25;
            clamped.Settings.DesktopRecycleBinY = 0.75;
            store.Save(clamped);
            var loaded = store.Load();
            Check(Math.Abs(loaded.Settings.DesktopRecycleBinX - 0.25) < 1e-6 &&
                  Math.Abs(loaded.Settings.DesktopRecycleBinY - 0.75) < 1e-6,
                "DesktopRecycleBinX/Y should persist valid coordinates");

            var (itemCount, totalBytes) = DesktopIconsService.GetRecycleBinStatus();
            Check(itemCount >= 0 && totalBytes >= 0,
                "GetRecycleBinStatus should return non-negative metrics");

            var emptyIcon = ShellIcon.GetRecycleBinIcon(false);
            var fullIcon = ShellIcon.GetRecycleBinIcon(true);
            Check(emptyIcon is not null && emptyIcon.IsFrozen,
                "Empty Recycle Bin stock icon should load and be frozen");
            Check(fullIcon is not null && fullIcon.IsFrozen,
                "Full Recycle Bin stock icon should load and be frozen");
        });

        Test("DesktopBoxWindow grid layout maintains aligned tops, equal spacing, and clean norm round-trip", () =>
        {
            var work = new PixelRect(0, 0, 1920, 1032);
            double scale = 1.0;

            var (l0, t0) = AppBox.App.Views.DesktopBoxWindow.GridCellToDip(0, 0, work, scale);
            var (l1, t1) = AppBox.App.Views.DesktopBoxWindow.GridCellToDip(1, 0, work, scale);
            var (l2, t2) = AppBox.App.Views.DesktopBoxWindow.GridCellToDip(2, 0, work, scale);
            var (l0r1, t0r1) = AppBox.App.Views.DesktopBoxWindow.GridCellToDip(0, 1, work, scale);

            Check(Math.Abs(t0 - t1) < 1e-6 && Math.Abs(t1 - t2) < 1e-6,
                "Containers in the same row should have perfectly aligned tops");
            double gap01 = l1 - (l0 + AppBox.App.Views.DesktopBoxWindow.IconSizeDip);
            double gap12 = l2 - (l1 + AppBox.App.Views.DesktopBoxWindow.IconSizeDip);
            double vGap = t0r1 - (t0 + AppBox.App.Views.DesktopBoxWindow.IconSizeDip);
            Check(Math.Abs(gap01 - AppBox.App.Views.DesktopBoxWindow.GridMarginDip) < 1e-6 &&
                  Math.Abs(gap12 - gap01) < 1e-6 &&
                  Math.Abs(vGap - gap01) < 1e-6,
                "Horizontal and vertical gaps between grid cells should be equal");

            var (nx, ny) = AppBox.App.Views.DesktopBoxWindow.GridCellToNorm(4, 2, work, scale);
            var snapped = AppBox.App.Views.DesktopBoxWindow.SnapNormToGridCell(nx, ny, work, scale);
            Check(snapped.Col == 4 && snapped.Row == 2,
                $"GridCellToNorm should snap back to exact cell (4, 2), got ({snapped.Col}, {snapped.Row})");
        });

        Test("DesktopBoxWindow.DoesExpandedBoxOverlapIcon detects overlapping vs clear neighbor icons", () =>
        {
            var expanded = new System.Windows.Rect(20, 20, 320, 260);
            var neighborInside = new System.Windows.Rect(104, 20, 64, 64);
            var neighborRightOutside = new System.Windows.Rect(356, 20, 64, 64);
            var neighborEdgeTouching = new System.Windows.Rect(340, 20, 64, 64);
            var neighborBelow = new System.Windows.Rect(20, 288, 64, 64);

            Check(AppBox.App.Views.DesktopBoxWindow.DoesExpandedBoxOverlapIcon(expanded, neighborInside),
                "Neighbor icon at (104, 20) inside (20, 20, 320x260) should be detected as overlapping");
            Check(!AppBox.App.Views.DesktopBoxWindow.DoesExpandedBoxOverlapIcon(expanded, neighborRightOutside),
                "Neighbor icon to the right at (356, 20) should not overlap");
            Check(!AppBox.App.Views.DesktopBoxWindow.DoesExpandedBoxOverlapIcon(expanded, neighborEdgeTouching),
                "Edge-touching neighbor at x=340 should not count as overlapping");
            Check(!AppBox.App.Views.DesktopBoxWindow.DoesExpandedBoxOverlapIcon(expanded, neighborBelow),
                "Neighbor icon below at y=288 should not overlap");
        });

        Console.WriteLine("\n========================================");
        Console.WriteLine($" Result: {passed} passed; {failures} failed.");
        Console.WriteLine("========================================\n");

        try { Directory.Delete(Root, true); } catch { }
        return failures == 0 ? 0 : 1;
    }

    private static void Test(string name, Action test)
    {
        try
        {
            test();
            passed++;
            Console.WriteLine("✔ PASS: " + name);
        }
        catch (Exception ex)
        {
            failures++;
            Console.WriteLine("✖ FAIL: " + name + " -> " + ex.Message);
        }
    }

    private static void Check(bool condition, string message = "Assertion failed.")
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new InvalidOperationException("Expected " + typeof(T).Name + " was not thrown.");
    }
}
