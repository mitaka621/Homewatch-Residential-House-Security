using SkiaSharp;
using System.Globalization;
using System.Xml.Linq;

namespace Homewatch.Core.Utilities
{
    public static class SvgIconRasterizer
    {
        private const float _defaultViewBoxSize = 24f;

        public static byte[] RasterizeToPng(string svgMarkup, string colorHex, int sizeInPixels)
        {
            var root = ParseRoot(svgMarkup);
            var viewBox = ReadViewBox(root);
            var scale = sizeInPixels / Math.Max(viewBox.Width, viewBox.Height);
            var offsetX = (sizeInPixels - viewBox.Width * scale) / 2f;
            var offsetY = (sizeInPixels - viewBox.Height * scale) / 2f;

            var color = SKColor.Parse(colorHex);
            var shapes = new List<(SKPath Path, float Opacity)>();

            try
            {
                CollectShapes(root, 1f, SKPathFillType.Winding, shapes);

                using var surface = SKSurface.Create(new SKImageInfo(sizeInPixels, sizeInPixels, SKColorType.Rgba8888, SKAlphaType.Premul));
                using var paint = new SKPaint
                {
                    IsAntialias = true,
                    Style = SKPaintStyle.Fill
                };

                var canvas = surface.Canvas;

                canvas.Clear(SKColors.Transparent);
                canvas.Translate(offsetX, offsetY);
                canvas.Scale(scale);
                canvas.Translate(-viewBox.Left, -viewBox.Top);

                foreach (var shape in shapes)
                {
                    paint.Color = color.WithAlpha((byte)Math.Round(shape.Opacity * byte.MaxValue));
                    canvas.DrawPath(shape.Path, paint);
                }

                using var image = surface.Snapshot();
                using var data = image.Encode(SKEncodedImageFormat.Png, 100);

                return data.ToArray();
            }
            finally
            {
                foreach (var shape in shapes)
                {
                    shape.Path.Dispose();
                }
            }
        }

        private static XElement ParseRoot(string svgMarkup)
        {
            var markup = svgMarkup.Trim();

            if (!markup.StartsWith("<svg", StringComparison.OrdinalIgnoreCase))
            {
                markup = $"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 24 24\">{markup}</svg>";
            }

            return XElement.Parse(markup);
        }

        private static SKRect ReadViewBox(XElement root)
        {
            var viewBoxValues = SplitNumbers(root.Attribute("viewBox")?.Value);

            if (viewBoxValues.Count == 4 && viewBoxValues[2] > 0 && viewBoxValues[3] > 0)
            {
                return SKRect.Create(viewBoxValues[0], viewBoxValues[1], viewBoxValues[2], viewBoxValues[3]);
            }

            var width = ReadNumber(root, "width", _defaultViewBoxSize);
            var height = ReadNumber(root, "height", _defaultViewBoxSize);

            return SKRect.Create(0, 0, width, height);
        }

        private static void CollectShapes(
            XElement parent,
            float inheritedOpacity,
            SKPathFillType inheritedFillType,
            List<(SKPath Path, float Opacity)> shapes)
        {
            foreach (var element in parent.Elements())
            {
                if (IsNotFilled(element))
                {
                    continue;
                }

                var opacity = inheritedOpacity
                    * ReadNumber(element, "opacity", 1f)
                    * ReadNumber(element, "fill-opacity", 1f);

                var fillType = ReadFillType(element, inheritedFillType);

                if (element.Name.LocalName == "g")
                {
                    CollectShapes(element, opacity, fillType, shapes);
                    continue;
                }

                var path = CreatePath(element);

                if (path != null)
                {
                    path.FillType = fillType;
                    shapes.Add((path, Math.Clamp(opacity, 0f, 1f)));
                }
            }
        }

        private static SKPath? CreatePath(XElement element)
        {
            switch (element.Name.LocalName)
            {
                case "path":
                    return SKPath.ParseSvgPathData(element.Attribute("d")?.Value ?? string.Empty);

                case "rect":
                    var width = ReadNumber(element, "width", 0);
                    var height = ReadNumber(element, "height", 0);

                    if (width <= 0 || height <= 0)
                    {
                        return null;
                    }

                    var rectangle = SKRect.Create(ReadNumber(element, "x", 0), ReadNumber(element, "y", 0), width, height);
                    var cornerRadius = ReadNumber(element, "rx", ReadNumber(element, "ry", 0));
                    var rectanglePath = new SKPath();

                    if (cornerRadius > 0)
                    {
                        rectanglePath.AddRoundRect(rectangle, cornerRadius, cornerRadius);
                    }
                    else
                    {
                        rectanglePath.AddRect(rectangle);
                    }

                    return rectanglePath;

                case "circle":
                    var radius = ReadNumber(element, "r", 0);

                    if (radius <= 0)
                    {
                        return null;
                    }

                    var circlePath = new SKPath();

                    circlePath.AddCircle(ReadNumber(element, "cx", 0), ReadNumber(element, "cy", 0), radius);

                    return circlePath;

                case "ellipse":
                    var radiusX = ReadNumber(element, "rx", 0);
                    var radiusY = ReadNumber(element, "ry", 0);

                    if (radiusX <= 0 || radiusY <= 0)
                    {
                        return null;
                    }

                    var ellipsePath = new SKPath();

                    ellipsePath.AddOval(SKRect.Create(ReadNumber(element, "cx", 0) - radiusX, ReadNumber(element, "cy", 0) - radiusY, radiusX * 2, radiusY * 2));

                    return ellipsePath;

                case "polygon":
                case "polyline":
                    var coordinates = SplitNumbers(element.Attribute("points")?.Value);

                    if (coordinates.Count < 6)
                    {
                        return null;
                    }

                    var points = new SKPoint[coordinates.Count / 2];

                    for (var index = 0; index < points.Length; index++)
                    {
                        points[index] = new SKPoint(coordinates[index * 2], coordinates[index * 2 + 1]);
                    }

                    var polygonPath = new SKPath();

                    polygonPath.AddPoly(points, true);

                    return polygonPath;

                default:
                    return null;
            }
        }

        private static bool IsNotFilled(XElement element)
        {
            var fill = element.Attribute("fill")?.Value.Trim();
            var style = element.Attribute("style")?.Value.Replace(" ", string.Empty) ?? string.Empty;

            return string.Equals(fill, "none", StringComparison.OrdinalIgnoreCase)
                || style.Contains("fill:none", StringComparison.OrdinalIgnoreCase)
                || style.Contains("display:none", StringComparison.OrdinalIgnoreCase);
        }

        private static SKPathFillType ReadFillType(XElement element, SKPathFillType inheritedFillType)
        {
            var fillRule = element.Attribute("fill-rule")?.Value.Trim();

            if (string.IsNullOrEmpty(fillRule))
            {
                return inheritedFillType;
            }

            return string.Equals(fillRule, "evenodd", StringComparison.OrdinalIgnoreCase)
                ? SKPathFillType.EvenOdd
                : SKPathFillType.Winding;
        }

        private static float ReadNumber(XElement element, string attributeName, float defaultValue)
        {
            var value = element.Attribute(attributeName)?.Value.Trim().Replace("px", string.Empty);

            return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
                ? number
                : defaultValue;
        }

        private static List<float> SplitNumbers(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return new List<float>();
            }

            return value
                .Split([' ', ',', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries)
                .Select(x => float.TryParse(x, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ? number : float.NaN)
                .Where(x => !float.IsNaN(x))
                .ToList();
        }
    }
}
