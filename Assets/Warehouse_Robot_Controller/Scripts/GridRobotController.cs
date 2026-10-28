using UnityEngine;

namespace Warcool.WarehouseRobot
{
    public class GridRobotController : MonoBehaviour
    {
        private enum RobotState
        {
            Idle,
            Moving,
            Rotating
        }

        [Header("Grid")]
        public float gridPointStep = 0.25f;
        public float cellSize = 0.5f;
        public int gridCellsX = 30;
        public int gridCellsZ = 30;

        [Header("Animator")]
        public Animator robotAnimator;
        public string isMovingParameter = "IsMoving";

        [Header("Movement")]
        public float moveSpeed = 1.2f;
        public float acceleration = 5f;
        public float deceleration = 8f;

        [Header("Rotation")]
        public float rotationStep = 45f;
        public float rotationSpeed = 180f;

        [Tooltip("Distance between the centers of the left and right tracks, in meters.")]
        public float trackWidth = 0.45f;

        [Header("Obstacle Check")]
        public LayerMask obstacleMask;
        public Vector3 obstacleCheckHalfExtents = new Vector3(0.23f, 0.25f, 0.23f);
        public Vector3 obstacleCheckOffset = new Vector3(0f, 0.25f, 0f);

        [Header("Tracks")]
        public DualProceduralTracksFromWheels tracks;

        [Header("Rear Light")]
        public RobotRearLightController rearLight;
        public float brakeLightDuration = 0.45f;

        [Header("External Lock")]
        public bool controlsLocked;

        private RobotState state = RobotState.Idle;

        private Vector3 targetPosition;
        private Quaternion targetRotation;

        private float currentMoveSpeed;
        private Vector3 currentMoveDirection;

        private Vector3 previousPosition;
        private Quaternion previousRotation;

        private int moveInputDirection;

        private float brakeLightTimer;
        private bool lastMoveWasReverse;

        private void Start()
        {
            transform.position = SnapToGrid(transform.position);
            transform.rotation = SnapRotation(transform.rotation);

            targetPosition = transform.position;
            targetRotation = transform.rotation;

            previousPosition = transform.position;
            previousRotation = transform.rotation;

            UpdateAnimator();
            UpdateRearLight();
        }

        private void Update()
        {
            RobotState previousState = state;

            if (controlsLocked)
            {
                currentMoveSpeed = 0f;
                moveInputDirection = 0;
                brakeLightTimer = 0f;

                UpdateAnimator();
                AnimateTracks();
                UpdateRearLight();

                previousPosition = transform.position;
                previousRotation = transform.rotation;
                return;
            }

            ReadMoveInput();

            switch (state)
            {
                case RobotState.Idle:
                    HandleIdleInput();
                    break;

                case RobotState.Moving:
                    UpdateMovement();
                    break;

                case RobotState.Rotating:
                    UpdateRotation();
                    break;
            }

            if (previousState == RobotState.Moving && state == RobotState.Idle && !lastMoveWasReverse)
            {
                brakeLightTimer = brakeLightDuration;
            }

            if (state == RobotState.Moving && !lastMoveWasReverse && moveInputDirection == 0)
            {
                brakeLightTimer = brakeLightDuration;
            }

            if (brakeLightTimer > 0f)
            {
                brakeLightTimer -= Time.deltaTime;
            }

            UpdateAnimator();
            AnimateTracks();
            UpdateRearLight();

            previousPosition = transform.position;
            previousRotation = transform.rotation;
        }

        private void ReadMoveInput()
        {
            if (Input.GetKey(KeyCode.W))
                moveInputDirection = 1;
            else if (Input.GetKey(KeyCode.S))
                moveInputDirection = -1;
            else
                moveInputDirection = 0;
        }

        private void HandleIdleInput()
        {
            if (moveInputDirection != 0)
            {
                StartMove(moveInputDirection);
                return;
            }

            if (Input.GetKey(KeyCode.A))
            {
                StartTurn(-rotationStep);
                return;
            }

            if (Input.GetKey(KeyCode.D))
            {
                StartTurn(rotationStep);
                return;
            }

            currentMoveSpeed = Mathf.MoveTowards(
                currentMoveSpeed,
                0f,
                deceleration * Time.deltaTime
            );
        }

        private void StartMove(int direction)
        {
            currentMoveDirection = direction > 0 ? transform.forward : -transform.forward;
            lastMoveWasReverse = direction < 0;

            Vector3 nextTarget = transform.position + currentMoveDirection * gridPointStep;
            nextTarget = SnapToGrid(nextTarget);
            nextTarget = ClampToGrid(nextTarget);

            if ((nextTarget - transform.position).sqrMagnitude < 0.000001f)
                return;

            if (IsPositionBlocked(nextTarget))
                return;

            targetPosition = nextTarget;
            state = RobotState.Moving;
        }

        private void StartTurn(float angle)
        {
            if (currentMoveSpeed > 0.001f)
                return;

            float currentY = transform.eulerAngles.y;
            float targetY = currentY + angle;
            targetY = Mathf.Round(targetY / rotationStep) * rotationStep;

            targetRotation = Quaternion.Euler(0f, targetY, 0f);
            state = RobotState.Rotating;
        }

        private void UpdateMovement()
        {
            float distanceToTarget = Vector3.Distance(transform.position, targetPosition);

            bool keepMoving =
                moveInputDirection != 0 &&
                Vector3.Dot(
                    currentMoveDirection,
                    moveInputDirection > 0 ? transform.forward : -transform.forward
                ) > 0.99f;

            float targetSpeed;

            if (keepMoving)
            {
                targetSpeed = moveSpeed;
            }
            else
            {
                float brakingDistance =
                    (currentMoveSpeed * currentMoveSpeed) /
                    (2f * Mathf.Max(0.001f, deceleration));

                targetSpeed = distanceToTarget <= brakingDistance ? 0f : moveSpeed;
            }

            float speedChange = targetSpeed > currentMoveSpeed ? acceleration : deceleration;

            currentMoveSpeed = Mathf.MoveTowards(
                currentMoveSpeed,
                targetSpeed,
                speedChange * Time.deltaTime
            );

            float step = currentMoveSpeed * Time.deltaTime;

            if (step >= distanceToTarget)
            {
                transform.position = targetPosition;

                if (keepMoving)
                {
                    StartMove(moveInputDirection);
                }
                else
                {
                    state = RobotState.Idle;
                    currentMoveSpeed = 0f;
                }

                return;
            }

            transform.position += currentMoveDirection * step;
        }

        private void UpdateRotation()
        {
            transform.rotation = Quaternion.RotateTowards(
                transform.rotation,
                targetRotation,
                rotationSpeed * Time.deltaTime
            );

            if (Quaternion.Angle(transform.rotation, targetRotation) < 0.01f)
            {
                transform.rotation = targetRotation;
                state = RobotState.Idle;

                if (Input.GetKey(KeyCode.A))
                    StartTurn(-rotationStep);
                else if (Input.GetKey(KeyCode.D))
                    StartTurn(rotationStep);
            }
        }

        private void UpdateAnimator()
        {
            if (robotAnimator == null)
                return;

            bool isMoving =
                !controlsLocked &&
                (state == RobotState.Moving || state == RobotState.Rotating);

            robotAnimator.SetBool(isMovingParameter, isMoving);
        }

        private void UpdateRearLight()
        {
            if (rearLight == null)
                return;

            if (controlsLocked)
            {
                rearLight.SetState(RobotRearLightController.RearLightState.Off);
                return;
            }

            if (state == RobotState.Moving && lastMoveWasReverse)
            {
                rearLight.SetState(RobotRearLightController.RearLightState.Reverse);
                return;
            }

            if (brakeLightTimer > 0f)
            {
                rearLight.SetState(RobotRearLightController.RearLightState.Stop);
                return;
            }

            rearLight.SetState(RobotRearLightController.RearLightState.Off);
        }

        private void AnimateTracks()
        {
            if (tracks == null)
                return;

            float leftDistance = 0f;
            float rightDistance = 0f;

            Vector3 moveDelta = transform.position - previousPosition;
            float moveDistance = moveDelta.magnitude;

            if (moveDistance > Mathf.Epsilon)
            {
                float sign = Vector3.Dot(previousRotation * Vector3.forward, moveDelta.normalized) >= 0f
                    ? 1f
                    : -1f;

                leftDistance += moveDistance * sign;
                rightDistance += moveDistance * sign;
            }

            float angleDelta = Mathf.DeltaAngle(
                previousRotation.eulerAngles.y,
                transform.rotation.eulerAngles.y
            );

            if (Mathf.Abs(angleDelta) > Mathf.Epsilon)
            {
                float angleRad = angleDelta * Mathf.Deg2Rad;
                float turnDistance = angleRad * (trackWidth * 0.5f);

                leftDistance += turnDistance;
                rightDistance -= turnDistance;
            }

            tracks.AnimateTracksByDistance(leftDistance, rightDistance);
        }

        private bool IsPositionBlocked(Vector3 position)
        {
            Vector3 center = position + obstacleCheckOffset;

            return Physics.CheckBox(
                center,
                obstacleCheckHalfExtents,
                transform.rotation,
                obstacleMask,
                QueryTriggerInteraction.Ignore
            );
        }

        private Vector3 SnapToGrid(Vector3 position)
        {
            position.x = Mathf.Round(position.x / gridPointStep) * gridPointStep;
            position.z = Mathf.Round(position.z / gridPointStep) * gridPointStep;
            return position;
        }

        private Quaternion SnapRotation(Quaternion rotation)
        {
            float y = rotation.eulerAngles.y;
            y = Mathf.Round(y / rotationStep) * rotationStep;
            return Quaternion.Euler(0f, y, 0f);
        }

        private Vector3 ClampToGrid(Vector3 position)
        {
            float halfX = gridCellsX * cellSize * 0.5f;
            float halfZ = gridCellsZ * cellSize * 0.5f;

            position.x = Mathf.Clamp(position.x, -halfX, halfX);
            position.z = Mathf.Clamp(position.z, -halfZ, halfZ);

            return position;
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.red;

            Vector3 center = transform.position + obstacleCheckOffset;
            Gizmos.matrix = Matrix4x4.TRS(center, transform.rotation, Vector3.one);
            Gizmos.DrawWireCube(Vector3.zero, obstacleCheckHalfExtents * 2f);
        }
    }
}
