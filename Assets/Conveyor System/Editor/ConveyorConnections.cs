using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace ConveyorSystem.Editor
{
    /// <summary>
    /// Computes, in edit mode, which output triggers will connect to which input triggers at runtime.
    /// Mirrors the runtime rule: an output trigger connects to every input trigger of another conveyor it overlaps.
    /// </summary>
    [InitializeOnLoad]
    public static class ConveyorConnections
    {
        /// <summary>
        /// State of one output trigger.
        /// </summary>
        public enum LinkState
        {
            /// <summary>Overlaps an input trigger: will connect at runtime.</summary>
            Connected,
            /// <summary>An input trigger is within snap distance but does not overlap: will NOT connect.</summary>
            NearMiss,
            /// <summary>Nothing nearby: the exit is open.</summary>
            Open
        }

        /// <summary>
        /// One output trigger and what it connects to.
        /// </summary>
        public struct Link
        {
            public ConveyorBase From;
            public ConveyorTrigger FromTrigger;
            public ConveyorBase To;
            public ConveyorTrigger ToTrigger;
            public LinkState State;
        }

        private static readonly List<ConveyorTrigger> cachedTriggers = new List<ConveyorTrigger>();
        private static bool cacheDirty = true;

        static ConveyorConnections()
        {
            EditorApplication.hierarchyChanged += () => cacheDirty = true;
        }

        /// <summary>
        /// Returns one or more links per output trigger in the loaded scenes.
        /// </summary>
        public static List<Link> Collect()
        {
            if (cacheDirty)
            {
                cachedTriggers.Clear();
                cachedTriggers.AddRange(SceneQuery.FindAll<ConveyorTrigger>());
                cacheDirty = false;
            }

            var inputs = new List<(ConveyorTrigger trigger, ConveyorBase owner, Bounds bounds)>();
            var outputs = new List<(ConveyorTrigger trigger, ConveyorBase owner, Bounds bounds)>();
            foreach (var t in cachedTriggers)
            {
                if (t == null || !t.isActiveAndEnabled) continue;
                var col = t.GetComponent<Collider>();
                var owner = t.GetComponentInParent<ConveyorBase>();
                if (col == null || owner == null) continue;

                var entry = (t, owner, GetWorldBounds(col));
                if (t.IsOutput) outputs.Add(entry);
                else inputs.Add(entry);
            }

            float snapDistance = ConveyorEditorSettings.SnapDistance;
            var links = new List<Link>();
            foreach (var o in outputs)
            {
                bool connected = false;
                float nearest = snapDistance;
                (ConveyorTrigger trigger, ConveyorBase owner, Bounds bounds) nearMiss = default;

                foreach (var i in inputs)
                {
                    if (i.owner == o.owner) continue;

                    if (o.bounds.Intersects(i.bounds))
                    {
                        connected = true;
                        links.Add(new Link { From = o.owner, FromTrigger = o.trigger, To = i.owner, ToTrigger = i.trigger, State = LinkState.Connected });
                        continue;
                    }

                    float d = Vector3.Distance(o.bounds.center, i.bounds.center);
                    if (d < nearest)
                    {
                        nearest = d;
                        nearMiss = i;
                    }
                }

                if (connected) continue;

                links.Add(nearMiss.trigger != null
                    ? new Link { From = o.owner, FromTrigger = o.trigger, To = nearMiss.owner, ToTrigger = nearMiss.trigger, State = LinkState.NearMiss }
                    : new Link { From = o.owner, FromTrigger = o.trigger, State = LinkState.Open });
            }

            return links;
        }

        /// <summary>
        /// World-space AABB of a collider computed from its transform, so it is correct in edit mode
        /// even before the physics scene has been synced.
        /// </summary>
        public static Bounds GetWorldBounds(Collider col)
        {
            var t = col.transform;
            switch (col)
            {
                case BoxCollider box:
                {
                    Vector3 half = box.size * 0.5f;
                    var b = new Bounds(t.TransformPoint(box.center), Vector3.zero);
                    for (int x = -1; x <= 1; x += 2)
                    for (int y = -1; y <= 1; y += 2)
                    for (int z = -1; z <= 1; z += 2)
                        b.Encapsulate(t.TransformPoint(box.center + Vector3.Scale(half, new Vector3(x, y, z))));
                    return b;
                }
                case SphereCollider sphere:
                {
                    Vector3 s = t.lossyScale;
                    float r = sphere.radius * Mathf.Max(Mathf.Abs(s.x), Mathf.Abs(s.y), Mathf.Abs(s.z));
                    return new Bounds(t.TransformPoint(sphere.center), Vector3.one * (r * 2f));
                }
                default:
                    return col.bounds;
            }
        }
    }
}
