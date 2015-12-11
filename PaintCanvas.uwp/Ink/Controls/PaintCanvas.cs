using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Numerics;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using Windows.UI;
using Windows.UI.Input;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.Graphics.Canvas.UI;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Windows.Devices.Input;
using Windows.Graphics.Display;
using Microsoft.Graphics.Canvas.Effects;

namespace Painting.Ink.Controls
{
    public sealed class PaintCanvas : Control
    {
        private static readonly CanvasStrokeStyle StrokeStyle = new CanvasStrokeStyle
        {
            StartCap = CanvasCapStyle.Round,
            EndCap = CanvasCapStyle.Round
        };

        private readonly ObservableCollection<InkLayer> _layers;
        private readonly Stack<KeyValuePair<InkLayer, CanvasRenderTarget>> _undoBuffer;
        private readonly Stack<KeyValuePair<InkLayer, CanvasRenderTarget>> _redoBuffer;
        private readonly Dictionary<uint, Point> _inputs;
        private readonly Dictionary<uint, double> _previousPressures;

        private ScrollViewer _scrollViewer;
        private CanvasVirtualControl _canvas;
        private CanvasBitmap _background;

        public ReadOnlyObservableCollection<InkLayer> Layers
            => new ReadOnlyObservableCollection<InkLayer>(_layers);

        public event EventHandler Win2dInitialized;

        public static readonly DependencyProperty StrokeColorProperty = DependencyProperty.Register(
            "StrokeColor", typeof(Color), typeof(PaintCanvas), new PropertyMetadata(default(Color)));

        public Color StrokeColor
        {
            get { return (Color)GetValue(StrokeColorProperty); }
            set { SetValue(StrokeColorProperty, value); }
        }

        public static readonly DependencyProperty StrokeThicknessProperty = DependencyProperty.Register(
            "StrokeThickness", typeof(double), typeof(PaintCanvas), new PropertyMetadata(default(double)));

        public double StrokeThickness
        {
            get { return (double)GetValue(StrokeThicknessProperty); }
            set { SetValue(StrokeThicknessProperty, value); }
        }

        public static readonly DependencyProperty PenModeProperty = DependencyProperty.Register(
            "PenMode", typeof(PenMode), typeof(PaintCanvas), new PropertyMetadata(default(PenMode)));

        public PenMode PenMode
        {
            get { return (PenMode)GetValue(PenModeProperty); }
            set { SetValue(PenModeProperty, value); }
        }

        public static readonly DependencyProperty ActiveLayerProperty = DependencyProperty.Register(
            "ActiveLayer", typeof(InkLayer), typeof(PaintCanvas), new PropertyMetadata(default(InkLayer)));

        public InkLayer ActiveLayer
        {
            get { return (InkLayer)GetValue(ActiveLayerProperty); }
            set { SetValue(ActiveLayerProperty, value); }
        }

        public static readonly DependencyProperty CanvasWidthProperty = DependencyProperty.Register(
            "CanvasWidth", typeof(double), typeof(PaintCanvas), new PropertyMetadata(default(double), CanvasSizeChanged));

        public double CanvasWidth
        {
            get { return (double)GetValue(CanvasWidthProperty); }
            set { SetValue(CanvasWidthProperty, value); }
        }

        public static readonly DependencyProperty CanvasHeightProperty = DependencyProperty.Register(
            "CanvasHeight", typeof(double), typeof(PaintCanvas), new PropertyMetadata(default(double), CanvasSizeChanged));

        public double CanvasHeight
        {
            get { return (double)GetValue(CanvasHeightProperty); }
            set { SetValue(CanvasHeightProperty, value); }
        }

        public static readonly DependencyProperty CanScrollableProperty = DependencyProperty.Register(
            "CanScrollable", typeof(bool), typeof(PaintCanvas), new PropertyMetadata(default(bool), CanScrollableChanged));

        public bool CanScrollable
        {
            get { return (bool)GetValue(CanScrollableProperty); }
            set { SetValue(CanScrollableProperty, value); }
        }

        private static void CanvasSizeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            ((PaintCanvas)d).CanvasSizeChanged((PaintCanvas)d, (SizeChangedEventArgs)null);
        }

        private static void CanScrollableChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            ((PaintCanvas)d).CanScrollableChanged(e);
        }

        private void CanScrollableChanged(DependencyPropertyChangedEventArgs e)
        {
            if (_scrollViewer == null) return;
            _scrollViewer.HorizontalScrollBarVisibility = _scrollViewer.VerticalScrollBarVisibility =
                CanScrollable ? ScrollBarVisibility.Auto : ScrollBarVisibility.Hidden;
            _scrollViewer.HorizontalScrollMode = _scrollViewer.VerticalScrollMode =
                CanScrollable ? ScrollMode.Auto : ScrollMode.Disabled;
            _canvas.ManipulationMode = CanScrollable ? ManipulationModes.System : ManipulationModes.None;
        }

        public PaintCanvas()
        {
            DefaultStyleKey = typeof(PaintCanvas);
            _layers = new ObservableCollection<InkLayer>();
            _undoBuffer = new Stack<KeyValuePair<InkLayer, CanvasRenderTarget>>();
            _redoBuffer = new Stack<KeyValuePair<InkLayer, CanvasRenderTarget>>();
            _inputs = new Dictionary<uint, Point>();
            _previousPressures = new Dictionary<uint, double>();
            // handle unload
            Unloaded += ThisUnloaded;
        }

        protected override void OnApplyTemplate()
        {
            _canvas = (CanvasVirtualControl)GetTemplateChild("PART_canvas");
            _canvas.ManipulationMode = CanScrollable ? ManipulationModes.System : ManipulationModes.None;
            _canvas.CreateResources += CanvasCreateResources;
            _canvas.SizeChanged += CanvasSizeChanged;
            _canvas.RegionsInvalidated += _canvas_RegionsInvalidated;
            _canvas.PointerPressed += CanvasPointerPressed;
            _canvas.PointerMoved += CanvasPointerMoved;
            _canvas.PointerReleased += CanvasPointerReleased;
            _canvas.PointerCanceled += CanvasPointerReleased;
            _canvas.PointerCaptureLost += CanvasPointerReleased;
            //
            _scrollViewer = (ScrollViewer)GetTemplateChild("PART_ScrollViewer");
            _scrollViewer.HorizontalScrollBarVisibility = _scrollViewer.VerticalScrollBarVisibility =
                CanScrollable ? ScrollBarVisibility.Auto : ScrollBarVisibility.Hidden;
            _scrollViewer.HorizontalScrollMode = _scrollViewer.VerticalScrollMode =
                CanScrollable ? ScrollMode.Auto : ScrollMode.Disabled;
        }

        private void _canvas_RegionsInvalidated(CanvasVirtualControl sender, CanvasRegionsInvalidatedEventArgs args)
        {
            using (var ds = sender.CreateDrawingSession(args.VisibleRegion))
            {
                DrawFrame(ds);
            }
        }

        private void ThisUnloaded(object sender, RoutedEventArgs e)
        {
            _canvas?.RemoveFromVisualTree();
            _canvas = null;
        }

        private async void CanvasCreateResources(CanvasVirtualControl sender, CanvasCreateResourcesEventArgs args)
        {
            // create default layer
            AddLayer();
            _background = await CanvasBitmap.LoadAsync(sender, new Uri("ms-appx:///PaintCanvas/Assets/canvas.png"));
            _canvas.Invalidate();
            Win2dInitialized?.Invoke(this, EventArgs.Empty);
        }

        private void CanvasSizeChanged(object sender, SizeChangedEventArgs sizeChangedEventArgs)
        {
            foreach (var layer in _layers)
            {
                var image = CanvasRenderTargetExtension.CreateEmpty(_canvas, new Size(CanvasWidth, CanvasHeight));
                using (var ds = image.CreateDrawingSession())
                {
                    ds.DrawImage(layer.Image);
                }
                var old = layer.Image;
                layer.Image = image;
                old.Dispose();
            }
        }

        private void DrawFrame(CanvasDrawingSession ds)
        {
            ds.Clear();
            if (_background != null)
            {
                var tile = new TileEffect();
                tile.Source = _background;
                tile.SourceRectangle = new Rect(0, 0, 400, 400);
                ds.DrawImage(tile);
            }
            foreach (var layer in _layers)
            {
                if (!layer.IsVisible) continue;
                ds.DrawImage(layer.Image);
            }
        }

        private void CanvasPointerPressed(object sender, PointerRoutedEventArgs e)
        {
            var pt = e.GetCurrentPoint(_canvas);
            _inputs[pt.PointerId] = pt.Position;
            _previousPressures[pt.PointerId] = pt.ComputePressure();
            _canvas.CapturePointer(e.Pointer);
            // save undo buffer
            var activeLayer = ActiveLayer;
            if (activeLayer == null || activeLayer.IsLocked) return;
            var undo = activeLayer.Image.Clone();
            using (var ds = undo.CreateDrawingSession())
            {
                ds.DrawImage(activeLayer.Image);
            }
            _undoBuffer.Push(new KeyValuePair<InkLayer, CanvasRenderTarget>(activeLayer, undo));
            while (_redoBuffer.Count > 0)
            {
                var buffer = _redoBuffer.Pop();
                buffer.Value.Dispose();
            }
        }

        private void CanvasPointerMoved(object sender, PointerRoutedEventArgs e)
        {
            var activeLayer = ActiveLayer?.Image;
            if (activeLayer == null || (ActiveLayer?.IsLocked ?? false)) return;
            if (!_inputs.ContainsKey(e.Pointer.PointerId)) return;
            if (!e.Pointer.IsInContact) return;
            var pt = e.GetCurrentPoint(_canvas);
            var from = _inputs[pt.PointerId].ToVector2();
            var to = pt.Position.ToVector2();
            if (IsPenModeEraser(pt.Properties))
            {
                activeLayer.EraseLine(from, to, (float)StrokeThickness, StrokeStyle);
            }
            else
            {
                var L = _inputs[pt.PointerId].Distance(pt.Position);
                var k = L / 8; // 8px
                var step = (pt.ComputePressure() - _previousPressures[pt.PointerId]) / 4;
                var segments = PointExtension.SplitSegments(_inputs[pt.PointerId], pt.Position, Math.Max(4, (int)k));
                var pressure = _previousPressures[pt.PointerId];
                var prevPt = _inputs[pt.PointerId];
                for (var i = 0; i < segments.Length; ++i)
                {
                    var p = (pressure + step * i);
                    var width = (float)StrokeThickness * p;
                    var opacity = p < 0.5 ? p * 2 : 1.0f;
                    activeLayer.DrawLine(prevPt.ToVector2(), segments[i].ToVector2(), StrokeColor, (float)width,
                        StrokeStyle, (float)opacity);
                    prevPt = segments[i];
                }
            }
            _inputs[pt.PointerId] = pt.Position;
            _previousPressures[pt.PointerId] = pt.ComputePressure();
            _canvas.Invalidate();
        }

        private void CanvasPointerReleased(object sender, PointerRoutedEventArgs e)
        {
            if (!_inputs.ContainsKey(e.Pointer.PointerId)) return;
            _inputs.Remove(e.Pointer.PointerId);
            _previousPressures.Remove(e.Pointer.PointerId);
            _canvas.ReleasePointerCapture(e.Pointer);
        }

        private bool IsPenModeEraser(PointerPointProperties prop)
        {
            return PenMode == PenMode.Eraser | prop.IsEraser | prop.IsRightButtonPressed;
        }

        private void Layer_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "IsVisible")
            {
                _canvas.Invalidate();
            }
        }

        public InkLayer AddLayer()
        {
            return AddLayer("Layer #" + (_layers.Count + 1));
        }

        public InkLayer AddLayer(string name)
        {
            var layer = new InkLayer
            {
                Image = CanvasRenderTargetExtension.CreateEmpty(_canvas, new Size(CanvasWidth, CanvasHeight)),
                Name = name,
                IsVisible = true
            };
            layer.PropertyChanged += Layer_PropertyChanged;
            using (var ds = layer.Image.CreateDrawingSession())
            {
                ds.Clear();
            }
            _layers.Add(layer);
            ActiveLayer = layer;
            return layer;
        }

        public void RemoveLayer(InkLayer layer)
        {
            var n = _layers.IndexOf(layer);
            _layers.Remove(layer);
            layer.Image.Dispose();
            ActiveLayer = n < _layers.Count && n >= 0 ? _layers[n] : null;
            _canvas.Invalidate();
        }

        public void Undo()
        {
            if (_undoBuffer.Count == 0) return;
            while (_undoBuffer.Count > 0)
            {
                var buffer = _undoBuffer.Pop();
                if (!_layers.Contains(buffer.Key))
                {
                    buffer.Value.Dispose();
                    continue;
                }
                // build redo image
                var redo = buffer.Value.Clone();
                using (var ds = redo.CreateDrawingSession())
                {
                    ds.DrawImage(buffer.Key.Image);
                }
                _redoBuffer.Push(new KeyValuePair<InkLayer, CanvasRenderTarget>(buffer.Key, redo));
                using (var ds = buffer.Key.Image.CreateDrawingSession())
                {
                    ds.Clear();
                    ds.DrawImage(buffer.Value);
                }
                buffer.Value.Dispose();
                break;
            }
            _canvas.Invalidate();
        }

        public void Redo()
        {
            if (_redoBuffer.Count == 0) return;
            while (_redoBuffer.Count > 0)
            {
                var buffer = _redoBuffer.Pop();
                if (!_layers.Contains(buffer.Key))
                {
                    buffer.Value.Dispose();
                    continue;
                }
                // build redo image
                var undo = buffer.Value.Clone();
                using (var ds = undo.CreateDrawingSession())
                {
                    ds.DrawImage(buffer.Key.Image);
                }
                _undoBuffer.Push(new KeyValuePair<InkLayer, CanvasRenderTarget>(buffer.Key, undo));
                using (var ds = buffer.Key.Image.CreateDrawingSession())
                {
                    ds.Clear();
                    ds.DrawImage(buffer.Value);
                }
                buffer.Value.Dispose();
                break;
            }
            _canvas.Invalidate();
        }

        public Task<bool> Export(IRandomAccessStream saveTo)
        {
            return Export(saveTo, BitmapEncoder.PngEncoderId);
        }

        public async Task<bool> Export(IRandomAccessStream saveTo, Guid encoderId)
        {
            var canvas = CanvasRenderTargetExtension.CreateEmpty(_canvas, new Size(CanvasWidth, CanvasHeight), 96);
            using (var ds = canvas.CreateDrawingSession())
            {
                DrawFrame(ds);
            }
            var pixels = canvas.GetPixelBytes();
            var encoder = await BitmapEncoder.CreateAsync(encoderId, saveTo);
            encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied,
                (uint)CanvasWidth, (uint)CanvasHeight, 96, 96, pixels);
            await encoder.FlushAsync();
            canvas.Dispose();
            return true;
        }

        public async Task<bool> ImportPicture(IRandomAccessStream stream)
        {
            var layer = new InkLayer
            {
                Image = CanvasRenderTargetExtension.CreateEmpty(_canvas, new Size(CanvasWidth, CanvasHeight)),
                Name = "Imported",
                IsVisible = true
            };
            layer.PropertyChanged += Layer_PropertyChanged;
            using (var bitmap = await CanvasBitmap.LoadAsync(_canvas, stream))
            using (var ds = layer.Image.CreateDrawingSession())
            {
                ds.Clear();
                ds.DrawImage(bitmap);
            }
            _layers.Insert(0, layer);
            _canvas.Invalidate();
            return true;
        }
    }

    internal static class CanvasDrawingSessionExtension
    {
        public static void Clear(this CanvasDrawingSession session)
        {
            session.Clear(Colors.Transparent);
        }
    }

    internal static class CanvasRenderTargetExtension
    {
        private static readonly Matrix3x2 Matrix3x2Identity = new Matrix3x2
        {
            M11 = 1.0f,
            M22 = 1.0f
        };

        public static void DrawLine(this CanvasRenderTarget target, Vector2 from, Vector2 to, Color color,
            float strokeWidth, CanvasStrokeStyle strokeStyle, float opacity)
        {
            using (var ds = target.CreateDrawingSession())
            {
                ds.DrawLine(from, to, new Color() { A = (byte)(255 * opacity), B = color.B, G = color.G, R = color.R },
                    strokeWidth, strokeStyle);
            }
        }

        public static void EraseLine(this CanvasRenderTarget target, Vector2 from, Vector2 to,
            float strokeWidth, CanvasStrokeStyle strokeStyle)
        {
            var rect = new Rect(0, 0, target.SizeInPixels.Width, target.SizeInPixels.Height);
            using (var overall = CanvasGeometry.CreateRectangle(target.Device, rect))
            using (var path = new CanvasPathBuilderHelper(from, to).Build(target.Device))
            using (var eraserbase = CanvasGeometry.CreatePath(path))
            using (var eraser = eraserbase.Stroke(strokeWidth, strokeStyle))
            using (var mask = overall.CombineWith(eraser, Matrix3x2Identity, CanvasGeometryCombine.Exclude))
            using (var buffer = target.Clone())
            {
                using (var bds = buffer.CreateDrawingSession())
                {
                    bds.Clear();
                    using (bds.CreateLayer(1.0f, mask))
                    {
                        bds.DrawImage(target);
                    }
                }
                using (var ds = target.CreateDrawingSession())
                {
                    ds.Clear();
                    ds.DrawImage(buffer);
                }
            }
        }

        public static CanvasRenderTarget Clone(this CanvasRenderTarget target)
        {
            return CreateEmpty(target.Device, target.Size, target.Dpi);
        }

        public static CanvasRenderTarget CreateEmpty(ICanvasResourceCreatorWithDpi device, Size size)
        {
            return CreateEmpty(device, size, device.Dpi);
        }

        public static CanvasRenderTarget CreateEmpty(ICanvasResourceCreator device, Size size, float dpi)
        {
            var target = new CanvasRenderTarget(device, (float)size.Width, (float)size.Height, dpi);
            using (var ds = target.CreateDrawingSession())
            {
                ds.Clear();
            }
            return target;
        }
    }

    internal class CanvasPathBuilderHelper
    {
        private readonly Vector2 _from;
        private readonly Vector2 _to;

        public CanvasPathBuilderHelper(Vector2 from, Vector2 to)
        {
            _from = from;
            _to = to;
        }

        public CanvasPathBuilderHelper(Point from, Point to)
        {
            _from = from.ToVector2();
            _to = to.ToVector2();
        }

        public CanvasPathBuilder Build(ICanvasResourceCreator creator)
        {
            var path = new CanvasPathBuilder(creator);
            path.BeginFigure(_from);
            path.AddLine(_to);
            path.EndFigure(CanvasFigureLoop.Open);
            return path;
        }
    }

    internal static class PointExtension
    {
        public static double Distance(this Point a, Point b)
        {
            return Math.Sqrt(Math.Pow(b.X - a.X, 2) + Math.Pow(b.Y - a.Y, 2));
        }

        public static Point[] SplitSegments(Point a, Point b, int segments)
        {
            /*
              x = (N * a.X + M * b.X) / (N + M);
              y = (N * a.Y + M & b.Y) / (N + M);
            */
            var points = new List<Point>();
            for (var i = 0; i < segments; ++i)
            {
                var m = i + 1;
                var n = segments - m;
                var x = (n * a.X + m * b.X) / segments;
                var y = (n * a.Y + m * b.Y) / segments;
                points.Add(new Point(x, y));
            }
            return points.ToArray();
        }
    }

    internal static class PointerPointExtension
    {
        public static double ComputePressure(this PointerPoint pt)
        {
            switch (pt.PointerDevice.PointerDeviceType)
            {
                case PointerDeviceType.Mouse:
                case PointerDeviceType.Pen:
                    return pt.Properties.Pressure;
                case PointerDeviceType.Touch:
                    {
                        var di = DisplayInformation.GetForCurrentView();
                        var scale = di.LogicalDpi / 96.0;
                        var w = pt.Properties.ContactRect.Width / (di.RawDpiX / 25.4) * scale;
                        var h = pt.Properties.ContactRect.Height / (di.RawDpiY / 25.4) * scale;
                        return Math.Min(1.0f, w * h / 300.0f);
                    }
            }
            return 0.5;
        }
    }
}