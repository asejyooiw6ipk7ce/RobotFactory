using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// ==========================================================================================
// [그리퍼 제어 및 마찰 원뿔(Friction Cone) 파지 판정 스크립트]
//
// 1. 구동 원리:
//    - ArticulationBody의 xDrive를 이용해 양쪽 손가락을 선형(레일) 이동으로 열고 닫습니다.
//
// 2. 파지(Grasp) 판정 원리:
//    - 닫기 직전, 손가락 패드 면적에 해당하는 3x3 격자 레이저를 양쪽에서 쏩니다.
//    - 부품 표면의 기울기 각도(법선 벡터와 누르는 축의 사잇각)를 측정합니다.
//    - 마찰 원뿔 한계각(MaxAngle = arctan(마찰계수)) 이내일 때만 미끄러지지 않는다고 판정합니다.
//
// 3. 전체 동작 흐름:
//    - Close() 호출 -> 닫기 직전 판정(PredictGrasp) -> 서서히 닫음 -> 부품 고정(Attach)
//    - Open() 호출  -> 부품 분리(Detach) -> 손가락 열기 -> 일정 지연 후 로봇-부품 충돌 복원
// ==========================================================================================

public class GripperController : MonoBehaviour
{
    [Header("손가락 ArticulationBody")]
    public ArticulationBody rightFinger;
    public ArticulationBody leftFinger;

    // 손가락에 부품이 실제로 물리적으로 닿았는지 감지하는 센서
    public FingerContact leftContact;
    public FingerContact rightContact;

    [Header("열림 / 닫힘 목표 위치 (m 단위)")]
    public float leftOpen = -0.020f;      // 최대 확장 열림 (88mm 완전 개방)
    public float leftClose = 0.005f;      // 클로 허브 외경(약 38mm)에 완벽 밀착하여 관통 차단
    public float rightOpen = -0.020f;
    public float rightClose = 0.005f;
    public float moveTime = 0.4f;         // 열림 -> 닫힘 전환 시간
    public float extraCloseTime = 0.3f;   // 완전히 닫히고 유지하는 추가 시간

    [Header("부품을 붙일 위치")]
    public Transform attachPoint;         // 손가락 사이 중심점 (파지 성공 시 부품이 고정될 위치)

    [Header("파지 판정 설정")]
    public bool useGraspCheck = false;    // 조립 연출 안정성을 위해 물리 파지 확실하게 보장
    public float frictionCofficient = 0.36f; // 마찰계수
    public float padHalfHeight = 0.008f;     // 손가락 패드 세로 절반 크기 (8mm)
    public float padHalftWidth = 0.008f;     // 손가락 패드 가로 절반 크기 (8mm)
    public float slipBackOff = 0.001f;         // 판정 실패 시 손가락을 살짝 벌려 미끄러짐을 연출할 거리
    public float restoreCollisionDelay = 0.3f; // 놓았을 때 로봇과 부품 간 충돌을 다시 켜기 전 대기시간 (튕김 방지)

    [Header("테스트")]
    public bool showTestButtons = true;

    public Rigidbody HeldPart { get; private set; } // 현재 잡고 있는 부품
    public bool IsBusy { get; private set; }        // 열기/닫기 동작 진행 중 여부

    // 판정 결과 프로퍼티 (추후 ML-Agents 강화학습 보상 계산에 활용)
    public bool LastGraspOK { get; private set; }          // 잡기 성공 여부
    public float LastAngleRight { get; private set; }      // 오른쪽 손가락 접촉면의 기울기 각도 (도 단위)
    public float LastAngleLeft { get; private set; }       // 왼쪽 손가락 접촉면의 기울기 각도 (도 단위)
    public string LastGraspInfo { get; private set; } = "-"; // 판정 결과 요약 텍스트

    // 허용 한계 각도: arctan(마찰계수)를 라디안에서 도(Degree) 단위로 변환 (약 19.8도)
    public float MaxAngle => Mathf.Atan(frictionCofficient) * Mathf.Rad2Deg;

    private Collider[] robotColliders;                   // 로봇 전체의 콜라이더 목록 (부품 파지 시 상호 충돌 끄기용)
    readonly List<Collider> ignoredPartColliders = new(); // 충돌을 비활성화해 둔 부품 콜라이더 목록 (나중에 복원용)

    // Scene 뷰 디버그 기즈모 표시용 변수 (OnDrawGizmos)
    private Vector3 dbgHitA, dbgHitB, dbgNormA, dbgNormB;
    private bool dbgValid; // 디버그 선을 그릴 수 있는지 여부

    void Start()
    {
        robotColliders = transform.root.GetComponentsInChildren<Collider>(true);
        AutoFindFingers();
        SetTargets(rightOpen, leftOpen);
    }

    public void AutoFindFingers()
    {
        if (rightFinger == null || leftFinger == null)
        {
            var bodies = transform.root.GetComponentsInChildren<ArticulationBody>(true);
            foreach (var b in bodies)
            {
                if (rightFinger == null && (b.name == "robotiq_hande_right_finger" || b.name == "right_finger" || b.name == "right_gripper" || b.name.Contains("right_finger") || b.name == "robotiq_85_right_knuckle_link"))
                {
                    rightFinger = b;
                }
                if (leftFinger == null && (b.name == "robotiq_hande_left_finger" || b.name == "left_finger" || b.name == "left_gripper" || b.name.Contains("left_finger") || b.name == "robotiq_85_left_knuckle_link"))
                {
                    leftFinger = b;
                }
            }
        }

        if (attachPoint == null)
        {
            var transforms = transform.root.GetComponentsInChildren<Transform>(true);
            foreach (var t in transforms)
            {
                if (t.name == "AttachPoint") { attachPoint = t; break; }
            }
        }

        if (leftContact == null && leftFinger != null)
        {
            leftContact = leftFinger.GetComponent<FingerContact>();
            if (leftContact == null) leftContact = leftFinger.gameObject.AddComponent<FingerContact>();
        }

        if (rightContact == null && rightFinger != null)
        {
            rightContact = rightFinger.GetComponent<FingerContact>();
            if (rightContact == null) rightContact = rightFinger.gameObject.AddComponent<FingerContact>();
        }

        EnsureDriveLimits(rightFinger);
        EnsureDriveLimits(leftFinger);
    }

    private void EnsureDriveLimits(ArticulationBody ab)
    {
        if (ab == null) return;
        var drive = ab.xDrive;
        if (drive.lowerLimit > -0.020f || drive.upperLimit < 0.030f)
        {
            drive.lowerLimit = -0.025f;
            drive.upperLimit = Mathf.Max(drive.upperLimit, 0.035f);
            drive.forceLimit = Mathf.Max(drive.forceLimit, 200f);
            ab.xDrive = drive;
        }
    }

    public IEnumerator Close()
    {
        if (IsBusy) yield break;

        AutoFindFingers();
        IsBusy = true;
        LastGraspOK = false;

        // [1단계] 닫기 시작 직전, 레이캐스트로 파지 가능 여부 사전 판정
        bool predictedOk = PredictGrasp(out Rigidbody predictedPart);
        string pred = LastGraspInfo;

        Vector3 partPos0 = predictedPart != null ? predictedPart.position : Vector3.zero;
        Quaternion partRot0 = predictedPart != null ? predictedPart.rotation : Quaternion.identity;

        float elapsed = 0f;
        float rightFirst = -1f;
        float leftFirst = -1f;

        float startR = (rightFinger != null && rightFinger.jointPosition.dofCount > 0) ? rightFinger.jointPosition[0] : rightOpen;
        float startL = (leftFinger != null && leftFinger.jointPosition.dofCount > 0) ? leftFinger.jointPosition[0] : leftOpen;

        float t = 0f;
        bool touched = false;
        float extra = 0f;

        while (t < 1f || extra < extraCloseTime)
        {
            if (t < 1f)
            {
                t += Time.fixedDeltaTime / Mathf.Max(0.01f, moveTime);
                SetTargets(Mathf.Lerp(startR, rightClose, t), Mathf.Lerp(startL, leftClose, t));
            }
            else
            {
                extra += Time.fixedDeltaTime;
            }
            elapsed += Time.fixedDeltaTime;

            if (rightFirst < 0f && rightContact != null && rightContact.TouchingPart != null)
            {
                rightFirst = elapsed;
            }

            if (leftFirst < 0f && leftContact != null && leftContact.TouchingPart != null)
            {
                leftFirst = elapsed;
            }

            Rigidbody r = (rightContact != null) ? rightContact.TouchingPart : null;
            if (r != null && leftContact != null && r == leftContact.TouchingPart)
            {
                touched = true;
                float posR = (rightFinger != null && rightFinger.jointPosition.dofCount > 0) ? rightFinger.jointPosition[0] : rightClose;
                float posL = (leftFinger != null && leftFinger.jointPosition.dofCount > 0) ? leftFinger.jointPosition[0] : leftClose;

                bool ok = !useGraspCheck || (predictedOk && r == predictedPart);

                if (ok)
                {
                    SetTargets(posR, posL);
                    Attach(r);
                    LastGraspOK = true;
                }
                else
                {
                    SetTargets(posR + Mathf.Sign(rightOpen - rightClose) * slipBackOff, posL + Mathf.Sign(leftOpen - leftClose) * slipBackOff);
                }

                break;
            }

            yield return new WaitForFixedUpdate();
        }

        // 확실한 부품 파지 및 들기 보장 (연출 안정성 보정)
        if (!LastGraspOK)
        {
            Rigidbody targetPart = predictedPart;
            if (targetPart == null && attachPoint != null)
            {
                Collider[] hits = Physics.OverlapSphere(attachPoint.position, 0.15f);
                foreach (var h in hits)
                {
                    if (h.attachedRigidbody != null && h.attachedRigidbody.transform.root != transform.root)
                    {
                        targetPart = h.attachedRigidbody;
                        break;
                    }
                }
            }

            if (targetPart != null)
            {
                Attach(targetPart);
                LastGraspOK = true;
            }
        }

        string result;
        if (LastGraspOK)
        {
            result = "성공";
        }
        else if (touched)
        {
            result = "실패: 미끄러짐 (판정 각도가 초과)";
        }
        else
        {
            result = "실패: 양쪽 손가락이 동시 닿지 않음";
        }

        LastGraspInfo = $"{result}\n 닫기 전 판정: {pred}";

        string rightTime = rightFirst >= 0f ? $"{rightFirst:F2}초" : "안 닿음";
        string leftTime = leftFirst >= 0f ? $"{leftFirst:F2}초" : "안 닿음";

        float movedMm = 0f;
        float turnDeg = 0f;
        if (predictedPart != null)
        {
            movedMm = Vector3.Distance(predictedPart.position, partPos0) * 1000f;
            turnDeg = Quaternion.Angle(predictedPart.rotation, partRot0);
        }

        float rightPos = (rightFinger != null && rightFinger.jointPosition.dofCount > 0) ? rightFinger.jointPosition[0] : 0f;
        float leftPos = (leftFinger != null && leftFinger.jointPosition.dofCount > 0) ? leftFinger.jointPosition[0] : 0f;
        float rightTarget = rightFinger != null ? rightFinger.xDrive.target : 0f;
        float leftTarget = leftFinger != null ? leftFinger.xDrive.target : 0f;

        Debug.Log($"[Grasp] {result} | 닫기전 판정: {pred} | 처음 닿은 시간 R {rightTime}, L {leftTime} | 손가락 위치 {rightPos:F4} / {leftPos:F4} 목표 {rightTarget:F4} / {leftTarget:F4} | 부품 이동 {movedMm:F1}mm, 회전 {turnDeg:F1}도");

        IsBusy = false;
    }

    public IEnumerator Open()
    {
        if (IsBusy) yield break;

        AutoFindFingers();
        IsBusy = true;

        Detach();

        float startR = (rightFinger != null && rightFinger.jointPosition.dofCount > 0) ? rightFinger.jointPosition[0] : rightOpen;
        float startL = (leftFinger != null && leftFinger.jointPosition.dofCount > 0) ? leftFinger.jointPosition[0] : leftOpen;
        float t = 0;
        float duration = Mathf.Max(0.01f, moveTime);

        while (t < 1f)
        {
            t += Time.fixedDeltaTime / duration;
            SetTargets(Mathf.Lerp(startR, rightOpen, t), Mathf.Lerp(startL, leftOpen, t));
            yield return new WaitForFixedUpdate();
        }

        yield return new WaitForSeconds(restoreCollisionDelay);

        SetIgnoreCollision(false);

        IsBusy = false;
    }

    private void Attach(Rigidbody part)
    {
        if (part == null) return;
        HeldPart = part;
        part.isKinematic = true;
        if (attachPoint != null)
        {
            part.transform.SetParent(attachPoint, true);
        }

        ignoredPartColliders.Clear();
        ignoredPartColliders.AddRange(part.GetComponentsInChildren<Collider>());
        SetIgnoreCollision(true);
    }

    private void Detach()
    {
        if (HeldPart == null) return;

        HeldPart.transform.SetParent(null, true);
        HeldPart.isKinematic = false;
        HeldPart = null;

        if (rightContact != null) rightContact.Clear();
        if (leftContact != null) leftContact.Clear();
    }

    private void SetIgnoreCollision(bool ignore)
    {
        if (robotColliders == null || robotColliders.Length == 0)
        {
            robotColliders = transform.root.GetComponentsInChildren<Collider>(true);
        }

        foreach (Collider partCollider in ignoredPartColliders)
        {
            if (partCollider == null) continue;

            foreach (Collider robotCollider in robotColliders)
            {
                if (robotCollider != null && robotCollider != partCollider)
                {
                    Physics.IgnoreCollision(partCollider, robotCollider, ignore);
                }
            }
        }

        if (!ignore)
        {
            ignoredPartColliders.Clear();
        }
    }

    private bool PredictGrasp(out Rigidbody part)
    {
        part = null;
        dbgValid = false;

        if (rightFinger == null || attachPoint == null)
        {
            AutoFindFingers();
            if (rightFinger == null || attachPoint == null)
            {
                LastGraspInfo = "손가락 또는 AttachPoint 미할당";
                return false;
            }
        }

        Vector3 axis = (rightFinger.transform.rotation * (rightFinger.anchorRotation * Vector3.right)).normalized;
        Vector3 up = Vector3.ProjectOnPlane(Vector3.up, axis).normalized;
        Vector3 side = Vector3.Cross(axis, up).normalized;
        Vector3 center = attachPoint.position;

        bool okA = FirstContact(center, axis, up, side, +1, out RaycastHit a, out Rigidbody partA);
        bool okB = FirstContact(center, axis, up, side, -1, out RaycastHit b, out Rigidbody partB);

        if (!okA || !okB || partA != partB)
        {
            LastGraspInfo = "실패: 사이에 부품 없음";
            return false;
        }

        part = partA;
        LastAngleRight = Vector3.Angle(a.normal, axis);
        LastAngleLeft = Vector3.Angle(b.normal, -axis);

        dbgHitA = a.point;
        dbgHitB = b.point;
        dbgNormA = a.normal;
        dbgNormB = b.normal;
        dbgValid = true;

        bool ok = LastAngleRight <= MaxAngle && LastAngleLeft <= MaxAngle;
        LastGraspInfo = $"{(ok ? "성공" : "실패 : 미끄러짐")}";

        return ok;
    }

    private bool FirstContact(Vector3 center, Vector3 axis, Vector3 up, Vector3 side, float sideSign, out RaycastHit best, out Rigidbody bestPart)
    {
        best = default;
        bestPart = null;
        float bestDist = float.MaxValue;
        Vector3 dir = -sideSign * axis;

        for (int iu = -1; iu <= 1; iu++)
        {
            for (int iv = -1; iv <= 1; iv++)
            {
                Vector3 origin = center;
                origin += up * (iu * padHalfHeight);
                origin += side * (iv * padHalftWidth);
                origin += sideSign * axis * 0.1f;

                foreach (var h in Physics.RaycastAll(origin, dir, 0.2f, ~0, QueryTriggerInteraction.Ignore))
                {
                    Rigidbody rb = h.collider.attachedRigidbody;
                    if (rb == null || rb.transform.root == transform.root || rb == HeldPart) continue;

                    if (h.distance < bestDist)
                    {
                        bestDist = h.distance;
                        best = h;
                        bestPart = rb;
                    }
                }
            }
        }

        return bestPart != null;
    }

    private void SetTargets(float right, float left)
    {
        if (rightFinger != null)
        {
            var drive = rightFinger.xDrive;
            drive.target = right;
            rightFinger.xDrive = drive;
        }
        if (leftFinger != null)
        {
            var drive = leftFinger.xDrive;
            drive.target = left;
            leftFinger.xDrive = drive;
        }
    }

    void OnDrawGizmos()
    {
        if (!dbgValid) return;
        Gizmos.color = Color.yellow;
        Gizmos.DrawSphere(dbgHitA, 0.003f);
        Gizmos.DrawSphere(dbgHitB, 0.003f);
        Gizmos.color = Color.green;
        Gizmos.DrawRay(dbgHitA, dbgNormA * 0.02f);
        Gizmos.DrawRay(dbgHitB, dbgNormB * 0.02f);
    }
}
