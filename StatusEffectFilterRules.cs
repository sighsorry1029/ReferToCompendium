using System;
using System.Collections.Generic;

namespace ReferToCompendium;

// Immutable after construction: config callbacks can replace the rules without touching Unity.
internal sealed class StatusEffectFilterRules
{
    private readonly HashSet<string> _keys = new(StringComparer.Ordinal);

    internal StatusEffectFilterRules(string? value)
    {
        foreach (string entry in (value ?? "").Split(new[] { ',', ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            string key = entry.Trim();
            if (key.Length != 0) _keys.Add(key);
        }
    }

    internal bool IsEmpty => _keys.Count == 0;

    internal bool Hides(string? internalName, string? nameToken) =>
        (!string.IsNullOrEmpty(internalName) && _keys.Contains(internalName!)) ||
        (!string.IsNullOrEmpty(nameToken) && _keys.Contains(nameToken!));

    internal static string NormalizeInternalName(string? name)
    {
        string result = (name ?? "").Trim();
        const string cloneSuffix = "(Clone)";
        if (result.EndsWith(cloneSuffix, StringComparison.Ordinal))
            result = result.Substring(0, result.Length - cloneSuffix.Length).TrimEnd();
        return result;
    }
}
