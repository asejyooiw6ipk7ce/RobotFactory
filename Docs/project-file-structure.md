# 프로젝트 파일 구조도

기준
- 클래스 다이어그램: `class-diagram.mmd` (최신 버전, 로봇1 폐기·이탈 이벤트 반영)
- 원본 패키지: `CellFactory_pack.unitypackage` (`Assets/Scripts/Factory/` 15개 스크립트 확인)

표시
- **[원본]** 수정 없이 그대로 씀
- **[수정]** 원본을 가져와 SRS에 맞게 고침 (필요하면 파일 이름 변경)
- **[신규]** 원본에 없음
- **[선택]** 시간 내 구현 시에만 추가 (ML, 담당자별 1개씩)

## 1. 파일 트리

```
Assets/
├─ Scripts/
│  ├─ Control/
│  │  ├─ ControlPanel.cs             [수정]  ← Cell/CellOperatorUI.cs
│  │  └─ ProcessController.cs        [수정]  ← Cell/CellController.cs
│  │
│  ├─ PartSupply/
│  │  ├─ PartSpawner.cs              [수정]  ← Cell/ProductionOrder.cs (SupplyPart 등 생성 함수)
│  │  ├─ ConveyorLine.cs             [수정]  ← Cell/ConveyorLine.cs
│  │  ├─ RejectTote.cs               [원본]  ← Cell/NgTote.cs (이름만 변경)
│  │  ├─ PartInfo.cs                 [수정]  ← Cell/PartInfo.cs
│  │  └─ BaseInfo.cs                 [수정]  ← Cell/BaseInfo.cs
│  │
│  ├─ Inspection/
│  │  ├─ InspectStation.cs           [신규]
│  │  ├─ InspectRejectSequence.cs    [신규]  로봇1 폐기
│  │  ├─ FinishedProductInspector.cs [신규]
│  │  └─ AngleErrorInjector.cs       [신규]
│  │
│  ├─ Assembly/
│  │  ├─ AssemblySequence.cs         [수정]  ← Sequence/AssemblySequence.cs
│  │  ├─ ArmIK.cs                    [원본]  ← Robot/ArmIK.cs
│  │  ├─ GripperController.cs        [수정]  ← Robot/GripperController.cs (Attach·Detach 추가)
│  │  └─ FingerContact.cs            [원본]  ← Robot/FingerContact.cs
│  │
│  ├─ Events/
│  │  ├─ InspectEventBus.cs          [신규]  검사 이벤트 + 이탈 이벤트 채널
│  │  ├─ InspectEvent.cs             [신규]
│  │  └─ PartExitEvent.cs            [신규]
│  │
│  ├─ Dashboard/
│  │  ├─ StatsTracker.cs             [신규]
│  │  ├─ AlarmMonitor.cs             [신규]
│  │  ├─ RobotStatusTracker.cs       [신규]
│  │  └─ Dashboard.cs                [신규]
│  │
│  ├─ Common/                        열거형 (파일 하나에 하나씩)
│  │  ├─ PartKind.cs                 [수정]  PartInfo.cs의 PartType에서 분리
│  │  ├─ DefectType.cs               [수정]  AssemblySequence.cs에서 분리
│  │  ├─ InspectResult.cs            [신규]
│  │  ├─ StationId.cs                [신규]
│  │  ├─ RobotState.cs               [신규]
│  │  └─ ExitReason.cs               [신규]
│  │
│
├─ ML/                             [선택]  ML-Agents 패키지 필요
│  ├─ PartGraspAgent.cs            [선택]  강유나: 부품 집기 (← Factory/ML/AssemblyGraspAgent.cs 참고)
│  ├─ PartInsertAgent.cs           [선택]  박창민: 조립 삽입 보정
│  └─ DefectVisionAgent.cs         [선택]  조성민: 카메라 정상/불량 판단 (← Factory/ML/InspectAgent.cs 참고)
│
├─ Scenes/
│  └─ Main.unity                     패키지 씬을 기반으로 작업
│
├─ 04_Prefabs/
│  ├─ CubePart.prefab                부품 (PartInfo)
│  ├─ PrismPart.prefab               부품 (PartInfo)
│  ├─ BaseSquare.prefab              베이스 (BaseInfo)
│  └─ BasePrism.prefab               베이스 (BaseInfo)
│
├─ 05_Materials/                     M_Table, M_Red, M_Blue, M_BaseTop, M_BaseFloor
├─ Models/TriangularPrism.obj        삼각기둥 부품 메시
├─ realvirtual/                      컨베이어 모델·재질
├─ niryo_one/                        로봇 URDF·메시 (ArmIK가 사용)
├─ Fonts/NanumGothic SDF.asset       한글 폰트 (Dashboard 표시용)
├─ TextMesh Pro/                     TMP 기본 리소스
└─ Settings/SampleSceneProfile.asset 렌더링 설정
```

## 2. 원본 → 우리 파일 대응

| 우리 파일 | 원본 파일 | 구분 | 비고 |
| --- | --- | --- | --- |
| `Control/ControlPanel.cs` | `Cell/CellOperatorUI.cs` | 수정 | 주문 UI(`orderText`, `addOrderButton` 등) 제거 |
| `Control/ProcessController.cs` | `Cell/CellController.cs` | 수정 | ML 에이전트·OEE·알람·품번 교체 제거. 로봇1 폐기 호출 추가 |
| `PartSupply/PartSpawner.cs` | `Cell/ProductionOrder.cs` | 수정 | `SupplyPart` 계열 가져옴. `OnLineCount`, 이탈 구독 추가 |
| `PartSupply/ConveyorLine.cs` | `Cell/ConveyorLine.cs` | 수정 | 출하·불량 이동 시 이탈 이벤트 발행 추가 |
| `PartSupply/RejectTote.cs` | `Cell/NgTote.cs` | 원본 | 이름만 변경. 클래스 이름도 `RejectTote`로 바꿔야 파일 이름과 맞음 |
| `PartSupply/PartInfo.cs` | `Cell/PartInfo.cs` | 수정 | `PartType` 열거형은 `Common/PartKind.cs`로 분리 |
| `PartSupply/BaseInfo.cs` | `Cell/BaseInfo.cs` | 수정 | `socket` 추가 |
| `Assembly/AssemblySequence.cs` | `Sequence/AssemblySequence.cs` | 수정 | `DefectType`은 `Common/`로 분리. `InspectClass`는 TODO 결정까지 이 파일에 둠 |
| `Assembly/ArmIK.cs` | `Robot/ArmIK.cs` | 원본 | |
| `Assembly/GripperController.cs` | `Robot/GripperController.cs` | 수정 | `Attach`, `Detach` 추가 |
| `Assembly/FingerContact.cs` | `Robot/FingerContact.cs` | 원본 | |

## 3. 작업 전 확인할 점

1. **파일 이름 = 클래스 이름**: Unity는 MonoBehaviour 파일 이름과 클래스 이름이 같아야 합니다. `CellController.cs`를 `ProcessController.cs`로 바꿀 때 클래스 이름도 함께 바꿉니다. 같은 이름의 클래스가 두 개 있으면 컴파일 오류가 납니다. 그래서 원본을 복사해 두 벌 두지 말고 작업 폴더에서 옮깁니다.
2. **원본 패키지는 건드리지 않습니다**: `CellFactory_pack.unitypackage`는 보관용으로 두고, 작업 복사본만 수정합니다.
3. **원본과 다이어그램이 겹치는 부분**: 아래 항목은 다이어그램을 고치기 전에 원본 코드를 먼저 봐야 합니다.
   - `GripperController`에 이미 `attachPoint`(부품을 붙일 위치)와 파지 판정이 있습니다. 새 `Attach`·`Detach`가 이것과 겹치지 않는지 확인합니다.
   - `BaseInfo`에 이미 `HoleYaw`(구멍 방향)가 있습니다. `socket.forward`와 중복될 수 있습니다.
   - 원본 `DefectType`은 `{None, Offset, WrongPart, Missing}`이고, 다이어그램은 `{None, PartLoss, AngleError}`입니다. 값을 바꾸면 `Offset`, `WrongPart`를 쓰는 코드도 함께 고쳐야 합니다.
   - 원본 `InspectClass {OK, Missing, Defect}`는 다이어그램 TODO의 `EvaluateResult`에 해당합니다. 위치를 정하면 이 열거형을 옮깁니다.
