using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace IconGenerator
{
    class Program
    {
        static void Main(string[] args)
        {
            string imgDir = @"d:\Learning\Geometry Transfer & Smart Polygon Matching (v2 – Refined)\GeometryTransferTool\Images";
            Directory.CreateDirectory(imgDir);

            CreateIcon(Path.Combine(imgDir, "GeometryTransfer16.png"), 16);
            CreateIcon(Path.Combine(imgDir, "GeometryTransfer32.png"), 32);
            CreateIcon(Path.Combine(imgDir, "GeometryTransferDockPane16.png"), 16);
            CreateIcon(Path.Combine(imgDir, "GeometryTransferDockPane32.png"), 32);

            Console.WriteLine("PNG Icons created successfully.");
        }

        static void CreateIcon(string path, int size)
        {
            using (var bmp = new Bitmap(size, size))
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.Clear(Color.Transparent);

                // Source Polygon (Blue / Azure)
                using (var brush1 = new SolidBrush(Color.FromArgb(210, 33, 150, 243)))
                using (var pen1 = new Pen(Color.FromArgb(255, 21, 101, 192), Math.Max(1.0f, size / 16.0f)))
                {
                    PointF[] pts1 = new PointF[]
                    {
                        new PointF(size * 0.1f, size * 0.2f),
                        new PointF(size * 0.55f, size * 0.12f),
                        new PointF(size * 0.45f, size * 0.68f),
                        new PointF(size * 0.12f, size * 0.58f)
                    };
                    g.FillPolygon(brush1, pts1);
                    g.DrawPolygon(pen1, pts1);
                }

                // Target Polygon (Green / Emerald)
                using (var brush2 = new SolidBrush(Color.FromArgb(210, 76, 175, 80)))
                using (var pen2 = new Pen(Color.FromArgb(255, 46, 125, 50), Math.Max(1.0f, size / 16.0f)))
                {
                    PointF[] pts2 = new PointF[]
                    {
                        new PointF(size * 0.38f, size * 0.36f),
                        new PointF(size * 0.88f, size * 0.22f),
                        new PointF(size * 0.88f, size * 0.86f),
                        new PointF(size * 0.42f, size * 0.74f)
                    };
                    g.FillPolygon(brush2, pts2);
                    g.DrawPolygon(pen2, pts2);
                }

                // Transfer Arrow (Gold / Orange with dark outline for contrast)
                using (var penArrowBg = new Pen(Color.FromArgb(220, 30, 30, 30), Math.Max(2.5f, size / 7.0f)))
                using (var penArrow = new Pen(Color.FromArgb(255, 255, 179, 0), Math.Max(1.5f, size / 10.0f)))
                {
                    using (var cap = new AdjustableArrowCap(Math.Max(3f, size / 5f), Math.Max(3f, size / 5f), true))
                    {
                        penArrowBg.CustomEndCap = cap;
                        penArrow.CustomEndCap = cap;

                        float x1 = size * 0.22f;
                        float y1 = size * 0.45f;
                        float x2 = size * 0.65f;
                        float y2 = size * 0.52f;

                        g.DrawLine(penArrowBg, x1, y1, x2, y2);
                        g.DrawLine(penArrow, x1, y1, x2, y2);
                    }
                }

                bmp.Save(path, ImageFormat.Png);
            }
        }
    }
}
