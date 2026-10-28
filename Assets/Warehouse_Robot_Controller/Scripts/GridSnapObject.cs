using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Warcool.WarehouseRobot
{
    [ExecuteAlways]
    public class GridSnapObject : MonoBehaviour
    {
        [Header("Grid")]
        public bool snapInEditor = true;

        [Tooltip("Grid step in meters. Should match the robot grid point step.")]
        public float gridStep = 0.25f;

        [Tooltip("Rotation step in degrees.")]
        public float rotationStep = 45f;

        [Header("Snap Options")]
        public bool snapPosition = true;
        public bool snapRotation = true;
        public bool keepYPosition = true;

        [Header("Runtime")]
        public bool snapOnStart = true;

        private void Start()
        {
            if (Application.isPlaying && snapOnStart)
                Snap();
        }

    #if UNITY_EDITOR
        private void Update()
        {
            if (Application.isPlaying)
                return;

            if (!snapInEditor)
                return;

            Snap();
        }
    #endif

        [ContextMenu("Snap Now")]
        public void Snap()
        {
            Vector3 position = transform.position;

            if (snapPosition && gridStep > 0.0001f)
            {
                position.x = Mathf.Round(position.x / gridStep) * gridStep;
                position.z = Mathf.Round(position.z / gridStep) * gridStep;

                if (!keepYPosition)
                    position.y = Mathf.Round(position.y / gridStep) * gridStep;

                transform.position = position;
            }

            if (snapRotation && rotationStep > 0.0001f)
            {
                Vector3 euler = transform.eulerAngles;
                euler.y = Mathf.Round(euler.y / rotationStep) * rotationStep;
                transform.eulerAngles = euler;
            }
        }
    }
}
