namespace GeometryTransferTool.Models
{
    /// <summary>
    /// Represents an evaluated pairwise match candidate between a Source and a Target polygon.
    /// </summary>
    public class MatchCandidate
    {
        public long SourceOid { get; set; }
        public long TargetOid { get; set; }
        public double OverlapPercentage { get; set; }
        public double IntersectionArea { get; set; }
        public double SourceArea { get; set; }
        public double TargetArea { get; set; }

        public override string ToString()
        {
            return $"Source OID {SourceOid} -> Target OID {TargetOid} ({OverlapPercentage:F1}%)";
        }
    }
}
