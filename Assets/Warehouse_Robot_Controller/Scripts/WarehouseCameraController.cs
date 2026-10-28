using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Warcool.WarehouseRobot
{
    public class WarehouseCameraController : MonoBehaviour
    {
        [Header("References")]
        public Transform target;
        public Camera targetCamera;

        [Header("Screen Zones")]
        [Range(0.05f, 0.9f)]
        public float deadZoneWidth = 0.45f;

        [Range(0.05f, 0.9f)]
        public float deadZoneHeight = 0.45f;

        [Range(0.05f, 1f)]
        public float softZoneWidth = 0.75f;

        [Range(0.05f, 1f)]
        public float softZoneHeight = 0.75f;

        [Header("Movement")]
        public float followSpeed = 6f;
        public float edgePanSpeed = 5f;
        public bool followX = true;
        public bool followZ = true;

        [Header("Bounds")]
        public bool useBounds;
        public Vector2 minXZ = new Vector2(-10f, -10f);
        public Vector2 maxXZ = new Vector2(10f, 10f);

        [Header("Zoom")]
        public bool enableZoom = true;
        public float zoomSpeed = 2f;
        public float minOrthographicSize = 4f;
        public float maxOrthographicSize = 12f;

        private Vector3 desiredPosition;

        private void Awake()
        {
            desiredPosition = transform.position;

            if (targetCamera == null)
                targetCamera = GetComponent<Camera>();
        }

        private void LateUpdate()
        {
            if (target == null)
                return;

            Camera cam = targetCamera != null ? targetCamera : Camera.main;

            if (cam == null)
                return;

            HandleZoom(cam);
            UpdateCameraPosition(cam);
        }

        private void HandleZoom(Camera cam)
        {
            if (!enableZoom || !cam.orthographic)
                return;

    #if UNITY_EDITOR
            if (!IsMouseOverGameView())
                return;
    #endif

            float scroll = Input.mouseScrollDelta.y;

            if (Mathf.Abs(scroll) < 0.001f)
                return;

            cam.orthographicSize -= scroll * zoomSpeed;
            cam.orthographicSize = Mathf.Clamp(
                cam.orthographicSize,
                minOrthographicSize,
                maxOrthographicSize
            );
        }

        private void UpdateCameraPosition(Camera cam)
        {
            Vector3 viewport = cam.WorldToViewportPoint(target.position);
            Vector2 viewportPan = GetViewportPan(viewport);

            if (viewportPan.sqrMagnitude > 0.000001f)
            {
                Vector3 worldPan = ViewportOffsetToWorldOffset(cam, viewportPan);

                if (!followX)
                    worldPan.x = 0f;

                if (!followZ)
                    worldPan.z = 0f;

                desiredPosition += worldPan * edgePanSpeed * Time.deltaTime;
                desiredPosition.y = transform.position.y;
            }

            if (useBounds)
            {
                desiredPosition.x = Mathf.Clamp(desiredPosition.x, minXZ.x, maxXZ.x);
                desiredPosition.z = Mathf.Clamp(desiredPosition.z, minXZ.y, maxXZ.y);
            }

            float lerpFactor = 1f - Mathf.Exp(-followSpeed * Time.deltaTime);

            transform.position = Vector3.Lerp(
                transform.position,
                desiredPosition,
                lerpFactor
            );
        }

        private Vector2 GetViewportPan(Vector3 viewport)
        {
            Vector2 pan = Vector2.zero;

            float deadMinX = 0.5f - deadZoneWidth * 0.5f;
            float deadMaxX = 0.5f + deadZoneWidth * 0.5f;
            float deadMinY = 0.5f - deadZoneHeight * 0.5f;
            float deadMaxY = 0.5f + deadZoneHeight * 0.5f;

            float softMinX = 0.5f - softZoneWidth * 0.5f;
            float softMaxX = 0.5f + softZoneWidth * 0.5f;
            float softMinY = 0.5f - softZoneHeight * 0.5f;
            float softMaxY = 0.5f + softZoneHeight * 0.5f;

            if (viewport.x < deadMinX)
            {
                float strength = Mathf.InverseLerp(deadMinX, softMinX, viewport.x);
                pan.x = -Smooth01(strength);
            }
            else if (viewport.x > deadMaxX)
            {
                float strength = Mathf.InverseLerp(deadMaxX, softMaxX, viewport.x);
                pan.x = Smooth01(strength);
            }

            if (viewport.y < deadMinY)
            {
                float strength = Mathf.InverseLerp(deadMinY, softMinY, viewport.y);
                pan.y = -Smooth01(strength);
            }
            else if (viewport.y > deadMaxY)
            {
                float strength = Mathf.InverseLerp(deadMaxY, softMaxY, viewport.y);
                pan.y = Smooth01(strength);
            }

            return pan;
        }

        private Vector3 ViewportOffsetToWorldOffset(Camera cam, Vector2 viewportOffset)
        {
            float viewHeight;
            float viewWidth;

            if (cam.orthographic)
            {
                viewHeight = cam.orthographicSize * 2f;
                viewWidth = viewHeight * cam.aspect;
            }
            else
            {
                viewHeight = Mathf.Max(1f, cam.transform.position.y);
                viewWidth = viewHeight * cam.aspect;
            }

            Vector3 cameraRight = transform.right;
            Vector3 cameraUp = transform.up;

            cameraRight.y = 0f;
            cameraUp.y = 0f;

            cameraRight.Normalize();
            cameraUp.Normalize();

            return
                cameraRight * viewportOffset.x * viewWidth +
                cameraUp * viewportOffset.y * viewHeight;
        }

        private float Smooth01(float value)
        {
            value = Mathf.Clamp01(value);
            return value * value * (3f - 2f * value);
        }

        [ContextMenu("Focus On Target")]
        public void FocusOnTarget()
        {
            if (target == null)
                return;

            Vector3 delta = target.position - transform.position;
            delta.y = 0f;

            desiredPosition += delta;

            if (useBounds)
            {
                desiredPosition.x = Mathf.Clamp(desiredPosition.x, minXZ.x, maxXZ.x);
                desiredPosition.z = Mathf.Clamp(desiredPosition.z, minXZ.y, maxXZ.y);
            }

            transform.position = desiredPosition;
        }

    #if UNITY_EDITOR
        private bool IsMouseOverGameView()
        {
            EditorWindow window = EditorWindow.mouseOverWindow;

            if (window == null)
                return false;

            return window.GetType().Name == "GameView";
        }
    #endif

        private void OnDrawGizmosSelected()
        {
            if (!useBounds)
                return;

            Gizmos.color = Color.yellow;

            Vector3 center = new Vector3(
                (minXZ.x + maxXZ.x) * 0.5f,
                transform.position.y,
                (minXZ.y + maxXZ.y) * 0.5f
            );

            Vector3 size = new Vector3(
                maxXZ.x - minXZ.x,
                0f,
                maxXZ.y - minXZ.y
            );

            Gizmos.DrawWireCube(center, size);
        }
    }
}
