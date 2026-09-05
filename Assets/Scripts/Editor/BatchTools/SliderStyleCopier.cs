// Put this file in any folder named "Editor".
// Window: Tools > UI > Slider Style Copier
// Also available as right-click on a Slider component: Copy / Paste Slider Style

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace UnityLibrary.UI.Tools
{
    public class SliderStyleCopier : EditorWindow
    {
        [SerializeField] Slider source;
        [SerializeField] List<Slider> targets = new List<Slider>();

        [SerializeField] bool copySlider = true;      // colors, transition, target graphic, fill/handle refs
        [SerializeField] bool copyRects = true;       // RectTransforms of children (root excluded)
        [SerializeField] bool copyGraphics = true;    // Image / RawImage / Text: sprite, color, type, settings
        [SerializeField] bool copyValues = false;     // value / min / max / wholeNumbers

        // Never copied: script ref and event wiring (target keeps its own listeners).
        static readonly HashSet<string> AlwaysSkip = new HashSet<string> { "m_Script", "m_OnValueChanged" };
        static readonly HashSet<string> ValueProps = new HashSet<string> { "m_Value", "m_MinValue", "m_MaxValue", "m_WholeNumbers" };

        static Slider clipboard;
        SerializedObject so;
        Vector2 scroll;

        [MenuItem("Tools/UI/Slider Style Copier")]
        static void Open() => GetWindow<SliderStyleCopier>("Slider Copier").Show();

        void OnEnable() => so = new SerializedObject(this);

        void OnGUI()
        {
            if (so == null) so = new SerializedObject(this);
            so.Update();

            EditorGUILayout.PropertyField(so.FindProperty("source"), new GUIContent("Source Slider"));

            EditorGUILayout.Space();
            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUILayout.PropertyField(so.FindProperty("targets"), new GUIContent("Targets"), true);
            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space();
            EditorGUILayout.PropertyField(so.FindProperty("copySlider"), new GUIContent("Slider component"));
            EditorGUILayout.PropertyField(so.FindProperty("copyRects"), new GUIContent("Child RectTransforms"));
            EditorGUILayout.PropertyField(so.FindProperty("copyGraphics"), new GUIContent("Images / Graphics"));
            EditorGUILayout.PropertyField(so.FindProperty("copyValues"), new GUIContent("Slider values (min/max/value)"));

            so.ApplyModifiedProperties();

            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Add Selection To Targets")) AddSelectionToTargets();
                if (GUILayout.Button("Clear Targets")) { targets.Clear(); so.Update(); }
            }

            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(source == null))
            {
                if (GUILayout.Button("Apply To Target List", GUILayout.Height(24)))
                    ApplyToMany(source, targets, Options());

                if (GUILayout.Button("Apply To Current Selection", GUILayout.Height(24)))
                    ApplyToMany(source, SelectedSliders(), Options());
            }

            if (source == null)
                EditorGUILayout.HelpBox("Assign a source slider. Target hierarchies must match the source hierarchy.", MessageType.Info);
        }

        Opts Options() => new Opts { slider = copySlider, rects = copyRects, graphics = copyGraphics, values = copyValues };

        void AddSelectionToTargets()
        {
            foreach (var s in SelectedSliders())
                if (s != source && !targets.Contains(s)) targets.Add(s);
            so.Update();
            Repaint();
        }

        static List<Slider> SelectedSliders()
        {
            var list = new List<Slider>();
            foreach (var go in Selection.gameObjects)
            {
                var s = go.GetComponent<Slider>();
                if (s != null && !list.Contains(s)) list.Add(s);
            }
            return list;
        }

        // ---------------------------------------------------------------- context menu

        [MenuItem("CONTEXT/Slider/Copy Slider Style")]
        static void ContextCopy(MenuCommand cmd) => clipboard = (Slider)cmd.context;

        [MenuItem("CONTEXT/Slider/Paste Slider Style", true)]
        static bool ContextPasteValidate() => clipboard != null;

        [MenuItem("CONTEXT/Slider/Paste Slider Style")]
        static void ContextPaste(MenuCommand cmd)
        {
            var list = SelectedSliders();
            var clicked = (Slider)cmd.context;
            if (!list.Contains(clicked)) list.Add(clicked);
            ApplyToMany(clipboard, list, new Opts { slider = true, rects = true, graphics = true, values = false });
        }

        // ---------------------------------------------------------------- copying

        struct Opts { public bool slider, rects, graphics, values; }

        static void ApplyToMany(Slider src, List<Slider> list, Opts opts)
        {
            if (src == null || list == null) return;

            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Copy Slider Style");

            int done = 0;
            foreach (var dst in list)
            {
                if (dst == null || dst == src) continue;
                Undo.RegisterFullObjectHierarchyUndo(dst.gameObject, "Copy Slider Style");
                CopyNode(src.transform, dst.transform, src.transform, dst.transform, opts);
                done++;
            }

            Undo.CollapseUndoOperations(group);
            Debug.Log($"Slider Style Copier: copied '{src.name}' to {done} slider(s).", src);
        }

        static void CopyNode(Transform s, Transform d, Transform sRoot, Transform dRoot, Opts opts)
        {
            if (opts.rects && s != sRoot && s is RectTransform sr && d is RectTransform dr)
                CopyRect(sr, dr);

            if (opts.graphics)
                CopySameTypeComponents<Graphic>(s, d, sRoot, dRoot, null);

            if (opts.slider)
                CopySameTypeComponents<Slider>(s, d, sRoot, dRoot, opts.values ? null : ValueProps);

            if (s.childCount != d.childCount)
                Debug.LogWarning($"Slider Style Copier: child count mismatch at '{d.name}' ({s.childCount} vs {d.childCount}). Extra children are ignored.", d);

            int n = Mathf.Min(s.childCount, d.childCount);
            for (int i = 0; i < n; i++)
                CopyNode(s.GetChild(i), d.GetChild(i), sRoot, dRoot, opts);
        }

        static void CopyRect(RectTransform s, RectTransform d)
        {
            Undo.RecordObject(d, "Copy Slider Style");
            d.anchorMin = s.anchorMin;
            d.anchorMax = s.anchorMax;
            d.pivot = s.pivot;
            d.anchoredPosition3D = s.anchoredPosition3D;
            d.sizeDelta = s.sizeDelta;
            d.localRotation = s.localRotation;
            d.localScale = s.localScale;
            PrefabUtility.RecordPrefabInstancePropertyModifications(d);
        }

        static void CopySameTypeComponents<T>(Transform s, Transform d, Transform sRoot, Transform dRoot, HashSet<string> extraSkip) where T : Component
        {
            var src = s.GetComponents<T>();
            var dst = d.GetComponents<T>();
            for (int i = 0; i < src.Length && i < dst.Length; i++)
            {
                if (src[i].GetType() != dst[i].GetType()) continue;
                CopyComponent(src[i], dst[i], sRoot, dRoot, extraSkip);
            }
        }

        // Serialized copy: covers every inspector field (colors, transition, sprite state,
        // image type/fill settings, ...) without listing them one by one.
        // Object references pointing inside the source hierarchy are remapped to the
        // matching object in the target hierarchy; asset references are copied as-is.
        static void CopyComponent(Component src, Component dst, Transform sRoot, Transform dRoot, HashSet<string> extraSkip)
        {
            var from = new SerializedObject(src);
            var to = new SerializedObject(dst);
            var it = from.GetIterator();

            bool enterChildren = true;
            while (it.NextVisible(enterChildren))
            {
                enterChildren = true;

                if (AlwaysSkip.Contains(it.propertyPath) || (extraSkip != null && extraSkip.Contains(it.propertyPath)))
                {
                    enterChildren = false; // do not descend into skipped branches
                    continue;
                }

                var tp = to.FindProperty(it.propertyPath);
                if (tp == null) continue;

                if (it.propertyType == SerializedPropertyType.ObjectReference)
                    tp.objectReferenceValue = Remap(it.objectReferenceValue, sRoot, dRoot);
                else if (!it.hasVisibleChildren)
                    to.CopyFromSerializedProperty(it);
            }

            to.ApplyModifiedProperties(); // registers undo + prefab modifications
        }

        static Object Remap(Object value, Transform sRoot, Transform dRoot)
        {
            if (value == null) return null;

            Transform t = value as Transform;
            if (t == null && value is Component c) t = c.transform;
            if (t == null && value is GameObject go) t = go.transform;
            if (t == null) return value;                       // asset (sprite, material, ...)
            if (!IsUnder(t, sRoot)) return value;              // outside the slider hierarchy, keep as is

            var mapped = FindMatching(t, sRoot, dRoot);
            if (mapped == null) return value;

            if (value is GameObject) return mapped.gameObject;
            if (value is Transform) return mapped;
            return mapped.GetComponent(value.GetType());
        }

        static bool IsUnder(Transform t, Transform root)
        {
            for (var p = t; p != null; p = p.parent)
                if (p == root) return true;
            return false;
        }

        // Same sibling-index path under the target root.
        static Transform FindMatching(Transform node, Transform sRoot, Transform dRoot)
        {
            var path = new List<int>();
            for (var t = node; t != sRoot; t = t.parent)
            {
                if (t == null) return null;
                path.Add(t.GetSiblingIndex());
            }
            path.Reverse();

            var d = dRoot;
            for (int i = 0; i < path.Count; i++)
            {
                if (path[i] >= d.childCount) return null;
                d = d.GetChild(path[i]);
            }
            return d;
        }
    }
}
