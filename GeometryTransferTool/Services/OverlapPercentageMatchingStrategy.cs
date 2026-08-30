using System;
using System.Collections.Generic;
using System.Threading;
using ArcGIS.Core.Geometry;
using GeometryTransferTool.Helpers;
using GeometryTransferTool.Models;

namespace GeometryTransferTool.Services
{
    /// <summary>
    /// Matching strategy based on Source Polygon Overlap Percentage:
    /// Overlap % = (Area(Source ∩ Target) / Area(Source)) * 100
    /// </summary>
    public class OverlapPercentageMatchingStrategy : IMatchingStrategy
    {
        public string Name => "Polygon Overlap Percentage";
        public string Description => "Matches polygons by calculating the percentage of the Source polygon area covered by the Target polygon.";

        public List<MatchCandidate> EvaluateCandidates(
            IReadOnlyDictionary<long, Polygon> sources,
            IReadOnlyDictionary<long, Polygon> targets,
            double minThreshold,
            CancellationToken cancellationToken = default)
        {
            var candidates = new List<MatchCandidate>();

            foreach (var (srcOid, srcPoly) in sources)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (srcPoly == null || srcPoly.IsEmpty) continue;

                double srcArea = GeometryHelper.GetPlanarArea(srcPoly);
                if (srcArea <= 0.0)
                {
                    continue;
                }

                var srcEnvelope = srcPoly.Extent;

                foreach (var (tgtOid, tgtPoly) in targets)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (tgtPoly == null || tgtPoly.IsEmpty) continue;

                    var tgtEnvelope = tgtPoly.Extent;

                    // Step 1: Fast envelope pre-filter
                    if (!GeometryHelper.EnvelopesIntersect(srcEnvelope, tgtEnvelope))
                    {
                        continue;
                    }

                    // Step 2: Full geometry intersection
                    Polygon? intersection = GeometryHelper.ComputeIntersection(srcPoly, tgtPoly);
                    if (intersection == null || intersection.IsEmpty)
                    {
                        continue;
                    }

                    double intersectionArea = GeometryHelper.GetPlanarArea(intersection);
                    if (intersectionArea <= 0.0)
                    {
                        continue;
                    }

                    double overlapPct = (intersectionArea / srcArea) * 100.0;
                    // Cap at 100% in case of minor floating point rounding
                    if (overlapPct > 100.0) overlapPct = 100.0;

                    candidates.Add(new MatchCandidate
                    {
                        SourceOid = srcOid,
                        TargetOid = tgtOid,
                        OverlapPercentage = Math.Round(overlapPct, 2),
                        IntersectionArea = intersectionArea,
                        SourceArea = srcArea,
                        TargetArea = GeometryHelper.GetPlanarArea(tgtPoly)
                    });
                }
            }

            return candidates;
        }
    }
}
