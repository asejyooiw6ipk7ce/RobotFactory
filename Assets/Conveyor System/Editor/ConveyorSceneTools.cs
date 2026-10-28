using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace ConveyorSystem.Editor
{
    /// <summary>
    /// Scene view integration: auto-snaps conveyors when a drag ends and draws their connections.
    /// </summary>
    [InitializeOnLoad]
    public static class ConveyorSceneTools
    {
        private const string MenuRoot = "Tools/Conveyor System/";

        private static readonly Color ConnectedColor = new Color(0.2f, 0.9f, 0.3f);
        private static readonly Color NearMissColor = new Color(1f, 0.6f, 0.1f);
        private static readonly Color OpenColor = new Color(0.95f, 0.25f, 0.25f);

        /// <summary>
        /// Pose of each selected conveyor when the mouse button went down.
        /// </summary>
        private static readonly Dictionary<Transform, (Vector3 position, Quaternion rotation)> dragStart =
            new Dictionary<Transform, (Vector3, Quaternion)>();

        static ConveyorSceneTools()
        {
            SceneView.duringSceneGui += OnSceneGUI;
        }

        /// <summary>
        /// Handles drag start/end for snapping and draws the connection overlay.
        /// </summary>
        private static void OnSceneGUI(SceneView view)
        {
            Event e = Event.current;

            // rawType: the move/rotate handles consume these events, but we still need to see them
            if (e.rawType == EventType.MouseDown && e.button == 0)
            {
                dragStart.Clear();
                foreach (var c in SelectedConveyors())
                    dragStart[c.transform] = (c.transform.position, c.transform.rotation);
            }
            else if (e.rawType == EventType.MouseUp && e.button == 0)
            {
                if (ConveyorEditorSettings.AutoSnap && !Application.isPlaying) SnapMovedConveyors();
                dragStart.Clear();
            }

            if (e.type == EventType.Repaint && ConveyorEditorSettings.ShowConnections) DrawConnections();
        }

        /// <summary>
        /// Snaps the selected conveyors that actually moved during the drag.
        /// </summary>
        private static void SnapMovedConveyors()
        {
            var moved = new List<ConveyorBase>();
            foreach (var c in SelectedConveyors())
            {
                if (!dragStart.TryGetValue(c.transform, out var start)) continue;
                if (c.transform.position != start.position || c.transform.rotation != start.rotation) moved.Add(c);
            }

            if (moved.Count > 0) ConveyorSnapping.Snap(moved, allowGrid: true);
        }

        /// <summary>
        /// Draws one marker per output trigger: green line = connected, orange line = near miss, red ring = open.
        /// </summary>
        private static void DrawConnections()
        {
            float size = 0.12f;
            foreach (var link in ConveyorConnections.Collect())
            {
                Vector3 from = link.FromTrigger.transform.position;
                switch (link.State)
                {
                    case ConveyorConnections.LinkState.Connected:
                        Handles.color = ConnectedColor;
                        Handles.DrawSolidDisc(from, Vector3.up, size * 0.5f);
                        Handles.DrawLine(from, link.ToTrigger.transform.position, 3f);
                        break;

                    case ConveyorConnections.LinkState.NearMiss:
                        Handles.color = NearMissColor;
                        Handles.DrawWireDisc(from, Vector3.up, size, 2f);
                        Handles.DrawDottedLine(from, link.ToTrigger.transform.position, 4f);
                        Handles.Label(from + Vector3.up * 0.25f, "not connected");
                        break;

                    default:
                        Handles.color = OpenColor;
                        Handles.DrawWireDisc(from, Vector3.up, size, 2f);
                        break;
                }
            }
        }

        /// <summary>
        /// Conveyors that are directly selected (not their child points).
        /// </summary>
        private static IEnumerable<ConveyorBase> SelectedConveyors()
        {
            foreach (var t in Selection.transforms)
            {
                var c = t.GetComponent<ConveyorBase>();
                if (c != null) yield return c;
            }
        }

        // ---------- Menu ----------

        [MenuItem(MenuRoot + "Snap Selected", priority = 0)]
        private static void SnapSelected()
        {
            var selected = new List<ConveyorBase>(SelectedConveyors());
            if (!ConveyorSnapping.Snap(selected, allowGrid: true))
                Debug.Log($"[Conveyor System] No connector within {ConveyorEditorSettings.SnapDistance} units (and grid snap is off).");
        }

        [MenuItem(MenuRoot + "Snap Selected", true)]
        private static bool SnapSelectedValidate()
        {
            foreach (var _ in SelectedConveyors()) return !Application.isPlaying;
            return false;
        }

        [MenuItem(MenuRoot + "Auto Snap", priority = 20)]
        private static void ToggleAutoSnap() => ConveyorEditorSettings.AutoSnap = !ConveyorEditorSettings.AutoSnap;

        [MenuItem(MenuRoot + "Auto Snap", true)]
        private static bool ToggleAutoSnapValidate()
        {
            Menu.SetChecked(MenuRoot + "Auto Snap", ConveyorEditorSettings.AutoSnap);
            return true;
        }

        [MenuItem(MenuRoot + "Grid Snap", priority = 21)]
        private static void ToggleGridSnap() => ConveyorEditorSettings.GridSnap = !ConveyorEditorSettings.GridSnap;

        [MenuItem(MenuRoot + "Grid Snap", true)]
        private static bool ToggleGridSnapValidate()
        {
            Menu.SetChecked(MenuRoot + "Grid Snap", ConveyorEditorSettings.GridSnap);
            return true;
        }

        [MenuItem(MenuRoot + "Show Connections", priority = 22)]
        private static void ToggleShowConnections()
        {
            ConveyorEditorSettings.ShowConnections = !ConveyorEditorSettings.ShowConnections;
            SceneView.RepaintAll();
        }

        [MenuItem(MenuRoot + "Show Connections", true)]
        private static bool ToggleShowConnectionsValidate()
        {
            Menu.SetChecked(MenuRoot + "Show Connections", ConveyorEditorSettings.ShowConnections);
            return true;
        }

        [MenuItem(MenuRoot + "Settings...", priority = 200)]
        private static void OpenSettings() => SettingsService.OpenUserPreferences("Preferences/Conveyor System");
    }
}
