using System;
using ArcGIS.Core.Geometry;

namespace GeometryTransferTool.Helpers
{
    /// <summary>
    /// Utility methods for geometric validation, coordinate projection, envelope checks, and planar area computation.
    /// All methods are designed to be safe against nulls, missing spatial references, and native geometry engine exceptions.
    /// </summary>
    public static class GeometryHelper
    {
        /// <summary>
        /// Determines an appropriate common projected spatial reference for metric/planar area calculation.
        /// </summary>
        public static SpatialReference GetCommonProjectedSpatialReference(SpatialReference? sourceSr, SpatialReference? targetSr)
        {
            if (targetSr != null && targetSr.IsProjected)
            {
                return targetSr;
            }

            if (sourceSr != null && sourceSr.IsProjected)
            {
                return sourceSr;
            }

            if (targetSr != null)
            {
                return targetSr;
            }

            if (sourceSr != null)
            {
                return sourceSr;
            }

            return SpatialReferences.WebMercator;
        }

        /// <summary>
        /// Validates, repairs, and projects a polygon geometry into the target spatial reference.
        /// Returns null if geometry is empty, null, or unrepairable.
        /// </summary>
        public static Polygon? ValidateAndPreparePolygon(Geometry? geometry, SpatialReference targetSr, SpatialReference? defaultLayerSr = null)
        {
            if (geometry == null || geometry.IsEmpty)
            {
                return null;
            }

            if (geometry.GeometryType != GeometryType.Polygon)
            {
                return null;
            }

            var polygon = geometry as Polygon;
            if (polygon == null || polygon.IsEmpty)
            {
                return null;
            }

            try
            {
                // If polygon has no spatial reference, assign the layer SR or target SR
                if (polygon.SpatialReference == null)
                {
                    var fallbackSr = defaultLayerSr ?? targetSr;
                    if (fallbackSr != null)
                    {
                        polygon = PolygonBuilderEx.CreatePolygon(polygon, fallbackSr);
                    }
                }

                // Attempt repair if not simple
                if (!GeometryEngine.Instance.IsSimpleAsFeature(polygon))
                {
                    try
                    {
                        var repaired = GeometryEngine.Instance.SimplifyAsFeature(polygon) as Polygon;
                        if (repaired != null && !repaired.IsEmpty)
                        {
                            polygon = repaired;
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn($"Failed to simplify polygon: {ex.Message}");
                    }
                }

                if (polygon == null || polygon.IsEmpty)
                {
                    return null;
                }

                // Project to common spatial reference if needed
                var currentSr = polygon.SpatialReference;
                if (currentSr != null && targetSr != null && !SpatialReference.AreEqual(currentSr, targetSr))
                {
                    try
                    {
                        var projected = GeometryEngine.Instance.Project(polygon, targetSr) as Polygon;
                        if (projected != null && !projected.IsEmpty)
                        {
                            polygon = projected;
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"Projection failed from SR {currentSr.Wkid} to {targetSr.Wkid}: {ex.Message}", ex);
                        return null;
                    }
                }

                return polygon;
            }
            catch (Exception ex)
            {
                Logger.Error($"ValidateAndPreparePolygon error: {ex.Message}", ex);
                return null;
            }
        }

        /// <summary>
        /// Performs a fast 2D axis-aligned bounding box (envelope) intersection pre-check.
        /// </summary>
        public static bool EnvelopesIntersect(Envelope? env1, Envelope? env2)
        {
            if (env1 == null || env2 == null || env1.IsEmpty || env2.IsEmpty)
            {
                return false;
            }

            return !(env1.XMax < env2.XMin ||
                     env1.XMin > env2.XMax ||
                     env1.YMax < env2.YMin ||
                     env1.YMin > env2.YMax);
        }

        /// <summary>
        /// Computes the intersection geometry of two polygons.
        /// Returns null if they do not intersect or result is empty.
        /// </summary>
        public static Polygon? ComputeIntersection(Polygon poly1, Polygon poly2)
        {
            if (poly1 == null || poly2 == null || poly1.IsEmpty || poly2.IsEmpty)
            {
                return null;
            }

            try
            {
                if (!GeometryEngine.Instance.Intersects(poly1, poly2))
                {
                    return null;
                }

                var intersection = GeometryEngine.Instance.Intersection(poly1, poly2) as Polygon;
                if (intersection == null || intersection.IsEmpty)
                {
                    return null;
                }

                return intersection;
            }
            catch (Exception ex)
            {
                Logger.Warn($"ComputeIntersection failed: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Computes the planar or geodesic area of a polygon in square units.
        /// </summary>
        public static double GetPlanarArea(Polygon? polygon)
        {
            if (polygon == null || polygon.IsEmpty)
            {
                return 0.0;
            }

            try
            {
                if (polygon.SpatialReference != null && polygon.SpatialReference.IsGeographic)
                {
                    return Math.Abs(GeometryEngine.Instance.GeodesicArea(polygon, AreaUnit.SquareMeters));
                }
                return Math.Abs(polygon.Area);
            }
            catch
            {
                return Math.Abs(polygon.Area);
            }
        }
    }
}
