public readonly struct InspectEvent{
    public readonly string partId;
    public readonly StationId stationId;
    public readonly InspectResult result;
    public readonly DefectType defectType;
    public readonly float timestamp;

    //생성자
    public InspectEvent(string partId, StationId stationId, InspectResult result, DefectType defectType, float timestamp){
        this.partId = partId;
        this.stationId = stationId;
        this.result = result;
        this.defectType = defectType;
        this.timestamp = timestamp;
    }
}