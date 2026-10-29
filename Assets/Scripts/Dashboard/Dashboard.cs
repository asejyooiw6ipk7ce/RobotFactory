using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;

/// 대시보드 화면. 이벤트를 받을 때마다 통계·알림·로봇 상태를 갱신하고 UI를 다시 그린다.
public class Dashboard : MonoBehaviour
{
    [SerializeField] private UIDocument document;

    [Tooltip("알림 기준 불량률(%). 이 값을 초과하면 알림을 표시한다.")]
    [SerializeField] private float alertThresholdPercent = 20f;

    [Tooltip("알림 판정에 쓰는 최근 검사 횟수")]
    [SerializeField, Min(1)] private int recentCount = 10;

    [Tooltip("알림 판정을 시작하는 최소 검사 횟수")]
    [SerializeField, Min(1)] private int minInspectionsForAlert = 5;

    private StatsTracker stats;
    private AlarmMonitor alarms;
    private RobotStatusTracker robots;

    private Label finalYield;
    private readonly Dictionary<StationId, Label> stationRates = new Dictionary<StationId, Label>();
    private readonly Dictionary<StationId, VisualElement> stationCards = new Dictionary<StationId, VisualElement>();
    private readonly Dictionary<CounterType, Label> counters = new Dictionary<CounterType, Label>();
    private readonly Dictionary<int, Label> robotStates = new Dictionary<int, Label>();
    private VisualElement alertList;
    private Label alertEmpty;

    private void Awake()
    {
        stats = new StatsTracker(recentCount);
        alarms = new AlarmMonitor(stats, alertThresholdPercent, minInspectionsForAlert);
        robots = new RobotStatusTracker();

        if (document == null)
        {
            Debug.LogError("Dashboard: UIDocument가 연결되지 않았습니다.", this);
            enabled = false;
            return;
        }
        BindUI();
    }

    private void OnEnable()
    {
        if (document == null) return;
        InspectEventBus.Published += OnPublished;
        InspectEventBus.CounterAdded += OnCounterAdded;
        InspectEventBus.RobotStateChanged += OnRobotStateChanged;
        Refresh();
    }

    private void OnDisable()
    {
        InspectEventBus.Published -= OnPublished;
        InspectEventBus.CounterAdded -= OnCounterAdded;
        InspectEventBus.RobotStateChanged -= OnRobotStateChanged;
    }

    private void OnValidate()
    {
        if (minInspectionsForAlert > recentCount)
        {
            Debug.LogWarning("최소 검사 횟수가 최근 검사 횟수보다 커서 알림이 나타나지 않습니다.", this);
        }
    }

    private void BindUI()
    {
        var root = document.rootVisualElement;

        finalYield = root.Q<Label>("final-yield");
        alertList = root.Q<VisualElement>("alert-list");
        alertEmpty = root.Q<Label>("alert-empty");

        BindStation(root, StationId.Station1, "station1");
        BindStation(root, StationId.Station2, "station2");
        BindStation(root, StationId.Station3, "station3");

        counters[CounterType.Input] = root.Q<Label>("count-input");
        counters[CounterType.Inspected] = root.Q<Label>("count-inspected");
        counters[CounterType.Discarded] = root.Q<Label>("count-discarded");
        counters[CounterType.Defective] = root.Q<Label>("count-defect");
        counters[CounterType.Shipped] = root.Q<Label>("count-shipped");

        robotStates[1] = root.Q<Label>("robot1-state");
        robotStates[2] = root.Q<Label>("robot2-state");
    }

    private void BindStation(VisualElement root, StationId station, string prefix)
    {
        stationRates[station] = root.Q<Label>($"{prefix}-rate");
        stationCards[station] = root.Q<VisualElement>($"{prefix}-card");
    }

    private void OnPublished(InspectEvent e)
    {
        stats.Record(e);
        alarms.Evaluate(e.stationId);
        Refresh();
    }

    private void OnCounterAdded(CounterType type)
    {
        stats.Increment(type);
        Refresh();
    }

    private void OnRobotStateChanged(int robotId, RobotState state)
    {
        robots.Apply(robotId, state);
        Refresh();
    }

    private void Refresh()
    {
        if (document == null) return;

        finalYield.text = FormatPercent(stats.FinalYieldRate, "0.0");

        foreach (var pair in stationRates)
        {
            pair.Value.text = FormatPercent(stats.StationDefectRate(pair.Key), "0");
            stationCards[pair.Key].EnableInClassList("is-alert", alarms.Active.ContainsKey(pair.Key));
        }

        foreach (var pair in counters)
        {
            pair.Value.text = stats.Count(pair.Key).ToString(CultureInfo.InvariantCulture);
        }

        foreach (var pair in robotStates)
        {
            SetRobotState(pair.Value, robots.Get(pair.Key));
        }

        RefreshAlerts();
    }

    private void RefreshAlerts()
    {
        alertList.Clear();

        foreach (var pair in alarms.Active)
        {
            var row = new VisualElement();
            row.AddToClassList("alert-item");

            var tag = new Label("경고");
            tag.AddToClassList("alert-tag");
            row.Add(tag);

            string rate = pair.Value.ToString("0", CultureInfo.InvariantCulture);
            string limit = alarms.ThresholdPercent.ToString("0", CultureInfo.InvariantCulture);
            var message = new Label($"스테이션 {(int)pair.Key} 불량률 {rate}% — 기준 {limit}% 초과");
            message.AddToClassList("alert-message");
            row.Add(message);

            alertList.Add(row);
        }

        alertEmpty.style.display = alarms.Active.Count == 0 ? DisplayStyle.Flex : DisplayStyle.None;
    }

    private static void SetRobotState(Label label, RobotState state)
    {
        label.text = RobotStateText(state);
        foreach (RobotState s in Enum.GetValues(typeof(RobotState)))
        {
            label.EnableInClassList("state-" + s.ToString().ToLowerInvariant(), s == state);
        }
    }

    private static string RobotStateText(RobotState state)
    {
        switch (state)
        {
            case RobotState.Idle: return "대기";
            case RobotState.Moving: return "이동";
            case RobotState.Working: return "작업";
            case RobotState.Assembling: return "조립";
            case RobotState.Error: return "오류";
            default: return "-";
        }
    }

    private static string FormatPercent(double? value, string format)
    {
        return value.HasValue
            ? value.Value.ToString(format, CultureInfo.InvariantCulture) + "%"
            : "-";
    }
}
