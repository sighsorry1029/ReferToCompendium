using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace ReferToCompendium;

/// <summary>Translations owned by this mod; never writes to Valheim's global dictionary.</summary>
internal sealed class TranslationCatalog
{
    private const string ResourcePrefix = "ReferToCompendium.translations.";
    private static readonly HashSet<string> Keys = new(StringComparer.Ordinal)
    {
        "rtc_raven_hint", "rtc_drag_hint", "rtc_reset", "rtc_collapsed_title", "rtc_collapsed_help"
    };

    private readonly Assembly _assembly;
    private readonly string _pluginDirectory;
    private readonly string _configDirectory;
    private readonly Action<string> _warning;
    private readonly Dictionary<string, string> _english;
    private Dictionary<string, string> _current;

    internal TranslationCatalog(Assembly assembly, string pluginDirectory, string configDirectory, Action<string> warning)
    {
        _assembly = assembly ?? throw new ArgumentNullException(nameof(assembly));
        _pluginDirectory = pluginDirectory ?? throw new ArgumentNullException(nameof(pluginDirectory));
        _configDirectory = configDirectory ?? throw new ArgumentNullException(nameof(configDirectory));
        _warning = warning ?? throw new ArgumentNullException(nameof(warning));
        using Stream stream = _assembly.GetManifestResourceStream(ResourcePrefix + "English.yml")
            ?? throw new InvalidDataException("The embedded English translations are missing.");
        _english = Read(stream, "embedded English.yml");
        foreach (string key in Keys)
        {
            if (!_english.ContainsKey(key))
                throw new InvalidDataException("The embedded English translations must define a nonempty value for " + key + ".");
        }
        _current = new Dictionary<string, string>(_english, StringComparer.Ordinal);
    }

    internal void Load(string language)
    {
        // Rebuild from defaults so changing language cannot retain the previous file's overrides.
        var next = new Dictionary<string, string>(_english, StringComparer.Ordinal);
        if (!IsLanguageFileName(language))
        {
            _warning("Translation language is not a valid file name; using embedded English.");
            _current = next;
            return;
        }

        if (!string.Equals(language, "English", StringComparison.Ordinal))
        {
            try
            {
                using Stream? embedded = _assembly.GetManifestResourceStream(ResourcePrefix + language + ".yml");
                if (embedded != null) Apply(next, Read(embedded, "embedded " + language + ".yml"));
            }
            catch (Exception exception) when (IsReadError(exception))
            {
                _warning("Could not read embedded " + language + " translations; keeping English defaults: " + exception.Message);
            }
        }

        string fileName = "ReferToCompendium." + language + ".yml";
        ApplyExternal(next, FindTranslation(_pluginDirectory, fileName));
        ApplyExternal(next, FindTranslation(_configDirectory, fileName));
        // Preserve explicitly configured translations from before recursive discovery.
        ApplyExternal(next, Path.Combine(_configDirectory, "ReferToCompendium", "translations", language + ".yml"));
        _current = next;
    }

    private void ApplyExternal(Dictionary<string, string> target, string? path)
    {
        if (path == null) return;
        try
        {
            using var external = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            // Read and validate the whole layer before changing any defaults.
            Apply(target, Read(external, path));
        }
        catch (FileNotFoundException) { }
        catch (DirectoryNotFoundException) { }
        catch (Exception exception) when (IsReadError(exception))
        {
            _warning("Could not read translations from " + path + "; keeping previously loaded translations: " + exception.Message);
        }
    }

    private string? FindTranslation(string root, string fileName)
    {
        var candidates = new List<string>();
        var pending = new Stack<string>();
        pending.Push(Path.GetFullPath(root));
        while (pending.Count > 0)
        {
            string directory = pending.Pop();
            try
            {
                // Do not follow junctions/symlinks outside these roots or into a loop.
                if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) continue;
                foreach (string child in Directory.GetDirectories(directory)) pending.Push(child);
                foreach (string path in Directory.GetFiles(directory))
                {
                    // Exact matching excludes examples, backups, other languages and other mods.
                    if (!string.Equals(Path.GetFileName(path), fileName, StringComparison.OrdinalIgnoreCase)) continue;
                    try
                    {
                        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0) candidates.Add(path);
                    }
                    catch (Exception exception) when (IsReadError(exception))
                    {
                        _warning("Could not inspect translation file " + path + ": " + exception.Message);
                    }
                }
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
            catch (Exception exception) when (IsReadError(exception))
            {
                _warning("Could not search translations in " + directory + ": " + exception.Message);
            }
        }
        if (candidates.Count == 0) return null;
        // Filesystem enumeration order must never decide which translation wins.
        candidates.Sort(StringComparer.Ordinal);
        if (candidates.Count > 1)
            _warning("Multiple translation files for " + fileName + " in " + root + ". Using " + candidates[0] +
                     "; ignoring " + string.Join(", ", candidates.GetRange(1, candidates.Count - 1)) + ".");
        return candidates[0];
    }

    internal string Get(string key) => _current.TryGetValue(key, out string value) ? value : key;

    private Dictionary<string, string> Read(Stream stream, string source)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, true);
        var yaml = new YamlStream();
        yaml.Load(reader);
        if (yaml.Documents.Count != 1 || !(yaml.Documents[0].RootNode is YamlMappingNode mapping))
            throw new InvalidDataException("Translations must contain one YAML mapping of text keys to strings.");

        var translations = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (KeyValuePair<YamlNode, YamlNode> pair in mapping.Children)
        {
            if (!(pair.Key is YamlScalarNode keyNode) || string.IsNullOrWhiteSpace(keyNode.Value))
                throw new InvalidDataException("Translation keys must be nonempty strings.");
            string key = keyNode.Value!;
            if (!Keys.Contains(key))
            {
                _warning("Ignoring unknown translation key '" + key + "' in " + source + ".");
                continue;
            }
            if (!(pair.Value is YamlScalarNode valueNode))
                throw new InvalidDataException("Translation " + key + " must be a string, not a nested mapping or list.");
            if (IsNull(valueNode) || string.IsNullOrWhiteSpace(valueNode.Value)) continue;
            translations.Add(key, valueNode.Value!);
        }
        return translations;
    }

    private static bool IsNull(YamlScalarNode node)
    {
        if (!node.Tag.IsEmpty && node.Tag.Value == "tag:yaml.org,2002:null") return true;
        return node.Tag.IsEmpty && node.Style == ScalarStyle.Plain &&
            (node.Value == "~" || string.Equals(node.Value, "null", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsLanguageFileName(string language)
    {
        if (string.IsNullOrWhiteSpace(language) || language == "." || language == ".." ||
            language.EndsWith(".", StringComparison.Ordinal) || language.EndsWith(" ", StringComparison.Ordinal)) return false;
        // Explicitly reject Windows separators/characters even when checks run on another platform.
        return language.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 &&
            language.IndexOfAny(new[] { '/', '\\', ':', '*', '?', '"', '<', '>', '|' }) < 0;
    }

    private static bool IsReadError(Exception exception) =>
        exception is IOException || exception is InvalidDataException || exception is UnauthorizedAccessException ||
        exception is YamlException || exception is ArgumentException || exception is NotSupportedException ||
        exception is System.Security.SecurityException;

    private static void Apply(Dictionary<string, string> target, Dictionary<string, string> translations)
    {
        foreach (KeyValuePair<string, string> pair in translations) target[pair.Key] = pair.Value;
    }
}
