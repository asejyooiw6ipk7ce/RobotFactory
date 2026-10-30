```mermaid
classDiagram
direction LR

%% ===== Unity 기반 클래스 (외부) =====
class MonoBehaviour {
    <<Unity>>
}

%% ===== 열거형 (Common 폴더) =====
namespace Common {
class PartKind {
    <<enumeration>>
    Claw
    RobotModule
}
class DefectType {
    <<enumeration>>
    None
    PartLoss
    AngleError
}
class InspectResult {
    <<enumeration>>
    Pass
    Fail
}
class StationId {
    <<enumeration>>
    Station1
    Station2
    Station3
}
class RobotState {
    <<enumeration>>
    Idle
    Moving
    Working
    Assembling
    Error
}
class ExitReason {
    <<enumeration>>
    Shipped
    Defective
    Discarded
}
class CounterType {
    <<enumeration>>
    Input
    Inspected
    Discarded
    Defective
    Shipped
}
}

%% ===== 제어 (Control 폴더) =====
namespace Control {
    class ControlPanel {
        <<adapt>>
        -ProcessController cycle
        -PartSpawner spawner
        +Start() void
        +Pause() void
        +Reset() void
        +SetDefectRates(float claw, float module, float joint) void
    }
    class ProcessController {
        <<new>>
        -bool paused
        -float simTime
        +Run() void
        +Pause() void
        +Reset() void
        -RunCycle() IEnumerator
    }
}

%% ===== 부품 공급과 이송 (PartSupply 폴더) =====
namespace PartSupply {
    class PartSpawner {
        <<new>>
        -float clawDefectRate
        -float moduleDefectRate
        -float jointDefectRate
        +int OnLineCount
        +EnsureOnLines() void
        +Spawn(PartKind type) PartBase
        -OnPartExited(PartExitEvent e) void
    }
    class ConveyorLine {
        <<reuse>>
        +Transform stopPoint
        +float speed
        +GameObject[] prefabs
        +RejectTote ngTote
        +bool stopperUp
        +Feed() IEnumerator
        +Release(bool toNg) IEnumerator
        +PushIntoTote() IEnumerator
    }
    class RejectTote {
        <<reuse>>
        +int Count
        +Vector3 NextSlot()
        +Add(List~Transform~ products) void
    }
    class PartBase {
        <<new>>
        +string id
        +DefectType defectType
        +PartKind Kind
    }
    class PartInfo {
        <<adapt>>
        +float halfHeight
        +float symmetryDeg
        +float gripOffset
        +float assemblyAngleError
    }
    class BaseInfo {
        <<adapt>>
        +float floorHeight
        +float topHeight
        +Vector3 holeCenter
        +Transform socket
        +bool Accepts(PartBase part)
    }
}

%% ===== 검사 (Inspection 폴더) =====
namespace Inspection {
    class InspectStation {
        <<new>>
        +StationId id
        -int inspectCount
        +Inspect(PartBase part) InspectResult
    }
    class InspectRejectSequence {
        <<new>>
        +ArmIK ik
        +GripperController gripper
        +Transform discardPoint
        +Discard(PartInfo part) IEnumerator
    }
    class FinishedProductInspector {
        <<new>>
        +float limitDeg
        +Judge(PartInfo part) InspectResult
    }
    class AngleErrorInjector {
        <<new>>
        +float defectRate
        +float minDeg
        +float maxDeg
        +Apply(PartInfo part) float
    }
}

%% ===== 조립 (Assembly 폴더) =====
namespace Assembly {
    class AssemblySequence {
        <<adapt>>
        -PartInfo currentPartInfo
        -BaseInfo currentBase
        +ArmIK ik
        +GripperController gripper
        +Transform basePoint
        +Transform rejectBin
        +Assemble(float graspYaw) IEnumerator
        +Reject(bool keepDropped) IEnumerator
        +Refeed() IEnumerator
    }
    class ArmIK {
        <<reuse>>
        +Transform tip
        +ArticulationBody[] joints
        +MoveTo(Vector3 goal, float yawDeg) IEnumerator
        +MoveLinear(Vector3 goal, float speed) IEnumerator
    }
    class GripperController {
        <<reuse>>
        +ArticulationBody rightFinger
        +ArticulationBody leftFinger
        +FingerContact rightContact
        +FingerContact leftContact
        +Close() IEnumerator
        +Open() IEnumerator
        +Attach(Transform part) void
        +Detach() void
    }
    class FingerContact {
        <<reuse>>
        +float holdTime
        +Clear() void
    }
}

%% ===== 이벤트 (Events 폴더) =====
namespace Events {
    class InspectEventBus {
        <<new>>
        +Action~InspectEvent~ InspectionCompleted
        +Action~PartExitEvent~ PartExited
        +Action~CounterType~ CounterAdded
        +Action~int, RobotState~ RobotStateChanged
        +PublishInspection(InspectEvent e)$ void
        +PublishPartExit(PartExitEvent e)$ void
        +PublishCounter(CounterType type)$ void
        +PublishRobotState(int robotId, RobotState state)$ void
    }
    class ProcessCounters {
        <<new>>
        +Add(CounterType type)$ void
    }
    class InspectEvent {
        <<new>>
        +string partId
        +StationId stationId
        +InspectResult result
        +DefectType defectType
        +float timestamp
    }
    class PartExitEvent {
        <<new>>
        +string partId
        +ExitReason reason
        +DefectType defectType
        +float timestamp
    }
}

%% ===== 대시보드 (Dashboard 폴더) =====
namespace Dashboard {
    class StatsTracker {
        <<new>>
        +Nullable~double~ FinalYieldRate
        +Nullable~double~ StationDefectRate(StationId station)
        +int Count(CounterType type)
        +Record(InspectEvent e) void
        +Increment(CounterType type) void
    }
    class AlarmMonitor {
        <<new>>
        -StatsTracker stats
        -float thresholdPercent
        -int minInspections
        +IReadOnlyDictionary~StationId, double~ Active
        +Evaluate(StationId station) void
    }
    class RobotStatusTracker {
        <<new>>
        -Dictionary~int, RobotState~ states
        +Set(int robotId, RobotState state)$ void
        +Apply(int robotId, RobotState state) void
        +Get(int robotId) RobotState
    }
    class Dashboard {
        <<new>>
        -UIDocument document
        -float alertThresholdPercent
        -int recentCount
        -int minInspectionsForAlert
        -StatsTracker stats
        -AlarmMonitor alarms
        -RobotStatusTracker robots
        -BindUI() void
        +Refresh() void
    }
}

%% ===== 상속 =====
MonoBehaviour <|-- ControlPanel
MonoBehaviour <|-- ProcessController
MonoBehaviour <|-- PartSpawner
MonoBehaviour <|-- ConveyorLine
MonoBehaviour <|-- RejectTote
MonoBehaviour <|-- PartBase
PartBase <|-- PartInfo
PartBase <|-- BaseInfo
MonoBehaviour <|-- InspectStation
MonoBehaviour <|-- InspectRejectSequence
MonoBehaviour <|-- FinishedProductInspector
MonoBehaviour <|-- AngleErrorInjector
MonoBehaviour <|-- AssemblySequence
MonoBehaviour <|-- ArmIK
MonoBehaviour <|-- GripperController
MonoBehaviour <|-- FingerContact
MonoBehaviour <|-- Dashboard

%% ===== 제어 관계 =====
ControlPanel --> ProcessController : 시작·정지·리셋 호출
ControlPanel --> PartSpawner : 불량 확률 설정
ProcessController "1" o-- "3" ConveyorLine : 컨베이어 A·B·C
ProcessController --> PartSpawner : 부품 보충 요청
ProcessController --> InspectStation : 스테이션 1·2 검사
ProcessController --> InspectRejectSequence : 로봇1 폐기 요청
ProcessController --> FinishedProductInspector : 완제품 판정
ProcessController --> AssemblySequence : 조립·폐기
ProcessController --> RobotStatusTracker : 로봇 상태 갱신

%% ===== 부품 공급과 이송 =====
PartSpawner ..> PartBase : 생성 (Claw → PartInfo, RobotModule → BaseInfo)
PartSpawner ..> InspectEventBus : 공정 이탈 이벤트 구독
ConveyorLine --> RejectTote : 불량 적재
ConveyorLine ..> InspectEventBus : 출하·불량 이동 이벤트 발행

%% ===== 검사 =====
InspectStation ..> PartBase : defectType 읽기
InspectStation ..> InspectEventBus : 검사 이벤트 발행
InspectStation --> ArmIK : 로봇1 검사 자세
InspectRejectSequence --> ArmIK : 로봇1 폐기 자세 IK
InspectRejectSequence --> GripperController : 그리퍼 Attach·Detach
InspectRejectSequence ..> PartInfo : 폐기 대상 부품
InspectRejectSequence ..> InspectEventBus : 폐기 이벤트 발행
FinishedProductInspector ..> PartInfo : assemblyAngleError 읽기
FinishedProductInspector ..> InspectEventBus : 검사 이벤트 발행

%% ===== 조립 =====
AssemblySequence --> PartInfo : 조립 대상 부품
AssemblySequence --> BaseInfo : 조립 대상 베이스 (socket 위치·접근 방향, Accepts 호출로 장착 가능 여부 확인)
AssemblySequence --> ArmIK : 로봇2 IK
AssemblySequence --> GripperController : 그리퍼 Attach·Detach
AssemblySequence ..> AngleErrorInjector : 각도 오차 적용
AssemblySequence ..> InspectEventBus : 폐기 이벤트 발행
GripperController *-- FingerContact : 손가락 접촉 감지

%% ===== 이벤트와 대시보드 =====
InspectEventBus ..> InspectEvent : 전달
InspectEventBus ..> PartExitEvent : 전달
InspectEventBus ..> StatsTracker : 이벤트 구독
InspectEventBus ..> AlarmMonitor : 이벤트 구독
InspectEventBus ..> PartSpawner : 이탈 이벤트 구독
StatsTracker ..> InspectEvent : Record 인자
ProcessCounters ..> InspectEventBus : 개수 이벤트 발행
PartExitEvent --> ExitReason
PartExitEvent --> DefectType
Dashboard --> StatsTracker : 수치 읽기
Dashboard --> AlarmMonitor : 알림 읽기
Dashboard --> RobotStatusTracker : 로봇 상태 읽기
RobotStatusTracker --> RobotState : 상태 값

%% ===== 열거형 사용 =====
PartBase --> PartKind
PartBase --> DefectType
InspectEvent --> InspectResult
InspectEvent --> StationId
InspectEvent --> DefectType
InspectStation ..> StationId
InspectStation ..> InspectResult
StatsTracker ..> CounterType : 처리 개수 종류
ProcessCounters ..> CounterType : 인자

%% ===== TODO 표시 =====
note for InspectStation "TODO: 검사 자세로 가는 방식(로봇1 IK 사용 여부)은 SRS에 없음"
note for InspectRejectSequence "TODO: 폐기 위치(폐기 빈 vs 불량 토트) SRS에 없음. 로봇1 그리퍼를 로봇2와 별도 인스턴스로 둘지 미정"
note for InspectEvent "구현: readonly struct (값 타입, 생성자로 값 설정)"
note for PartExitEvent "TODO: 출하 이벤트를 ConveyorLine에서 발행하는 것이 맞는지 확정 필요"
note for InspectEventBus "구현: static class, static event (구독은 +=, 발행은 Publish 메서드로만)"
note for InspectEventBus "TODO: 구독자 목록 관리 방식 미정. 지금은 의존으로 표시"
note for InspectEventBus "TODO: 검사 외 이벤트(출하·폐기)도 담게 되어 버스 이름 재검토 필요"
note for InspectEventBus "CounterAdded(CounterType), RobotStateChanged(int 로봇번호, RobotState)"
note for AssemblySequence "TODO: 패키지의 EvaluateResult (InspectClass 반환)를 어느 쪽에 둘지 미정"
note for AssemblySequence "TODO: Assemble은 currentBase.socket 위치·접근 방향으로 수평 삽입. 삽입 경로 구현 방식 미정"
note for Dashboard "구현: UIDocument의 root.Q<Label>(이름)으로 라벨을 찾고, 이벤트를 구독해 값을 갱신. 기준값은 인스펙터에서 설정"
note for Dashboard "UI 파일: Assets/UI/Dashboard/Dashboard.uxml (화면 구조), Dashboard.uss (스타일)"
note for StatsTracker "구현: 순수 C# 클래스 (MonoBehaviour 아님). 처리 개수는 CounterType 사전으로 셈"
note for AlarmMonitor "구현: 순수 C# 클래스 (MonoBehaviour 아님). 스테이션별 최근 불량률로 알림 판정"
note for RobotStatusTracker "구현: 순수 C# 클래스 (MonoBehaviour 아님). 로봇2만 Assembling 상태를 가질 수 있음"
note for FinishedProductInspector "TODO: 완제품 촬영(FR-18)은 제외. 카메라 참조 추가 여부 미정"
note for PartBase "abstract. Kind는 자식이 고정 (PartInfo=Claw, BaseInfo=RobotModule)"

%% ===== 스타일 =====
%% reuse: 초록
style ConveyorLine fill:#d4ecdc,stroke:#2e7d4f
style RejectTote fill:#d4ecdc,stroke:#2e7d4f
style ArmIK fill:#d4ecdc,stroke:#2e7d4f
style GripperController fill:#d4ecdc,stroke:#2e7d4f
style FingerContact fill:#d4ecdc,stroke:#2e7d4f
%% adapt: 노랑
style PartInfo fill:#fbe7b5,stroke:#b07a12
style BaseInfo fill:#fbe7b5,stroke:#b07a12
style AssemblySequence fill:#fbe7b5,stroke:#b07a12
style ControlPanel fill:#fbe7b5,stroke:#b07a12
style PartKind fill:#fbe7b5,stroke:#b07a12
style DefectType fill:#fbe7b5,stroke:#b07a12
%% new: 파랑
style ProcessController fill:#d3e3f7,stroke:#2f5d9e
style PartSpawner fill:#d3e3f7,stroke:#2f5d9e
style InspectStation fill:#d3e3f7,stroke:#2f5d9e
style InspectRejectSequence fill:#d3e3f7,stroke:#2f5d9e
style FinishedProductInspector fill:#d3e3f7,stroke:#2f5d9e
style AngleErrorInjector fill:#d3e3f7,stroke:#2f5d9e
style InspectEventBus fill:#d3e3f7,stroke:#2f5d9e
style InspectEvent fill:#d3e3f7,stroke:#2f5d9e
style PartExitEvent fill:#d3e3f7,stroke:#2f5d9e
style StatsTracker fill:#d3e3f7,stroke:#2f5d9e
style AlarmMonitor fill:#d3e3f7,stroke:#2f5d9e
style RobotStatusTracker fill:#d3e3f7,stroke:#2f5d9e
style Dashboard fill:#d3e3f7,stroke:#2f5d9e
style InspectResult fill:#d3e3f7,stroke:#2f5d9e
style StationId fill:#d3e3f7,stroke:#2f5d9e
style RobotState fill:#d3e3f7,stroke:#2f5d9e
style ExitReason fill:#d3e3f7,stroke:#2f5d9e
style CounterType fill:#d3e3f7,stroke:#2f5d9e
style PartBase fill:#d3e3f7,stroke:#2f5d9e
style ProcessCounters fill:#d3e3f7,stroke:#2f5d9e

```

## 0. CellFactory 원본과 구분

원본은 `CellFactory_pack.unitypackage`의 `Assets/Scripts/Factory/` 스크립트입니다. 파일 경로는 이 폴더 기준입니다.

구분 기준

- **reuse (그대로)**: 원본을 수정 없이 씀
- **adapt (변형)**: 원본을 SRS에 맞게 줄이거나 고쳐 씀, 또는 원본에 기능을 추가함
- **new (새로)**: 원본에 대응하는 클래스가 없음

| 우리 프로젝트 | 구분 | CellFactory 원본 클래스 | 원본 파일 | 처리 |
| --- | --- | --- | --- | --- |
| `ProcessController` | adapt | `CellController` | `Cell/CellController.cs` | 같은 역할을 함. 주문, OEE, 알람을 빼고 SRS 운전 순서만 남김. 로봇1 폐기 요청(`InspectRejectSequence`) 호출을 추가함 |
| `ControlPanel` | adapt | `CellOperatorUI` | `Cell/CellOperatorUI.cs` | 시작·정지·리셋과 불량 확률 입력만 씀. 주문 목록은 뺌 |
| `PartSpawner` | adapt | `ProductionOrder.SupplyPart`, `AssemblySequence.SpawnPart` | `Cell/ProductionOrder.cs`, `Sequence/AssemblySequence.cs` | 생성 방식은 가져오고, 보충 규칙은 SRS 기준으로 바꿈. 공정 위 부품 수(`OnLineCount`)를 두고 이탈 이벤트로 갱신함 |
| `ConveyorLine` | adapt | `ConveyorLine` | `Cell/ConveyorLine.cs` | 이송, 스토퍼, 푸셔는 그대로 씀. 출하·불량 이동 시 이탈 이벤트를 발행하도록 추가함 |
| `RejectTote` | reuse | `NgTote` | `Cell/NgTote.cs` | 불량 적재 역할이 같음. 이름만 바꿈 |
| `PartInfo` | adapt | `PartInfo` | `Cell/PartInfo.cs` | `PartBase`를 상속. 원본 `halfHeight`, `symmetryDeg`, `gripOffset`을 유지하고 각도 오차 필드(`assemblyAngleError`)를 추가함 |
| `BaseInfo` | adapt | `BaseInfo` | `Cell/BaseInfo.cs` | `PartBase`를 상속. 부품2(로봇모듈)가 베이스. 원본 `floorHeight`, `topHeight`를 유지하고, 구멍 중심(`holeCenter`)과 수평 조립용 `Transform socket`(위치와 접근 방향)을 추가함. 맞는 부품은 `Accepts`로 확인하므로 `holeType`은 두지 않음 |
| `PartBase` | new | 없음 | — | `PartInfo`와 `BaseInfo`의 공통 부모. `id`, `defectType`, `Kind`를 가지며, `Kind`는 자식이 고정함 |
| `PartKind` (열거형) | adapt | `PartType` (이름과 구조만 가져옴) | `Cell/PartInfo.cs` | 값은 원본 Cube, Prism 대신 역할인 Claw(부품1, `moto_part/claw.prefab`), RobotModule(부품2, `moto_bot 1/moto_base.prefab`)으로 바꿈. 모양 enum은 만들지 않음 |
| `DefectType` (열거형) | adapt | `DefectType` (Offset, WrongPart, Missing) | `Sequence/AssemblySequence.cs` | 값을 SRS 용어(일부 부품 상실, 결합 틀어짐)로 바꿈 |
| `AssemblySequence` | adapt | `AssemblySequence` | `Sequence/AssemblySequence.cs` | 원본은 수직 조립. SRS는 수평 조립이라 `socket` 기준으로 삽입하도록 바꿈. 폐기 시 이벤트를 발행함 |
| `ArmIK` | reuse | `ArmIK` | `Robot/ArmIK.cs` | 로봇 팔 역기구학을 그대로 씀 |
| `GripperController` | adapt | `GripperController` | `Robot/GripperController.cs` | 개폐 동작은 그대로 씀. `Attach(Transform)`, `Detach()`를 추가함 |
| `FingerContact` | reuse | `FingerContact` | `Robot/FingerContact.cs` | 손가락 접촉 감지를 그대로 씀 |
| `InspectRejectSequence` | new | 없음 (원본 확인 필요) | — | 로봇1이 검사 불량 부품을 집어 폐기함. 폐기 위치는 SRS에 없어 미정 |
| `InspectStation` | new | 없음 | — | 스테이션 검사와 결과 이벤트 발행. ML 에이전트(`BaseInspectAgent`)는 개념만 참고하고 쓰지 않음 |
| `FinishedProductInspector` | new | 없음 | — | 완제품 각도 3° 기준 판정 |
| `AngleErrorInjector` | new | 없음 | — | 조립 후 결합 각도 오차 생성 |
| `InspectEvent`, `InspectEventBus` | new | 없음 | — | 검사 결과와 이탈 이벤트를 다른 클래스에 전달 |
| `PartExitEvent` | new | 없음 | — | 부품이 공정을 떠날 때(출하, 불량 이동, 폐기) 발생하는 이벤트 |
| `ProcessCounters` | new | 없음 | — | 처리 개수 증가의 진입점. 다른 팀원이 `ProcessCounters.Add(CounterType)`으로 한 줄씩 올림 |
| `StatsTracker`, `AlarmMonitor`, `RobotStatusTracker`, `Dashboard` | new | 없음 | — | 처리 개수 집계(`CounterType`), 최종 양품률·스테이션 불량률, 이상 알림, 로봇 상태, 화면 표시 |
| `Dashboard.uxml`, `Dashboard.uss` | new | 없음 | `UI/Dashboard/` | 화면 구조(양품률·불량률·처리 개수 카드, 로봇 상태 뱃지, 알림 목록)와 스타일(다크 테마, 알림 빨간 테두리, Pretendard) |
| `InspectResult`, `StationId`, `RobotState`, `ExitReason`, `CounterType` (열거형) | new | 없음 | — | 새로 정의한 열거형 |

### 원본에 있지만 쓰지 않는 것

| CellFactory 원본 클래스 | 원본 파일 | 이유 |
| --- | --- | --- |
| `OrderBook` | `Cell/OrderBook.cs` | SRS에 주문 관리 요구사항이 없음 |
| `ProductionLog` | `Cell/ProductionLog.cs` | SRS 6장: 데이터를 파일에 저장하지 않음 |
| `BaseInspectAgent`, `InspectAgent`, `AssemblyGraspAgent` | `ML/*.cs` | ML 에이전트를 쓰지 않고, 검사는 플래그 읽기로 대체함 |
| `ProductionOrder` (`SupplyPart` 제외) | `Cell/ProductionOrder.cs` | 주문 흐름 전체는 쓰지 않음. 생성 함수만 `PartSpawner`에 가져옴 |
| `CellController`의 OEE, 알람, 품번 교체 시간 | `Cell/CellController.cs` | SRS에 없음 |

### 수치 요약

- reuse: 3개 (`RejectTote`, `ArmIK`, `FingerContact`)
- adapt: 10개 (`ProcessController`, `ControlPanel`, `PartSpawner`, `ConveyorLine`, `PartInfo`, `BaseInfo`, `PartKind`, `DefectType`, `AssemblySequence`, `GripperController`)
- new: 18개 (`InspectRejectSequence`, `InspectStation`, `FinishedProductInspector`, `AngleErrorInjector`, `InspectEvent`, `PartExitEvent`, `InspectEventBus`, `StatsTracker`, `AlarmMonitor`, `RobotStatusTracker`, `Dashboard`, `InspectResult`, `StationId`, `RobotState`, `ExitReason`, `PartBase`, `CounterType`, `ProcessCounters`)

### 변경 사유

- `GripperController`와 `ConveyorLine`은 원본을 수정하는 항목이 생겨 reuse에서 adapt로 옮겼습니다.
- `InspectRejectSequence`는 원본 `CellFactory`에 로봇 폐기 동작이 있는지 확인하지 않았습니다. 있다면 adapt로 바꾸고 원본 클래스를 적어야 합니다.
