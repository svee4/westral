using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.CodeDom;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
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

using unsafe WinEventHookCallback = delegate* unmanaged[Stdcall]<global::Windows.Win32.UI.Accessibility.HWINEVENTHOOK, uint, global::Windows.Win32.Foundation.HWND, int, int, uint, uint, void>;

public enum ConnectionState
{
    Connected = 1,
    Loading,
    Error,
}

public sealed partial class MainWindowViewModel : ObservableObject
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
            State = ConnectionState.Loading,
        };

        _httpClient = new HttpClient
        {
            BaseAddress = new Uri("http://localhost:8111")
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

        await Task.WhenAll(_windowLoopTask, _apiLoopTask);

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
                Log($"Got response {response.StatusCode}");

                if (!response.IsSuccessStatusCode)
                {
                    SetErrorState($"Error {response.StatusCode}");

                    Log(await response.Content.ReadAsStringAsync());

                    await Task.Delay(500, token);
                    continue;
                }

                var content = await response.Content.ReadAsStringAsync(token);

                var json = JsonNode.Parse(content);
                if (json?["valid"]?.GetValueKind() is not JsonValueKind.True)
                {
                    // not sure when this would happen
                    Log($"Not valid: {content}");
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
            catch (Exception e)
            {
                Log($"Unknown error: {e}");
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
                _vm.Data1 = [];
                _vm.Data2 = [];
            });
        }
    }
}
