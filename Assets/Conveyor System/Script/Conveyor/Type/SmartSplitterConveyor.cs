using UnityEngine;

namespace ConveyorSystem
{
    /// <summary>
    /// Represents a smart splitter conveyor segment that routes parcels based on their type.
    /// If the preferred route is unavailable, it falls back to round-robin distribution.
    /// </summary>
    public class SmartSplitterConveyor : SplitterConveyor
    {
        /// <summary>
        /// The default output index to use if no specific mapping is found for the parcel type.
        /// </summary>
        [Header("Smart Routing")]
        [Tooltip("Default output index if no specific mapping is found.")]
        public int DefaultOutputIndex = 0;

        /// <summary>
        /// Array mapping parcel types (by enum ordinal) to preferred output indices.
        /// </summary>
        [Tooltip("Map parcel types (by enum ordinal) to preferred output index.")]
        public int[] TypeToOutput;

        /// <summary>
        /// If true, a parcel waits for its preferred output instead of falling back to round-robin.
        /// </summary>
        [Tooltip("If enabled, parcels wait for their preferred output instead of falling back to round-robin.")]
        public bool StrictRouting = false;

        /// <summary>
        /// Resets the component to its default state.
        /// Sets the conveyor type to SmartSplitter.
        /// </summary>
        private void Reset()
        {
            ConveyorType = ConveyorType.SmartSplitter;
        }

        /// <summary>
        /// Determines the next conveyor to send the parcel to.
        /// First attempts to route based on the parcel type.
        /// If the preferred route is blocked or invalid, falls back to the base SplitterConveyor logic (round-robin).
        /// </summary>
        /// <param name="parcel">The parcel to route.</param>
        /// <returns>The next conveyor segment, or null if no valid path is found.</returns>
        public override ConveyorBase GetNextConveyor(Parcel parcel)
        {
            if (parcel == null) return base.GetNextConveyor(parcel);

            // Determine the preferred output index based on parcel type; unmapped types use DefaultOutputIndex
            int typeIndex = (int)parcel.Type;
            int preferred = (TypeToOutput != null && typeIndex >= 0 && typeIndex < TypeToOutput.Length) ? TypeToOutput[typeIndex] : DefaultOutputIndex;

            // Try the preferred output path first
            if (preferred >= 0 && preferred < NextConveyors.Length)
            {
                foreach (var c in NextConveyors[preferred])
                {
                    if (c != null && c.CanAcceptParcel())
                    {
                        return c;
                    }
                }
            }

            // In strict mode the parcel waits at the center until its preferred path frees up
            // (an invalid preferred index still falls back so the parcel cannot get stuck forever)
            if (StrictRouting && preferred >= 0 && preferred < NextConveyors.Length) return null;

            // Fallback to round-robin distribution if preferred path is unavailable
            return base.GetNextConveyor(parcel);
        }
    }
}