using UnityEditor;
using UnityEngine;

namespace ConveyorSystem.Editor
{
    /// <summary>
    /// Per-user editor settings for the conveyor tools, stored in EditorPrefs.
    /// Shown under Edit > Preferences > Conveyor System.
    /// </summary>
    public static class ConveyorEditorSettings
    {
        private const string Prefix = "ConveyorSystem.";

        /// <summary>
        /// Snap a conveyor to the nearest matching connector when a move or rotate drag ends.
        /// </summary>
        public static bool AutoSnap
        {
            get => EditorPrefs.GetBool(Prefix + "AutoSnap", true);
            set => EditorPrefs.SetBool(Prefix + "AutoSnap", value);
        }

        /// <summary>
        /// Maximum distance (world units) between two connectors for them to snap together.
        /// </summary>
        public static float SnapDistance
        {
            get => EditorPrefs.GetFloat(Prefix + "SnapDistance", 0.5f);
            set => EditorPrefs.SetFloat(Prefix + "SnapDistance", Mathf.Max(0.01f, value));
        }

        /// <summary>
        /// Rotate the snapped conveyor so its flow continues the neighbour's flow.
        /// </summary>
        public static bool AlignRotation
        {
            get => EditorPrefs.GetBool(Prefix + "AlignRotation", true);
            set => EditorPrefs.SetBool(Prefix + "AlignRotation", value);
        }

        /// <summary>
        /// Snap to the grid when no connector is close enough.
        /// </summary>
        public static bool GridSnap
        {
            get => EditorPrefs.GetBool(Prefix + "GridSnap", false);
            set => EditorPrefs.SetBool(Prefix + "GridSnap", value);
        }

        /// <summary>
        /// Grid cell size (world units) used by grid snapping.
        /// </summary>
        public static float GridSize
        {
            get => EditorPrefs.GetFloat(Prefix + "GridSize", 1f);
            set => EditorPrefs.SetFloat(Prefix + "GridSize", Mathf.Max(0.01f, value));
        }

        /// <summary>
        /// Yaw step (degrees) used by grid snapping.
        /// </summary>
        public static float RotationStep
        {
            get => EditorPrefs.GetFloat(Prefix + "RotationStep", 90f);
            set => EditorPrefs.SetFloat(Prefix + "RotationStep", Mathf.Clamp(value, 1f, 180f));
        }

        /// <summary>
        /// Draw connected / open / near-miss connections in the Scene view.
        /// </summary>
        public static bool ShowConnections
        {
            get => EditorPrefs.GetBool(Prefix + "ShowConnections", true);
            set => EditorPrefs.SetBool(Prefix + "ShowConnections", value);
        }

        /// <summary>
        /// Registers the Preferences page.
        /// </summary>
        [SettingsProvider]
        private static SettingsProvider CreateProvider()
        {
            return new SettingsProvider("Preferences/Conveyor System", SettingsScope.User)
            {
                keywords = new[] { "conveyor", "snap", "grid", "connection" },
                guiHandler = _ => DrawGUI()
            };
        }

        /// <summary>
        /// Draws the settings UI.
        /// </summary>
        private static void DrawGUI()
        {
            EditorGUIUtility.labelWidth = 200f;

            EditorGUILayout.LabelField("Snapping", EditorStyles.boldLabel);
            AutoSnap = EditorGUILayout.Toggle(new GUIContent("Auto Snap", "Snap when a move/rotate drag ends"), AutoSnap);
            using (new EditorGUI.DisabledScope(!AutoSnap))
            {
                SnapDistance = EditorGUILayout.FloatField(new GUIContent("Snap Distance", "Max distance between connectors"), SnapDistance);
                AlignRotation = EditorGUILayout.Toggle(new GUIContent("Align Rotation", "Continue the neighbour's flow direction"), AlignRotation);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Grid", EditorStyles.boldLabel);
            GridSnap = EditorGUILayout.Toggle(new GUIContent("Grid Snap", "Used when no connector is in range"), GridSnap);
            using (new EditorGUI.DisabledScope(!GridSnap))
            {
                GridSize = EditorGUILayout.FloatField("Grid Size", GridSize);
                RotationStep = EditorGUILayout.FloatField("Rotation Step (deg)", RotationStep);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Scene View", EditorStyles.boldLabel);
            ShowConnections = EditorGUILayout.Toggle(new GUIContent("Show Connections", "Green = connected, orange = near miss, red = open"), ShowConnections);

            if (GUI.changed) SceneView.RepaintAll();
        }
    }
}
