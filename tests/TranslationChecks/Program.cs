using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

internal static class Program
{
    private static int _checks;
    private static readonly List<string> DirectoryLinks = new();

    private static int Main(string[] args)
    {
        string parent = Path.GetFullPath(Path.GetTempPath());
        string directory = Path.Combine(parent, "ReferToCompendium-TranslationChecks-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            // Optional argument checks the final merged plugin without using the linked catalog.
            Assembly target = args.Length == 0 ? typeof(Program).Assembly : Assembly.LoadFrom(Path.GetFullPath(args[0]));
            Run(target, directory);
            Console.WriteLine("Translation checks passed: " + _checks + " (" + target.GetName().Name + ")");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
        finally
        {
            string resolved = Path.GetFullPath(directory);
            string prefix = parent.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (resolved.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                Path.GetFileName(resolved).StartsWith("ReferToCompendium-TranslationChecks-", StringComparison.Ordinal))
            {
                // Unlink only links created by these checks, before any recursive deletion.
                foreach (string link in DirectoryLinks)
                {
                    if (!Path.GetFullPath(link).StartsWith(resolved + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("Refusing to remove a link outside the test workspace.");
                    Directory.Delete(link, false);
                }
                DirectoryLinks.Clear();
                Directory.Delete(resolved, true);
            }
        }
    }

    private static void Run(Assembly target, string rootDirectory)
    {
        string plugins = Path.Combine(rootDirectory, "baseline", "plugins");
        string config = Path.Combine(rootDirectory, "baseline", "config");
        string directory = Path.Combine(config, "ReferToCompendium", "translations");
        Directory.CreateDirectory(directory);
        var warnings = new List<string>();
        var catalog = new Catalog(target, target, plugins, config, warnings.Add);
        var english = new Dictionary<string, string>
        {
            ["rtc_raven_hint"] = "Click for more info\nCaw!",
            ["rtc_drag_hint"] = "Hold a title and drag up or down.\nChanges save automatically.",
            ["rtc_reset"] = "Reset",
            ["rtc_collapsed_title"] = "Collapsed entries",
            ["rtc_collapsed_help"] = "Click the collapsed entries heading to expand or fold the group (gamepad: select this heading and press A). Use + to restore an entry.\n\nHold a title and drag it up or down. Release at the gold line to move it within its group; changes save automatically. Hold near the top or bottom to scroll. Drop outside the list to cancel. Reset restores the entries currently available in this compendium."
        };
        var korean = new Dictionary<string, string>
        {
            ["rtc_raven_hint"] = "클릭해서 정보 확인\n까악!",
            ["rtc_drag_hint"] = "제목을 누른 채 위아래로 끌기\n순서가 자동 저장됩니다.",
            ["rtc_reset"] = "초기화",
            ["rtc_collapsed_title"] = "접어 둔 항목",
            ["rtc_collapsed_help"] = "접어 둔 항목의 제목을 누르면 목록을 펼치거나 접을 수 있습니다. 게임패드는 이 제목을 선택한 뒤 A 버튼을 누르세요. + 버튼으로 항목을 원래 목록에 되돌립니다.\n\n항목 제목을 누른 채 위아래로 끌어 금색 선에 놓으면 같은 그룹 안에서 순서가 바뀌고 자동 저장됩니다. 목록 위아래 가장자리에서는 자동으로 스크롤됩니다. 목록 밖에 놓으면 취소됩니다. 초기화는 현재 도감에 있는 항목만 되돌립니다."
        };

        AssertValues(catalog, english, "constructor defaults");
        Check(warnings.Count == 0, "bundled English is valid");
        catalog.Load("Korean");
        AssertValues(catalog, korean, "bundled Korean");
        Check(warnings.Count == 0, "bundled Korean is valid");
        catalog.Load("French");
        AssertValues(catalog, english, "unsupported language");
        Check(warnings.Count == 0, "missing optional files are normal");

        Write(directory, "Korean", "rtc_reset: 다시 정렬\nrtc_collapsed_title: 보관\n");
        catalog.Load("Korean");
        Check(catalog.Get("rtc_reset") == "다시 정렬", "external Korean overrides embedded Korean");
        Check(catalog.Get("rtc_collapsed_title") == "보관", "second external key applied");
        Check(catalog.Get("rtc_raven_hint") == korean["rtc_raven_hint"], "partial Korean keeps embedded fallback");
        catalog.Load("English");
        AssertValues(catalog, english, "switching clears Korean overrides");

        Write(directory, "English", "rtc_reset: English override\n");
        catalog.Load("English");
        Check(catalog.Get("rtc_reset") == "English override", "external English applies to English");
        catalog.Load("French");
        AssertValues(catalog, english, "external English does not leak into other languages");
        Write(directory, "French", "rtc_reset: Réinitialiser\n");
        catalog.Load("French");
        Check(catalog.Get("rtc_reset") == "Réinitialiser", "unbundled language accepts external translation");
        Check(catalog.Get("rtc_drag_hint") == english["rtc_drag_hint"], "unbundled language retains English missing keys");

        Write(directory, "Korean", "rtc_reset: null\nrtc_collapsed_title: ~\nrtc_raven_hint:\nrtc_drag_hint: ''\nrtc_collapsed_help: '  '\n");
        catalog.Load("Korean");
        AssertValues(catalog, korean, "null and empty values keep embedded fallback");
        Write(directory, "Korean", "rtc_reset: !!null ignored\nrtc_collapsed_title: NULL\n");
        catalog.Load("Korean");
        AssertValues(catalog, korean, "explicit and uppercase null keep fallback");
        Write(directory, "Korean", "rtc_reset: 'null'\nrtc_collapsed_title: !!str null\n");
        catalog.Load("Korean");
        Check(catalog.Get("rtc_reset") == "null", "quoted null is literal text");
        Check(catalog.Get("rtc_collapsed_title") == "null", "explicit string null is literal text");

        Reject(directory, catalog, warnings, korean, "rtc_reset: changed\nrtc_drag_hint: [unterminated\n", "malformed YAML is transactional");
        Reject(directory, catalog, warnings, korean, "rtc_reset: changed\nrtc_drag_hint:\n  nested: value\n", "nested known value rejects whole layer");
        Reject(directory, catalog, warnings, korean, "rtc_reset: changed\nrtc_drag_hint: [one, two]\n", "sequence known value rejects whole layer");
        Reject(directory, catalog, warnings, korean, "rtc_reset: first\nrtc_reset: second\n", "duplicate key rejects whole layer");
        Reject(directory, catalog, warnings, korean, "rtc_reset: changed\nunknown: first\nunknown: second\n", "duplicate unknown key rejects whole layer");
        Reject(directory, catalog, warnings, korean, "- rtc_reset\n- changed\n", "sequence root rejected");
        Reject(directory, catalog, warnings, korean, "rtc_reset: first\n---\nrtc_reset: second\n", "multiple YAML documents rejected");
        Reject(directory, catalog, warnings, korean, "rtc_reset: changed\n? [not, a, string]\n: value\n", "complex key rejected");

        warnings.Clear();
        Write(directory, "Korean", "rtc_reset: changed\nunknown: ignored\nother_unknown: { nested: ignored }\n");
        catalog.Load("Korean");
        Check(catalog.Get("rtc_reset") == "changed", "unknown keys do not block valid values");
        Check(catalog.Get("rtc_raven_hint") == korean["rtc_raven_hint"], "unknown keys preserve other defaults");
        Check(warnings.Count == 2 && warnings.All(w => w.Contains("unknown")), "each unknown key is reported");
        Check(catalog.Get("unknown") == "unknown", "unknown caller key is returned without file injection");

        Write(directory, "Korean", "rtc_raven_hint: |-\n  까악! 🐦\n  두 번째 줄\n\n  마지막 줄\nrtc_reset: 'Réinitialiser 日本語'\n");
        catalog.Load("Korean");
        Check(catalog.Get("rtc_raven_hint") == "까악! 🐦\n두 번째 줄\n\n마지막 줄", "Unicode multiline block preserves line breaks");
        Check(catalog.Get("rtc_reset") == "Réinitialiser 日本語", "Unicode scalar preserved");
        Write(directory, "Korean", "rtc_reset: newest\n");
        Check(catalog.Get("rtc_reset") == "Réinitialiser 日本語", "Get does not reread changed files");
        catalog.Load("Korean");
        Check(catalog.Get("rtc_reset") == "newest", "Load rereads changed files");
        Check(catalog.Get("rtc_raven_hint") == korean["rtc_raven_hint"], "removed override returns to embedded value");
        File.Delete(Path.Combine(directory, "Korean.yml"));
        catalog.Load("Korean");
        AssertValues(catalog, korean, "removed external file returns to embedded values");

        foreach (string invalid in new[] { "../English", "..\\English", "C:\\English", "English/other", "English.", "English ", ".", "..", "", " ", "English|other" })
        {
            warnings.Clear();
            catalog.Load(invalid);
            AssertValues(catalog, english, "invalid language " + invalid);
            Check(warnings.Count == 1, "invalid language warned: " + invalid);
        }
        warnings.Clear();
        Write(directory, "Chinese_Trad", "rtc_reset: 重設\n");
        catalog.Load("Chinese_Trad");
        Check(catalog.Get("rtc_reset") == "重設", "standard underscore language name allowed");
        Check(warnings.Count == 0, "valid new language does not warn");

        bool missingRejected = false;
        try { new Catalog(target, typeof(string).Assembly, plugins, config, warnings.Add); }
        catch (TargetInvocationException exception) when (exception.InnerException is InvalidDataException) { missingRejected = true; }
        Check(missingRejected, "constructor requires embedded English resource");

        string embeddedEnglish;
        using (Stream stream = target.GetManifestResourceStream("ReferToCompendium.translations.English.yml")!)
        using (var reader = new StreamReader(stream, Encoding.UTF8)) embeddedEnglish = reader.ReadToEnd();
        var fixture = new ResourceAssembly();
        fixture.Resources["ReferToCompendium.translations.English.yml"] = "rtc_reset: Reset\n";
        Check(RejectsEnglish(target, fixture, plugins, config), "constructor requires all five English keys");
        fixture.Resources["ReferToCompendium.translations.English.yml"] = embeddedEnglish.Replace("rtc_reset: Reset", "rtc_reset: ''");
        Check(RejectsEnglish(target, fixture, plugins, config), "constructor rejects blank required English key");
        fixture.Resources["ReferToCompendium.translations.English.yml"] = embeddedEnglish;
        fixture.Resources["ReferToCompendium.translations.Korean.yml"] = "rtc_reset: changed\nrtc_drag_hint: [unterminated\n";
        warnings.Clear();
        var brokenEmbedded = new Catalog(target, fixture, plugins, config, warnings.Add);
        brokenEmbedded.Load("Korean");
        AssertValues(brokenEmbedded, english, "invalid optional embedded translation keeps English");
        Check(warnings.Count == 1 && warnings[0].Contains("keeping English defaults"), "invalid optional embedded translation warns");

        CheckSearchLayers(target, Path.Combine(rootDirectory, "search-layers"), english, korean);
        CheckSearchDuplicates(target, Path.Combine(rootDirectory, "search-duplicates"), korean);
        CheckLinkedDirectory(target, Path.Combine(rootDirectory, "search-link"), korean);
    }

    private static void CheckSearchLayers(Assembly target, string root, Dictionary<string, string> english,
        Dictionary<string, string> korean)
    {
        string plugins = Path.Combine(root, "plugins");
        string config = Path.Combine(root, "config");
        var warnings = new List<string>();
        var catalog = new Catalog(target, target, plugins, config, warnings.Add);
        catalog.Load("Korean");
        AssertValues(catalog, korean, "nonexistent search roots use embedded translations");
        Check(warnings.Count == 0, "nonexistent search roots are normal");

        string badYaml = "rtc_reset: [unterminated\n";
        foreach (string searchRoot in new[] { plugins, config })
        {
            foreach (string ignoredName in new[]
            {
                "English.yml", "Korean.yml", "ReferToCompendium.Korean.example.yml", "ReferToCompendium.English.example.yml",
                "ReferToCompendium.Korean.yml.bak", "OtherMod.Korean.yml", "ReferToCompendium.French.yml",
                "ReferToCompendium.Korean.yaml", "Prefix.ReferToCompendium.Korean.yml", "ReferToCompendium.KoreanExtra.yml"
            })
                WriteFile(Path.Combine(searchRoot, "ignored", "nested", ignoredName), badYaml);
        }
        // BepInEx siblings outside the two search roots must never become candidates.
        WriteFile(Path.Combine(root, "cache", "ReferToCompendium.Korean.yml"), badYaml);
        WriteFile(Path.Combine(root, "patchers", "ReferToCompendium.Korean.yml"), badYaml);
        catalog.Load("Korean");
        AssertValues(catalog, korean, "examples, other names, and files outside search roots are ignored");
        Check(warnings.Count == 0, "ignored files are not parsed or warned about");

        string pluginFile = Path.Combine(plugins, "translation-pack", "deep", "refertocompendium.kOREAN.YML");
        WriteFile(pluginFile, "rtc_reset: Plugin reset\nrtc_raven_hint: Plugin raven\nrtc_drag_hint: Plugin drag\n");
        catalog.Load("Korean");
        Check(catalog.Get("rtc_reset") == "Plugin reset", "recursive plugin search accepts exact case-insensitive filename");
        Check(catalog.Get("rtc_raven_hint") == "Plugin raven", "plugin layer overrides embedded values");
        Check(catalog.Get("rtc_collapsed_title") == korean["rtc_collapsed_title"], "partial plugin layer keeps selected embedded language");
        Check(warnings.Count == 0, "one matching plugin file does not warn");

        string configFile = Path.Combine(config, "user", "nested", "ReferToCompendium.Korean.yml");
        WriteFile(configFile, "rtc_reset: Config reset\nrtc_collapsed_title: Config title\nrtc_raven_hint: null\nrtc_drag_hint: ''\n");
        Check(catalog.Get("rtc_reset") == "Plugin reset", "Get does not discover newly added config files");
        catalog.Load("Korean");
        Check(catalog.Get("rtc_reset") == "Config reset", "config layer overrides plugin layer");
        Check(catalog.Get("rtc_collapsed_title") == "Config title", "config layer fills keys missing from plugin file");
        Check(catalog.Get("rtc_raven_hint") == "Plugin raven", "null config value keeps plugin value");
        Check(catalog.Get("rtc_drag_hint") == "Plugin drag", "empty config value keeps plugin value");
        Check(catalog.Get("rtc_collapsed_help") == korean["rtc_collapsed_help"], "keys missing from both external tiers keep embedded language");

        string legacyFile = Path.Combine(config, "ReferToCompendium", "translations", "Korean.yml");
        WriteFile(legacyFile, "rtc_reset: Legacy reset\nrtc_collapsed_help: Legacy help\nrtc_raven_hint: ''\n");
        catalog.Load("Korean");
        Check(catalog.Get("rtc_reset") == "Legacy reset", "legacy exact path has highest priority");
        Check(catalog.Get("rtc_collapsed_help") == "Legacy help", "partial legacy file applies its keys");
        Check(catalog.Get("rtc_collapsed_title") == "Config title", "partial legacy file keeps config-only key");
        Check(catalog.Get("rtc_raven_hint") == "Plugin raven", "blank legacy value keeps plugin-only key");
        Check(warnings.Count == 0, "separate priority tiers do not count as duplicate files");

        warnings.Clear();
        WriteFile(legacyFile, "rtc_reset: uncommitted\nrtc_collapsed_help: [unterminated\n");
        catalog.Load("Korean");
        Check(catalog.Get("rtc_reset") == "Config reset", "invalid legacy file preserves config tier atomically");
        Check(catalog.Get("rtc_raven_hint") == "Plugin raven", "invalid legacy file preserves plugin tier");
        Check(catalog.Get("rtc_collapsed_help") == korean["rtc_collapsed_help"], "invalid legacy file clears its previous successful overrides");
        Check(warnings.Any(w => w.Contains(legacyFile)), "invalid legacy warning names the file");

        File.Delete(legacyFile);
        WriteFile(configFile, "rtc_reset: Updated config\n");
        catalog.Load("Korean");
        Check(catalog.Get("rtc_reset") == "Updated config", "Load rereads discovered files");
        Check(catalog.Get("rtc_collapsed_title") == korean["rtc_collapsed_title"], "removed config key does not linger");
        File.Delete(configFile);
        Check(catalog.Get("rtc_reset") == "Updated config", "Get does not rescan removed files");
        catalog.Load("Korean");
        Check(catalog.Get("rtc_reset") == "Plugin reset", "removed config file falls back to plugin tier");
        File.Delete(pluginFile);
        catalog.Load("Korean");
        AssertValues(catalog, korean, "removed plugin file falls back to embedded selected language");

        // Root-level matches are valid too; English overrides stay English-only.
        string englishPlugin = Path.Combine(plugins, "ReferToCompendium.English.yml");
        WriteFile(englishPlugin, "rtc_reset: Plugin English\n");
        WriteFile(Path.Combine(config, "ReferToCompendium.English.yml"), "rtc_raven_hint: Config English\n");
        catalog.Load("English");
        Check(catalog.Get("rtc_reset") == "Plugin English", "root-level plugin match is loaded");
        Check(catalog.Get("rtc_raven_hint") == "Config English", "root-level config match is loaded");
        catalog.Load("Korean");
        AssertValues(catalog, korean, "switching language discards both English search tiers");
        // The deliberately malformed French examples above remain unrelated to this language.
        WriteFile(Path.Combine(plugins, "new-language", "ReferToCompendium.German.yml"), "rtc_reset: Zurücksetzen\n");
        catalog.Load("German");
        Check(catalog.Get("rtc_reset") == "Zurücksetzen", "unbundled language can use a discovered plugin translation");
        Check(catalog.Get("rtc_drag_hint") == english["rtc_drag_hint"], "unbundled search language uses embedded English fallback");
        catalog.Load("Korean");
        AssertValues(catalog, korean, "switching back does not retain unbundled-language overrides");
    }

    private static void CheckSearchDuplicates(Assembly target, string root, Dictionary<string, string> korean)
    {
        string plugins = Path.Combine(root, "plugins");
        string config = Path.Combine(root, "config");
        string selectedPlugin = Path.Combine(plugins, "Zed", "ReferToCompendium.Korean.yml");
        string ignoredPlugin = Path.Combine(plugins, "alpha", "ReferToCompendium.Korean.yml");
        string selectedConfig = Path.Combine(config, "Zed", "ReferToCompendium.Korean.yml");
        string ignoredConfig = Path.Combine(config, "alpha", "ReferToCompendium.Korean.yml");
        var warnings = new List<string>();
        var catalog = new Catalog(target, target, plugins, config, warnings.Add);
        // Create the Ordinal-last path first. Case-insensitive sorting would choose alpha.
        WriteFile(ignoredPlugin, "rtc_reset: Ignored plugin\nrtc_raven_hint: Ignored raven\n");
        WriteFile(selectedPlugin, "rtc_reset: Selected plugin\nrtc_drag_hint: Selected plugin drag\n");
        Check(StringComparer.Ordinal.Compare(selectedPlugin, ignoredPlugin) < 0, "duplicate fixture distinguishes Ordinal from case-insensitive order");
        catalog.Load("Korean");
        Check(catalog.Get("rtc_reset") == "Selected plugin", "plugin duplicates choose Ordinal first full path");
        Check(catalog.Get("rtc_raven_hint") == korean["rtc_raven_hint"], "ignored plugin duplicate is not merged");
        CheckDuplicateWarning(warnings, selectedPlugin, ignoredPlugin, "plugin duplicate");

        warnings.Clear();
        WriteFile(ignoredConfig, "rtc_reset: Ignored config\nrtc_collapsed_help: Ignored help\n");
        WriteFile(selectedConfig, "rtc_reset: Selected config\nrtc_collapsed_title: Selected config title\n");
        catalog.Load("Korean");
        Check(catalog.Get("rtc_reset") == "Selected config", "config duplicates choose Ordinal first path and override plugin tier");
        Check(catalog.Get("rtc_drag_hint") == "Selected plugin drag", "partial selected config preserves selected plugin keys");
        Check(catalog.Get("rtc_collapsed_help") == korean["rtc_collapsed_help"], "ignored config duplicate is not merged");
        CheckDuplicateWarning(warnings, selectedConfig, ignoredConfig, "config duplicate");

        warnings.Clear();
        WriteFile(selectedConfig, "rtc_reset: uncommitted\nrtc_collapsed_title: [unterminated\n");
        catalog.Load("Korean");
        Check(catalog.Get("rtc_reset") == "Selected plugin", "invalid first config file keeps preceding plugin layer");
        Check(catalog.Get("rtc_collapsed_title") == korean["rtc_collapsed_title"], "invalid config file does not retain earlier load's values");
        Check(catalog.Get("rtc_collapsed_help") == korean["rtc_collapsed_help"], "invalid first config does not try second candidate");
        CheckDuplicateWarning(warnings, selectedConfig, ignoredConfig, "invalid selected config duplicate");

        WriteFile(selectedConfig, "{}\n");
        catalog.Load("Korean");
        Check(catalog.Get("rtc_reset") == "Selected plugin", "empty first config mapping does not try another candidate");
        File.Delete(selectedConfig);
        catalog.Load("Korean");
        Check(catalog.Get("rtc_reset") == "Ignored config", "removing first candidate selects remaining config file on Load");
        Check(catalog.Get("rtc_collapsed_help") == "Ignored help", "newly selected candidate applies all its keys");
        File.Delete(ignoredConfig);

        warnings.Clear();
        WriteFile(selectedPlugin, "rtc_reset: uncommitted\nrtc_drag_hint:\n  nested: wrong\n");
        catalog.Load("Korean");
        AssertValues(catalog, korean, "invalid first plugin preserves embedded layer and never tries second candidate");
        CheckDuplicateWarning(warnings, selectedPlugin, ignoredPlugin, "invalid selected plugin duplicate");
        WriteFile(selectedConfig, "rtc_reset: Valid later tier\n");
        catalog.Load("Korean");
        Check(catalog.Get("rtc_reset") == "Valid later tier", "invalid plugin does not block valid config layer");
        Check(catalog.Get("rtc_drag_hint") == korean["rtc_drag_hint"], "invalid plugin remains unapplied below valid config");
        WriteFile(Path.Combine(config, "ReferToCompendium", "translations", "Korean.yml"), "rtc_reset: Valid legacy tier\n");
        catalog.Load("Korean");
        Check(catalog.Get("rtc_reset") == "Valid legacy tier", "valid legacy tier still applies after failed earlier tier");
    }

    private static void CheckDuplicateWarning(List<string> warnings, string selected, string ignored, string context)
    {
        Check(warnings.Any(w => w.Contains(selected)), context + " warning identifies selected file");
        Check(warnings.Any(w => w.Contains(ignored)), context + " warning identifies ignored file");
    }

    private static void CheckLinkedDirectory(Assembly target, string root, Dictionary<string, string> korean)
    {
        if (Environment.OSVersion.Platform != PlatformID.Win32NT)
        {
            Console.WriteLine("Skipped directory-link check: Windows link creation is unavailable.");
            return;
        }
        string plugins = Path.Combine(root, "plugins");
        Directory.CreateDirectory(plugins);
        string link = Path.Combine(plugins, "loop");
        // 1 = directory, 2 = allow unprivileged creation when Windows enables it.
        if (!CreateSymbolicLink(link, plugins, 3) && !CreateSymbolicLink(link, plugins, 1))
        {
            Console.WriteLine("Skipped directory-link check: Windows could not create the test link (error " + Marshal.GetLastWin32Error() + ").");
            return;
        }
        DirectoryLinks.Add(link);
        Check((File.GetAttributes(link) & FileAttributes.ReparsePoint) != 0, "directory-link fixture is a reparse point");
        var warnings = new List<string>();
        var catalog = new Catalog(target, target, plugins, Path.Combine(root, "config"), warnings.Add);
        catalog.Load("Korean");
        AssertValues(catalog, korean, "recursive search skips a directory link back to its own root");
        Check(warnings.Count == 0, "skipping a directory link does not produce a recursion error");
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool CreateSymbolicLink(string link, string target, uint flags);

    private static bool RejectsEnglish(Assembly target, Assembly resources, string plugins, string config)
    {
        try { new Catalog(target, resources, plugins, config, _ => { }); }
        catch (TargetInvocationException exception) when (exception.InnerException is InvalidDataException) { return true; }
        return false;
    }

    private static void Reject(string directory, Catalog catalog, List<string> warnings,
        Dictionary<string, string> defaults, string text, string name)
    {
        warnings.Clear();
        Write(directory, "Korean", text);
        catalog.Load("Korean");
        AssertValues(catalog, defaults, name);
        Check(warnings.Any(w => w.Contains(Path.Combine(directory, "Korean.yml"))), name + " warns with file path");
    }

    private static void Write(string directory, string language, string text) =>
        File.WriteAllText(Path.Combine(directory, language + ".yml"), text, new UTF8Encoding(false));

    private static void WriteFile(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text, new UTF8Encoding(false));
    }

    private static void AssertValues(Catalog catalog, Dictionary<string, string> expected, string context)
    {
        foreach (KeyValuePair<string, string> pair in expected) Check(catalog.Get(pair.Key) == pair.Value, context + ": " + pair.Key);
    }

    private static void Check(bool passed, string name)
    {
        if (!passed) throw new InvalidOperationException("FAILED: " + name);
        _checks++;
    }

    private sealed class Catalog
    {
        private readonly object _instance;
        private readonly MethodInfo _load;
        private readonly MethodInfo _get;

        internal Catalog(Assembly code, Assembly resources, string plugins, string config, Action<string> warning)
        {
            Type type = code.GetType("ReferToCompendium.TranslationCatalog", true)!;
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
            _instance = type.GetConstructor(flags, null, new[] { typeof(Assembly), typeof(string), typeof(string), typeof(Action<string>) }, null)!
                .Invoke(new object[] { resources, plugins, config, warning });
            _load = type.GetMethod("Load", flags)!;
            _get = type.GetMethod("Get", flags)!;
        }

        internal void Load(string language) => _load.Invoke(_instance, new object[] { language });
        internal string Get(string key) => (string)_get.Invoke(_instance, new object[] { key })!;
    }

    private sealed class ResourceAssembly : Assembly
    {
        internal readonly Dictionary<string, string> Resources = new(StringComparer.Ordinal);
        public override Stream? GetManifestResourceStream(string name) => Resources.TryGetValue(name, out string value)
            ? new MemoryStream(Encoding.UTF8.GetBytes(value)) : null;
    }
}
