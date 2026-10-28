using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ConveyorSystem
{
    /// <summary>
    /// Abstract base class for all conveyor segment types in the system.
    /// Handles parcel movement, routing logic, events, and visual debugging through gizmos.
    /// </summary>
    /// <remarks>
    /// Waiting is event-driven: a parcel blocked at the center parks its movement routine
    /// (no per-frame work) and is woken when a downstream segment reports an availability change.
    /// </remarks>
    [RequireComponent(typeof(Collider))]
    public abstract class ConveyorBase : MonoBehaviour
    {
        /// <summary>
        /// The type of conveyor segment this component represents.
        /// </summary>
        public ConveyorType ConveyorType;

        /// <summary>
        /// The movement speed of parcels on this conveyor segment (units per second).
        /// </summary>
        public float Speed = 2f;

        /// <summary>
        /// Transform representing the entry point where parcels enter this conveyor.
        /// </summary>
        public Transform InputPoint;

        /// <summary>
        /// Transform representing the central point of this conveyor segment, used for movement and routing logic.
        /// </summary>
        public Transform CenterPoint;

        /// <summary>
        /// Array of Transforms representing the exit points for parcels leaving this conveyor.
        /// The index of each exit point corresponds to the output index used in routing logic.
        /// </summary>
        public Transform[] OutputPoints;

        /// <summary>
        /// The parcel currently on this conveyor segment. Null if no parcel is present.
        /// </summary>
        [HideInInspector] public Parcel CurrentParcel;

        /// <summary>
        /// Raised when a parcel is accepted onto this segment.
        /// </summary>
        public event Action<ConveyorBase, Parcel> ParcelEntered;

        /// <summary>
        /// Raised when a parcel leaves this segment (handed over, stored, consumed or picked up).
        /// </summary>
        public event Action<ConveyorBase, Parcel> ParcelExited;

        /// <summary>
        /// Raised when a parcel starts waiting at the center because no downstream segment can take it.
        /// </summary>
        public event Action<ConveyorBase, Parcel> ParcelBlocked;

        /// <summary>
        /// Raised when a parcel has been assigned an exit; the int is the output index.
        /// </summary>
        public event Action<ConveyorBase, Parcel, int> ParcelRouted;

        /// <summary>
        /// Raised whenever the result of CanAcceptParcel() may have changed.
        /// </summary>
        public event Action<ConveyorBase> AvailabilityChanged;

        /// <summary>
        /// Yield this from MoveParcelRoutine to park the routine until WakeUp() is called.
        /// A parked routine costs nothing per frame. It is woken (one frame later) when a downstream
        /// segment reports an availability change, when a new downstream segment connects, or by WakeUp().
        /// </summary>
        protected static readonly object WaitForChange = new object();

        // Each output index can have multiple downstream conveyors (registered via triggers)
        protected List<ConveyorBase>[] NextConveyors;

        protected Coroutine moveRoutine;

        /// <summary>
        /// Segments that feed into this one; woken when this segment's availability changes.
        /// </summary>
        private readonly List<ConveyorBase> upstream = new List<ConveyorBase>();

        /// <summary>
        /// Movement routine parked on WaitForChange, or null.
        /// </summary>
        private IEnumerator parkedRoutine;

        /// <summary>
        /// Pending resume of the parked routine, or null.
        /// </summary>
        private Coroutine resumeRoutine;

        /// <summary>
        /// True while the current parcel's movement is parked waiting for a change.
        /// </summary>
        public bool IsWaiting => parkedRoutine != null;

        /// <summary>
        /// Called when the component is initialized.
        /// Initializes the NextConveyors array based on the number of output points.
        /// </summary>
        protected virtual void Awake()
        {
            if (OutputPoints == null) OutputPoints = new Transform[0];
            NextConveyors = new List<ConveyorBase>[OutputPoints.Length];
            for (int i = 0; i < NextConveyors.Length; i++) NextConveyors[i] = new List<ConveyorBase>();
        }

        /// <summary>
        /// Resumes a parked routine that may have missed its wake-up while the component was disabled.
        /// </summary>
        protected virtual void OnEnable()
        {
            if (parkedRoutine != null) WakeUp();
        }

        /// <summary>
        /// Determines whether this conveyor segment can accept a new parcel.
        /// Overrides that depend on extra state must call NotifyAvailabilityChanged() when that state changes.
        /// </summary>
        /// <returns>True if a parcel can be accepted now.</returns>
        public virtual bool CanAcceptParcel()
        {
            return CurrentParcel == null;
        }

        /// <summary>
        /// Attempts to accept a parcel onto this conveyor segment.
        /// </summary>
        /// <param name="parcel">The parcel to accept.</param>
        /// <returns>True if the parcel was accepted.</returns>
        public virtual bool AcceptParcel(Parcel parcel)
        {
            // A segment destroyed at runtime (e.g. removed by a level editor) must refuse hand-overs
            if (this == null || parcel == null || !CanAcceptParcel()) return false;
            BeginTransport(parcel);
            return true;
        }

        /// <summary>
        /// Takes ownership of the parcel and starts its movement coroutine, without checking occupancy.
        /// Used by AcceptParcel and by types that manage their own admission (e.g. MultiJoiner's queue).
        /// </summary>
        /// <param name="parcel">The parcel to transport.</param>
        /// <param name="raiseEntered">False if ParcelEntered was already raised for this parcel (e.g. when it was queued).</param>
        protected void BeginTransport(Parcel parcel, bool raiseEntered = true)
        {
            CurrentParcel = parcel;
            parcel.SetPhysicsEnabled(false);
            parcel.CurrentConveyor = this;

            // Start movement coroutine
            StopMovement();
            moveRoutine = StartCoroutine(Drive(MoveParcelRoutine(parcel)));

            if (raiseEntered) RaiseParcelEntered(parcel);
            NotifyAvailabilityChanged();
        }

        /// <summary>
        /// Releases the current parcel from this conveyor segment, allowing it to be moved or accepted by another conveyor.
        /// </summary>
        public virtual void ReleaseParcel()
        {
            var parcel = CurrentParcel;
            CurrentParcel = null;
            if (parcel != null) RaiseParcelExited(parcel);
            NotifyAvailabilityChanged();
        }

        /// <summary>
        /// Removes a parcel that is taken off this conveyor externally (e.g. picked up by the player).
        /// Stops its movement so the conveyor does not keep dragging it, and frees the segment.
        /// </summary>
        /// <param name="parcel">The parcel being removed.</param>
        public virtual void RemoveParcel(Parcel parcel)
        {
            if (parcel == null || CurrentParcel != parcel) return;
            StopMovement();
            ReleaseParcel();
        }

        // Default selection: first available downstream conveyor
        public virtual ConveyorBase GetNextConveyor(Parcel parcel)
        {
            for (int i = 0; i < NextConveyors.Length; i++)
            {
                foreach (var c in NextConveyors[i])
                {
                    if (c != null && c.CanAcceptParcel()) return c;
                }
            }

            return null;
        }

        /// <summary>
        /// Helper method to find the output index corresponding to a given downstream conveyor.
        /// </summary>
        /// <param name="conv">The downstream conveyor.</param>
        /// <returns>The output index, or -1 if it is not connected.</returns>
        protected int GetOutputIndexForConveyor(ConveyorBase conv)
        {
            for (int i = 0; i < NextConveyors.Length; i++)
                if (NextConveyors[i].Contains(conv))
                    return i;
            return -1;
        }

        /// <summary>
        /// Checks if the exit path corresponding to the given output index has any downstream conveyor that can accept a parcel.
        /// </summary>
        /// <param name="outputIndex">The output index to check.</param>
        /// <returns>True if at least one downstream conveyor on this exit can accept a parcel.</returns>
        public virtual bool IsExitFree(int outputIndex)
        {
            if (NextConveyors == null || outputIndex < 0 || outputIndex >= NextConveyors.Length) return false;
            foreach (var c in NextConveyors[outputIndex])
                if (c != null && c.CanAcceptParcel())
                    return true;
            return false;
        }

        /// <summary>
        /// Returns true if any output is connected to at least one downstream conveyor.
        /// </summary>
        public bool HasDownstream
        {
            get
            {
                if (NextConveyors == null) return false;
                foreach (var list in NextConveyors)
                    if (list.Count > 0) return true;
                return false;
            }
        }

        /// <summary>
        /// Registers a downstream conveyor for a specific output index.
        /// Called by ConveyorTrigger when an output trigger detects a connection to another conveyor's input trigger.
        /// </summary>
        /// <param name="outputIndex">The output index the connection belongs to.</param>
        /// <param name="other">The downstream conveyor.</param>
        public void RegisterNextConveyor(int outputIndex, ConveyorBase other)
        {
            if (NextConveyors == null || outputIndex < 0 || outputIndex >= NextConveyors.Length || other == null) return;
            if (!NextConveyors[outputIndex].Contains(other)) NextConveyors[outputIndex].Add(other);
            if (!other.upstream.Contains(this)) other.upstream.Add(this);

            // A new route may unblock a waiting parcel
            WakeUp();
        }

        /// <summary>
        /// Unregisters a downstream conveyor for a specific output index.
        /// Called by ConveyorTrigger when an output trigger detects that a connection to another conveyor's input trigger has been broken.
        /// </summary>
        /// <param name="outputIndex">The output index the connection belongs to.</param>
        /// <param name="other">The downstream conveyor.</param>
        public void UnregisterNextConveyor(int outputIndex, ConveyorBase other)
        {
            if (NextConveyors == null || outputIndex < 0 || outputIndex >= NextConveyors.Length || other == null) return;
            NextConveyors[outputIndex].Remove(other);
            if (GetOutputIndexForConveyor(other) < 0) other.upstream.Remove(this);
        }

        /// <summary>
        /// Signals that CanAcceptParcel() may now return a different result.
        /// Raises AvailabilityChanged and wakes upstream segments that are waiting for this one.
        /// Call it from custom types whenever a condition used by CanAcceptParcel() changes.
        /// </summary>
        public void NotifyAvailabilityChanged()
        {
            AvailabilityChanged?.Invoke(this);

            for (int i = upstream.Count - 1; i >= 0; i--)
            {
                var up = upstream[i];
                if (up == null)
                {
                    upstream.RemoveAt(i);
                    continue;
                }
                up.WakeUp();
            }
        }

        /// <summary>
        /// Resumes a routine parked on WaitForChange on the next frame. Does nothing if nothing is parked.
        /// Deferring by one frame avoids re-entrant hand-overs inside another segment's routine.
        /// </summary>
        public void WakeUp()
        {
            if (parkedRoutine == null || resumeRoutine != null || !isActiveAndEnabled) return;
            resumeRoutine = StartCoroutine(ResumeNextFrame());
        }

        /// <summary>
        /// Stops the current movement routine, including a parked one and a pending resume.
        /// </summary>
        protected void StopMovement()
        {
            if (moveRoutine != null) StopCoroutine(moveRoutine);
            if (resumeRoutine != null) StopCoroutine(resumeRoutine);
            moveRoutine = null;
            resumeRoutine = null;
            parkedRoutine = null;
        }

        /// <summary>
        /// Raises ParcelEntered. For derived types that admit parcels themselves.
        /// </summary>
        protected void RaiseParcelEntered(Parcel parcel) => ParcelEntered?.Invoke(this, parcel);

        /// <summary>
        /// Raises ParcelExited. For derived types that remove parcels themselves.
        /// </summary>
        protected void RaiseParcelExited(Parcel parcel) => ParcelExited?.Invoke(this, parcel);

        /// <summary>
        /// Raises ParcelBlocked. For custom MoveParcelRoutine implementations.
        /// </summary>
        protected void RaiseParcelBlocked(Parcel parcel) => ParcelBlocked?.Invoke(this, parcel);

        /// <summary>
        /// Raises ParcelRouted. For custom MoveParcelRoutine implementations.
        /// </summary>
        protected void RaiseParcelRouted(Parcel parcel, int outputIndex) => ParcelRouted?.Invoke(this, parcel, outputIndex);

        /// <summary>
        /// Runs a movement routine step by step and parks it when it yields WaitForChange.
        /// </summary>
        private IEnumerator Drive(IEnumerator routine)
        {
            while (routine.MoveNext())
            {
                if (ReferenceEquals(routine.Current, WaitForChange))
                {
                    // Park: no per-frame cost until WakeUp() resumes it
                    parkedRoutine = routine;
                    yield break;
                }
                yield return routine.Current;
            }
        }

        /// <summary>
        /// Waits one frame, then continues the parked routine where it left off.
        /// </summary>
        private IEnumerator ResumeNextFrame()
        {
            yield return null;
            resumeRoutine = null;

            var routine = parkedRoutine;
            parkedRoutine = null;
            if (routine != null) moveRoutine = StartCoroutine(Drive(routine));
        }

        /// <summary>
        /// Coroutine that handles the movement of a parcel on this conveyor segment.
        /// The movement is divided into three phases:
        /// 1. Move from the input point to the center point.
        /// 2. Wait at the center point until a valid next conveyor is available and can accept the parcel.
        /// 3. Move from the center point to the appropriate output point and transfer the parcel to the next conveyor.
        /// </summary>
        /// <param name="parcel">The parcel to move.</param>
        /// <returns>IEnumerator for coroutine execution.</returns>
        protected virtual IEnumerator MoveParcelRoutine(Parcel parcel)
        {
            // Phase 1: Move to center
            Transform t = parcel.transform;
            while (Vector3.Distance(t.position, CenterPoint.position) > 0.01f)
            {
                t.position = Vector3.MoveTowards(t.position, CenterPoint.position, Speed * Time.deltaTime);
                yield return null;
            }

            // Wait until a valid next conveyor exists. If no downstream at all, stay at center.
            bool blocked = false;
            while (true)
            {
                // Occupancy rule: stay at center until a downstream segment can take the parcel
                var next = GetNextConveyor(parcel);
                if (next == null || !next.CanAcceptParcel())
                {
                    if (!blocked) RaiseParcelBlocked(parcel);
                    blocked = true;
                    yield return WaitForChange;
                    continue;
                }
                blocked = false;

                // Find output index leading to 'next'
                int outIdx = GetOutputIndexForConveyor(next);
                if (outIdx < 0)
                {
                    // fallback: try to move to first output point
                    outIdx = 0;
                }
                RaiseParcelRouted(parcel, outIdx);

                // Phase 2: move center -> exit point
                Transform exitPoint = (outIdx >= 0 && outIdx < OutputPoints.Length) ? OutputPoints[outIdx] : null;
                Vector3 exitPos = exitPoint ? exitPoint.position : CenterPoint.position + transform.forward * 0.5f;
                while (Vector3.Distance(t.position, exitPos) > 0.01f)
                {
                    t.position = Vector3.MoveTowards(t.position, exitPos, Speed * Time.deltaTime);
                    yield return null;
                }

                // Phase 3: transfer to next conveyor
                bool accepted = next.AcceptParcel(parcel);
                if (accepted)
                {
                    ReleaseParcel();
                    yield break;
                }
                else
                {
                    // If next suddenly became unavailable, snap back to center and retry
                    while (Vector3.Distance(t.position, CenterPoint.position) > 0.01f)
                    {
                        t.position = Vector3.MoveTowards(t.position, CenterPoint.position, Speed * Time.deltaTime);
                        yield return null;
                    }
                }
            }
        }

        /// <summary>
        /// Called by Unity to draw gizmos in the editor. Used here to visualize the conveyor layout, input/output points, and routing paths.
        /// </summary>
        // Gizmos for debugging
        protected virtual void OnDrawGizmos()
        {
            // Draw input -> center (green arrow)
            if (InputPoint && CenterPoint)
            {
                Gizmos.color = Color.green;
                Gizmos.DrawSphere(InputPoint.position, 0.05f);
                Gizmos.DrawSphere(CenterPoint.position, 0.06f);
                DrawArrow(InputPoint.position, CenterPoint.position, Color.green, 18f, 0.15f);
            }
            else
            {
                // still draw existing spheres if present
                Gizmos.color = Color.green;
                if (InputPoint) Gizmos.DrawSphere(InputPoint.position, 0.05f);
                Gizmos.color = Color.yellow;
                if (CenterPoint) Gizmos.DrawSphere(CenterPoint.position, 0.06f);
            }

            // Draw center -> outputs (dynamic color in play mode)
            if (OutputPoints != null && CenterPoint)
            {
                for (int i = 0; i < OutputPoints.Length; i++)
                {
                    var op = OutputPoints[i];
                    if (op == null) continue;

                    // Decide color: in Play mode check IsExitFree, otherwise default to red
                    Color lineColor = Color.red;
                    if (Application.isPlaying)
                    {
                        // If any downstream for this output is free -> yellow, otherwise red
                        bool free = IsExitFree(i);
                        lineColor = free ? Color.yellow : Color.red;
                    }
                    else
                    {
                        lineColor = Color.red;
                    }

                    // Draw output sphere and arrow
                    Gizmos.color = lineColor;
                    Gizmos.DrawSphere(op.position, 0.05f);
                    DrawArrow(CenterPoint.position, op.position, lineColor, 18f, 0.15f);

                    // Draw connector line for visual (thin)
                    Gizmos.color = lineColor;
                    Gizmos.DrawLine(CenterPoint.position, op.position);
                }
            }
        }

        /// <summary>
        /// Helper method to draw an arrow from 'from' to 'to' with the specified color and head parameters.
        /// Used in OnDrawGizmos to visualize movement directions and routing paths.
        /// The arrow consists of a main line and two head lines forming the arrowhead.
        /// The headAngle parameter controls the angle of the arrowhead lines relative to the main line, and headLength controls their length.
        /// </summary>
        /// <param name="from">Start position.</param>
        /// <param name="to">End position.</param>
        /// <param name="color">Color of the arrow.</param>
        /// <param name="headAngle">Angle of the arrow head lines.</param>
        /// <param name="headLength">Length of the arrow head lines.</param>
        private void DrawArrow(Vector3 from, Vector3 to, Color color, float headAngle = 20f, float headLength = 0.15f)
        {
            Gizmos.color = color;
            Gizmos.DrawLine(from, to);

            Vector3 dir = (to - from).normalized;
            if (dir.sqrMagnitude <= 0f) return;

            // Compute two head directions
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
