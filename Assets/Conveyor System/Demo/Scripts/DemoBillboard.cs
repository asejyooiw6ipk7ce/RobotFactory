using UnityEngine;

namespace ConveyorSystem.Demo
{
    /// <summary>
    /// Keeps a world-space label turned towards the camera, so signs stay readable while the camera orbits.
    /// </summary>
    [ExecuteAlways]
    [AddComponentMenu("Conveyor System/Demo/Demo Billboard")]
    public class DemoBillboard : MonoBehaviour
    {
        [Tooltip("Camera to face (defaults to the main camera).")]
        public Camera Target;

        [Tooltip("On: parallel to the screen (crisp text from any angle). Off: turn around the vertical axis only.")]
        public bool MatchCameraRotation = true;

        private void LateUpdate()
        {
            if (Target == null) Target = Camera.main;
            if (Target == null) return;

            if (MatchCameraRotation)
            {
                transform.rotation = Target.transform.rotation;
                return;
            }

            Vector3 away = transform.position - Target.transform.position;
            away.y = 0f;
            if (away.sqrMagnitude > 1e-4f) transform.rotation = Quaternion.LookRotation(away, Vector3.up);
        }
    }
}
