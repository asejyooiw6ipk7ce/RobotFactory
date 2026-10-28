using UnityEngine;
using System.Collections;

namespace ConveyorSystem
{
    /// <summary>
    /// Represents a corner conveyor segment that rotates parcels to align with the exit direction.
    /// </summary>
    public class CornerConveyor : ConveyorBase
    {
        /// <summary>
        /// The speed at which the parcel rotates (degrees per second) when it reaches the center point.
        /// </summary>
        [Header("Rotation")]
        [Tooltip("Parcel rotation speed (degrees per second) when located at CenterPoint")]
        [Range(10f, 1080f)]
        public float RotationSpeed = 180f;

        /// <summary>
        /// Resets the component to its default state.
        /// Sets the conveyor type to Corner.
        /// </summary>
        private void Reset()
        {
            ConveyorType = ConveyorType.Corner;
        }

        /// <summary>
        /// Coroutine that handles the movement and rotation of a parcel on this corner conveyor.
        /// 1. Moves the parcel to the center point.
        /// 2. Waits for a valid next conveyor.
        /// 3. Rotates the parcel in place at the center point to align with the exit point's rotation.
        /// 4. Moves the parcel to the exit point.
        /// 5. Transfers the parcel to the next conveyor.
        /// </summary>
        /// <param name="parcel">The parcel to move.</param>
        /// <returns>IEnumerator for coroutine execution.</returns>
        protected override IEnumerator MoveParcelRoutine(Parcel parcel)
        {
            Transform t = parcel.transform;

            // Phase 1: Move to center
            while (Vector3.Distance(t.position, CenterPoint.position) > 0.01f)
            {
                t.position = Vector3.MoveTowards(t.position, CenterPoint.position, Speed * Time.deltaTime);
                yield return null;
            }

            // Phase 2: Wait for next conveyor and rotate
            bool blocked = false;
            while (true)
            {
                // Park until a downstream segment can take the parcel (no per-frame polling)
                var next = GetNextConveyor(parcel);
                if (next == null || !next.CanAcceptParcel())
                {
                    if (!blocked) RaiseParcelBlocked(parcel);
                    blocked = true;
                    yield return WaitForChange;
                    continue;
                }
                blocked = false;

                int outIdx = GetOutputIndexForConveyor(next);
                RaiseParcelRouted(parcel, outIdx < 0 ? 0 : outIdx);
                Transform exitPoint = (outIdx >= 0 && outIdx < OutputPoints.Length) ? OutputPoints[outIdx] : null;
                Vector3 exitPos = exitPoint ? exitPoint.position : CenterPoint.position + transform.forward * 0.5f;

                // Determine target rotation: match exit point rotation or default to forward direction
                Quaternion targetRot = exitPoint ? exitPoint.rotation : Quaternion.LookRotation(transform.forward, Vector3.up);

                // Rotate in place at center until aligned
                while (Quaternion.Angle(parcel.transform.rotation, targetRot) > 0.5f)
                {
                    parcel.transform.rotation = Quaternion.RotateTowards(parcel.transform.rotation, targetRot, RotationSpeed * Time.deltaTime);

                    // Keep parcel exactly at center while rotating to avoid drift
                    if (Vector3.Distance(t.position, CenterPoint.position) > 0.001f)
                    {
                        t.position = CenterPoint.position;
                    }
                    yield return null;
                }

                // Phase 3: Move to exit point (after rotation is complete)
                while (Vector3.Distance(t.position, exitPos) > 0.01f)
                {
                    t.position = Vector3.MoveTowards(t.position, exitPos, Speed * Time.deltaTime);
                    yield return null;
                }

                // Phase 4: Transfer to next conveyor
                bool accepted = next.AcceptParcel(parcel);
                if (accepted)
                {
                    ReleaseParcel();
                    yield break;
                }
                else
                {
                    // If transfer fails, snap back to center and retry
                    // Rotation is already done, so we just move back to center
                    while (Vector3.Distance(t.position, CenterPoint.position) > 0.01f)
                    {
                        t.position = Vector3.MoveTowards(t.position, CenterPoint.position, Speed * Time.deltaTime);
                        yield return null;
                    }
                }
            }
        }
    }
}