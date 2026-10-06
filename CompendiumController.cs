using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ReferToCompendium;

/// <summary>
/// Arranges the rows other mods already created. Their TextInfo objects and button
/// listeners stay intact. Hidden rows are retained until the game's next refresh.
/// </summary>
internal sealed partial class CompendiumController : MonoBehaviour
{
    private static readonly AccessTools.FieldRef<TextsDialog, List<TextsDialog.TextInfo>> Texts =
        AccessTools.FieldRefAccess<TextsDialog, List<TextsDialog.TextInfo>>("m_texts");
    private static readonly AccessTools.FieldRef<TextsDialog, int> SelectionIndex =
        AccessTools.FieldRefAccess<TextsDialog, int>("m_selectionIndex");
    private static readonly AccessTools.FieldRef<TextsDialog, float> BaseListSize =
        AccessTools.FieldRefAccess<TextsDialog, float>("m_baseListSize");
    private static readonly MethodInfo ShowText = AccessTools.Method(
        typeof(TextsDialog), "ShowText", new[] { typeof(TextsDialog.TextInfo) });
    private static readonly HashSet<CompendiumController> Instances = new();

    private readonly List<Entry> _entries = new();
    private readonly List<TextsDialog.TextInfo> _displayed = new();
    private TextsDialog _dialog = null!;
    private ScrollRect? _listScroll;
    private TextsDialog.TextInfo? _selection;
    private TextsDialog.TextInfo? _collapsedHeader;
    private GameObject? _toolbar;
    private TMP_Text? _guideLabel;
    private TMP_Text? _resetLabel;
    private bool _expanded;
    private bool _pending;
    private int _readyFrame;
    private int _lastHeaderToggleFrame = -1;
    private bool _disposed;

    internal static void Schedule(TextsDialog dialog)
    {
        CompendiumController controller = dialog.GetComponent<CompendiumController>();
        if (controller == null)
        {
            controller = dialog.gameObject.AddComponent<CompendiumController>();
            controller._dialog = dialog;
            Instances.Add(controller);
        }
        controller._pending = true;
        // Setup postfixes may still add entries or replace rows after ours runs.
        controller._readyFrame = Time.frameCount + 1;
    }

    internal static void BeforeFill(TextsDialog dialog)
    {
        CompendiumController controller = dialog.GetComponent<CompendiumController>();
        if (controller != null)
        {
            controller.ReleaseRows(false);
        }
    }

    internal static void Selected(TextsDialog dialog, TextsDialog.TextInfo text)
    {
        CompendiumController controller = dialog.GetComponent<CompendiumController>();
        if (controller == null || controller._disposed)
        {
            return;
        }
        controller._selection = text;
        int index = Texts(dialog).IndexOf(text);
        if (index >= 0)
        {
            // The vanilla mouse overload does not update this gamepad cursor.
            SelectionIndex(dialog) = index;
        }
    }

    internal static void Shutdown()
    {
        foreach (CompendiumController controller in Instances.ToArray())
        {
            if (controller != null)
            {
                controller.ReleaseRows(true);
                controller._disposed = true;
                Destroy(controller);
            }
        }
        Instances.Clear();
    }

    internal static void RefreshTranslations()
    {
        foreach (CompendiumController controller in Instances.ToArray())
        {
            if (controller == null || controller._disposed) continue;
            // Never recapture third-party titles or recalculate stable IDs when
            // the language changes. Only our labels and synthetic page refresh.
            controller.CancelDrag();
            if (controller._guideLabel != null)
                controller._guideLabel.text = UiLocalization.Get(UiLocalization.DragHint);
            if (controller._resetLabel != null)
                controller._resetLabel.text = UiLocalization.Get(UiLocalization.Reset);
            if (controller._collapsedHeader?.m_listElement != null)
            {
                controller.UpdateHeader(controller._entries.Count(entry => Plugin.State.IsCollapsed(entry.Id)));
                if (controller._selection == controller._collapsedHeader)
                {
                    // Assign in place: calling ShowText here would start a scroll
                    // coroutine and disturb the user's current reading position.
                    controller._dialog.m_textAreaTopic.text = controller._collapsedHeader.m_topic;
                    controller._dialog.m_textArea.text = controller._collapsedHeader.m_text;
                }
            }
        }
    }

    private void LateUpdate()
    {
        if (_disposed || _dialog == null)
        {
            return;
        }
        if (!_pending)
        {
            if (_dragEntry != null)
            {
                UpdateDrag();
                return;
            }
            if (_collapsedHeader != null && _selection == _collapsedHeader &&
                ZInput.IsExclusiveGamepadActive() && ZInput.GetButtonDown("JoyButtonA"))
            {
                ToggleHeader();
            }
            return;
        }
        if (Time.frameCount < _readyFrame)
        {
            return;
        }
        _pending = false;
        try
        {
            CaptureRows();
            ApplyLayout();
        }
        catch (Exception exception)
        {
            Plugin.LogWarning("Compendium layout could not be applied: " + exception);
            ReleaseRows(true);
        }
    }

    private void OnDestroy()
    {
        CancelDrag();
        Instances.Remove(this);
    }

    private void OnDisable() => CancelDrag();

    private float RowHeight => Mathf.Max(32f, _dialog.m_spacing);
    private float ButtonSize => Mathf.Clamp(RowHeight * 0.55f, 24f, 42f);

    private void CaptureRows()
    {
        // A second Setup without FillTextList is uncommon, but do not lose any
        // rows retained from the first pass or accidentally capture our header.
        var current = Texts(_dialog).Where(text => text != _collapsedHeader).ToList();
        foreach (Entry previous in _entries)
        {
            if (previous.Info.m_listElement != null && !current.Contains(previous.Info))
            {
                current.Add(previous.Info);
            }
        }
        ClearDecorations();
        // The shipped dialog's m_leftScrollRect points at the right-hand body.
        // Match the actual list content instead of trusting that field's name.
        _listScroll = _dialog.m_listRoot.GetComponentsInParent<ScrollRect>(true)
            .FirstOrDefault(scroll => scroll.content == _dialog.m_listRoot) ??
            _dialog.GetComponentsInChildren<ScrollRect>(true)
                .FirstOrDefault(scroll => scroll.content == _dialog.m_listRoot);
        if (_listScroll == null)
        {
            Plugin.LogWarning("Compendium title list scroll area was not found; drag ordering is unavailable.");
        }
        _entries.Clear();
        Dictionary<string, string> knownTopics = BuildKnownTopicKeys();
        var occurrences = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (TextsDialog.TextInfo info in current)
        {
            if (info == null)
            {
                continue;
            }
            string basis = StableTopicKey(info.m_topic ?? string.Empty, knownTopics);
            Type concreteType = info.GetType();
            if (concreteType != typeof(TextsDialog.TextInfo))
            {
                basis = "provider:" + concreteType.Assembly.GetName().Name + ":" +
                    concreteType.FullName + ":" + basis;
            }
            occurrences.TryGetValue(basis, out int occurrence);
            occurrences[basis] = occurrence + 1;
            // Length-prefixing avoids a literal title colliding with a suffix.
            string id = basis.Length + ":" + basis + ":" + occurrence;
            EnsureRow(info);
            var entry = new Entry(id, info);
            _entries.Add(entry);
            if (info.m_selected != null && info.m_selected.activeSelf)
            {
                _selection = info;
            }
        }
        if (_entries.Count > 0)
        {
            CreateToolbar();
        }
    }

    private static Dictionary<string, string> BuildKnownTopicKeys()
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (Player.m_localPlayer == null || Localization.instance == null)
        {
            return result;
        }
        foreach (KeyValuePair<string, string> pair in Player.m_localPlayer.GetKnownTexts())
        {
            string raw = pair.Key.Replace("\u0016", string.Empty);
            string localized = Localization.instance.Localize(raw);
            // Identical localized titles are ambiguous; their text must never be
            // used as identity because stats and mod page bodies change often.
            if (!result.ContainsKey(localized))
            {
                result.Add(localized, raw);
            }
            else if (result[localized] != raw)
            {
                result[localized] = string.Empty;
            }
        }
        return result;
    }

    private static string StableTopicKey(string topic, Dictionary<string, string> knownTopics)
    {
        string[] builtins = { "$inventory_activeeffects", "$inventory_logs", "$inventory_stats" };
        foreach (string builtin in builtins)
        {
            if (topic == builtin || Localization.instance != null && topic == Localization.instance.Localize(builtin))
            {
                return "builtin:" + builtin;
            }
        }
        if (topic.StartsWith("$", StringComparison.Ordinal))
        {
            return "token:" + topic;
        }
        if (knownTopics.TryGetValue(topic, out string key) && key.Length > 0)
        {
            return "known:" + key;
        }
        return "title:" + topic;
    }

    private void EnsureRow(TextsDialog.TextInfo info)
    {
        if (info.m_listElement == null)
        {
            // Some mods append a TextInfo from Setup rather than UpdateTextsList.
            // Only such missing rows need a new listener; existing ones are kept.
            info.m_listElement = Instantiate(_dialog.m_elementPrefab, _dialog.m_listRoot, false);
            info.m_listElement.SetActive(true);
            Transform name = Utils.FindChild(info.m_listElement.transform, "name");
            if (name != null)
            {
                name.GetComponent<TMP_Text>().text = Localization.instance.Localize(info.m_topic);
            }
            info.m_listElement.GetComponent<Button>().onClick.AddListener(() => Select(info));
        }
        Transform selected = Utils.FindChild(info.m_listElement.transform, "selected");
        if (info.m_selected == null && selected != null)
        {
            info.m_selected = selected.gameObject;
            info.m_selected.SetActive(false);
        }
    }

    private void ApplyLayout()
    {
        CancelDrag();
        if (_entries.Count == 0)
        {
            return;
        }
        Dictionary<string, Entry> byId = _entries.ToDictionary(entry => entry.Id, StringComparer.Ordinal);
        List<Entry> ordered = Plugin.State.GetOrderedIds(_entries.Select(entry => entry.Id))
            .Where(byId.ContainsKey).Select(id => byId[id]).ToList();
        List<Entry> visible = ordered.Where(entry => !Plugin.State.IsCollapsed(entry.Id)).ToList();
        List<Entry> collapsed = ordered.Where(entry => Plugin.State.IsCollapsed(entry.Id)).ToList();
        _displayed.Clear();
        float y = 0f;
        for (int index = 0; index < visible.Count; index++)
        {
            Entry entry = visible[index];
            ArrangeRow(entry, y, false);
            _displayed.Add(entry.Info);
            y += RowHeight;
        }
        if (collapsed.Count > 0)
        {
            EnsureHeader();
            UpdateHeader(collapsed.Count);
            SetRowPosition(_collapsedHeader!.m_listElement, y);
            _collapsedHeader.m_listElement.SetActive(true);
            _displayed.Add(_collapsedHeader);
            y += RowHeight;
            for (int index = 0; index < collapsed.Count; index++)
            {
                Entry entry = collapsed[index];
                if (_expanded)
                {
                    ArrangeRow(entry, y, true);
                    _displayed.Add(entry.Info);
                    y += RowHeight;
                }
                else
                {
                    entry.Info.m_listElement.SetActive(false);
                    if (entry.Info.m_selected != null)
                    {
                        entry.Info.m_selected.SetActive(false);
                    }
                }
            }
        }
        else if (_collapsedHeader != null)
        {
            _collapsedHeader.m_listElement.SetActive(false);
        }

        // Vanilla navigation now sees exactly the displayed entries, in the same
        // order. ReleaseRows restores hidden entries before vanilla destroys rows.
        List<TextsDialog.TextInfo> texts = Texts(_dialog);
        texts.Clear();
        texts.AddRange(_displayed);
        _dialog.m_listRoot.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical,
            Mathf.Max(BaseListSize(_dialog), y));
        if (_selection != null && _displayed.Contains(_selection))
        {
            SelectionIndex(_dialog) = _displayed.IndexOf(_selection);
        }
        else if (_displayed.Count > 0)
        {
            Select(collapsed.Any(entry => entry.Info == _selection)
                ? _collapsedHeader! : _displayed[0]);
        }
        LayoutRebuilder.MarkLayoutForRebuild(_dialog.m_listRoot);
    }

    private void ArrangeRow(Entry entry, float y, bool collapsed)
    {
        entry.Info.m_listElement.SetActive(true);
        SetRowPosition(entry.Info.m_listElement, y);
        if (entry.Controls == null)
        {
            CreateRowControls(entry);
        }
        if (entry.Drag == null && _listScroll != null)
        {
            entry.Drag = entry.Info.m_listElement.AddComponent<CompendiumRowDrag>();
            entry.Drag.Scroll = _listScroll;
            entry.Drag.Controls = entry.Controls!.transform;
            entry.Drag.Begin = data => BeginDrag(entry, data);
            entry.Drag.Track = data => TrackDrag(entry, data);
            entry.Drag.Finish = data => FinishDrag(entry, data);
            entry.Drag.Cancel = CancelDrag;
        }
        entry.CollapseLabel!.text = collapsed ? "+" : "-";
        if (entry.Label != null)
        {
            Vector2 max = entry.OriginalOffsetMax;
            max.x -= ButtonSize + 8f;
            entry.Label.rectTransform.offsetMax = max;
        }
    }

    private static void SetRowPosition(GameObject row, float y)
    {
        RectTransform rect = (RectTransform)row.transform;
        rect.anchoredPosition = new Vector2(rect.anchoredPosition.x, -y);
    }

    private void CreateToolbar()
    {
        _toolbar = new GameObject("ReferToCompendium.Toolbar", typeof(RectTransform));
        _toolbar.layer = _dialog.m_listRoot.gameObject.layer;
        RectTransform rect = (RectTransform)_toolbar.transform;
        // TextList is the fixed panel above the masked SkillList ScrollRect.
        // A sibling of the ScrollRect stays visible while its content scrolls.
        Transform panel = _listScroll != null ? _listScroll.transform.parent :
            _dialog.m_listRoot.parent.parent;
        rect.SetParent(panel != null ? panel : _dialog.transform, false);
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(0.5f, 0f);
        rect.sizeDelta = new Vector2(-8f, 32f);
        rect.anchoredPosition = new Vector2(0f, 8f);
        TMP_Text style = _entries[0].Label ?? _dialog.m_textAreaTopic;
        var guideObject = new GameObject("DragGuide", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        guideObject.layer = _toolbar.layer;
        guideObject.transform.SetParent(rect, false);
        TMP_Text guide = guideObject.GetComponent<TMP_Text>();
        guide.font = style.font;
        guide.fontSharedMaterial = style.fontSharedMaterial;
        guide.fontSize = Mathf.Clamp(style.fontSize * 0.55f, 14f, 16f);
        guide.enableAutoSizing = true;
        guide.fontSizeMin = 12f;
        guide.fontSizeMax = guide.fontSize;
        guide.textWrappingMode = TextWrappingModes.Normal;
        guide.alignment = TextAlignmentOptions.MidlineLeft;
        guide.color = new Color(1f, 0.77f, 0.4f, 1f);
        guide.raycastTarget = false;
        guide.text = UiLocalization.Get(UiLocalization.DragHint);
        _guideLabel = guide;
        SetStretch(guide.rectTransform, Vector2.zero, new Vector2(0.78f, 1f));
        guide.rectTransform.offsetMin = new Vector2(2f, 0f);
        guide.rectTransform.offsetMax = new Vector2(-6f, 0f);
        Button reset = MakeButton(rect, "Reset", UiLocalization.Get(UiLocalization.Reset), style, () =>
        {
            Plugin.State.Reset(_entries.Select(entry => entry.Id));
            Plugin.SaveLayout();
            _expanded = false;
            ApplyLayout();
        });
        RectTransform resetRect = (RectTransform)reset.transform;
        SetStretch(resetRect, new Vector2(0.8f, 0.5f), new Vector2(1f, 0.5f));
        resetRect.sizeDelta = new Vector2(0f, Mathf.Min(ButtonSize, 28f));
        _resetLabel = reset.GetComponentInChildren<TMP_Text>();
    }

    private void CreateRowControls(Entry entry)
    {
        entry.Controls = new GameObject("ReferToCompendium.Controls", typeof(RectTransform));
        entry.Controls.layer = entry.Info.m_listElement.layer;
        RectTransform rect = (RectTransform)entry.Controls.transform;
        rect.SetParent(entry.Info.m_listElement.transform, false);
        rect.anchorMin = new Vector2(1f, 0.5f);
        rect.anchorMax = new Vector2(1f, 0.5f);
        rect.pivot = new Vector2(1f, 0.5f);
        rect.sizeDelta = new Vector2(ButtonSize, ButtonSize);
        rect.anchoredPosition = new Vector2(-4f, 0f);
        TMP_Text style = entry.Label ?? _dialog.m_textAreaTopic;
        Button collapse = MakeButton(rect, "CollapseRestore", "-", style, () =>
        {
            Plugin.State.SetCollapsed(entry.Id, !Plugin.State.IsCollapsed(entry.Id));
            Plugin.SaveLayout();
            ApplyLayout();
        });
        entry.CollapseLabel = collapse.GetComponentInChildren<TMP_Text>();
        SetStretch((RectTransform)collapse.transform, Vector2.zero, Vector2.one);
    }

    private void EnsureHeader()
    {
        if (_collapsedHeader != null && _collapsedHeader.m_listElement != null)
        {
            return;
        }
        _collapsedHeader = new TextsDialog.TextInfo(string.Empty, string.Empty);
        GameObject row = Instantiate(_dialog.m_elementPrefab, _dialog.m_listRoot, false);
        row.name = "ReferToCompendium.CollapsedGroup";
        _collapsedHeader.m_listElement = row;
        _collapsedHeader.m_selected = Utils.FindChild(row.transform, "selected").gameObject;
        _collapsedHeader.m_selected.SetActive(false);
        row.GetComponent<Button>().onClick.AddListener(ToggleHeader);
    }

    private void ToggleHeader()
    {
        if (_collapsedHeader == null || _lastHeaderToggleFrame == Time.frameCount)
        {
            return;
        }
        _lastHeaderToggleFrame = Time.frameCount;
        _expanded = !_expanded;
        Select(_collapsedHeader);
        ApplyLayout();
    }

    private void UpdateHeader(int count)
    {
        _collapsedHeader!.m_topic = UiLocalization.Get(UiLocalization.CollapsedTitle);
        _collapsedHeader.m_text = UiLocalization.Get(UiLocalization.CollapsedHelp);
        TMP_Text label = Utils.FindChild(_collapsedHeader.m_listElement.transform, "name").GetComponent<TMP_Text>();
        label.text = (_expanded ? "[-] " : "[+] ") + _collapsedHeader.m_topic + " (" + count + ")";
        label.color = new Color(1f, 0.76f, 0.37f, 1f);
    }

    private void Select(TextsDialog.TextInfo info)
    {
        if (info == null || info.m_listElement == null)
        {
            return;
        }
        // Cached original metadata, invoked through the patched game method so
        // other mods' ShowText postfixes (including CreatureManager icons) run.
        ShowText.Invoke(_dialog, new object[] { info });
        Selected(_dialog, info);
    }

    private void ReleaseRows(bool restoreLayout)
    {
        CancelDrag();
        _pending = false;
        if (_dialog == null)
        {
            return;
        }
        List<TextsDialog.TextInfo> texts = Texts(_dialog);
        if (_entries.Count > 0)
        {
            var additions = texts.Where(info => info != _collapsedHeader &&
                !_entries.Any(entry => entry.Info == info)).ToArray();
            texts.Clear();
            texts.AddRange(_entries.Select(entry => entry.Info));
            texts.AddRange(additions);
        }
        else if (_collapsedHeader != null)
        {
            texts.Remove(_collapsedHeader);
        }
        ClearDecorations();
        if (restoreLayout)
        {
            for (int index = 0; index < texts.Count; index++)
            {
                if (texts[index].m_listElement != null)
                {
                    texts[index].m_listElement.SetActive(true);
                    SetRowPosition(texts[index].m_listElement, index * RowHeight);
                }
            }
            _dialog.m_listRoot.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical,
                Mathf.Max(BaseListSize(_dialog), texts.Count * RowHeight));
            if (_selection != null && texts.Contains(_selection))
            {
                SelectionIndex(_dialog) = texts.IndexOf(_selection);
            }
            else if (texts.Count > 0 && _dialog.gameObject.activeInHierarchy)
            {
                Select(texts[0]);
            }
        }
        _entries.Clear();
        _displayed.Clear();
        _selection = null;
    }

    private void ClearDecorations()
    {
        CancelDrag();
        foreach (Entry entry in _entries)
        {
            if (entry.Drag != null)
            {
                entry.Drag.enabled = false;
                Destroy(entry.Drag);
                entry.Drag = null;
            }
            if (entry.Label != null)
            {
                entry.Label.rectTransform.offsetMax = entry.OriginalOffsetMax;
            }
            RemoveObject(entry.Controls);
            entry.Controls = null;
        }
        RemoveObject(_toolbar);
        _toolbar = null;
        _guideLabel = null;
        _resetLabel = null;
        if (_collapsedHeader != null)
        {
            RemoveObject(_collapsedHeader.m_listElement);
            _collapsedHeader = null;
        }
    }

    private static void RemoveObject(GameObject? gameObject)
    {
        if (gameObject != null)
        {
            gameObject.SetActive(false);
            Destroy(gameObject);
        }
    }

    private static Button MakeButton(Transform parent, string name, string caption, TMP_Text style, Action onClick)
    {
        var gameObject = new GameObject("ReferToCompendium." + name,
            typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        gameObject.layer = parent.gameObject.layer;
        gameObject.transform.SetParent(parent, false);
        Image image = gameObject.GetComponent<Image>();
        image.color = new Color(0.12f, 0.09f, 0.055f, 0.93f);
        Button button = gameObject.GetComponent<Button>();
        button.targetGraphic = image;
        ColorBlock colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1.6f, 1.5f, 1.2f, 1f);
        colors.pressedColor = new Color(0.8f, 0.65f, 0.4f, 1f);
        colors.disabledColor = new Color(0.55f, 0.55f, 0.55f, 0.65f);
        button.colors = colors;
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        button.onClick.AddListener(() => onClick());
        var labelObject = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        labelObject.layer = gameObject.layer;
        labelObject.transform.SetParent(gameObject.transform, false);
        TMP_Text label = labelObject.GetComponent<TMP_Text>();
        label.font = style.font;
        label.fontSharedMaterial = style.fontSharedMaterial;
        label.fontSize = Mathf.Clamp(style.fontSize * 0.75f, 16f, 28f);
        label.enableAutoSizing = true;
        label.fontSizeMin = 12f;
        label.fontSizeMax = label.fontSize;
        label.alignment = TextAlignmentOptions.Center;
        label.color = new Color(1f, 0.77f, 0.4f, 1f);
        label.raycastTarget = false;
        label.text = caption;
        SetStretch(label.rectTransform, Vector2.zero, Vector2.one);
        label.rectTransform.offsetMin = new Vector2(3f, 1f);
        label.rectTransform.offsetMax = new Vector2(-3f, -1f);
        return button;
    }

    private static void SetStretch(RectTransform? rect, Vector2 min, Vector2 max)
    {
        if (rect == null)
        {
            return;
        }
        rect.anchorMin = min;
        rect.anchorMax = max;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private sealed class Entry
    {
        internal readonly string Id;
        internal readonly TextsDialog.TextInfo Info;
        internal readonly TMP_Text? Label;
        internal readonly Vector2 OriginalOffsetMax;
        internal GameObject? Controls;
        internal TMP_Text? CollapseLabel;
        internal CompendiumRowDrag? Drag;

        internal Entry(string id, TextsDialog.TextInfo info)
        {
            Id = id;
            Info = info;
            Transform name = Utils.FindChild(info.m_listElement.transform, "name");
            Label = name != null ? name.GetComponent<TMP_Text>() : null;
            OriginalOffsetMax = Label != null ? Label.rectTransform.offsetMax : Vector2.zero;
        }
    }
}

[HarmonyPatch(typeof(TextsDialog), "Setup")]
internal static class CompendiumSetupPatch
{
    [HarmonyPostfix, HarmonyPriority(Priority.Last)]
    private static void Postfix(TextsDialog __instance) => CompendiumController.Schedule(__instance);
}

[HarmonyPatch(typeof(TextsDialog), "FillTextList")]
internal static class CompendiumFillPatch
{
    [HarmonyPrefix, HarmonyPriority(Priority.First)]
    private static void Prefix(TextsDialog __instance) => CompendiumController.BeforeFill(__instance);
}

[HarmonyPatch(typeof(TextsDialog), "ShowText", new[] { typeof(TextsDialog.TextInfo) })]
internal static class CompendiumSelectionPatch
{
    [HarmonyPostfix, HarmonyPriority(Priority.Last)]
    private static void Postfix(TextsDialog __instance, TextsDialog.TextInfo text) =>
        CompendiumController.Selected(__instance, text);
}
