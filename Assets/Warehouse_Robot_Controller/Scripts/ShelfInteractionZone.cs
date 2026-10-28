using UnityEngine;

namespace Warcool.WarehouseRobot
{
    public class ShelfInteractionZone : MonoBehaviour
    {
        public enum AccessSide
        {
            Front,
            Back
        }

        [Header("Shelf / Pallet")]
        public ShelfStorage shelf;

        [Min(0)]
        public int columnIndex;

        public AccessSide accessSide;

        [Header("Docking")]
        public Transform dockPoint;
        public float positionTolerance = 0.05f;
        public float angleTolerance = 5f;

        [Header("Debug")]
        public bool drawGizmos = true;

        public bool IsRobotCorrectlyDocked(Transform robot)
        {
            if (robot == null || dockPoint == null)
                return false;

            Vector3 robotPos = robot.position;
            Vector3 dockPos = dockPoint.position;

            robotPos.y = 0f;
            dockPos.y = 0f;

            if (Vector3.Distance(robotPos, dockPos) > positionTolerance)
                return false;

            Vector3 robotForward = robot.forward;
            Vector3 dockForward = dockPoint.forward;

            robotForward.y = 0f;
            dockForward.y = 0f;

            robotForward.Normalize();
            dockForward.Normalize();

            float angle = Vector3.Angle(robotForward, dockForward);

            return angle <= angleTolerance;
        }

        private void OnDrawGizmosSelected()
        {
            if (!drawGizmos || dockPoint == null)
                return;

            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(dockPoint.position, positionTolerance);

            Gizmos.color = Color.blue;
            Gizmos.DrawRay(dockPoint.position, dockPoint.forward * 0.5f);
        }
    }
}
