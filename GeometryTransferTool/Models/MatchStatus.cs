namespace GeometryTransferTool.Models
{
    /// <summary>
    /// Status representing the outcome of polygon matching and geometry transfer.
    /// </summary>
    public enum MatchStatus
    {
        /// <summary>
        /// Successfully matched and ready to transfer (during Preview) or already transferred (after Transfer).
        /// </summary>
        Transferred,

        /// <summary>
        /// Best candidate overlap is below the minimum threshold.
        /// </summary>
        BelowThreshold,

        /// <summary>
        /// Multiple candidates are within the ambiguity tolerance of each other. Requires manual review.
        /// </summary>
        Ambiguous,

        /// <summary>
        /// The candidate target was claimed by another source with a higher overlap percentage.
        /// </summary>
        TargetAlreadyMatched,

        /// <summary>
        /// Processing failed due to invalid geometry, repair failure, or an execution error.
        /// </summary>
        Failed
    }
}
