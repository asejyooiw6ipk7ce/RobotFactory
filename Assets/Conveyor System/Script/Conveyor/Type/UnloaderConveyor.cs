using System.Collections;
using UnityEngine;
using UnityEngine.Serialization;

namespace ConveyorSystem
{
    /// <summary>
    /// Represents an unloader conveyor segment that retrieves parcels from a storage system and places them onto the conveyor.
    /// Event-driven: it tries to unload when it becomes free or when the storage changes, never per frame.
    /// </summary>
    public class UnloaderConveyor : ConveyorBase
    {
        /// <summary>
        /// Serialized source storage. Use the ParcelStorage property at runtime.
        /// </summary>
        [Header("Unloader")]
        [Tooltip("Assign a ParcelStorage component whose children represent stored parcels.")]
        [SerializeField, FormerlySerializedAs("ParcelStorage")]
        private ParcelStorage parcelStorage;

        /// <summary>
        /// The minimum time (in seconds) between two unloaded parcels.
        /// </summary>
        [Tooltip("Minimum time between two unloaded parcels.")]
        public float UnloadInterval = 0.2f;

        /// <summary>
        /// The storage this unloader is currently listening to.
        /// </summary>
        private ParcelStorage subscribedStorage;

        /// <summary>
        /// Pending unload attempt, or null.
        /// </summary>
        private Coroutine scheduledUnload;

        /// <summary>
        /// Time of the last successful unload.
        /// </summary>
        private float lastUnloadTime = float.NegativeInfinity;

        /// <summary>
        /// The storage component from which parcels will be retrieved.
        /// </summary>
        public ParcelStorage ParcelStorage
        {
            get => parcelStorage;
            set
            {
                parcelStorage = value;
                Resubscribe();
                RequestUnload();
            }
        }

        /// <summary>
        /// Resets the component to its default state.
        /// Sets the conveyor type to Unloader.
        /// </summary>
        private void Reset()
        {
            ConveyorType = ConveyorType.Unloader;
        }

        /// <summary>
        /// Starts listening to the storage and tries a first unload.
        /// </summary>
        protected override void OnEnable()
        {
            base.OnEnable();
            Resubscribe();
            RequestUnload();
        }

        /// <summary>
        /// Stops listening to the storage and cancels a pending unload.
        /// </summary>
        private void OnDisable()
        {
            if (subscribedStorage != null) subscribedStorage.Changed -= OnStorageChanged;
            subscribedStorage = null;

            if (scheduledUnload != null) StopCoroutine(scheduledUnload);
            scheduledUnload = null;
        }

        /// <summary>
        /// Applies Inspector changes to the storage made during play mode.
        /// </summary>
        private void OnValidate()
        {
            if (Application.isPlaying && isActiveAndEnabled && subscribedStorage != parcelStorage) ParcelStorage = parcelStorage;
        }

        /// <summary>
        /// Releases the current parcel and immediately schedules the next unload.
        /// </summary>
        public override void ReleaseParcel()
        {
            base.ReleaseParcel();
            RequestUnload();
        }

        /// <summary>
        /// Moves the Changed subscription to the current storage.
        /// </summary>
        private void Resubscribe()
        {
            if (subscribedStorage == parcelStorage || !isActiveAndEnabled) return;
            if (subscribedStorage != null) subscribedStorage.Changed -= OnStorageChanged;
            subscribedStorage = parcelStorage;
            if (subscribedStorage != null) subscribedStorage.Changed += OnStorageChanged;
        }

        /// <summary>
        /// Storage content changed: a parcel may be available now.
        /// </summary>
        private void OnStorageChanged(ParcelStorage storage)
        {
            RequestUnload();
        }

        /// <summary>
        /// Schedules an unload attempt, respecting UnloadInterval. Repeated requests are merged.
        /// </summary>
        public void RequestUnload()
        {
            if (scheduledUnload != null || !isActiveAndEnabled) return;
            if (!CanAcceptParcel() || parcelStorage == null || parcelStorage.Count == 0) return;
            scheduledUnload = StartCoroutine(UnloadWhenReady());
        }

        /// <summary>
        /// Waits for the remaining interval, then unloads one parcel.
        /// </summary>
        private IEnumerator UnloadWhenReady()
        {
            float wait = lastUnloadTime + UnloadInterval - Time.time;
            if (wait > 0f) yield return new WaitForSeconds(wait);
            else yield return null; // never hand over inside another segment's routine

            // Storage changes raised while unloading must not schedule another attempt
            if (TryUnloadFromStorage()) lastUnloadTime = Time.time;
            scheduledUnload = null;
        }

        /// <summary>
        /// Attempts to retrieve a parcel from the assigned storage and place it on this conveyor.
        /// </summary>
        /// <returns>True if a parcel was successfully unloaded and accepted by the conveyor, false otherwise.</returns>
        private bool TryUnloadFromStorage()
        {
            if (!CanAcceptParcel() || parcelStorage == null) return false;

            // Use the ParcelStorage API to retrieve a stored parcel (already detached and activated)
            var parcel = parcelStorage.RetrieveParcel();
            if (parcel == null) return false;

            // Move to input point, aligned with the conveyor
            parcel.transform.SetParent(transform, true);
            parcel.transform.SetPositionAndRotation(InputPoint.position, InputPoint.rotation);

            // Try accept on this conveyor
            if (AcceptParcel(parcel))
            {
                return true;
            }

            // If accept failed, put the parcel back so it is not orphaned
            if (!parcelStorage.StoreParcel(parcel))
            {
                Debug.LogWarning($"UnloaderConveyor [{gameObject.name}]: could not return parcel to storage, dropping it", this);
                parcel.StartFalling();
            }
            return false;
        }
    }
}
