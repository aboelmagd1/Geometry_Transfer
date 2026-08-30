using System;
using ArcGIS.Core.CIM;
using ArcGIS.Core.Geometry;
using ArcGIS.Desktop.Mapping;
using GeometryTransferTool.Helpers;

namespace GeometryTransferTool.Services
{
    /// <summary>
    /// Validates layer editability, geometry type, and schema compatibility.
    /// </summary>
    public static class LayerValidationService
    {
        /// <summary>
        /// Validates layer geometry types (polygon required) and target editability.
        /// </summary>
        public static ValidationResult ValidateLayers(FeatureLayer? sourceLayer, FeatureLayer? targetLayer)
        {
            if (sourceLayer == null || targetLayer == null)
            {
                return ValidationResult.Fail("Source and Target layers must both be specified.");
            }

            // Verify Geometry Types
            var srcShapeType = sourceLayer.ShapeType;
            if (srcShapeType != esriGeometryType.esriGeometryPolygon)
            {
                return ValidationResult.Fail($"Source layer '{sourceLayer.Name}' is not a polygon layer ({srcShapeType}). Only polygon layers are supported.");
            }

            var tgtShapeType = targetLayer.ShapeType;
            if (tgtShapeType != esriGeometryType.esriGeometryPolygon)
            {
                return ValidationResult.Fail($"Target layer '{targetLayer.Name}' is not a polygon layer ({tgtShapeType}). Only polygon layers are supported.");
            }

            // Verify Target Editability
            if (!targetLayer.CanEditData())
            {
                return ValidationResult.Fail("The Target Layer is not editable. Please verify the layer permissions and editing capabilities.");
            }

            return ValidationResult.Success();
        }
    }
}
