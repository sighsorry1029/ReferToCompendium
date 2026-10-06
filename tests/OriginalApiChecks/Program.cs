using Mono.Cecil;

if (args.Length != 3)
{
    System.Console.Error.WriteLine("Usage: OriginalApiChecks <plugin.dll> <original Managed directory> <BepInEx core directory>");
    return 2;
}

int failures = 0;
int count = 0;
void Check(bool condition, string message)
{
    if (condition) return;
    failures++;
    System.Console.Error.WriteLine("FAIL " + message);
}

using var resolver = new DefaultAssemblyResolver();
resolver.AddSearchDirectory(Path.GetFullPath(args[1]));
resolver.AddSearchDirectory(Path.GetFullPath(args[2]));
resolver.AddSearchDirectory(Path.GetDirectoryName(Path.GetFullPath(args[0]))!);
using var module = ModuleDefinition.ReadModule(args[0], new ReaderParameters { AssemblyResolver = resolver });
foreach (var type in module.GetTypes())
foreach (var method in type.Methods.Where(m => m.HasBody))
foreach (var instruction in method.Body.Instructions)
{
    if (instruction.Operand is not MemberReference member) continue;
    string scope = (member is TypeReference t ? t.Scope?.Name : member.DeclaringType?.Scope?.Name) ?? "";
    if (!scope.StartsWith("assembly_", StringComparison.Ordinal) && !scope.StartsWith("Unity", StringComparison.Ordinal) && scope != "gui_framework") continue;
    count++;
    try
    {
        switch (member)
        {
            case FieldReference field:
                var resolvedField = field.Resolve();
                Check(resolvedField != null, "Unresolved field " + member);
                if (resolvedField != null)
                    Check(!resolvedField.IsPrivate && !resolvedField.IsAssembly && !resolvedField.IsFamilyAndAssembly,
                        "Direct non-public game field access " + member + " in " + method.FullName);
                break;
            case MethodReference called:
                var resolvedMethod = called.Resolve();
                Check(resolvedMethod != null, "Unresolved method " + member);
                if (resolvedMethod != null)
                    Check(!resolvedMethod.IsPrivate && !resolvedMethod.IsAssembly && !resolvedMethod.IsFamilyAndAssembly,
                        "Direct non-public game method access " + member + " in " + method.FullName);
                break;
            case TypeReference referencedType:
                Check(referencedType.Resolve() != null, "Unresolved type " + member);
                break;
        }
    }
    catch (Exception exception) { Check(false, member + ": " + exception.Message); }
}

Check(!module.Assembly.CustomAttributes.Any(a => a.AttributeType.Name == "IgnoresAccessChecksToAttribute"),
    "Unexpected access-check bypass attribute");
Check(!module.AssemblyReferences.Any(a => a.Name is "ServerSync" or "Jotunn" or "CreatureManager" or "NPCGoeslive"
    or "StatusEffectFilter" or "CompactStatusEffects" or "MyLittleUI"),
    "Reference examples must not become runtime dependencies");
Check(!module.AssemblyReferences.Any(a => a.Name is "YamlDotNet" or "LocalizationManager"),
    "Translation support must not require a separate runtime DLL");
Check(module.GetType("YamlDotNet.RepresentationModel.YamlStream") is { IsPublic: false },
    "The YAML parser is merged and internalized");
foreach (string language in new[] { "English", "Korean" })
    Check(module.Resources.OfType<EmbeddedResource>().Any(r =>
        r.Name == "ReferToCompendium.translations." + language + ".yml"), "Embedded YAML " + language);
Check(module.Resources.OfType<EmbeddedResource>().Any(r => r.Name.EndsWith("ThirdPartyNotices.txt")),
    "Single-DLL builds retain third-party license notices");

using var game = ModuleDefinition.ReadModule(Path.Combine(args[1], "assembly_valheim.dll"));
var dialog = game.GetType("TextsDialog");
Check(dialog != null, "TextsDialog type");
if (dialog != null)
{
    foreach (var expected in new[] { ("m_texts", "System.Collections.Generic.List`1<TextsDialog/TextInfo>"), ("m_selectionIndex", "System.Int32"), ("m_baseListSize", "System.Single") })
    {
        var field = dialog.Fields.SingleOrDefault(f => f.Name == expected.Item1);
        Check(field != null && field.IsPrivate && field.FieldType.FullName == expected.Item2,
            "Original private field contract " + expected.Item1);
    }
    foreach (var name in new[] { "Setup", "FillTextList", "SnapTo" })
        Check(dialog.Methods.Count(m => m.Name == name) == 1, "Unique Harmony target " + name);
    Check(dialog.Methods.Any(m => m.Name == "ShowText" && m.IsPrivate && m.ReturnType.FullName == "System.Void"
        && m.Parameters.Count == 1 && m.Parameters[0].ParameterType.FullName == "TextsDialog/TextInfo"), "Private ShowText(TextInfo) overload");
    Check(dialog.Methods.Any(m => m.Name == "SnapTo" && m.IsPrivate && !m.IsStatic
        && m.ReturnType.FullName == "System.Void" && m.Parameters.Select(p => p.ParameterType.FullName)
            .SequenceEqual(new[] { "UnityEngine.UI.ScrollRect", "UnityEngine.RectTransform", "UnityEngine.RectTransform" })),
        "Private delayed scroll target used by the drag guard");
}
var chat = game.GetType("Chat");
Check(chat?.Fields.Any(f => f.Name == "m_npcTextBase" && f.IsPublic && f.FieldType.FullName == "UnityEngine.GameObject") == true, "Public native speech prefab");
var inventory = game.GetType("InventoryGui");
Check(inventory?.Methods.Any(m => m.Name == "OnOpenTexts" && m.IsPublic && m.Parameters.Count == 0) == true, "Raven click callback");

var hud = game.GetType("Hud");
Check(hud?.Methods.Count(m => m.Name == "UpdateStatusEffects") == 1, "Unique HUD status effect Harmony target");
Check(hud?.Methods.Any(m => m.Name == "UpdateStatusEffects" && m.IsPrivate && !m.IsStatic
    && m.ReturnType.FullName == "System.Void" && m.Parameters.Count == 1
    && m.Parameters[0].ParameterType.FullName == "System.Collections.Generic.List`1<StatusEffect>") == true,
    "Private Hud.UpdateStatusEffects(List<StatusEffect>) contract");
Check(hud?.Fields.Any(f => f.Name == "m_statusEffects" && f.IsPrivate && !f.IsStatic
    && f.FieldType.FullName == "System.Collections.Generic.List`1<UnityEngine.RectTransform>") == true,
    "Original private HUD row field for Harmony injection");
Check(hud?.Methods.Count(m => m.Name == "OnDestroy" && !m.IsStatic
    && m.ReturnType.FullName == "System.Void" && m.Parameters.Count == 0) == 1,
    "Unique HUD cleanup Harmony target");
var statusHud = module.GetType("ReferToCompendium.StatusEffectHud");
var filterPrefix = statusHud?.Methods.SingleOrDefault(m => m.Name == "Prefix");
Check(filterPrefix != null && filterPrefix.IsStatic && filterPrefix.ReturnType.FullName == "System.Void"
    && filterPrefix.Parameters.Count == 1 && filterPrefix.Parameters[0].Name == "statusEffects"
    && filterPrefix.Parameters[0].ParameterType.FullName == "System.Collections.Generic.List`1<StatusEffect>&",
    "Filter prefix replaces the HUD list argument without suppressing native rendering");
if (filterPrefix != null)
{
    Check(filterPrefix.CustomAttributes.Any(a => a.AttributeType.FullName == "HarmonyLib.HarmonyAfter"
        && a.ConstructorArguments.Count == 1
        && a.ConstructorArguments[0].Value is CustomAttributeArgument[] owners
        && owners.Any(owner => (string?)owner.Value == "Azumatt.StatusEffectFilter")),
        "Filter prefix runs after the external StatusEffectFilter");
    Check(filterPrefix.CustomAttributes.Any(a => a.AttributeType.FullName == "HarmonyLib.HarmonyPriority"
        && a.ConstructorArguments.Count == 1 && a.ConstructorArguments[0].Value is int priority && priority == 0),
        "Filter prefix uses Priority.Last");
}
var layoutPostfix = statusHud?.Methods.SingleOrDefault(m => m.Name == "Postfix");
Check(layoutPostfix != null && layoutPostfix.IsStatic && layoutPostfix.ReturnType.FullName == "System.Void"
    && layoutPostfix.Parameters.Count == 2
    && layoutPostfix.Parameters[0].Name == "__instance" && layoutPostfix.Parameters[0].ParameterType.FullName == "Hud"
    && layoutPostfix.Parameters[1].Name == "___m_statusEffects"
    && layoutPostfix.Parameters[1].ParameterType.FullName == "System.Collections.Generic.List`1<UnityEngine.RectTransform>",
    "Layout postfix accesses private HUD rows through Harmony field injection");

System.Console.WriteLine($"Checked {count} game/Unity IL references and native UI metadata contracts; {failures} failure(s). No Unity scene or actual gameplay executed.");
return failures == 0 ? 0 : 1;
