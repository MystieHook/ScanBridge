using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Linq;

namespace ScanBridge
{
    internal static class ImageProcessing
    {
        internal static bool IsBlank(Bitmap image)
        {
            int step = Math.Max(1, Math.Max(image.Width, image.Height) / 500);
            int[] histogram = new int[256]; int total = 0;
            int xMargin = image.Width / 40, yMargin = image.Height / 40;
            for (int y = yMargin; y < image.Height - yMargin; y += step)
                for (int x = xMargin; x < image.Width - xMargin; x += step)
                { var c = image.GetPixel(x, y); histogram[(c.R * 299 + c.G * 587 + c.B * 114) / 1000]++; total++; }
            if (total == 0) return false;
            int background = 255, cumulative = 0;
            for (int i = 0; i < 256; i++) { cumulative += histogram[i]; if (cumulative >= total * .8) { background = i; break; } }
            if (background < 185) return false; // Never discard dark photographs as blank.
            int threshold = Math.Max(0, background - 32), ink = histogram.Take(threshold).Sum();
            return ink / (double)total < .00035;
        }

        internal static Bitmap Process(string path, Profile profile, int rotation)
        {
            Bitmap current;
            using (var source = new Bitmap(path)) current = new Bitmap(source);
            try
            {
                if (profile.deskew)
                {
                    double angle = EstimateSkew(current);
                    if (Math.Abs(angle) > .1)
                    {
                        var corrected = Rotate(current, -angle); current.Dispose(); current = corrected;
                    }
                }
                if (rotation == 90) current.RotateFlip(RotateFlipType.Rotate90FlipNone);
                else if (rotation == 180) current.RotateFlip(RotateFlipType.Rotate180FlipNone);
                else if (rotation == 270) current.RotateFlip(RotateFlipType.Rotate270FlipNone);
                if (profile.colorMode == "gray")
                {
                    var gray = new Bitmap(current.Width, current.Height, PixelFormat.Format24bppRgb);
                    using (var graphics = Graphics.FromImage(gray))
                    using (var attributes = new ImageAttributes())
                    {
                        attributes.SetColorMatrix(new ColorMatrix(new[] {
                            new float[] {.299f,.299f,.299f,0,0}, new float[] {.587f,.587f,.587f,0,0},
                            new float[] {.114f,.114f,.114f,0,0}, new float[] {0,0,0,1,0},new float[] {0,0,0,0,1} }));
                        graphics.DrawImage(current, new Rectangle(0,0,gray.Width,gray.Height), 0,0,current.Width,current.Height,GraphicsUnit.Pixel,attributes);
                    }
                    current.Dispose(); current = gray;
                }
                return current;
            }
            catch { current.Dispose(); throw; }
        }

        private static double EstimateSkew(Bitmap image)
        {
            // Conservative projection-profile deskew, bounded to +/- 3 degrees.
            int width = Math.Min(650, image.Width), height = Math.Max(1, image.Height * width / image.Width);
            using (var small = new Bitmap(image, width, height))
            {
                var points = new System.Collections.Generic.List<Point>();
                for (int y = height / 20; y < height * .95; y += 2)
                    for (int x = width / 20; x < width * .95; x += 2)
                    { var c = small.GetPixel(x,y); if (c.R + c.G + c.B < 420) points.Add(new Point(x,y)); }
                if (points.Count < 80 || points.Count > width * height / 10) return 0;
                double best = 0, bestScore = 0, baseline = 0;
                for (double degrees = -3; degrees <= 3; degrees += .25)
                {
                    double slope = Math.Tan(degrees * Math.PI / 180);
                    var rows = new int[height + width + 10];
                    foreach (var p in points) { int row = (int)Math.Round(p.Y - p.X * slope) + width / 2; if (row >= 0 && row < rows.Length) rows[row]++; }
                    double score = rows.Sum(n => (double)n*n);
                    if (Math.Abs(degrees) < .01) baseline = score;
                    if (score > bestScore) { best = degrees; bestScore = score; }
                }
                return baseline > 0 && bestScore / baseline > 1.15 && Math.Abs(best) < 3 ? best : 0;
            }
        }
        private static Bitmap Rotate(Bitmap image, double degrees)
        {
            double radians = degrees * Math.PI / 180;
            int width = (int)Math.Ceiling(Math.Abs(image.Width*Math.Cos(radians))+Math.Abs(image.Height*Math.Sin(radians)));
            int height = (int)Math.Ceiling(Math.Abs(image.Height*Math.Cos(radians))+Math.Abs(image.Width*Math.Sin(radians)));
            var output = new Bitmap(width,height,PixelFormat.Format24bppRgb);
            using (var g = Graphics.FromImage(output))
            {
                g.Clear(Color.White); g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.TranslateTransform(width/2f,height/2f); g.RotateTransform((float)degrees);
                g.DrawImage(image,-image.Width/2f,-image.Height/2f,image.Width,image.Height);
            }
            return output;
        }
    }
}
