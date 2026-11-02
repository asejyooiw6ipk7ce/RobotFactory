using UnityEditor;
using UnityEngine;

/// <summary>
/// [FR-09 / TC-11] 카메라 3 완제품 검사기 커스텀 에디터
/// - 실시간 스냅샷 미리보기 창 및 합격/불량 컬러 배지 표시
/// - 메인 씬 통합용 상대 구도 저장 및 원클릭 자동 정렬 도구
/// - TC-11 경계값(3.0° 양품 / 3.1° 불량) 단독 즉시 검증 버튼
/// </summary>
[CustomEditor(typeof(Camera3Inspector))]
public class Camera3InspectorEditor : Editor
{
    public override void OnInspectorGUI()
    {
        // 1. 기본 인스펙터 프로퍼티 표시
        DrawDefaultInspector();

        Camera3Inspector insp = (Camera3Inspector)target;
        EditorGUILayout.Space(12);
        EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);

        // =====================================================================
        // [1] 비전 스냅샷 미리보기 패널
        // =====================================================================
        GUILayout.Label("📸 카메라 3 비전 캡처 미리보기", EditorStyles.boldLabel);
        EditorGUILayout.BeginVertical("box");

        if (insp.LastSnapshot != null)
        {
            Rect rect = GUILayoutUtility.GetAspectRect((float)insp.snapshotSize.x / insp.snapshotSize.y);
            GUI.DrawTexture(rect, insp.LastSnapshot, ScaleMode.ScaleToFit);

            EditorGUILayout.Space(6);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("대상 부품 ID:", insp.LastPartId ?? "-", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("측정 오차 각도:", $"{insp.LastMeasuredAngle:F2}°", EditorStyles.boldLabel);
            EditorGUILayout.EndHorizontal();

            // 판정 결과 배지
            if (insp.LastResult.HasValue)
            {
                bool isPass = insp.LastResult.Value == InspectResult.Pass;
                GUI.backgroundColor = isPass ? new Color(0.2f, 0.85f, 0.35f) : new Color(1.0f, 0.35f, 0.35f);
                string badgeText = isPass ? "🟢 PASS (양품 - 허용 각도 3.0° 이내)" : "🔴 FAIL (불량 - DefectType.AngleError 발생)";
                EditorGUILayout.HelpBox(badgeText, isPass ? MessageType.Info : MessageType.Error);
                GUI.backgroundColor = Color.white;
            }
        }
        else
        {
            EditorGUILayout.HelpBox("💡 아직 촬영된 스냅샷이 없습니다.\n아래 테스트 버튼을 누르거나 Play 모드에서 조립 시퀀스를 실행하면 캡처본이 표시됩니다.", MessageType.Info);
        }

        EditorGUILayout.EndVertical();

        // =====================================================================
        // [2] 메인 씬 통합용 구도 보존 및 정렬 도구
        // =====================================================================
        EditorGUILayout.Space(10);
        GUILayout.Label("🌐 메인 씬 통합 대비 구도 관리 (Pose Alignment)", EditorStyles.boldLabel);
        EditorGUILayout.BeginVertical("box");

        EditorGUILayout.HelpBox(
            "• [💾 구도 저장]: ChangminScene에서 맞춘 카메라 위치를 상대 오프셋으로 박제합니다.\n" +
            "• [🤖 / 🎯 자동 정렬]: 월요일 메인 씬에 프리팹을 배치한 후 클릭 시 동일 구도로 즉시 정렬됩니다.",
            MessageType.None
        );

        if (GUILayout.Button("💾 현재 카메라 위치를 기준 오프셋으로 저장", GUILayout.Height(28)))
        {
            Undo.RecordObject(insp, "Save Camera3 Pose");
            var seq = FindAnyObjectByType<AssemblySequence>();
            if (seq != null && seq.ik != null)
            {
                insp.SavePoseRelativeToRobot(seq.ik.transform);
                seq.GetSlotInfo(out Vector3 sc, out _, out Vector3 idir);
                if (sc != Vector3.zero && idir != Vector3.zero)
                {
                    Vector3 cleanInsert = Vector3.ProjectOnPlane(idir, Vector3.up).normalized;
                    if (cleanInsert.sqrMagnitude < 0.001f) cleanInsert = Vector3.left;
                    Vector3 rightDir = Vector3.Cross(Vector3.up, cleanInsert).normalized;
                    Vector3 delta = insp.transform.position - sc;
                    insp.offsetFromSocketLocal = new Vector3(
                        Vector3.Dot(delta, rightDir),
                        delta.y,
                        -Vector3.Dot(delta, cleanInsert)
                    );
                }
            }
            EditorUtility.SetDirty(insp);
            Debug.Log("[Camera3Inspector] 💾 구도 오프셋 저장 완료! (Prefab Apply를 눌러 프리팹에 반영할 수 있습니다)");
        }

        EditorGUILayout.Space(4);
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("🤖 로봇2(ArmIK) 기준 자동 정렬", GUILayout.Height(26)))
        {
            Undo.RecordObject(insp.transform, "Align Camera3 to Robot");
            var seq = FindAnyObjectByType<AssemblySequence>();
            if (seq != null && seq.ik != null)
            {
                insp.AlignToRobot(seq.ik.transform);
                EditorUtility.SetDirty(insp.transform);
            }
            else
            {
                EditorUtility.DisplayDialog("알림", "씬에서 AssemblySequence (로봇2)를 찾을 수 없습니다.", "확인");
            }
        }

        if (GUILayout.Button("🎯 소켓(Socket) 기준 자동 정렬", GUILayout.Height(26)))
        {
            Undo.RecordObject(insp.transform, "Align Camera3 to Socket");
            var seq = FindAnyObjectByType<AssemblySequence>();
            if (seq != null)
            {
                seq.GetSlotInfo(out Vector3 sc, out _, out Vector3 idir);
                insp.AlignToSocket(sc, idir);
                EditorUtility.SetDirty(insp.transform);
            }
            else
            {
                EditorUtility.DisplayDialog("알림", "씬에서 조립 소켓 정보를 가져올 수 없습니다.", "확인");
            }
        }
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.EndVertical();

        // =====================================================================
        // [3] TC-11 단독 즉시 테스트 패널 (Play 모드)
        // =====================================================================
        EditorGUILayout.Space(10);
        GUILayout.Label("🧪 TC-11 단독 즉시 검증 (Play 모드 전용)", EditorStyles.boldLabel);
        EditorGUILayout.BeginVertical("box");

        if (GUILayout.Button("📸 현재 상태 즉시 촬영 & 판정", GUILayout.Height(30)))
        {
            if (Application.isPlaying)
            {
                ExecuteDirectInspection(insp, null);
            }
            else
            {
                EditorUtility.DisplayDialog("알림", "Play(▶) 모드에서 실행해주세요!", "확인");
            }
        }

        EditorGUILayout.Space(4);
        EditorGUILayout.BeginHorizontal();
        GUI.backgroundColor = new Color(0.35f, 0.85f, 0.45f);
        if (GUILayout.Button("✅ [3.0° 양품] 오차 주입 & 판정\n(기대: Pass / None)", GUILayout.Height(38)))
        {
            if (Application.isPlaying)
            {
                ExecuteDirectInspection(insp, 3.0f);
            }
            else
            {
                EditorUtility.DisplayDialog("알림", "Play(▶) 모드에서 실행해주세요!", "확인");
            }
        }

        GUI.backgroundColor = new Color(1.0f, 0.45f, 0.45f);
        if (GUILayout.Button("❌ [3.1° 불량] 오차 주입 & 판정\n(기대: Fail / AngleError)", GUILayout.Height(38)))
        {
            if (Application.isPlaying)
            {
                ExecuteDirectInspection(insp, 3.1f);
            }
            else
            {
                EditorUtility.DisplayDialog("알림", "Play(▶) 모드에서 실행해주세요!", "확인");
            }
        }
        GUI.backgroundColor = Color.white;
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.HelpBox("💡 로봇 이동을 기다리지 않고, 씬의 부품에 3.0° / 3.1° 오차를 즉시 주입하고 판정 결과 및 스냅샷을 0.1초 만에 확인합니다.", MessageType.None);
        EditorGUILayout.EndVertical();
    }

    private void ExecuteDirectInspection(Camera3Inspector insp, float? forcedAngle)
    {
        var seq = FindAnyObjectByType<AssemblySequence>();
        PartInfo part = seq != null ? seq.CurrentPart : FindAnyObjectByType<PartInfo>();
        BaseInfo bInfo = seq != null ? seq.CurrentBase : FindAnyObjectByType<BaseInfo>();

        if (forcedAngle.HasValue && part != null)
        {
            float prevAngle = part.assemblyAngleError;
            part.assemblyAngleError = forcedAngle.Value;
            float delta = forcedAngle.Value - prevAngle;
            if (Mathf.Abs(delta) > 0.001f)
            {
                part.transform.Rotate(Vector3.right, delta, Space.Self);
            }
        }

        insp.InspectProduct(part, bInfo);
        Repaint();
    }
}
