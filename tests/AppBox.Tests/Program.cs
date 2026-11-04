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

        Test("All 11 supported language dictionaries contain 100% of baseline keys", () =>
        {
            var baseline = LocalizationService.GetDictionary("en");
            var languages = new[] { "es", "fr", "de", "it", "pt", "nl", "tr", "ru", "zh", "ja" };

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

            LocalizationService.SetLanguage(AppLanguage.Japanese);
            Check(LocalizationService.EffectiveCultureCode == "ja");
            Check(LocalizationService.Get("Common.Done") == "完了");

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

            foreach (var lang in new[] { AppLanguage.English, AppLanguage.German, AppLanguage.Japanese, AppLanguage.Spanish, AppLanguage.Dutch, AppLanguage.Turkish, AppLanguage.Russian })
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
            foreach (var lang in new[] { "es", "fr", "de", "it", "pt", "nl", "tr", "ru", "zh", "ja" })
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
