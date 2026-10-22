using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using global::ILGPU;
using global::ILGPU.Runtime;
using global::ILGPU.Runtime.CPU;
using SpawnDev.ILGPU.Demo.Shared.Benchmarks;
using Grid = System.Windows.Controls.Grid;

namespace SpawnDev.ILGPU.WpfDemo.Pages
{
    public partial class BenchmarksPage : UserControl, IDisposable
    {
        // ── State ──
        private bool _isRunning;
        private readonly List<BenchmarkResult> _results = new();

        // Ranking display item
        public class RankingItem
        {
            public string RankLabel { get; set; } = "";
            public string Name { get; set; } = "";
            public Color DotColor { get; set; }
            public string WinsLabel { get; set; } = "";
            public string AvgLabel { get; set; } = "";
        }

        // ── Desktop backend profiles (all use standard workload sizes) ──
        private readonly BackendProfile[] _backends =
        [
            new("Cuda", BenchmarkDefs.StandardProfile.DefaultN, BenchmarkDefs.StandardProfile.MandelbrotSize, BenchmarkDefs.StandardProfile.PrimesN),
            new("OpenCL", BenchmarkDefs.StandardProfile.DefaultN, BenchmarkDefs.StandardProfile.MandelbrotSize, BenchmarkDefs.StandardProfile.PrimesN),
            new("Velocity", BenchmarkDefs.StandardProfile.DefaultN, BenchmarkDefs.StandardProfile.MandelbrotSize, BenchmarkDefs.StandardProfile.PrimesN),
            new("CPU", BenchmarkDefs.StandardProfile.DefaultN, BenchmarkDefs.StandardProfile.MandelbrotSize, BenchmarkDefs.StandardProfile.PrimesN),
        ];

        // ── Backend colors ──
        private static Color GetBackendColor(string name) => name switch
        {
            "Cuda" => Color.FromRgb(0x22, 0xC5, 0x5E),
            "OpenCL" => Color.FromRgb(0xF9, 0x73, 0x16),
            "Velocity" => Color.FromRgb(0x8B, 0x5C, 0xF6),
            "CPU" => Color.FromRgb(0x64, 0x74, 0x8B),
            _ => Color.FromRgb(0x94, 0xA3, 0xB8),
        };

        private static string GetBackendColorHex(string name) => name switch
        {
            "Cuda" => "#22C55E",
            "OpenCL" => "#F97316",
            "Velocity" => "#8B5CF6",
            "CPU" => "#64748B",
            _ => "#94A3B8",
        };

        public BenchmarksPage()
        {
            InitializeComponent();
        }

        private void OnLoaded(object sender, RoutedEventArgs e) { }

        // ====================================================================
        //  BACKEND FACTORY (Desktop backends)
        // ====================================================================

        private async Task<(global::ILGPU.Context ctx, Accelerator acc)?> CreateBackend(string name)
        {
            try
            {
                AcceleratorType targetType = name switch
                {
                    "Cuda" => AcceleratorType.Cuda,
                    "OpenCL" => AcceleratorType.OpenCL,
                    "Velocity" => AcceleratorType.Velocity,
                    "CPU" => AcceleratorType.CPU,
                    _ => throw new ArgumentException($"Unknown backend: {name}")
                };

                // AllAccelerators() registers Velocity (the SIMD CPU backend) instead of the plain CPU accelerator
                // since 5.4.0; the plain one is the debugging backend and is added explicitly for the "CPU" row.
                var ctx = global::ILGPU.Context.Create(builder => builder.AllAccelerators().DefaultCPU());
                Device? device = null;
                foreach (var d in ctx)
                {
                    if (d.AcceleratorType == targetType)
                    {
                        device = d;
                        break;
                    }
                }

                if (device == null) { ctx.Dispose(); return null; }

                // For CPU, use a high-throughput config: small groups (2×2 = 4 threads)
                // but all CPU cores as multiprocessors, with parallel execution mode
                if (device is CPUDevice)
                {
                    var htDevice = new CPUDevice(2, 2, Environment.ProcessorCount);
                    var acc = htDevice.CreateCPUAccelerator(ctx, CPUAcceleratorMode.Parallel);
                    return (ctx, acc);
                }
                else
                {
                    var acc = device.CreateAccelerator(ctx);
                    return (ctx, acc);
                }
            }
            catch
            {
                return null;
            }
        }

        // ====================================================================
        //  ORCHESTRATOR
        // ====================================================================

        private async void OnRunAll(object sender, RoutedEventArgs e)
        {
            if (_isRunning) return;
            _isRunning = true;
            RunButton.IsEnabled = false;
            _results.Clear();
            ResultsPanel.Children.Clear();
            RankingCard.Visibility = Visibility.Collapsed;
            ProgressPanel.Visibility = Visibility.Visible;
            StatusText.Text = "Starting benchmarks...";

            int totalSteps = BenchmarkDefs.All.Length * _backends.Length;
            int step = 0;

            foreach (var bench in BenchmarkDefs.All)
            {
                var benchResults = new List<BenchmarkResult>();

                foreach (var backend in _backends)
                {
                    step++;
                    double pct = 100.0 * step / totalSteps;
                    ProgressFill.Width = ProgressPanel.ActualWidth * (pct / 100.0);
                    ProgressLabel.Text = $"{bench.Name} → {backend.Name} ({step}/{totalSteps})";
                    StatusText.Text = ProgressLabel.Text;
                    await Task.Delay(10); // yield to UI

                    var result = await RunSingleBenchmark(bench, backend);
                    _results.Add(result);
                    benchResults.Add(result);
                }

                // Add result card for this benchmark
                AddBenchmarkCard(bench, benchResults);
                await Task.Delay(10);
            }

            // Show ranking
            UpdateRanking();

            ProgressPanel.Visibility = Visibility.Collapsed;
            StatusText.Text = $"Done — {_results.Count(r => !r.Failed)} of {_results.Count} benchmarks succeeded";
            _isRunning = false;
            RunButton.IsEnabled = true;
        }

        // ====================================================================
        //  SINGLE BENCHMARK RUNNER — delegates to shared BenchmarkRunner
        // ====================================================================

        private async Task<BenchmarkResult> RunSingleBenchmark(BenchmarkDef bench, BackendProfile backend)
        {
            (global::ILGPU.Context ctx, Accelerator acc)? pair = null;
            try
            {
                pair = await CreateBackend(backend.Name);
                if (pair == null)
                    return new BenchmarkResult { BackendName = backend.Name, BenchmarkName = bench.Name, Failed = true, Error = "Backend unavailable" };

                var (ctx, acc) = pair.Value;
                return await BenchmarkRunner.RunAsync(acc, bench.Id, backend);
            }
            catch (Exception ex)
            {
                return new BenchmarkResult
                {
                    BackendName = backend.Name,
                    BenchmarkName = bench.Name,
                    Failed = true,
                    Error = ex.Message,
                };
            }
            finally
            {
                if (pair.HasValue)
                {
                    pair.Value.acc.Dispose();
                    pair.Value.ctx.Dispose();
                }
            }
        }

        // ====================================================================
        //  UI BUILDING — Result Cards
        // ====================================================================

        private void AddBenchmarkCard(BenchmarkDef bench, List<BenchmarkResult> benchResults)
        {
            var card = new Border
            {
                Background = FindResource("BgCardBrush") as Brush,
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(20),
                Margin = new Thickness(0, 0, 0, 16),
                BorderBrush = FindResource("BorderSubtleBrush") as Brush,
                BorderThickness = new Thickness(1),
            };

            var stack = new StackPanel();

            // Header
            var header = new TextBlock
            {
                Text = $"{bench.Icon} {bench.Name}",
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                Foreground = FindResource("TextPrimaryBrush") as Brush,
                Margin = new Thickness(0, 0, 0, 4),
            };
            stack.Children.Add(header);

            var desc = new TextBlock
            {
                Text = bench.Description,
                FontSize = 12,
                Foreground = FindResource("TextMutedBrush") as Brush,
                Margin = new Thickness(0, 0, 0, 12),
            };
            stack.Children.Add(desc);

            // Results table
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });

            // Header row
            AddTableHeader(grid, 0, "Backend");
            AddTableHeader(grid, 1, "Warmup (ms)");
            AddTableHeader(grid, 2, "Median (ms)");
            AddTableHeader(grid, 3, "Throughput");
            AddTableHeader(grid, 4, "Elements");
            AddTableHeader(grid, 5, "Status");

            var ordered = benchResults.OrderBy(r => r.MedianMs ?? double.MaxValue).ToList();
            double? bestMs = ordered.FirstOrDefault(r => r.MedianMs.HasValue)?.MedianMs;

            int row = 1;
            foreach (var r in ordered)
            {
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                bool isBest = r.MedianMs.HasValue && r.MedianMs == bestMs;

                // Row background for best
                if (isBest)
                {
                    var rowBg = new Border
                    {
                        Background = new SolidColorBrush(Color.FromArgb(0x10, 0x22, 0xC5, 0x5E)),
                    };
                    Grid.SetRow(rowBg, row);
                    Grid.SetColumnSpan(rowBg, 6);
                    grid.Children.Add(rowBg);
                }

                // Backend name with dot
                var namePanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                namePanel.Children.Add(new Ellipse
                {
                    Width = 10,
                    Height = 10,
                    Fill = new SolidColorBrush(GetBackendColor(r.BackendName)),
                    Margin = new Thickness(0, 0, 6, 0),
                });
                namePanel.Children.Add(new TextBlock
                {
                    Text = r.BackendName,
                    Foreground = FindResource("TextPrimaryBrush") as Brush,
                    FontSize = 13,
                    Opacity = r.Failed ? 0.5 : 1,
                });
                AddTableCell(grid, row, 0, namePanel);

                AddTableText(grid, row, 1, r.WarmupMs > 0 ? r.WarmupMs.ToString("F0") : "—", r.Failed);
                AddTableText(grid, row, 2, r.MedianMs.HasValue ? r.MedianMs.Value.ToString("F2") : "—", r.Failed);
                AddTableText(grid, row, 3, r.Throughput ?? "—", r.Failed);
                AddTableText(grid, row, 4, r.ElementCount > 0 ? r.ElementCount.ToString("N0") : "—", r.Failed);

                // Status
                var statusText = new TextBlock
                {
                    Text = r.Failed ? $"✗ {TruncateError(r.Error)}" : "✓",
                    FontSize = 12,
                    Foreground = new SolidColorBrush(r.Failed ? Color.FromRgb(0xFC, 0xA5, 0xA5) : Color.FromRgb(0x4A, 0xDE, 0x80)),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(8, 6, 8, 6),
                    ToolTip = r.Error,
                };
                Grid.SetRow(statusText, row);
                Grid.SetColumn(statusText, 5);
                grid.Children.Add(statusText);

                row++;
            }

            // Add the header row definition at index 0
            grid.RowDefinitions.Insert(0, new RowDefinition { Height = GridLength.Auto });

            stack.Children.Add(grid);

            // Bar chart
            var chart = CreateBarChart(benchResults);
            if (chart != null)
            {
                chart.Margin = new Thickness(0, 16, 0, 0);
                stack.Children.Add(chart);
            }

            card.Child = stack;
            ResultsPanel.Children.Add(card);
        }

        private void AddTableHeader(Grid grid, int col, string text)
        {
            var tb = new TextBlock
            {
                Text = text,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = FindResource("TextSecondaryBrush") as Brush,
                Margin = new Thickness(8, 6, 8, 6),
            };
            Grid.SetRow(tb, 0);
            Grid.SetColumn(tb, col);
            grid.Children.Add(tb);
        }

        private void AddTableText(Grid grid, int row, int col, string text, bool faded)
        {
            var tb = new TextBlock
            {
                Text = text,
                FontSize = 13,
                Foreground = FindResource("TextPrimaryBrush") as Brush,
                Opacity = faded ? 0.5 : 1,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 6, 8, 6),
            };
            Grid.SetRow(tb, row);
            Grid.SetColumn(tb, col);
            grid.Children.Add(tb);
        }

        private void AddTableCell(Grid grid, int row, int col, UIElement element)
        {
            Grid.SetRow(element, row);
            Grid.SetColumn(element, col);
            element.SetValue(FrameworkElement.MarginProperty, new Thickness(8, 6, 8, 6));
            grid.Children.Add(element);
        }

        // ====================================================================
        //  BAR CHART (WPF Canvas-based)
        // ====================================================================

        private FrameworkElement? CreateBarChart(List<BenchmarkResult> benchResults)
        {
            var valid = benchResults.Where(r => r.MedianMs.HasValue).OrderBy(r =>
                Array.FindIndex(_backends, b => b.Name == r.BackendName)).ToList();
            if (valid.Count == 0) return null;

            var canvas = new Canvas { Height = 200, ClipToBounds = true };
            canvas.Loaded += (s, e) =>
            {
                canvas.Children.Clear();
                double w = canvas.ActualWidth;
                if (w < 100) w = 700;
                double h = 200;

                double padL = 70, padR = 20, padT = 20, padB = 36;
                double chartW = w - padL - padR;
                double chartH = h - padT - padB;

                var values = valid.Select(r => r.ThroughputElemPerSec).ToArray();
                double maxVal = values.Max() * 1.15;
                double barW = Math.Min(60, chartW / valid.Count * 0.6);
                double gap = (chartW - barW * valid.Count) / (valid.Count + 1);

                // Grid lines
                for (int g = 0; g <= 4; g++)
                {
                    double gy = padT + chartH - (chartH * g / 4.0);
                    var line = new Line
                    {
                        X1 = padL, Y1 = gy, X2 = w - padR, Y2 = gy,
                        Stroke = new SolidColorBrush(Color.FromArgb(0x10, 0xFF, 0xFF, 0xFF)),
                        StrokeThickness = 1,
                    };
                    canvas.Children.Add(line);

                    var label = new TextBlock
                    {
                        Text = BenchmarkRunner.FormatShortThroughput(maxVal * g / 4.0),
                        FontSize = 10,
                        Foreground = FindResource("TextMutedBrush") as Brush,
                    };
                    Canvas.SetLeft(label, padL - 50);
                    Canvas.SetTop(label, gy - 7);
                    canvas.Children.Add(label);
                }

                // Bars
                for (int i = 0; i < valid.Count; i++)
                {
                    double x = padL + gap + i * (barW + gap);
                    double barH = (values[i] / maxVal) * chartH;
                    double y = padT + chartH - barH;

                    var color = GetBackendColor(valid[i].BackendName);
                    var rect = new Rectangle
                    {
                        Width = barW,
                        Height = barH,
                        RadiusX = 4,
                        RadiusY = 4,
                        Fill = new LinearGradientBrush(
                            color,
                            Color.FromArgb(0x33, color.R, color.G, color.B),
                            90),
                    };
                    Canvas.SetLeft(rect, x);
                    Canvas.SetTop(rect, y);
                    canvas.Children.Add(rect);

                    // Value label above bar
                    var valLabel = new TextBlock
                    {
                        Text = BenchmarkRunner.FormatShortThroughput(values[i]),
                        FontSize = 11,
                        FontWeight = FontWeights.Bold,
                        Foreground = FindResource("TextPrimaryBrush") as Brush,
                    };
                    Canvas.SetLeft(valLabel, x + barW / 2 - 15);
                    Canvas.SetTop(valLabel, y - 18);
                    canvas.Children.Add(valLabel);

                    // Axis label
                    var axisLabel = new TextBlock
                    {
                        Text = valid[i].BackendName,
                        FontSize = 12,
                        Foreground = FindResource("TextSecondaryBrush") as Brush,
                    };
                    Canvas.SetLeft(axisLabel, x + barW / 2 - 15);
                    Canvas.SetTop(axisLabel, padT + chartH + 8);
                    canvas.Children.Add(axisLabel);
                }
            };

            return canvas;
        }

        // ====================================================================
        //  RANKING
        // ====================================================================

        private void UpdateRanking()
        {
            var ranking = _results
                .Where(r => r.MedianMs.HasValue)
                .GroupBy(r => r.BackendName)
                .Select(g => new
                {
                    Name = g.Key,
                    AvgMs = g.Average(r => r.MedianMs!.Value),
                    Wins = g.Count(r =>
                    {
                        var benchGroup = _results.Where(x => x.BenchmarkName == r.BenchmarkName && x.MedianMs.HasValue);
                        return benchGroup.Any() && r.MedianMs == benchGroup.Min(x => x.MedianMs);
                    })
                })
                .OrderByDescending(x => x.Wins).ThenBy(x => x.AvgMs)
                .ToList();

            var items = new List<RankingItem>();
            int rank = 1;
            foreach (var r in ranking)
            {
                items.Add(new RankingItem
                {
                    RankLabel = $"#{rank++}",
                    Name = r.Name,
                    DotColor = GetBackendColor(r.Name),
                    WinsLabel = $"{r.Wins} 🥇",
                    AvgLabel = $"avg {r.AvgMs:F1} ms",
                });
            }

            RankingList.ItemsSource = items;
            RankingCard.Visibility = Visibility.Visible;
        }

        // ====================================================================
        //  HELPERS
        // ====================================================================

        private static string TruncateError(string? err) =>
            err != null && err.Length > 40 ? err[..40] + "…" : err ?? "";

        public void Dispose() { }
    }
}
