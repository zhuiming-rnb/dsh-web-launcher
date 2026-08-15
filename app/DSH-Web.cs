// DeepSeek Harness Web - native desktop shell.
// WinForms + WebView2 window that hosts http://127.0.0.1:3080/ with the DSH
// whale icon embedded in the exe (independent taskbar identity).
using System;
using System.Drawing;
using System.Net.Sockets;
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

        private static readonly IntPtr DpiAwarenessPerMonitorV2 = new IntPtr(-4);

        [STAThread]
        private static void Main()
        {
            // Per-Monitor V2 DPI awareness (also declared in the embedded
            // manifest): keeps the WebView2 surface crisp on scaled displays.
            try { SetProcessDpiAwarenessContext(DpiAwarenessPerMonitorV2); } catch { }
            try { SetCurrentProcessExplicitAppUserModelID("DeepSeekHarness.Desktop"); } catch { }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }

    internal class MainForm : Form
    {
        private const int Port = 3080;
        private static readonly string Url = "http://127.0.0.1:3080/";
        private WebView2 _web;
        private Label _status;

        public MainForm()
        {
            Text = "DeepSeek Harness";
            Width = 1440;
            Height = 920;
            MinimumSize = new Size(960, 640);
            StartPosition = FormStartPosition.CenterScreen;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

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

            _web = new WebView2 { Dock = DockStyle.Fill, Visible = false };
            Controls.Add(_web);

            Shown += async (s, e) => await BootstrapAsync();
        }

        private async Task BootstrapAsync()
        {
            // If the local server is not up yet, start it ourselves (hidden
            // node process, no console window) and wait for it.
            if (!IsListening()) StartServerIfNeeded();

            var deadline = DateTime.UtcNow.AddSeconds(90);
            while (DateTime.UtcNow < deadline)
            {
                if (IsListening()) break;
                await Task.Delay(1000);
            }
            if (!IsListening())
            {
                _status.Text = "无法连接本地服务 (127.0.0.1:3080)。\r\n\r\n请检查 E:\\workplace\\DSH-Web\\dsh-web.log 或手动运行 start-dsh.ps1。";
                return;
            }
            try
            {
                // Keep the WebView2 profile out of the app folder.
                var userData = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "DSH-Web-WebView2");
                var env = await Microsoft.Web.WebView2.Core.CoreWebView2Environment.CreateAsync(null, userData, null);
                await _web.EnsureCoreWebView2Async(env);
                _web.CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled = true;
                _web.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
                _web.Visible = true;
                _status.Visible = false;
                _web.Source = new Uri(Url);
            }
            catch (Exception ex)
            {
                _status.Text = "WebView2 初始化失败：" + ex.Message;
            }
        }

        /// <summary>Start the detached dsh web server (no console window).</summary>
        private static void StartServerIfNeeded()
        {
            var node = @"D:\Program Files\nodejs\node.exe";
            var dshBin = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                @"npm-cache\_npx\1e7f6d9597241db0\node_modules\@deepseek-ai\dsh\lib\bin.js");
            if (!System.IO.File.Exists(node) || !System.IO.File.Exists(dshBin)) return;
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = node,
                    Arguments = "\"" + dshBin + "\" web",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden,
                    WorkingDirectory = @"E:\workplace"
                };
                System.Diagnostics.Process.Start(psi);
            }
            catch { }
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
    }
}
