using eft_dma_shared.Common.Misc;
using SkiaSharp;
using SkiaSharp.Views.WPF;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using Point = System.Windows.Point;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;

namespace eft_dma_radar.UI.SKWidgetControl
{
    public abstract class SKWidget : IDisposable
    {
        #region Fields & Properties
        private static readonly List<SKWidget> _widgets = new();
        private static readonly object _widgetsLock = new();
        private static SKWidget _capturedWidget = null;
        private static readonly Dictionary<SKGLElement, bool> _registeredParents = new();

        private readonly Lock _sync = new();
        private readonly SKGLElement _parent;
        private bool _titleDrag = false;
        private bool _resizeDrag = false;
        private Point _lastRawMousePosition; // Stores the raw mouse position for delta calculation
        private SKPoint _location = new(1, 1);
        private SKSize _size = new(200, 200);
        private SKPath _resizeTriangle;
        private int _zIndex;

        private float TitleBarHeight => 14.5f * ScaleFactor;
        private SKRect TitleBar => new(Rectangle.Left, Rectangle.Top, Rectangle.Right, Rectangle.Top + TitleBarHeight);
        private SKRect MinimizeButton => new(TitleBar.Right - TitleBarHeight, TitleBar.Top, TitleBar.Right, TitleBar.Bottom);

        protected string Title { get; set; }
        protected string RightTitleInfo { get; set; }
        protected bool CanResize { get; }
        protected float ScaleFactor { get; private set; }
        protected SKPath ResizeTriangle => _resizeTriangle;

        public bool Minimized { get; protected set; }
        public SKRect ClientRectangle => new(Rectangle.Left, Rectangle.Top + TitleBarHeight, Rectangle.Right, Rectangle.Bottom);
        public int ZIndex { get => _zIndex; set { _zIndex = value; SortWidgets(); } }
        public SKPoint Location { get => GetCompensatedLocation(); set { lock (_sync) { _location = CompensateLocationForStorage(value); CorrectLocationBounds(); InitializeResizeTriangle(); } } }
        public SKSize Size { get => _size; set { lock (_sync) { _size = value; InitializeResizeTriangle(); } } }
        public SKRect Rectangle => new(Location.X, Location.Y, Location.X + Size.Width, Location.Y + Size.Height + TitleBarHeight);
        #endregion

        #region Constructor
        protected SKWidget(SKGLElement parent, string title, SKPoint location, SKSize clientSize, float scaleFactor, bool canResize = true)
        {
            _parent = parent;
            CanResize = canResize;
            Title = title;
            ScaleFactor = scaleFactor;
            Size = clientSize;
            Location = location;

            EnsureParentEventHandlers(parent);

            lock (_widgetsLock)
            {
                _zIndex = _widgets.Count;
                _widgets.Add(this);
                SortWidgets();
            }
            InitializeResizeTriangle();
        }
        #endregion

        #region Static Mouse Handlers (Hybrid Approach)
        private static void Parent_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var parentGrid = MainWindow.Window?.mainContentGrid;
            if (parentGrid is null) return;

            // Use Canvas coordinates to match widget positioning
            var canvasPos = e.GetPosition(sender as UIElement);
            var canvasSKPos = new SKPoint((float)canvasPos.X, (float)canvasPos.Y);

            SKWidget hitWidget = null;
            lock (_widgetsLock)
            {
                for (int i = _widgets.Count - 1; i >= 0; i--)
                {
                    var widget = _widgets[i];
                    if (widget.HitTest(canvasSKPos) != WidgetClickEvent.None)
                    {
                        hitWidget = widget;
                        break;
                    }
                }
            }

            if (hitWidget != null)
            {
                hitWidget.BringToFront();
                hitWidget.HandleMouseDown(e);
            }
        }

        private static void Parent_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_capturedWidget != null)
            {
                _capturedWidget.HandleMouseUp(e);
                _capturedWidget = null;
                if (sender is UIElement element && element.IsMouseCaptured)
                {
                    element.ReleaseMouseCapture();
                }
                e.Handled = true;
            }
            else // Handle simple clicks (like minimize)
            {
                var parentGrid = MainWindow.Window?.mainContentGrid;
                if (parentGrid is null) return;
                var canvasPos = e.GetPosition(sender as UIElement);
                var canvasSKPos = new SKPoint((float)canvasPos.X, (float)canvasPos.Y);

                lock (_widgetsLock)
                {
                    for (int i = _widgets.Count - 1; i >= 0; i--)
                    {
                        var widget = _widgets[i];
                        var test = widget.HitTest(canvasSKPos);
                        if (test == WidgetClickEvent.ClickedMinimize)
                        {
                            widget.Minimized = !widget.Minimized;
                            widget._parent.InvalidateVisual();
                            e.Handled = true;
                            break;
                        }
                        else if (test == WidgetClickEvent.ClickedClientArea)
                        {
                            if (widget.HandleClientAreaClick(canvasSKPos))
                            {
                                e.Handled = true;
                                break;
                            }
                        }
                    }
                }
            }
        }

        private static void Parent_MouseMove(object sender, MouseEventArgs e)
        {
            if (_capturedWidget != null)
            {
                if (e.LeftButton != MouseButtonState.Pressed)
                {
                    _capturedWidget.HandleMouseUp(null); _capturedWidget = null;
                    if (sender is UIElement element && element.IsMouseCaptured) element.ReleaseMouseCapture();
                    return;
                }
                _capturedWidget.HandleMouseMove(e);
                e.Handled = true;
            }
        }
        #endregion

        #region Instance Mouse Handlers
        private void HandleMouseDown(MouseButtonEventArgs e)
        {
            // Use Canvas coordinates to match widget positioning
            _lastRawMousePosition = e.GetPosition(_parent);
            
            _titleDrag = true;
            _capturedWidget = this;
            _parent.CaptureMouse();
            e.Handled = true;
        }

        private void HandleMouseUp(MouseButtonEventArgs e)
        {
            _titleDrag = false;
            _resizeDrag = false;
            if (e != null) e.Handled = true;
        }

        private void HandleMouseMove(MouseEventArgs e)
        {
            var currentRawPos = e.GetPosition(_parent);

            var deltaX = (float)(currentRawPos.X - _lastRawMousePosition.X);
            var deltaY = (float)(currentRawPos.Y - _lastRawMousePosition.Y);

            if (_resizeDrag && CanResize)
            {
                Size = new SKSize(Size.Width + deltaX, Size.Height + deltaY);
            }
            else if (_titleDrag)
            {
                Location = new SKPoint(Location.X + deltaX, Location.Y + deltaY);
            }

            _lastRawMousePosition = currentRawPos;
            _parent.InvalidateVisual();
        }
        #endregion

        #region Other Methods (No logical changes, just for completeness)
        private void EnsureParentEventHandlers(SKGLElement parent)
        {
            lock (_registeredParents)
            {
                if (_registeredParents.TryGetValue(parent, out bool registered) && registered) return;
                parent.PreviewMouseLeftButtonDown += Parent_MouseLeftButtonDown;
                parent.PreviewMouseLeftButtonUp += Parent_MouseLeftButtonUp;
                parent.PreviewMouseMove += Parent_MouseMove;
                _registeredParents[parent] = true;
            }
        }
        private static void SortWidgets() { lock (_widgetsLock) { _widgets.Sort((a, b) => a.ZIndex.CompareTo(b.ZIndex)); } }
        public void BringToFront() { lock (_widgetsLock) { int highestZ = 0; foreach (var widget in _widgets) { if (widget != this && widget._zIndex > highestZ) highestZ = widget._zIndex; } ZIndex = highestZ + 1; } }
        public void ToggleMinimized() { Minimized = !Minimized; InitializeResizeTriangle(); }
        public WidgetClickEvent HitTest(SKPoint point) { if (!Rectangle.Contains(point)) return WidgetClickEvent.None; if (MinimizeButton.Contains(point)) return WidgetClickEvent.ClickedMinimize; if (TitleBar.Contains(point)) return WidgetClickEvent.ClickedTitleBar; if (!Minimized && CanResize && ResizeTriangle.Contains(point.X, point.Y)) return WidgetClickEvent.ClickedResize; if (!Minimized && ClientRectangle.Contains(point)) return WidgetClickEvent.ClickedClientArea; return WidgetClickEvent.Clicked; }
        public virtual bool HandleClientAreaClick(SKPoint point) => false;
        public virtual void Draw(SKCanvas canvas) { if (!Minimized) canvas.DrawRect(this.Rectangle, WidgetBackgroundPaint); canvas.DrawRect(this.TitleBar, TitleBarPaint); var titleCenterY = this.TitleBar.Top + (this.TitleBar.Height / 2); var titleYOffset = (TitleBarText.FontMetrics.Ascent + TitleBarText.FontMetrics.Descent) / 2; canvas.DrawText(this.Title, new SKPoint(this.TitleBar.Left + 2.5f * ScaleFactor, titleCenterY - titleYOffset), TitleBarText); if (!string.IsNullOrEmpty(this.RightTitleInfo)) { var rightInfoWidth = RightTitleInfoText.MeasureText(this.RightTitleInfo); var rightX = this.TitleBar.Right - rightInfoWidth - 2.5f * ScaleFactor - this.TitleBarHeight; canvas.DrawText(this.RightTitleInfo, new SKPoint(rightX, titleCenterY - titleYOffset), RightTitleInfoText); } canvas.DrawRect(this.MinimizeButton, ButtonBackgroundPaint); DrawMinimizeButton(canvas); if (!Minimized && CanResize) DrawResizeCorner(canvas); DrawDebugInfo(canvas); }
        public virtual void SetScaleFactor(float newScale) { ScaleFactor = newScale; InitializeResizeTriangle(); TitleBarText.TextSize = 12F * newScale; RightTitleInfoText.TextSize = 12F * newScale; SymbolPaint.StrokeWidth = 2f * newScale; }
        private void CorrectLocationBounds() 
        { 
            var parentGrid = MainWindow.Window?.mainContentGrid; 
            if (parentGrid is null) return; 
            
            // Use Grid bounds since that's our coordinate system
            var gridBounds = new Rect(0, 0, parentGrid.ActualWidth, parentGrid.ActualHeight); 
            var rect = Minimized ? TitleBar : Rectangle; 
            var newLoc = _location; 
            
            if (rect.Right > gridBounds.Right) 
                newLoc.X = (float)(gridBounds.Right - rect.Width); 
            if (rect.Bottom > gridBounds.Bottom) 
                newLoc.Y = (float)(gridBounds.Bottom - rect.Height); 
            if (rect.Left < gridBounds.Left) 
                newLoc.X = (float)gridBounds.Left; 
            if (rect.Top < gridBounds.Top) 
                newLoc.Y = (float)gridBounds.Top; 
                
            _location = newLoc; 
        }
        private void DrawMinimizeButton(SKCanvas canvas) { var minHalfLength = MinimizeButton.Width / 4; if (Minimized) { canvas.DrawLine(MinimizeButton.MidX - minHalfLength, MinimizeButton.MidY, MinimizeButton.MidX + minHalfLength, MinimizeButton.MidY, SymbolPaint); canvas.DrawLine(MinimizeButton.MidX, MinimizeButton.MidY - minHalfLength, MinimizeButton.MidX, MinimizeButton.MidY + minHalfLength, SymbolPaint); } else canvas.DrawLine(MinimizeButton.MidX - minHalfLength, MinimizeButton.MidY, MinimizeButton.MidX + minHalfLength, MinimizeButton.MidY, SymbolPaint); }
        private void InitializeResizeTriangle() { var triangleSize = 10.5f * ScaleFactor; var bottomRight = new SKPoint(Rectangle.Right, Rectangle.Bottom); var topOfTriangle = new SKPoint(bottomRight.X, bottomRight.Y - triangleSize); var leftOfTriangle = new SKPoint(bottomRight.X - triangleSize, bottomRight.Y); var path = new SKPath { FillType = SKPathFillType.Winding }; path.MoveTo(bottomRight); path.LineTo(topOfTriangle); path.LineTo(leftOfTriangle); path.Close(); var old = Interlocked.Exchange(ref _resizeTriangle, path); old?.Dispose(); }
        private void DrawResizeCorner(SKCanvas canvas) { if (ResizeTriangle is not null) canvas.DrawPath(ResizeTriangle, TitleBarPaint); }
        
        private SKPoint GetScreenLocation()
        {
            // Transform canvas coordinates to screen coordinates accounting for rotation
            var parentGrid = MainWindow.Window?.mainContentGrid;
            if (parentGrid is null) return _location;
            
            var canvasWidth = (float)parentGrid.ActualWidth;
            var canvasHeight = (float)parentGrid.ActualHeight;
            var centerX = canvasWidth / 2f;
            var centerY = canvasHeight / 2f;
            
            if (MainWindow.RotationDegrees == 0) 
                return _location;
                
            // Apply rotation transformation to get screen position
            var radians = MainWindow.RotationDegrees * (Math.PI / 180.0);
            var cos = (float)Math.Cos(radians);
            var sin = (float)Math.Sin(radians);
            
            var translatedX = _location.X - centerX;
            var translatedY = _location.Y - centerY;
            
            var rotatedX = translatedX * cos - translatedY * sin;
            var rotatedY = translatedX * sin + translatedY * cos;
            
            return new SKPoint(rotatedX + centerX, rotatedY + centerY);
        }
        
        private SKPoint TransformToCanvasCoordinates(SKPoint screenPoint)
        {
            // Transform screen coordinates to canvas coordinates accounting for rotation
            var parentGrid = MainWindow.Window?.mainContentGrid;
            if (parentGrid is null) return screenPoint;
            
            var canvasWidth = (float)parentGrid.ActualWidth;
            var canvasHeight = (float)parentGrid.ActualHeight;
            var centerX = canvasWidth / 2f;
            var centerY = canvasHeight / 2f;
            
            if (MainWindow.RotationDegrees == 0) 
                return screenPoint;
                
            // Apply inverse rotation transformation
            var radians = -MainWindow.RotationDegrees * (Math.PI / 180.0);
            var cos = (float)Math.Cos(radians);
            var sin = (float)Math.Sin(radians);
            
            var translatedX = screenPoint.X - centerX;
            var translatedY = screenPoint.Y - centerY;
            
            var rotatedX = translatedX * cos - translatedY * sin;
            var rotatedY = translatedX * sin + translatedY * cos;
            
            return new SKPoint(rotatedX + centerX, rotatedY + centerY);
        }
        
        private void DrawDebugInfo(SKCanvas canvas)
        {
            // Only show debug info for one widget to avoid clutter
            if (Title != "Debug Info") return;
            
            var debugText = $"Rot: {MainWindow.RotationDegrees}° | Stored: ({_location.X:F0},{_location.Y:F0}) | Compensated: ({GetCompensatedLocation().X:F0},{GetCompensatedLocation().Y:F0})";
            
            var debugPos = new SKPoint(Rectangle.Left, Rectangle.Bottom + 15 * ScaleFactor);
            using var debugPaint = new SKPaint
            {
                Color = SKColors.Yellow,
                TextSize = 10 * ScaleFactor,
                IsAntialias = true
            };
            
            canvas.DrawText(debugText, debugPos, debugPaint);
        }
        
        private static SKPoint TransformMousePosition(Point rawPosition)
        {
            // If there's no rotation, we don't need to do anything.
            if (MainWindow.RotationDegrees == 0)
            {
                return new SKPoint((float)rawPosition.X, (float)rawPosition.Y);
            }

            var parentGrid = MainWindow.Window?.mainContentGrid;
            if (parentGrid is null) return new SKPoint((float)rawPosition.X, (float)rawPosition.Y);

            // Get the center of the canvas, which is our rotation pivot.
            var centerX = (float)parentGrid.ActualWidth / 2;
            var centerY = (float)parentGrid.ActualHeight / 2;

            // Create a matrix that performs the INVERSE rotation around the center point.
            // We use the NEGATIVE angle to reverse the canvas rotation.
            var inverseRotationMatrix = SKMatrix.CreateRotationDegrees(-MainWindow.RotationDegrees, centerX, centerY);

            // Apply the inverse matrix to the raw mouse point to get its logical position.
            return inverseRotationMatrix.MapPoint(new SKPoint((float)rawPosition.X, (float)rawPosition.Y));
        }
        
        private SKPoint GetTransformedLocation()
        {
            // Transform stored canvas coordinates to maintain visual position across rotations
            if (MainWindow.RotationDegrees == 0)
                return _location;

            var parentGrid = MainWindow.Window?.mainContentGrid;
            if (parentGrid is null) return _location;

            var gridWidth = (float)parentGrid.ActualWidth;
            var gridHeight = (float)parentGrid.ActualHeight;
            var gridCenterX = gridWidth / 2f;
            var gridCenterY = gridHeight / 2f;

            // Transform the stored location to maintain visual position
            var radians = MainWindow.RotationDegrees * (Math.PI / 180.0);
            var cos = (float)Math.Cos(radians);
            var sin = (float)Math.Sin(radians);

            var translatedX = _location.X - gridCenterX;
            var translatedY = _location.Y - gridCenterY;

            var rotatedX = translatedX * cos - translatedY * sin;
            var rotatedY = translatedX * sin + translatedY * cos;

            return new SKPoint(rotatedX + gridCenterX, rotatedY + gridCenterY);
        }

        private SKPoint TransformLocationForStorage(SKPoint visualLocation)
        {
            // Transform visual coordinates back to storage coordinates
            if (MainWindow.RotationDegrees == 0)
                return visualLocation;

            var parentGrid = MainWindow.Window?.mainContentGrid;
            if (parentGrid is null) return visualLocation;

            var gridWidth = (float)parentGrid.ActualWidth;
            var gridHeight = (float)parentGrid.ActualHeight;
            var gridCenterX = gridWidth / 2f;
            var gridCenterY = gridHeight / 2f;

            // Apply inverse transformation
            var radians = -MainWindow.RotationDegrees * (Math.PI / 180.0);
            var cos = (float)Math.Cos(radians);
            var sin = (float)Math.Sin(radians);

            var translatedX = visualLocation.X - gridCenterX;
            var translatedY = visualLocation.Y - gridCenterY;

            var rotatedX = translatedX * cos - translatedY * sin;
            var rotatedY = translatedX * sin + translatedY * cos;

            return new SKPoint(rotatedX + gridCenterX, rotatedY + gridCenterY);
        }
        
        private SKPoint GetSwappedLocation()
        {
            // Swap coordinates to match canvas dimension flipping
            if (MainWindow.RotationDegrees == 90 || MainWindow.RotationDegrees == 270)
            {
                return new SKPoint(_location.Y, _location.X);
            }
            return _location;
        }
        
        private SKPoint GetCompensatedLocation()
        {
            // SIMPLE SOLUTION: No coordinate compensation at all!
            // Let the counter-rotation matrix in the drawing code handle positioning
            return _location;
        }
        
        private SKPoint CompensateLocationForStorage(SKPoint visualLocation)
        {
            // SIMPLE SOLUTION: No coordinate compensation at all!
            // Store coordinates as-is
            return visualLocation;
        }
        
        public virtual void Dispose() { /* Base implementation */ }
        #endregion

        #region Paints
        private static readonly SKPaint WidgetBackgroundPaint = new() { Color = SKColors.Black.WithAlpha(0xBE), StrokeWidth = 1, Style = SKPaintStyle.Fill, }; private static readonly SKPaint TitleBarPaint = new() { Color = SKColors.Gray, StrokeWidth = 0.5f, Style = SKPaintStyle.Fill, }; private static readonly SKPaint ButtonBackgroundPaint = new() { Color = SKColors.LightGray, StrokeWidth = 0.1f, Style = SKPaintStyle.Fill, }; private static readonly SKPaint SymbolPaint = new() { Color = SKColors.Black, StrokeWidth = 2f, Style = SKPaintStyle.Stroke, IsAntialias = true }; private static readonly SKPaint TitleBarText = new() { SubpixelText = true, Color = SKColors.White, IsStroke = false, TextSize = 12f, TextAlign = SKTextAlign.Left, IsAntialias = true, FilterQuality = SKFilterQuality.High }; private static readonly SKPaint RightTitleInfoText = new() { SubpixelText = true, Color = SKColors.White, IsStroke = false, TextSize = 12f, TextAlign = SKTextAlign.Left, IsAntialias = true, FilterQuality = SKFilterQuality.High };
        #endregion
    }
}