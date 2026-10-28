using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Serialization;

namespace ConveyorSystem
{
    /// <summary>
    /// Represents a pauser conveyor segment that can stop parcels for a specified duration or be manually toggled on/off.
    /// </summary>
    public class PauserConveyor : ConveyorBase
    {
        /// <summary>
        /// The duration (in seconds) to pause the parcel at the center point.
        /// </summary>
        [Header("Pauser")]
        public float PauseDuration = 2f;

        /// <summary>
        /// Serialized active state. Use the IsActive property at runtime so waiting parcels are woken.
        /// </summary>
        [Header("Control")]
        [Tooltip("If false, the conveyor is stopped and does not accept/move parcels.")]
        [SerializeField, FormerlySerializedAs("IsActive")]
        private bool isActive = true;

        /// <summary>
        /// The active state the runtime last acted on; used to detect Inspector edits in play mode.
        /// </summary>
        private bool appliedActive;

        /// <summary>
        /// Raised when the conveyor is started or stopped. The bool is the new active state.
        /// </summary>
        public event Action<PauserConveyor, bool> ActiveChanged;

        /// <summary>
        /// Controls whether the conveyor is currently active.
        /// If false, the conveyor stops moving parcels and refuses new ones.
        /// </summary>
        public bool IsActive
        {
            get => isActive;
            set
            {
                isActive = value;
                ApplyActiveState();
            }
        }

        /// <summary>
        /// Resets the component to its default state.
        /// Sets the conveyor type to Pauser.
        /// </summary>
        private void Reset()
        {
            ConveyorType = ConveyorType.Pauser;
        }

        /// <summary>
        /// Called when the component is initialized.
        /// </summary>
        protected override void Awake()
        {
            base.Awake();
            appliedActive = isActive;
        }

        /// <summary>
        /// Applies Inspector changes to IsActive made during play mode.
        /// </summary>
        private void OnValidate()
        {
            if (Application.isPlaying) ApplyActiveState();
        }

        /// <summary>
        /// Activates the conveyor, allowing it to move and accept parcels.
        /// </summary>
        public void StartConveyor()
        {
            IsActive = true;
        }

        /// <summary>
        /// Deactivates the conveyor, stopping movement and preventing new parcels from entering.
        /// </summary>
        public void StopConveyor()
        {
            IsActive = false;
        }

        /// <summary>
        /// Toggles the active state of the conveyor.
        /// </summary>
        public void ToggleConveyor()
        {
            IsActive = !IsActive;
        }

        /// <summary>
        /// Checks if the conveyor can accept a new parcel.
        /// Returns false if the conveyor is inactive or already occupied.
        /// </summary>
        /// <returns>True if the conveyor can accept a parcel, false otherwise.</returns>
        public override bool CanAcceptParcel()
        {
            return isActive && base.CanAcceptParcel();
        }

        /// <summary>
        /// Raises events and wakes waiting routines when the active state actually changed.
        /// </summary>
        private void ApplyActiveState()
        {
            if (appliedActive == isActive) return;
            appliedActive = isActive;

            ActiveChanged?.Invoke(this, isActive);

            // Upstream segments may now hand over (or must stop), and a parcel parked here may resume
            NotifyAvailabilityChanged();
            WakeUp();
        }

        /// <summary>
        /// Coroutine that handles the movement of a parcel on this pauser conveyor.
        /// 1. Moves the parcel to the center point (frozen while inactive).
        /// 2. Waits at the center point for the specified PauseDuration (only counting active time).
        /// 3. Waits for a valid next conveyor (frozen while inactive).
        /// 4. Moves the parcel to the exit point (frozen while inactive).
        /// 5. Transfers the parcel to the next conveyor.
        /// While inactive the routine is parked, so a stopped Pauser costs nothing per frame.
        /// </summary>
        /// <param name="parcel">The parcel to move.</param>
        /// <returns>IEnumerator for coroutine execution.</returns>
        protected override IEnumerator MoveParcelRoutine(Parcel parcel)
        {
            Transform t = parcel.transform;

            // Phase 1: Move to center (only while active)
            while (Vector3.Distance(t.position, CenterPoint.position) > 0.01f)
            {
                if (!isActive)
                {
                    yield return WaitForChange;
                    continue;
                }
                t.position = Vector3.MoveTowards(t.position, CenterPoint.position, Speed * Time.deltaTime);
                yield return null;
            }

            // Phase 2: Pause at center
            // We only accumulate pause time when the conveyor is active
            float paused = 0f;
            while (paused < PauseDuration)
            {
                if (!isActive)
                {
                    yield return WaitForChange;
                    continue;
                }
                paused += Time.deltaTime;
                yield return null;
            }

            // Phase 3-4: find next conveyor, move to exit, transfer. Retried until the transfer succeeds.
            bool blocked = false;
            while (true)
            {
                // Remain at center while stopped or blocked; parked until something changes
                if (!isActive)
                {
                    yield return WaitForChange;
                    continue;
                }

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

                // Move to exit (wait when the conveyor stops)
                while (Vector3.Distance(t.position, exitPos) > 0.01f)
                {
                    if (!isActive)
                    {
                        yield return WaitForChange;
                        continue;
                    }
                    t.position = Vector3.MoveTowards(t.position, exitPos, Speed * Time.deltaTime);
                    yield return null;
                }

                // Transfer to next conveyor
                if (next.AcceptParcel(parcel))
                {
                    ReleaseParcel();
                    yield break;
                }

                // Snap back if not accepted (also consider pauses when stopping), then retry
                while (Vector3.Distance(t.position, CenterPoint.position) > 0.01f)
                {
                    if (!isActive)
                    {
                        yield return WaitForChange;
                        continue;
                    }
                    t.position = Vector3.MoveTowards(t.position, CenterPoint.position, Speed * Time.deltaTime);
                    yield return null;
                }
            }
        }
    }
}
