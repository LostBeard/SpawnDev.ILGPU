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
    public partial class BoidsPage : UserControl, IDisposable
    {
        private int _width = 800;
        private int _height = 600;

        // Backend
        private List<(string Key, string Label)> _availableBackends = new();
        private string _selectedBackend = "";
        private volatile bool _pendingBackendSwitch;

        // Boids parameters
        private int _particleCount = 2000;
        private volatile int _speciesCount = 3;
        private float _separation = 1.5f;
        private float _alignment = 1.0f;
        private float _cohesion = 1.0f;
        private float _speed = 2.0f;
        private volatile bool _needsReinit;

        // Camera
        private float _cameraTheta = 0.8f;
        private float _cameraPhi = 0.5f;
        private float _cameraDist = 20.0f;

        // Rendering state
        private volatile bool _disposed;
        private double _lastKernelMs;
        private double _lastSimMs;
        private double _lastRenderMs;
        private double _currentFps;
        private double[] _fpsHistory = new double[120];
        private int _fpsIndex;
        private DateTime _lastFrameTime = DateTime.UtcNow;

        // Mouse drag
        private bool _isDragging;
        private double _lastMouseX, _lastMouseY;

        // ILGPU resources
        private Context? _ilgpuContext;
        private Accelerator? _ilgpuAccelerator;
        private Action<Index1D, ArrayView1D<float, Stride1D.Dense>, ArrayView1D<float, Stride1D.Dense>,
            int, int, float, float, float, float>? _simKernel;
        private Action<Index2D, ArrayView2D<uint, Stride2D.DenseX>, ArrayView1D<float, Stride1D.Dense>,
            int, int, int,
            float, float, float, float, float>? _renderKernel;
        private MemoryBuffer1D<float, Stride1D.Dense>? _boidsBuffer;
        private MemoryBuffer1D<float, Stride1D.Dense>? _boidsBufferOut;
        private MemoryBuffer2D<uint, Stride2D.DenseX>? _outputBuffer;

        // WPF
        private WriteableBitmap? _bitmap;
        private WriteableBitmap? _fpsGraphBitmap;
        private readonly object _paramLock = new();
        private Thread? _renderThread;

        public BoidsPage()
        {
            InitializeComponent();
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            await DetectAvailableBackends();
            _renderThread = new Thread(RenderLoop) { IsBackground = true, Name = "BoidsRenderThread" };
            _renderThread.Start();
        }

        private static int GetBackendPriority(string key) => key switch
        {
            "Cuda" => 0, "OpenCL" => 1, "CPU" => 2, _ => 3
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
            if (backends.Count == 0) backends.Add(("CPU", "🐢 CPU"));
            backends.Sort((a, b) => GetBackendPriority(a.Key).CompareTo(GetBackendPriority(b.Key)));
            _availableBackends = backends;
            BackendCombo.Items.Clear();
            foreach (var b in backends)
                BackendCombo.Items.Add(new ComboBoxItem { Content = b.Label, Tag = b.Key });
            _selectedBackend = backends[0].Key;
            BackendCombo.SelectedIndex = 0;
        }

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
            _simKernel = null;
            _renderKernel = null;
            try { _ilgpuAccelerator?.Synchronize(); } catch { }
            try { _boidsBuffer?.Dispose(); } catch { } _boidsBuffer = null;
            try { _boidsBufferOut?.Dispose(); } catch { } _boidsBufferOut = null;
            try { _outputBuffer?.Dispose(); } catch { } _outputBuffer = null;
            try { _ilgpuAccelerator?.Dispose(); } catch { } _ilgpuAccelerator = null;
            try { _ilgpuContext?.Dispose(); } catch { } _ilgpuContext = null;
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            Thread.Sleep(200);
        }

        private void InitializeBackend(string backendName)
        {
            _ilgpuContext = Context.Create(builder => builder.AllAccelerators());
            Device? device = null;
            foreach (var d in _ilgpuContext)
            {
                if (d.AcceleratorType.ToString() == backendName) { device = d; break; }
            }
            if (device != null)
            {
                if (device is CPUDevice)
                {
                    var htDev = new CPUDevice(2, 2, Environment.ProcessorCount);
                    _ilgpuAccelerator = htDev.CreateCPUAccelerator(_ilgpuContext, CPUAcceleratorMode.Parallel);
                }
                else _ilgpuAccelerator = device.CreateAccelerator(_ilgpuContext);
            }
            else
            {
                var preferred = _ilgpuContext.GetBestDevice();
                if (preferred is CPUDevice)
                {
                    var htDev = new CPUDevice(2, 2, Environment.ProcessorCount);
                    _ilgpuAccelerator = htDev.CreateCPUAccelerator(_ilgpuContext, CPUAcceleratorMode.Parallel);
                }
                else _ilgpuAccelerator = preferred.CreateAccelerator(_ilgpuContext);
            }

            _simKernel = _ilgpuAccelerator.LoadAutoGroupedStreamKernel<
                Index1D, ArrayView1D<float, Stride1D.Dense>, ArrayView1D<float, Stride1D.Dense>,
                int, int, float, float, float, float>(BoidsKernels.Simulate);
            _renderKernel = _ilgpuAccelerator.LoadAutoGroupedStreamKernel<
                Index2D, ArrayView2D<uint, Stride2D.DenseX>, ArrayView1D<float, Stride1D.Dense>,
                int, int, int,
                float, float, float, float, float>(BoidsKernels.Render);

            _outputBuffer = _ilgpuAccelerator.Allocate2DDenseX<uint>(new Index2D(_width, _height));
            InitializeBoids();
        }

        private void InitializeBoids()
        {
            _boidsBuffer?.Dispose();
            _boidsBufferOut?.Dispose();

            int stride = 6;
            var data = new float[_particleCount * stride];
            var rng = new Random(42);
            float spawnRadius = 8.0f;
            for (int i = 0; i < _particleCount; i++)
            {
                int idx = i * stride;
                float theta = (float)(rng.NextDouble() * 2 * Math.PI);
                float phi = (float)(rng.NextDouble() * Math.PI);
                float r = (float)(rng.NextDouble() * spawnRadius);
                data[idx + 0] = r * MathF.Sin(phi) * MathF.Cos(theta);
                data[idx + 1] = r * MathF.Cos(phi);
                data[idx + 2] = r * MathF.Sin(phi) * MathF.Sin(theta);
                data[idx + 3] = (float)(rng.NextDouble() - 0.5) * 2.0f;
                data[idx + 4] = (float)(rng.NextDouble() - 0.5) * 2.0f;
                data[idx + 5] = (float)(rng.NextDouble() - 0.5) * 2.0f;
            }
            _boidsBuffer = _ilgpuAccelerator!.Allocate1D(data);
            _boidsBufferOut = _ilgpuAccelerator!.Allocate1D<float>(_particleCount * stride);
        }

        // ========== Render Loop ==========

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
                            _fpsHistory = new double[120]; _fpsIndex = 0;
                            _lastKernelMs = 0; _lastSimMs = 0; _lastRenderMs = 0; _currentFps = 0;
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

                    if (_needsReinit)
                    {
                        _needsReinit = false;
                        InitializeBoids();
                        continue;
                    }

                    RenderFrame();
                    InvokeUI(UpdatePerformanceUI);
                }
            }
            catch (Exception ex)
            {
                if (!_disposed) InvokeUI(() =>
                {
                    BackendStatus.Text = $"Error: {ex.Message}";
                    BackendStatus.Foreground = new SolidColorBrush(Colors.Red);
                });
            }
            finally { DisposeAcceleratorResources(); }
        }

        private void InvokeUI(Action action)
        {
            if (_disposed) return;
            try { Dispatcher.Invoke(action, DispatcherPriority.Normal); }
            catch (TaskCanceledException) { }
            catch (OperationCanceledException) { }
            catch (InvalidOperationException) { }
        }

        private void RenderFrame()
        {
            if (_simKernel == null || _renderKernel == null || _boidsBuffer == null ||
                _boidsBufferOut == null || _outputBuffer == null || _ilgpuAccelerator == null || _disposed) return;
            try
            {
                var sw = Stopwatch.StartNew();
                var now = DateTime.UtcNow;
                float dt = (float)(now - _lastFrameTime).TotalSeconds;
                if (dt > 0.1f) dt = 0.016f;

                float separation, alignment, cohesion, speed;
                float camTheta, camPhi, camDist;
                int speciesCount, particleCount;
                lock (_paramLock)
                {
                    separation = _separation;
                    alignment = _alignment;
                    cohesion = _cohesion;
                    speed = _speed;
                    camTheta = _cameraTheta;
                    camPhi = _cameraPhi;
                    camDist = _cameraDist;
                    speciesCount = _speciesCount;
                    particleCount = _particleCount;
                }

                // Pass 1: Simulation
                var swSim = Stopwatch.StartNew();
                _simKernel(
                    (Index1D)particleCount,
                    _boidsBuffer.View, _boidsBufferOut.View,
                    particleCount, speciesCount,
                    separation, alignment, cohesion, speed * dt);
                _ilgpuAccelerator.Synchronize();
                swSim.Stop();
                _lastSimMs = swSim.Elapsed.TotalMilliseconds;

                // Swap buffers
                var temp = _boidsBuffer;
                _boidsBuffer = _boidsBufferOut;
                _boidsBufferOut = temp;

                // Pass 2: Render
                var swRender = Stopwatch.StartNew();
                int packedSize = _width * 65536 + _height;
                _renderKernel(
                    _outputBuffer.IntExtent, _outputBuffer.View,
                    _boidsBuffer.View,
                    particleCount, speciesCount, packedSize,
                    camTheta, camPhi, camDist, 0f, 0f);
                _ilgpuAccelerator.Synchronize();
                swRender.Stop();
                _lastKernelMs = swRender.Elapsed.TotalMilliseconds;

                var result = new uint[_width * _height];
                _outputBuffer.View.BaseView.CopyToCPU(result);

                // RGBA → BGRA
                for (int i = 0; i < result.Length; i++)
                {
                    uint c = result[i];
                    uint r = c & 0xFFu;
                    uint b = (c >> 16) & 0xFFu;
                    result[i] = (c & 0xFF00FF00u) | (r << 16) | b;
                }

                InvokeUI(() =>
                {
                    if (_bitmap == null || _bitmap.PixelWidth != _width || _bitmap.PixelHeight != _height)
                    {
                        _bitmap = new WriteableBitmap(_width, _height, 96, 96, PixelFormats.Bgra32, null);
                        SceneImage.Source = _bitmap;
                    }
                    _bitmap.Lock();
                    try
                    {
                        var bytes = MemoryMarshal.AsBytes<uint>(result);
                        Marshal.Copy(bytes.ToArray(), 0, _bitmap.BackBuffer, _width * _height * 4);
                        _bitmap.AddDirtyRect(new Int32Rect(0, 0, _width, _height));
                    }
                    finally { _bitmap.Unlock(); }
                });

                sw.Stop();
                _lastRenderMs = sw.Elapsed.TotalMilliseconds;
                _lastFrameTime = now;
                if (_lastRenderMs > 0)
                {
                    double measuredFps = 1000.0 / _lastRenderMs;
                    _fpsHistory[_fpsIndex % _fpsHistory.Length] = measuredFps;
                    _fpsIndex++;
                    _currentFps = measuredFps;
                }

                InvokeUI(DrawFpsGraph);
            }
            catch (ObjectDisposedException) { }
            catch (Exception) when (_disposed) { }
        }

        private void UpdatePerformanceUI()
        {
            PerfFps.Text = _currentFps.ToString("F1");
            PerfSim.Text = _lastSimMs.ToString("F1");
            PerfKernel.Text = _lastKernelMs.ToString("F1");
            PerfParticles.Text = _particleCount.ToString();
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
            for (int i = 0; i < count; i++) maxFps = Math.Max(maxFps, _fpsHistory[i]);
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
                    for (int px = startX; px < endX && px < w; px++) pixels[py * w + px] = col;
                }
            }
            _fpsGraphBitmap.Lock();
            try
            {
                var bytes = MemoryMarshal.AsBytes<uint>(pixels);
                Marshal.Copy(bytes.ToArray(), 0, _fpsGraphBitmap.BackBuffer, w * h * 4);
                _fpsGraphBitmap.AddDirtyRect(new Int32Rect(0, 0, w, h));
            }
            finally { _fpsGraphBitmap.Unlock(); }
        }

        // ========== Mouse Interaction ==========

        private void OnMouseWheel(object sender, MouseWheelEventArgs e)
        {
            lock (_paramLock)
            {
                float factor = e.Delta > 0 ? 0.9f : 1.1f;
                _cameraDist *= factor;
                if (_cameraDist < 5f) _cameraDist = 5f;
                if (_cameraDist > 60f) _cameraDist = 60f;
            }
            e.Handled = true;
        }

        private void OnMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                lock (_paramLock) { _cameraTheta = 0.8f; _cameraPhi = 0.5f; _cameraDist = 20.0f; }
                return;
            }
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                var pos = e.GetPosition(SceneImage);
                _isDragging = true; _lastMouseX = pos.X; _lastMouseY = pos.Y;
                SceneImage.CaptureMouse();
            }
        }

        private void OnMouseMove(object sender, MouseEventArgs e)
        {
            if (!_isDragging) return;
            var pos = e.GetPosition(SceneImage);
            double dx = pos.X - _lastMouseX;
            double dy = pos.Y - _lastMouseY;
            lock (_paramLock)
            {
                _cameraTheta += (float)(dx * 0.005);
                _cameraPhi -= (float)(dy * 0.005);
                if (_cameraPhi < 0.05f) _cameraPhi = 0.05f;
                if (_cameraPhi > 3.1f) _cameraPhi = 3.1f;
            }
            _lastMouseX = pos.X; _lastMouseY = pos.Y;
        }

        private void OnMouseUp(object sender, MouseButtonEventArgs e)
        {
            _isDragging = false; SceneImage.ReleaseMouseCapture();
        }


        // ========== Control Handlers ==========

        private void OnParticleCountChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!IsLoaded || ParticleCountCombo == null) return;
            if (ParticleCountCombo.SelectedItem is ComboBoxItem item && item.Tag is string tag)
            {
                if (int.TryParse(tag, out int count))
                {
                    lock (_paramLock) { _particleCount = count; }
                    _needsReinit = true;
                }
            }
        }

        private void OnSpeciesChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            lock (_paramLock) { _speciesCount = (int)e.NewValue; }
            if (SpeciesLabel != null) SpeciesLabel.Text = _speciesCount.ToString();
        }

        private void OnParamChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            lock (_paramLock)
            {
                if (SeparationSlider != null) _separation = (float)SeparationSlider.Value;
                if (AlignmentSlider != null) _alignment = (float)AlignmentSlider.Value;
                if (CohesionSlider != null) _cohesion = (float)CohesionSlider.Value;
                if (SpeedSlider != null) _speed = (float)SpeedSlider.Value;
            }
            if (SeparationLabel != null) SeparationLabel.Text = _separation.ToString("F2");
            if (AlignmentLabel != null) AlignmentLabel.Text = _alignment.ToString("F2");
            if (CohesionLabel != null) CohesionLabel.Text = _cohesion.ToString("F2");
            if (SpeedLabel != null) SpeedLabel.Text = _speed.ToString("F1");
        }

        public void Dispose()
        {
            _disposed = true;
            if (_renderThread != null && _renderThread.IsAlive)
            {
                var sw = Stopwatch.StartNew();
                while (_renderThread.IsAlive && sw.ElapsedMilliseconds < 3000)
                {
                    var frame = new DispatcherFrame();
                    Dispatcher.BeginInvoke(DispatcherPriority.Background,
                        new Action(() => frame.Continue = false));
                    Dispatcher.PushFrame(frame);
                }
            }
        }
    }
}
