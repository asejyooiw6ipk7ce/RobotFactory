using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ConveyorSystem
{
    /// <summary>
    /// Represents a joiner conveyor segment that merges multiple input lines into a single output.
    /// </summary>
    public class JoinerConveyor : ConveyorBase
    {
        /// <summary>
        /// Array of input transforms. The first element is used as the base InputPoint for compatibility.
        /// </summary>
        [Header("Joiner Inputs")]
        [Tooltip("Multiple input transforms. The first element will be used as the base InputPoint for compatibility.")]
        public Transform[] InputPoints = new Transform[0];

        /// <summary>
        /// Resets the component to its default state.
        /// Sets the conveyor type to Joiner.
        /// </summary>
        private void Reset()
        {
            ConveyorType = ConveyorType.Joiner;
        }

        /// <summary>
        /// Called when the script is loaded or a value is changed in the Inspector.
        /// Ensures the base InputPoint is synchronized with the first element of InputPoints.
        /// </summary>
        private void OnValidate()
        {
            // Keep base.InputPoint compatible with inspector (first input)
            InputPoint = (InputPoints != null && InputPoints.Length > 0) ? InputPoints[0] : InputPoint;
        }

        /// <summary>
        /// Called when the component is initialized.
        /// Ensures the base InputPoint is set correctly at runtime.
        /// </summary>
        protected override void Awake()
        {
            base.Awake();
            // Ensure base InputPoint points to first element if present
            if (InputPoints != null && InputPoints.Length > 0) InputPoint = InputPoints[0];
        }

        /// <summary>
        /// Draws gizmos to visualize the multiple input points and their paths to the center.
        /// </summary>
        protected override void OnDrawGizmos()
        {
            base.OnDrawGizmos();

            if (InputPoints == null || CenterPoint == null) return;

            for (int i = 0; i < InputPoints.Length; i++)
            {
                var ip = InputPoints[i];
                if (ip == null) continue;

                Gizmos.color = Color.green;
                Gizmos.DrawSphere(ip.position, 0.05f);
                DrawArrowLocal(ip.position, CenterPoint.position, Color.green, 18f, 0.12f);
            }
        }

        /// <summary>
        /// Helper method to draw an arrow for visualization.
        /// This is a local copy of the private DrawArrow method in ConveyorBase.
        /// </summary>
        /// <param name="from">Start position.</param>
        /// <param name="to">End position.</param>
        /// <param name="color">Color of the arrow.</param>
        /// <param name="headAngle">Angle of the arrow head lines.</param>
        /// <param name="headLength">Length of the arrow head lines.</param>
        private void DrawArrowLocal(Vector3 from, Vector3 to, Color color, float headAngle = 20f, float headLength = 0.12f)
        {
            Gizmos.color = color;
            Gizmos.DrawLine(from, to);

            Vector3 dir = (to - from);
            if (dir.sqrMagnitude <= 0f) return;
            dir.Normalize();

            Quaternion baseRot = Quaternion.LookRotation(dir);
            Quaternion leftRot = baseRot * Quaternion.Euler(0f, 180f + headAngle, 0f);
            Quaternion rightRot = baseRot * Quaternion.Euler(0f, 180f - headAngle, 0f);

            Vector3 leftDir = leftRot * Vector3.forward;
            Vector3 rightDir = rightRot * Vector3.forward;

            Vector3 headBase = to;
            Gizmos.DrawLine(headBase, headBase + leftDir * headLength);
            Gizmos.DrawLine(headBase, headBase + rightDir * headLength);
        }
    }
}