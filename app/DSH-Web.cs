// DeepSeek Harness Web - native desktop shell.
// WinForms + WebView2 window hosting http://127.0.0.1:3080/ with the DSH whale
// icon embedded in the exe. Features: auto-resolved server paths, single
// instance, session-aware window title, tray (minimize-to-tray), remembered
// window bounds, and a health monitor that reconnects when the server drops.
// NOTE: written in C# 5 so it compiles with the stock .NET Framework csc.exe.
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.WinForms;

namespace DSHWeb
{
    internal static class Program
    {
        [System.Runtime.InteropServices.DllImport("shell32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern int SetCurrentProcessExplicitAppUserModelID(string appID);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool SetProcessDpiAwarenessContext(IntPtr value);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        private static readonly IntPtr DpiAwarenessPerMonitorV2 = new IntPtr(-4);

        [STAThread]
        private static void Main()
        {
            // Per-Monitor V2 DPI awareness (also declared in the embedded manifest).
            try { SetProcessDpiAwarenessContext(DpiAwarenessPerMonitorV2); } catch { }
            try { SetCurrentProcessExplicitAppUserModelID("DeepSeekHarness.Desktop"); } catch { }

            // Single instance: a second launch shows/activates the existing
            // window (works even when it is hidden in the tray).
            bool createdNew;
            using (var mutex = new Mutex(true, @"Local\DSHWebLauncher.SingleInstance", out createdNew))
            {
                if (!createdNew)
                {
                    FocusExisting();
                    return;
                }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new MainForm());
            }
        }

        /// <summary>Show + activate the already-running instance's window.</summary>
        private static void FocusExisting()
        {
            var self = Process.GetCurrentProcess().Id;
            foreach (var p in Process.GetProcessesByName("DSH-Web"))
            {
                if (p.Id == self) continue;
                var hwnd = FindFirstTopWindow(p.Id);
                if (hwnd != IntPtr.Zero)
                {
                    ShowWindow(hwnd, 9); // SW_RESTORE: show if hidden, restore if minimized
                    try { SetForegroundWindow(hwnd); } catch { }
                    break;
                }
            }
        }

        /// <summary>Find the running instance's main WinForms window (even if hidden).</summary>
        private static IntPtr FindFirstTopWindow(int pid)
        {
            IntPtr found = IntPtr.Zero;
            EnumWindows(delegate(IntPtr hWnd, IntPtr lParam)
            {
                uint windowPid;
                GetWindowThreadProcessId(hWnd, out windowPid);
                if (windowPid == (uint)pid)
                {
                    var sb = new System.Text.StringBuilder(256);
                    GetClassName(hWnd, sb, 256);
                    if (sb.ToString().StartsWith("WindowsForms10.Window", StringComparison.Ordinal))
                    {
                        found = hWnd;
                        return false; // stop enumerating
                    }
                }
                return true;
            }, IntPtr.Zero);
            return found;
        }
    }

    /// <summary>Portable path + logging helpers (no machine-specific literals).</summary>
    internal static class DshPaths
    {
        public static readonly string AppDir = AppDomain.CurrentDomain.BaseDirectory;

        private static string ResolveRootDir()
        {
            // GetParent is confused by a trailing separator; trim it first.
            var dir = AppDir.TrimEnd('\\', '/');
            var parent = Directory.GetParent(dir);
            return parent != null ? parent.FullName : dir;
        }

        public static readonly string RootDir = ResolveRootDir();
        public static readonly string LogFile = Path.Combine(RootDir, "dsh-web.log");
        public static readonly string WindowStateFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DSH-Web", "window-state.txt");

        public static string WorkDir()
        {
            var ws = Environment.GetEnvironmentVariable("DSH_WORKSPACE");
            return string.IsNullOrEmpty(ws)
                ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
                : ws;
        }

        public static void Log(string line)
        {
            try
            {
                var dir = Path.GetDirectoryName(LogFile);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.AppendAllText(LogFile, "[app] " + line + " [" + DateTime.Now.ToString("o") + "]" + Environment.NewLine);
            }
            catch { }
        }
    }

    /// <summary>Resolve the dsh server launcher (node + dsh bin.js) without hardcoding.</summary>
    internal static class ServerLocator
    {
        public static string ResolveNodeExe()
        {
            // 1) node on PATH
            try
            {
                var psi = new ProcessStartInfo("where.exe", "node")
                {
                    UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardOutput = true
                };
                using (var p = Process.Start(psi))
                {
                    var line = p.StandardOutput.ReadToEnd().Trim();
                    var first = line.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    if (first.Length > 0 && File.Exists(first[0])) return first[0];
                }
            }
            catch { }
            // 2) common install locations
            var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            foreach (var cand in new[]
            {
                Path.Combine(pf, "nodejs", "node.exe"),
                Path.Combine(pf86, "nodejs", "node.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm", "node.exe")
            })
            {
                if (File.Exists(cand)) return cand;
            }
            return null;
        }

        public static string ResolveDshBin()
        {
            // 1) explicit override
            var env = Environment.GetEnvironmentVariable("DSH_BIN");
            if (!string.IsNullOrEmpty(env) && File.Exists(env)) return env;
            // 2) npx cache: %LOCALAPPDATA%\npm-cache\_npx\*\node_modules\@deepseek-ai\dsh\lib\bin.js
            var npxRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "npm-cache", "_npx");
            try
            {
                if (Directory.Exists(npxRoot))
                {
                    foreach (var dir in Directory.GetDirectories(npxRoot))
                    {
                        var cand = Path.Combine(dir, "node_modules", "@deepseek-ai", "dsh", "lib", "bin.js");
                        if (File.Exists(cand)) return cand;
                    }
                }
            }
            catch { }
            // 3) global npm install
            var global = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "npm", "node_modules", "@deepseek-ai", "dsh", "lib", "bin.js");
            if (File.Exists(global)) return global;
            return null;
        }
    }

    internal class MainForm : Form
    {
        private const int Port = 3080;
        private static readonly string Url = "http://127.0.0.1:3080/";

        // Custom title bar (Doubao-style): a slim white strip with only the
        // three window controls on the right, separated from the page by a
        // light gray divider line.
        private const int TitleBarHeight = 40;
        private static readonly Color StripBg = Color.White;
        private static readonly Color StripDivider = Color.FromArgb(225, 225, 225); // light gray line
        private static readonly Color BtnHoverBg = Color.FromArgb(240, 240, 240);
        private static readonly Color BtnCloseBg = Color.FromArgb(232, 17, 35);
        private static readonly Color BtnFg = Color.FromArgb(95, 95, 95);

        private const int WM_NCHITTEST = 0x0084;
        private const int WM_GETMINMAXINFO = 0x0024;
        private const int HTCLIENT = 1, HTCAPTION = 2;
        private const int HTLEFT = 10, HTRIGHT = 11, HTTOP = 12, HTTOPLEFT = 13, HTTOPRIGHT = 14;
        private const int HTBOTTOM = 15, HTBOTTOMLEFT = 16, HTBOTTOMRIGHT = 17;
        private const int ResizeEdge = 6;

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct NativePoint { public int X; public int Y; }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct MinMaxInfo
        {
            public NativePoint ptReserved;
            public NativePoint ptMaxSize;
            public NativePoint ptMaxPosition;
            public NativePoint ptMinTrackSize;
            public NativePoint ptMaxTrackSize;
        }

        [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        private WebView2 _web;
        private Label _status;
        private Button _btnRetry, _btnRestart, _btnExit;
        private Button _btnMin, _btnMax, _btnClose;
        private NotifyIcon _tray;
        private System.Windows.Forms.Timer _healthTimer;
        private bool _serverWasUp = true;
        private bool _quitting;

        public MainForm()
        {
            Text = "DeepSeek Harness";
            Width = 1440;
            Height = 920;
            MinimumSize = new Size(960, 640);
            StartPosition = FormStartPosition.CenterScreen;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            // Frameless window: the top strip is drawn by the form itself.
            FormBorderStyle = FormBorderStyle.None;
            BackColor = StripBg;
            Padding = new Padding(0, TitleBarHeight, 0, 0);
            DoubleBuffered = true;

            _status = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Color.FromArgb(230, 238, 249),
                BackColor = Color.FromArgb(11, 13, 18),
                Font = new Font("Microsoft YaHei UI", 12f),
                Text = "正在启动 DeepSeek Harness…"
            };
            Controls.Add(_status);

            _btnRetry = MakeButton("重新连接", delegate { ReconnectAsync(); });
            _btnRestart = MakeButton("重启服务", delegate { RestartServerAsync(); });
            _btnExit = MakeButton("退出", delegate { _quitting = true; Application.Exit(); });
            Controls.Add(_btnRetry);
            Controls.Add(_btnRestart);
            Controls.Add(_btnExit);
            HideButtons();

            _web = new WebView2 { Dock = DockStyle.Fill, Visible = false };
            Controls.Add(_web);

            SetupTitleBar();
            SetupTray();

            Shown += delegate { BootstrapAsync(); };
            FormClosing += OnFormClosing;
            Load += delegate { RestoreWindowState(); };
            Paint += delegate(object s, PaintEventArgs e)
            {
                // 1px light gray divider under the white strip.
                using (var pen = new Pen(StripDivider))
                {
                    e.Graphics.DrawLine(pen, 0, TitleBarHeight, ClientSize.Width, TitleBarHeight);
                }
            };
        }

        /// <summary>The three window controls in the top-right of the strip.</summary>
        private void SetupTitleBar()
        {
            int w = 46, h = TitleBarHeight;
            _btnMin = MakeTitleButton("─", delegate { WindowState = FormWindowState.Minimized; });
            _btnMax = MakeTitleButton("□", delegate { ToggleMaximize(); });
            _btnClose = MakeTitleButton("✕", delegate { Close(); });
            _btnClose.FlatAppearance.MouseOverBackColor = BtnCloseBg;
            _btnClose.ForeColor = Color.FromArgb(95, 95, 95);
            // White X on the red hover (this WinForms has no MouseOverForeColor).
            _btnClose.MouseEnter += delegate { _btnClose.ForeColor = Color.White; };
            _btnClose.MouseLeave += delegate { _btnClose.ForeColor = Color.FromArgb(95, 95, 95); };
            Controls.Add(_btnMin);
            Controls.Add(_btnMax);
            Controls.Add(_btnClose);
            LayoutTitleButtons(w, h);
        }

        private static Button MakeTitleButton(string glyph, EventHandler onClick)
        {
            var b = new Button
            {
                Text = glyph,
                Width = 46,
                Height = TitleBarHeight,
                FlatStyle = FlatStyle.Flat,
                BackColor = StripBg,
                ForeColor = BtnFg,
                Font = new Font("Segoe UI Symbol", 11f),
                TabStop = false
            };
            b.FlatAppearance.BorderSize = 0;
            b.FlatAppearance.MouseOverBackColor = BtnHoverBg;
            b.FlatAppearance.MouseDownBackColor = Color.FromArgb(226, 226, 226);
            b.Click += onClick;
            return b;
        }

        private void LayoutTitleButtons(int w, int h)
        {
            _btnClose.SetBounds(ClientSize.Width - w, 0, w, h);
            _btnMax.SetBounds(ClientSize.Width - 2 * w, 0, w, h);
            _btnMin.SetBounds(ClientSize.Width - 3 * w, 0, w, h);
        }

        private void ToggleMaximize()
        {
            WindowState = WindowState == FormWindowState.Maximized
                ? FormWindowState.Normal
                : FormWindowState.Maximized;
        }

        private static Button MakeButton(string text, EventHandler onClick)
        {
            var b = new Button
            {
                Text = text,
                Width = 96,
                Height = 34,
                FlatStyle = FlatStyle.Flat,
                ForeColor = Color.FromArgb(230, 238, 249),
                BackColor = Color.FromArgb(30, 36, 48)
            };
            b.FlatAppearance.BorderColor = Color.FromArgb(80, 96, 130);
            b.Click += onClick;
            return b;
        }

        private void HideButtons()
        {
            _btnRetry.Visible = false;
            _btnRestart.Visible = false;
            _btnExit.Visible = false;
        }

        private void ShowButtons()
        {
            _btnRetry.Visible = true;
            _btnRestart.Visible = true;
            _btnExit.Visible = true;
            LayoutButtons();
        }

        private void LayoutButtons()
        {
            // Center within the fill area (below the custom title strip).
            Rectangle area = _status.Bounds;
            int cy = area.Top + area.Height / 2 + 26;
            int cx = area.Left + area.Width / 2;
            _btnRetry.Location = new Point(cx - 236, cy);
            _btnRestart.Location = new Point(cx - 118, cy);
            _btnExit.Location = new Point(cx, cy);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            // Rounded corners (Win11) for the frameless window.
            try
            {
                int cornerPref = 2; // DWMWA_WINDOW_CORNER_PREFERENCE = 33, DWMWCP_ROUND = 2
                DwmSetWindowAttribute(Handle, 33, ref cornerPref, sizeof(int));
            }
            catch { }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            // OnResize can fire during construction before controls exist.
            if (_btnMin != null) LayoutTitleButtons(46, TitleBarHeight);
            if (_btnMax != null)
            {
                _btnMax.Text = WindowState == FormWindowState.Maximized ? "❐" : "□";
            }
            if (_btnRetry != null && _btnRetry.Visible) LayoutButtons();
            if (_tray != null && WindowState == FormWindowState.Minimized)
            {
                Hide();
            }
        }

        /// <summary>Frameless window: drag from the strip, resize at the edges,
        /// and maximize to the working area so the taskbar stays visible.</summary>
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_GETMINMAXINFO)
            {
                base.WndProc(ref m);
                try
                {
                    var mmi = (MinMaxInfo)System.Runtime.InteropServices.Marshal.PtrToStructure(
                        m.LParam, typeof(MinMaxInfo));
                    var wa = Screen.FromHandle(Handle).WorkingArea;
                    mmi.ptMaxPosition.X = wa.X;
                    mmi.ptMaxPosition.Y = wa.Y;
                    mmi.ptMaxSize.X = wa.Width;
                    mmi.ptMaxSize.Y = wa.Height;
                    System.Runtime.InteropServices.Marshal.StructureToPtr(mmi, m.LParam, false);
                }
                catch { }
                return;
            }

            if (m.Msg == WM_NCHITTEST)
            {
                int x = (short)(m.LParam.ToInt32() & 0xFFFF);
                int y = (short)((m.LParam.ToInt32() >> 16) & 0xFFFF);
                Point p = PointToClient(new Point(x, y));

                if (_btnMin != null && _btnMin.Bounds.Contains(p)) { m.Result = (IntPtr)HTCLIENT; return; }
                if (_btnMax != null && _btnMax.Bounds.Contains(p)) { m.Result = (IntPtr)HTCLIENT; return; }
                if (_btnClose != null && _btnClose.Bounds.Contains(p)) { m.Result = (IntPtr)HTCLIENT; return; }

                if (WindowState != FormWindowState.Maximized)
                {
                    bool l = p.X <= ResizeEdge, r = p.X >= ClientSize.Width - ResizeEdge;
                    bool t = p.Y <= ResizeEdge, b = p.Y >= ClientSize.Height - ResizeEdge;
                    if (t && l) m.Result = (IntPtr)HTTOPLEFT;
                    else if (t && r) m.Result = (IntPtr)HTTOPRIGHT;
                    else if (b && l) m.Result = (IntPtr)HTBOTTOMLEFT;
                    else if (b && r) m.Result = (IntPtr)HTBOTTOMRIGHT;
                    else if (t) m.Result = (IntPtr)HTTOP;
                    else if (b) m.Result = (IntPtr)HTBOTTOM;
                    else if (l) m.Result = (IntPtr)HTLEFT;
                    else if (r) m.Result = (IntPtr)HTRIGHT;
                    else if (p.Y <= TitleBarHeight) m.Result = (IntPtr)HTCAPTION;
                    else m.Result = (IntPtr)HTCLIENT;
                }
                else
                {
                    m.Result = p.Y <= TitleBarHeight ? (IntPtr)HTCAPTION : (IntPtr)HTCLIENT;
                }
                return;
            }
            base.WndProc(ref m);
        }

        private void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            SaveWindowState();
            if (_quitting
                || e.CloseReason == CloseReason.WindowsShutDown
                || e.CloseReason == CloseReason.TaskManagerClosing)
            {
                if (_healthTimer != null) _healthTimer.Dispose();
                if (_tray != null) _tray.Dispose();
                return;
            }
            // Close hides to tray (the detached dsh server keeps running).
            e.Cancel = true;
            Hide();
        }

        private void SetupTray()
        {
            _tray = new NotifyIcon { Icon = Icon, Text = "DeepSeek Harness", Visible = true };
            var menu = new ContextMenuStrip();
            menu.Items.Add("显示 / 隐藏", null, delegate { ToggleWindow(); });
            menu.Items.Add("重新连接", null, delegate { ReconnectAsync(); });
            menu.Items.Add("重启服务", null, delegate { RestartServerAsync(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("退出", null, delegate { _quitting = true; Application.Exit(); });
            _tray.ContextMenuStrip = menu;
            _tray.DoubleClick += delegate { ToggleWindow(); };
        }

        private void ToggleWindow()
        {
            if (Visible) Hide();
            else { Show(); WindowState = FormWindowState.Normal; Activate(); }
        }

        // ---------- lifecycle ----------

        private async void BootstrapAsync()
        {
            DshPaths.Log("app started, exe=" + Application.ExecutablePath);
            if (!IsListening()) StartServerIfNeeded();

            var deadline = DateTime.UtcNow.AddSeconds(90);
            while (DateTime.UtcNow < deadline)
            {
                if (IsListening()) break;
                await Task.Delay(1000);
            }
            if (!IsListening())
            {
                DshPaths.Log("server not reachable after 90s");
                ShowDisconnected("无法连接本地服务 (127.0.0.1:3080)。");
                return;
            }
            try
            {
                var userData = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "DSH-Web-WebView2");
                var env = await Microsoft.Web.WebView2.Core.CoreWebView2Environment.CreateAsync(null, userData, null);
                await _web.EnsureCoreWebView2Async(env);
                _web.CoreWebView2.DocumentTitleChanged += delegate
                {
                    try
                    {
                        var title = _web.CoreWebView2.DocumentTitle;
                        if (!string.IsNullOrWhiteSpace(title)) Text = title;
                    }
                    catch { }
                };
                _web.CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled = true;
                _web.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
                HideButtons();
                _status.Visible = false;
                _web.Visible = true;
                _web.Source = new Uri(Url);
                _serverWasUp = true;
                StartHealthMonitor();
                DshPaths.Log("WebView2 loaded " + Url);
            }
            catch (Exception ex)
            {
                DshPaths.Log("webview2 init failed: " + ex);
                ShowDisconnected("WebView2 初始化失败：" + ex.Message);
            }
        }

        private void StartHealthMonitor()
        {
            _healthTimer = new System.Windows.Forms.Timer { Interval = 5000 };
            _healthTimer.Tick += delegate
            {
                var up = IsListening();
                if (!up && _serverWasUp)
                {
                    _serverWasUp = false;
                    DshPaths.Log("health: server went down");
                    ShowDisconnected("本地服务已断开连接。");
                }
                else if (up && !_serverWasUp)
                {
                    _serverWasUp = true;
                    DshPaths.Log("health: server is back");
                    HideButtons();
                    _status.Visible = false;
                    _web.Visible = true;
                    if (_web.CoreWebView2 != null) { try { _web.CoreWebView2.Reload(); } catch { } }
                }
            };
            _healthTimer.Start();
        }

        private void ShowDisconnected(string message)
        {
            _status.Text = message;
            _status.Visible = true;
            _web.Visible = false;
            ShowButtons();
        }

        private async void ReconnectAsync()
        {
            HideButtons();
            _status.Text = "正在重新连接…";
            _status.Visible = true;
            var deadline = DateTime.UtcNow.AddSeconds(30);
            while (DateTime.UtcNow < deadline)
            {
                if (IsListening()) break;
                await Task.Delay(1000);
            }
            if (IsListening())
            {
                _serverWasUp = true;
                HideButtons();
                _status.Visible = false;
                _web.Visible = true;
                if (_web.CoreWebView2 != null) { try { _web.CoreWebView2.Reload(); } catch { } }
            }
            else
            {
                ShowDisconnected("仍然无法连接本地服务 (127.0.0.1:3080)。");
            }
        }

        private async void RestartServerAsync()
        {
            HideButtons();
            _status.Text = "正在启动本地服务…";
            _status.Visible = true;
            _serverWasUp = false;
            StartServerIfNeeded();
            var deadline = DateTime.UtcNow.AddSeconds(60);
            while (DateTime.UtcNow < deadline)
            {
                if (IsListening()) break;
                await Task.Delay(1000);
            }
            if (IsListening())
            {
                _serverWasUp = true;
                HideButtons();
                _status.Visible = false;
                _web.Visible = true;
                if (_web.CoreWebView2 != null) { try { _web.CoreWebView2.Reload(); } catch { } }
            }
            else
            {
                ShowDisconnected("本地服务启动失败，请检查 dsh-web.log。");
            }
        }

        // ---------- server ----------

        private static bool StartServerIfNeeded()
        {
            if (IsListening()) return true;
            var node = ServerLocator.ResolveNodeExe();
            var dsh = ServerLocator.ResolveDshBin();
            if (node == null || dsh == null)
            {
                DshPaths.Log("cannot start server: node=" + (node ?? "(not found)") + " dshBin=" + (dsh ?? "(not found)"));
                return false;
            }
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = node,
                    Arguments = "\"" + dsh + "\" web",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    WorkingDirectory = DshPaths.WorkDir()
                };
                var p = Process.Start(psi);
                DshPaths.Log("started dsh server pid=" + (p == null ? "?" : p.Id.ToString()) + " bin=" + dsh);
                return true;
            }
            catch (Exception ex)
            {
                DshPaths.Log("start server failed: " + ex.Message);
                return false;
            }
        }

        private static bool IsListening()
        {
            try
            {
                using (var client = new TcpClient())
                {
                    var ar = client.BeginConnect("127.0.0.1", Port, null, null);
                    if (!ar.AsyncWaitHandle.WaitOne(500)) return false;
                    client.EndConnect(ar);
                    return true;
                }
            }
            catch { return false; }
        }

        // ---------- window state ----------

        private void RestoreWindowState()
        {
            try
            {
                var f = DshPaths.WindowStateFile;
                if (!File.Exists(f)) return;
                var parts = File.ReadAllText(f).Split(';');
                int x, y, w, h;
                if (parts.Length == 4
                    && int.TryParse(parts[0], out x) && int.TryParse(parts[1], out y)
                    && int.TryParse(parts[2], out w) && int.TryParse(parts[3], out h)
                    && w >= 300 && h >= 200)
                {
                    var screen = Screen.FromPoint(new Point(x + w / 2, y + h / 2));
                    if (screen != null && screen.WorkingArea.IntersectsWith(new Rectangle(x, y, w, h)))
                    {
                        StartPosition = FormStartPosition.Manual;
                        SetBounds(x, y, w, h);
                    }
                }
            }
            catch { }
        }

        private void SaveWindowState()
        {
            try
            {
                if (WindowState != FormWindowState.Normal) return;
                var dir = Path.GetDirectoryName(DshPaths.WindowStateFile);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(DshPaths.WindowStateFile,
                    Left + ";" + Top + ";" + Width + ";" + Height);
            }
            catch { }
        }
    }
}
