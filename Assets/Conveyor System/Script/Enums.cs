namespace ConveyorSystem
{
    /// <summary>
    /// Defines the different types of conveyor belt segments available in the system.
    /// Each type represents a unique conveyor configuration with specific behavior.
    /// </summary>
    public enum ConveyorType
    {
        Straight,
        Corner,
        Splitter,
        SmartSplitter,
        Joiner,
        MultiJoiner,
        Pauser,
        Loader,
        Unloader
    }

    /// <summary>
    /// Defines size categories for parcels, which can be used for sorting, conveyor compatibility, or visual representation.
    /// </summary>
    public enum ParcelSize
    {
        Small,
        Medium,
        Large
    }

    /// <summary>
    /// Defines type categories for parcels, which can be used for sorting, conveyor routing, or special handling logic.
    /// </summary>
    public enum ParcelType
    {
        Generic,
        Fragile,
        Heavy,
        Perishable
    }

    /// <summary>
    /// Defines cardinal directions for conveyor orientation and routing logic.
    /// </summary>
    public enum Direction
    {
        North,
        East,
        South,
        West
    }
}