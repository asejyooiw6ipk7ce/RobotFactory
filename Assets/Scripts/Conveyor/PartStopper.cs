using System.Collections;
using UnityEngine;

/// <summary>
/// 컨베이어 벨트 상의 부품을 물리적으로 가로막거나 통과시키는 스토퍼 바 제어 컴포넌트.
/// 시작 시 벨트 아래(Down)로 내려가 있다가, 부품 도착 시 올라와서(Up) 막고, 출발 시 다시 내려갑니다.
/// </summary>
public class PartStopper : MonoBehaviour
{
    public enum PositionMode
    {
        Relative, // 사용자가 씬에 배치한 초기 위치(벨트 밑)를 기준으로 상대 높이만큼 올림 (권장)
        Absolute  // 직접 입력한 World/Local Y 좌표로 이동
    }

    [Header("스토퍼 대상 Transform")]
    [Tooltip("실제로 위아래로 움직일 스토퍼 바 오브젝트 (비어있으면 현재 오브젝트)")]
    public Transform stopper;

    [Header("이동 방식 설정")]
    [Tooltip("Relative: 씬의 현재 위치를 내려간 위치로 자동 인식하고 liftHeight만큼 올라감 (가장 편리)\nAbsolute: 아래 upY, downY 좌표로 직접 이동")]
    public PositionMode positionMode = PositionMode.Relative;

    [Tooltip("부모 기준 로컬 좌표계 사용 여부 (권장)")]
    public bool useLocalPosition = true;

    [Header("상대 이동 설정 (Relative 모드)")]
    [Tooltip("스토퍼가 위로 솟아오를 높이 (m) - 기본 위치에서 이만큼 위로 올라옵니다.")]
    public float liftHeight = 0.25f;

    [Header("절대 이동 좌표 (Absolute 모드)")]
    [Tooltip("스토퍼가 올라왔을 때의 Y 좌표 (부품 가로막음)")]
    public float stopperUpY = 0.95f;

    [Tooltip("스토퍼가 내려갔을 때의 Y 좌표 (벨트 밑 숨김, 통과 허용)")]
    public float stopperDownY = 0.65f;

    [Header("동작 속도 및 초기 상태")]
    [Tooltip("올라가고 내려가는 데 걸리는 시간 (초)")]
    public float stopperMoveTime = 0.25f;

    [Tooltip("게임 시작 시 스토퍼를 내려간 상태(벨트 밑)로 시작할지 여부")]
    public bool startDown = true;

    [Header("실시간 상태")]
    [SerializeField] private bool stopperMoving;
    [SerializeField] private bool isStopperUp = false;

    // 실제 이동 계산용 캐시 좌표
    private float cachedDownY;
    private float cachedUpY;
    private bool isInitialized = false;

    public bool StopperUp => isStopperUp;
    public bool IsMoving => stopperMoving;

    private void Awake()
    {
        if (stopper == null)
            stopper = transform;

        InitializePositions();
    }

    private void Start()
    {
        if (startDown)
        {
            // 시작 시 벨트 밑(내려간 상태)으로 즉시 배치
            SetYPosition(cachedDownY);
            isStopperUp = false;
        }
        else
        {
            SetYPosition(cachedUpY);
            isStopperUp = true;
        }
    }

    /// <summary>
    /// 모드에 따른 Up/Down Y 좌표를 초기화합니다.
    /// </summary>
    public void InitializePositions()
    {
        if (stopper == null)
            stopper = transform;

        float currentY = useLocalPosition ? stopper.localPosition.y : stopper.position.y;

        if (positionMode == PositionMode.Relative)
        {
            // 사용자가 씬에서 벨트 밑에 둔 현재 위치를 내려간 위치(Down)로 기억!
            cachedDownY = currentY;
            cachedUpY = currentY + Mathf.Abs(liftHeight);
        }
        else
        {
            cachedDownY = stopperDownY;
            cachedUpY = stopperUpY;
        }

        isInitialized = true;
    }

    private float GetCurrentY()
    {
        if (stopper == null) return 0f;
        return useLocalPosition ? stopper.localPosition.y : stopper.position.y;
    }

    private void SetYPosition(float y)
    {
        if (stopper == null) return;

        if (useLocalPosition)
        {
            Vector3 pos = stopper.localPosition;
            stopper.localPosition = new Vector3(pos.x, y, pos.z);
        }
        else
        {
            Vector3 pos = stopper.position;
            stopper.position = new Vector3(pos.x, y, pos.z);
        }
    }

    /// <summary>
    /// 스토퍼를 부드럽게 올리거나 내리는 코루틴
    /// </summary>
    private IEnumerator MoveStopper(bool up)
    {
        if (stopper == null) yield break;
        if (!isInitialized) InitializePositions();

        stopperMoving = true;

        float from = GetCurrentY();
        float to = up ? cachedUpY : cachedDownY;

        float elapsed = 0f;
        while (elapsed < stopperMoveTime)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / stopperMoveTime);
            // 부드러운 감속 효과 적용 (SmoothStep)
            float smoothT = Mathf.SmoothStep(0f, 1f, t);
            float newY = Mathf.Lerp(from, to, smoothT);

            SetYPosition(newY);
            yield return null;
        }

        SetYPosition(to);
        isStopperUp = up;
        stopperMoving = false;
    }

    /// <summary>
    /// [올리기] 스토퍼를 위로 올려 부품을 가로막습니다. (부품 정지 시 호출)
    /// </summary>
    public IEnumerator Stop()
    {
        while (stopperMoving) yield return null;
        if (isStopperUp) yield break; // 이미 올라와 있으면 중복 실행 안 함

        yield return MoveStopper(true);
    }

    /// <summary>
    /// [내리기] 스토퍼를 벨트 밑으로 내려 부품을 통과시킵니다. (부품 출발 시 호출)
    /// </summary>
    public IEnumerator Resume()
    {
        while (stopperMoving) yield return null;
        if (!isStopperUp) yield break; // 이미 내려가 있으면 중복 실행 안 함

        yield return MoveStopper(false);
    }

    /// <summary>
    /// 외부에서 즉시 상태를 설정할 때 사용합니다. (애니메이션 없이 순간이동)
    /// </summary>
    public void SetImmediateState(bool up)
    {
        if (!isInitialized) InitializePositions();
        StopAllCoroutines();
        stopperMoving = false;
        isStopperUp = up;
        SetYPosition(up ? cachedUpY : cachedDownY);
    }

    // ==========================================
    // 에디터 편의용 기능
    // </summary>
    public void CaptureCurrentAsDown()
    {
        if (stopper == null) stopper = transform;
        float currentY = GetCurrentY();
        stopperDownY = currentY;
        cachedDownY = currentY;
        Debug.Log($"[PartStopper] 현재 Y({currentY:F3})를 '내려간 위치(Down)'로 저장했습니다.");
    }

    public void CaptureCurrentAsUp()
    {
        if (stopper == null) stopper = transform;
        float currentY = GetCurrentY();
        stopperUpY = currentY;
        cachedUpY = currentY;
        Debug.Log($"[PartStopper] 현재 Y({currentY:F3})를 '올라간 위치(Up)'로 저장했습니다.");
    }

    // Reset: stop any movement and put the stopper back in the down position
    public void ForceDown()
    {
        StopAllCoroutines();
        stopperMoving = false;
        isStopperUp = false;
        if (stopper != null)
            stopper.position = new Vector3(stopper.position.x, stopperDownY, stopper.position.z);
    }
}
