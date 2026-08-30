using System;
using System.Windows;
using ArcGIS.Desktop.Framework.Dialogs;

namespace GeometryTransferTool.Helpers
{
    /// <summary>
    /// Helper for displaying ArcGIS Pro dialogs and notification messages.
    /// </summary>
    public static class MessageHelper
    {
        public static void ShowInfo(string message, string title = "Geometry Transfer Tool")
        {
            ArcGIS.Desktop.Framework.Dialogs.MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);
        }

        public static void ShowWarning(string message, string title = "Geometry Transfer Warning")
        {
            ArcGIS.Desktop.Framework.Dialogs.MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        public static void ShowError(string message, string title = "Geometry Transfer Error")
        {
            ArcGIS.Desktop.Framework.Dialogs.MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);
        }

        public static bool ShowQuestion(string message, string title = "Confirm Transfer")
        {
            var result = ArcGIS.Desktop.Framework.Dialogs.MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question);
            return result == MessageBoxResult.Yes;
        }
    }
}
