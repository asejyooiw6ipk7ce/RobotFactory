using UnityEngine;

namespace ConveyorSystem.Editor
{
    /// <summary>
    /// Version-safe scene object lookup (the FindObjectsSortMode overloads are deprecated from Unity 6.4).
    /// </summary>
    internal static class SceneQuery
    {
        /// <summary>
        /// Returns all loaded objects of type T, in no particular order.
        /// </summary>
        public static T[] FindAll<T>(bool includeInactive = false) where T : Object
        {
            var inactive = includeInactive ? FindObjectsInactive.Include : FindObjectsInactive.Exclude;
#if UNITY_6000_4_OR_NEWER
            return Object.FindObjectsByType<T>(inactive);
#else
            return Object.FindObjectsByType<T>(inactive, FindObjectsSortMode.None);
#endif
        }
    }
}
