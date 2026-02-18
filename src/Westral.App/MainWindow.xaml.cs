using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Diagnostics;
using System.Globalization;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Westral.App.WtApi;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Accessibility;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Westral.App;

public sealed partial class MainWindowViewModel : ObservableObject
{
    public int BorderThickness =>
#if DEBUG
        1
#else
        0
#endif
        ;

    [ObservableProperty]
    public partial IReadOnlyCollection<DataRow> Data1 { get; set; } = [];

    [ObservableProperty]
    public partial IReadOnlyCollection<DataRow> Data2 { get; set; } = [];

    [ObservableProperty]
    public partial IReadOnlyCollection<DataRow> SubData { get; set; } = [];
}

public record DataRow(string Text, Brush Color)
{
    public DataRow(string text) : this(text, DefaultBrushes.Base) { }
}

public static class DefaultBrushes
{
    /// <summary>White</summary>
    public static Brush Base => Brushes.WhiteSmoke;

    /// <summary>Green</summary>
    public static Brush Good => Brushes.LimeGreen;

    /// <summary>Pink</summary>
    public static Brush Note => Brushes.LightPink;

    /// <summary>Coral</summary>
    public static Brush Alert => Brushes.Coral;

    /// <summary>Red</summary>
    public static Brush Error => Brushes.Red;
}

public partial class MainWindow : Window
{
    private const int UnfocusedWindowSize =
#if DEBUG
        500
#else
        0
#endif
        ;

    private readonly HttpClient _httpClient;
    private readonly MainWindowViewModel _vm;

    private readonly System.Windows.Forms.NotifyIcon _trayIcon;
    private readonly CancellationTokenSource _cancellationTokenSource = new();

    private readonly Task _windowLoopTask;
    private readonly Task _uiLoopTask;
    private readonly Task _apiLoopTask;

    public MainWindow()
    {
#if DEBUG
        // logging is currently debug only.
        // TODO make this configurable somehow and maybe add a file sink
        _ = Task.Run(RunLogLoop);
#endif

        Log($"Main T: {Environment.CurrentManagedThreadId}");

        DataContext = _vm = new()
        {
            SubData = [new("Loading", DefaultBrushes.Note)],
        };

        _httpClient = new HttpClient
        {
            BaseAddress = new Uri("http://127.0.0.1:8111"),
        };

        Loaded += (_, _) =>
        {
            Log("Hiding from alt tab");

            var windowHandle = (HWND)new WindowInteropHelper(this).Handle;
            var windowLong = PInvoke.GetWindowLong(windowHandle, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE);

            _ = PInvoke.SetWindowLong(
                windowHandle,
                WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE,
                (windowLong | (int)WINDOW_EX_STYLE.WS_EX_TOOLWINDOW) & ~(int)WINDOW_EX_STYLE.WS_EX_APPWINDOW);
        };

        Left = 0;
        Top = 0;
        ResizeMode = ResizeMode.NoResize;
        WindowState = WindowState.Normal;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        IsHitTestVisible = false;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;

        _trayIcon = new System.Windows.Forms.NotifyIcon()
        {
            Text = "Westral",
            Visible = true,
            Icon = new System.Drawing.Icon(
                Application.GetResourceStream(new Uri("pack://application:,,,/Icon.ico")).Stream),
            ContextMenuStrip = new System.Windows.Forms.ContextMenuStrip()
            {
                Items =
                {
                    new System.Windows.Forms.ToolStripMenuItem()
                    {
                        Text = "Quit",
                        Command = new RelayCommand(Quit)
                    }
                }
            }
        };

        InitializeComponent();

        _windowLoopTask = Task.Run(async () =>
        {
            try
            {
                await RunWindowLoop();
            }
            catch { }
        });

        _uiLoopTask = Task.Run(async () =>
        {
            try
            {
                await RunUiLoop();
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
        Log("Quit requested");
        _cancellationTokenSource.Cancel();

        Log($"Waiting for exit");
        var start = Stopwatch.GetTimestamp();

        await Task.WhenAll(_windowLoopTask, _uiLoopTask, _apiLoopTask);

        var elapsed = Stopwatch.GetElapsedTime(start);
        Log($"Exited in {elapsed.Milliseconds}ms");

        _trayIcon.Dispose();
        Application.Current.Shutdown();
    }

    private readonly Channel<string> _logChannel = Channel.CreateUnbounded<string>(new UnboundedChannelOptions()
    {
        AllowSynchronousContinuations = false,
        SingleReader = true,
        SingleWriter = false,
    });

    [Conditional("DEBUG")]
    private void Log(
        string message,
        [CallerMemberName] string? caller = null)
    {
        _logChannel.Writer.TryWrite($"[{caller}] {message}");
    }

    private async Task RunLogLoop()
    {
        PInvoke.AllocConsole();

        await foreach (var message in _logChannel.Reader.ReadAllAsync())
        {
            Console.WriteLine(message);
        }
    }

    private static readonly Channel<HWND> _foregroundWindowChannel = Channel.CreateBounded<HWND>(
        new BoundedChannelOptions(capacity: 1)
        {
            SingleReader = true,
            SingleWriter = true,
            AllowSynchronousContinuations = false,
            FullMode = BoundedChannelFullMode.DropOldest,
        });

    private async Task RunWindowLoop()
    {
        const int EVENT_SYSTEM_FOREGROUND = 0x0003;

        unsafe
        {
            // has to run on the message loop thread i believe
            Dispatcher.Invoke(() =>
            {
                Log($"Setup T: {Environment.CurrentManagedThreadId}");

                var eventHandle = PInvoke.SetWinEventHook(
                    EVENT_SYSTEM_FOREGROUND,
                    EVENT_SYSTEM_FOREGROUND,
                    HMODULE.Null,
                    &ForegroundCallback,
                    0, 0, 0);            
            });
        }

        await RunForegroundListener();

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
        unsafe static void ForegroundCallback(
            HWINEVENTHOOK hWinEventHook,
            uint @event,
            HWND hwnd,
            int idObject,
            int idChild,
            uint idEventThread,
            uint dwmsEventTime)
        {
            _foregroundWindowChannel.Writer.TryWrite(hwnd);
        }
    }

    private async Task RunForegroundListener()
    {
        var token = _cancellationTokenSource.Token;

        bool isTopmost;
        UnsetTopmost();

        const string TitleMatch = "War Thunder";
        const int WindowTextBufferSize = 256 + 1;

        PWSTR windowTextBuf;
        unsafe
        {
            windowTextBuf = (PWSTR)NativeMemory.Alloc(WindowTextBufferSize, sizeof(char));
        }

        await foreach (var hwnd in _foregroundWindowChannel.Reader.ReadAllAsync(token))
        {
            Span<char> windowText;

            unsafe
            {
                var length = PInvoke.GetWindowText(hwnd, windowTextBuf, WindowTextBufferSize);
                windowText = new Span<char>((void*)windowTextBuf, length);
            }

            Log($"T: {Environment.CurrentManagedThreadId}");
            Log($"FG: {windowText}");

            if (windowText.StartsWith("Task Switching", StringComparison.Ordinal))
            {
                // alt tab fires a "task switching" window,
                // AFTER the actual window that was focused.
                Log("Ignored alt tab window");
                continue;
            }

            if (windowText.StartsWith(TitleMatch, StringComparison.Ordinal))
            {
                Log("Got matching window");

                if (isTopmost)
                {
                    Log("Already topmost (?)");
                }
                else
                {
                    Log("Setting topmost");
                    SetTopmost();
                }
            }
            else
            {
                Log("Did not match window");

                if (isTopmost)
                {
                    Log("Unsetting topmost");
                    UnsetTopmost();
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
                Topmost = false;
                Height = UnfocusedWindowSize;
                Width = UnfocusedWindowSize;
            });

            isTopmost = false;
        }
    }

    private Channel<JsonObject> _stateChannel = Channel.CreateBounded<JsonObject>(new BoundedChannelOptions(capacity: 1)
        {
            SingleReader = true,
            SingleWriter = true,
            AllowSynchronousContinuations = false,
            FullMode = BoundedChannelFullMode.DropOldest,
        });

    private Channel<JsonObject> _indicatorsChannel = Channel.CreateBounded<JsonObject>(new BoundedChannelOptions(capacity: 1)
    {
        SingleReader = true,
        SingleWriter = true,
        AllowSynchronousContinuations = false,
        FullMode = BoundedChannelFullMode.DropOldest,
    });

    private async Task RunUiLoop()
    {
        var token = _cancellationTokenSource.Token;

        var latencyQueue = new Queue<double>(capacity: 50);

        for (var i = 0; i < 50; i++)
        {
            latencyQueue.Enqueue(0);
        }

        var last = Stopwatch.GetTimestamp();

        var state = await _stateChannel.Reader.ReadAsync(token);
        var indicators = await _indicatorsChannel.Reader.ReadAsync(token);

        var dataUpdatedEvent = new AutoResetEvent(true);

        _ = Task.Run(async () =>
        {
            await foreach (var item in _stateChannel.Reader.ReadAllAsync(token))
            {
                state = item;
                dataUpdatedEvent.Set();
            }
        });

        _ = Task.Run(async () =>
        {
            await foreach (var item in _indicatorsChannel.Reader.ReadAllAsync(token))
            {
                indicators = item;
                dataUpdatedEvent.Set();
            }
        });

        while (!token.IsCancellationRequested)
        {
            if (dataUpdatedEvent.WaitOne(TimeSpan.FromMilliseconds(100)))
            {
                Render(state, indicators);
            }
        }

        void Render(JsonObject state, JsonObject indicators)
        {
            var now = Stopwatch.GetTimestamp();
            var latency = Stopwatch.GetElapsedTime(last).TotalMilliseconds;
            last = now;

            latencyQueue.Dequeue();
            latencyQueue.Enqueue(latency);

            var latencyMs = latencyQueue.Average();

            List<DataRow> dataRows = new List<DataRow>(3);

            if (state["airbrake, %"]?.GetValue<int>() is int and > 0)
            {
                dataRows.Add(new($"Airbrake", DefaultBrushes.Alert));
            }

            // some planes don't have the flaps value
            if (indicators["type"]?.GetValue<string>() is not "f_2a_adtw")
            {
                DataRow? flapsDescription = state["flaps, %"]?.GetValue<int>() switch
                {
                    0 => new("Raised"),
                    <= 20 => new("Combat", DefaultBrushes.Note),
                    <= 33 => new("Takeoff", DefaultBrushes.Alert),
                    <= 100 => new("Landing", DefaultBrushes.Alert),
                    _ => null,
                };

                if (flapsDescription is not null)
                {
                    dataRows.Add(flapsDescription);
                }
            }

            // some planes (jets?) dont have power value
            if (indicators["type"]?.GetValue<string>() is not "f_2a_adtw")
            {
                var powers = state
                    .Where(node => node.Key.StartsWith("power ", StringComparison.Ordinal))
                    .Select(node => node.Value.Deserialize<float>())
                    .ToArray();

                var text = string.Join(" / ", powers.Select(p => p.ToString("F0", CultureInfo.InvariantCulture)));
                var brush = powers.Any(p => p == 0) ? DefaultBrushes.Alert : DefaultBrushes.Base;

                dataRows.Add(new DataRow(text, brush));
            }

            DataRow[] extraDataRows =
            [
                new($"Δy: {state["Vy, m/s"]?.GetValue<float>(),5:F1} m/s"),
                new($"{-(indicators["aviahorizon_pitch"]?.GetValue<float>() ?? 0),3:F1}°"),
            ];

            DataRow[] subData =
            [
                new($"Latency: {latencyMs:F0} ms"),
                new("Connected", DefaultBrushes.Good),
            ];

            Dispatcher.Invoke(() =>
            {
                _vm.SubData = subData;
                _vm.Data1 = dataRows;
                _vm.Data2 = extraDataRows;
            });
        }
    }

    private async Task RunApiLoop()
    {
        var token = _cancellationTokenSource.Token;

        var valid1 = true;
        var valid2 = true;

        var task1 = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var response = await _httpClient.GetAsync(StateModel.Route, token);

                    if (!response.IsSuccessStatusCode)
                    {
                        Log(await response.Content.ReadAsStringAsync(token));
                        continue;
                    }

                    var json = await JsonNode.ParseAsync(response.Content.ReadAsStream(), cancellationToken: token);
                    valid1 = json?["valid"]?.GetValueKind() is JsonValueKind.True;

                    if (!valid1 && !valid2)
                    {
                        SetErrorState("Not valid");
                        continue;
                    }

                    _stateChannel.Writer.TryWrite(json!.AsObject());
                }
                catch (Exception e)
                {
                    Log(e.ToString());
                    SetErrorState("Error");
                }
            }
        });

        var task2 = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var response = await _httpClient.GetAsync(IndicatorsModel.Route, token);

                    if (!response.IsSuccessStatusCode)
                    {
                        Log(await response.Content.ReadAsStringAsync(token));
                    }

                    var json = await JsonNode.ParseAsync(response.Content.ReadAsStream(), cancellationToken: token);

                    valid2 = json?["valid"]?.GetValueKind() is JsonValueKind.True;

                    if (!valid1 && !valid2)
                    {
                        SetErrorState("Not valid");
                        continue;
                    }

                    _indicatorsChannel.Writer.TryWrite(json!.AsObject());
                }
                catch (Exception e)
                {
                    Log(e.ToString());
                    SetErrorState("Error");
                }
            }
        });

        await Task.WhenAll(task1, task2);
    }

    private void SetErrorState(string errorMessage)
    {
        Dispatcher.Invoke(() =>
        {
            _vm.SubData = [new DataRow(errorMessage, DefaultBrushes.Error)];
            _vm.Data1 = [];
            _vm.Data2 = [];
        });
    }
}
