using UnityEngine;

namespace ConveyorSystem
{
    /// <summary>
    /// Represents a splitter conveyor segment that distributes parcels to multiple output paths in a round-robin fashion.
    /// </summary>
    public class SplitterConveyor : ConveyorBase
    {
        /// <summary>
        /// The index of the next output path to use for round-robin distribution.
        /// </summary>
        private int roundRobinIndex = 0;

        /// <summary>
        /// Resets the component to its default state.
        /// Sets the conveyor type to Splitter.
        /// </summary>
        private void Reset()
        {
            ConveyorType = ConveyorType.Splitter;
        }

        /// <summary>
        /// Determines the next conveyor to send the parcel to.
        /// Iterates through available output paths starting from the current round-robin index.
        /// If a valid path is found, updates the round-robin index for the next parcel.
        /// </summary>
        /// <param name="parcel">The parcel to route.</param>
        /// <returns>The next conveyor segment, or null if no valid path is found.</returns>
        public override ConveyorBase GetNextConveyor(Parcel parcel)
        {
            if (OutputPoints == null || OutputPoints.Length == 0) return null;

            int outputs = OutputPoints.Length;
            for (int i = 0; i < outputs; i++)
            {
                // Calculate the current output index based on round-robin logic
                int idx = (roundRobinIndex + i) % outputs;

                // Check all connected conveyors at this output index
                foreach (var c in NextConveyors[idx])
                {
                    if (c != null && c.CanAcceptParcel())
                    {
                        // Found a valid path, update the index for the next parcel
                        roundRobinIndex = (idx + 1) % outputs;
                        return c;
                    }
                }
            }

            return null;
        }
    }
}