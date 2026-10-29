using NUnit.Framework;

/// TC-14: 완제품 검사 후 양품률과 스테이션별 불량률 계산 검증
public class StatsTrackerTests
{
    private static InspectEvent Make(StationId station, bool fail) =>
        new InspectEvent("P", station, fail ? InspectResult.Fail : InspectResult.Pass, DefectType.None, 0f);

    [Test]
    public void NoInspection_ReturnsNull()
    {
        var stats = new StatusTracker(10);

        Assert.IsNull(stats.FinalYieldRate);
        Assert.IsNull(stats.StationDefectRate(StationId.Station1));
    }

    [Test]
    public void FinalYield_UsesStation3Only()
    {
        var stats = new StatusTracker(10);

        // 완제품 20개: 양품 14, 불량 6
        for (int i = 0; i < 14; i++) stats.Record(Make(StationId.Station3, false));
        for (int i = 0; i < 6; i++) stats.Record(Make(StationId.Station3, true));
        // 스테이션1 결과는 양품률에 들어가면 안 된다
        for (int i = 0; i < 10; i++) stats.Record(Make(StationId.Station1, true));

        Assert.AreEqual(70.0, stats.FinalYieldRate.Value, 1e-9);
    }

    [Test]
    public void StationDefectRate_CountsOnlyThatStation()
    {
        var stats = new StatusTracker(10);

        for (int i = 0; i < 9; i++) stats.Record(Make(StationId.Station1, false));
        stats.Record(Make(StationId.Station1, true));
        stats.Record(Make(StationId.Station2, true));

        Assert.AreEqual(10.0, stats.StationDefectRate(StationId.Station1).Value, 1e-9);
        Assert.AreEqual(100.0, stats.StationDefectRate(StationId.Station2).Value, 1e-9);
    }

    [Test]
    public void Counters_IncrementPerType()
    {
        var stats = new StatusTracker(10);

        stats.Increment(CounterType.Input);
        stats.Increment(CounterType.Input);
        stats.Increment(CounterType.Shipped);

        Assert.AreEqual(2, stats.Count(CounterType.Input));
        Assert.AreEqual(1, stats.Count(CounterType.Shipped));
        Assert.AreEqual(0, stats.Count(CounterType.Discarded));
    }

    [Test]
    public void RecentWindow_KeepsOnlyLatestResults()
    {
        var stats = new StatusTracker(10);

        for (int i = 0; i < 10; i++) stats.Record(Make(StationId.Station2, true));
        for (int i = 0; i < 10; i++) stats.Record(Make(StationId.Station2, false));

        Assert.AreEqual(10, stats.RecentCount(StationId.Station2));
        Assert.AreEqual(0, stats.RecentFailCount(StationId.Station2));
    }
}
