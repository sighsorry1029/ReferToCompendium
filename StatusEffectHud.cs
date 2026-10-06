using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace ReferToCompendium;

// Installed separately so optional HUD failures cannot unpatch the Compendium.
internal static class StatusEffectHud
{
    private static ConfigEntry<bool>? _singleColumn;
    private static ConfigEntry<Vector2>? _position;
    private static ConfigEntry<float>? _scale;
    private static ConfigEntry<float>? _spacing;
    private static ConfigEntry<string>? _hiddenEffects;
    private static volatile StatusEffectFilterRules _rules = new("");
    private static ConditionalWeakTable<StatusEffect, EffectName> _names = new();
    private static Harmony? _harmony;
    private static bool _ready;
    private static bool _filterFailed;
    private static bool _layoutFailed;

    // Only the Unity name is cached; m_name may change and is read directly each update.
    private sealed class EffectName
    {
        internal readonly string Name;
        internal EffectName(string name) => Name = StatusEffectFilterRules.NormalizeInternalName(name);
    }

    internal static void Initialize(ConfigFile config)
    {
        try
        {
            _singleColumn = config.Bind("Status effects", "Single column", true,
                "Arrange HUD status effects in one vertical column. Client only. Leave off when another HUD mod controls the layout. Changes apply on the next HUD update.");
            _position = config.Bind("Status effects", "Position", new Vector2(-40f, -250f),
                "Position of the column's upper-right corner, relative to the HUD's upper-right corner. Fixed UI coordinates; does not follow a moved minimap. Also used in no-map mode.");
            _scale = config.Bind("Status effects", "Scale", 0.8f,
                new ConfigDescription("Scale of each status effect row. Client only. Row spacing is adjusted separately.", new AcceptableValueRange<float>(0.5f, 2f)));
            _spacing = config.Bind("Status effects", "Row spacing", 40f,
                new ConfigDescription("Vertical distance between rows in UI units. Client only.", new AcceptableValueRange<float>(20f, 150f)));
            _hiddenEffects = config.Bind("Status effects", "Hidden effects", "",
                "Hide these effects from the HUD only, even when Single column is off. Separate internal names or raw localization tokens with commas, semicolons, or newlines. Exact, case-sensitive matching; example: Rested,Resting. Empty shows all effects. Gameplay and Compendium information are unchanged.");
            _hiddenEffects.SettingChanged += HiddenEffectsChanged;
            _rules = new StatusEffectFilterRules(_hiddenEffects.Value);
            _filterFailed = false;
            _layoutFailed = false;
            _harmony = new Harmony(Plugin.PluginId + ".StatusEffects");
            var update = AccessTools.DeclaredMethod(typeof(Hud), "UpdateStatusEffects", new[] { typeof(List<StatusEffect>) })
                ?? throw new MissingMethodException("Hud.UpdateStatusEffects(List<StatusEffect>)");
            var destroy = AccessTools.DeclaredMethod(typeof(Hud), "OnDestroy", Type.EmptyTypes)
                ?? throw new MissingMethodException("Hud.OnDestroy()");
            _harmony.Patch(update, prefix: new HarmonyMethod(typeof(StatusEffectHud), nameof(Prefix)),
                postfix: new HarmonyMethod(typeof(StatusEffectHud), nameof(Postfix)));
            _harmony.Patch(destroy, prefix: new HarmonyMethod(typeof(StatusEffectHud), nameof(HudDestroyed)));
            _ready = true;
        }
        catch (Exception error)
        {
            Shutdown();
            Plugin.LogWarning("Status effect HUD could not initialize; Compendium features are unaffected: " + error);
        }
    }

    private static void HiddenEffectsChanged(object sender, EventArgs args) =>
        _rules = new StatusEffectFilterRules(_hiddenEffects?.Value);

    [HarmonyAfter("Azumatt.StatusEffectFilter")]
    [HarmonyPriority(Priority.Last)]
    internal static void Prefix(ref List<StatusEffect> statusEffects)
    {
        if (!_ready || _filterFailed || statusEffects == null) return;
        StatusEffectFilterRules rules = _rules;
        if (rules.IsEmpty) return;
        try
        {
            // Assign only after the full pass succeeds; never mutate the caller's collection.
            List<StatusEffect>? filtered = null;
            for (int i = 0; i < statusEffects.Count; i++)
            {
                StatusEffect effect = statusEffects[i];
                bool hidden = false;
                if (!ReferenceEquals(effect, null))
                {
                    string name = _names.GetValue(effect, ReadName).Name;
                    hidden = rules.Hides(name, effect.m_name);
                }
                if (hidden)
                {
                    if (filtered == null)
                    {
                        filtered = new List<StatusEffect>(statusEffects.Count);
                        for (int before = 0; before < i; before++) filtered.Add(statusEffects[before]);
                    }
                }
                else filtered?.Add(effect!); // Preserve even null slots supplied by another prefix.
            }
            if (filtered != null) statusEffects = filtered;
        }
        catch (Exception error)
        {
            _filterFailed = true;
            Plugin.LogWarning("Status effect filtering disabled for this session after an error: " + error);
        }
    }

    private static EffectName ReadName(StatusEffect effect) => new(effect.name);

    [HarmonyAfter("shudnal.MyLittleUI", "com.ttolg.compactstatuseffects")]
    [HarmonyPriority(Priority.Last)]
    internal static void Postfix(Hud __instance, List<RectTransform> ___m_statusEffects)
    {
        if (!_ready || _layoutFailed) return;
        try
        {
            Vector2 position = _position!.Value;
            if (!Finite(position.x)) position.x = -40f;
            if (!Finite(position.y)) position.y = -250f;
            float scale = _scale!.Value;
            float spacing = _spacing!.Value;
            StatusEffectLayout.Apply(__instance, ___m_statusEffects, _singleColumn!.Value,
                position, Finite(scale) ? Mathf.Clamp(scale, 0.5f, 2f) : 0.8f,
                Finite(spacing) ? Mathf.Clamp(spacing, 20f, 150f) : 40f);
        }
        catch (Exception error)
        {
            _layoutFailed = true;
            RestoreLayout();
            Plugin.LogWarning("Single-column layout disabled for this session after an error; filtering and Compendium features remain available: " + error);
        }
    }

    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

    internal static void HudDestroyed(Hud __instance)
    {
        try { StatusEffectLayout.Release(__instance); }
        catch (Exception error) { Plugin.LogWarning("Could not restore status effect layout during HUD cleanup: " + error); }
        _names = new ConditionalWeakTable<StatusEffect, EffectName>();
    }

    private static void RestoreLayout()
    {
        try { StatusEffectLayout.Shutdown(); }
        catch (Exception error) { Plugin.LogWarning("Could not restore status effect layout: " + error); }
    }

    internal static void Shutdown()
    {
        _ready = false;
        if (_hiddenEffects != null) _hiddenEffects.SettingChanged -= HiddenEffectsChanged;
        RestoreLayout();
        _harmony?.UnpatchSelf();
        _harmony = null;
        _names = new ConditionalWeakTable<StatusEffect, EffectName>();
        _rules = new StatusEffectFilterRules("");
        _singleColumn = null;
        _position = null;
        _scale = null;
        _spacing = null;
        _hiddenEffects = null;
    }
}
