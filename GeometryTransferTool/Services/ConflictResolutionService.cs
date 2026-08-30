using System;
using System.Collections.Generic;
using System.Linq;
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
            bool ignoreThreshold = false)
        {
            failedSourceOids ??= new HashSet<long>();
            var results = new List<MatchResult>();

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

            // Step 1: Detect Ambiguity for each source (§8)
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
                                SourceOid = srcOid,
                                TargetOidDisplay = $"{top1.TargetOid} / {top2.TargetOid}",
                                TargetOid = null,
                                OverlapPercentage = top1.OverlapPercentage,
                                OverlapDisplay = $"{top1.OverlapPercentage:F1}% / {top2.OverlapPercentage:F1}%",
                                Status = MatchStatus.Ambiguous,
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
                if (failedSourceOids.Contains(srcOid))
                {
                    results.Add(new MatchResult
                    {
                        SourceOid = srcOid,
                        TargetOidDisplay = "-",
                        TargetOid = null,
                        OverlapPercentage = null,
                        OverlapDisplay = "-",
                        Status = MatchStatus.Failed,
                        Details = "Invalid source geometry or geometry repair failed."
                    });
                    continue;
                }

                if (ambiguousSources.Contains(srcOid))
                {
                    results.Add(ambiguousResults[srcOid]);
                    continue;
                }

                if (confirmedMatches.TryGetValue(srcOid, out var match))
                {
                    string details = ignoreThreshold
                        ? $"Matched with {match.OverlapPercentage:F1}% overlap (threshold ignored)."
                        : $"Matched with {match.OverlapPercentage:F1}% overlap.";

                    results.Add(new MatchResult
                    {
                        SourceOid = srcOid,
                        TargetOidDisplay = match.TargetOid.ToString(),
                        TargetOid = match.TargetOid,
                        OverlapPercentage = match.OverlapPercentage,
                        OverlapDisplay = $"{match.OverlapPercentage:F1}%",
                        Status = MatchStatus.Transferred,
                        Details = details
                    });
                    continue;
                }

                // If not confirmed, determine whether it was a conflict (Target already claimed) or Below Threshold / No Match
                var candidates = sourceCandidateMap.TryGetValue(srcOid, out var list) ? list : new List<MatchCandidate>();
                var topCandidate = candidates.FirstOrDefault();

                if (topCandidate != null && topCandidate.OverlapPercentage >= effectiveThreshold)
                {
                    // The best candidate met the criteria, but was claimed by a higher-overlap source
                    if (targetWinningSource.TryGetValue(topCandidate.TargetOid, out var winningMatch))
                    {
                        results.Add(new MatchResult
                        {
                            SourceOid = srcOid,
                            TargetOidDisplay = topCandidate.TargetOid.ToString(),
                            TargetOid = null,
                            OverlapPercentage = topCandidate.OverlapPercentage,
                            OverlapDisplay = $"{topCandidate.OverlapPercentage:F1}%",
                            Status = MatchStatus.TargetAlreadyMatched,
                            Details = $"Target {topCandidate.TargetOid} claimed by Source {winningMatch.SourceOid} ({winningMatch.OverlapPercentage:F1}% overlap)."
                        });
                        continue;
                    }
                }

                // Otherwise, below threshold or no intersection
                double? bestOverlap = topCandidate?.OverlapPercentage;
                string tgtDisplay = topCandidate != null ? topCandidate.TargetOid.ToString() : "-";
                string overlapStr = bestOverlap.HasValue ? $"{bestOverlap.Value:F1}%" : "0.0%";

                string belowDetails = ignoreThreshold
                    ? "No intersecting target polygon found."
                    : (bestOverlap.HasValue
                        ? $"Best overlap ({bestOverlap.Value:F1}%) is below minimum threshold ({threshold:F1}%)."
                        : "No intersecting target polygons found.");

                results.Add(new MatchResult
                {
                    SourceOid = srcOid,
                    TargetOidDisplay = tgtDisplay,
                    TargetOid = null,
                    OverlapPercentage = bestOverlap ?? 0.0,
                    OverlapDisplay = overlapStr,
                    Status = MatchStatus.BelowThreshold,
                    Details = belowDetails
                });
            }

            return results;
        }
    }
}
