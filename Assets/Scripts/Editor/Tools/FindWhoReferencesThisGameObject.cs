// find what scripts reference selected GameObject in the scene (in events, public fields..)

using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Events;

namespace UnityLibrary.Editor
{
    public class FindWhoReferencesThisGameObject : EditorWindow
    {
        private GameObject target;
        private Vector2 scroll;

        private class ReferenceResult
        {
            public string message;
            public GameObject owner;
        }

        private List<ReferenceResult> results = new List<ReferenceResult>();

        [MenuItem("Tools/UnityLibrary/Find References To GameObject")]
        public static void ShowWindow()
        {
            var win = GetWindow<FindWhoReferencesThisGameObject>("Find References");
            win.minSize = new Vector2(500, 300);
        }

        private void OnGUI()
        {
            GUILayout.Label("Find scripts that reference this GameObject", EditorStyles.boldLabel);
            target = EditorGUILayout.ObjectField("Target GameObject", target, typeof(GameObject), true) as GameObject;

            if (GUILayout.Button("Find References"))
            {
                results.Clear();
                if (target != null)
                {
                    FindReferences(target);
                }
                else
                {
                    Debug.LogWarning("Please assign a GameObject.");
                }
            }

            if (results.Count > 0)
            {
                GUILayout.Label("Results:", EditorStyles.boldLabel);
                scroll = GUILayout.BeginScrollView(scroll, GUILayout.Height(400));
                foreach (var res in results)
                {
                    if (GUILayout.Button(res.message, GUILayout.ExpandWidth(true)))
                    {
                        EditorGUIUtility.PingObject(res.owner);
                        Selection.activeGameObject = res.owner;
                    }
                }
                GUILayout.EndScrollView();
            }
        }

        private void FindReferences(GameObject target)
        {
            var allObjects = Object.FindObjectsByType<MonoBehaviour>(findObjectsInactive: FindObjectsInactive.Include, sortMode: FindObjectsSortMode.None);

            foreach (var mono in allObjects)
            {
                if (mono == null || mono.gameObject == target) continue;

                var type = mono.GetType();
                var fields = type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                foreach (var field in fields)
                {
                    if (typeof(UnityEventBase).IsAssignableFrom(field.FieldType))
                    {
                        var unityEvent = field.GetValue(mono) as UnityEventBase;
                        if (unityEvent != null)
                        {
                            int count = unityEvent.GetPersistentEventCount();
                            for (int i = 0; i < count; i++)
                            {
                                var listener = unityEvent.GetPersistentTarget(i);
                                if (listener == target)
                                {
                                    results.Add(new ReferenceResult
                                    {
                                        message = $"{mono.name} ({type.Name}) -> UnityEvent '{field.Name}'",
                                        owner = mono.gameObject
                                    });
                                }
                            }
                        }

                        continue;
                    }

                    if (!typeof(UnityEngine.Object).IsAssignableFrom(field.FieldType))
                        continue;

                    var value = field.GetValue(mono) as UnityEngine.Object;
                    if (ReferencesTarget(value, target))
                    {
                        results.Add(new ReferenceResult
                        {
                            message = $"{mono.name} ({type.Name}) -> Field '{field.Name}'",
                            owner = mono.gameObject
                        });
                    }
                }

                // Also scan serialized properties (handles public fields, [SerializeField] private, arrays/lists, etc.)
                FindSerializedReferences(mono, target);
            }

            if (results.Count == 0)
            {
                results.Add(new ReferenceResult
                {
                    message = "No references found.",
                    owner = null
                });
            }
        }

        private void FindSerializedReferences(MonoBehaviour mono, GameObject target)
        {
            var so = new SerializedObject(mono);
            var it = so.GetIterator();

            // enterChildren=true on first call to include all fields
            bool enterChildren = true;
            while (it.NextVisible(enterChildren))
            {
                enterChildren = false;

                if (it.propertyType != SerializedPropertyType.ObjectReference)
                    continue;

                var obj = it.objectReferenceValue;
                if (!ReferencesTarget(obj, target))
                    continue;

                results.Add(new ReferenceResult
                {
                    message = $"{mono.name} ({mono.GetType().Name}) -> Serialized '{it.propertyPath}'",
                    owner = mono.gameObject
                });
            }
        }

        private static bool ReferencesTarget(UnityEngine.Object value, GameObject target)
        {
            if (value == null || target == null) return false;

            if (value == target) return true;

            // most common case: field is Transform/Component referencing the target GO
            if (value is Component c && c.gameObject == target) return true;

            return false;
        }
    }
}
