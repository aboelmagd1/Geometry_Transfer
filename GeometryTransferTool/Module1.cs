using System;
using ArcGIS.Desktop.Framework;
using ArcGIS.Desktop.Framework.Contracts;
using GeometryTransferTool.Helpers;

namespace GeometryTransferTool
{
    /// <summary>
    /// ArcGIS Pro Module for Geometry Transfer Tool.
    /// </summary>
    internal class Module1 : Module
    {
        private static Module1? _this;

        public static Module1 Current => _this ??= (Module1)FrameworkApplication.FindModule("GeometryTransferTool_Module");

        protected override bool Initialize()
        {
            Logger.Info("Geometry Transfer Tool Add-in initialized.");
            return base.Initialize();
        }

        protected override bool CanUnload()
        {
            return true;
        }
    }
}
