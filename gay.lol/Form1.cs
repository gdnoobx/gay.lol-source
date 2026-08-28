using QuorumAPI;
using System.Drawing.Drawing2D;
using System.Net.Http;
using System.Runtime.InteropServices;
namespace gay.lol

{

    public partial class Form1 : Form
    {

        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();
        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);
        private const int WM_NCLBUTTONDOWN = 0xA1;
        private const int HTCAPTION = 0x2;

        private static readonly HttpClient http = new(new HttpClientHandler { AutomaticDecompression = System.Net.DecompressionMethods.All }) { Timeout = TimeSpan.FromSeconds(10) };

        private class BufferedPanel : Panel
        {
            public BufferedPanel() { DoubleBuffered = true; }
        }
        private BufferedPanel loaderPanel;
        private System.Windows.Forms.Timer loaderTimer;
        private System.Windows.Forms.Timer fadeTimer;
        private int loaderAngle = 0;
        private int loaderAlpha = 255;
        private bool wantTopMost = true;

        public Form1()
        {
            InitializeComponent();
            try
            {
                string p = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "icon.ico");
                if (System.IO.File.Exists(p)) Icon = new Icon(p);
                else using (var s = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("gay.lol.icon.ico"))
                { if (s != null) Icon = new Icon(s); }
            }
            catch { }
            QuorumAPI.QuorumAPI.AutoUpdate();
            InitLoader();
            this.Shown += (s, e) => { TopMost = wantTopMost; };
        }

        private void InitLoader()
        {
            loaderPanel = new BufferedPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Black,
                Visible = true
            };
            loaderPanel.Paint += LoaderPanel_Paint;
            Controls.Add(loaderPanel);
            loaderPanel.BringToFront();
            loaderTimer = new System.Windows.Forms.Timer { Interval = 16 };
            loaderTimer.Tick += (s, e) => { loaderAngle = (loaderAngle + 7) % 360; loaderPanel.Invalidate(); };
            loaderTimer.Start();
            var hideTimer = new System.Windows.Forms.Timer { Interval = 4000, Enabled = true };
            hideTimer.Tick += (s, e) => { hideTimer.Stop(); HideLoader(); };
        }

        private void LoaderPanel_Paint(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int bg = (int)(loaderAlpha * 8.0 / 255.0);
            using var back = new SolidBrush(Color.FromArgb(bg, bg, bg));
            g.FillRectangle(back, loaderPanel.ClientRectangle);
            int size = 46;
            var rect = new Rectangle((loaderPanel.Width - size) / 2, (loaderPanel.Height - size) / 2, size, size);
            using var track = new Pen(Color.FromArgb((int)(loaderAlpha * 40.0 / 255.0), 40, 40, 40), 4);
            using var pen = new Pen(Color.FromArgb(loaderAlpha, 128, 128, 128), 4);
            g.DrawArc(track, rect, 0, 360);
            g.DrawArc(pen, rect, loaderAngle, 270);
        }

        private void HideLoader()
        {
            if (loaderPanel == null || !loaderPanel.Visible || fadeTimer != null) return;
            loaderTimer?.Stop();
            fadeTimer = new System.Windows.Forms.Timer { Interval = 16 };
            fadeTimer.Tick += (s, e) =>
            {
                loaderAlpha -= 22;
                if (loaderAlpha <= 0)
                {
                    fadeTimer.Stop();
                    fadeTimer.Dispose();
                    fadeTimer = null;
                    loaderPanel.Visible = false;
                }
                else loaderPanel.Invalidate();
            };
            fadeTimer.Start();
        }
        bool isRobloxOpen = QuorumAPI.QuorumAPI.IsRobloxOpen();
        private bool attached = false;
        private string lastStatus = "";
        private System.Windows.Forms.Timer statusTimer;

        private async void Form1_Load(object sender, EventArgs e)
        {
            await webView21.EnsureCoreWebView2Async();

            string monacoFolderPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Monaco");

            webView21.CoreWebView2.SetVirtualHostNameToFolderMapping(
                "monaco.local",
                monacoFolderPath,
                Microsoft.Web.WebView2.Core.CoreWebView2HostResourceAccessKind.Allow
            );

            webView21.CoreWebView2.WebMessageReceived += async (s, ev) =>
            {
                var msg = ev.TryGetWebMessageAsString();
                if (msg == "drag") DragWindow();
                else if (msg == "close") Close();
                else if (msg == "min") WindowState = FormWindowState.Minimized;
                else if (msg == "max") WindowState = WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized;
                else if (msg == "ready") HideLoader();
                else if (!string.IsNullOrEmpty(msg) && msg.StartsWith("{"))
                {
                    try
                    {
                        using var doc = System.Text.Json.JsonDocument.Parse(msg);
                        var root = doc.RootElement;
                        var cmd = root.GetProperty("cmd").GetString();
                        if (cmd == "attach") { QuorumAPI.QuorumAPI.AttachAPI(); attached = true; }
                        else if (cmd == "execute")
                        {
                            var code = root.GetProperty("code").GetString() ?? "";
                            QuorumAPI.QuorumAPI.ExecuteScript(code);
                        }
                        else if (cmd == "topmost")
                        {
                            var v = root.GetProperty("code").GetString() ?? "0";
                            wantTopMost = v == "1";
                            TopMost = wantTopMost;
                        }
                        else if (cmd == "fetch")
                        {
                            var url = root.GetProperty("url").GetString() ?? "";
                            var id = root.GetProperty("id").GetString() ?? "";
                            try
                            {
                                var resp = await http.GetAsync(url);
                                var body = await resp.Content.ReadAsStringAsync();
                                webView21.CoreWebView2.PostWebMessageAsString("fetch:" + id + ":" + (resp.IsSuccessStatusCode ? "ok" : "err") + ":" + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(body)));
                            }
                            catch
                            {
                                webView21.CoreWebView2.PostWebMessageAsString("fetch:" + id + ":err:");
                            }
                        }
                    }
                    catch { }
                }
            };

            webView21.Source = new Uri("http://monaco.local/index.html");

            ApplyRoundedRegion();

            statusTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            statusTimer.Tick += StatusTimer_Tick;
            statusTimer.Start();

            Resize += (_, _) => ApplyRoundedRegion();
        }

        private void ApplyRoundedRegion(int radius = 8)
        {
            var r = new Rectangle(0, 0, ClientSize.Width, ClientSize.Height);
            var path = new GraphicsPath();
            path.AddArc(r.X, r.Y, radius * 2, radius * 2, 180, 90);
            path.AddArc(r.Right - radius * 2, r.Y, radius * 2, radius * 2, 270, 90);
            path.AddArc(r.Right - radius * 2, r.Bottom - radius * 2, radius * 2, radius * 2, 0, 90);
            path.AddArc(r.X, r.Bottom - radius * 2, radius * 2, radius * 2, 90, 90);
            path.CloseAllFigures();
            Region = new Region(path);
        }

        private void StatusTimer_Tick(object sender, EventArgs e)
        {
            string st;
            if (attached) st = "green";
            else if (QuorumAPI.QuorumAPI.IsRobloxOpen()) st = "yellow";
            else st = "red";
            if (st != lastStatus)
            {
                lastStatus = st;
                webView21.CoreWebView2?.PostWebMessageAsString("status:" + st);
            }
        }

        private void DragWindow()
        {
            ReleaseCapture();
            SendMessage(Handle, WM_NCLBUTTONDOWN, (IntPtr)HTCAPTION, IntPtr.Zero);
        }

        private void button1_Click(object sender, EventArgs e)
        {
            QuorumAPI.QuorumAPI.AttachAPI();
        }

        private void button2_Click(object sender, EventArgs e)
        {



        }

        private void webView21_Click(object sender, EventArgs e)
        {

        }
        private void webView21_Click_1(object sender, EventArgs e)
        {

        }
    }
}
