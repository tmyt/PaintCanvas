using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using Windows.UI;
using Windows.UI.Input;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Windows.Devices.Input;
using Windows.Graphics.Display;
using Windows.System;
using Windows.System.Threading;
using Windows.UI.Core;
using Windows.UI.Xaml.Controls.Primitives;
using Microsoft.Graphics.Canvas.Effects;
using Painting.Internal;

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

        private GestureRecognizer _recognizer;

        private ScrollViewer _scrollViewer;
        private ScrollBar _horizontalBar;
        private ScrollBar _verticalBar;
        private CanvasSwapChainPanel _canvas;
        private CanvasBitmap _background;
        private CanvasRenderTarget _buffer;
        private CanvasRenderTarget _tmpBuffer;

        #region Backgroud thread input processor

        private CoreIndependentInputSource _inputSource;

        #endregion

        #region Drawing Parameters

        private Color __strokeColor;
        private double __strokeThickness;
        private PenMode __penMode;
        private InkLayer __activeLayer;
        private double __logicalDpi;
        private double __dpiX;
        private double __dpiY;
        private double __zoomFactor;
        private bool __canscrollable;

        #endregion

        public ObservableCollection<InkLayer> Layers
            => _layers;

        public event EventHandler Win2dInitialized;

        public static readonly DependencyProperty StrokeColorProperty = DependencyProperty.Register(
            "StrokeColor", typeof(Color), typeof(PaintCanvas), new PropertyMetadata(default(Color), (d, e) => ((PaintCanvas)d).__strokeColor = (Color)e.NewValue));

        public Color StrokeColor
        {
            get { return (Color)GetValue(StrokeColorProperty); }
            set { SetValue(StrokeColorProperty, value); }
        }

        public static readonly DependencyProperty StrokeThicknessProperty = DependencyProperty.Register(
            "StrokeThickness", typeof(double), typeof(PaintCanvas), new PropertyMetadata(default(double), (d, e) => ((PaintCanvas)d).__strokeThickness = (double)e.NewValue));

        public double StrokeThickness
        {
            get { return (double)GetValue(StrokeThicknessProperty); }
            set { SetValue(StrokeThicknessProperty, value); }
        }

        public static readonly DependencyProperty PenModeProperty = DependencyProperty.Register(
            "PenMode", typeof(PenMode), typeof(PaintCanvas), new PropertyMetadata(default(PenMode), (d, e) => ((PaintCanvas)d).__penMode = (PenMode)e.NewValue));

        public PenMode PenMode
        {
            get { return (PenMode)GetValue(PenModeProperty); }
            set { SetValue(PenModeProperty, value); }
        }

        public static readonly DependencyProperty ActiveLayerProperty = DependencyProperty.Register(
            "ActiveLayer", typeof(InkLayer), typeof(PaintCanvas), new PropertyMetadata(default(InkLayer), (d, e) => ((PaintCanvas)d).__activeLayer = (InkLayer)e.NewValue));

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

        public bool CanUndo => _undoBuffer.Count > 0;
        public bool CanRedo => _redoBuffer.Count > 0;

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
            __canscrollable = (bool)e.NewValue;
        }

        public PaintCanvas()
        {
            DefaultStyleKey = typeof(PaintCanvas);
            _layers = new ObservableCollection<InkLayer>();
            _undoBuffer = new Stack<KeyValuePair<InkLayer, CanvasRenderTarget>>();
            _redoBuffer = new Stack<KeyValuePair<InkLayer, CanvasRenderTarget>>();
            _inputs = new Dictionary<uint, Point>();
            _previousPressures = new Dictionary<uint, double>();
            __zoomFactor = 1;
            // handle reorder
            _layers.CollectionChanged += LayersCollectionChanged;
            // handle unload
            Unloaded += ThisUnloaded;
        }

        protected override void OnApplyTemplate()
        {
            _canvas = (CanvasSwapChainPanel)GetTemplateChild("PART_canvas");
            _canvas.Loaded += CanvasCreateResources;
            _canvas.SizeChanged += CanvasSizeChanged;
            _scrollViewer = (ScrollViewer)GetTemplateChild("PART_ScrollViewer");
            _horizontalBar = _scrollViewer.GetVisualChildren<ScrollBar>()
                .FirstOrDefault(x => x.Orientation == Orientation.Horizontal);
            _verticalBar = _scrollViewer.GetVisualChildren<ScrollBar>()
                .FirstOrDefault(x => x.Orientation == Orientation.Vertical);
        }

        private void _recognizer_ManipulationUpdated(GestureRecognizer sender, ManipulationUpdatedEventArgs args)
        {
            var delta = args.Delta;
            _scrollViewer.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                _scrollViewer.ScrollToVerticalOffset(_scrollViewer.VerticalOffset - delta.Translation.Y * __zoomFactor);
                _scrollViewer.ScrollToHorizontalOffset(_scrollViewer.HorizontalOffset - delta.Translation.X * __zoomFactor);
                _scrollViewer.ZoomToFactor(_scrollViewer.ZoomFactor * delta.Scale);
                __zoomFactor = _scrollViewer.ZoomFactor;
            }).AsTask().ConfigureAwait(false).GetAwaiter();
        }


        private void CanvasWheelChanged(object sender, PointerEventArgs args)
        {
            var delta = args.CurrentPoint.Properties.MouseWheelDelta;
            var pt = args.CurrentPoint.Position;
            var isHorizontal = args.CurrentPoint.Properties.IsHorizontalMouseWheel;
            var control = (args.KeyModifiers & VirtualKeyModifiers.Control) != 0;
            if (control && isHorizontal) return;
            _scrollViewer.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                if (control)
                {
                    var factor = (delta / 1200.0) + 1;
                    var dw = ((pt.X * factor) - pt.X);
                    var dh = ((pt.Y * factor) - pt.Y);
                    _scrollViewer.ChangeView(_scrollViewer.HorizontalOffset + dw,
                        _scrollViewer.VerticalOffset + dh,
                        (float) (_scrollViewer.ZoomFactor * factor));
                    __zoomFactor = _scrollViewer.ZoomFactor;
                    return;
                }
                if (isHorizontal)
                    _scrollViewer.ScrollToHorizontalOffset(_scrollViewer.HorizontalOffset + delta);
                else
                    _scrollViewer.ScrollToVerticalOffset(_scrollViewer.VerticalOffset - delta);
            }).AsTask().ConfigureAwait(false).GetAwaiter();
        }

        private void UpdateIndicatorMode(PointerDeviceType type)
        {
            _scrollViewer.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                switch (type)
                {
                    default:
                    case PointerDeviceType.Mouse:
                    case PointerDeviceType.Pen:
                        VisualStateManager.GoToState(_scrollViewer, "MouseIndicator", true);
                        break;
                    case PointerDeviceType.Touch:
                        VisualStateManager.GoToState(_scrollViewer, "TouchIndicator", true);
                        break;
                }
            }).AsTask().ConfigureAwait(false);
        }

        private void LayersCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            //_canvas?.Invalidate();
            Invalidate();
        }

        private void ThisUnloaded(object sender, RoutedEventArgs e)
        {
            DisplayInformation.DisplayContentsInvalidated -= DisplayInformation_DisplayContentsInvalidated;
            _canvas?.RemoveFromVisualTree();
            _canvas = null;
            // dispose buffers
            _buffer.Dispose();
            _tmpBuffer.Dispose();
            _background.Dispose();
            foreach (var buffer in _undoBuffer)
            {
                buffer.Value.Dispose();
            }
            foreach (var buffer in _redoBuffer)
            {
                buffer.Value.Dispose();
            }
            foreach (var layer in _layers)
            {
                layer.Image.Dispose();
            }
            _undoBuffer.Clear();
            _redoBuffer.Clear();
            _layers.Clear();
            _buffer = null;
            _tmpBuffer = null;
            _background = null;
        }

        private async void CanvasCreateResources(object o, RoutedEventArgs routedEventArgs)
        {
            // initialize current displayinfo
            UpdateDisplayInfo();
            DisplayInformation.DisplayContentsInvalidated += DisplayInformation_DisplayContentsInvalidated;
            // create swapchain
            var swapChain = new CanvasSwapChain(CanvasDevice.GetSharedDevice(), (float)CanvasWidth, (float)CanvasHeight, 96);
            _canvas.SwapChain = swapChain;
            // create back buffer
            _buffer = CanvasRenderTargetExtension.CreateEmpty(_canvas, new Size(CanvasWidth, CanvasHeight));
            _tmpBuffer = CanvasRenderTargetExtension.CreateEmpty(_canvas, new Size(CanvasWidth, CanvasHeight));
            // create default layer
            AddLayer();
            _background = await CanvasBitmap.LoadAsync(_canvas.SwapChain, new Uri("ms-appx:///PaintCanvas/Assets/canvas.png"));
            //_canvas.Invalidate();
            Invalidate();
            Win2dInitialized?.Invoke(this, EventArgs.Empty);
            // initialize background input thread

            ThreadPool.RunAsync(_ =>
            {
                // touch processor
                _inputSource = _canvas.CreateCoreIndependentInputSource(
                    CoreInputDeviceTypes.Touch | CoreInputDeviceTypes.Mouse | CoreInputDeviceTypes.Pen
                );
                _inputSource.PointerPressed += CanvasPointerPressed;
                _inputSource.PointerMoved += CanvasPointerMoved;
                _inputSource.PointerReleased += CanvasPointerReleased;
                _inputSource.PointerCaptureLost += CanvasPointerReleased;
                _inputSource.PointerWheelChanged += CanvasWheelChanged;
                // setup gesture recognizer
                _recognizer = new GestureRecognizer { AutoProcessInertia = true };
                _recognizer.GestureSettings =
                    GestureSettings.ManipulationTranslateInertia | GestureSettings.ManipulationTranslateRailsX |
                    GestureSettings.ManipulationTranslateRailsY | GestureSettings.ManipulationTranslateX |
                    GestureSettings.ManipulationTranslateY |
                    GestureSettings.ManipulationScale | GestureSettings.ManipulationScaleInertia;
                _recognizer.ManipulationUpdated += _recognizer_ManipulationUpdated;
                _inputSource.Dispatcher.ProcessEvents(CoreProcessEventsOption.ProcessUntilQuit);
            }, WorkItemPriority.High, WorkItemOptions.TimeSliced);
        }

        private void DisplayInformation_DisplayContentsInvalidated(DisplayInformation sender, object args)
        {
            UpdateDisplayInfo();
        }

        private void UpdateDisplayInfo()
        {
            var info = DisplayInformation.GetForCurrentView();
            __logicalDpi = info.LogicalDpi;
            __dpiX = info.RawDpiX;
            __dpiY = info.RawDpiY;
        }

        private void CanvasSizeChanged(object sender, SizeChangedEventArgs sizeChangedEventArgs)
        {
            _canvas?.SwapChain?.ResizeBuffers((float)CanvasWidth, (float)CanvasHeight, 96);
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
            if (_buffer == null) return;
            _buffer.Dispose();
            _tmpBuffer.Dispose();
            _buffer = CanvasRenderTargetExtension.CreateEmpty(_canvas, new Size(CanvasWidth, CanvasHeight));
            _tmpBuffer = CanvasRenderTargetExtension.CreateEmpty(_canvas, new Size(CanvasWidth, CanvasHeight));
            Invalidate();
        }

        private void Invalidate()
        {
            if (_canvas?.SwapChain == null) return;
            using (var ds = _canvas.SwapChain.CreateDrawingSession(Colors.Transparent))
            {
                DrawFrame(ds);
            }
            _canvas.SwapChain.Present(0);
        }

        private void DrawFrame(CanvasDrawingSession ds)
        {
            //ds.Clear();
            if (_background != null)
            {
                var tile = new TileEffect();
                tile.Source = _background;
                tile.SourceRectangle = new Rect(0, 0, 400, 400);
                ds.DrawImage(tile);
            }
            using (var blendDs = _buffer.CreateDrawingSession())
            {
                blendDs.Clear();
            }
            foreach (var layer in _layers.Reverse().Where(l => l.IsVisible))
            {
                using (var tmpDs = _tmpBuffer.CreateDrawingSession())
                {
                    tmpDs.Clear(Colors.Transparent);
                    switch (layer.BlendMode)
                    {
                        case BlendMode.Normal:
                            tmpDs.DrawImage(_buffer);
                            tmpDs.DrawImage(layer.Image, (float)layer.Opacity / 100);
                            break;
                        case BlendMode.Addition:
                            tmpDs.DrawImage(_buffer);
                            tmpDs.Blend = CanvasBlend.Add;
                            tmpDs.DrawImage(layer.Image, (float)layer.Opacity / 100);
                            break;
                        default:
                            using (var alpha = layer.Image.Clone())
                            using (var blend = new BlendEffect())
                            {
                                using (var alphaDs = alpha.CreateDrawingSession())
                                {
                                    alphaDs.DrawImage(layer.Image, (float)layer.Opacity / 100);
                                }
                                blend.Background = _buffer;
                                blend.Foreground = alpha;
                                blend.Mode = layer.BlendMode.ToBlendEffectMode();
                                tmpDs.DrawImage(blend);
                            }
                            break;
                    }
                }
                using (var blendDs = _buffer.CreateDrawingSession())
                {
                    blendDs.Clear();
                    blendDs.DrawImage(_tmpBuffer);
                }
                ds.DrawImage(_buffer);
            }
        }

        private void CanvasPointerPressed(object sender, PointerEventArgs e)
        {
            if (__canscrollable && e.CurrentPoint.PointerDevice.PointerDeviceType == PointerDeviceType.Touch)
            {
                _recognizer.ProcessDownEvent(e.CurrentPoint);
                return;
            }
            var pt = e.CurrentPoint;
            _inputs[pt.PointerId] = pt.Position;
            _previousPressures[pt.PointerId] = pt.ComputePressure(__logicalDpi, __dpiX, __dpiY, __zoomFactor);
            //_canvas.CapturePointer(e.Pointer);
            // save undo buffer
            var activeLayer = __activeLayer;
            if (activeLayer == null || activeLayer.IsLocked) return;
            // process spoit on pressed.
            if (!IsPenModeEraser(pt.Properties) && __penMode == PenMode.Spoit)
            {
                if (IsInRange(pt.Position))
                {
                    __strokeColor = activeLayer.Image.GetPixelColor((int)pt.Position.X, (int)pt.Position.Y).RemoveAlpha();
                    Dispatcher.RunIdleAsync(_ => StrokeColor = __strokeColor);
                }
                // spoit does not create undo buffer
                return;
            }
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

        private void CanvasPointerMoved(object sender, PointerEventArgs e)
        {
            // update visualsatte
            UpdateIndicatorMode(e.CurrentPoint.PointerDevice.PointerDeviceType);
            // transport to recognizer
            if (__canscrollable && e.CurrentPoint.PointerDevice.PointerDeviceType == PointerDeviceType.Touch)
            {
                _recognizer.ProcessMoveEvents(new[] { e.CurrentPoint });
                return;
            }
            var activeLayer = __activeLayer?.Image;
            if (activeLayer == null || (__activeLayer?.IsLocked ?? false)) return;
            if (!_inputs.ContainsKey(e.CurrentPoint.PointerId)) return;
            if (!e.CurrentPoint.IsInContact) return;
            var pt = e.CurrentPoint;
            var from = _inputs[pt.PointerId].ToVector2();
            var to = pt.Position.ToVector2();
            if (IsPenModeEraser(pt.Properties))
            {
                activeLayer.EraseLine(from, to, (float)__strokeThickness, StrokeStyle);
            }
            else if (__penMode == PenMode.Spoit)
            {
                if (IsInRange(pt.Position))
                {
                    __strokeColor = activeLayer.GetPixelColor((int)pt.Position.X, (int)pt.Position.Y).RemoveAlpha();
                    Dispatcher.RunIdleAsync(_ => StrokeColor = __strokeColor);
                }
            }
            else
            {
                var L = _inputs[pt.PointerId].Distance(pt.Position);
                var k = L / 8; // 8px
                var step = (pt.ComputePressure(__logicalDpi, __dpiX, __dpiY, __zoomFactor) - _previousPressures[pt.PointerId]) / 4;
                var segments = PointExtension.SplitSegments(_inputs[pt.PointerId], pt.Position, Math.Max(4, (int)k));
                var pressure = _previousPressures[pt.PointerId];
                var prevPt = _inputs[pt.PointerId];
                for (var i = 0; i < segments.Length; ++i)
                {
                    var p = (pressure + step * i);
                    var width = (float)__strokeThickness * p;
                    var opacity = p < 0.5 ? p * 2 : 1.0f;
                    activeLayer.DrawLine(prevPt.ToVector2(), segments[i].ToVector2(), __strokeColor, (float)width,
                        StrokeStyle, (float)opacity);
                    prevPt = segments[i];
                }
            }
            _inputs[pt.PointerId] = pt.Position;
            _previousPressures[pt.PointerId] = pt.ComputePressure(__logicalDpi, __dpiX, __dpiY, __zoomFactor);
            //_canvas.Invalidate();
            Invalidate();
        }

        private void CanvasPointerReleased(object sender, PointerEventArgs e)
        {
            if (__canscrollable && e.CurrentPoint.PointerDevice.PointerDeviceType == PointerDeviceType.Touch)
            {
                _recognizer.ProcessUpEvent(e.CurrentPoint);
                return;
            }
            if (!_inputs.ContainsKey(e.CurrentPoint.PointerId)) return;
            _inputs.Remove(e.CurrentPoint.PointerId);
            _previousPressures.Remove(e.CurrentPoint.PointerId);
            //_canvas.ReleasePointerCapture(e.Pointer);
        }

        private bool IsInRange(Point pt)
        {
            return pt.X >= 0 && pt.Y >= 0 && pt.X < CanvasWidth && pt.Y < CanvasHeight;
        }

        private bool IsPenModeEraser(PointerPointProperties prop)
        {
            return __penMode == PenMode.Eraser | prop.IsEraser | prop.IsRightButtonPressed;
        }

        private void Layer_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            //_canvas.Invalidate();
            Invalidate();
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
                IsVisible = true,
                Opacity = 100
            };
            layer.PropertyChanged += Layer_PropertyChanged;
            using (var ds = layer.Image.CreateDrawingSession())
            {
                ds.Clear();
            }
            _layers.Insert(0, layer);
            ActiveLayer = layer;
            return layer;
        }

        public void RemoveLayer(InkLayer layer)
        {
            if (layer == null) return;
            var n = _layers.IndexOf(layer);
            _layers.Remove(layer);
            layer.Image.Dispose();
            ActiveLayer = n < _layers.Count && n >= 0 ? _layers[n] : _layers.LastOrDefault();
            //_canvas.Invalidate();
            Invalidate();
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
            //_canvas.Invalidate();
            Invalidate();
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
            //_canvas.Invalidate();
            Invalidate();
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
                IsVisible = true,
                Opacity = 100
            };
            layer.PropertyChanged += Layer_PropertyChanged;
            using (var bitmap = await CanvasBitmap.LoadAsync(_canvas.SwapChain, stream))
            using (var ds = layer.Image.CreateDrawingSession())
            {
                ds.Clear();
                ds.DrawImage(bitmap);
            }
            _layers.Add(layer);
            //_canvas.Invalidate();
            Invalidate();
            return true;
        }
    }

    internal static class CanvasDrawingSessionExtension
    {
        public static void Clear(this CanvasDrawingSession session)
        {
            session.Clear(Colors.Transparent);
        }

        public static void DrawImage(this CanvasDrawingSession session, ICanvasImage image, float opacity)
        {
            var rect = image.GetBounds(session);
            session.DrawImage(image, rect, rect, opacity);
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

        public static Color GetPixelColor(this CanvasRenderTarget target, int left, int top)
        {
            var scale = 96.0 / target.Dpi;
            return target.GetPixelColors((int)(left / scale), (int)(top / scale), 1, 1)[0];
        }

        public static CanvasRenderTarget CreateEmpty(ICanvasResourceCreatorWithDpi device, Size size)
        {
            return CreateEmpty(device, size, device.Dpi);
        }

        public static CanvasRenderTarget CreateEmpty(CanvasSwapChainPanel device, Size size)
        {
            return CreateEmpty(device.SwapChain, size);
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

        public static CanvasRenderTarget CreateEmpty(CanvasSwapChainPanel device, Size size, float dpi)
        {
            return CreateEmpty(device.SwapChain, size, dpi);
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

    internal static class ColorExtension
    {
        public static Color RemoveAlpha(this Color c)
        {
            return Color.FromArgb(255, c.R, c.G, c.B);
        }
    }

    internal static class PointerPointExtension
    {
        public static double ComputePressure(this PointerPoint pt, double logicalDpi, double dpiX, double dpiY, double factor)
        {
            switch (pt.PointerDevice.PointerDeviceType)
            {
                case PointerDeviceType.Mouse:
                case PointerDeviceType.Pen:
                    return pt.Properties.Pressure;
                case PointerDeviceType.Touch:
                    {
                        //var di = DisplayInformation.GetForCurrentView();
                        var scale = (logicalDpi / 96.0) * factor;
                        var w = pt.Properties.ContactRect.Width / (dpiX / 25.4) * scale;
                        var h = pt.Properties.ContactRect.Height / (dpiY / 25.4) * scale;
                        return Math.Min(1.0f, w * h / 300.0f);
                    }
            }
            return 0.5;
        }
    }
}