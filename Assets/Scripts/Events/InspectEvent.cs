namespace RobotFactory
{
    /// 검사 결과 이벤트 (FR-11)
    public readonly struct InspectEvent
    {
        public readonly string PartId;
        public readonly StationId StationId;
        public readonly InspectResult Result;
        public readonly DefectType DefectType;
        public readonly float Timestamp; // 시뮬레이션 시작 후 경과 초

        public InspectEvent(string partId, StationId stationId, InspectResult result, DefectType defectType, float timestamp)
        {
            PartId = partId;
            StationId = stationId;
            Result = result;
            DefectType = defectType;
            Timestamp = timestamp;
        }
    }
}
