using System;
using ArcGIS.Desktop.Mapping;

namespace GeometryTransferTool.Services
{
    public class ValidationResult
    {
        public bool IsValid { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;

        public static ValidationResult Success() => new() { IsValid = true };
        public static ValidationResult Fail(string message) => new() { IsValid = false, ErrorMessage = message };
    }

    /// <summary>
    /// Validates feature layer selection counts according to §4:
    /// - Both source and target must have 1-60 selected features.
    /// - Strict cap, stops immediately if violated.
    /// </summary>
    public static class SelectionValidationService
    {
        public const int MaxPolygonSelectionLimit = 60;
        public const int MaxPolylineSelectionLimit = 400;

        public static ValidationResult ValidateSelections(FeatureLayer? sourceLayer, FeatureLayer? targetLayer, int sourceCount, int targetCount)
        {
            if (sourceLayer == null)
            {
                return ValidationResult.Fail("Please choose a Source Layer.");
            }

            if (targetLayer == null)
            {
                return ValidationResult.Fail("Please choose a Target Layer.");
            }

            if (sourceLayer.URI == targetLayer.URI)
            {
                return ValidationResult.Fail("Source Layer and Target Layer must be different layers.");
            }

            if (sourceCount <= 0)
            {
                return ValidationResult.Fail("Please select features from the Source / Drawing Layer first.");
            }

            if (targetCount <= 0)
            {
                return ValidationResult.Fail("Please select features from the Target Layer first.");
            }

            bool isPolylineSource = sourceLayer.ShapeType == ArcGIS.Core.CIM.esriGeometryType.esriGeometryPolyline;
            int maxSourceLimit = isPolylineSource ? MaxPolylineSelectionLimit : MaxPolygonSelectionLimit;

            if (sourceCount > maxSourceLimit)
            {
                return ValidationResult.Fail("Selection limit exceeded. Please select a maximum of 60 features ploygon or 400 features line in the Source Layer.");
            }

            if (targetCount > MaxPolygonSelectionLimit)
            {
                return ValidationResult.Fail($"Selection limit exceeded. Please select a maximum of {MaxPolygonSelectionLimit} features in the Target Layer.");
            }

            return ValidationResult.Success();
        }
    }
}
