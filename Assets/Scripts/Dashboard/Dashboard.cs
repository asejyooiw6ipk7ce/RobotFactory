using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;

/// 대시보드 화면. 이벤트를 받을 때마다 통계·알림·로봇·공정 상태를 갱신하고 UI를 다시 그린다.
public class Dashboard : MonoBehaviour
{
    [SerializeField] private UIDocument document;

    [Tooltip("알림 기준 불량률(%). 이 값을 초과하면 알림을 표시한다.")]
    [SerializeField] private float alertThresholdPercent = 20f;

    [Tooltip("알림 판정에 쓰는 최근 검사 횟수")]
    [SerializeField, Min(1)] private int recentCount = 10;

    [Tooltip("알림 판정을 시작하는 최소 검사 횟수")]
    [SerializeField, Min(1)] private int minInspectionsForAlert = 5;

    private StatusTracker stats;
    private AlarmMonitor alarms;
    private RobotStatusTracker robots;

    // 공정 상태 (ProcessController가 버스로 알려줌)
    private ProcessState processState = ProcessState.Stopped;
    private string alarmReason;
    private float runTime;
    private int shownSeconds = -1;

    private Label processStateLabel;
    private Label runTimeLabel;
    private Label totalYield;
    private Label finalPassRate;
    private readonly Dictionary<StationId, Label> stationRates = new Dictionary<StationId, Label>();
    private readonly Dictionary<StationId, VisualElement> stationCards = new Dictionary<StationId, VisualElement>();
    private readonly Dictionary<CounterType, Label> counters = new Dictionary<CounterType, Label>();
    private readonly Dictionary<int, Label> robotStates = new Dictionary<int, Label>();
    private VisualElement alertList;
    private VisualElement alertBox;
    private Label alertEmpty;

    private bool IsBound => processStateLabel != null;

    private void Awake()
    {
        CreateTrackers();

        if (document == null)
        {
            Debug.LogError("Dashboard: UIDocument가 연결되지 않았습니다.", this);
            enabled = false;
        }
        // BindUI는 UIDocument의 rootVisualElement가 준비된 뒤(OnEnable/Start)에 호출해야 함
    }

    private void OnEnable()
    {
        if (document == null) return;
        if (!IsBound) BindUI();
        InspectEventBus.Published += OnPublished;
        InspectEventBus.CounterAdded += OnCounterAdded;
        InspectEventBus.RobotStateChanged += OnRobotStateChanged;
        InspectEventBus.ProcessStateChanged += OnProcessStateChanged;
        InspectEventBus.ProcessReset += OnProcessReset;
        Refresh();
    }

    private void Start()
    {
        if (document == null) return;
        if (!IsBound) BindUI();
        Refresh();
    }

    private void OnDisable()
    {
        InspectEventBus.Published -= OnPublished;
        InspectEventBus.CounterAdded -= OnCounterAdded;
        InspectEventBus.RobotStateChanged -= OnRobotStateChanged;
        InspectEventBus.ProcessStateChanged -= OnProcessStateChanged;
        InspectEventBus.ProcessReset -= OnProcessReset;
    }

    private void Update()
    {
        if (processState != ProcessState.Running) return;
        runTime += Time.deltaTime;
        UpdateRunTime();
    }

    private void OnValidate()
    {
        if (minInspectionsForAlert > recentCount)
        {
            Debug.LogWarning("최소 검사 횟수가 최근 검사 횟수보다 커서 알림이 나타나지 않습니다.", this);
        }
    }

    private void CreateTrackers()
    {
        stats = new StatusTracker(recentCount);
        alarms = new AlarmMonitor(stats, alertThresholdPercent, minInspectionsForAlert);
        robots = new RobotStatusTracker();
    }

    private void BindUI()
    {
        var root = document.rootVisualElement;

        processStateLabel = root.Q<Label>("process-state");
        runTimeLabel = root.Q<Label>("run-time");
        totalYield = root.Q<Label>("total-yield");
        finalPassRate = root.Q<Label>("final-pass-rate");
        alertList = root.Q<VisualElement>("alert-list");
        alertEmpty = root.Q<Label>("alert-empty");
        alertBox = root.Q<VisualElement>("alert-box");

        // 스테이션1 = 클로 검사, 스테이션2 = 베이스 검사, 스테이션3 = 완제품 검사
        stationRates[StationId.Station1] = root.Q<Label>("station1-rate");
        stationRates[StationId.Station2] = root.Q<Label>("station2-rate");
        stationCards[StationId.Station1] = root.Q<VisualElement>("station1-card");
        stationCards[StationId.Station2] = root.Q<VisualElement>("station2-card");
        stationCards[StationId.Station3] = root.Q<VisualElement>("station3-card");

        counters[CounterType.InputBase] = root.Q<Label>("count-input-base");
        counters[CounterType.InputClaw] = root.Q<Label>("count-input-claw");
        counters[CounterType.DiscardedBase] = root.Q<Label>("count-discarded-base");
        counters[CounterType.DiscardedClaw] = root.Q<Label>("count-discarded-claw");
        counters[CounterType.Defective] = root.Q<Label>("count-defect");
        counters[CounterType.Shipped] = root.Q<Label>("count-shipped");

        robotStates[1] = root.Q<Label>("robot1-state");
        robotStates[2] = root.Q<Label>("robot2-state");
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

    private void OnProcessStateChanged(ProcessState state, string reason)
    {
        processState = state;
        alarmReason = state == ProcessState.Alarm ? reason : null;
        Refresh();
    }

    private void OnProcessReset()
    {
        CreateTrackers();
        runTime = 0f;
        shownSeconds = -1;
        alarmReason = null;
        Refresh();
    }

    private void Refresh()
    {
        if (!IsBound) return;   // UI 바인딩 전이면 건너뜀

        SetProcessState(processStateLabel, processState);
        UpdateRunTime();

        double? yield = TotalYieldRate();
        totalYield.text = FormatPercent(yield, "0");
        totalYield.EnableInClassList("is-empty", !yield.HasValue);
        finalPassRate.text = FormatPercent(stats.FinalYieldRate, "0");

        foreach (var pair in stationRates)
        {
            pair.Value.text = FormatPercent(stats.StationDefectRate(pair.Key), "0");
            pair.Value.EnableInClassList("is-bad", alarms.Active.ContainsKey(pair.Key));   // 기준 초과일 때만 빨강
        }

        foreach (var pair in stationCards)
        {
            pair.Value.EnableInClassList("is-alert", alarms.Active.ContainsKey(pair.Key));
        }

        foreach (var pair in counters)
        {
            int n = stats.Count(pair.Key);
            //pair.Value.text = stats.Count(pair.Key).ToString(CultureInfo.InvariantCulture);
            pair.Value.text = n.ToString(CultureInfo.InvariantCulture);
            pair.Value.EnableInClassList("is-bad", n > 0 && IsBadCounter(pair.Key));          // 폐기·불량이 생기면 빨강
            pair.Value.EnableInClassList("is-good", n > 0 && pair.Key == CounterType.Shipped); // 출하가 생기면 초록
        }

        foreach (var pair in robotStates)
        {
            SetRobotState(pair.Value, robots.Get(pair.Key));
        }

        RefreshAlerts();
    }

    // 전체 수율 = 출하 / 라인을 끝까지 빠져나간 베이스 (출하 + 완제품 불량 + 베이스 폐기)
    private double? TotalYieldRate()
    {
        int shipped = stats.Count(CounterType.Shipped);
        int finished = shipped
            + stats.Count(CounterType.Defective)
            + stats.Count(CounterType.DiscardedBase);
        return finished == 0 ? (double?)null : shipped * 100.0 / finished;
    }

    private void UpdateRunTime()
    {
        if (!IsBound) return;
        int seconds = (int)runTime;
        if (seconds == shownSeconds) return;   // 초가 바뀔 때만 갱신
        shownSeconds = seconds;
        runTimeLabel.text = $"{seconds / 60:00}:{seconds % 60:00}";
    }

    private void RefreshAlerts()
    {
        alertList.Clear();

        if (processState == ProcessState.Alarm)
        {
            string reason = string.IsNullOrEmpty(alarmReason) ? "공정이 알람으로 멈췄습니다" : alarmReason;
            AddAlert("정지", $"{reason} — Reset 필요");
        }

        foreach (var pair in alarms.Active)
        {
            string rate = pair.Value.ToString("0", CultureInfo.InvariantCulture);
            string limit = alarms.ThresholdPercent.ToString("0", CultureInfo.InvariantCulture);
            AddAlert("경고", $"{StationName(pair.Key)} 불량률 {rate}% — 기준 {limit}% 초과");
        }

        alertEmpty.style.display = alertList.childCount == 0 ? DisplayStyle.Flex : DisplayStyle.None;
        alertBox.EnableInClassList("has-alert", alertList.childCount > 0);
    }

    private void AddAlert(string tagText, string messageText)
    {
        var row = new VisualElement();
        row.AddToClassList("alert-item");

        var tag = new Label(tagText);
        tag.AddToClassList("alert-tag");
        row.Add(tag);

        var message = new Label(messageText);
        message.AddToClassList("alert-message");
        row.Add(message);

        alertList.Add(row);
    }

    private static string StationName(StationId station)
    {
        switch (station)
        {
            case StationId.Station1: return "클로 검사";
            case StationId.Station2: return "베이스 검사";
            case StationId.Station3: return "완제품 검사";
            default: return station.ToString();
        }
    }

    private static void SetProcessState(Label label, ProcessState state)
    {
        label.text = ProcessStateText(state);
        foreach (ProcessState s in Enum.GetValues(typeof(ProcessState)))
        {
            label.EnableInClassList("proc-" + s.ToString().ToLowerInvariant(), s == state);
        }
    }

    private static string ProcessStateText(ProcessState state)
    {
        switch (state)
        {
            case ProcessState.Running: return "가동 중";
            case ProcessState.Paused: return "일시정지";
            case ProcessState.Alarm: return "알람 정지";
            default: return "정지";
        }
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

    private static bool IsBadCounter(CounterType type) => type == CounterType.DiscardedBase || type == CounterType.DiscardedClaw || type == CounterType.Defective;
}
