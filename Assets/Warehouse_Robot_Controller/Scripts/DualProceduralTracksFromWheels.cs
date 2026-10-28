using UnityEngine;

namespace Warcool.WarehouseRobot
{
    public class DualProceduralTracksFromWheels : MonoBehaviour
    {
        public enum Axis
        {
            X,
            Y,
            Z,
            NegativeX,
            NegativeY,
            NegativeZ
        }

        [System.Serializable]
        public class TrackSide
        {
            [Header("Wheels")]
            public Transform frontWheel;
            public Transform rearWheel;
            public Transform[] wheelsToRotate;

            [Header("Drive Gears")]
            public Transform[] driveGearsToRotate;

            [Tooltip("How much faster the drive gears rotate relative to the wheels.")]
            public float gearSpeedMultiplier = 4f;

            [Tooltip("Rotate drive gears in the opposite direction from the wheels.")]
            public bool invertGearRotation = true;

            [Header("Bones / Track Dummies")]
            public Transform[] trackBones;

            [Header("Track Shape")]
            [Tooltip("Radius used for the bone path. Track thickness is not added to this value.")]
            public float wheelRadius = 0.5f;

            [Tooltip("Used only for wheel and drive gear rotation calculations.")]
            public float trackThickness = 0.04f;

            [Header("Movement")]
            public bool reverseTrack;

            [Header("Bone Rotation")]
            public bool rotateBones = true;

            [Tooltip("Local bone axis used as the forward direction of the track link.")]
            public Axis boneForwardAxis = Axis.Z;

            [Tooltip("Additional rotation offset if the track link is not aligned correctly.")]
            public Vector3 boneRotationOffset;

            [Header("Wheel / Gear Rotation")]
            [Tooltip("Local spin axis for wheels and drive gears.")]
            public Vector3 wheelSpinAxis = Vector3.right;

            public float wheelRotationMultiplier = 1f;

            [Header("Debug")]
            public bool drawGizmos = true;
            public int gizmoSteps = 96;

            [HideInInspector] public float traveledDistance;

            private Quaternion[] initialLocalRotations;
            private Vector3[] initialLocalForwardDirections;

            public float EffectiveRadius
            {
                get
                {
                    return Mathf.Max(0.0001f, wheelRadius + trackThickness * 0.5f);
                }
            }

            private float GetStraightLength(Transform root)
            {
                if (root == null || frontWheel == null || rearWheel == null)
                    return 0f;

                Vector3 rearLocal = root.InverseTransformPoint(rearWheel.position);
                Vector3 frontLocal = root.InverseTransformPoint(frontWheel.position);

                return Vector3.Distance(rearLocal, frontLocal);
            }

            private float GetBoneArcLength()
            {
                return Mathf.PI * wheelRadius;
            }

            private float GetBonePathLength(Transform root)
            {
                float straight = GetStraightLength(root);
                float arc = GetBoneArcLength();

                return straight * 2f + arc * 2f;
            }

            public bool IsValid(Transform root)
            {
                return root != null &&
                       frontWheel != null &&
                       rearWheel != null &&
                       trackBones != null &&
                       trackBones.Length > 0 &&
                       GetStraightLength(root) > 0.0001f &&
                       wheelRadius > 0.0001f;
            }

            public void CacheInitialBoneData(Transform root)
            {
                if (root == null || trackBones == null)
                    return;

                initialLocalRotations = new Quaternion[trackBones.Length];
                initialLocalForwardDirections = new Vector3[trackBones.Length];

                for (int i = 0; i < trackBones.Length; i++)
                {
                    Transform bone = trackBones[i];

                    if (bone == null)
                    {
                        initialLocalRotations[i] = Quaternion.identity;
                        initialLocalForwardDirections[i] = Vector3.forward;
                        continue;
                    }

                    initialLocalRotations[i] = Quaternion.Inverse(root.rotation) * bone.rotation;

                    Vector3 worldAxis = GetAxisDirectionWorld(bone, boneForwardAxis);
                    Vector3 localAxis = root.InverseTransformDirection(worldAxis).normalized;

                    initialLocalForwardDirections[i] = localAxis;
                }
            }

            public void UpdateTrackByDistance(Transform root, float distanceDelta)
            {
                if (!IsValid(root))
                    return;

                if (initialLocalRotations == null ||
                    initialLocalForwardDirections == null ||
                    initialLocalRotations.Length != trackBones.Length ||
                    initialLocalForwardDirections.Length != trackBones.Length)
                {
                    CacheInitialBoneData(root);
                }

                float direction = reverseTrack ? -1f : 1f;
                float delta = distanceDelta * direction;

                traveledDistance += delta;

                float pathLength = GetBonePathLength(root);
                float spacing = pathLength / trackBones.Length;

                for (int i = 0; i < trackBones.Length; i++)
                {
                    Transform bone = trackBones[i];

                    if (bone == null)
                        continue;

                    float distance = traveledDistance + i * spacing;

                    EvaluateBonePathLocal(
                        root,
                        distance,
                        out Vector3 localPosition,
                        out Vector3 localTangent
                    );

                    bone.position = root.TransformPoint(localPosition);

                    if (rotateBones && localTangent.sqrMagnitude > 0.000001f)
                    {
                        Quaternion deltaRotation =
                            Quaternion.FromToRotation(
                                initialLocalForwardDirections[i],
                                localTangent.normalized
                            );

                        Quaternion finalLocalRotation =
                            deltaRotation *
                            initialLocalRotations[i] *
                            Quaternion.Euler(boneRotationOffset);

                        bone.rotation = root.rotation * finalLocalRotation;
                    }
                }

                RotateWheelsAndGears(delta);
            }

            public void SetTrackPose(Transform root)
            {
                UpdateTrackByDistance(root, 0f);
            }

            private void RotateWheelsAndGears(float trackSurfaceDistanceDelta)
            {
                float angleRad = trackSurfaceDistanceDelta / EffectiveRadius;
                float wheelAngleDeg = angleRad * Mathf.Rad2Deg * wheelRotationMultiplier;

                Vector3 axis = wheelSpinAxis.normalized;

                if (wheelsToRotate != null)
                {
                    foreach (Transform wheel in wheelsToRotate)
                    {
                        if (wheel == null)
                            continue;

                        wheel.Rotate(axis, wheelAngleDeg, Space.Self);
                    }
                }

                if (driveGearsToRotate != null)
                {
                    float gearAngleDeg = wheelAngleDeg * gearSpeedMultiplier;

                    if (invertGearRotation)
                        gearAngleDeg *= -1f;

                    foreach (Transform gear in driveGearsToRotate)
                    {
                        if (gear == null)
                            continue;

                        gear.Rotate(axis, gearAngleDeg, Space.Self);
                    }
                }
            }

            private void EvaluateBonePathLocal(
                Transform root,
                float distance,
                out Vector3 position,
                out Vector3 tangent)
            {
                Vector3 rear = root.InverseTransformPoint(rearWheel.position);
                Vector3 front = root.InverseTransformPoint(frontWheel.position);

                Vector3 forward = (front - rear).normalized;

                Vector3 referenceUp = Vector3.up;

                if (Mathf.Abs(Vector3.Dot(referenceUp, forward)) > 0.98f)
                    referenceUp = Vector3.forward;

                Vector3 side = Vector3.Cross(referenceUp, forward).normalized;
                Vector3 up = Vector3.Cross(forward, side).normalized;

                float straight = Vector3.Distance(rear, front);
                float arc = Mathf.PI * wheelRadius;
                float total = straight * 2f + arc * 2f;
                float radius = wheelRadius;

                distance = Mathf.Repeat(distance, total);

                if (distance < straight)
                {
                    float t = distance / straight;

                    position = Vector3.Lerp(
                        rear + up * radius,
                        front + up * radius,
                        t
                    );

                    tangent = forward;
                    return;
                }

                distance -= straight;

                if (distance < arc)
                {
                    float angle = distance / radius;

                    position =
                        front +
                        Mathf.Cos(angle) * up * radius +
                        Mathf.Sin(angle) * forward * radius;

                    tangent =
                        -Mathf.Sin(angle) * up +
                        Mathf.Cos(angle) * forward;

                    tangent.Normalize();
                    return;
                }

                distance -= arc;

                if (distance < straight)
                {
                    float t = distance / straight;

                    position = Vector3.Lerp(
                        front - up * radius,
                        rear - up * radius,
                        t
                    );

                    tangent = -forward;
                    return;
                }

                distance -= straight;

                {
                    float angle = Mathf.PI + distance / radius;

                    position =
                        rear +
                        Mathf.Cos(angle) * up * radius +
                        Mathf.Sin(angle) * forward * radius;

                    tangent =
                        -Mathf.Sin(angle) * up +
                        Mathf.Cos(angle) * forward;

                    tangent.Normalize();
                }
            }

            private Vector3 GetAxisDirectionWorld(Transform target, Axis axis)
            {
                switch (axis)
                {
                    case Axis.X:
                        return target.right;

                    case Axis.Y:
                        return target.up;

                    case Axis.Z:
                        return target.forward;

                    case Axis.NegativeX:
                        return -target.right;

                    case Axis.NegativeY:
                        return -target.up;

                    case Axis.NegativeZ:
                        return -target.forward;

                    default:
                        return target.forward;
                }
            }

            public void DrawGizmos(Transform root)
            {
                if (!drawGizmos || !IsValid(root))
                    return;

                Gizmos.color = Color.yellow;

                int steps = Mathf.Max(8, gizmoSteps);
                Vector3 previous = Vector3.zero;

                for (int i = 0; i <= steps; i++)
                {
                    float d = GetBonePathLength(root) * i / steps;

                    EvaluateBonePathLocal(root, d, out Vector3 localPoint, out _);

                    Vector3 point = root.TransformPoint(localPoint);

                    if (i > 0)
                        Gizmos.DrawLine(previous, point);

                    previous = point;
                }

                Gizmos.color = Color.cyan;
                Gizmos.DrawWireSphere(frontWheel.position, wheelRadius);
                Gizmos.DrawWireSphere(rearWheel.position, wheelRadius);
            }
        }

        [Header("Tracks")]
        public TrackSide leftTrack = new TrackSide();
        public TrackSide rightTrack = new TrackSide();

        private void Awake()
        {
            RecacheInitialBoneData();
        }

        private void Start()
        {
            SetTrackPose();
        }

        public void AnimateTracksByDistance(float leftDistanceDelta, float rightDistanceDelta)
        {
            leftTrack.UpdateTrackByDistance(transform, leftDistanceDelta);
            rightTrack.UpdateTrackByDistance(transform, rightDistanceDelta);
        }

        public void SetTrackPose()
        {
            leftTrack.SetTrackPose(transform);
            rightTrack.SetTrackPose(transform);
        }

        public void RecacheInitialBoneData()
        {
            leftTrack.CacheInitialBoneData(transform);
            rightTrack.CacheInitialBoneData(transform);
        }

        public void ResetTravel()
        {
            leftTrack.traveledDistance = 0f;
            rightTrack.traveledDistance = 0f;

            SetTrackPose();
        }

        private void OnValidate()
        {
            ValidateTrack(leftTrack);
            ValidateTrack(rightTrack);
        }

        private void ValidateTrack(TrackSide track)
        {
            if (track == null)
                return;

            track.wheelRadius = Mathf.Max(0.0001f, track.wheelRadius);
            track.trackThickness = Mathf.Max(0f, track.trackThickness);
            track.gizmoSteps = Mathf.Max(8, track.gizmoSteps);
            track.gearSpeedMultiplier = Mathf.Max(0f, track.gearSpeedMultiplier);
        }

        private void OnDrawGizmosSelected()
        {
            leftTrack?.DrawGizmos(transform);
            rightTrack?.DrawGizmos(transform);
        }
    }
}
