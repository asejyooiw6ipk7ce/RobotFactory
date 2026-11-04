using UnityEngine;
using UnityEngine.UIElements;

public class DashboardCameraView : MonoBehaviour
{
    [System.Serializable]
    public class View { public string label; public Transform anchor; }

    [SerializeField] private UIDocument document;
    [SerializeField] private Camera viewCamera;
    [SerializeField] private View[] views;

    private Button[] buttons;

    private Coroutine moving;

    private void Start()
    {
        var root = document.rootVisualElement;
        var tabs = root.Q<VisualElement>("cam-tabs");

        buttons = new Button[views.Length];
        for (int i = 0; i < views.Length; i++)
        {
            int index = i;
            var b = new Button(() => Show(index)) { text = views[i].label };
            b.AddToClassList("cam-tab");
            tabs.Add(b);
            buttons[i] = b;
        }
        if (views.Length > 0)
        {
            Transform a = views[0].anchor;
            viewCamera.transform.SetPositionAndRotation(a.position, a.rotation);
            buttons[0].AddToClassList("is-active");
        }
    }

    private void Show(int index)
    {
        for (int i = 0; i < buttons.Length; i++)
            buttons[i].EnableInClassList("is-active", i == index);

        if (moving != null) StopCoroutine(moving);
        moving = StartCoroutine(MoveTo(views[index].anchor, 0.6f));
    }

    private System.Collections.IEnumerator MoveTo(Transform target, float duration)
    {
        Transform cam = viewCamera.transform;
        Vector3 p0 = cam.position;
        Quaternion r0 = cam.rotation;
        for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / duration)
        {
            float s = Mathf.SmoothStep(0f, 1f, t);
            cam.SetPositionAndRotation(Vector3.Lerp(p0, target.position, s), Quaternion.Slerp(r0, target.rotation, s));
            yield return null;
        }
        cam.SetPositionAndRotation(target.position, target.rotation);
    }
}