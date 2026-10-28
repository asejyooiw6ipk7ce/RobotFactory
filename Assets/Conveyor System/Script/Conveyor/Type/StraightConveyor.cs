using UnityEngine;

namespace ConveyorSystem
{
    /// <summary>
    /// Represents a straight conveyor segment.
    /// This is the simplest conveyor type, moving parcels in a straight line from input to output.
    /// </summary>
    [ExecuteAlways]
    public class StraightConveyor : ConveyorBase
    {
        /// <summary>
        /// Resets the component to its default state.
        /// Sets the conveyor type to Straight and creates default points if missing.
        /// </summary>
        private void Reset()
        {
            ConveyorType = ConveyorType.Straight;
            // If there are no points, create them by default
            CreateDefaultPoints();
        }

        /// <summary>
        /// Called when the script is loaded or a value is changed in the Inspector.
        /// Ensures the conveyor type is Straight and that the OutputPoints array has exactly one element.
        /// Also attempts to auto-assign points from child objects.
        /// </summary>
        private void OnValidate()
        {
            ConveyorType = ConveyorType.Straight;

            // We guarantee that the OutputPoints array contains exactly 1 element for StraightConveyor
            if (OutputPoints == null || OutputPoints.Length != 1)
            {
                var old = OutputPoints;
                OutputPoints = new Transform[1];
                if (old != null && old.Length > 0) OutputPoints[0] = old[0];
            }

            // If some points are not assigned, we will try to find them by name among the child objects.
            if (InputPoint == null) InputPoint = transform.Find("InputPoint");
            if (CenterPoint == null) CenterPoint = transform.Find("CenterPoint");
            if (OutputPoints[0] == null) OutputPoints[0] = transform.Find("OutputPoint");

            // Automatically create points if they don't exist (only in the editor).
            // Unity forbids creating GameObjects inside OnValidate, so the work is deferred.
#if UNITY_EDITOR
            if (InputPoint == null || CenterPoint == null || OutputPoints[0] == null)
            {
                UnityEditor.EditorApplication.delayCall += CreateDefaultPointsDeferred;
            }
#endif
        }

#if UNITY_EDITOR
        /// <summary>
        /// Deferred callback for OnValidate. Skips destroyed components, play mode and prefab assets that are not open for editing.
        /// </summary>
        private void CreateDefaultPointsDeferred()
        {
            if (this == null || Application.isPlaying) return;
            if (UnityEditor.EditorUtility.IsPersistent(this)) return;
            if (InputPoint != null && CenterPoint != null && OutputPoints != null && OutputPoints.Length == 1 && OutputPoints[0] != null) return;

            CreateDefaultPoints();
        }
#endif

#if UNITY_EDITOR
        /// <summary>
        /// Creates basic child transforms: InputPoint, CenterPoint, OutputPoint.
        /// Does not overwrite existing objects.
        /// </summary>
        [ContextMenu("Create Default Points")]
        private void CreateDefaultPoints()
        {
            if (transform.Find("InputPoint") == null)
            {
                var go = new GameObject("InputPoint");
                go.transform.SetParent(transform, false);
                go.transform.localPosition = new Vector3(0f, 0f, -0.5f);
                InputPoint = go.transform;
            }
            else InputPoint = transform.Find("InputPoint");

            if (transform.Find("CenterPoint") == null)
            {
                var go = new GameObject("CenterPoint");
                go.transform.SetParent(transform, false);
                go.transform.localPosition = Vector3.zero;
                CenterPoint = go.transform;
            }
            else CenterPoint = transform.Find("CenterPoint");

            if (transform.Find("OutputPoint") == null)
            {
                var go = new GameObject("OutputPoint");
                go.transform.SetParent(transform, false);
                go.transform.localPosition = new Vector3(0f, 0f, 0.5f);
                OutputPoints = new Transform[1] { go.transform };
            }
            else
            {
                var op = transform.Find("OutputPoint");
                OutputPoints = new Transform[1] { op };
            }

            // Update the NextConveyors array inside the base Awake/OnValidate on the next check
            UnityEditor.EditorUtility.SetDirty(this);
        }
#endif
    }
}