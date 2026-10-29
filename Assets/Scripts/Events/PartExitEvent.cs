public readonly struct PartExitEvent{
    public readonly string partId;
    public readonly ExitReason reason;
    public readonly DefectType defectType;
    public readonly float timestamp;

    public PartExitEvent(string partId, ExitReason reason, DefectType defectType, float timestamp){
        this.partId = partId;
        this.reason = reason;
        this.defectType = defectType;
        this.timestamp = timestamp;
    }
}