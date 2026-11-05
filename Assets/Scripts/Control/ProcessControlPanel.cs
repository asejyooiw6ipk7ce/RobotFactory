using UnityEngine;
using UnityEngine.UIElements;

/// ��ú����� ���� / �Ͻ����� / ���� ��ư�� ProcessController�� �����Ѵ�.
public class ProcessControlPanel : MonoBehaviour
{
    [SerializeField] private UIDocument document;
    [SerializeField] private ProcessController controller;

    private Button runButton;
    // private Button pauseButton;
    private Button resetButton;

    // UIDocument�� rootVisualElement�� �غ�� �ڿ� ��ư�� ã���� Start���� ���ε�
    private void Start()
    {
        if (document == null || controller == null)
        {
            Debug.LogError("ProcessControlPanel: UIDocument �Ǵ� ProcessController�� ������� �ʾҽ��ϴ�.", this);
            enabled = false;
            return;
        }

        var root = document.rootVisualElement;
        runButton = root.Q<Button>("btn-run");
        // pauseButton = root.Q<Button>("btn-pause");
        resetButton = root.Q<Button>("btn-reset");

        // if (runButton == null || pauseButton == null || resetButton == null)
        if (runButton == null || resetButton == null)
        {
            Debug.LogError("ProcessControlPanel: UXML���� ��ư(btn-run / btn-pause / btn-reset)�� ã�� ���߽��ϴ�.", this);
            enabled = false;
            return;
        }

        runButton.clicked += controller.Run;
        // pauseButton.clicked += controller.Pause;
        resetButton.clicked += controller.ResetProcess;
        InspectEventBus.ProcessStateChanged += OnProcessStateChanged;

        UpdateButtons(controller.State);
    }

    private void OnDestroy()
    {
        if (runButton != null) runButton.clicked -= controller.Run;
        // if (pauseButton != null) pauseButton.clicked -= controller.Pause;
        if (resetButton != null) resetButton.clicked -= controller.ResetProcess;
        InspectEventBus.ProcessStateChanged -= OnProcessStateChanged;
    }

    private void OnProcessStateChanged(ProcessState state, string reason) => UpdateButtons(state);

    // ���� ���¿��� ���� �� �ִ� ��ư�� Ȱ��ȭ
    private void UpdateButtons(ProcessState state)
    {
        runButton.SetEnabled(state == ProcessState.Stopped || state == ProcessState.Paused);
        // pauseButton.SetEnabled(state == ProcessState.Running);
        resetButton.SetEnabled(true);
    }
}