using UnityEngine;

namespace ConveyorSystem.Demo
{
    /// <summary>
    /// Showcase camera: orbits a focus point, slowly turns by itself when idle,
    /// and can be steered with Q/E or the arrow keys and zoomed with the mouse wheel.
    /// </summary>
    [AddComponentMenu("Conveyor System/Demo/Demo Camera")]
    public class DemoCamera : MonoBehaviour
    {
        [Header("Framing")]
        [Tooltip("World point the camera looks at.")]
        public Vector3 Focus = Vector3.zero;

        [Tooltip("Distance from the focus point.")]
        public float Distance = 17f;

        [Tooltip("Vertical angle in degrees.")]
        [Range(10f, 89f)]
        public float Pitch = 45f;

        [Tooltip("Horizontal angle in degrees.")]
        public float Yaw = 210f;

        [Header("Limits")]
        public float MinDistance = 6f;
        public float MaxDistance = 30f;
        public float MinPitch = 20f;
        public float MaxPitch = 80f;

        [Header("Control")]
        [Tooltip("Degrees per second when steering with the keyboard.")]
        public float RotateSpeed = 70f;

        [Tooltip("Distance change per mouse wheel step.")]
        public float ZoomStep = 1.5f;

        [Header("Pan")]
        [Tooltip("Move the focus point with WASD.")]
        public bool EnablePan = false;

        [Tooltip("Pan speed in units per second.")]
        public float PanSpeed = 8f;

        [Tooltip("Maximum distance of the focus point from its start position on X / Z.")]
        public Vector2 PanLimit = new Vector2(14f, 10f);

        [Header("Idle Orbit")]
        [Tooltip("Degrees per second the camera turns by itself when idle (0 = off).")]
        public float AutoOrbitSpeed = 3f;

        [Tooltip("Seconds without input before the idle orbit resumes.")]
        public float IdleDelay = 6f;

        private float idleTime;
        private float targetDistance;
        private Vector3 startFocus;

        private void Start()
        {
            targetDistance = Distance;
            startFocus = Focus;
            Apply();
        }

        private void LateUpdate()
        {
            float orbit = DemoInput.Orbit;
            float tilt = DemoInput.Tilt;
            float scroll = DemoInput.Scroll;

            // Unscaled so the camera still works while time is paused; clamped so a hitch does not make it jump
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);

            Vector2 pan = EnablePan ? DemoInput.Pan : Vector2.zero;
            if (pan != Vector2.zero)
            {
                // Move on the ground plane relative to the current view direction
                Quaternion yawOnly = Quaternion.Euler(0f, Yaw, 0f);
                Vector3 move = yawOnly * new Vector3(pan.x, 0f, pan.y) * (PanSpeed * dt);
                Vector3 f = Focus + move;
                f.x = Mathf.Clamp(f.x, startFocus.x - PanLimit.x, startFocus.x + PanLimit.x);
                f.z = Mathf.Clamp(f.z, startFocus.z - PanLimit.y, startFocus.z + PanLimit.y);
                Focus = f;
            }

            bool hasInput = orbit != 0f || tilt != 0f || scroll != 0f || pan != Vector2.zero;
            idleTime = hasInput ? 0f : idleTime + dt;

            Yaw += orbit * RotateSpeed * dt;
            Pitch = Mathf.Clamp(Pitch + tilt * RotateSpeed * 0.5f * dt, MinPitch, MaxPitch);
            targetDistance = Mathf.Clamp(targetDistance - scroll * ZoomStep, MinDistance, MaxDistance);

            if (idleTime > IdleDelay) Yaw += AutoOrbitSpeed * dt;

            Distance = Mathf.Lerp(Distance, targetDistance, 1f - Mathf.Exp(-8f * dt));
            Apply();
        }

        /// <summary>
        /// Places the camera on its orbit.
        /// </summary>
        public void Apply()
        {
            Quaternion rotation = Quaternion.Euler(Pitch, Yaw, 0f);
            transform.SetPositionAndRotation(Focus - rotation * Vector3.forward * Distance, rotation);
        }

        private void OnValidate()
        {
            if (!Application.isPlaying) Apply();
        }
    }
}
