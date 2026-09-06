using System;
using System.Text;
using SkiaSharp;
using Svg.Skia;

namespace SmartPaste
{
    /// <summary>
    /// Rasterizes SVG (MathJax/KaTeX equations, vector icons) to PNG via Skia.
    /// SVG cannot be embedded in RTF and is dropped by the CF_BITMAP fallback, so a
    /// raster form lets equations and vector art survive into WordPad and image editors.
    /// The self-contained CF_HTML path keeps the original SVG untouched for rich targets.
    /// </summary>
    public static class SvgRasterizer
    {
        /// <summary>
        /// Renders SVG bytes to PNG. <paramref name="cssWidth"/>/<paramref name="cssHeight"/> are
        /// the intended display size in CSS px; the raster is produced at <paramref name="scale"/>×
        /// that for crispness. Returns null when the SVG cannot be parsed or has no picture.
        /// </summary>
        public static byte[]? ToPng(byte[] svgBytes, int cssWidth, int cssHeight, int scale = 2)
        {
            try
            {
                string svgText = Encoding.UTF8.GetString(svgBytes);
                using var svg = new SKSvg();
                if (svg.FromSvg(svgText) is null || svg.Picture is null)
                    return null;

                SKRect bounds = svg.Picture.CullRect;
                float srcW = bounds.Width > 0 ? bounds.Width : cssWidth;
                float srcH = bounds.Height > 0 ? bounds.Height : cssHeight;
                if (srcW <= 0 || srcH <= 0) return null;

                int targetW = Math.Max(1, (cssWidth > 0 ? cssWidth : (int)Math.Ceiling(srcW)) * scale);
                int targetH = Math.Max(1, (cssHeight > 0 ? cssHeight : (int)Math.Ceiling(srcH)) * scale);

                var info = new SKImageInfo(targetW, targetH, SKColorType.Rgba8888, SKAlphaType.Premul);
                using var surface = SKSurface.Create(info);
                if (surface is null) return null;

                SKCanvas canvas = surface.Canvas;
                canvas.Clear(SKColors.Transparent);
                canvas.Scale(targetW / srcW, targetH / srcH);
                canvas.Translate(-bounds.Left, -bounds.Top);
                canvas.DrawPicture(svg.Picture);
                canvas.Flush();

                using SKImage image = surface.Snapshot();
                using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);
                return data?.ToArray();
            }
            catch
            {
                return null;
            }
        }
    }
}
