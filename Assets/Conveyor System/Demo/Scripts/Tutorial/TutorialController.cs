using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ConveyorSystem.Demo
{
    /// <summary>
    /// One page of the tutorial: what to show (camera framing), what to read, and what to try.
    /// </summary>
    [Serializable]
    public class TutorialStep
    {
        public string Title;

        [TextArea(4, 12)]
        public string Body;

        [Tooltip("Optional hands-on task, shown highlighted.")]
        [TextArea(1, 4)]
        public string Task;

        [Tooltip("Optional code sample, shown in the code panel.")]
        [TextArea(4, 20)]
        public string Code;

        [Header("Camera")]
        public Transform Focus;
        public float Distance = 10f;
        public float Pitch = 42f;
        public float Yaw = -12f;

        [Tooltip("Behaviours started when the step is shown and stopped when it is left.")]
        public TutorialStepAction[] Actions;
    }

    /// <summary>
    /// Step-by-step tutorial: moves the camera between live example stations and explains each concept.
    /// Next: Right Arrow / Enter / N or the Next button. Back: Left Arrow / B or the Back button.
    /// </summary>
    [AddComponentMenu("Conveyor System/Demo/Tutorial Controller")]
    public class TutorialController : MonoBehaviour
    {
        public TutorialStep[] Steps;

        [Header("Camera")]
        public Camera ViewCamera;

        [Tooltip("How quickly the camera glides to the next station.")]
        public float CameraSharpness = 2.5f;

        [Header("UI")]
        public TMP_Text StepLabel;
        public TMP_Text TitleText;
        public TMP_Text BodyText;
        public GameObject TaskPanel;
        public TMP_Text TaskText;
        public GameObject CodePanel;
        public TMP_Text CodeText;
        public Image ProgressFill;
        public RectTransform BackButton;
        public RectTransform NextButton;
        public TMP_Text NextLabel;

        [Header("Layout")]
        [Tooltip("Lesson panel; resized to fit the title, text and task.")]
        public RectTransform LessonPanel;

        [Tooltip("Code panel; resized to fit the code.")]
        public RectTransform CodePanelRect;

        private int index = -1;
        private Vector3 focus;
        private float distance, pitch, yaw;

        /// <summary>
        /// Index of the step currently shown.
        /// </summary>
        public int CurrentStep => index;

        private void Start()
        {
            if (ViewCamera == null) ViewCamera = Camera.main;

            // Make sure every action starts in its "not active" state
            foreach (var step in Steps)
                if (step.Actions != null)
                    foreach (var action in step.Actions)
                        if (action != null) action.Prepare();

            Go(0, instant: true);
        }

        private void Update()
        {
            if (DemoInput.GetKeyDown(DemoKey.RightArrow) || DemoInput.GetKeyDown(DemoKey.Enter) || DemoInput.GetKeyDown(DemoKey.N)) Next();
            if (DemoInput.GetKeyDown(DemoKey.LeftArrow) || DemoInput.GetKeyDown(DemoKey.B)) Back();

            if (DemoInput.LeftPressed)
            {
                Vector2 pointer = DemoInput.PointerPosition;
                if (NextButton != null && RectTransformUtility.RectangleContainsScreenPoint(NextButton, pointer, null)) Next();
                else if (BackButton != null && BackButton.gameObject.activeInHierarchy && RectTransformUtility.RectangleContainsScreenPoint(BackButton, pointer, null)) Back();
            }
        }

        private void LateUpdate()
        {
            if (ViewCamera == null || index < 0) return;
            var step = Steps[index];

            Vector3 targetFocus = step.Focus != null ? step.Focus.position : Vector3.zero;
            float t = 1f - Mathf.Exp(-CameraSharpness * Mathf.Min(Time.unscaledDeltaTime, 0.1f));
            focus = Vector3.Lerp(focus, targetFocus, t);
            distance = Mathf.Lerp(distance, step.Distance, t);
            pitch = Mathf.Lerp(pitch, step.Pitch, t);
            yaw = Mathf.LerpAngle(yaw, step.Yaw, t);
            ApplyCamera();
        }

        /// <summary>
        /// Shows the next step (or restarts after the last one).
        /// </summary>
        public void Next() => Go(index + 1 < Steps.Length ? index + 1 : 0);

        /// <summary>
        /// Shows the previous step.
        /// </summary>
        public void Back()
        {
            if (index > 0) Go(index - 1);
        }

        /// <summary>
        /// Shows a step: stops the old step's actions, starts the new ones and updates the panel.
        /// </summary>
        public void Go(int newIndex, bool instant = false)
        {
            if (Steps == null || Steps.Length == 0) return;
            newIndex = Mathf.Clamp(newIndex, 0, Steps.Length - 1);

            if (index >= 0 && Steps[index].Actions != null)
                foreach (var action in Steps[index].Actions)
                    if (action != null) action.Exit();

            index = newIndex;
            var step = Steps[index];

            if (step.Actions != null)
                foreach (var action in step.Actions)
                    if (action != null) action.Enter();

            if (instant)
            {
                focus = step.Focus != null ? step.Focus.position : Vector3.zero;
                distance = step.Distance;
                pitch = step.Pitch;
                yaw = step.Yaw;
                ApplyCamera();
            }

            RefreshUI(step);
        }

        private void RefreshUI(TutorialStep step)
        {
            if (StepLabel != null) StepLabel.text = $"STEP {index + 1} / {Steps.Length}";
            if (TitleText != null) TitleText.text = step.Title;
            if (BodyText != null) BodyText.text = step.Body;

            bool hasTask = !string.IsNullOrWhiteSpace(step.Task);
            if (TaskPanel != null) TaskPanel.SetActive(hasTask);
            if (TaskText != null) TaskText.text = hasTask ? step.Task : string.Empty;

            bool hasCode = !string.IsNullOrWhiteSpace(step.Code);
            if (CodePanel != null) CodePanel.SetActive(hasCode);
            if (CodeText != null) CodeText.text = hasCode ? step.Code : string.Empty;

            Layout(hasTask, hasCode);

            if (ProgressFill != null) ProgressFill.fillAmount = (index + 1f) / Steps.Length;
            if (BackButton != null) BackButton.gameObject.SetActive(index > 0);
            if (NextLabel != null) NextLabel.text = index == Steps.Length - 1 ? "RESTART" : "NEXT";
        }

        /// <summary>
        /// Stacks title, text and task in the lesson panel and fits both panels to their content.
        /// </summary>
        private void Layout(bool hasTask, bool hasCode)
        {
            const float gap = 18f;

            if (LessonPanel != null && TitleText != null && BodyText != null)
            {
                var titleRt = TitleText.rectTransform;
                float titleH = TitleText.GetPreferredValues(TitleText.text, titleRt.sizeDelta.x, 0f).y;
                titleRt.sizeDelta = new Vector2(titleRt.sizeDelta.x, titleH);
                float y = -titleRt.anchoredPosition.y + titleH + gap;

                var bodyRt = BodyText.rectTransform;
                float bodyH = BodyText.GetPreferredValues(BodyText.text, bodyRt.sizeDelta.x, 0f).y;
                bodyRt.anchoredPosition = new Vector2(bodyRt.anchoredPosition.x, -y);
                bodyRt.sizeDelta = new Vector2(bodyRt.sizeDelta.x, bodyH);
                y += bodyH + gap;

                if (hasTask && TaskPanel != null && TaskText != null)
                {
                    var taskRt = (RectTransform)TaskPanel.transform;
                    var textRt = TaskText.rectTransform;
                    float taskTextH = TaskText.GetPreferredValues(TaskText.text, textRt.sizeDelta.x, 0f).y;
                    textRt.sizeDelta = new Vector2(textRt.sizeDelta.x, taskTextH);
                    float taskH = -textRt.anchoredPosition.y + taskTextH + 16f;

                    taskRt.anchorMin = taskRt.anchorMax = taskRt.pivot = new Vector2(0f, 1f);
                    taskRt.anchoredPosition = new Vector2(24f, -y);
                    taskRt.sizeDelta = new Vector2(taskRt.sizeDelta.x, taskH);
                    var taskAccent = taskRt.Find("Task Accent") as RectTransform;
                    if (taskAccent != null) taskAccent.sizeDelta = new Vector2(taskAccent.sizeDelta.x, taskH);
                    y += taskH + gap;
                }

                LessonPanel.sizeDelta = new Vector2(LessonPanel.sizeDelta.x, y + 6f);
                var accent = LessonPanel.Find("Accent") as RectTransform;
                if (accent != null) accent.sizeDelta = new Vector2(accent.sizeDelta.x, y + 6f);
            }

            if (hasCode && CodePanelRect != null && CodeText != null)
            {
                var codeRt = CodeText.rectTransform;
                float codeH = CodeText.GetPreferredValues(CodeText.text, codeRt.sizeDelta.x, 0f).y;
                codeRt.sizeDelta = new Vector2(codeRt.sizeDelta.x, codeH);
                CodePanelRect.sizeDelta = new Vector2(CodePanelRect.sizeDelta.x, -codeRt.anchoredPosition.y + codeH + 22f);
            }
        }

        private void ApplyCamera()
        {
            Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
            ViewCamera.transform.SetPositionAndRotation(focus - rotation * Vector3.forward * distance, rotation);
        }
    }
}
