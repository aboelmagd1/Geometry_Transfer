namespace GeometryTransferTool.Models
{
    /// <summary>
    /// Represents the finalized match result for a Source feature.
    /// </summary>
    public class MatchResult
    {
        public long SourceOid { get; set; }
        public string TargetOidDisplay { get; set; } = string.Empty;
        public long? TargetOid { get; set; }
        public double? OverlapPercentage { get; set; }
        public string OverlapDisplay { get; set; } = "-";
        public MatchStatus Status { get; set; }
        public string StatusDisplay => GetStatusDescription();
        public string Details { get; set; } = string.Empty;

        /// <summary>
        /// Gets whether this result is confirmed and valid for geometry transfer.
        /// </summary>
        public bool CanTransfer => Status == MatchStatus.Transferred && TargetOid.HasValue;

        private string GetStatusDescription()
        {
            return Status switch
            {
                MatchStatus.Transferred => "Matched & Ready",
                MatchStatus.BelowThreshold => "Below Threshold",
                MatchStatus.Ambiguous => "Ambiguous",
                MatchStatus.TargetAlreadyMatched => "Target Already Matched",
                MatchStatus.Failed => "Failed",
                _ => Status.ToString()
            };
        }
    }
}
