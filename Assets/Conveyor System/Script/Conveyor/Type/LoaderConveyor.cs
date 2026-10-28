using UnityEngine;
using UnityEngine.Serialization;

namespace ConveyorSystem
{
    /// <summary>
    /// Represents a loader conveyor segment that loads parcels into a storage system or consumes them.
    /// </summary>
    public class LoaderConveyor : ConveyorBase
    {
        /// <summary>
        /// Serialized target storage. Use the TargetStorage property at runtime.
        /// </summary>
        [Header("Storage")]
        [Tooltip("If specified, parcels will be placed in this storage when loaded.")]
        [SerializeField, FormerlySerializedAs("TargetStorage")]
        private ParcelStorage targetStorage;

        /// <summary>
        /// The storage this loader is currently listening to.
        /// </summary>
        private ParcelStorage subscribedStorage;

        /// <summary>
        /// The target storage where parcels will be placed.
        /// If null, parcels are simply consumed via their OnLoaded method.
        /// </summary>
        public ParcelStorage TargetStorage
        {
            get => targetStorage;
            set
            {
                targetStorage = value;
                Resubscribe();
                NotifyAvailabilityChanged();
                WakeUp();
            }
        }

        /// <summary>
        /// Resets the component to its default state.
        /// Sets the conveyor type to Loader.
        /// </summary>
        private void Reset()
        {
            ConveyorType = ConveyorType.Loader;
        }

        /// <summary>
        /// Starts listening to the target storage.
        /// </summary>
        protected override void OnEnable()
        {
            base.OnEnable();
            Resubscribe();
        }

        /// <summary>
        /// Stops listening to the target storage.
        /// </summary>
        private void OnDisable()
        {
            if (subscribedStorage != null) subscribedStorage.Changed -= OnStorageChanged;
            subscribedStorage = null;
        }

        /// <summary>
        /// Applies Inspector changes to the target storage made during play mode.
        /// </summary>
        private void OnValidate()
        {
            if (Application.isPlaying && isActiveAndEnabled && subscribedStorage != targetStorage) TargetStorage = targetStorage;
        }

        /// <summary>
        /// Moves the Changed subscription to the current target storage.
        /// </summary>
        private void Resubscribe()
        {
            if (subscribedStorage == targetStorage || !isActiveAndEnabled) return;
            if (subscribedStorage != null) subscribedStorage.Changed -= OnStorageChanged;
            subscribedStorage = targetStorage;
            if (subscribedStorage != null) subscribedStorage.Changed += OnStorageChanged;
        }

        /// <summary>
        /// Storage space changed: upstream may hand over again, and a parcel waiting at the exit may be stored.
        /// </summary>
        private void OnStorageChanged(ParcelStorage storage)
        {
            NotifyAvailabilityChanged();
            WakeUp();
        }

        /// <summary>
        /// Checks if the conveyor can accept a new parcel.
        /// Also refuses parcels while the target storage is full, so the line backs up instead of losing parcels.
        /// </summary>
        /// <returns>True if the conveyor is free and the storage has room, false otherwise.</returns>
        public override bool CanAcceptParcel()
        {
            return base.CanAcceptParcel() && (targetStorage == null || !targetStorage.IsFull);
        }

        /// <summary>
        /// Coroutine that handles the movement of a parcel on this loader conveyor.
        /// 1. Moves the parcel to the center point.
        /// 2. Moves the parcel to the exit point (or stays at center if no exit).
        /// 3. Stores the parcel in the TargetStorage, waiting (parked) while the storage is full.
        /// 4. If no storage is assigned, calls OnLoaded on the parcel to consume it.
        /// </summary>
        /// <param name="parcel">The parcel to move.</param>
        /// <returns>IEnumerator for coroutine execution.</returns>
        protected override System.Collections.IEnumerator MoveParcelRoutine(Parcel parcel)
        {
            Transform t = parcel.transform;

            // Phase 1: Move to center
            while (Vector3.Distance(t.position, CenterPoint.position) > 0.01f)
            {
                t.position = Vector3.MoveTowards(t.position, CenterPoint.position, Speed * Time.deltaTime);
                yield return null;
            }

            // Phase 2: Move to exit (use first output if exists, else stay at center)
            Transform exit = (OutputPoints != null && OutputPoints.Length > 0) ? OutputPoints[0] : CenterPoint;
            while (Vector3.Distance(t.position, exit.position) > 0.01f)
            {
                t.position = Vector3.MoveTowards(t.position, exit.position, Speed * Time.deltaTime);
                yield return null;
            }

            // Phase 3: Load into storage. If it filled up meanwhile (e.g. shared by several loaders),
            // wait at the exit until there is room instead of losing the parcel. Woken by storage changes.
            bool blocked = false;
            while (targetStorage != null && !targetStorage.StoreParcel(parcel))
            {
                if (!blocked) RaiseParcelBlocked(parcel);
                blocked = true;
                yield return WaitForChange;
            }

            if (targetStorage == null)
            {
                // No storage assigned: "Load" the parcel (consume it)
                parcel.OnLoaded();
            }

            ReleaseParcel();
        }
    }
}
