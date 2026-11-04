using System.Collections;
using UnityEngine;

public partial class InspectRejectSequence : MonoBehaviour
{
    [Header("로봇 및 그리퍼 연결")]
    public ArmIK ik;
    public GripperController gripper;

    [Header("목표 위치 및 폐기함 트랜스폼")]
    public Transform pickPoint;   // 불량 부품 정지 위치 기준점 (또는 부품 자체)
    public Transform rejectPose;  // 폐기 박스 기준 위치 (폐기함 없을 시 폴백)
    public Transform rejectBin;   // 폐기함(RejectBin) 루트 오브젝트
    public Transform targetPart;  // 현재 파지 대상 불량 부품 (미지정 시 자동 감지)

    [Header("이동 및 높이 설정")]
    public float liftHeight = 0.15f;               // 수직 리프팅 높이 (m)
    public float moveSpeed = 0.25f;                // 직선 이동 속도 (m/s)
    public float gripWidthMm = 48f;                // 그리퍼 목표 파지 폭 (mm)
    public float defaultPickYaw = 90f;             // 수직 하강 기본 손목 각도 (deg)
    public float safeTransitHeightOffset = 0.65f;  // 로봇 베이스 기준 안전 상공 통과 고도 오프셋 (m)
    public float pickHeightOffset = -0.025f;       // 부품 파지 높이 미세 보정 (m)
    public float dropHeightOffset = 0.18f;         // 폐기함 상단 바운즈 기준 투하 상공 오프셋 (m)

    [Header("폐기 결과 및 상태 모니터")]
    public int maxGraspTries = 3;                  // 파지 최대 재시도 횟수
    public bool LastRejectOK { get; private set; } // true: 폐기 성공, false: 잡기 실패 등으로 폐기 실패
    public bool IsBusy { get; private set; }       // 시퀀스 실행 중 여부
    public string Status { get; private set; } = "대기";

    private void Awake()
    {
        AutoFindReferences();
    }

    private void Start()
    {
        AutoFindReferences();
        ApplyGripperCloseTarget();
    }

    private void SetState(string status)
    {
        Status = status;
        Debug.Log($"[InspectRejectSequence] {Status}");
    }

    /// <summary>
    /// 외부(InspectStation 등)에서 파지 대상 부품을 직접 지정
    /// </summary>
    public void SetTargetPart(Transform part)
    {
        targetPart = part;
        if (targetPart != null)
        {
            EnsurePlatformCollider(targetPart);
        }
    }

    /// <summary>
    /// 로봇 및 그리퍼, 폐기함 컴포넌트 자동 탐색 및 연결
    /// </summary>
    public void AutoFindReferences()
    {
        if (ik == null)
        {
            ik = GetComponent<ArmIK>();
            if (ik == null) ik = GetComponentInParent<ArmIK>();
            if (ik == null) ik = FindAnyObjectByType<ArmIK>();
        }

        if (gripper == null)
        {
            gripper = GetComponentInChildren<GripperController>();
            if (gripper == null && ik != null)
            {
                gripper = ik.GetComponentInChildren<GripperController>();
            }
            if (gripper == null) gripper = FindAnyObjectByType<GripperController>();
        }

        if (rejectBin == null)
        {
            var binObj = GameObject.Find("RejectBin_base");
            if (binObj == null) binObj = GameObject.Find("RejectBin");
            if (binObj != null) rejectBin = binObj.transform;
        }

        if (pickPoint == null)
        {
            var stopPointObj = GameObject.Find("PartStopPoint");
            if (stopPointObj != null) pickPoint = stopPointObj.transform;
        }
    }

    /// <summary>
    /// 그리퍼 닫힘 한계값을 gripWidthMm에 맞춰 설정하여 관통 방지 및 감지 안정화
    /// </summary>
    public void ApplyGripperCloseTarget()
    {
        if (gripper == null) AutoFindReferences();
        if (gripper != null)
        {
            float targetM = Mathf.Max(0f, (48f - gripWidthMm) * 0.5f * 0.001f);
            gripper.leftClose = targetM;
            gripper.rightClose = targetM;
            gripper.useGraspCheck = false;
        }
    }

    /// <summary>
    /// 대상 부품에 물리 콜라이더 및 Rigidbody를 보장하고 거대 콜라이더를 보정하여 결손(PartLoss) 시에도 안정적 파지 지원
    /// </summary>
    private void EnsurePlatformCollider(Transform part)
    {
        if (part == null) return;

        // 1. Rigidbody 보장
        var rb = part.GetComponent<Rigidbody>();
        if (rb == null) rb = part.GetComponentInParent<Rigidbody>();
        if (rb == null)
        {
            rb = part.gameObject.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;
        }

        // 2. grap_platfrom_01 활성화 여부 확인 및 콜라이더 부여
        Transform graspPlatform = part.Find("grap_platfrom_01");
        bool hasActivePlatform = (graspPlatform != null && graspPlatform.gameObject.activeInHierarchy);

        if (hasActivePlatform)
        {
            var box = graspPlatform.GetComponent<BoxCollider>();
            if (box == null) box = graspPlatform.gameObject.AddComponent<BoxCollider>();
            box.size = new Vector3(0.035f, 0.040f, 0.040f);
            box.center = Vector3.zero;
        }

        // 3. [부품 결손 PartLoss 대응] grap_platfrom_01이 비활성화되었을 경우 루트/자식 렌더러 바운즈 기반 콜라이더 자동 생성
        var existingColliders = part.GetComponentsInChildren<Collider>(false);
        if (existingColliders == null || existingColliders.Length == 0)
        {
            var renderers = part.GetComponentsInChildren<Renderer>(false);
            if (renderers != null && renderers.Length > 0)
            {
                Bounds b = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++)
                {
                    b.Encapsulate(renderers[i].bounds);
                }

                var rootBox = part.GetComponent<BoxCollider>();
                if (rootBox == null) rootBox = part.gameObject.AddComponent<BoxCollider>();
                rootBox.center = part.InverseTransformPoint(b.center);
                Vector3 localSize = part.InverseTransformVector(b.size);
                rootBox.size = new Vector3(Mathf.Abs(localSize.x), Mathf.Abs(localSize.y), Mathf.Abs(localSize.z));
            }
            else
            {
                var rootBox = part.GetComponent<BoxCollider>();
                if (rootBox == null) rootBox = part.gameObject.AddComponent<BoxCollider>();
                rootBox.size = new Vector3(0.04f, 0.04f, 0.04f);
                rootBox.center = Vector3.zero;
            }
        }

        // 4. [비정형/대형 콜라이더 보정] 0.25m 이상의 과도하게 큰 프리팹 콜라이더(예: Part1 1m 박스)가 파지 손가락에 걸려 튕기는 것 방지
        foreach (var col in part.GetComponentsInChildren<BoxCollider>(false))
        {
            if (col.size.x > 0.25f || col.size.y > 0.25f || col.size.z > 0.25f)
            {
                col.size = new Vector3(
                    Mathf.Min(col.size.x, 0.08f),
                    Mathf.Min(col.size.y, 0.08f),
                    Mathf.Min(col.size.z, 0.08f)
                );
            }
        }
    }

    /// <summary>
    /// 픽업 대상 부품 탐색 및 반환 (PickPoint 직접 할당, OverlapSphere, 렌더러 탐색 다층 지원)
    /// </summary>
    public Transform DetectOrGetTargetPart()
    {
        // 1. 이미 유효한 targetPart가 할당되어 있다면 사용
        if (targetPart != null && targetPart.gameObject.activeInHierarchy)
        {
            EnsurePlatformCollider(targetPart);
            return targetPart;
        }

        // 2. pickPoint 자체가 부품(또는 부품 루트)인 경우 직접 인식
        if (pickPoint != null && pickPoint.root != transform.root && (ik == null || pickPoint.root != ik.transform.root))
        {
            var rbOnPick = pickPoint.GetComponentInParent<Rigidbody>();
            if (rbOnPick != null && rbOnPick.transform.root != transform.root)
            {
                targetPart = rbOnPick.transform;
                EnsurePlatformCollider(targetPart);
                return targetPart;
            }

            var rends = pickPoint.GetComponentsInChildren<Renderer>(false);
            if (rends != null && rends.Length > 0)
            {
                targetPart = pickPoint;
                EnsurePlatformCollider(targetPart);
                return targetPart;
            }
        }

        // 3. 픽업 위치 주변 OverlapSphere 탐색 (반경 0.35m)
        Vector3 searchCenter = pickPoint != null ? pickPoint.position : transform.position;
        Collider[] hits = Physics.OverlapSphere(searchCenter, 0.35f);
        Transform found = null;

        foreach (var col in hits)
        {
            if (col.transform.root == transform.root) continue;
            if (ik != null && col.transform.root == ik.transform.root) continue;

            if (col.attachedRigidbody != null)
            {
                found = col.attachedRigidbody.transform;
                break;
            }
            found = col.transform.root != col.transform ? col.transform.root : col.transform;
            break;
        }

        // 4. 콜라이더가 비활성화된 경우 씬 내 렌더러 거리 기반 폴백 탐색
        if (found == null)
        {
            var allRenderers = FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None);
            float bestDist = 0.45f;
            foreach (var rend in allRenderers)
            {
                if (!rend.gameObject.activeInHierarchy) continue;
                if (rend.transform.root == transform.root) continue;
                if (ik != null && rend.transform.root == ik.transform.root) continue;

                float d = Vector3.Distance(rend.transform.position, searchCenter);
                if (d < bestDist)
                {
                    bestDist = d;
                    var rb = rend.GetComponentInParent<Rigidbody>();
                    found = (rb != null) ? rb.transform : (rend.transform.parent != null ? rend.transform.parent : rend.transform);
                }
            }
        }

        if (found != null)
        {
            targetPart = found;
            EnsurePlatformCollider(targetPart);
        }

        return targetPart;
    }

    /// <summary>
    /// 부품의 grap_platfrom_01 허브 또는 활성 렌더러 바운즈 중심의 월드 파지 좌표 산출
    /// </summary>
    public Vector3 GetPickPosition()
    {
        Transform part = DetectOrGetTargetPart();
        if (part != null)
        {
            Transform graspPlatform = part.Find("grap_platfrom_01");
            if (graspPlatform != null && graspPlatform.gameObject.activeInHierarchy)
            {
                return new Vector3(graspPlatform.position.x, graspPlatform.position.y + pickHeightOffset, graspPlatform.position.z);
            }

            // grap_platfrom_01이 비활성화된 경우, 활성 자식 렌더러들의 중심 좌표 산출
            var renderers = part.GetComponentsInChildren<Renderer>(false);
            if (renderers != null && renderers.Length > 0)
            {
                Bounds b = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++)
                {
                    b.Encapsulate(renderers[i].bounds);
                }
                return new Vector3(b.center.x, b.center.y + pickHeightOffset, b.center.z);
            }

            return new Vector3(part.position.x, part.position.y + 0.03f + pickHeightOffset, part.position.z);
        }

        if (pickPoint != null)
        {
            return pickPoint.position + new Vector3(0f, pickHeightOffset, 0f);
        }

        return transform.position;
    }

    /// <summary>
    /// 부품 방향에 따른 최적 손목 회전 각도(Yaw) 산출
    /// </summary>
    public float GetEffectiveYaw()
    {
        Transform part = DetectOrGetTargetPart();
        if (part == null) return defaultPickYaw;

        Vector3 valley = Vector3.ProjectOnPlane(part.up, Vector3.up).normalized;
        if (valley.sqrMagnitude < 0.001f)
        {
            valley = Vector3.ProjectOnPlane(part.forward, Vector3.up).normalized;
        }
        if (valley.sqrMagnitude < 0.001f) return defaultPickYaw;

        float angle = Mathf.Atan2(valley.x, valley.z) * Mathf.Rad2Deg;
        while (angle > 180f) angle -= 180f;
        while (angle < 0f) angle += 180f;

        float candidate1 = angle;
        float candidate2 = angle - 180f;
        return (Mathf.Abs(candidate1 - defaultPickYaw) <= Mathf.Abs(candidate2 - defaultPickYaw)) ? candidate1 : candidate2;
    }

    /// <summary>
    /// 폐기함(RejectBin) 자식 콜라이더 바운즈를 결합하여 최상단 개구부 상공의 투하 좌표 정밀 산출
    /// </summary>
    public Vector3 GetRejectBinDropPosition()
    {
        if (rejectBin != null)
        {
            var colliders = rejectBin.GetComponentsInChildren<Collider>();
            if (colliders != null && colliders.Length > 0)
            {
                Bounds combined = colliders[0].bounds;
                for (int i = 1; i < colliders.Length; i++)
                {
                    combined.Encapsulate(colliders[i].bounds);
                }
                return new Vector3(combined.center.x, combined.max.y + dropHeightOffset, combined.center.z);
            }

            return new Vector3(rejectBin.position.x, rejectBin.position.y + dropHeightOffset, rejectBin.position.z);
        }

        if (rejectPose != null)
        {
            return rejectPose.position + Vector3.up * liftHeight;
        }

        Transform baseTr = (ik != null ? ik.transform : transform);
        return baseTr.position + baseTr.rotation * new Vector3(0.93f, 0.65f, -0.30f);
    }

    /// <summary>
    /// 부품이 현재 그리퍼에 결합(Attach)되어 들려있는 상태인지 확인
    /// </summary>
    public bool IsPartAttachedToGripper()
    {
        if (targetPart == null) return false;
        Transform parentPoint = (gripper != null && gripper.attachPoint != null)
            ? gripper.attachPoint
            : (ik != null && ik.tip != null ? ik.tip : null);

        return parentPoint != null && (targetPart.parent == parentPoint || targetPart.IsChildOf(parentPoint));
    }

    /// <summary>
    /// 그리퍼 끝에 부품을 결합(Attach)하여 리프팅 및 선회 이동 중 완벽 고정
    /// </summary>
    public void AttachPartToGripper()
    {
        Transform part = DetectOrGetTargetPart();
        if (part == null) return;

        Transform parentPoint = (gripper != null && gripper.attachPoint != null)
            ? gripper.attachPoint
            : (ik != null && ik.tip != null ? ik.tip : transform);

        // 컨베이어 무버가 켜져 있으면 부품 위치를 강제 덮어쓰므로 비활성화
        var mover = part.GetComponentInParent<ConveyorItemMover>();
        if (mover == null) mover = part.GetComponentInChildren<ConveyorItemMover>();
        if (mover != null) mover.enabled = false;

        var rb = part.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.isKinematic = true;
            rb.useGravity = false;
        }

        part.SetParent(parentPoint, true);
        Debug.Log($"[InspectRejectSequence] 부품 '{part.name}'을 '{parentPoint.name}'에 결합(Attach) 완료.");
    }

    /// <summary>
    /// 폐기함 상공에서 부품 결합을 해제하고 중력 및 하향 초속을 부여하여 자연스러운 물리 낙하 보장
    /// </summary>
    public void ReleasePartWithGravity(out string releasedPartId, out DefectType releasedDefect)
    {
        releasedPartId = "DISCARDED_PART";
        releasedDefect = DefectType.None;

        Transform part = targetPart;
        if (part != null)
        {
            var info = part.GetComponentInChildren<PartInfo>();
            if (info != null)
            {
                releasedPartId = !string.IsNullOrEmpty(info.id) ? info.id : part.name;
                releasedDefect = info.defectType;
            }
            else
            {
                releasedPartId = part.name;
            }

            part.SetParent(null, true);
            var rb = part.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.isKinematic = false;
                rb.useGravity = true;
                rb.linearVelocity = Vector3.down * 0.4f; // 자연스러운 하향 낙하 초속 부여
            }
            targetPart = null;
        }
    }

    public void ReleasePartWithGravity()
    {
        ReleasePartWithGravity(out _, out _);
    }

    /// <summary>
    /// 불량 부품을 수직으로 파지하여 폐기함 상공으로 운반 후 중력 낙하 투하하는 코루틴
    /// </summary>
    public IEnumerator Reject()
    {
        if (IsBusy) yield break;
        IsBusy = true;
        LastRejectOK = false;
        SetState("폐기 시퀀스 시작");

        AutoFindReferences();
        ApplyGripperCloseTarget();

        if (ik == null)
        {
            Debug.LogError("[InspectRejectSequence] ArmIK가 연결되지 않아 시퀀스를 중단합니다.");
            SetState("오류: ArmIK 누락");
            IsBusy = false;
            yield break;
        }

        // ArmIK 초기화 및 부호 캘리브레이션 대기
        float waitTimer = 0f;
        while (!ik.Ready && waitTimer < 3f)
        {
            waitTimer += Time.deltaTime;
            yield return null;
        }

        // 1. 초기 그리퍼 개방
        SetState("1. 그리퍼 개방");
        if (gripper != null)
        {
            yield return gripper.Open();
        }

        DetectOrGetTargetPart();
        Vector3 pickPos = GetPickPosition();
        Vector3 pickApproach = pickPos + Vector3.up * liftHeight;
        float targetYaw = GetEffectiveYaw();

        // 수직 하향 자세 고정
        ik.pointDown = true;
        ik.useTargetLookDir = false;

        // 2. 상공 안전 고도(Elbow-Up) 경유 후 픽업 상공 접근
        SetState($"2. 상공 경유 수직 픽업 대기 위치 접근 (손목: {targetYaw:F1}°)");
        float safeTransitY = Mathf.Max(pickApproach.y + 0.15f, ik.transform.position.y + safeTransitHeightOffset);
        Vector3 highSafeTransit = new Vector3(pickApproach.x, safeTransitY, pickApproach.z);
        yield return ik.MoveTo(highSafeTransit, null);
        yield return ik.MoveLinearSmooth(pickApproach, moveSpeed, targetYaw, false);

        bool grasped = false;

        // 3. 수직 하강 및 파지 루프
        for (int attempt = 1; attempt <= maxGraspTries; attempt++)
        {
            SetState($"3. 수직 하강 및 그리퍼 파지 (시도 {attempt}/{maxGraspTries})");
            ik.pointDown = true;
            ik.useTargetLookDir = false;
            yield return ik.MoveLinearSmooth(pickPos, moveSpeed, targetYaw, false);

            if (gripper != null)
            {
                yield return gripper.Close();
                AttachPartToGripper();

                // 부품이 그리퍼에 결합되었거나 접촉 판정이 성공했으면 파지 성공
                if (IsPartAttachedToGripper() || gripper.LastGraspOK)
                {
                    grasped = true;
                    break;
                }

                Debug.LogWarning($"[InspectRejectSequence] 파지 실패 ({attempt}/{maxGraspTries}) - 재시도");
                yield return gripper.Open();
                yield return ik.MoveLinearSmooth(pickApproach, moveSpeed, targetYaw, false);
            }
            else
            {
                AttachPartToGripper();
                grasped = true;
                break;
            }
        }

        // 최대 시도 횟수 초과 시
        if (!grasped)
        {
            Debug.LogError("[InspectRejectSequence] 파지 최대 재시도 초과 - 폐기 실패");
            SetState("오류: 파지 실패");
            yield return ik.MoveLinearSmooth(pickApproach, moveSpeed, targetYaw, false);
            yield return ik.ReturnToHomeJoints();
            IsBusy = false;
            yield break;
        }

        // 4. 수직 리프팅 (Lift)
        SetState("4. 수직 리프팅");
        AttachPartToGripper(); // 리프팅 직전 재확인
        ik.pointDown = true;
        ik.useTargetLookDir = false;
        yield return ik.MoveLinearSmooth(pickApproach, moveSpeed, targetYaw);

        // 5. 폐기함 상공 개구부로 선회 이동
        Vector3 dropPos = GetRejectBinDropPosition();
        SetState($"5. 폐기함 상공({dropPos.x:F2}, {dropPos.y:F2}, {dropPos.z:F2})으로 선회 이동");
        ik.pointDown = true;
        ik.useTargetLookDir = false;
        yield return ik.MoveTo(dropPos, null);
        yield return new WaitForSeconds(0.15f);

        // 6. 그리퍼 개방 및 중력 투하 (Gravity Drop)
        SetState("6. 폐기함으로 중력 투하 (Gravity Drop)");
        if (gripper != null)
        {
            yield return gripper.Open();
        }
        ReleasePartWithGravity(out string discardedPartId, out DefectType discardedDefect);
        yield return new WaitForSeconds(0.5f);

        // 7. 폐기 카운트 집계 및 이벤트 버스 발행
        ProcessCounters.Add(CounterType.DiscardedClaw);
        InspectEventBus.PublishExit(new PartExitEvent(discardedPartId, ExitReason.Discarded, discardedDefect, Time.time));
        Debug.LogWarning($"[InspectRejectSequence] 📢 폐기 배출 이벤트 발행 완료: {discardedPartId} (불량: {discardedDefect})");

        // 8. 홈 대기 자세로 복귀 (상공에서 바로 안전 복귀하므로 벽 충돌 없음)
        SetState("8. 대기 자세 복귀");
        ik.pointDown = true;
        ik.useTargetLookDir = false;
        yield return ik.ReturnToHomeJoints();

        LastRejectOK = true;
        SetState("폐기 완료");
        IsBusy = false;
    }
}
