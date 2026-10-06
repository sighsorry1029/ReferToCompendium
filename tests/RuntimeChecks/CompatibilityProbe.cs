using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using HarmonyLib;

namespace ReferToCompendium.Tests
{
    public static class CompatibilityProbe
    {
        private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        private static int checks;

        public static int Run()
        {
            try
            {
                string managed = Environment.GetEnvironmentVariable("COMPENDIUM_PROBE_MANAGED");
                AppDomain.CurrentDomain.AssemblyResolve += (_, args) =>
                {
                    string dependency = Path.Combine(managed, new AssemblyName(args.Name).Name + ".dll");
                    return File.Exists(dependency) ? Assembly.LoadFrom(dependency) : null;
                };
                RunAccessChecks(managed);
                RunStatusFilterChecks();
                RunStandaloneChecks("COMPENDIUM_STATE_CHECKS");
                if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("COMPENDIUM_TRANSLATION_CHECKS")))
                    RunStandaloneChecks("COMPENDIUM_TRANSLATION_CHECKS", Environment.GetEnvironmentVariable("COMPENDIUM_PROBE_DLL"));
                System.Console.WriteLine("PASS: " + checks + " original-game Mono access and actual product HUD-filter checks. " +
                    "No Harmony detour, original HUD body, Unity native calls, rendering, or network session. " +
                    "StatusEffect fixtures are constructorless; the product name cache is seeded instead of calling Unity's native name getter.");
                return 0;
            }
            catch (Exception error) { System.Console.Error.WriteLine(error); return 1; }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void RunStatusFilterChecks()
        {
            Assembly mod = Assembly.LoadFrom(Environment.GetEnvironmentVariable("COMPENDIUM_PROBE_DLL"));
            Type service = mod.GetType("ReferToCompendium.StatusEffectHud", true);
            Type rulesType = mod.GetType("ReferToCompendium.StatusEffectFilterRules", true);
            MethodInfo prefix = service.GetMethod("Prefix", All);
            Check(prefix != null && prefix.IsStatic && prefix.ReturnType == typeof(void), "actual product filter prefix signature");
            Check(prefix.GetParameters().Length == 1 && prefix.GetParameters()[0].ParameterType == typeof(List<StatusEffect>).MakeByRefType(),
                "actual filter prefix accepts original StatusEffect list by reference");

            FieldInfo readyField = service.GetField("_ready", All);
            FieldInfo failedField = service.GetField("_filterFailed", All);
            FieldInfo rulesField = service.GetField("_rules", All);
            FieldInfo namesField = service.GetField("_names", All);
            object originalReady = readyField.GetValue(null);
            object originalFailed = failedField.GetValue(null);
            object originalRules = rulesField.GetValue(null);
            object originalNames = namesField.GetValue(null);
            try
            {
                // Initialize would install detours and Shutdown would touch Unity
                // layout objects. Seed only the managed filter boundary instead.
                readyField.SetValue(null, true);
                failedField.SetValue(null, false);
                namesField.SetValue(null, Activator.CreateInstance(originalNames.GetType()));
                var alpha = NewFilterEffect(service, "Alpha(Clone)", "$se_alpha");
                var beta = NewFilterEffect(service, " Beta (Clone) ", "$se_beta");
                var gamma = NewFilterEffect(service, "Gamma", "$se_gamma");
                var duplicateAlpha = NewFilterEffect(service, "Alpha", "$se_alpha_other");
                var lowerAlpha = NewFilterEffect(service, "alpha", "$se_lower");
                var literalClone = NewFilterEffect(service, "Alpha(Clone)Suffix", "$se_literal");
                var noName = NewFilterEffect(service, "", "$se_unnamed");
                alpha.m_ttl = 42f;
                var incoming = new List<StatusEffect> { beta, alpha, null, literalClone, gamma, beta, duplicateAlpha, lowerAlpha };
                StatusEffect[] original = incoming.ToArray();

                SetFilterRules(rulesType, rulesField, null);
                Check(ReferenceEquals(InvokeFilter(prefix, incoming), incoming), "null filter configuration hides nothing");
                SetFilterRules(rulesType, rulesField, " , ; \r\n \t ");
                Check(ReferenceEquals(InvokeFilter(prefix, incoming), incoming), "blank separated filter configuration hides nothing");
                SetFilterRules(rulesType, rulesField, "alph");
                Check(ReferenceEquals(InvokeFilter(prefix, incoming), incoming), "matching is exact rather than substring-based");

                SetFilterRules(rulesType, rulesField, " Alpha ; $se_gamma,\r\n ");
                List<StatusEffect> filtered = InvokeFilter(prefix, incoming);
                Check(!ReferenceEquals(filtered, incoming), "matching filter replaces only the argument list");
                CheckFilterReferences(incoming, original, "caller list and original order stay unchanged");
                CheckFilterReferences(filtered, new[] { beta, null, literalClone, beta, lowerAlpha },
                    "filter preserves survivor order, repeated references, null, and case-distinct effects");
                Check(alpha.m_name == "$se_alpha" && alpha.m_ttl == 42f && !alpha.m_hidden
                    && duplicateAlpha.m_name == "$se_alpha_other" && !duplicateAlpha.m_hidden,
                    "filter does not change effect names, lifetime, or gameplay hidden flags");

                SetFilterRules(rulesType, rulesField, " Beta ; $se_literal \r\n Alpha,Beta\n");
                var grammar = new List<StatusEffect> { beta, literalClone, alpha, lowerAlpha };
                CheckFilterReferences(InvokeFilter(prefix, grammar), new[] { lowerAlpha },
                    "commas, semicolons, newlines, whitespace and repeated keys work together");
                SetFilterRules(rulesType, rulesField, "alpha");
                CheckFilterReferences(InvokeFilter(prefix, new List<StatusEffect> { alpha, lowerAlpha }), new[] { alpha },
                    "internal-name matching is ordinal and case-sensitive");
                SetFilterRules(rulesType, rulesField, "$SE_BETA");
                var tokenCase = new List<StatusEffect> { beta };
                Check(ReferenceEquals(InvokeFilter(prefix, tokenCase), tokenCase), "raw token matching is case-sensitive");
                SetFilterRules(rulesType, rulesField, "$se_unnamed");
                CheckFilterReferences(InvokeFilter(prefix, new List<StatusEffect> { noName }), new StatusEffect[0],
                    "raw token can filter an effect with an empty internal name");

                SetFilterRules(rulesType, rulesField, "$se_changed");
                var changingToken = new List<StatusEffect> { gamma };
                Check(ReferenceEquals(InvokeFilter(prefix, changingToken), changingToken), "unmatched raw token initially passes through");
                gamma.m_name = "$se_changed";
                CheckFilterReferences(InvokeFilter(prefix, changingToken), new StatusEffect[0], "raw token updates are read rather than cached");
                gamma.m_name = "$se_gamma";

                SetFilterRules(rulesType, rulesField, "Alpha");
                var subset = new List<StatusEffect> { beta, gamma };
                Check(ReferenceEquals(InvokeFilter(prefix, subset), subset), "externally filtered subset never restores absent Alpha");
                CheckFilterReferences(subset, new[] { beta, gamma }, "external filter survivor order is unchanged");
                var allMatched = new List<StatusEffect> { alpha, duplicateAlpha, alpha };
                CheckFilterReferences(InvokeFilter(prefix, allMatched), new StatusEffect[0], "all matching instances and repeats can be hidden");
                CheckFilterReferences(allMatched, new[] { alpha, duplicateAlpha, alpha }, "fully filtered caller list is unchanged");
                var empty = new List<StatusEffect>();
                Check(ReferenceEquals(InvokeFilter(prefix, empty), empty), "empty incoming list passes through");
                Check(ReferenceEquals(InvokeFilter(prefix, null), null), "null incoming list passes through");
                Check(!(bool)failedField.GetValue(null), "managed filter fixtures did not trigger the failure guard");
                readyField.SetValue(null, false);
                Check(ReferenceEquals(InvokeFilter(prefix, incoming), incoming), "inactive service leaves the argument untouched");
                readyField.SetValue(null, true);
                failedField.SetValue(null, true);
                Check(ReferenceEquals(InvokeFilter(prefix, incoming), incoming), "failed filter leaves the argument untouched");
            }
            finally
            {
                readyField.SetValue(null, originalReady);
                failedField.SetValue(null, originalFailed);
                rulesField.SetValue(null, originalRules);
                namesField.SetValue(null, originalNames);
            }
        }

        private static StatusEffect NewFilterEffect(Type service, string internalName, string token)
        {
            var effect = (StatusEffect)FormatterServices.GetUninitializedObject(typeof(StatusEffect));
            effect.m_name = token;
            Type nameType = service.GetNestedType("EffectName", All);
            // Use the real cache value constructor, including terminal Clone normalization.
            object name = nameType.GetConstructor(All, null, new[] { typeof(string) }, null).Invoke(new object[] { internalName });
            object table = service.GetField("_names", All).GetValue(null);
            table.GetType().GetMethod("Add", All).Invoke(table, new[] { (object)effect, name });
            return effect;
        }

        private static void SetFilterRules(Type type, FieldInfo field, string value)
        {
            object rules = type.GetConstructor(All, null, new[] { typeof(string) }, null).Invoke(new object[] { value });
            field.SetValue(null, rules);
        }

        private static List<StatusEffect> InvokeFilter(MethodInfo prefix, List<StatusEffect> incoming)
        {
            object[] arguments = { incoming };
            prefix.Invoke(null, arguments);
            return (List<StatusEffect>)arguments[0];
        }

        private static void CheckFilterReferences(List<StatusEffect> actual, StatusEffect[] expected, string label)
        {
            bool same = actual != null && actual.Count == expected.Length;
            for (int i = 0; same && i < expected.Length; ++i) same = ReferenceEquals(actual[i], expected[i]);
            Check(same, label);
        }

        private static void RunStandaloneChecks(string variable, params string[] arguments)
        {
            string path = Environment.GetEnvironmentVariable(variable);
            if (string.IsNullOrEmpty(path)) throw new InvalidOperationException("Missing standalone checks: " + variable);
            Assembly program = Assembly.LoadFrom(path);
            object result = program.EntryPoint.Invoke(null, new object[] { arguments });
            if (result is int && (int)result != 0) throw new InvalidOperationException(variable + " returned " + result);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void RunAccessChecks(string managed)
        {
            Assembly mod = Assembly.LoadFrom(Environment.GetEnvironmentVariable("COMPENDIUM_PROBE_DLL"));
            Check(string.Equals(Path.GetFullPath(typeof(TextsDialog).Assembly.Location), Path.Combine(managed, "assembly_valheim.dll"), StringComparison.OrdinalIgnoreCase), "loaded original game assembly");
            System.Console.WriteLine("Game: " + typeof(TextsDialog).Assembly.Location);
            Type controller = mod.GetType("ReferToCompendium.CompendiumController", true);
            RuntimeHelpers.RunClassConstructor(controller.TypeHandle);
            Check(true, "all product cached accessors constructed");
            var dialog = (TextsDialog)FormatterServices.GetUninitializedObject(typeof(TextsDialog));

            FieldInfo originalTexts = OriginalField("m_texts", typeof(List<TextsDialog.TextInfo>));
            var texts = (AccessTools.FieldRef<TextsDialog, List<TextsDialog.TextInfo>>)controller.GetField("Texts", All).GetValue(null);
            var first = new List<TextsDialog.TextInfo>();
            texts(dialog) = first;
            Check(ReferenceEquals(originalTexts.GetValue(dialog), first), "cached m_texts write");
            var second = new List<TextsDialog.TextInfo>();
            originalTexts.SetValue(dialog, second);
            Check(ReferenceEquals(texts(dialog), second), "cached m_texts read");

            FieldInfo originalIndex = OriginalField("m_selectionIndex", typeof(int));
            var index = (AccessTools.FieldRef<TextsDialog, int>)controller.GetField("SelectionIndex", All).GetValue(null);
            index(dialog) = 17;
            Check((int)originalIndex.GetValue(dialog) == 17, "cached m_selectionIndex write");
            originalIndex.SetValue(dialog, -1);
            Check(index(dialog) == -1, "cached m_selectionIndex read");

            FieldInfo originalSize = OriginalField("m_baseListSize", typeof(float));
            var size = (AccessTools.FieldRef<TextsDialog, float>)controller.GetField("BaseListSize", All).GetValue(null);
            size(dialog) = 125.5f;
            Check((float)originalSize.GetValue(dialog) == 125.5f, "cached m_baseListSize write");
            originalSize.SetValue(dialog, 64.25f);
            Check(size(dialog) == 64.25f, "cached m_baseListSize read");

            var show = (MethodInfo)controller.GetField("ShowText", All).GetValue(null);
            Check(show != null && show.DeclaringType == typeof(TextsDialog) && show.Name == "ShowText", "cached ShowText resolves on original TextsDialog");
            Check(show.IsPrivate && !show.IsStatic && show.ReturnType == typeof(void), "original ShowText private instance signature");
            ParameterInfo[] parameters = show.GetParameters();
            Check(parameters.Length == 1 && parameters[0].ParameterType == typeof(TextsDialog.TextInfo), "ShowText overload takes TextInfo");
        }

        private static FieldInfo OriginalField(string name, Type expected)
        {
            FieldInfo field = typeof(TextsDialog).GetField(name, All);
            Check(field != null && field.IsPrivate && !field.IsStatic && field.FieldType == expected, "original private field contract " + name);
            return field;
        }

        private static void Check(bool success, string label)
        {
            if (!success) throw new InvalidOperationException("FAIL: " + label);
            checks++;
        }
    }
}
