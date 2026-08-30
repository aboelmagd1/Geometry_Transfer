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
        public const int MaxSelectionLimit = 60;

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
                return ValidationResult.Fail("Please select features from the Source Layer first.");
            }

            if (targetCount <= 0)
            {
                return ValidationResult.Fail("Please select features from the Target Layer first.");
            }

            if (sourceCount > MaxSelectionLimit || targetCount > MaxSelectionLimit)
            {
                return ValidationResult.Fail("Selection limit exceeded. Please select a maximum of 60 features in each layer.");
            }

            return ValidationResult.Success();
        }
    }
}
