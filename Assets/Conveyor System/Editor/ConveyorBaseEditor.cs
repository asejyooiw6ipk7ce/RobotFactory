using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace ConveyorSystem.Editor
{
    /// <summary>
    /// Inspector for every conveyor type: default fields, setup validation, snapping and runtime state.
    /// </summary>
    [CustomEditor(typeof(ConveyorBase), true)]
    [CanEditMultipleObjects]
    public class ConveyorBaseEditor : UnityEditor.Editor
    {
        /// <summary>
        /// Draws the inspector.
        /// </summary>
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var conveyor = (ConveyorBase)target;

            if (targets.Length == 1)
            {
                DrawValidation(conveyor);
                if (Application.isPlaying) DrawRuntimeState(conveyor);
            }

            DrawTools();
        }

        /// <summary>
        /// Shows setup problems as help boxes.
        /// </summary>
        private static void DrawValidation(ConveyorBase conveyor)
        {
            var issues = ConveyorValidator.Validate(conveyor);
            if (issues.Count == 0) return;

            EditorGUILayout.Space();
            foreach (var issue in issues) EditorGUILayout.HelpBox(issue.Message, issue.Severity);
        }

        /// <summary>
        /// Shows what the conveyor is doing during play mode.
        /// </summary>
        private static void DrawRuntimeState(ConveyorBase conveyor)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Runtime", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.ObjectField("Current Parcel", conveyor.CurrentParcel, typeof(Parcel), true);
                EditorGUILayout.Toggle("Waiting (parked)", conveyor.IsWaiting);
                EditorGUILayout.Toggle("Has Downstream", conveyor.HasDownstream);
                EditorGUILayout.Toggle("Can Accept Parcel", conveyor.CanAcceptParcel());
                if (conveyor is MultiJoinerConveyor multi) EditorGUILayout.IntField("Queued", multi.QueuedCount);
            }
        }

        /// <summary>
        /// Snap and helper buttons.
        /// </summary>
        private void DrawTools()
        {
            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(Application.isPlaying))
                {
                    if (GUILayout.Button(new GUIContent("Snap To Nearest", "Snap to the closest connector of another conveyor (or the grid)")))
                    {
                        var list = new List<ConveyorBase>();
                        foreach (var t in targets) list.Add((ConveyorBase)t);
                        if (!ConveyorSnapping.Snap(list, allowGrid: true))
                            Debug.Log($"[Conveyor System] No connector within {ConveyorEditorSettings.SnapDistance} units (and grid snap is off).");
                    }
                }

                bool missingEvents = false;
                foreach (var t in targets)
                    if (((ConveyorBase)t).GetComponent<ConveyorEvents>() == null) missingEvents = true;

                using (new EditorGUI.DisabledScope(!missingEvents))
                {
                    if (GUILayout.Button(new GUIContent("Add Conveyor Events", "Expose this conveyor's events as UnityEvents")))
                    {
                        foreach (var t in targets)
                        {
                            var c = (ConveyorBase)t;
                            if (c.GetComponent<ConveyorEvents>() == null) Undo.AddComponent<ConveyorEvents>(c.gameObject);
                        }
                    }
                }
            }

            if (GUILayout.Button("Validate Scene")) ConveyorValidator.ValidateScene();
        }

        /// <summary>
        /// Keeps the runtime section live in play mode.
        /// </summary>
        public override bool RequiresConstantRepaint() => Application.isPlaying;
    }
}
