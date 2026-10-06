using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ReferToCompendium;

/// <summary>
/// Styles only the HUD rows supplied by the game. The native root, template,
/// status effect data, and special children such as WeightSE stay untouched.
/// </summary>
internal static class StatusEffectLayout
{
    private static readonly Dictionary<Hud, LayoutOwner> Owners = new();
    private static readonly List<Hud> DeadOwners = new();

    internal static void Apply(Hud hud, List<RectTransform> rows, bool enabled,
        Vector2 position, float scale, float spacing)
    {
        RemoveDestroyedOwners();
        if (hud == null)
        {
            return;
        }
        if (!enabled)
        {
            Release(hud);
            return;
        }
        RectTransform nativeRoot = hud.m_statusEffectListRoot;
        if (Owners.TryGetValue(hud, out LayoutOwner owner) && owner.NativeRoot != nativeRoot)
        {
            Release(hud);
            owner = null!;
        }
        if (owner == null)
        {
            owner = new LayoutOwner(nativeRoot);
            Owners.Add(hud, owner);
        }
        if (owner.Blocked)
        {
            return;
        }
        if (nativeRoot == null || !(nativeRoot.parent is RectTransform parent))
        {
            owner.Stop("The status effect root has no UI parent.");
            return;
        }
        if (!Finite(position.x) || !Finite(position.y)) position = new Vector2(-40f, -250f);
        if (!Finite(scale) || scale <= 0f) scale = 0.8f;
        if (!Finite(spacing) || spacing <= 0f) spacing = 40f;
        owner.Apply(rows, parent, position, scale, spacing);
    }

    internal static void Release(Hud hud)
    {
        // ReferenceEquals also permits cleanup from a Unity destroyed-object key.
        if (ReferenceEquals(hud, null) || !Owners.TryGetValue(hud, out LayoutOwner owner))
        {
            return;
        }
        owner.Restore();
        Owners.Remove(hud);
    }

    internal static void Shutdown()
    {
        foreach (LayoutOwner owner in Owners.Values)
        {
            owner.Restore();
        }
        Owners.Clear();
        DeadOwners.Clear();
    }

    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

    private static void RemoveDestroyedOwners()
    {
        DeadOwners.Clear();
        foreach (KeyValuePair<Hud, LayoutOwner> pair in Owners)
        {
            if (pair.Key == null) DeadOwners.Add(pair.Key!); // Unity destroyed-object key, not a CLR null.
        }
        foreach (Hud hud in DeadOwners) Release(hud);
        DeadOwners.Clear();
    }

    private sealed class LayoutOwner
    {
        internal readonly RectTransform NativeRoot;
        internal bool Blocked;
        private RectTransform? _container;
        private readonly Dictionary<RectTransform, RowSnapshot> _rows = new();
        private readonly HashSet<RectTransform> _currentRows = new();
        private readonly List<RectTransform> _removedRows = new();
        private readonly List<RowSnapshot> _restoreOrder = new();

        internal LayoutOwner(RectTransform nativeRoot) => NativeRoot = nativeRoot;

        internal void Apply(List<RectTransform> rows, RectTransform parent, Vector2 position, float scale, float spacing)
        {
            _currentRows.Clear();
            foreach (RectTransform row in rows)
            {
                if (row != null) _currentRows.Add(row);
            }
            _removedRows.Clear();
            foreach (KeyValuePair<RectTransform, RowSnapshot> pair in _rows)
            {
                if (pair.Key == null || !_currentRows.Contains(pair.Key)) _removedRows.Add(pair.Key!);
            }
            foreach (RectTransform row in _removedRows)
            {
                _rows[row].Restore(_container);
                _rows.Remove(row);
            }
            _removedRows.Clear();

            // Capture every new row before moving any of them, so their original
            // sibling indices refer to the same unchanged hierarchy.
            foreach (RectTransform row in rows)
            {
                if (row == null) continue;
                if (_rows.TryGetValue(row, out RowSnapshot existing))
                {
                    if (!existing.Valid || row.parent != _container)
                    {
                        Stop("A status effect row was replaced or moved by another UI owner.");
                        return;
                    }
                    continue;
                }
                if (row.parent != NativeRoot || !RowSnapshot.TryCapture(row, out RowSnapshot snapshot))
                {
                    Stop("A status effect row does not have the expected native UI structure.");
                    return;
                }
                _rows.Add(row, snapshot);
            }

            if (_container == null)
            {
                var gameObject = new GameObject("ReferToCompendium.StatusEffectColumn",
                    typeof(RectTransform), typeof(LayoutElement));
                gameObject.layer = NativeRoot.gameObject.layer;
                _container = (RectTransform)gameObject.transform;
                _container.SetParent(NativeRoot, false);
                _container.anchorMin = new Vector2(0.5f, 0.5f);
                _container.anchorMax = _container.anchorMin;
                _container.pivot = Vector2.one;
                _container.sizeDelta = Vector2.zero;
                gameObject.GetComponent<LayoutElement>().ignoreLayout = true;
            }

            // A child inherits the native root's visibility, CanvasGroups and
            // scale. Coordinate conversion provides a fixed upper-right anchor
            // without relocating that root or any unrelated HUD children.
            Vector3 parentPoint = new(parent.rect.xMax + position.x, parent.rect.yMax + position.y,
                NativeRoot.localPosition.z);
            _container.localPosition = NativeRoot.InverseTransformPoint(parent.TransformPoint(parentPoint));
            for (int index = 0; index < rows.Count; index++)
            {
                RectTransform row = rows[index];
                if (row == null) continue;
                _rows[row].Apply(_container, index, scale, spacing);
            }
        }

        internal void Stop(string reason)
        {
            Restore();
            Blocked = true;
            Plugin.LogWarning("Status effect column left to the original UI: " + reason +
                " Toggle Single column off and on after resolving the UI conflict to retry.");
        }

        internal void Restore()
        {
            _restoreOrder.Clear();
            _restoreOrder.AddRange(_rows.Values);
            _restoreOrder.Sort((left, right) => left.SiblingIndex.CompareTo(right.SiblingIndex));
            foreach (RowSnapshot row in _restoreOrder) row.Restore(_container);
            _restoreOrder.Clear();
            _rows.Clear();
            _currentRows.Clear();
            _removedRows.Clear();
            if (_container != null)
            {
                // Do not destroy somebody else's newly attached child. Native
                // HUD destruction will still own its eventual lifetime.
                if (_container.childCount == 0) UnityEngine.Object.Destroy(_container.gameObject);
                _container = null;
            }
        }
    }

    private sealed class RowSnapshot
    {
        private const float Width = 250f;
        private const float Height = 44f;
        private readonly RectTransform _row;
        private readonly Transform _parent;
        internal readonly int SiblingIndex;
        private readonly RectSnapshot _rowRect;
        private readonly RectSnapshot _iconRect;
        private readonly RectSnapshot _cooldownRect;
        private readonly RectSnapshot _nameRect;
        private readonly RectSnapshot _timeRect;
        private readonly TextSnapshot _nameStyle;
        private readonly TextSnapshot _timeStyle;

        private RowSnapshot(RectTransform row, RectTransform icon, RectTransform cooldown, TMP_Text name, TMP_Text time)
        {
            _row = row;
            _parent = row.parent;
            SiblingIndex = row.GetSiblingIndex();
            _rowRect = new RectSnapshot(row);
            _iconRect = new RectSnapshot(icon);
            _cooldownRect = new RectSnapshot(cooldown);
            _nameRect = new RectSnapshot(name.rectTransform);
            _timeRect = new RectSnapshot(time.rectTransform);
            _nameStyle = new TextSnapshot(name);
            _timeStyle = new TextSnapshot(time);
        }

        internal bool Valid => _row != null && _iconRect.Target != null && _cooldownRect.Target != null &&
            _nameStyle.Target != null && _timeStyle.Target != null;

        internal static bool TryCapture(RectTransform row, out RowSnapshot snapshot)
        {
            snapshot = null!;
            RectTransform? icon = row.Find("Icon") as RectTransform;
            RectTransform? cooldown = row.Find("Cooldown") as RectTransform;
            Transform nameTransform = row.Find("Name");
            Transform timeTransform = row.Find("TimeText");
            TMP_Text? name = nameTransform != null ? nameTransform.GetComponent<TMP_Text>() : null;
            TMP_Text? time = timeTransform != null ? timeTransform.GetComponent<TMP_Text>() : null;
            if (icon == null || cooldown == null || name == null || time == null) return false;
            snapshot = new RowSnapshot(row, icon, cooldown, name, time);
            return true;
        }

        internal void Apply(RectTransform container, int index, float scale, float spacing)
        {
            if (_row.parent != container) _row.SetParent(container, false);
            _row.SetSiblingIndex(index);
            _row.anchorMin = Vector2.one;
            _row.anchorMax = Vector2.one;
            _row.pivot = Vector2.one;
            _row.sizeDelta = new Vector2(Width, Height);
            _row.anchoredPosition3D = new Vector3(0f, -index * spacing, 0f);
            _row.localScale = Vector3.one * scale;
            _row.localRotation = Quaternion.identity;

            // The whole row extends left from its upper-right anchor; text must
            // not run off the right edge when only a small right margin is set.
            Place(_iconRect.Target, 0f, -4f, 36f, 36f);
            Place(_cooldownRect.Target, 0f, -4f, 36f, 36f);
            bool timed = _timeStyle.Target.gameObject.activeSelf;
            Place(_nameRect.Target, 46f, timed ? -1f : 0f, Width - 46f, timed ? 24f : Height);
            Place(_timeRect.Target, 46f, -25f, Width - 46f, 18f);
            _nameStyle.Apply(18f);
            _timeStyle.Apply(14f);
            // Text, sprites, flashing colors and TimeText/Cooldown active state
            // remain exactly as the native HUD wrote them this update.
        }

        internal void Restore(RectTransform? container)
        {
            if (_row == null || container == null || _row.parent != container || _parent == null) return;
            _row.SetParent(_parent, false);
            _row.SetSiblingIndex(SiblingIndex);
            _rowRect.Restore();
            _iconRect.Restore();
            _cooldownRect.Restore();
            _nameRect.Restore();
            _timeRect.Restore();
            _nameStyle.Restore();
            _timeStyle.Restore();
        }

        private static void Place(RectTransform rect, float x, float y, float width, float height)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = rect.anchorMin;
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition3D = new Vector3(x, y, 0f);
            rect.sizeDelta = new Vector2(width, height);
            rect.localScale = Vector3.one;
            rect.localRotation = Quaternion.identity;
        }
    }

    private sealed class RectSnapshot
    {
        internal readonly RectTransform Target;
        private readonly Vector2 _anchorMin;
        private readonly Vector2 _anchorMax;
        private readonly Vector2 _pivot;
        private readonly Vector2 _size;
        private readonly Vector3 _position;
        private readonly Vector3 _scale;
        private readonly Quaternion _rotation;

        internal RectSnapshot(RectTransform target)
        {
            Target = target;
            _anchorMin = target.anchorMin;
            _anchorMax = target.anchorMax;
            _pivot = target.pivot;
            _size = target.sizeDelta;
            _position = target.anchoredPosition3D;
            _scale = target.localScale;
            _rotation = target.localRotation;
        }

        internal void Restore()
        {
            if (Target == null) return;
            Target.anchorMin = _anchorMin;
            Target.anchorMax = _anchorMax;
            Target.pivot = _pivot;
            Target.sizeDelta = _size;
            Target.anchoredPosition3D = _position;
            Target.localScale = _scale;
            Target.localRotation = _rotation;
        }
    }

    private sealed class TextSnapshot
    {
        internal readonly TMP_Text Target;
        private readonly float _size;
        private readonly float _sizeMin;
        private readonly float _sizeMax;
        private readonly bool _autoSize;
        private readonly TextAlignmentOptions _alignment;
        private readonly TextWrappingModes _wrapping;
        private readonly TextOverflowModes _overflow;
        private readonly Vector4 _margin;

        internal TextSnapshot(TMP_Text target)
        {
            Target = target;
            _size = target.fontSize;
            _sizeMin = target.fontSizeMin;
            _sizeMax = target.fontSizeMax;
            _autoSize = target.enableAutoSizing;
            _alignment = target.alignment;
            _wrapping = target.textWrappingMode;
            _overflow = target.overflowMode;
            _margin = target.margin;
        }

        internal void Apply(float fontSize)
        {
            Target.enableAutoSizing = false;
            Target.fontSize = fontSize;
            Target.fontSizeMin = fontSize;
            Target.fontSizeMax = fontSize;
            Target.alignment = TextAlignmentOptions.MidlineLeft;
            Target.textWrappingMode = TextWrappingModes.NoWrap;
            Target.overflowMode = TextOverflowModes.Ellipsis;
            Target.margin = Vector4.zero;
        }

        internal void Restore()
        {
            if (Target == null) return;
            Target.enableAutoSizing = false;
            Target.fontSize = _size;
            Target.fontSizeMin = _sizeMin;
            Target.fontSizeMax = _sizeMax;
            Target.alignment = _alignment;
            Target.textWrappingMode = _wrapping;
            Target.overflowMode = _overflow;
            Target.margin = _margin;
            Target.enableAutoSizing = _autoSize;
        }
    }
}
