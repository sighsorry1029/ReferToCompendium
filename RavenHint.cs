using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ReferToCompendium;

/// <summary>A local UI copy of the native ambient-speech presentation.</summary>
internal static class RavenHint
{
    private const float Padding = 8f;
    private const float BeakGap = 4f;
    private const float ScreenMargin = 10f;
    private static InventoryGui? _inventory;
    private static Button? _raven;
    private static Canvas? _canvas;
    private static RectTransform? _layer;
    private static RectTransform? _bubble;
    private static TMP_Text? _text;
    private static CanvasGroup? _layerGroup;
    private static CanvasGroup[] _bubbleGroups = System.Array.Empty<CanvasGroup>();
    private static GameObject? _failedPrefab;
    private static bool _wasOpen;
    private static bool _showing;
    private static bool _warnedMissingButton;
    private static bool _warnedMissingPrefab;
    private static float _nextFind;
    private static float _nextCreate;
    private static float _openedAt;
    private static float _layoutWidth;
    private static float _layoutHeight;
    private static string _currentText = "";

    internal static void Tick(bool enabled, string text)
    {
        InventoryGui? inventory = InventoryGui.instance;
        // Unity's overloaded equality considers an already destroyed component
        // equal to null. Managed identity also detects the unload-to-null edge,
        // allowing us to release the old scene's remaining references promptly.
        if (!object.ReferenceEquals(_inventory, inventory))
        {
            Dispose();
            _inventory = inventory;
        }

        bool open = enabled && inventory != null && Player.m_localPlayer != null
                    && InventoryGui.IsVisible();
        if (!open)
        {
            HideImmediately();
            _wasOpen = false;
            return;
        }

        float now = Time.unscaledTime;
        if (!_wasOpen)
        {
            _wasOpen = true;
            _openedAt = now;
        }

        // Keep the invitation visible throughout the inventory visit. A modal
        // panel hides it only while that panel is open; closing it restores it.
        if (string.IsNullOrWhiteSpace(text) || HasBlockingPanel(inventory!))
        {
            HideImmediately();
            return;
        }

        if (_raven == null && now >= _nextFind)
        {
            _nextFind = now + 1f;
            _raven = FindRaven(inventory!);
            if (_raven == null && now - _openedAt > 5f && !_warnedMissingButton)
            {
                _warnedMissingButton = true;
                Debug.LogWarning("[ReferToCompendium] Could not find the Compendium button's OnOpenTexts listener; the raven hint is unavailable for this inventory UI.");
            }
        }
        if (_raven == null || !_raven.gameObject.activeInHierarchy || !_raven.interactable)
        {
            HideImmediately();
            return;
        }

        if (_bubble == null)
        {
            if (now < _nextCreate) return;
            _nextCreate = now + 1f;
            if (!CreateBubble(inventory!)) return;
        }
        Vector2 beak = GetBeakPosition();
        UpdateLayout(text, beak);

        if (!_showing || !_bubble!.gameObject.activeSelf)
        {
            _showing = true;
            _bubble!.gameObject.SetActive(true);
        }
        KeepOpaque();
        PositionBubble(beak);
    }

    private static bool HasBlockingPanel(InventoryGui inventory)
    {
        return Menu.IsVisible() || Console.IsVisible()
               || (Chat.instance != null && Chat.instance.HasFocus())
               || (TextViewer.instance != null && TextViewer.instance.IsVisible())
               || (Hud.instance != null && Hud.instance.m_userHidden)
               || (inventory.m_textsDialog != null && inventory.m_textsDialog.gameObject.activeInHierarchy)
               || (inventory.m_skillsDialog != null && inventory.m_skillsDialog.gameObject.activeInHierarchy)
               || (inventory.m_trophiesPanel != null && inventory.m_trophiesPanel.activeInHierarchy)
               || (inventory.m_achievementsPanel != null && inventory.m_achievementsPanel.gameObject.activeInHierarchy)
               || (inventory.m_variantDialog != null && inventory.m_variantDialog.gameObject.activeInHierarchy)
               || (inventory.m_splitDialog != null && inventory.m_splitDialog.IsActive);
    }

    private static Button? FindRaven(InventoryGui inventory)
    {
        // The serialized callback is a stable semantic identifier, independent
        // of the button name, localization, hierarchy or order of the icons.
        foreach (Button button in inventory.GetComponentsInChildren<Button>(true))
        {
            for (int i = 0; i < button.onClick.GetPersistentEventCount(); ++i)
            {
                if (button.onClick.GetPersistentTarget(i) == inventory
                    && button.onClick.GetPersistentMethodName(i) == nameof(InventoryGui.OnOpenTexts))
                    return button;
            }
        }
        return null;
    }

    private static bool CreateBubble(InventoryGui inventory)
    {
        if (Chat.instance == null || Chat.instance.m_npcTextBase == null) return false;
        GameObject prefab = Chat.instance.m_npcTextBase;
        if (object.ReferenceEquals(_failedPrefab, prefab)) return false;
        Canvas? inventoryCanvas = inventory.GetComponentInParent<Canvas>();
        _canvas = inventoryCanvas != null ? inventoryCanvas.rootCanvas : null;
        if (_canvas == null) return false;

        // Do not call SetNpcText or attach Talker/NpcTalk. Those presentations
        // belong to world-space actors and their lifecycle/chat/network systems.
        // Here only the game's existing ambient bubble artwork/font is reused.
        if (_layer != null) UnityEngine.Object.Destroy(_layer.gameObject);
        GameObject layer = new GameObject("ReferToCompendium.RavenHint", typeof(RectTransform), typeof(CanvasGroup));
        _layer = (RectTransform)layer.transform;
        _layer.SetParent(_canvas.transform, false);
        _layer.anchorMin = Vector2.zero;
        _layer.anchorMax = Vector2.one;
        _layer.offsetMin = Vector2.zero;
        _layer.offsetMax = Vector2.zero;
        _layerGroup = layer.GetComponent<CanvasGroup>();
        _layerGroup.alpha = 1f;
        _layerGroup.ignoreParentGroups = true;
        _layerGroup.interactable = false;
        _layerGroup.blocksRaycasts = false;

        GameObject clone = UnityEngine.Object.Instantiate(prefab, _layer, false);
        clone.name = "AmbientCompendiumHint";
        _bubble = clone.transform as RectTransform;
        Transform? textChild = clone.transform.Find("Text");
        _text = textChild != null ? textChild.GetComponent<TMP_Text>() : null;
        if (_bubble == null || _text == null)
        {
            _failedPrefab = prefab;
            if (!_warnedMissingPrefab)
            {
                _warnedMissingPrefab = true;
                Debug.LogWarning("[ReferToCompendium] Native ambient-speech UI is missing its RectTransform or Text child; raven hint disabled for this UI.");
            }
            UnityEngine.Object.Destroy(layer);
            _layer = null;
            _layerGroup = null;
            _bubble = null;
            _text = null;
            return false;
        }

        // Reuse the NPC artwork/font, but remove its transient presentation.
        // The native Animator owns CanvasGroup.alpha even after our TTL is gone.
        // Disable first (Destroy is deferred) and detach before forcing alpha.
        foreach (Animator animator in clone.GetComponentsInChildren<Animator>(true))
        {
            animator.enabled = false;
            animator.runtimeAnimatorController = null;
            UnityEngine.Object.Destroy(animator);
        }
        foreach (Animation animation in clone.GetComponentsInChildren<Animation>(true))
        {
            animation.Stop();
            animation.enabled = false;
            UnityEngine.Object.Destroy(animation);
        }
        foreach (Graphic graphic in clone.GetComponentsInChildren<Graphic>(true))
        {
            graphic.raycastTarget = false;
            graphic.canvasRenderer.SetAlpha(1f);
        }
        _bubbleGroups = clone.GetComponentsInChildren<CanvasGroup>(true);
        foreach (CanvasGroup childGroup in _bubbleGroups)
        {
            childGroup.alpha = 1f;
            childGroup.ignoreParentGroups = false;
            childGroup.interactable = false;
            childGroup.blocksRaycasts = false;
        }
        foreach (ContentSizeFitter fitter in clone.GetComponentsInChildren<ContentSizeFitter>(true)) fitter.enabled = false;
        foreach (LayoutGroup layout in clone.GetComponentsInChildren<LayoutGroup>(true)) layout.enabled = false;
        Transform? topic = clone.transform.Find("Topic");
        if (topic != null) topic.gameObject.SetActive(false);

        _bubble.anchorMin = _bubble.anchorMax = new Vector2(0.5f, 0.5f);
        _bubble.pivot = new Vector2(0.5f, 0.5f);
        _bubble.localScale = Vector3.one;
        _bubble.localRotation = Quaternion.identity;
        _text.enableAutoSizing = false;
        _text.textWrappingMode = TextWrappingModes.Normal;
        _text.overflowMode = TextOverflowModes.Ellipsis;
        _text.alignment = TextAlignmentOptions.Right;
        _text.maxVisibleCharacters = int.MaxValue;
        _text.maxVisibleWords = int.MaxValue;
        _text.maxVisibleLines = int.MaxValue;
        _text.alpha = 1f;
        _layoutWidth = _layoutHeight = 0f;
        _currentText = "";
        _showing = false;
        clone.SetActive(false);
        return true;
    }

    private static void KeepOpaque()
    {
        // Only our own groups are written. Visibility is controlled explicitly by
        // inventory/modal state, independent of inherited UI fade animations.
        if (_layerGroup != null) _layerGroup.alpha = 1f;
        foreach (CanvasGroup group in _bubbleGroups)
            if (group != null) group.alpha = 1f;
    }

    private static Vector2 GetBeakPosition()
    {
        // In all four native inventory variants the button's targetGraphic is
        // the 64x64 raven Icon. Use its upper-left beak, not the button corner.
        RectTransform icon = _raven!.targetGraphic != null
            ? _raven.targetGraphic.rectTransform : (RectTransform)_raven.transform;
        Rect rect = icon.rect;
        Vector3 localBeak = new Vector3(
            Mathf.Lerp(rect.xMin, rect.xMax, 0.10f),
            Mathf.Lerp(rect.yMin, rect.yMax, 0.72f), 0f);
        return _layer!.InverseTransformPoint(icon.TransformPoint(localBeak));
    }

    private static void UpdateLayout(string text, Vector2 beak)
    {
        if (_layer == null || _bubble == null || _text == null) return;
        // Wrap into the space in front of the left-facing raven. A narrow left
        // margin must not push the caption back over the other toolbar icons.
        float availableWidth = Mathf.Max(1f, Mathf.Min(
            _layer.rect.width - ScreenMargin * 2f,
            beak.x - BeakGap - (_layer.rect.xMin + ScreenMargin)));
        float availableHeight = Mathf.Max(1f, _layer.rect.height - ScreenMargin * 2f);
        if (_currentText == text && Mathf.Approximately(_layoutWidth, availableWidth)
                                 && Mathf.Approximately(_layoutHeight, availableHeight)) return;
        _currentText = text;
        _layoutWidth = availableWidth;
        _layoutHeight = availableHeight;
        _text.text = text;
        float width = Mathf.Min(280f, availableWidth);
        float fontSize = _text.fontSize > 0f ? _text.fontSize : 20f;
        float contentWidth = Mathf.Max(1f, width - Padding * 2f);
        float preferredHeight = _text.GetPreferredValues(text, contentWidth, float.PositiveInfinity).y;
        if (float.IsNaN(preferredHeight) || float.IsInfinity(preferredHeight)) preferredHeight = fontSize * 3f;
        float height = Mathf.Min(availableHeight,
            Mathf.Max(fontSize * 1.35f, preferredHeight) + Padding * 2f);
        _bubble.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
        _bubble.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
        RectTransform textRect = _text.rectTransform;
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(Padding, Padding);
        textRect.offsetMax = new Vector2(-Padding, -Padding);
    }

    private static void PositionBubble(Vector2 beak)
    {
        if (_raven == null || _layer == null || _bubble == null || _canvas == null) return;
        float width = _bubble.rect.width;
        float height = _bubble.rect.height;
        Rect bounds = _layer.rect;
        // The text's right edge faces the beak at the same height. Work in
        // canvas coordinates to follow UI scale and a moved inventory panel.
        float x = beak.x - BeakGap - width * 0.5f;
        float y = beak.y;
        x = Mathf.Clamp(x, bounds.xMin + ScreenMargin + width * 0.5f,
            bounds.xMax - ScreenMargin - width * 0.5f);
        y = Mathf.Clamp(y, bounds.yMin + ScreenMargin + height * 0.5f,
            bounds.yMax - ScreenMargin - height * 0.5f);
        _bubble.localPosition = new Vector3(x, y, 0f);
    }

    private static void HideImmediately()
    {
        _showing = false;
        if (_bubble != null) _bubble.gameObject.SetActive(false);
    }

    internal static void Dispose()
    {
        if (_layer != null) UnityEngine.Object.Destroy(_layer.gameObject);
        _inventory = null;
        _raven = null;
        _canvas = null;
        _layer = null;
        _bubble = null;
        _text = null;
        _layerGroup = null;
        _bubbleGroups = System.Array.Empty<CanvasGroup>();
        _failedPrefab = null;
        _wasOpen = _showing = false;
        _warnedMissingButton = _warnedMissingPrefab = false;
        _nextFind = _nextCreate = _openedAt = 0f;
        _layoutWidth = _layoutHeight = 0f;
        _currentText = "";
    }
}
