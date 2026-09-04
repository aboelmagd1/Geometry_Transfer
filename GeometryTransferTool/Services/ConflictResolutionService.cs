using System;
using System.Collections.Generic;
using System.Linq;
using ArcGIS.Core.Geometry;
using GeometryTransferTool.Helpers;
using GeometryTransferTool.Models;

namespace GeometryTransferTool.Services
{
    /// <summary>
    /// Implements §5a Global Conflict Resolution (one-to-one greedy assignment by descending overlap %)
    /// and §8 Ambiguity Detection, with optional threshold bypassing.
    /// </summary>
    public static class ConflictResolutionService
    {
        public static List<MatchResult> ResolveMatches(
            IReadOnlyCollection<long> sourceOids,
            IReadOnlyCollection<long> targetOids,
            List<MatchCandidate> allCandidates,
            double threshold,
            double ambiguityTolerance,
            HashSet<long>? failedSourceOids = null,
            bool ignoreThreshold = false,
            string? runId = null,
            string sourceGeometryType = "Polygon",
            IReadOnlyDictionary<long, Polygon>? workingGeometries = null,
            IReadOnlyDictionary<long, string>? sourceFailureDetails = null)
        {
            failedSourceOids ??= new HashSet<long>();
            var results = new List<MatchResult>();

            string currentRunId = runId ?? $"GT_{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid().ToString().Substring(0, 4).ToUpperInvariant()}";
            int matchCounter = 1;
            bool isPolylineSource = sourceGeometryType.Equals("Polyline", StringComparison.OrdinalIgnoreCase);

            // If ignoreThreshold is true, any positive overlap (> 0%) is eligible
            double effectiveThreshold = ignoreThreshold ? 0.0001 : threshold;

            // Group candidates by source OID, ordered descending by overlap %
            var sourceCandidateMap = new Dictionary<long, List<MatchCandidate>>();
            foreach (var srcOid in sourceOids)
            {
                var candidates = allCandidates
                    .Where(c => c.SourceOid == srcOid)
                    .OrderByDescending(c => c.OverlapPercentage)
                    .ToList();
                sourceCandidateMap[srcOid] = candidates;
            }

            // Step 1: Detect Ambiguity for each source (§8, §11)
            var ambiguousSources = new HashSet<long>();
            var ambiguousResults = new Dictionary<long, MatchResult>();

            foreach (var (srcOid, candidates) in sourceCandidateMap)
            {
                if (failedSourceOids.Contains(srcOid))
                {
                    continue;
                }

                if (candidates.Count >= 2)
                {
                    var top1 = candidates[0];
                    var top2 = candidates[1];

                    // Check if top candidate meets threshold (or is positive when ignoring threshold)
                    if (top1.OverlapPercentage >= effectiveThreshold)
                    {
                        double diff = Math.Abs(top1.OverlapPercentage - top2.OverlapPercentage);
                        if (diff <= ambiguityTolerance)
                        {
                            // Flag as Ambiguous
                            ambiguousSources.Add(srcOid);
                            ambiguousResults[srcOid] = new MatchResult
                            {
                                MatchId = $"GT-M{matchCounter++:D6}",
                                RunId = currentRunId,
                                SourceOid = srcOid,
                                TargetOidDisplay = $"{top1.TargetOid} / {top2.TargetOid}",
                                TargetOid = null,
                                OverlapPct = top1.OverlapPercentage,
                                ThresholdPct = threshold,
                                CandidateCount = candidates.Count,
                                SecondBestOverlapPct = top2.OverlapPercentage,
                                MatchStatus = MatchStatus.Ambiguous,
                                TransferStatus = TransferStatus.NotAttempted,
                                RunDate = DateTime.Now,
                                Details = $"Ambiguous match detected (difference of {diff:F2}% <= tolerance of {ambiguityTolerance:F2}%). Manual review required."
                            };
                            Logger.Info($"Ambiguity detected for Source OID {srcOid}: Targets {top1.TargetOid} ({top1.OverlapPercentage}%) and {top2.TargetOid} ({top2.OverlapPercentage}%)");
                        }
                    }
                }
            }

            // Step 2: Global Greedy One-to-One Resolution (§5a)
            // Filter out candidates from ambiguous or failed sources, and keep only candidates >= effectiveThreshold
            var eligibleCandidates = allCandidates
                .Where(c => !ambiguousSources.Contains(c.SourceOid) &&
                            !failedSourceOids.Contains(c.SourceOid) &&
                            c.OverlapPercentage >= effectiveThreshold)
                .OrderByDescending(c => c.OverlapPercentage)
                .ToList();

            var assignedSources = new HashSet<long>();
            var assignedTargets = new HashSet<long>();
            var confirmedMatches = new Dictionary<long, MatchCandidate>();
            var targetWinningSource = new Dictionary<long, MatchCandidate>();

            foreach (var candidate in eligibleCandidates)
            {
                if (!assignedSources.Contains(candidate.SourceOid) && !assignedTargets.Contains(candidate.TargetOid))
                {
                    assignedSources.Add(candidate.SourceOid);
                    assignedTargets.Add(candidate.TargetOid);
                    confirmedMatches[candidate.SourceOid] = candidate;
                    targetWinningSource[candidate.TargetOid] = candidate;

                    Logger.Info($"Matched Source OID {candidate.SourceOid} -> Target OID {candidate.TargetOid} ({candidate.OverlapPercentage:F1}%)");
                }
            }

            // Step 3: Classify every source feature
            foreach (var srcOid in sourceOids)
            {
                var candidates = sourceCandidateMap.TryGetValue(srcOid, out var list) ? list : new List<MatchCandidate>();
                double? secondBest = candidates.Count >= 2 ? candidates[1].OverlapPercentage : null;
                Polygon? workingPoly = null;
                workingGeometries?.TryGetValue(srcOid, out workingPoly);

                if (failedSourceOids.Contains(srcOid))
                {
                    string failureDetail = "Source feature contains invalid or empty geometry and was skipped.";
                    if (sourceFailureDetails != null && sourceFailureDetails.TryGetValue(srcOid, out var customMsg))
                    {
                        failureDetail = customMsg;
                    }
                    else if (isPolylineSource)
                    {
                        failureDetail = "Source polyline does not form a closed polygon.";
                    }

                    results.Add(new MatchResult
                    {
                        MatchId = $"GT-M{matchCounter++:D6}",
                        RunId = currentRunId,
                        SourceOid = srcOid,
                        TargetOidDisplay = "-",
                        TargetOid = null,
                        OverlapPct = null,
                        ThresholdPct = threshold,
                        CandidateCount = 0,
                        SecondBestOverlapPct = null,
                        MatchStatus = MatchStatus.InvalidGeometry,
                        TransferStatus = TransferStatus.Skipped,
                        RunDate = DateTime.Now,
                        SourceGeometryType = sourceGeometryType,
                        ConversionStatus = isPolylineSource ? "Failed" : "None",
                        WorkingPolygon = null,
                        Details = failureDetail
                    });
                    continue;
                }

                if (ambiguousSources.Contains(srcOid))
                {
                    var ambResult = ambiguousResults[srcOid];
                    ambResult.SourceGeometryType = sourceGeometryType;
                    ambResult.ConversionStatus = isPolylineSource ? "Converted" : "None";
                    ambResult.WorkingPolygon = workingPoly;
                    results.Add(ambResult);
                    continue;
                }

                if (confirmedMatches.TryGetValue(srcOid, out var match))
                {
                    string details = ignoreThreshold
                        ? $"Matched with {match.OverlapPercentage:F1}% overlap (threshold ignored)."
                        : $"Matched with {match.OverlapPercentage:F1}% overlap.";

                    results.Add(new MatchResult
                    {
                        MatchId = $"GT-M{matchCounter++:D6}",
                        RunId = currentRunId,
                        SourceOid = srcOid,
                        TargetOidDisplay = match.TargetOid.ToString(),
                        TargetOid = match.TargetOid,
                        OverlapPct = match.OverlapPercentage,
                        ThresholdPct = threshold,
                        CandidateCount = candidates.Count,
                        SecondBestOverlapPct = secondBest,
                        MatchStatus = MatchStatus.Matched,
                        TransferStatus = TransferStatus.NotAttempted,
                        RunDate = DateTime.Now,
                        SourceGeometryType = sourceGeometryType,
                        ConversionStatus = isPolylineSource ? "Converted" : "None",
                        WorkingPolygon = workingPoly,
                        Details = details
                    });
                    continue;
                }

                // If not confirmed, determine whether it was a conflict (Target already claimed) or Below Threshold / No Intersection
                var topCandidate = candidates.FirstOrDefault();

                if (topCandidate != null && topCandidate.OverlapPercentage >= effectiveThreshold)
                {
                    // The best candidate met the criteria, but was claimed by a higher-overlap source
                    if (targetWinningSource.TryGetValue(topCandidate.TargetOid, out var winningMatch))
                    {
                        results.Add(new MatchResult
                        {
                            MatchId = $"GT-M{matchCounter++:D6}",
                            RunId = currentRunId,
                            SourceOid = srcOid,
                            TargetOidDisplay = topCandidate.TargetOid.ToString(),
                            TargetOid = null,
                            OverlapPct = topCandidate.OverlapPercentage,
                            ThresholdPct = threshold,
                            CandidateCount = candidates.Count,
                            SecondBestOverlapPct = secondBest,
                            MatchStatus = MatchStatus.TargetAlreadyMatched,
                            TransferStatus = TransferStatus.Skipped,
                            RunDate = DateTime.Now,
                            SourceGeometryType = sourceGeometryType,
                            ConversionStatus = isPolylineSource ? "Converted" : "None",
                            WorkingPolygon = workingPoly,
                            Details = $"Target {topCandidate.TargetOid} claimed by Source {winningMatch.SourceOid} ({winningMatch.OverlapPercentage:F1}% overlap)."
                        });
                        continue;
                    }
                }

                // Otherwise, below threshold or no intersection
                if (topCandidate == null || topCandidate.OverlapPercentage <= 0.0)
                {
                    results.Add(new MatchResult
                    {
                        MatchId = $"GT-M{matchCounter++:D6}",
                        RunId = currentRunId,
                        SourceOid = srcOid,
                        TargetOidDisplay = "-",
                        TargetOid = null,
                        OverlapPct = 0.0,
                        ThresholdPct = threshold,
                        CandidateCount = 0,
                        SecondBestOverlapPct = null,
                        MatchStatus = MatchStatus.NoIntersection,
                        TransferStatus = TransferStatus.Skipped,
                        RunDate = DateTime.Now,
                        SourceGeometryType = sourceGeometryType,
                        ConversionStatus = isPolylineSource ? "Converted" : "None",
                        WorkingPolygon = workingPoly,
                        Details = "No intersecting target polygon found."
                    });
                }
                else
                {
                    double bestOverlap = topCandidate.OverlapPercentage;
                    results.Add(new MatchResult
                    {
                        MatchId = $"GT-M{matchCounter++:D6}",
                        RunId = currentRunId,
                        SourceOid = srcOid,
                        TargetOidDisplay = topCandidate.TargetOid.ToString(),
                        TargetOid = null,
                        OverlapPct = bestOverlap,
                        ThresholdPct = threshold,
                        CandidateCount = candidates.Count,
                        SecondBestOverlapPct = secondBest,
                        MatchStatus = MatchStatus.BelowThreshold,
                        TransferStatus = TransferStatus.Skipped,
                        RunDate = DateTime.Now,
                        SourceGeometryType = sourceGeometryType,
                        ConversionStatus = isPolylineSource ? "Converted" : "None",
                        WorkingPolygon = workingPoly,
                        Details = $"Best overlap ({bestOverlap:F1}%) is below minimum threshold ({threshold:F1}%)."
                    });
                }
            }

            return results;
        }
    }
}
