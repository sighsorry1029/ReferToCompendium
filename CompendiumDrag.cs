using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ReferToCompendium;

internal sealed partial class CompendiumController
{
    private Entry? _dragEntry;
    private readonly List<Entry> _dragGroup = new();
    private int _dragPointerId;
    private Vector2 _dragPosition;
    private Camera? _dragCamera;
    private RectTransform? _dragViewport;
    private RectTransform? _dragGhost;
    private RectTransform? _dropLine;
    private Color _dragLabelColor;
    private int _dragSource;
    private int _dragTarget;
    private bool _validDrop;

    internal static bool IsDragging(TextsDialog dialog)
    {
        CompendiumController controller = dialog.GetComponent<CompendiumController>();
        return controller != null && controller._dragEntry != null;
    }

    private bool BeginDrag(Entry entry, PointerEventData data)
    {
        if (_pending || _disposed || _dragEntry != null || _listScroll == null ||
            data.button != PointerEventData.InputButton.Left || !Application.isFocused)
        {
            return false;
        }
        ScrollRect scroll = _listScroll;
        _dragViewport = scroll.viewport != null ? scroll.viewport : (RectTransform)scroll.transform;
        bool collapsed = Plugin.State.IsCollapsed(entry.Id);
        _dragGroup.AddRange(_entries.Where(item => item.Info.m_listElement.activeInHierarchy &&
            Plugin.State.IsCollapsed(item.Id) == collapsed).OrderBy(item => _displayed.IndexOf(item.Info)));
        _dragSource = _dragGroup.IndexOf(entry);
        if (_dragSource < 0 || _dragGroup.Count < 2)
        {
            CancelDrag();
            return false;
        }
        _dragEntry = entry;
        _dragPointerId = data.pointerId;
        _dragPosition = data.position;
        // Newer input modules reuse PointerEventData between mouse buttons.
        // Keep the accepted gesture's camera/id rather than retaining that object.
        _dragCamera = data.pressEventCamera;
        _dragTarget = _dragSource;
        scroll.StopMovement();
        // A row's Button and its drag handler share the same GameObject. Unity
        // does not cancel that Button's click automatically when a drag starts.
        data.eligibleForClick = false;
        CreateDragVisuals(entry);
        if (entry.Label != null)
        {
            _dragLabelColor = entry.Label.color;
            Color faded = _dragLabelColor;
            faded.a *= 0.4f;
            entry.Label.color = faded;
        }
        UpdateDrag(false);
        return true;
    }

    private void UpdateDrag(bool autoScroll = true)
    {
        if (_dragEntry == null || _dragViewport == null)
        {
            return;
        }
        if (!Application.isFocused || _listScroll == null || _listScroll.content != _dialog.m_listRoot ||
            _dragGroup.Any(entry => entry.Info.m_listElement == null ||
            !entry.Info.m_listElement.activeInHierarchy))
        {
            CancelDrag();
            return;
        }
        _validDrop = false;
        bool inside = RectTransformUtility.ScreenPointToLocalPointInRectangle(
            _dragViewport, _dragPosition, _dragCamera, out Vector2 point) &&
            _dragViewport.rect.Contains(point);
        _dragGhost!.gameObject.SetActive(inside);
        _dropLine!.gameObject.SetActive(false);
        if (!inside)
        {
            return;
        }

        ScrollRect scroll = _listScroll;
        scroll.StopMovement();
        if (autoScroll)
        {
            Rect view = _dragViewport.rect;
            float edge = Mathf.Min(RowHeight * 0.8f, view.height * 0.25f);
            float speed = Mathf.Clamp01((point.y - (view.yMax - edge)) / edge) -
                Mathf.Clamp01(((view.yMin + edge) - point.y) / edge);
            // Convert the visible extent to content units, also when another
            // UI mod scales the list independently of its viewport.
            float visibleHeight = Mathf.Abs(_dialog.m_listRoot.InverseTransformPoint(
                _dragViewport.TransformPoint(new Vector3(0f, view.yMax))).y -
                _dialog.m_listRoot.InverseTransformPoint(
                _dragViewport.TransformPoint(new Vector3(0f, view.yMin))).y);
            float range = _dialog.m_listRoot.rect.height - visibleHeight;
            if (range > 0f && speed != 0f)
            {
                scroll.verticalNormalizedPosition = Mathf.Clamp01(scroll.verticalNormalizedPosition +
                    speed * RowHeight * 8f * Mathf.Min(Time.unscaledDeltaTime, 0.05f) / range);
            }
        }
        _dragGhost.anchoredPosition = new Vector2(6f, point.y - _dragViewport.rect.yMax);

        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
            _dialog.m_listRoot, _dragPosition, _dragCamera, out Vector2 contentPoint))
        {
            return;
        }
        // Keep normal entries and the expanded collapsed group separate. The
        // heading/toolbar cannot become drop targets or change collapse state.
        if (contentPoint.y > RowY(_dragGroup[0], 1f) ||
            contentPoint.y < RowY(_dragGroup[_dragGroup.Count - 1], 0f))
        {
            return;
        }
        int target = 0;
        Entry? next = null;
        Entry? previous = null;
        foreach (Entry candidate in _dragGroup)
        {
            if (candidate == _dragEntry) continue;
            if (contentPoint.y > RowY(candidate, 0.5f))
            {
                next = candidate;
                break;
            }
            target++;
            previous = candidate;
        }
        _dragTarget = target;
        float lineY = next != null ? RowY(next, 1f) : RowY(previous!, 0f);
        _dropLine.anchoredPosition = new Vector2(0f, lineY - _dialog.m_listRoot.rect.yMax);
        _dropLine.gameObject.SetActive(true);
        _validDrop = true;
    }

    private float RowY(Entry entry, float fraction)
    {
        RectTransform row = (RectTransform)entry.Info.m_listElement.transform;
        return _dialog.m_listRoot.InverseTransformPoint(row.TransformPoint(
            new Vector3(row.rect.center.x, Mathf.Lerp(row.rect.yMin, row.rect.yMax, fraction)))).y;
    }

    private void TrackDrag(Entry entry, PointerEventData data)
    {
        if (_dragEntry == entry && data.pointerId == _dragPointerId &&
            data.button == PointerEventData.InputButton.Left)
        {
            _dragPosition = data.position;
        }
    }

    private void FinishDrag(Entry entry, PointerEventData data)
    {
        if (_dragEntry != entry || data.pointerId != _dragPointerId ||
            data.button != PointerEventData.InputButton.Left)
        {
            return;
        }
        TrackDrag(entry, data);
        UpdateDrag(false);
        bool commit = _validDrop && Application.isFocused;
        int delta = _dragTarget - _dragSource;
        CancelDrag();
        if (commit && Plugin.State.Move(entry.Id, delta, _entries.Select(item => item.Id)))
        {
            Plugin.SaveLayout();
            ApplyLayout();
        }
    }

    private void CancelDrag()
    {
        if (_dragEntry?.Drag != null)
        {
            _dragEntry.Drag.ResetGesture();
        }
        if (_dragEntry?.Label != null)
        {
            _dragEntry.Label.color = _dragLabelColor;
        }
        _dragEntry = null;
        _dragCamera = null;
        _dragGroup.Clear();
        _validDrop = false;
        RemoveObject(_dragGhost != null ? _dragGhost.gameObject : null);
        RemoveObject(_dropLine != null ? _dropLine.gameObject : null);
        _dragGhost = null;
        _dropLine = null;
        _dragViewport = null;
    }

    private void OnApplicationFocus(bool focused)
    {
        if (!focused) CancelDrag();
    }

    private void CreateDragVisuals(Entry entry)
    {
        _dropLine = CreateDragImage("DropLine", _dialog.m_listRoot, new Color(1f, 0.72f, 0.18f));
        _dropLine.anchorMin = new Vector2(0f, 1f);
        _dropLine.anchorMax = Vector2.one;
        _dropLine.sizeDelta = new Vector2(-8f, 3f);

        _dragGhost = CreateDragImage("DraggedTitle", _dragViewport!, new Color(0.13f, 0.09f, 0.04f, 0.94f));
        _dragGhost.anchorMin = new Vector2(0f, 1f);
        _dragGhost.anchorMax = Vector2.one;
        _dragGhost.pivot = new Vector2(0f, 0.5f);
        _dragGhost.sizeDelta = new Vector2(-12f, RowHeight * 0.85f);
        var labelObject = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        labelObject.layer = _dragGhost.gameObject.layer;
        labelObject.transform.SetParent(_dragGhost, false);
        TMP_Text source = entry.Label ?? _dialog.m_textAreaTopic;
        TMP_Text label = labelObject.GetComponent<TMP_Text>();
        label.font = source.font;
        label.fontSharedMaterial = source.fontSharedMaterial;
        label.fontSize = source.fontSize;
        label.fontStyle = source.fontStyle;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.overflowMode = TextOverflowModes.Ellipsis;
        label.alignment = TextAlignmentOptions.MidlineLeft;
        label.color = new Color(1f, 0.78f, 0.4f);
        label.raycastTarget = false;
        label.text = entry.Label != null ? entry.Label.text : entry.Info.m_topic;
        SetStretch(label.rectTransform, Vector2.zero, Vector2.one);
        label.rectTransform.offsetMin = new Vector2(8f, 2f);
        label.rectTransform.offsetMax = new Vector2(-8f, -2f);
    }

    private static RectTransform CreateDragImage(string name, Transform parent, Color color)
    {
        var obj = new GameObject("ReferToCompendium." + name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        obj.layer = parent.gameObject.layer;
        obj.transform.SetParent(parent, false);
        Image image = obj.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return image.rectTransform;
    }
}

/// <summary>Titles always support dragging; short clicks keep the native page action.</summary>
internal sealed class CompendiumRowDrag : MonoBehaviour, IInitializePotentialDragHandler,
    IBeginDragHandler, IDragHandler, IEndDragHandler
{
    internal ScrollRect Scroll = null!;
    internal Transform Controls = null!;
    internal Func<PointerEventData, bool> Begin = null!;
    internal Action<PointerEventData> Track = null!;
    internal Action<PointerEventData> Finish = null!;
    internal Action Cancel = null!;
    private bool _dragging;
    private int _pointerId;

    internal void ResetGesture() => _dragging = false;

    public void OnInitializePotentialDrag(PointerEventData data)
    {
        GameObject hit = data.pointerPressRaycast.gameObject;
        if (hit != null && hit.transform.IsChildOf(Controls))
        {
            // Only our collapse/restore control delegates to native scrolling.
            // Title graphics and child labels all belong to the row's gesture.
            data.pointerDrag = Scroll.gameObject;
        }
        Scroll.OnInitializePotentialDrag(data);
    }

    public void OnBeginDrag(PointerEventData data)
    {
        if (_dragging) return;
        _dragging = Begin(data);
        if (_dragging)
        {
            _pointerId = data.pointerId;
            data.eligibleForClick = false;
        }
    }

    public void OnDrag(PointerEventData data)
    {
        if (_dragging && data.pointerId == _pointerId && data.button == PointerEventData.InputButton.Left)
        {
            data.eligibleForClick = false;
            Track(data);
        }
    }

    public void OnEndDrag(PointerEventData data)
    {
        if (!_dragging || data.pointerId != _pointerId || data.button != PointerEventData.InputButton.Left) return;
        _dragging = false;
        Finish(data);
    }

    private void OnDisable()
    {
        if (!_dragging) return;
        _dragging = false;
        Cancel();
    }
}

// A preceding click can still have a delayed vanilla SnapTo coroutine pending.
// Let it finish without moving the list out from under the current drag.
[HarmonyPatch(typeof(TextsDialog), "SnapTo")]
internal static class CompendiumDragScrollPatch
{
    [HarmonyPrefix]
    private static bool Prefix(TextsDialog __instance) => !CompendiumController.IsDragging(__instance);
}
