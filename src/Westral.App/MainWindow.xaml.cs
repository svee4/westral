using CommunityToolkit.Mvvm.ComponentModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Westral.App.WtApi;

namespace Westral.App;

[INotifyPropertyChanged]
public sealed partial class MainWindowViewModel
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StateColor))]
    public partial string State { get; set; } = "";

    public Brush StateColor => State switch
    {
        "Connected" => Brushes.LimeGreen,
        "Loading" => Brushes.Yellow,
        "Error" => Brushes.Red,
        _ => Brushes.Black,
    };

    public int BorderThickness =>
#if DEBUG
        1
#else
        0
#endif
        ;

    public Brush BaseTextColor => Brushes.White;

    [ObservableProperty]
    public partial DataRow[] Data1 { get; set; } = [];

    [ObservableProperty]
    public partial string[] Data2 { get; set; } = [];
}

public sealed record DataRow(string Text, Brush Color)
{
    public DataRow(string text) : this(text, Brushes.White) { }
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
        State = "Loading",
    };

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
        Topmost = true;
        IsHitTestVisible = false;
        AllowsTransparency = true;
        Background = Brushes.Transparent;


        _ = RunWindowLoop();
        _ = RunApiLoop();
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
        await Task.Yield();

        bool isTopmost = false;

        const int MaxWindowTextLengthInCharsIncludingNull = 256;

        // cant await in unsafe
        nuint windowTextBuf;
        unsafe
        {
            windowTextBuf = (nuint)NativeMemory.Alloc(MaxWindowTextLengthInCharsIncludingNull, sizeof(char));
        }

        while (true)
        {
            await Task.Delay(100);

            nuint wtWindowHandle = 0;

            unsafe
            {
                EnumWindows((hwnd, _) =>
                {
                    var length = GetWindowTextW(hwnd, (void*)windowTextBuf, MaxWindowTextLengthInCharsIncludingNull);

                    var span = new Span<byte>((void*)windowTextBuf, length * sizeof(char));
                    var windowText = Encoding.Unicode.GetString(span);

                    if (windowText.StartsWith("War Thunder", StringComparison.Ordinal))
                    {
                        wtWindowHandle = hwnd;
                        return false;
                    }

                    return true;
                }, 0);
            }

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
            });

            isTopmost = true;
        }

        void UnsetTopmost()
        {
            Dispatcher.Invoke(() =>
            {
                Height = UnfocusedWindowSize;
                Width = UnfocusedWindowSize;
            });

            isTopmost = false;
        }

    }

    private async Task RunApiLoop()
    {
        await Task.Yield();

        while (true)
        {
            var last = Stopwatch.GetTimestamp();

            try
            {
                var response = await _httpClient.GetAsync(StateModel.Route);
                var content = await response.Content.ReadAsStringAsync();

                var json = JsonNode.Parse(content);
                if (!(json?["valid"]?.GetValueKind() == JsonValueKind.True))
                {
                    SetErrorState("Not valid");
                    continue;
                }

                var state = JsonSerializer.Deserialize<StateModel>(json) ?? throw new UnreachableException();

                var latencyMs = Stopwatch.GetElapsedTime(last).Milliseconds;
                last = Stopwatch.GetTimestamp();

                DataRow flapsDescription = state.Flaps switch
                {
                    0 => new("Raised"),
                    <= 20 => new("Combat", Brushes.LightPink),
                    <= 33 => new("Takeoff", Brushes.Coral),
                    <= 100 => new("Landing", Brushes.Coral),
                    _ => new("Unknown", Brushes.White)
                };

                DataRow[] dataRows =
                [
                    flapsDescription,
                    new($"{state.Power1} / {state.Power2}"),
                ];

                string[] extraDataRows =
                [
                    $"Δy: {state.Vy:F1} m/s",
                    $"Latency: {latencyMs} ms",
                ];

                Dispatcher.Invoke(() =>
                {
                    _vm.State = "Connected";

                    _vm.Data1 = dataRows;

                    _vm.Data2 = extraDataRows;
                });
            }
            catch
            {
                SetErrorState("Error");
            }
        }

        void SetErrorState(string errorMessage)
        {
            Dispatcher.Invoke(() =>
            {
                _vm.State = errorMessage;
            });
        }
    }
}
