namespace GeometryTransferTool.Models
{
    /// <summary>
    /// Holds aggregated summary statistics of a preview or transfer operation.
    /// </summary>
    public class TransferSummary
    {
        public int TotalSourceFeatures { get; set; }
        public int TotalTargetFeatures { get; set; }
        public int MatchedCount { get; set; }
        public int BelowThresholdCount { get; set; }
        public int AmbiguousCount { get; set; }
        public int TargetAlreadyMatchedCount { get; set; }
        public int NoIntersectionCount { get; set; }
        public int FailedCount { get; set; }

        public bool HasMatches => MatchedCount > 0;
        public bool HasIssues => BelowThresholdCount > 0 || AmbiguousCount > 0 || TargetAlreadyMatchedCount > 0 || NoIntersectionCount > 0 || FailedCount > 0;

        public override string ToString()
        {
            return $"Source: {TotalSourceFeatures}, Target: {TotalTargetFeatures} | Matched: {MatchedCount}, Below Threshold: {BelowThresholdCount}, Ambiguous: {AmbiguousCount}, Conflict: {TargetAlreadyMatchedCount}, No Match: {NoIntersectionCount}, Failed: {FailedCount}";
        }
    }
}
