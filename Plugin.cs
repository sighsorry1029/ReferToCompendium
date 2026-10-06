using System;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace ReferToCompendium;

[BepInPlugin(PluginId, PluginName, PluginVersion)]
public sealed class Plugin : BaseUnityPlugin
{
    public const string PluginId = "sighsorry.ReferToCompendium";
    public const string PluginName = "ReferToCompendium";
    public const string PluginVersion = "1.0.2";

    private static ManualLogSource? _log;
    private static LayoutStore? _store;
    private static readonly LayoutState EmptyState = new();
    private ConfigEntry<bool> _showHint = null!;
    private ConfigEntry<string> _hintText = null!;
    private Harmony? _harmony;
    private bool _ready;
    private bool _hintFailed;

    internal static LayoutState State => _store?.State ?? EmptyState;
    internal static bool SaveLayout() => _store?.Save() ?? false;
    internal static void LogWarning(string message) => _log?.LogWarning(message);

    private void Awake()
    {
        _log = Logger;
        // This is a local UI plugin; do not install UI patches on a headless server.
        if (Application.isBatchMode) return;

        _showHint = Config.Bind("Raven hint", "Enabled", true,
            "Keep a speech hint in front of the compendium raven's beak while the inventory is open. Client only; takes effect immediately.");
        _hintText = Config.Bind("Raven hint", "Text", "",
            "Custom hint text. Leave empty for the localized default text selected by the game language. Client only.");
        string layoutPath = Path.Combine(Application.persistentDataPath, PluginName, "layout-shared.json");
        _store = new LayoutStore(layoutPath, LogWarning);
        _harmony = new Harmony(PluginId);
        try
        {
            UiLocalization.Initialize();
            _harmony.PatchAll(typeof(Plugin).Assembly);
            _ready = true;
            Logger.LogInfo($"{PluginName} {PluginVersion}: client layout at {layoutPath}");
        }
        catch (Exception exception)
        {
            UiLocalization.Shutdown();
            _harmony.UnpatchSelf();
            Logger.LogError($"Could not install compendium UI patches: {exception}");
        }
        StatusEffectHud.Initialize(Config);
    }

    private void LateUpdate()
    {
        if (!_ready) return;
        UiLocalization.Tick();
        if (_hintFailed) return;
        try
        {
            string hint = string.IsNullOrWhiteSpace(_hintText.Value)
                ? UiLocalization.Get(UiLocalization.RavenHint)
                : _hintText.Value;
            RavenHint.Tick(_showHint.Value, hint);
        }
        catch (Exception exception)
        {
            _hintFailed = true;
            RavenHint.Dispose();
            LogWarning($"Raven hint disabled for this session after a UI error: {exception}");
        }
    }

    private void OnDestroy()
    {
        _ready = false;
        UiLocalization.Shutdown();
        StatusEffectHud.Shutdown();
        RavenHint.Dispose();
        CompendiumController.Shutdown();
        _harmony?.UnpatchSelf();
        _store = null;
        _log = null;
    }
}
