using System.Collections.Generic;
using System.Threading;
using ArcGIS.Core.Geometry;
using GeometryTransferTool.Models;

namespace GeometryTransferTool.Services
{
    /// <summary>
    /// Extensible interface for polygon matching algorithms.
    /// Evaluates candidate match pairs on the MCT thread.
    /// </summary>
    public interface IMatchingStrategy
    {
        string Name { get; }
        string Description { get; }

        /// <summary>
        /// Evaluates match candidate pairs between source and target polygons.
        /// </summary>
        /// <param name="sources">Dictionary mapping Source OID to Polygon geometry.</param>
        /// <param name="targets">Dictionary mapping Target OID to Polygon geometry.</param>
        /// <param name="minThreshold">Minimum match score or overlap percentage threshold.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>List of valid candidate triples meeting criteria.</returns>
        List<MatchCandidate> EvaluateCandidates(
            IReadOnlyDictionary<long, Polygon> sources,
            IReadOnlyDictionary<long, Polygon> targets,
            double minThreshold,
            CancellationToken cancellationToken = default);
    }
}
