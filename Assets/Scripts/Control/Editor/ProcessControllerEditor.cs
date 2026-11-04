using UnityEditor;
using UnityEngine;

// 인스펙터에 테스트용 Run / Pause / Reset 버튼을 추가한다 (에디터 전용, 빌드에 포함되지 않음)
[CustomEditor(typeof(ProcessController))]
public class ProcessControllerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        var controller = (ProcessController)target;

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("테스트", EditorStyles.boldLabel);
        EditorGUILayout.LabelField("상태", controller.State.ToString());
        EditorGUILayout.LabelField("경과 시간", controller.SimTime.ToString("0.0") + " 초");

        using (new EditorGUI.DisabledScope(!Application.isPlaying))
        {
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Run")) controller.Run();
            if (GUILayout.Button("Pause")) controller.Pause();
            if (GUILayout.Button("Reset")) controller.ResetProcess();
            EditorGUILayout.EndHorizontal();
        }

        if (!Application.isPlaying)
            EditorGUILayout.HelpBox("버튼은 Play 모드에서만 동작합니다.", MessageType.Info);

        // 실행 중에는 상태 표시가 바뀌므로 매 프레임 다시 그린다
        if (Application.isPlaying) Repaint();
    }
}
