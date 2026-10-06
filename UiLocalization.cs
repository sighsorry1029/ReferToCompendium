using System;
using BepInEx;

namespace ReferToCompendium;

/// <summary>Localizes only this mod's UI; game translations and topic IDs stay untouched.</summary>
internal static class UiLocalization
{
    internal const string RavenHint = "rtc_raven_hint";
    internal const string DragHint = "rtc_drag_hint";
    internal const string Reset = "rtc_reset";
    internal const string CollapsedTitle = "rtc_collapsed_title";
    internal const string CollapsedHelp = "rtc_collapsed_help";

    private static TranslationCatalog? _catalog;
    private static bool _refreshPending;

    internal static void Initialize()
    {
        Shutdown();
        _catalog = new TranslationCatalog(typeof(Plugin).Assembly,
            Paths.PluginPath, Paths.ConfigPath, Plugin.LogWarning);
        _refreshPending = true;
        Localization.OnLanguageChange += RequestRefresh;
    }

    private static void RequestRefresh() => _refreshPending = true;

    internal static void Tick()
    {
        if (!_refreshPending || _catalog == null) return;
        _refreshPending = false;
        try
        {
            // Defer the initial lookup until Unity updates, rather than creating
            // the game's Localization singleton during the plugin's Awake.
            _catalog.Load(Localization.instance.GetSelectedLanguage());
            CompendiumController.RefreshTranslations();
        }
        catch (Exception exception)
        {
            Plugin.LogWarning("UI translations could not be refreshed: " + exception.Message);
        }
    }

    internal static string Get(string key) => _catalog?.Get(key) ?? key;

    internal static void Shutdown()
    {
        Localization.OnLanguageChange -= RequestRefresh;
        _catalog = null;
        _refreshPending = false;
    }
}
