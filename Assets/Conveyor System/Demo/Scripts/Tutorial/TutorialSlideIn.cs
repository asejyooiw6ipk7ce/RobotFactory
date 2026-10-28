using System.Collections;
using UnityEngine;

namespace ConveyorSystem.Demo
{
    /// <summary>
    /// Lowers a conveyor into a gap when the step starts (the connection forms through the triggers)
    /// and lifts it out again when the step is left.
    /// </summary>
    [AddComponentMenu("Conveyor System/Demo/Tutorial/Slide In")]
    public class TutorialSlideIn : TutorialStepAction
    {
        [Tooltip("Object to move (e.g. a conveyor).")]
        public Transform Target;

        [Tooltip("Offset from the final position while the step is not shown.")]
        public Vector3 Offset = new Vector3(0f, 1.6f, 0f);

        [Tooltip("Delay before moving, so the viewer sees the gap first.")]
        public float Delay = 1.2f;

        public float Duration = 1.2f;

        private Vector3 placed;
        private Coroutine move;

        public override void Prepare()
        {
            placed = Target.position;
            Target.position = placed + Offset;
        }

        public override void Enter() => MoveTo(placed, Delay);
        public override void Exit() => MoveTo(placed + Offset, 0f);

        private void MoveTo(Vector3 destination, float delay)
        {
            if (move != null) StopCoroutine(move);
            move = StartCoroutine(Move(destination, delay));
        }

        private IEnumerator Move(Vector3 destination, float delay)
        {
            if (delay > 0f) yield return new WaitForSeconds(delay);
            Vector3 start = Target.position;
            for (float t = 0f; t < 1f; t += Time.deltaTime / Mathf.Max(0.01f, Duration))
            {
                Target.position = Vector3.Lerp(start, destination, Mathf.SmoothStep(0f, 1f, t));
                yield return null;
            }
            Target.position = destination;
            move = null;
        }
    }
}
