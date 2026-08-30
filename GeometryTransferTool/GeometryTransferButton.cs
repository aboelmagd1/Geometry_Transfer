using ArcGIS.Desktop.Framework;
using ArcGIS.Desktop.Framework.Contracts;

namespace GeometryTransferTool
{
    /// <summary>
    /// Ribbon button to show / activate the Geometry Transfer DockPane.
    /// </summary>
    internal class GeometryTransferButton : Button
    {
        private const string DockPaneId = "GeometryTransferDockPane";

        protected override void OnClick()
        {
            var pane = FrameworkApplication.DockPaneManager.Find(DockPaneId);
            if (pane != null && !pane.IsVisible)
            {
                pane.Activate();
            }
        }
    }
}
