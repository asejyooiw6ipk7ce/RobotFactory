using NUnit.Framework;

/// TC-16: 이상 알림 판정 검증 (기준 20%, 판정 횟수 10, 최소 검사 5)
public class AlarmMonitorTests
{
    private StatsTracker stats;
    private AlarmMonitor alarms;

    [SetUp]
    public void SetUp()
    {
        stats = new StatsTracker(10);
        alarms = new AlarmMonitor(stats, 20f, 5);
    }

    private void Feed(StationId station, int fails, int total)
    {
        for (int i = 0; i < total; i++)
        {
            bool fail = i < fails;
            stats.Record(new InspectEvent("P", station,
                fail ? InspectResult.Fail : InspectResult.Pass, DefectType.None, 0f));
            alarms.Evaluate(station);
        }
    }

    [Test]
    public void ThreeFailsOutOfTen_ShowsAlert()
    {
        Feed(StationId.Station1, fails: 3, total: 10);

        Assert.IsTrue(alarms.Active.ContainsKey(StationId.Station1));
    }

    [Test]
    public void TwoFailsOutOfTen_DoesNotShowAlert()
    {
        Feed(StationId.Station1, fails: 2, total: 10);

        Assert.IsFalse(alarms.Active.ContainsKey(StationId.Station1));
    }

    [Test]
    public void TwoFailsOutOfFour_DoesNotJudge()
    {
        Feed(StationId.Station1, fails: 2, total: 4);

        Assert.IsFalse(alarms.Active.ContainsKey(StationId.Station1));
    }

    [Test]
    public void AlertClears_WhenRateDropsToThreshold()
    {
        Feed(StationId.Station2, fails: 3, total: 10);
        Assert.IsTrue(alarms.Active.ContainsKey(StationId.Station2));

        // 최근 10회가 모두 양품이 되면 해제
        Feed(StationId.Station2, fails: 0, total: 10);

        Assert.IsFalse(alarms.Active.ContainsKey(StationId.Station2));
    }

    [Test]
    public void AlertIsOnePerStation_AndValueUpdates()
    {
        Feed(StationId.Station3, fails: 3, total: 10);
        double first = alarms.Active[StationId.Station3];

        // 같은 스테이션에서 불량이 더 쌓이면 알림은 하나로 유지되고 값만 바뀐다
        stats.Record(new InspectEvent("P", StationId.Station3, InspectResult.Fail, DefectType.None, 0f));
        alarms.Evaluate(StationId.Station3);

        Assert.AreEqual(1, alarms.Active.Count);
        Assert.AreNotEqual(first, alarms.Active[StationId.Station3]);
    }
}
