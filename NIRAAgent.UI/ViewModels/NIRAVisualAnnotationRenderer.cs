using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NIRAAgent.Artifacts;

namespace NIRAAgent.UI.ViewModels;

// WPF-only drawing: no model-generated XAML, scripts, or screen actions.
// Source images remain unmodified; the same visual is used in chat and viewer.
public static class NIRAVisualAnnotationRenderer
{
    public static ImageSource? Render(
        ImageSource? source, IReadOnlyList<NIRAVisualAnnotation> annotations)
    {
        if (source is not BitmapSource bitmap || annotations.Count == 0)
            return source;
        double w = bitmap.PixelWidth, h = bitmap.PixelHeight;
        if (w < 1 || h < 1) return source;
        DrawingGroup drawing = new();
        drawing.Children.Add(new ImageDrawing(bitmap, new Rect(0, 0, w, h)));
        Pen cyan = new(new SolidColorBrush(Color.FromRgb(34, 211, 238)),
            Math.Max(2, w * 0.003));
        cyan.Freeze();
        foreach (NIRAVisualAnnotation raw in annotations.Take(12))
        {
            NIRAVisualAnnotation a;
            try { a = raw.Normalize(); }
            catch (InvalidOperationException) { continue; }
            Rect box = new(a.X * w, a.Y * h, a.Width * w, a.Height * h);
            if (a.Kind == "rectangle")
            {
                drawing.Children.Add(new GeometryDrawing(
                    null, cyan, new RectangleGeometry(box, 5, 5)));
            }
            else
            {
                // Arrow from the start of the grounded source rectangle
                // toward its opposite corner. No UI clicks are generated.
                Point start = box.TopLeft, end = box.BottomRight;
                Vector backwards = start - end;
                if (backwards.Length < 1) continue;
                backwards.Normalize();
                Vector side = new(-backwards.Y, backwards.X);
                double length = Math.Max(12, w * 0.018);
                drawing.Children.Add(new GeometryDrawing(null, cyan,
                    new LineGeometry(start, end)));
                drawing.Children.Add(new GeometryDrawing(null, cyan,
                    new LineGeometry(end, end + backwards * length + side * length * 0.45)));
                drawing.Children.Add(new GeometryDrawing(null, cyan,
                    new LineGeometry(end, end + backwards * length - side * length * 0.45)));
            }
        }
        drawing.Freeze();
        DrawingImage rendered = new(drawing);
        rendered.Freeze();
        return rendered;
    }
}
