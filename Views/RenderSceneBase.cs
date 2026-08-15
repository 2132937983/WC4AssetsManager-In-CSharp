using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using SkiaSharp;
using SkiaSharp.Views.WPF;
using WC4MapEditor.Core.Helpers;
using WC4MapEditor.Models;
using SkiaSharp.Views.Desktop;

namespace WC4MapEditor.Views;

public abstract class RenderSceneBase : UserControl, IDisposable
{
    protected readonly MainWindow Window;
    private MainRender _renderEngine = null!;
    private SKElement _skElement = null!;
    private Grid _rootGrid = null!;
    private MapData? _mapData;
    private Camera _camera = null!;

    private Point _lastMousePos;
    private bool _isPanning;
    private bool _disposed;

    protected MainRender RenderEngine => _renderEngine;

    protected abstract string SceneTitle { get; }
    protected abstract MapData? LoadMapData();
    protected abstract void InitializeRenderers();

    protected RenderSceneBase(MainWindow window)
    {
        Window = window;
        SetupUI();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void SetupUI()
    {
        _rootGrid = new Grid();
        _rootGrid.Background = Brushes.Black;

        _skElement = new SKElement
        {
            IgnorePixelScaling = true
        };
        _skElement.PaintSurface += OnPaintSurface;
        _rootGrid.Children.Add(_skElement);

        var titleBar = new Grid
        {
            Height = 40,
            Background = new SolidColorBrush(Color.FromArgb(180, 0, 0, 0)),
            VerticalAlignment = VerticalAlignment.Top
        };

        var titlePanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 0, 0)
        };

        var backButton = new Button
        {
            Content = "← 返回",
            Foreground = Brushes.White,
            FontSize = 13,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
            Padding = new Thickness(8, 4, 8, 4),
            Margin = new Thickness(0, 0, 15, 0)
        };
        backButton.Click += BackButton_Click;
        titlePanel.Children.Add(backButton);

        var titleLabel = new TextBlock
        {
            Text = SceneTitle,
            Foreground = Brushes.White,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Effect = new DropShadowEffect { BlurRadius = 4, ShadowDepth = 1, Opacity = 0.6, Color = Colors.Black }
        };
        titlePanel.Children.Add(titleLabel);

        titleBar.Children.Add(titlePanel);
        _rootGrid.Children.Add(titleBar);

        Content = _rootGrid;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _renderEngine = new MainRender(RenderEngineFactory.Create());

        _mapData = LoadMapData();
        if (_mapData == null)
        {
            Window.ReturnToMainScene();
            return;
        }

        _camera = new Camera
        {
            MapWidth = _mapData.MapWidth,
            MapHeight = _mapData.MapHeight,
            ViewportWidth = (int)_skElement.ActualWidth,
            ViewportHeight = (int)_skElement.ActualHeight
        };
        _camera.CenterOnMap();

        InitializeRenderers();

        _renderEngine.Initialize(IntPtr.Zero, (int)_skElement.ActualWidth, (int)_skElement.ActualHeight);
        _renderEngine.Resize((int)_skElement.ActualWidth, (int)_skElement.ActualHeight);

        _skElement.MouseLeftButtonDown += OnMouseLeftDown;
        _skElement.MouseLeftButtonUp += OnMouseLeftUp;
        _skElement.MouseMove += OnMouseMove;
        _skElement.MouseWheel += OnMouseWheel;
        _skElement.SizeChanged += OnSizeChanged;

        _skElement.InvalidateVisual();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _skElement.PaintSurface -= OnPaintSurface;
        _skElement.MouseLeftButtonDown -= OnMouseLeftDown;
        _skElement.MouseLeftButtonUp -= OnMouseLeftUp;
        _skElement.MouseMove -= OnMouseMove;
        _skElement.MouseWheel -= OnMouseWheel;
        _skElement.SizeChanged -= OnSizeChanged;
    }

    private void OnPaintSurface(object? sender, SKPaintSurfaceEventArgs e)
    {
        if (_mapData == null || _camera == null) return;

        var canvas = e.Surface.Canvas;
        var info = e.Info;

        canvas.Clear(SKColors.Black);

        _camera.ViewportWidth = info.Width;
        _camera.ViewportHeight = info.Height;

        _renderEngine.Render(canvas, _mapData, _camera);
    }

    private void OnMouseLeftDown(object sender, MouseButtonEventArgs e)
    {
        _isPanning = true;
        _lastMousePos = e.GetPosition(_skElement);
        _skElement.CaptureMouse();
    }

    private void OnMouseLeftUp(object sender, MouseButtonEventArgs e)
    {
        _isPanning = false;
        _skElement.ReleaseMouseCapture();
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isPanning || _camera == null) return;

        var pos = e.GetPosition(_skElement);
        double dx = pos.X - _lastMousePos.X;
        double dy = pos.Y - _lastMousePos.Y;
        _lastMousePos = pos;

        _camera.Pan(dx, dy);
        _skElement.InvalidateVisual();
    }

    private void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_camera == null) return;

        var pos = e.GetPosition(_skElement);
        double factor = e.Delta > 0 ? 1.15 : 1.0 / 1.15;
        double newZoom = Math.Max(0.1, Math.Min(5.0, _camera.ZoomLevel * factor));
        _camera.ZoomAt(pos.X, pos.Y, newZoom);
        _skElement.InvalidateVisual();
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        int w = (int)_skElement.ActualWidth;
        int h = (int)_skElement.ActualHeight;
        if (w > 0 && h > 0)
            _renderEngine.Resize(w, h);
        _skElement.InvalidateVisual();
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        Window.ReturnToMainScene();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _renderEngine?.Dispose();
    }
}
