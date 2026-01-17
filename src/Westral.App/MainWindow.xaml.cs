using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Media;
using Westral.App.WtApi;

namespace Westral.App;

public enum ConnectionState
{
    Connected = 1,
    Loading,
    Error,
}

[INotifyPropertyChanged]
public sealed partial class MainWindowViewModel
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StateColor))]
    public partial ConnectionState State { get; set; }

    public Brush StateColor => State switch
    {
        ConnectionState.Connected => Brushes.LimeGreen,
        ConnectionState.Loading => Brushes.Yellow,
        ConnectionState.Error => Brushes.Red,
        _ => Brushes.Black,
    };

    public string? ErrorMessage { get; set; }

    public int BorderThickness =>
#if DEBUG
        1
#else
        0
#endif
        ;

    [ObservableProperty]
    public partial DataRow[] Data1 { get; set; } = [];

    [ObservableProperty]
    public partial string[] Data2 { get; set; } = [];
}

public sealed record DataRow(string Text, Brush Color)
{
    public DataRow(string text) : this(text, DefaultBrushes.Base) { }

}

public static class DefaultBrushes
{
    public static Brush Base => Brushes.White;

    public static Brush Good => Brushes.LimeGreen;

    public static Brush Note => Brushes.LightPink;

    public static Brush Alert => Brushes.Coral;

    public static Brush Error => Brushes.Red;
}

public partial class MainWindow : Window
{
    private const int UnfocusedWindowSize =
#if DEBUG
        0
#else
        0
#endif
        ;

    private HttpClient _httpClient;

    private MainWindowViewModel _vm = new()
    {
        State = ConnectionState.Loading,
    };

    private System.Windows.Forms.NotifyIcon _trayIcon;
    private CancellationTokenSource _cancellationTokenSource = new();

    private Task _windowLoopTask;
    private Task _apiLoopTask;

    public MainWindow()
    {
        DataContext = _vm;
        _httpClient = new HttpClient();
        _httpClient.BaseAddress = new Uri("http://localhost:8111");

        InitializeComponent();

        Height = UnfocusedWindowSize;
        Width = UnfocusedWindowSize;

        Left = 0;
        Top = 0;
        ResizeMode = ResizeMode.NoResize;
        WindowState = WindowState.Normal;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        IsHitTestVisible = false;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;

        var iconStream = Application.GetResourceStream(
                new Uri("pack://application:,,,/Icon.ico"))
            .Stream;

        _trayIcon = new System.Windows.Forms.NotifyIcon()
        {
            Text = "Westral",
            Visible = true,
            Icon = new System.Drawing.Icon(iconStream),
            ContextMenuStrip = new System.Windows.Forms.ContextMenuStrip()
            {
                Items =
                {
                    new System.Windows.Forms.ToolStripButton()
                    {
                        Text = "Quit",
                        Command = new RelayCommand(Quit)
                    }
                }
            }
        };

        _windowLoopTask = Task.Run(async () =>
        {
            try
            {
                await RunWindowLoop();
            }
            catch { }
        });

        _apiLoopTask = Task.Run(async () =>
        {
            try
            {
                await RunApiLoop();
            }
            catch { }
        });
    }

    private async void Quit()
    {
        _cancellationTokenSource.Cancel();

        await Task.WhenAll(_windowLoopTask, _apiLoopTask);

        _trayIcon.Dispose();
        Application.Current.Shutdown();
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EnumWindows(EnumWindowsProc enumProc, nuint lParam);

    private delegate bool EnumWindowsProc(nuint hWnd, nuint lParam);

    [LibraryImport("user32.dll")]
    private static unsafe partial int GetWindowTextW(
        nuint hWnd,
        void* lpString,
        int nMaxCount);

    [LibraryImport("user32.dll")]
    private static partial nuint GetForegroundWindow();

    private async Task RunWindowLoop()
    {
        bool isTopmost;
        UnsetTopmost();

        const string TitleMatch = "War Thunder";

        var token = _cancellationTokenSource.Token;

        nuint windowTextBuf;
        unsafe
        {
            windowTextBuf = (nuint)NativeMemory.Alloc((nuint)TitleMatch.Length + 1, sizeof(char));
        }

        // TODO: we should try hooking into some window events instead of polling,
        // or process creation events and use the WT PID
        while (true)
        {
            await Task.Delay(100, token);

            nuint wtWindowHandle = 0;

            unsafe
            {
                EnumWindows((hwnd, _) =>
                {
                    var length = GetWindowTextW(hwnd, (void*)windowTextBuf, TitleMatch.Length + 1);

                    var span = new Span<byte>((void*)windowTextBuf, length * sizeof(char));
                    var windowText = Encoding.Unicode.GetString(span);

                    if (windowText.Equals("War Thunder", StringComparison.Ordinal))
                    {
                        wtWindowHandle = hwnd;
                        return false;
                    }

                    return true;
                }, 0);
            }

            token.ThrowIfCancellationRequested();

            if (wtWindowHandle != 0)
            {
                var foregroundWindow = GetForegroundWindow();

                if (foregroundWindow == wtWindowHandle)
                {
                    if (!isTopmost)
                    {
                        SetTopmost();
                    }
                }
                else
                {
                    if (isTopmost)
                    {
                        UnsetTopmost();
                    }
                }
            }
        }

        void SetTopmost()
        {
            Dispatcher.Invoke(() =>
            {
                Height = SystemParameters.PrimaryScreenHeight;
                Width = SystemParameters.PrimaryScreenWidth;
                Topmost = true;
            });

            isTopmost = true;
        }

        void UnsetTopmost()
        {
            Dispatcher.Invoke(() =>
            {
                Height = UnfocusedWindowSize;
                Width = UnfocusedWindowSize;
                Topmost = false;
            });

            isTopmost = false;
        }

    }

    private async Task RunApiLoop()
    {
        var token = _cancellationTokenSource.Token;
        while (true)
        {
            if (token.IsCancellationRequested)
            {
                return;
            }

            var last = Stopwatch.GetTimestamp();

            try
            {
                var response = await _httpClient.GetAsync(StateModel.Route, token);

                if (!response.IsSuccessStatusCode)
                {
                    SetErrorState($"Error {response.StatusCode}");
                    await Task.Delay(500, token);
                    continue;
                }

                var content = await response.Content.ReadAsStringAsync(token);

                var json = JsonNode.Parse(content);
                if (json?["valid"]?.GetValueKind() is not JsonValueKind.True)
                {
                    // not sure when this would happen
                    SetErrorState("Not valid"); 
                    await Task.Delay(500, token);
                    continue;
                }

                var state = JsonSerializer.Deserialize<StateModel>(json) ?? throw new UnreachableException();

                var latencyMs = Stopwatch.GetElapsedTime(last).Milliseconds;
                last = Stopwatch.GetTimestamp();

                DataRow flapsDescription = state.Flaps switch
                {
                    0 => new("Raised"),
                    <= 20 => new("Combat", DefaultBrushes.Note),
                    <= 33 => new("Takeoff", DefaultBrushes.Alert),
                    <= 100 => new("Landing", DefaultBrushes.Alert),
                    _ => new("Unknown", DefaultBrushes.Error),
                };

                DataRow enginePower;

                {
                    var powers = json.AsObject()
                        .Where(node => node.Key.StartsWith("power ", StringComparison.Ordinal))
                        .Select(node => node.Value.Deserialize<float>())
                        .ToArray();

                    var text = string.Join(" / ", powers.Select(p => p.ToString("F0", CultureInfo.InvariantCulture)));
                    var brush = powers.Any(p => p == 0) ? DefaultBrushes.Alert : DefaultBrushes.Base;

                    enginePower = new DataRow(text, brush);
                }

                DataRow[] dataRows =
                [
                    flapsDescription,
                    enginePower,
                ];

                string[] extraDataRows =
                [
                    $"Δy: {state.Vy:F1} m/s",
                    $"Latency: {latencyMs} ms",
                ];

                Dispatcher.Invoke(() =>
                {
                    _vm.State = ConnectionState.Connected;
                    _vm.ErrorMessage = null;

                    _vm.Data1 = dataRows;
                    _vm.Data2 = extraDataRows;
                });
            }
            catch
            {
                SetErrorState("Unknown error");
                await Task.Delay(5000, token); // probably game isnt running. todo check more precisely
            }
        }

        void SetErrorState(string errorMessage)
        {
            Dispatcher.Invoke(() =>
            {
                _vm.State = ConnectionState.Error;
                _vm.ErrorMessage = errorMessage;
            });
        }
    }
}
