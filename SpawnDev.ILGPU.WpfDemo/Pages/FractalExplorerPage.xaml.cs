using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using global::ILGPU;
using global::ILGPU.Runtime;
using global::ILGPU.Runtime.CPU;
using SpawnDev.ILGPU;
using SpawnDev.ILGPU.Demo.Shared.Kernels;

namespace SpawnDev.ILGPU.WpfDemo.Pages
{
    public partial class FractalExplorerPage : UserControl, IDisposable
    {
        // Canvas size
        private int _width = 900;
        private int _height = 700;

        // Backend selection
        private List<(string Key, string Label)> _availableBackends = new();
        private string _selectedBackend = "";
        private volatile bool _pendingBackendSwitch;

        // Fractal parameters
        private volatile int _fractalType = 0;
        private volatile int _colorScheme = 0;
        private int _maxIterations = 500;
        private double _centerX = -0.5;
        private double _centerY = 0.0;
        private double _zoom = 1.0;

        // Julia & Phoenix parameters
        private double _juliaReal = -0.7;
        private double _juliaImag = 0.27015;
        private double _phoenixP = 0.5667;
        private double _phoenixQ = -0.5;

        // Rendering state
        private volatile bool _disposed;
        private double _lastRenderMs;
        private double _lastKernelMs;
        private double _lastReadbackMs;
        private double _currentFps;

        // FPS history
        private double[] _fpsHistory = new double[120];
        private int _fpsIndex;
        private DateTime _lastFrameTime = DateTime.UtcNow;

        // Mouse drag
        private bool _isDragging;
        private double _lastMouseX;
        private double _lastMouseY;

        // ILGPU resources (owned by render thread)
        private Context? _ilgpuContext;
        private Accelerator? _ilgpuAccelerator;
        private Action<Index2D, ArrayView2D<uint, Stride2D.DenseX>,
            int, int, int,
            double, double, double, double, double>? _kernel;
        private MemoryBuffer2D<uint, Stride2D.DenseX>? _outputBuffer;

        // WPF rendering (UI thread only)
        private WriteableBitmap? _bitmap;
        private WriteableBitmap? _fpsGraphBitmap;

        // Synchronization
        private readonly object _paramLock = new();
        private Thread? _renderThread;

        public FractalExplorerPage()
        {
            InitializeComponent();
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            await DetectAvailableBackends();
            // Start the render loop on a dedicated background thread
            _renderThread = new Thread(RenderLoop)
            {
                IsBackground = true,
                Name = "FractalRenderThread"
            };
            _renderThread.Start();
        }

        // ========== Backend Detection ==========

        private static int GetBackendPriority(string key) => key switch
        {
            "Cuda" => 0,
            "OpenCL" => 1,
            "CPU" => 2,
            _ => 3
        };

        private async Task DetectAvailableBackends()
        {
            var backends = new List<(string Key, string Label)>();

            using var probeContext = await Context.CreateAsync(builder => builder.AllAcceleratorsAsync());
            foreach (var device in probeContext)
            {
                string key = device.AcceleratorType.ToString();
                if (backends.Any(b => b.Key == key)) continue;

                string label = device.AcceleratorType switch
                {
                    AcceleratorType.Cuda => "🚀 CUDA",
                    AcceleratorType.OpenCL => "⚡ OpenCL",
                    AcceleratorType.CPU => "🐢 CPU",
                    _ => $"❓ {device.Name}"
                };
                backends.Add((key, $"{label} — {device.Name}"));
            }

            if (backends.Count == 0)
                backends.Add(("CPU", "🐢 CPU (Multi-thread)"));

            backends.Sort((a, b) => GetBackendPriority(a.Key).CompareTo(GetBackendPriority(b.Key)));

            _availableBackends = backends;

            BackendCombo.Items.Clear();
            foreach (var b in backends)
                BackendCombo.Items.Add(new ComboBoxItem { Content = b.Label, Tag = b.Key });

            _selectedBackend = backends[0].Key;
            BackendCombo.SelectedIndex = 0;
        }

        // ========== Backend Switching (UI thread) ==========

        private void OnBackendChanged(object sender, SelectionChangedEventArgs e)
        {
            if (BackendCombo.SelectedItem is ComboBoxItem item && item.Tag is string key)
            {
                if (key == _selectedBackend && _ilgpuAccelerator != null) return;
                _selectedBackend = key;
                _pendingBackendSwitch = true;
            }
        }

        private void DisposeAcceleratorResources()
        {
            _kernel = null;
            try { _ilgpuAccelerator?.Synchronize(); } catch { }
            try { _outputBuffer?.Dispose(); } catch { }
            _outputBuffer = null;
            try { _ilgpuAccelerator?.Dispose(); } catch { }
            _ilgpuAccelerator = null;
            try { _ilgpuContext?.Dispose(); } catch { }
            _ilgpuContext = null;

            // Force cleanup of native CUDA/OpenCL handles before re-init
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            Thread.Sleep(200);
        }

        // ========== Initialization (Render thread) ==========

        private void InitializeBackend(string backendName)
        {
            _ilgpuContext = Context.Create(builder => builder.AllAccelerators());

            Device? device = null;
            foreach (var d in _ilgpuContext)
            {
                if (d.AcceleratorType.ToString() == backendName)
                {
                    device = d;
                    break;
                }
            }

            if (device != null)
            {
                if (device is CPUDevice)
                {
                    int cores = Environment.ProcessorCount;
                    var highThroughputDevice = new CPUDevice(
                        numThreadsPerWarp: 2,
                        numWarpsPerMultiprocessor: 2,
                        numMultiprocessors: cores);
                    _ilgpuAccelerator = highThroughputDevice.CreateCPUAccelerator(
                        _ilgpuContext, CPUAcceleratorMode.Parallel);
                }
                else
                {
                    _ilgpuAccelerator = device.CreateAccelerator(_ilgpuContext);
                }
            }
            else
            {
                var preferred = _ilgpuContext.GetBestDevice();
                if (preferred is CPUDevice)
                {
                    int cores = Environment.ProcessorCount;
                    var htDev = new CPUDevice(2, 2, cores);
                    _ilgpuAccelerator = htDev.CreateCPUAccelerator(
                        _ilgpuContext, CPUAcceleratorMode.Parallel);
                }
                else
                {
                    _ilgpuAccelerator = preferred.CreateAccelerator(_ilgpuContext);
                }
            }

            _kernel = _ilgpuAccelerator.LoadAutoGroupedStreamKernel<
                Index2D, ArrayView2D<uint, Stride2D.DenseX>,
                int, int, int,
                double, double, double, double, double>(FractalKernels.Render);
            _outputBuffer = _ilgpuAccelerator.Allocate2DDenseX<uint>(new Index2D(_width, _height));
        }

        // ========== Render Loop (Dedicated background thread) ==========
        // All ILGPU work happens here. UI updates are dispatched to the WPF
        // dispatcher. All Dispatcher calls are wrapped in try/catch to
        // gracefully handle window close.

        private void RenderLoop()
        {
            try
            {
                InitializeBackend(_selectedBackend);
                InvokeUI(() =>
                {
                    BackendStatus.Text = $"✓ {_ilgpuAccelerator!.Name}";
                    BackendStatus.Foreground = FindResource("SuccessGreenBrush") as Brush;
                    PerfBackend.Text = _ilgpuAccelerator.Name;
                });

                while (!_disposed)
                {
                    if (_pendingBackendSwitch)
                    {
                        _pendingBackendSwitch = false;
                        var newBackend = _selectedBackend;

                        InvokeUI(() =>
                        {
                            BackendStatus.Text = $"Initializing {newBackend}...";
                            BackendStatus.Foreground = FindResource("TextMutedBrush") as Brush;
                        });

                        try
                        {
                            DisposeAcceleratorResources();
                            InitializeBackend(newBackend);

                            // Reset FPS
                            _fpsHistory = new double[120];
                            _fpsIndex = 0;
                            _lastKernelMs = 0;
                            _lastReadbackMs = 0;
                            _lastRenderMs = 0;
                            _currentFps = 0;

                            InvokeUI(() =>
                            {
                                BackendStatus.Text = $"✓ {_ilgpuAccelerator!.Name}";
                                BackendStatus.Foreground = FindResource("SuccessGreenBrush") as Brush;
                                PerfBackend.Text = _ilgpuAccelerator.Name;
                            });
                        }
                        catch (Exception ex)
                        {
                            InvokeUI(() =>
                            {
                                BackendStatus.Text = $"Error: {ex.Message}";
                                BackendStatus.Foreground = new SolidColorBrush(Colors.Red);
                            });
                        }
                        continue;
                    }

                    RenderFrame();
                    InvokeUI(UpdatePerformanceUI);
                }
            }
            catch (Exception ex)
            {
                if (!_disposed)
                {
                    InvokeUI(() =>
                    {
                        BackendStatus.Text = $"Error: {ex.Message}";
                        BackendStatus.Foreground = new SolidColorBrush(Colors.Red);
                    });
                }
            }
            finally
            {
                // Clean up GPU resources when loop exits
                DisposeAcceleratorResources();
            }
        }

        /// <summary>
        /// Safely invoke an action on the WPF dispatcher.
        /// Silently ignores errors if the window/dispatcher is shutting down.
        /// </summary>
        private void InvokeUI(Action action)
        {
            if (_disposed) return;
            try
            {
                Dispatcher.Invoke(action, DispatcherPriority.Normal);
            }
            catch (TaskCanceledException) { }
            catch (OperationCanceledException) { }
            catch (InvalidOperationException) { } // Dispatcher shut down
        }

        // ========== Rendering (Render thread) ==========

        private void RenderFrame()
        {
            if (_kernel == null || _outputBuffer == null || _ilgpuAccelerator == null || _disposed) return;

            try
            {
                var sw = Stopwatch.StartNew();

                int fractalType, colorScheme, maxIter;
                double centerX, centerY, zoom, juliaReal, juliaImag, phoenixP, phoenixQ;
                lock (_paramLock)
                {
                    fractalType = _fractalType;
                    colorScheme = _colorScheme;
                    maxIter = _maxIterations;
                    centerX = _centerX;
                    centerY = _centerY;
                    zoom = _zoom;
                    juliaReal = _juliaReal;
                    juliaImag = _juliaImag;
                    phoenixP = _phoenixP;
                    phoenixQ = _phoenixQ;
                }

                InvokeUI(() =>
                {
                    JuliaPanel.Visibility = fractalType == 1 ? Visibility.Visible : Visibility.Collapsed;
                    PhoenixPanel.Visibility = fractalType == 4 ? Visibility.Visible : Visibility.Collapsed;
                });

                int packedSize = _width * 65536 + _height;
                int packedConfig = fractalType * 256 + colorScheme;
                double extra1 = fractalType == 1 ? juliaReal : phoenixP;
                double extra2 = fractalType == 1 ? juliaImag : phoenixQ;

                _kernel(
                    _outputBuffer.IntExtent, _outputBuffer.View,
                    packedSize, maxIter, packedConfig,
                    centerX, centerY, zoom, extra1, extra2);

                _ilgpuAccelerator.Synchronize();

                var kernelTime = sw.Elapsed.TotalMilliseconds;
                _lastKernelMs = kernelTime;

                var swReadback = Stopwatch.StartNew();
                var result = new uint[_width * _height];
                _outputBuffer.View.BaseView.CopyToCPU(result);
                swReadback.Stop();
                _lastReadbackMs = swReadback.Elapsed.TotalMilliseconds;

                // Convert RGBA → BGRA
                for (int i = 0; i < result.Length; i++)
                {
                    uint c = result[i];
                    uint r = c & 0xFFu;
                    uint b = (c >> 16) & 0xFFu;
                    result[i] = (c & 0xFF00FF00u) | (r << 16) | b;
                }

                // Blit to WriteableBitmap on UI thread
                InvokeUI(() =>
                {
                    if (_bitmap == null || _bitmap.PixelWidth != _width || _bitmap.PixelHeight != _height)
                    {
                        _bitmap = new WriteableBitmap(_width, _height, 96, 96, PixelFormats.Bgra32, null);
                        FractalImage.Source = _bitmap;
                    }

                    _bitmap.Lock();
                    try
                    {
                        var bytes = MemoryMarshal.AsBytes<uint>(result);
                        Marshal.Copy(bytes.ToArray(), 0, _bitmap.BackBuffer, _width * _height * 4);
                        _bitmap.AddDirtyRect(new Int32Rect(0, 0, _width, _height));
                    }
                    finally
                    {
                        _bitmap.Unlock();
                    }
                });

                sw.Stop();
                _lastRenderMs = sw.Elapsed.TotalMilliseconds;

                var now = DateTime.UtcNow;
                var dt = (now - _lastFrameTime).TotalSeconds;
                _lastFrameTime = now;
                if (dt > 0)
                {
                    _fpsHistory[_fpsIndex % _fpsHistory.Length] = 1.0 / dt;
                    _fpsIndex++;
                    _currentFps = 1.0 / dt;
                }

                InvokeUI(DrawFpsGraph);
            }
            catch (ObjectDisposedException) { }
            catch (Exception) when (_disposed) { }
        }

        private void UpdatePerformanceUI()
        {
            PerfFps.Text = _currentFps.ToString("F1");
            PerfTotal.Text = _lastRenderMs.ToString("F1");
            PerfKernel.Text = _lastKernelMs.ToString("F1");
            PerfReadback.Text = _lastReadbackMs.ToString("F1");
            PerfZoom.Text = _zoom.ToString("G6");
            PerfCenter.Text = $"{_centerX:G10}, {_centerY:G10}";
        }

        private void DrawFpsGraph()
        {
            const int w = 200, h = 50;
            if (_fpsGraphBitmap == null)
            {
                _fpsGraphBitmap = new WriteableBitmap(w, h, 96, 96, PixelFormats.Bgra32, null);
                FpsGraphImage.Source = _fpsGraphBitmap;
            }

            var pixels = new uint[w * h];

            double maxFps = 10;
            int count = Math.Min(_fpsIndex, _fpsHistory.Length);
            for (int i = 0; i < count; i++)
                maxFps = Math.Max(maxFps, _fpsHistory[i]);

            double barWidth = (double)w / _fpsHistory.Length;
            for (int i = 0; i < count; i++)
            {
                int idx = (_fpsIndex - count + i + _fpsHistory.Length) % _fpsHistory.Length;
                double fps = _fpsHistory[idx];
                int barHeight = (int)((fps / maxFps) * (h - 12));
                double ratio = fps / maxFps;
                byte r = (byte)(255 * (1 - ratio));
                byte g = (byte)(255 * ratio);
                uint col = 0xFF000000u | ((uint)r << 16) | ((uint)g << 8) | 50u;

                int startX = (int)(i * barWidth);
                int endX = Math.Min((int)((i + 1) * barWidth), w);
                for (int py = h - barHeight - 8; py < h - 8; py++)
                {
                    if (py < 0 || py >= h) continue;
                    for (int px = startX; px < endX && px < w; px++)
                        pixels[py * w + px] = col;
                }
            }

            _fpsGraphBitmap.Lock();
            try
            {
                var bytes = MemoryMarshal.AsBytes<uint>(pixels);
                Marshal.Copy(bytes.ToArray(), 0, _fpsGraphBitmap.BackBuffer, w * h * 4);
                _fpsGraphBitmap.AddDirtyRect(new Int32Rect(0, 0, w, h));
            }
            finally
            {
                _fpsGraphBitmap.Unlock();
            }
        }

        // ========== Mouse Interaction (UI Thread) ==========

        private void OnMouseWheel(object sender, MouseWheelEventArgs e)
        {
            var pos = e.GetPosition(FractalImage);
            double imgWidth = FractalImage.ActualWidth;
            double imgHeight = FractalImage.ActualHeight;
            if (imgWidth <= 0 || imgHeight <= 0) return;

            lock (_paramLock)
            {
                double mouseX = pos.X / imgWidth * _width;
                double mouseY = pos.Y / imgHeight * _height;

                double scale = 4.0 / _zoom;
                double mouseRealBefore = _centerX + (mouseX - _width / 2.0) * scale / _width;
                double mouseImagBefore = _centerY + (mouseY - _height / 2.0) * scale / _height;

                double zoomFactor = e.Delta > 0 ? 1.2 : 1.0 / 1.2;
                _zoom *= zoomFactor;
                _zoom = Math.Max(0.5, Math.Min(_zoom, 1e15));

                double newScale = 4.0 / _zoom;
                _centerX = mouseRealBefore - (mouseX - _width / 2.0) * newScale / _width;
                _centerY = mouseImagBefore - (mouseY - _height / 2.0) * newScale / _height;
            }

            e.Handled = true;
        }

        private void OnMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                var pos = e.GetPosition(FractalImage);
                _isDragging = true;
                _lastMouseX = pos.X;
                _lastMouseY = pos.Y;
                FractalImage.CaptureMouse();
            }
        }

        private void OnMouseMove(object sender, MouseEventArgs e)
        {
            if (!_isDragging) return;

            var pos = e.GetPosition(FractalImage);
            double imgWidth = FractalImage.ActualWidth;
            double imgHeight = FractalImage.ActualHeight;
            if (imgWidth <= 0 || imgHeight <= 0) return;

            double dx = pos.X - _lastMouseX;
            double dy = pos.Y - _lastMouseY;
            if (Math.Abs(dx) < 0.5 && Math.Abs(dy) < 0.5) return;

            lock (_paramLock)
            {
                double pixelToFractalX = _width / imgWidth;
                double pixelToFractalY = _height / imgHeight;
                double fractalDx = dx * pixelToFractalX;
                double fractalDy = dy * pixelToFractalY;

                double scale = 4.0 / _zoom;
                _centerX -= fractalDx * scale / _width;
                _centerY -= fractalDy * scale / _height;
            }

            _lastMouseX = pos.X;
            _lastMouseY = pos.Y;
        }

        private void OnMouseUp(object sender, MouseButtonEventArgs e)
        {
            _isDragging = false;
            FractalImage.ReleaseMouseCapture();
        }

        private void OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                lock (_paramLock)
                {
                    _zoom = 1.0;
                    switch (_fractalType)
                    {
                        case 0: _centerX = -0.5; _centerY = 0.0; break;
                        case 1: _centerX = 0.0; _centerY = 0.0; break;
                        case 2: _centerX = -0.4; _centerY = -0.5; break;
                        case 3: _centerX = -0.5; _centerY = 0.0; break;
                        case 4: _centerX = 0.0; _centerY = 0.0; break;
                    }
                }
            }
        }

        // ========== Control Event Handlers (UI Thread) ==========

        private void OnFractalTypeChanged(object sender, SelectionChangedEventArgs e)
        {
            lock (_paramLock) { _fractalType = FractalTypeCombo.SelectedIndex; }
        }

        private void OnColorSchemeChanged(object sender, SelectionChangedEventArgs e)
        {
            lock (_paramLock) { _colorScheme = ColorSchemeCombo.SelectedIndex; }
        }

        private void OnIterationsChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            lock (_paramLock) { _maxIterations = (int)e.NewValue; }
            if (IterationsLabel != null)
                IterationsLabel.Text = _maxIterations.ToString();
        }

        private void OnJuliaParamChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            lock (_paramLock)
            {
                if (JuliaRealSlider != null) _juliaReal = JuliaRealSlider.Value;
                if (JuliaImagSlider != null) _juliaImag = JuliaImagSlider.Value;
            }
            if (JuliaRealLabel != null) JuliaRealLabel.Text = _juliaReal.ToString("F4");
            if (JuliaImagLabel != null) JuliaImagLabel.Text = _juliaImag.ToString("F4");
        }

        private void OnPhoenixParamChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            lock (_paramLock)
            {
                if (PhoenixPSlider != null) _phoenixP = PhoenixPSlider.Value;
                if (PhoenixQSlider != null) _phoenixQ = PhoenixQSlider.Value;
            }
            if (PhoenixPLabel != null) PhoenixPLabel.Text = _phoenixP.ToString("F4");
            if (PhoenixQLabel != null) PhoenixQLabel.Text = _phoenixQ.ToString("F4");
        }

        // ========== Disposal ==========

        public void Dispose()
        {
            _disposed = true;
            // Don't use _renderThread.Join() — that deadlocks because the render
            // thread may be blocked inside Dispatcher.Invoke, waiting for this thread.
            // Instead, pump the dispatcher to process those Invoke calls while
            // waiting for the render thread to exit naturally.
            if (_renderThread != null && _renderThread.IsAlive)
            {
                var sw = Stopwatch.StartNew();
                while (_renderThread.IsAlive && sw.ElapsedMilliseconds < 3000)
                {
                    // Process one batch of pending dispatcher work
                    var frame = new DispatcherFrame();
                    Dispatcher.BeginInvoke(DispatcherPriority.Background,
                        new Action(() => frame.Continue = false));
                    Dispatcher.PushFrame(frame);
                }
            }
            // GPU resources are disposed in the RenderLoop's finally block
        }
    }
}
