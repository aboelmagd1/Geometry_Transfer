namespace GeometryTransferTool.Models
{
    /// <summary>
    /// Status representing the outcome of polygon matching (§11).
    /// </summary>
    public enum MatchStatus
    {
        /// <summary>
        /// Successfully matched and accepted for geometry transfer.
        /// </summary>
        Matched,

        /// <summary>
        /// Best candidate overlap is below the minimum configured threshold.
        /// </summary>
        BelowThreshold,

        /// <summary>
        /// Multiple candidates are within the ambiguity tolerance of each other. Requires manual review.
        /// </summary>
        Ambiguous,

        /// <summary>
        /// No intersecting target polygon was found.
        /// </summary>
        NoIntersection,

        /// <summary>
        /// The candidate target was claimed by another source polygon with a higher overlap percentage.
        /// </summary>
        TargetAlreadyMatched,

        /// <summary>
        /// Source or candidate target feature contains empty or invalid polygon geometry.
        /// </summary>
        InvalidGeometry,

        /// <summary>
        /// Matching or spatial evaluation failed.
        /// </summary>
        Failed,

        /// <summary>
        /// Feature processing was skipped.
        /// </summary>
        Skipped
    }
}
