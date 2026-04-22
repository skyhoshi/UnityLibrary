/// <summary>
/// PivotAligner — Unity Editor Window
/// Align two 3D models by picking a custom pivot point (vertex or face center)
/// on the source model, then rotating/translating it around that point.
///
/// Place this file inside any  Editor/  folder in your project.
/// Open via:  Tools/UnityLibrary/Pivot Aligner
/// </summary>

using UnityEngine;
using UnityEditor;

namespace UnityLibrary.Tools
{
    public class PivotAligner : EditorWindow
    {
        // ──────────────────────────────────────────────────────────────────────────
        // Enums & constants
        // ──────────────────────────────────────────────────────────────────────────

        enum PickMode { Vertex, Face }
        enum ToolState { Idle, Picking, Rotating }

        const string MENU_PATH = "Tools/UnityLibrary/Pivot Aligner";
        const float GIZMO_RADIUS = 0.06f;
        const float GIZMO_CROSS = 0.25f;

        // ──────────────────────────────────────────────────────────────────────────
        // Inspector / serialised fields
        // ──────────────────────────────────────────────────────────────────────────

        [SerializeField] GameObject sourceObject;
        [SerializeField] PickMode pickMode = PickMode.Vertex;

        // ── Rotation ──────────────────────────────────────────────────────────────
        // Coarse float fields  (full range, typed or dragged)
        float rotX, rotY, rotZ;

        // Fine-tune additive deltas  (±fineTuneRange degrees, applied on top of coarse)
        bool showFinetune = false;
        float fineTuneRange = 5f;
        float fineX, fineY, fineZ;

        // ── Position offset ───────────────────────────────────────────────────────
        // Shifts the model in world space AND moves the pivot so subsequent
        // rotations keep the same relative geometry.
        bool showPosOffset = false;
        float posOffsetX, posOffsetY, posOffsetZ;
        float finePosRange = 0.1f;

        // ──────────────────────────────────────────────────────────────────────────
        // Runtime state
        // ──────────────────────────────────────────────────────────────────────────

        ToolState state = ToolState.Idle;
        Vector3 pivotWorld = Vector3.zero;
        bool hasPivot = false;

        // Snapshot taken when pivot is confirmed – rotation is always rebuilt from
        // this base so there is no floating-point drift on repeated slider edits.
        Vector3 basePosition;
        Quaternion baseRotation;

        // Highlight during picking
        Vector3 highlightPoint = Vector3.zero;
        Vector3 highlightNormal = Vector3.up;
        bool hasHighlight = false;

        // Scroll view
        Vector2 scroll;

        // Style cache
        GUIStyle headerStyle, sectionStyle, stateStyle, subLabelStyle;
        bool stylesInit;

        // ──────────────────────────────────────────────────────────────────────────
        // Window lifecycle
        // ──────────────────────────────────────────────────────────────────────────

        [MenuItem(MENU_PATH)]
        public static void ShowWindow()
        {
            var win = GetWindow<PivotAligner>("Pivot Aligner");
            win.minSize = new Vector2(440, 520);
        }

        void OnEnable()
        {
            SceneView.duringSceneGui += OnSceneGUI;
            titleContent = new GUIContent("Pivot Aligner",
                EditorGUIUtility.IconContent("d_ToolHandleLocal").image);
        }

        void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
            CancelPicking();
        }

        // ──────────────────────────────────────────────────────────────────────────
        // Editor Window GUI
        // ──────────────────────────────────────────────────────────────────────────

        void OnGUI()
        {
            InitStyles();
            scroll = EditorGUILayout.BeginScrollView(scroll);

            // ── Header ────────────────────────────────────────────────────────────
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("PIVOT ALIGNER", headerStyle);
            EditorGUILayout.Space(2);
            DrawHR();

            // ── 1 · Source Model ─────────────────────────────────────────────────
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("1 · Source Model", sectionStyle);
            EditorGUI.BeginChangeCheck();
            sourceObject = (GameObject)EditorGUILayout.ObjectField(
                "Game Object", sourceObject, typeof(GameObject), true);
            if (EditorGUI.EndChangeCheck()) ResetTool();

            if (sourceObject == null)
            {
                EditorGUILayout.HelpBox("Assign a GameObject to begin.", MessageType.Info);
                EditorGUILayout.EndScrollView();
                return;
            }

            // ── 2 · Pick Mode ─────────────────────────────────────────────────────
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("2 · Pick Mode", sectionStyle);
            EditorGUILayout.BeginHorizontal();
            if (DrawModeButton("  Vertex", pickMode == PickMode.Vertex))
            { pickMode = PickMode.Vertex; hasHighlight = false; }
            if (DrawModeButton("  Face", pickMode == PickMode.Face))
            { pickMode = PickMode.Face; hasHighlight = false; }
            EditorGUILayout.EndHorizontal();

            // ── 3 · Pivot Point ───────────────────────────────────────────────────
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("3 · Select Pivot Point", sectionStyle);

            using (new EditorGUI.DisabledScope(state == ToolState.Rotating))
            {
                if (state != ToolState.Picking)
                {
                    if (GUILayout.Button("⊕  Select Target Point in Scene", GUILayout.Height(30)))
                        BeginPicking();
                }
                else
                {
                    Color prev = GUI.backgroundColor;
                    GUI.backgroundColor = new Color(1f, 0.55f, 0.15f);
                    if (GUILayout.Button("✕  Cancel Picking", GUILayout.Height(30)))
                        CancelPicking();
                    GUI.backgroundColor = prev;
                    EditorGUILayout.HelpBox(
                        $"Hover model → {(pickMode == PickMode.Vertex ? "vertex" : "face center")} highlights. Click to confirm pivot.",
                        MessageType.None);
                }
            }

            if (hasPivot)
            {
                EditorGUILayout.Space(2);
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField($"Pivot  {pivotWorld:F4}", subLabelStyle);
                    if (GUILayout.Button("Re-pick", GUILayout.Width(56), GUILayout.Height(18)))
                        BeginPicking();
                }
            }

            // ── 4 · Rotation ──────────────────────────────────────────────────────
            EditorGUILayout.Space(8);
            DrawHR();
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("4 · Rotation Around Pivot", sectionStyle);

            using (new EditorGUI.DisabledScope(!hasPivot))
            {
                // Coarse float input rows
                bool dirty = false;
                EditorGUI.BeginChangeCheck();
                DrawRotRow("Pitch  X", ref rotX);
                DrawRotRow("Yaw    Y", ref rotY);
                DrawRotRow("Roll   Z", ref rotZ);
                if (EditorGUI.EndChangeCheck()) dirty = true;

                // Fine-tune
                EditorGUILayout.Space(4);
                using (new EditorGUILayout.HorizontalScope())
                {
                    showFinetune = EditorGUILayout.ToggleLeft("Fine-tune  ±", showFinetune, GUILayout.Width(102));
                    using (new EditorGUI.DisabledScope(!showFinetune))
                        fineTuneRange = Mathf.Max(0.001f, EditorGUILayout.FloatField(fineTuneRange, GUILayout.Width(52)));
                    EditorGUILayout.LabelField("°", subLabelStyle, GUILayout.Width(14));
                }

                if (showFinetune)
                {
                    EditorGUI.BeginChangeCheck();
                    fineX = DrawFineRotSlider("  Δ Pitch X", fineX, fineTuneRange);
                    fineY = DrawFineRotSlider("  Δ Yaw   Y", fineY, fineTuneRange);
                    fineZ = DrawFineRotSlider("  Δ Roll  Z", fineZ, fineTuneRange);
                    if (EditorGUI.EndChangeCheck()) dirty = true;
                }

                if (dirty && hasPivot) ApplyAll("Pivot Aligner — Rotate");

                EditorGUILayout.Space(4);
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Reset All Rotation", GUILayout.Height(24)))
                        ResetRotation();
                    if (showFinetune && GUILayout.Button("Reset Fine", GUILayout.Width(80), GUILayout.Height(24)))
                    { fineX = fineY = fineZ = 0f; if (hasPivot) ApplyAll("Pivot Aligner — Rotate"); }
                }
            }

            // ── 5 · Position Offset ───────────────────────────────────────────────
            EditorGUILayout.Space(8);
            DrawHR();
            EditorGUILayout.Space(4);

            using (new EditorGUILayout.HorizontalScope())
            {
                showPosOffset = EditorGUILayout.Foldout(showPosOffset, "5 · Position Offset", true, sectionStyle);
                EditorGUILayout.LabelField("(moves pivot too)", subLabelStyle);
            }

            if (showPosOffset)
            {
                using (new EditorGUI.DisabledScope(!hasPivot))
                {
                    // Slider range control
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField("Slider range  ±", subLabelStyle, GUILayout.Width(102));
                        finePosRange = Mathf.Max(0.0001f, EditorGUILayout.FloatField(finePosRange, GUILayout.Width(52)));
                        EditorGUILayout.LabelField("m", subLabelStyle, GUILayout.Width(14));
                    }

                    EditorGUILayout.Space(2);

                    EditorGUI.BeginChangeCheck();
                    posOffsetX = DrawOffsetSliderRow("Offset  X", posOffsetX, finePosRange);
                    posOffsetY = DrawOffsetSliderRow("Offset  Y", posOffsetY, finePosRange);
                    posOffsetZ = DrawOffsetSliderRow("Offset  Z", posOffsetZ, finePosRange);
                    if (EditorGUI.EndChangeCheck() && hasPivot)
                        ApplyAll("Pivot Aligner — Move");

                    EditorGUILayout.Space(3);
                    if (GUILayout.Button("Reset Position Offset", GUILayout.Height(24)))
                        ResetPositionOffset();
                }
            }

            // ── Apply / Revert ────────────────────────────────────────────────────
            EditorGUILayout.Space(8);
            DrawHR();
            EditorGUILayout.Space(4);

            using (new EditorGUI.DisabledScope(!hasPivot))
            {
                Color prev = GUI.backgroundColor;
                GUI.backgroundColor = new Color(0.22f, 0.80f, 0.40f);
                if (GUILayout.Button("✔  Apply & Clear Pivot", GUILayout.Height(36)))
                    Apply();
                GUI.backgroundColor = prev;
            }

            if (hasPivot)
            {
                if (GUILayout.Button("↺  Cancel & Revert to Original", GUILayout.Height(26)))
                    RevertAndReset();
            }

            // ── Status bar ────────────────────────────────────────────────────────
            EditorGUILayout.Space(4);
            string totalRot = hasPivot
                ? $"rot({rotX + fineX:F3}, {rotY + fineY:F3}, {rotZ + fineZ:F3})°  " +
                  $"pos offset({posOffsetX:F4}, {posOffsetY:F4}, {posOffsetZ:F4}) m"
                : "";
            string stateLabel = state switch
            {
                ToolState.Picking => "● PICKING",
                ToolState.Rotating => $"● ROTATING   {totalRot}",
                _ => "○ idle"
            };
            EditorGUILayout.LabelField(stateLabel, stateStyle);
            EditorGUILayout.Space(4);
            EditorGUILayout.EndScrollView();

            SceneView.RepaintAll();
        }

        // ──────────────────────────────────────────────────────────────────────────
        // Row helpers
        // ──────────────────────────────────────────────────────────────────────────

        /// Radio-style button: highlighted when active, always clickable, returns true on click.
        bool DrawModeButton(string label, bool active)
        {
            Color prev = GUI.backgroundColor;
            if (active) GUI.backgroundColor = new Color(0.3f, 0.65f, 1f);
            bool clicked = GUILayout.Button(label, GUILayout.Height(26));
            GUI.backgroundColor = prev;
            return clicked;
        }

        /// Coarse rotation row: label | float field | quick-snap buttons
        void DrawRotRow(string label, ref float value)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(label, GUILayout.Width(72));
                value = EditorGUILayout.FloatField(value, GUILayout.Width(74));
                if (GUILayout.Button("-90", GUILayout.Width(36), GUILayout.Height(18))) value -= 90f;
                if (GUILayout.Button("-45", GUILayout.Width(36), GUILayout.Height(18))) value -= 45f;
                if (GUILayout.Button("0", GUILayout.Width(28), GUILayout.Height(18))) value = 0f;
                if (GUILayout.Button("+45", GUILayout.Width(36), GUILayout.Height(18))) value += 45f;
                if (GUILayout.Button("+90", GUILayout.Width(36), GUILayout.Height(18))) value += 90f;
            }
        }

        /// Fine rotation slider: returns new value.
        float DrawFineRotSlider(string label, float value, float range)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(label, subLabelStyle, GUILayout.Width(76));
                value = GUILayout.HorizontalSlider(value, -range, range);
                value = EditorGUILayout.FloatField(value, GUILayout.Width(64));
                EditorGUILayout.LabelField("°", subLabelStyle, GUILayout.Width(14));
            }
            return value;
        }

        /// Position offset row: slider + float field + zero button. Direct value, no delta accumulation.
        float DrawOffsetSliderRow(string label, float value, float range)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(label, GUILayout.Width(72));
                value = GUILayout.HorizontalSlider(value, -range, range);
                value = EditorGUILayout.FloatField(value, GUILayout.Width(74));
                EditorGUILayout.LabelField("m", subLabelStyle, GUILayout.Width(14));
                if (GUILayout.Button("0", GUILayout.Width(24), GUILayout.Height(18))) value = 0f;
            }
            return value;
        }

        // ──────────────────────────────────────────────────────────────────────────
        // Scene GUI
        // ──────────────────────────────────────────────────────────────────────────

        void OnSceneGUI(SceneView sv)
        {
            if (sourceObject == null) return;
            if (hasPivot) DrawPivotGizmo(pivotWorld, Color.cyan);
            if (state == ToolState.Picking) HandlePicking(sv);
        }

        void HandlePicking(SceneView sv)
        {
            Event e = Event.current;
            if (e.type == EventType.Layout)
                HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));

            if (e.type == EventType.MouseMove || e.type == EventType.MouseDown)
            {
                Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
                TryRaycast(ray, out hasHighlight, out highlightPoint, out highlightNormal);
                if (hasHighlight)
                    DrawPivotGizmo(highlightPoint, new Color(1f, 0.8f, 0.1f, 0.9f));
                if (e.type == EventType.MouseDown && e.button == 0 && hasHighlight)
                { ConfirmPivot(highlightPoint); e.Use(); }
                sv.Repaint();
            }
            if (hasHighlight)
                DrawPivotGizmo(highlightPoint, new Color(1f, 0.8f, 0.1f, 0.9f));
        }

        // ──────────────────────────────────────────────────────────────────────────
        // Raycasting
        // ──────────────────────────────────────────────────────────────────────────

        void TryRaycast(Ray ray, out bool hit, out Vector3 point, out Vector3 normal)
        {
            hit = false; point = Vector3.zero; normal = Vector3.up;
            var filters = sourceObject.GetComponentsInChildren<MeshFilter>();
            float bestDist = float.MaxValue;

            foreach (var mf in filters)
            {
                if (mf.sharedMesh == null) continue;
                Mesh mesh = mf.sharedMesh;
                Transform t = mf.transform;
                Ray localRay = new Ray(
                    t.InverseTransformPoint(ray.origin),
                    t.InverseTransformDirection(ray.direction).normalized);

                Vector3[] verts = mesh.vertices;
                int[] tris = mesh.triangles;
                Vector3[] normals = mesh.normals;

                for (int i = 0; i < tris.Length; i += 3)
                {
                    Vector3 v0 = verts[tris[i]], v1 = verts[tris[i + 1]], v2 = verts[tris[i + 2]];
                    if (!RayTriangle(localRay, v0, v1, v2, out float dist, out float u, out float v)) continue;
                    if (dist < 0 || dist >= bestDist) continue;
                    bestDist = dist; hit = true;

                    if (pickMode == PickMode.Vertex)
                    {
                        float w = 1f - u - v;
                        int vi = FindNearestVertex(u, v, w);
                        point = t.TransformPoint(vi == 0 ? v0 : vi == 1 ? v1 : v2);
                    }
                    else point = t.TransformPoint((v0 + v1 + v2) / 3f);

                    Vector3 n0 = normals.Length > tris[i] ? normals[tris[i]] : Vector3.up;
                    Vector3 n1 = normals.Length > tris[i + 1] ? normals[tris[i + 1]] : Vector3.up;
                    Vector3 n2 = normals.Length > tris[i + 2] ? normals[tris[i + 2]] : Vector3.up;
                    normal = t.TransformDirection((n0 * (1 - u - v) + n1 * u + n2 * v).normalized);
                }
            }
        }

        static bool RayTriangle(Ray ray, Vector3 v0, Vector3 v1, Vector3 v2,
                                 out float dist, out float u, out float v)
        {
            dist = u = v = 0;
            Vector3 e1 = v1 - v0, e2 = v2 - v0, h = Vector3.Cross(ray.direction, e2);
            float det = Vector3.Dot(e1, h);
            if (Mathf.Abs(det) < 1e-6f) return false;
            float f = 1f / det;
            Vector3 s = ray.origin - v0;
            u = f * Vector3.Dot(s, h);
            if (u < 0 || u > 1) return false;
            Vector3 q = Vector3.Cross(s, e1);
            v = f * Vector3.Dot(ray.direction, q);
            if (v < 0 || u + v > 1) return false;
            dist = f * Vector3.Dot(e2, q);
            return dist > 1e-5f;
        }

        static int FindNearestVertex(float u, float v, float w)
        {
            if (w >= u && w >= v) return 0;
            if (u >= w && u >= v) return 1;
            return 2;
        }

        // ──────────────────────────────────────────────────────────────────────────
        // Gizmo drawing
        // ──────────────────────────────────────────────────────────────────────────

        void DrawPivotGizmo(Vector3 pos, Color color)
        {
            Handles.color = color;
            Handles.SphereHandleCap(0, pos, Quaternion.identity,
                GIZMO_RADIUS * HandleUtility.GetHandleSize(pos), EventType.Repaint);
            float sz = GIZMO_CROSS * HandleUtility.GetHandleSize(pos);
            Handles.DrawLine(pos - Vector3.right * sz, pos + Vector3.right * sz);
            Handles.DrawLine(pos - Vector3.up * sz, pos + Vector3.up * sz);
            Handles.DrawLine(pos - Vector3.forward * sz, pos + Vector3.forward * sz);
            GUIStyle s = new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = color } };
            Handles.Label(pos + Vector3.up * sz * 1.5f,
                (hasPivot && pos == pivotWorld) ? "PIVOT" : "○", s);
        }

        // ──────────────────────────────────────────────────────────────────────────
        // State transitions
        // ──────────────────────────────────────────────────────────────────────────

        void BeginPicking()
        {
            if (sourceObject == null) return;
            state = ToolState.Picking; hasHighlight = false;
            SceneView.RepaintAll();
        }

        void CancelPicking()
        {
            if (state == ToolState.Picking)
                state = hasPivot ? ToolState.Rotating : ToolState.Idle;
            hasHighlight = false; SceneView.RepaintAll();
        }

        void ConfirmPivot(Vector3 worldPoint)
        {
            if (hasPivot && state == ToolState.Rotating) RevertTransform();
            pivotWorld = worldPoint;
            hasPivot = true;
            state = ToolState.Rotating;
            hasHighlight = false;
            basePosition = sourceObject.transform.position;
            baseRotation = sourceObject.transform.rotation;
            rotX = rotY = rotZ = 0f;
            fineX = fineY = fineZ = 0f;
            posOffsetX = posOffsetY = posOffsetZ = 0f;
            Repaint(); SceneView.RepaintAll();
        }

        // ──────────────────────────────────────────────────────────────────────────
        // Transform computation — single source of truth
        // ──────────────────────────────────────────────────────────────────────────

        // Each continuous drag (slider or float field) collapses into a single undo
        // step by using the same group name while the control is hot, then
        // incrementing undoGroupIndex when the user releases (EndChangeCheck fires
        // but the control is no longer hot on the next frame).
        int undoGroupIndex = 0;
        int lastHotControl = 0;
        string lastUndoLabel = "";

        void ApplyAll(string undoLabel = "Pivot Aligner")
        {
            if (sourceObject == null || !hasPivot) return;

            // Start a new undo group whenever the active control changes or the
            // label changes (e.g. switching from Rotate to Move).
            int hot = GUIUtility.hotControl;
            if (hot != lastHotControl || undoLabel != lastUndoLabel)
            {
                undoGroupIndex++;
                lastHotControl = hot;
                lastUndoLabel = undoLabel;
            }

            Undo.RecordObject(sourceObject.transform, undoLabel);

            Quaternion delta = Quaternion.Euler(rotX + fineX, rotY + fineY, rotZ + fineZ);
            Vector3 posOff = new Vector3(posOffsetX, posOffsetY, posOffsetZ);

            sourceObject.transform.position = pivotWorld + delta * (basePosition - pivotWorld) + posOff;
            sourceObject.transform.rotation = delta * baseRotation;

            // Collapse all RecordObject calls for this drag into one undo step
            Undo.CollapseUndoOperations(Undo.GetCurrentGroup() - undoGroupIndex + 1);
        }

        void ResetRotation()
        {
            rotX = rotY = rotZ = fineX = fineY = fineZ = 0f;
            ApplyAll("Pivot Aligner — Reset Rotation");
        }

        void ResetPositionOffset()
        {
            posOffsetX = posOffsetY = posOffsetZ = 0f;
            ApplyAll("Pivot Aligner — Reset Offset");
        }

        void Apply()
        {
            if (sourceObject == null) return;
            Undo.SetCurrentGroupName("Pivot Aligner Apply");
            Undo.CollapseUndoOperations(Undo.GetCurrentGroup());
            ResetTool();
        }

        void RevertAndReset() { RevertTransform(); ResetTool(); }

        void RevertTransform()
        {
            if (sourceObject == null || !hasPivot) return;
            Undo.RecordObject(sourceObject.transform, "Pivot Aligner Revert");
            sourceObject.transform.position = basePosition;
            sourceObject.transform.rotation = baseRotation;
        }

        void ResetTool()
        {
            state = ToolState.Idle; hasPivot = hasHighlight = false;
            rotX = rotY = rotZ = fineX = fineY = fineZ = 0f;
            posOffsetX = posOffsetY = posOffsetZ = 0f;
            SceneView.RepaintAll(); Repaint();
        }

        // ──────────────────────────────────────────────────────────────────────────
        // Styles & layout helpers
        // ──────────────────────────────────────────────────────────────────────────

        void InitStyles()
        {
            if (stylesInit) return;
            stylesInit = true;
            headerStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 13,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(0.65f, 0.88f, 1f) }
            };
            sectionStyle = new GUIStyle(EditorStyles.boldLabel)
            { normal = { textColor = new Color(0.85f, 0.85f, 0.85f) } };
            stateStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleRight,
                normal = { textColor = new Color(0.40f, 0.75f, 0.50f) }
            };
            subLabelStyle = new GUIStyle(EditorStyles.miniLabel)
            { normal = { textColor = new Color(0.55f, 0.55f, 0.55f) } };
        }

        void DrawHR()
        {
            Rect r = EditorGUILayout.GetControlRect(false, 1);
            EditorGUI.DrawRect(r, new Color(0.35f, 0.35f, 0.35f, 0.6f));
        }
    }
}
