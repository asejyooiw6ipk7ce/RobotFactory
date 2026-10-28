using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace ConveyorSystem.Editor
{
    /// <summary>
    /// Connector-based snapping for conveyor segments.
    /// An input point of one conveyor snaps onto an output point of another (and vice versa);
    /// optionally the moved conveyor is rotated so the flow continues in the same direction.
    /// </summary>
    public static class ConveyorSnapping
    {
        /// <summary>
        /// One input or output point of a conveyor, with the direction parcels flow through it.
        /// </summary>
        public struct Connector
        {
            public ConveyorBase Owner;
            public Transform Point;
            public bool IsInput;

            /// <summary>
            /// Horizontal flow direction: into the center for inputs, away from it for outputs.
            /// </summary>
            public Vector3 FlowDirection;
        }

        /// <summary>
        /// Enumerates all connectors of a conveyor (every InputPoint of a Joiner, every OutputPoint).
        /// </summary>
        public static IEnumerable<Connector> GetConnectors(ConveyorBase conveyor)
        {
            if (conveyor == null) yield break;

            foreach (var input in GetInputPoints(conveyor))
                yield return Make(conveyor, input, true);

            if (conveyor.OutputPoints == null) yield break;
            foreach (var output in conveyor.OutputPoints)
                if (output != null) yield return Make(conveyor, output, false);
        }

        /// <summary>
        /// The input points of a conveyor: all InputPoints for joiners, otherwise InputPoint.
        /// </summary>
        public static IEnumerable<Transform> GetInputPoints(ConveyorBase conveyor)
        {
            if (conveyor is JoinerConveyor joiner && joiner.InputPoints != null && joiner.InputPoints.Length > 0)
            {
                foreach (var p in joiner.InputPoints)
                    if (p != null) yield return p;
                yield break;
            }

            if (conveyor.InputPoint != null) yield return conveyor.InputPoint;
        }

        /// <summary>
        /// Builds a connector, deriving its flow direction from the center point.
        /// </summary>
        private static Connector Make(ConveyorBase owner, Transform point, bool isInput)
        {
            Vector3 dir = owner.CenterPoint != null
                ? (isInput ? owner.CenterPoint.position - point.position : point.position - owner.CenterPoint.position)
                : owner.transform.forward;
            dir.y = 0f;
            if (dir.sqrMagnitude < 1e-6f) dir = owner.transform.forward;

            return new Connector { Owner = owner, Point = point, IsInput = isInput, FlowDirection = dir.normalized };
        }

        /// <summary>
        /// Snaps the given conveyors (moved together as a group) to the nearest compatible connector
        /// of any other conveyor in the scene, or to the grid when enabled and nothing is in range.
        /// </summary>
        /// <returns>True if anything was moved.</returns>
        public static bool Snap(IList<ConveyorBase> moved, bool allowGrid)
        {
            if (moved == null || moved.Count == 0) return false;

            var movedSet = new HashSet<ConveyorBase>(moved);
            var others = new List<ConveyorBase>();
            foreach (var c in SceneQuery.FindAll<ConveyorBase>())
                if (!movedSet.Contains(c) && c.gameObject.scene == moved[0].gameObject.scene) others.Add(c);

            if (TryFindBestPair(moved, others, ConveyorEditorSettings.SnapDistance, out var mine, out var theirs))
            {
                ApplySnap(moved, mine, theirs, ConveyorEditorSettings.AlignRotation);
                return true;
            }

            if (allowGrid && ConveyorEditorSettings.GridSnap)
            {
                SnapToGrid(moved);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Finds the closest (moved connector, other connector) pair of opposite kinds within maxDistance.
        /// </summary>
        public static bool TryFindBestPair(IList<ConveyorBase> moved, IList<ConveyorBase> others, float maxDistance,
            out Connector mine, out Connector theirs)
        {
            mine = default;
            theirs = default;
            float best = maxDistance;
            bool found = false;

            var otherConnectors = new List<Connector>();
            foreach (var o in others) otherConnectors.AddRange(GetConnectors(o));

            foreach (var m in moved)
            {
                foreach (var a in GetConnectors(m))
                {
                    foreach (var b in otherConnectors)
                    {
                        if (a.IsInput == b.IsInput) continue;

                        float d = Vector3.Distance(a.Point.position, b.Point.position);
                        if (d > best) continue;

                        best = d;
                        mine = a;
                        theirs = b;
                        found = true;
                    }
                }
            }

            return found;
        }

        /// <summary>
        /// Rotates (around the moved connector) and translates the group so the two connectors coincide.
        /// </summary>
        private static void ApplySnap(IList<ConveyorBase> moved, Connector mine, Connector theirs, bool alignRotation)
        {
            var transforms = new Transform[moved.Count];
            for (int i = 0; i < moved.Count; i++) transforms[i] = moved[i].transform;
            Undo.RecordObjects(transforms, "Snap Conveyor");

            Vector3 pivot = mine.Point.position;

            if (alignRotation)
            {
                // Make the moved connector's flow continue the neighbour's flow
                float yaw = Vector3.SignedAngle(mine.FlowDirection, theirs.FlowDirection, Vector3.up);
                if (Mathf.Abs(yaw) > 0.01f)
                {
                    foreach (var t in transforms) t.RotateAround(pivot, Vector3.up, yaw);
                }
            }

            Vector3 offset = theirs.Point.position - mine.Point.position;
            foreach (var t in transforms) t.position += offset;
        }

        /// <summary>
        /// Rounds the group's position (XZ) to the grid and its yaw to the rotation step, keeping the group rigid.
        /// </summary>
        private static void SnapToGrid(IList<ConveyorBase> moved)
        {
            var transforms = new Transform[moved.Count];
            for (int i = 0; i < moved.Count; i++) transforms[i] = moved[i].transform;
            Undo.RecordObjects(transforms, "Snap Conveyor To Grid");

            // Use the first conveyor as the reference and move the rest with it
            Transform reference = transforms[0];
            float step = ConveyorEditorSettings.RotationStep;
            float yaw = reference.eulerAngles.y;
            float snappedYaw = Mathf.Round(yaw / step) * step;
            foreach (var t in transforms) t.RotateAround(reference.position, Vector3.up, snappedYaw - yaw);

            float grid = ConveyorEditorSettings.GridSize;
            Vector3 p = reference.position;
            Vector3 snapped = new Vector3(Mathf.Round(p.x / grid) * grid, p.y, Mathf.Round(p.z / grid) * grid);
            Vector3 offset = snapped - p;
            foreach (var t in transforms) t.position += offset;
        }
    }
}
