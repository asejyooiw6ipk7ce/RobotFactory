using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(PartStopper))]
public class PartStopperEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        PartStopper stopper = (PartStopper)target;

        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("스토퍼 위치 캡처 및 수동 테스트", EditorStyles.boldLabel);

        // 1. 위치 캡처 버튼 (Edit 모드에서도 유용)
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("🔽 현재 위치를 'Down'으로 캡처"))
        {
            Undo.RecordObject(stopper, "Capture Stopper Down");
            stopper.CaptureCurrentAsDown();
            EditorUtility.SetDirty(stopper);
        }
        if (GUILayout.Button("🔼 현재 위치를 'Up'으로 캡처"))
        {
            Undo.RecordObject(stopper, "Capture Stopper Up");
            stopper.CaptureCurrentAsUp();
            EditorUtility.SetDirty(stopper);
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(6);

        // 2. Play 모드 테스트 버튼
        bool isPlaying = Application.isPlaying;
        EditorGUI.BeginDisabledGroup(!isPlaying);

        EditorGUILayout.BeginHorizontal();
        GUI.backgroundColor = new Color(0.7f, 0.9f, 1f);
        if (GUILayout.Button("🔽 내리기 테스트 (Resume)"))
        {
            stopper.StartCoroutine(stopper.Resume());
        }

        GUI.backgroundColor = new Color(1f, 0.8f, 0.7f);
        if (GUILayout.Button("🔼 올리기 테스트 (Stop)"))
        {
            stopper.StartCoroutine(stopper.Stop());
        }
        GUI.backgroundColor = Color.white;
        EditorGUILayout.EndHorizontal();

        EditorGUI.EndDisabledGroup();

        if (!isPlaying)
        {
            EditorGUILayout.HelpBox("올리기/내리기 테스트 버튼은 Play(플레이) 모드에서 동작합니다.\n씬에서 스토퍼를 원하는 위치에 두고 위 '캡처' 버튼을 누르면 좌표가 자동 저장됩니다.", MessageType.Info);
        }
    }
}

