// ==========================================================================================
// [인스펙터 테스트 GUI 활성화 플래그]
// 아래 #define ENABLE_INSPECTOR_TEST 앞에 슬래시 두 개(//)를 붙여 주석 처리하면
// 인스펙터의 모든 테스트 버튼 및 디버그 패널이 즉시 사라지고 기본 인스펙터로 전환됩니다.
// ==========================================================================================
#define ENABLE_INSPECTOR_TEST

#if UNITY_EDITOR && ENABLE_INSPECTOR_TEST
using UnityEditor;
using UnityEngine;

// ==========================================================================================
// [인스펙터 전용 CustomEditor]
// 코드 상단의 "#define ENABLE_INSPECTOR_TEST" 를 주석 처리하면 이 블록 전체가 비활성화되어
// 인스펙터에서 테스트 GUI가 완전히 사라집니다.
// ==========================================================================================
[CustomEditor(typeof(AssemblySequence))]
public class AssemblySequenceEditor : Editor
{
    public override void OnInspectorGUI()
    {
        // 1. 기본 인스펙터 프로퍼티 표시
        DrawDefaultInspector();

        AssemblySequence seq = (AssemblySequence)target;
        EditorGUILayout.Space(12);

        // 구분선
        EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);
        GUILayout.Label("🛠️ [AssemblySequence] 실시간 테스트 패널", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("💡 상단 '#define ENABLE_INSPECTOR_TEST' 를 주석 처리(//)하면\n이 테스트 패널이 인스펙터에서 완전히 사라집니다.", MessageType.Info);

        // 2. 참조 자동 연결 버튼
        EditorGUILayout.Space(6);
        if (GUILayout.Button("🔧 참조 자동 연결 및 모터 잠금 해제 (Auto Wire & Fix)", GUILayout.Height(30)))
        {
            seq.AutoFindReferences();
            seq.ApplyGripperCloseTarget();
            EditorUtility.SetDirty(seq);
            Debug.Log("[AssemblySequence] 자동 연결 및 모터 잠금 해제 완료!");
        }

        // 3. 그리퍼 개폐 단독 테스트
        EditorGUILayout.Space(8);
        GUILayout.Label("그리퍼 확장 개폐 단독 테스트", EditorStyles.boldLabel);
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("👐 그리퍼 열기 (Open)", GUILayout.Height(30)))
        {
            if (Application.isPlaying && seq.gripper != null)
                seq.StartCoroutine(seq.gripper.Open());
            else if (!Application.isPlaying)
                EditorUtility.DisplayDialog("알림", "Play(▶) 모드에서 테스트해주세요!", "확인");
        }
        if (GUILayout.Button("✊ 그리퍼 닫기 (Close)", GUILayout.Height(30)))
        {
            if (Application.isPlaying && seq.gripper != null)
                seq.StartCoroutine(seq.gripper.Close());
            else if (!Application.isPlaying)
                EditorUtility.DisplayDialog("알림", "Play(▶) 모드에서 테스트해주세요!", "확인");
        }
        GUILayout.EndHorizontal();

        // 4. 파지 폭 슬라이더
        EditorGUILayout.Space(8);
        EditorGUILayout.BeginVertical("box");
        GUILayout.Label("그리퍼 파지 폭 (관통 원천 방지)", EditorStyles.boldLabel);
        float newWidth = EditorGUILayout.Slider("목표 파지 간격 (mm)", seq.gripWidthMm, 30f, 60f);
        if (Mathf.Abs(newWidth - seq.gripWidthMm) > 0.1f)
        {
            seq.gripWidthMm = newWidth;
            seq.ApplyGripperCloseTarget();
            EditorUtility.SetDirty(seq);
        }
        EditorGUILayout.HelpBox($"허브 외경 맞춤 권장: 48mm ~ 50mm (현재 {seq.gripWidthMm:F0}mm 밀착 관통 차단)", MessageType.None);
        EditorGUILayout.EndVertical();

        // 5. 자세 적응형 파지 각도
        EditorGUILayout.Space(8);
        EditorGUILayout.BeginVertical("box");
        GUILayout.Label("자세 적응형 파지 각도 (실시간 감지)", EditorStyles.boldLabel);
        float currentYaw = seq.GetEffectiveYaw();
        EditorGUILayout.LabelField("감지된 최적 손목 각도:", $"{currentYaw:F1}°");

        if (GUILayout.Button("🎯 감지 각도로 손목 실시간 회전", GUILayout.Height(28)))
        {
            if (Application.isPlaying && seq.ik != null)
                seq.StartCoroutine(seq.RotateWristLive(currentYaw));
            else if (!Application.isPlaying)
                EditorUtility.DisplayDialog("알림", "Play(▶) 모드에서 테스트해주세요!", "확인");
        }
        EditorGUILayout.EndVertical();

        // 5-1. 4단계 소켓 수직 정렬 미세 조정
        EditorGUILayout.Space(8);
        EditorGUILayout.BeginVertical("box");
        GUILayout.Label("📐 4단계 소켓 수직 정렬 설정", EditorStyles.boldLabel);

        bool newAutoDetect = EditorGUILayout.Toggle("차체 회전 기준 삽입 방향 자동 추적", seq.autoDetectInsertDir);
        if (newAutoDetect != seq.autoDetectInsertDir)
        {
            seq.autoDetectInsertDir = newAutoDetect;
            EditorUtility.SetDirty(seq);
            SceneView.RepaintAll();
        }

        if (seq.autoDetectInsertDir)
        {
            seq.GetSlotInfo(out _, out _, out Vector3 liveDir);
            EditorGUILayout.LabelField("실시간 감지 삽입 방향 (insertDir):", $"{liveDir}");
        }
        else
        {
            Vector3 newInsertDir = EditorGUILayout.Vector3Field("소켓 삽입 방향 (insertDir):", seq.customInsertDir);
            if (newInsertDir != seq.customInsertDir)
            {
                seq.customInsertDir = newInsertDir;
                EditorUtility.SetDirty(seq);
                SceneView.RepaintAll();
            }
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("⬅ 왼쪽 (-X, 화살표)", GUILayout.Height(22))) { seq.customInsertDir = Vector3.left; EditorUtility.SetDirty(seq); SceneView.RepaintAll(); }
            if (GUILayout.Button("➡ 오른쪽 (+X)", GUILayout.Height(22))) { seq.customInsertDir = Vector3.right; EditorUtility.SetDirty(seq); SceneView.RepaintAll(); }
            if (GUILayout.Button("⬆ 전방 (+Z)", GUILayout.Height(22))) { seq.customInsertDir = Vector3.forward; EditorUtility.SetDirty(seq); SceneView.RepaintAll(); }
            GUILayout.EndHorizontal();
        }

        float newAlignYaw = EditorGUILayout.Slider("소켓 정렬 손목 각도 (alignWristYaw):", seq.alignWristYaw, 0f, 360f);
        if (Mathf.Abs(newAlignYaw - seq.alignWristYaw) > 0.1f)
        {
            seq.alignWristYaw = newAlignYaw;
            EditorUtility.SetDirty(seq);
        }

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("0° (수직 정면)", GUILayout.Height(24))) { seq.alignWristYaw = 0f; EditorUtility.SetDirty(seq); }
        if (GUILayout.Button("90°", GUILayout.Height(24))) { seq.alignWristYaw = 90f; EditorUtility.SetDirty(seq); }
        if (GUILayout.Button("180° (반전 180°)", GUILayout.Height(24))) { seq.alignWristYaw = 180f; EditorUtility.SetDirty(seq); }
        if (GUILayout.Button("270°", GUILayout.Height(24))) { seq.alignWristYaw = 270f; EditorUtility.SetDirty(seq); }
        GUILayout.EndHorizontal();

        float newDescendSpeed = EditorGUILayout.Slider("2단계 하강 속도 (m/s):", seq.descendSpeed, 0.03f, 0.50f);
        if (Mathf.Abs(newDescendSpeed - seq.descendSpeed) > 0.001f)
        {
            seq.descendSpeed = newDescendSpeed;
            EditorUtility.SetDirty(seq);
        }
        float newLiftSpeed = EditorGUILayout.Slider("3단계 상승 속도 (m/s):", seq.liftSpeed, 0.05f, 0.60f);
        if (Mathf.Abs(newLiftSpeed - seq.liftSpeed) > 0.001f)
        {
            seq.liftSpeed = newLiftSpeed;
            EditorUtility.SetDirty(seq);
        }
        float newRetractLiftSpeed = EditorGUILayout.Slider("6단계 복귀 상승 속도 (m/s):", seq.retractLiftSpeed, 0.05f, 0.60f);
        if (Mathf.Abs(newRetractLiftSpeed - seq.retractLiftSpeed) > 0.001f)
        {
            seq.retractLiftSpeed = newRetractLiftSpeed;
            EditorUtility.SetDirty(seq);
        }
        float newInsertDist = EditorGUILayout.Slider("소켓 앞 대기 유격 (m):", seq.insertDistance, 0.05f, 0.30f);
        if (Mathf.Abs(newInsertDist - seq.insertDistance) > 0.001f)
        {
            seq.insertDistance = newInsertDist;
            EditorUtility.SetDirty(seq);
        }
        float newClawOffset = EditorGUILayout.Slider("클로 돌출 길이 보정 (m):", seq.clawLengthOffset, 0.0f, 0.25f);
        if (Mathf.Abs(newClawOffset - seq.clawLengthOffset) > 0.001f)
        {
            seq.clawLengthOffset = newClawOffset;
            EditorUtility.SetDirty(seq);
        }
        float newMountOffset = EditorGUILayout.Slider("5단계 결합 깊이 보정 (m):", seq.socketMountOffset, 0.0f, 0.18f);
        if (Mathf.Abs(newMountOffset - seq.socketMountOffset) > 0.001f)
        {
            seq.socketMountOffset = newMountOffset;
            if (Application.isPlaying && seq.clawPart != null && !seq.IsClawHeldByGripper() && seq.targetRobotBase != null && seq.clawPart.parent == seq.targetRobotBase)
            {
                seq.GetSlotInfo(out Vector3 sc, out _, out Vector3 idir);
                seq.clawPart.position = seq.GetSocketMountPosition(sc, idir);
            }
            EditorUtility.SetDirty(seq);
        }
        EditorGUILayout.EndVertical();

        // 5-2. 작업 반경 및 씬 배치 진단 패널
        EditorGUILayout.Space(8);
        EditorGUILayout.BeginVertical("box");
        GUILayout.Label("🌐 로봇 작업 반경 및 공정 배치 진단 (Work Envelope)", EditorStyles.boldLabel);

        if (seq.ik != null)
        {
            // 작업 반경 원형 표시 On/Off 토글 버튼
            EditorGUILayout.BeginHorizontal();
            GUI.backgroundColor = seq.ik.showWorkRadius ? new Color(0.3f, 0.9f, 0.5f) : new Color(0.85f, 0.85f, 0.85f);
            string toggleBtnText = seq.ik.showWorkRadius ? "🟢 360° 작업 반경 표시 [ON]" : "⚪ 360° 작업 반경 표시 [OFF]";
            if (GUILayout.Button(toggleBtnText, GUILayout.Height(28)))
            {
                seq.ik.showWorkRadius = !seq.ik.showWorkRadius;
                EditorUtility.SetDirty(seq.ik);
                SceneView.RepaintAll();
            }
            GUI.backgroundColor = Color.white;

            bool newOnlySelected = EditorGUILayout.ToggleLeft("선택 시만 표시", seq.ik.showOnlyWhenSelected, GUILayout.Width(100));
            if (newOnlySelected != seq.ik.showOnlyWhenSelected)
            {
                seq.ik.showOnlyWhenSelected = newOnlySelected;
                EditorUtility.SetDirty(seq.ik);
                SceneView.RepaintAll();
            }
            EditorGUILayout.EndHorizontal();

            // 2D / 3D 뷰 모드 전환 버튼
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("시각화 차원:", GUILayout.Width(80));
            ArmIK.EnvelopeViewMode curMode = seq.ik.viewMode;
            if (GUILayout.Button("2D 바닥 평면", curMode == ArmIK.EnvelopeViewMode.Flat2D ? EditorStyles.miniButtonMid : EditorStyles.miniButton, GUILayout.Height(22)))
            {
                seq.ik.viewMode = ArmIK.EnvelopeViewMode.Flat2D;
                EditorUtility.SetDirty(seq.ik);
                SceneView.RepaintAll();
            }
            if (GUILayout.Button("3D 상공 돔", curMode == ArmIK.EnvelopeViewMode.Dome3D ? EditorStyles.miniButtonMid : EditorStyles.miniButton, GUILayout.Height(22)))
            {
                seq.ik.viewMode = ArmIK.EnvelopeViewMode.Dome3D;
                EditorUtility.SetDirty(seq.ik);
                SceneView.RepaintAll();
            }
            if (GUILayout.Button("3D 전체 구체", curMode == ArmIK.EnvelopeViewMode.Sphere3D ? EditorStyles.miniButtonMid : EditorStyles.miniButton, GUILayout.Height(22)))
            {
                seq.ik.viewMode = ArmIK.EnvelopeViewMode.Sphere3D;
                EditorUtility.SetDirty(seq.ik);
                SceneView.RepaintAll();
            }
            if (GUILayout.Button("2D+3D 동시", curMode == ArmIK.EnvelopeViewMode.Both ? EditorStyles.miniButtonMid : EditorStyles.miniButton, GUILayout.Height(22)))
            {
                seq.ik.viewMode = ArmIK.EnvelopeViewMode.Both;
                EditorUtility.SetDirty(seq.ik);
                SceneView.RepaintAll();
            }
            EditorGUILayout.EndHorizontal();

            float newMax = EditorGUILayout.Slider("최대 도달 반경 (m):", seq.ik.maxReachRadius, 0.8f, 2.0f);
            if (Mathf.Abs(newMax - seq.ik.maxReachRadius) > 0.005f) { seq.ik.maxReachRadius = newMax; EditorUtility.SetDirty(seq.ik); }
            float newRec = EditorGUILayout.Slider("권장 작업 반경 (m):", seq.ik.recommendedRadius, 0.5f, 1.8f);
            if (Mathf.Abs(newRec - seq.ik.recommendedRadius) > 0.005f) { seq.ik.recommendedRadius = newRec; EditorUtility.SetDirty(seq.ik); }

            // 공정 기준점 실시간 배치 진단
            EditorGUILayout.Space(4);
            GUILayout.Label("📍 주요 공정 배치 위치 도달 진단:", EditorStyles.miniBoldLabel);

            DrawTargetReachDiagnostic(seq.ik, "부품1 픽업 (Claw)", seq.partStopPoint != null ? seq.partStopPoint.position : (seq.clawPart != null ? seq.GetPickPosition() : (Vector3?)null));
            seq.GetSlotInfo(out Vector3 sc, out _, out _);
            DrawTargetReachDiagnostic(seq.ik, "부품2 소켓 (Socket)", sc != Vector3.zero ? (Vector3?)sc : null);
            DrawTargetReachDiagnostic(seq.ik, "불량 폐기함 (RejectBin)", seq.rejectBin != null ? (Vector3?)seq.rejectBin.position : null);
            if (seq.outputConveyorPoint != null)
                DrawTargetReachDiagnostic(seq.ik, "완제품 배출 (Conveyor C)", seq.outputConveyorPoint.position);

            EditorGUILayout.Space(4);
            bool newReachGizmo = EditorGUILayout.Toggle("배치 가이드 연결선 표시", seq.showReachabilityGizmos);
            if (newReachGizmo != seq.showReachabilityGizmos)
            {
                seq.showReachabilityGizmos = newReachGizmo;
                EditorUtility.SetDirty(seq);
                SceneView.RepaintAll();
            }
        }
        else
        {
            EditorGUILayout.HelpBox("로봇 ArmIK 컴포넌트가 연결되지 않아 작업 반경을 표시할 수 없습니다.\n위의 '참조 자동 연결' 버튼을 눌러주세요.", MessageType.Warning);
        }
        EditorGUILayout.EndVertical();

        // 6. 단계별 검증 및 연속 실행
        EditorGUILayout.Space(8);
        GUILayout.Label("📋 단계별 검증 및 연속 실행 (Step-by-Step)", EditorStyles.boldLabel);
        EditorGUILayout.BeginVertical("box");

        GUI.backgroundColor = new Color(0.4f, 0.8f, 1f);
        if (GUILayout.Button("🔄 부품 위치 및 로봇 초기화 (Reset Part & Arm)", GUILayout.Height(28)))
        {
            if (Application.isPlaying) seq.ResetToInitialState();
            else EditorUtility.DisplayDialog("알림", "Play(▶) 모드에서 실행해주세요!", "확인");
        }
        GUI.backgroundColor = Color.white;
        EditorGUILayout.Space(6);

        // ★ [연속 실행 메인 버튼]
        GUI.backgroundColor = seq.IsBusy ? Color.yellow : new Color(0.25f, 0.90f, 0.45f);
        string stepNameDesc;
        switch (seq.continuousStepTarget)
        {
            case 1: stepNameDesc = "1단계 (접근)"; break;
            case 2: stepNameDesc = "1~2단계 (접근 ➔ 파지)"; break;
            case 3: stepNameDesc = "1~3단계 (접근 ➔ 파지 ➔ 상공 리프트)"; break;
            case 4: stepNameDesc = "1~4단계 (접근 ➔ 파지 ➔ 리프트 ➔ 소켓 정렬)"; break;
            case 5: stepNameDesc = "1~5단계 (접근 ➔ 파지 ➔ 리프트 ➔ 정렬 ➔ 삽입)"; break;
            default: stepNameDesc = "1~6단계 (전체 조립 완료)"; break;
        }

        string btnText = seq.IsBusy
            ? $"동작 진행 중... ({seq.Status})"
            : $"⚡ {stepNameDesc} 연속 실행";

        if (GUILayout.Button(btnText, GUILayout.Height(34)))
        {
            if (Application.isPlaying) seq.RunStepsContinuous(seq.continuousStepTarget);
            else EditorUtility.DisplayDialog("알림", "Play(▶) 모드에서 실행해주세요!", "확인");
        }
        GUI.backgroundColor = Color.white;

        // 목표 단계 1클릭 변경 탭 버튼
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("연속 단계 선택:", GUILayout.Width(85));
        if (GUILayout.Button("1~3단계 (현재)", seq.continuousStepTarget == 3 ? EditorStyles.miniButtonMid : EditorStyles.miniButton, GUILayout.Height(20)))
        {
            seq.continuousStepTarget = 3;
            EditorUtility.SetDirty(seq);
        }
        if (GUILayout.Button("1~4단계", seq.continuousStepTarget == 4 ? EditorStyles.miniButtonMid : EditorStyles.miniButton, GUILayout.Height(20)))
        {
            seq.continuousStepTarget = 4;
            EditorUtility.SetDirty(seq);
        }
        if (GUILayout.Button("1~5단계", seq.continuousStepTarget == 5 ? EditorStyles.miniButtonMid : EditorStyles.miniButton, GUILayout.Height(20)))
        {
            seq.continuousStepTarget = 5;
            EditorUtility.SetDirty(seq);
        }
        if (GUILayout.Button("1~6단계", seq.continuousStepTarget == 6 ? EditorStyles.miniButtonMid : EditorStyles.miniButton, GUILayout.Height(20)))
        {
            seq.continuousStepTarget = 6;
            EditorUtility.SetDirty(seq);
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);
        GUILayout.Label("👇 개별 단계 단독 실행", EditorStyles.miniBoldLabel);

        if (GUILayout.Button("1단계. 클로 상공 접근 (Approach)", GUILayout.Height(26)))
        {
            if (Application.isPlaying) seq.RunStep1_Approach();
            else EditorUtility.DisplayDialog("알림", "Play(▶) 모드에서 실행해주세요!", "확인");
        }
        if (GUILayout.Button("2단계. 하강 및 그리퍼 파지 (Grasp)", GUILayout.Height(26)))
        {
            if (Application.isPlaying) seq.RunStep2_Grasp();
            else EditorUtility.DisplayDialog("알림", "Play(▶) 모드에서 실행해주세요!", "확인");
        }
        if (GUILayout.Button("3단계. 상공 들어올리기 (Lift)", GUILayout.Height(26)))
        {
            if (Application.isPlaying) seq.RunStep3_Lift();
            else EditorUtility.DisplayDialog("알림", "Play(▶) 모드에서 실행해주세요!", "확인");
        }
        if (GUILayout.Button("4단계. 소켓 앞 수평 정렬 (수평 전환 -> 베이스 회전)", GUILayout.Height(26)))
        {
            if (Application.isPlaying) seq.RunStep4_Align();
            else EditorUtility.DisplayDialog("알림", "Play(▶) 모드에서 실행해주세요!", "확인");
        }
        if (GUILayout.Button("5단계. 슬롯 수평 삽입 및 결합 (TestTarget 기준)", GUILayout.Height(26)))
        {
            if (Application.isPlaying) seq.RunStep5_Insert();
            else EditorUtility.DisplayDialog("알림", "Play(▶) 모드에서 실행해주세요!", "확인");
        }
        if (GUILayout.Button("6단계. 그리퍼 분리 및 대기 복귀 (Release & Retract)", GUILayout.Height(26)))
        {
            if (Application.isPlaying) seq.RunStep6_Release();
            else EditorUtility.DisplayDialog("알림", "Play(▶) 모드에서 실행해주세요!", "확인");
        }

        EditorGUILayout.EndVertical();

        // 7. 메인 시퀀스 실행 버튼들 (Assemble / Reject / Refeed)
        EditorGUILayout.Space(10);
        GUILayout.Label("🚀 메인 시퀀스 실행", EditorStyles.boldLabel);

        GUI.backgroundColor = seq.IsBusy ? Color.yellow : new Color(0.2f, 0.85f, 0.35f);
        if (GUILayout.Button(seq.IsBusy ? "시퀀스 동작 중..." : "▶ 전체 수평 조립 실행 (Assemble)", GUILayout.Height(36)))
        {
            if (Application.isPlaying) seq.StartCoroutine(seq.Assemble(-1f));
            else EditorUtility.DisplayDialog("알림", "Play(▶) 모드에서 실행해주세요!", "확인");
        }
        GUI.backgroundColor = Color.white;

        GUILayout.BeginHorizontal();
        GUI.backgroundColor = new Color(0.9f, 0.4f, 0.4f);
        if (GUILayout.Button("🗑️ 불량 부품 폐기 (Reject)", GUILayout.Height(30)))
        {
            if (Application.isPlaying) seq.StartCoroutine(seq.Reject(false));
            else EditorUtility.DisplayDialog("알림", "Play(▶) 모드에서 실행해주세요!", "확인");
        }

        GUI.backgroundColor = new Color(0.3f, 0.6f, 0.9f);
        if (GUILayout.Button("🔄 재투입 재조립 (Refeed)", GUILayout.Height(30)))
        {
            if (Application.isPlaying) seq.StartCoroutine(seq.Refeed());
            else EditorUtility.DisplayDialog("알림", "Play(▶) 모드에서 실행해주세요!", "확인");
        }
        GUI.backgroundColor = Color.white;
        GUILayout.EndHorizontal();

        // 상태 표시
        EditorGUILayout.Space(6);
        EditorGUILayout.HelpBox($"현재 상태: {seq.Status}", MessageType.None);
    }

    private void DrawTargetReachDiagnostic(ArmIK ik, string targetName, Vector3? pos)
    {
        if (!pos.HasValue)
        {
            EditorGUILayout.LabelField($" - {targetName}:", "(미지정 / 씬 검색 필요)", EditorStyles.miniLabel);
            return;
        }

        int code = ik.EvaluateReach(pos.Value, out float hDist, out _);
        string statusStr;
        Color col;
        switch (code)
        {
            case 0:
                statusStr = "🟢 안전 권장 범위 (OK)";
                col = new Color(0.1f, 0.6f, 0.2f);
                break;
            case 1:
                statusStr = "🟡 한계 근접 (Marginal)";
                col = new Color(0.7f, 0.5f, 0.1f);
                break;
            case -1:
                statusStr = "🟠 중심 사각지대 (너무 가까움)";
                col = new Color(0.8f, 0.4f, 0.1f);
                break;
            default:
                statusStr = "🔴 도달 불가 (Out of Reach!)";
                col = new Color(0.8f, 0.1f, 0.1f);
                break;
        }

        GUIStyle s = new GUIStyle(EditorStyles.miniLabel);
        s.normal.textColor = col;
        s.fontStyle = FontStyle.Bold;
        EditorGUILayout.LabelField($" - {targetName}: {hDist:F2}m", statusStr, s);
    }
}
#endif

