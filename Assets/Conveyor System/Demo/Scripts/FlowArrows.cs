using System.Collections.Generic;
using UnityEngine;

namespace ConveyorSystem.Demo
{
    /// <summary>
    /// Flat direction arrows above a conveyor: input -> center (in) and center -> each output (out).
    /// Created at runtime as children of the module, so they rotate and move with it.
    /// </summary>
    public static class FlowArrows
    {
        /// <summary>
        /// Name of the child object that holds the arrows.
        /// </summary>
        public const string RootName = "Flow Arrows";

        private static Mesh arrowMesh;

        /// <summary>
        /// Adds arrows for the given conveyor under target (usually the conveyor itself, or a render-only copy).
        /// </summary>
        /// <returns>The object holding the arrows.</returns>
        public static GameObject Add(GameObject target, ConveyorBase conveyor, Material inMaterial, Material outMaterial, float height = 0.56f)
        {
            var root = new GameObject(RootName);
            root.transform.SetParent(target.transform, false);
            if (conveyor == null || conveyor.CenterPoint == null) return root;

            Vector3 center = target.transform.InverseTransformPoint(conveyor.CenterPoint.position);

            var inputs = new List<Transform>();
            if (conveyor is JoinerConveyor joiner && joiner.InputPoints != null && joiner.InputPoints.Length > 0) inputs.AddRange(joiner.InputPoints);
            else if (conveyor.InputPoint != null) inputs.Add(conveyor.InputPoint);

            foreach (var input in inputs)
                if (input != null) CreateArrow(root.transform, target.transform.InverseTransformPoint(input.position), center, inMaterial, height);

            if (conveyor.OutputPoints != null)
                foreach (var output in conveyor.OutputPoints)
                    if (output != null) CreateArrow(root.transform, center, target.transform.InverseTransformPoint(output.position), outMaterial, height);

            return root;
        }

        private static void CreateArrow(Transform parent, Vector3 from, Vector3 to, Material material, float height)
        {
            from.y = to.y = 0f;
            Vector3 dir = to - from;
            if (dir.sqrMagnitude < 1e-4f || material == null) return;

            // Leave a small gap at both ends so consecutive arrows read as separate steps
            float length = dir.magnitude * 0.8f;
            var go = new GameObject("Arrow");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = from + dir * 0.1f + Vector3.up * height;
            go.transform.localRotation = Quaternion.LookRotation(dir.normalized, Vector3.up);
            go.transform.localScale = new Vector3(1f, 1f, length);

            go.AddComponent<MeshFilter>().sharedMesh = GetMesh();
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
        }

        /// <summary>
        /// Flat arrow in the XZ plane pointing along +Z, from z = 0 to z = 1, visible from both sides.
        /// </summary>
        private static Mesh GetMesh()
        {
            if (arrowMesh != null) return arrowMesh;

            const float shaft = 0.07f, head = 0.2f, neck = 0.55f;
            var v = new[]
            {
                new Vector3(-shaft, 0, 0), new Vector3(shaft, 0, 0), new Vector3(shaft, 0, neck), new Vector3(-shaft, 0, neck),
                new Vector3(-head, 0, neck), new Vector3(head, 0, neck), new Vector3(0, 0, 1f)
            };
            var t = new[] { 0, 3, 2, 0, 2, 1, 4, 6, 5 };
            var both = new int[t.Length * 2];
            for (int i = 0; i < t.Length; i += 3)
            {
                both[i] = t[i]; both[i + 1] = t[i + 1]; both[i + 2] = t[i + 2];
                both[t.Length + i] = t[i]; both[t.Length + i + 1] = t[i + 2]; both[t.Length + i + 2] = t[i + 1];
            }

            arrowMesh = new Mesh { name = "Flow Arrow", vertices = v, triangles = both };
            arrowMesh.RecalculateNormals();
            arrowMesh.RecalculateBounds();
            return arrowMesh;
        }
    }
}
