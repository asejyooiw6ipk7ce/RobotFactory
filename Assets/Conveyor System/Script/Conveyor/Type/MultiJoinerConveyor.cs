using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ConveyorSystem
{
    /// <summary>
    /// Represents a multi-joiner conveyor segment that can accept parcels from multiple sources simultaneously.
    /// Parcels are queued and processed one by one in a FIFO (First-In-First-Out) manner.
    /// </summary>
    public class MultiJoinerConveyor : JoinerConveyor
    {
        /// <summary>
        /// Maximum number of parcels held by this segment, including the one in transit (0 = unlimited).
        /// </summary>
        [Header("Queue")]
        [Tooltip("Maximum number of parcels held by this segment, including the one in transit (0 = unlimited).")]
        [Min(0)]
        public int QueueCapacity = 4;

        /// <summary>
        /// Queue to hold incoming parcels waiting to be processed.
        /// </summary>
        private readonly Queue<Parcel> queue = new Queue<Parcel>();

        /// <summary>
        /// Pending start of the next queued parcel, or null.
        /// </summary>
        private Coroutine startNextRoutine;

        /// <summary>
        /// Gets the number of parcels waiting in the queue (excluding the one in transit).
        /// </summary>
        public int QueuedCount => queue.Count;

        /// <summary>
        /// Resets the component to its default state.
        /// Sets the conveyor type to MultiJoiner.
        /// </summary>
        private void Reset()
        {
            ConveyorType = ConveyorType.MultiJoiner;
        }

        /// <summary>
        /// Checks if the conveyor can accept a new parcel.
        /// Unlike the base implementation, occupancy does not block: parcels are accepted while the queue has room.
        /// </summary>
        /// <returns>True if the queue has room, false otherwise.</returns>
        public override bool CanAcceptParcel()
        {
            if (QueueCapacity <= 0) return true;
            int held = queue.Count + (CurrentParcel != null ? 1 : 0);
            return held < QueueCapacity;
        }

        /// <summary>
        /// Accepts a parcel onto the conveyor by adding it to the queue,
        /// regardless of whether a parcel is currently in transit.
        /// </summary>
        /// <param name="parcel">The parcel to accept.</param>
        /// <returns>True if the parcel was queued, false if it is null or the queue is full.</returns>
        public override bool AcceptParcel(Parcel parcel)
        {
            if (this == null || parcel == null || !CanAcceptParcel()) return false;

            // Enqueue regardless of current occupancy — will process FIFO
            queue.Enqueue(parcel);
            parcel.SetPhysicsEnabled(false);
            parcel.transform.SetParent(transform, true);
            parcel.CurrentConveyor = this;

            RaiseParcelEntered(parcel);
            NotifyAvailabilityChanged();

            // Start right away if nothing is in transit
            if (CurrentParcel == null && startNextRoutine == null) StartNext();
            return true;
        }

        /// <summary>
        /// Releases the parcel in transit and schedules the next queued parcel.
        /// </summary>
        public override void ReleaseParcel()
        {
            base.ReleaseParcel();

            // Deferred by one frame: this runs inside the movement routine that just finished
            if (queue.Count > 0 && startNextRoutine == null && isActiveAndEnabled)
                startNextRoutine = StartCoroutine(StartNextDeferred());
        }

        /// <summary>
        /// Removes a parcel taken off this conveyor externally, whether it is in transit or still queued.
        /// </summary>
        /// <param name="parcel">The parcel being removed.</param>
        public override void RemoveParcel(Parcel parcel)
        {
            if (parcel == null) return;

            if (CurrentParcel == parcel)
            {
                base.RemoveParcel(parcel);
                return;
            }

            if (!queue.Contains(parcel)) return;
            var remaining = new List<Parcel>(queue);
            remaining.Remove(parcel);
            queue.Clear();
            foreach (var p in remaining) queue.Enqueue(p);

            RaiseParcelExited(parcel);
            NotifyAvailabilityChanged();
        }

        /// <summary>
        /// Waits one frame, then starts the next queued parcel.
        /// </summary>
        private IEnumerator StartNextDeferred()
        {
            yield return null;
            startNextRoutine = null;
            if (CurrentParcel == null) StartNext();
        }

        /// <summary>
        /// Dequeues the next parcel and starts moving it through the segment.
        /// </summary>
        private void StartNext()
        {
            while (queue.Count > 0)
            {
                // Skip parcels destroyed while waiting
                Parcel p = queue.Dequeue();
                if (p == null) continue;

                // Place at input position, then start the shared movement logic
                p.transform.position = InputPoint.position;
                BeginTransport(p, raiseEntered: false);
                return;
            }
        }
    }
}
