using UnityEngine;

namespace ConveyorSystem
{
    /// <summary>
    /// Represents a parcel that can be carried, placed on conveyors, or fall to the ground.
    /// </summary>
    public class Parcel : MonoBehaviour
    {
        /// <summary>
        /// Physical size category of this parcel.
        /// </summary>
        [Tooltip("Physical size category of this parcel")]
        public ParcelSize Size;

        /// <summary>
        /// Weight of the parcel in kilograms.
        /// </summary>
        [Tooltip("Weight of the parcel in kilograms")]
        public float Weight;

        /// <summary>
        /// Type/category of the parcel for sorting purposes.
        /// </summary>
        [Tooltip("Type/category of the parcel for sorting purposes")]
        public ParcelType Type;

        /// <summary>
        /// The conveyor currently holding the parcel. Null if not on a conveyor.
        /// </summary>
        [HideInInspector] public ConveyorBase CurrentConveyor;

        /// <summary>
        /// Indicates whether the parcel is currently being carried by a player or mechanism.
        /// </summary>
        [HideInInspector] public bool IsCarried;

        /// <summary>
        /// Layer mask defining what surfaces the parcel can land on.
        /// </summary>
        [Header("Ground Detection")]
        [Tooltip("Layer mask defining what surfaces the parcel can land on")]
        public LayerMask GroundLayers = ~0;

        /// <summary>
        /// Extra raycast distance beyond the parcel bounds for ground detection.
        /// </summary>
        [Tooltip("Extra raycast distance beyond the parcel bounds for ground detection")]
        public float GroundRaycastExtra = 0.1f;

        /// <summary>
        /// Radius of the sphere used for ground detection.
        /// </summary>
        [Tooltip("Radius of the sphere used for ground detection")]
        public float GroundCheckRadius = 0.08f;

        private bool isFalling = false;

        /// <summary>
        /// Optional Rigidbody. When present, falling is driven by physics; otherwise by a simple manual gravity.
        /// </summary>
        private Rigidbody rb;

        /// <summary>
        /// Vertical speed used by the manual gravity fallback (parcels without a Rigidbody).
        /// </summary>
        private float fallVelocity;

        /// <summary>
        /// True once the parcel has landed; ground checks stop until something can change.
        /// </summary>
        private bool resting;

        /// <summary>
        /// Busy conveyor the resting parcel waits for, or null.
        /// </summary>
        private ConveyorBase waitingFor;

        /// <summary>
        /// Caches the optional Rigidbody.
        /// </summary>
        private void Awake()
        {
            rb = GetComponent<Rigidbody>();
        }

        /// <summary>
        /// Enables or disables physics simulation for this parcel.
        /// Disabled (kinematic) while on a conveyor or carried; enabled when dropped. Safe without a Rigidbody.
        /// </summary>
        /// <param name="enabled">True to let physics move the parcel, false to make it kinematic.</param>
        public void SetPhysicsEnabled(bool enabled)
        {
            fallVelocity = 0f;
            if (rb == null) return;

            if (!enabled && !rb.isKinematic)
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
            rb.isKinematic = !enabled;
        }

        /// <summary>
        /// Called once per frame to handle falling logic when not carried or on a conveyor.
        /// </summary>
        private void Update()
        {
            if (IsCarried || CurrentConveyor != null)
            {
                if (isFalling) StopFalling();
                return;
            }

            // A resting parcel does no ground checks until physics moves it again
            // or the conveyor it waits for reports a change (see OnWaitedConveyorChanged)
            if (resting && (rb == null || rb.isKinematic || rb.IsSleeping())) return;

            SimulateFalling();
        }

        /// <summary>
        /// Stops waiting for a conveyor when the parcel is disabled (e.g. stored).
        /// </summary>
        private void OnDisable()
        {
            StopResting();
        }

        /// <summary>
        /// Simulates falling behavior by checking for ground contact.
        /// If a conveyor is detected below, attempts to place the parcel on it.
        /// </summary>
        private void SimulateFalling()
        {
            isFalling = true;

            float rayDist = GroundRaycastExtra + GroundCheckRadius;
            Vector3 rayOrigin = transform.position + Vector3.up * GroundCheckRadius;

            if (Physics.SphereCast(rayOrigin, GroundCheckRadius, Vector3.down, out RaycastHit hit, rayDist, GroundLayers, QueryTriggerInteraction.Collide))
            {
                var trigger = hit.collider.GetComponentInParent<ConveyorTrigger>();
                if (trigger != null && !trigger.IsOutput)
                {
                    var conv = trigger.GetComponentInParent<ConveyorBase>();
                    if (conv != null && conv.CanAcceptParcel())
                    {
                        StopResting();
                        transform.SetParent(conv.transform, true);
                        if (conv.InputPoint != null) transform.position = conv.InputPoint.position;
                        isFalling = false;
                        conv.AcceptParcel(this);
                        return;
                    }

                    // Landed on a busy conveyor entry: wait for it to free up instead of re-checking every frame
                    Rest(hit.collider.transform, conv);
                    return;
                }

                Rest(hit.collider.transform, null);
                return;
            }

            // Physics drives the fall when a non-kinematic Rigidbody is present; otherwise apply simple gravity.
            if (rb == null || rb.isKinematic)
            {
                fallVelocity += Physics.gravity.y * Time.deltaTime;
                transform.position += Vector3.up * (fallVelocity * Time.deltaTime);
            }
        }

        /// <summary>
        /// Stops the falling simulation and resets relevant state when the parcel is picked up or placed on a conveyor.
        /// </summary>
        private void StopFalling()
        {
            isFalling = false;
        }

        /// <summary>
        /// Marks the parcel as landed on the given surface, optionally waiting for a busy conveyor.
        /// </summary>
        private void Rest(Transform surface, ConveyorBase conveyor)
        {
            StopResting();
            isFalling = false;
            fallVelocity = 0f;
            resting = true;
            if (transform.parent != surface) transform.SetParent(surface, true);

            if (conveyor != null)
            {
                waitingFor = conveyor;
                waitingFor.AvailabilityChanged += OnWaitedConveyorChanged;
            }
        }

        /// <summary>
        /// Leaves the resting state and stops waiting for a conveyor.
        /// </summary>
        private void StopResting()
        {
            if (waitingFor != null) waitingFor.AvailabilityChanged -= OnWaitedConveyorChanged;
            waitingFor = null;
            resting = false;
        }

        /// <summary>
        /// The conveyor below may have freed up: re-check on the next Update (deferred, never inside its routine).
        /// </summary>
        private void OnWaitedConveyorChanged(ConveyorBase conveyor)
        {
            if (conveyor.CanAcceptParcel()) StopResting();
        }

        /// <summary>
        /// Called to pick up the parcel by a carrier (e.g. player or robotic arm).
        /// </summary>
        /// <param name="carrier">The transform of the carrier.</param>
        public void PickUp(Transform carrier)
        {
            // Leave the conveyor cleanly so it does not keep moving a parcel it no longer owns
            if (CurrentConveyor != null) CurrentConveyor.RemoveParcel(this);

            StopResting();
            IsCarried = true;
            CurrentConveyor = null;
            isFalling = false;
            SetPhysicsEnabled(false);
            transform.SetParent(carrier, true);
        }

        /// <summary>
        /// Attempts to place the parcel onto a conveyor. If successful, the parcel becomes a child of the conveyor and is positioned at the input point.
        /// </summary>
        /// <param name="conveyor">The target conveyor.</param>
        /// <returns>True if the parcel was successfully placed, false otherwise.</returns>
        public bool PlaceOnConveyor(ConveyorBase conveyor)
        {
            if (conveyor == null || !conveyor.CanAcceptParcel()) return false;

            StopResting();
            transform.SetParent(conveyor.transform, true);
            if (conveyor.InputPoint != null) transform.position = conveyor.InputPoint.position;
            else if (conveyor.CenterPoint != null) transform.position = conveyor.CenterPoint.position;

            isFalling = false;
            IsCarried = false;

            bool accepted = conveyor.AcceptParcel(this);
            if (!accepted)
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Called when the parcel is stored in a ParcelStorage. Deactivates the parcel to hide it from the scene.
        /// </summary>
        public void OnLoaded()
        {
            gameObject.SetActive(false);
        }

        /// <summary>
        /// Called when the parcel is retrieved from a ParcelStorage. Activates the parcel to make it visible in the scene.
        /// </summary>
        public void StartFalling()
        {
            StopResting();
            transform.SetParent(null, true);
            CurrentConveyor = null;
            IsCarried = false;
            isFalling = true;
            SetPhysicsEnabled(true);
        }
    }
}