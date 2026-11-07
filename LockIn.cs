using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Configuration;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml.Serialization;

namespace LockIn
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            Native.SetProcessDPIAware();
            if (args.Length == 2 && args[0] == "--watch") { Recovery.Watch(args[1]); return; }
            if (args.Length == 1 && args[0] == "--test-idle") { Thread.Sleep(2000); return; }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            if (args.Length == 2 && args[0] == "--self-test") { Checks.Run(args[1]); return; }
            if (args.Length == 2 && (args[0] == "--preview" || args[0] == "--preview-timer"))
            {
                using (Form preview = args[0] == "--preview" ? (Form)new MainForm() : new TimerBadge(1499, delegate { }))
                {
                    preview.StartPosition = FormStartPosition.Manual; preview.Location = new System.Drawing.Point(-20000, -20000);
                    preview.Show(); Application.DoEvents();
                    using (Bitmap bitmap = new Bitmap(preview.Width, preview.Height))
                    { preview.DrawToBitmap(bitmap, new Rectangle(0, 0, preview.Width, preview.Height)); bitmap.Save(args[1]); }
                    preview.Close();
                }
                return;
            }
            bool created;
            using (Mutex mutex = new Mutex(true, "Local\\LockIn.Desktop.v1", out created))
            {
                if (!created) { MessageBox.Show("LockIn is already running.", "LockIn"); return; }
                using (MainForm main = new MainForm())
                {
                    Application.ThreadException += delegate(object sender, ThreadExceptionEventArgs e)
                    { main.RecoverSession(); MessageBox.Show(e.Exception.Message, "LockIn"); };
                    Application.Run(main);
                }
            }
        }
    }

    internal static class Theme
    {
        public static readonly Color Background = Color.FromArgb(255, 249, 245);
        public static readonly Color Card = Color.FromArgb(255, 254, 252);
        public static readonly Color Line = Color.FromArgb(221, 193, 191);
        public static readonly Color Text = Color.FromArgb(71, 22, 37);
        public static readonly Color Muted = Color.FromArgb(115, 76, 85);
        public static readonly Color Accent = Color.FromArgb(166, 53, 78);
        public static readonly Color Tint = Color.FromArgb(251, 225, 226);

        public static Font Font(float size, FontStyle style = FontStyle.Regular)
        {
            return new Font("Segoe UI", size, style);
        }

        public static GraphicsPath RoundedRectangle(Rectangle bounds, int radius)
        {
            int diameter = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
            GraphicsPath path = new GraphicsPath();
            path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    internal sealed class FlatButton : Button
    {
        public bool Primary;
        public bool Selected;
        public bool AccentOutline;
        public string Glyph;
        private bool hovered;

        public FlatButton()
        {
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            Cursor = Cursors.Hand;
            Font = Theme.Font(11, FontStyle.Bold);
            SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            hovered = true;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            hovered = false;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Parent == null ? Theme.Background : Parent.BackColor);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Color fill = Selected || hovered ? Theme.Tint : Theme.Card;
            if (Primary) fill = hovered ? Color.FromArgb(139, 38, 62) : Theme.Accent;
            if (!Enabled) fill = Theme.Background;
            Color ink = Primary ? Color.White : AccentOutline ? Theme.Accent : Theme.Text;
            if (!Enabled) ink = Theme.Muted;
            Rectangle bounds = new Rectangle(1, 1, Width - 2, Height - 2);
            using (GraphicsPath path = Theme.RoundedRectangle(bounds, 9))
            using (SolidBrush brush = new SolidBrush(fill))
            using (Pen outline = new Pen(Selected || AccentOutline || Focused ? Theme.Accent : Theme.Line,
                Selected || Focused ? 1.8f : 1f))
            {
                e.Graphics.FillPath(brush, path);
                if (!Primary || !Enabled) e.Graphics.DrawPath(outline, path);
            }
            TextFormatFlags flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                TextFormatFlags.SingleLine | TextFormatFlags.NoPadding;
            Rectangle textBounds = ClientRectangle;
            if (!string.IsNullOrEmpty(Glyph))
            {
                using (Font iconFont = new Font("Segoe UI Symbol", Font.Size + 2))
                {
                    int textWidth = TextRenderer.MeasureText(e.Graphics, Text, Font, Size.Empty, flags).Width;
                    int iconWidth = TextRenderer.MeasureText(e.Graphics, Glyph, iconFont, Size.Empty, flags).Width;
                    int left = (Width - textWidth - iconWidth - 9) / 2;
                    Rectangle iconBounds = new Rectangle(left, 0, iconWidth, Height);
                    TextRenderer.DrawText(e.Graphics, Glyph, iconFont, iconBounds, ink, flags);
                    textBounds = new Rectangle(left + iconWidth + 9, 0, textWidth + 2, Height);
                }
            }
            TextRenderer.DrawText(e.Graphics, Text, Font, textBounds, ink, flags);
            if (Focused && ShowFocusCues)
                ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(bounds, -4, -4));
        }
    }

    internal sealed class MainForm : Form
    {
        private readonly AppTarget chatGpt = new AppTarget("ChatGPT");
        private readonly AppTarget claude = new AppTarget("Claude");
        private readonly FlatButton chatGptButton;
        private readonly FlatButton claudeButton;
        private readonly FlatButton start;
        private readonly FlatButton custom;
        private readonly List<FlatButton> durationButtons = new List<FlatButton>();
        private readonly Label status;
        private AppTarget selectedApp;
        private int durationMinutes = 25;
        private StudySession session;
        private bool cancelLaunch;
        private bool launching;

        public MainForm()
        {
            SuspendLayout();
            Text = "LockIn";
            ClientSize = new Size(484, 378);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            AutoScaleDimensions = new SizeF(96, 96);
            AutoScaleMode = AutoScaleMode.Dpi;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Theme.Background;
            ForeColor = Theme.Text;
            Font = Theme.Font(10);
            DoubleBuffered = true;

            Label title = AddLabel("LockIn", 204, 23, 182, 58, 28, FontStyle.Bold);
            title.TextAlign = ContentAlignment.MiddleLeft;
            AddLabel("App", 28, 98, 428, 25, 11, FontStyle.Bold);
            chatGptButton = AddButton("ChatGPT", 28, 128, 208, 55);
            chatGptButton.Glyph = "\u25ce";
            chatGptButton.Click += delegate { SelectApp(chatGpt); };
            claudeButton = AddButton("Claude", 248, 128, 208, 55);
            claudeButton.Glyph = "\u2733";
            claudeButton.Click += delegate { SelectApp(claude); };

            AddLabel("Timer", 28, 205, 428, 25, 11, FontStyle.Bold);
            int[] lengths = { 25, 45, 60 };
            for (int i = 0; i < lengths.Length; i++)
            {
                int value = lengths[i];
                FlatButton button = AddButton(value + " min", 28 + i * 110, 235, 98, 43);
                button.Click += delegate { SelectDuration(value, button); };
                durationButtons.Add(button);
            }
            custom = AddButton("Custom", 358, 235, 98, 43);
            custom.Click += delegate { ChooseDuration(); };
            durationButtons.Add(custom);
            SelectDuration(durationMinutes, durationButtons[0]);

            start = AddButton("Start session", 28, 300, 428, 49);
            start.Primary = true;
            start.Click += async delegate
            {
                if (launching) { cancelLaunch = true; return; }
                await LaunchSession();
            };
            AcceptButton = start;
            status = AddLabel("", 28, 351, 428, 25, 9);
            status.ForeColor = Theme.Muted;
            SelectApp(chatGpt);

            FormClosing += delegate(object sender, FormClosingEventArgs e)
            {
                if (session != null && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; return; }
                cancelLaunch = true;
                RecoverSession();
            };
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged += DisplayChanged;
            Disposed += delegate { Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= DisplayChanged; };
            ResumeLayout(false);
            PerformLayout();
        }

        private void DisplayChanged(object sender, EventArgs e)
        {
            if (session != null && IsHandleCreated) BeginInvoke((Action)RecoverSession);
        }

        private Label AddLabel(string text, int x, int y, int width, int height, float size, FontStyle style = FontStyle.Regular)
        {
            Label label = new Label
            {
                Text = text, Location = new Point(x, y), Size = new Size(width, height),
                Font = Theme.Font(size, style), ForeColor = Theme.Text, BackColor = Color.Transparent
            };
            Controls.Add(label);
            return label;
        }

        private FlatButton AddButton(string text, int x, int y, int width, int height)
        {
            FlatButton button = new FlatButton { Text = text, Bounds = new Rectangle(x, y, width, height) };
            Controls.Add(button);
            return button;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            GraphicsState state = e.Graphics.Save();
            float scale = ClientSize.Width / 484f;
            e.Graphics.ScaleTransform(scale, scale);
            using (Pen shackle = new Pen(Theme.Accent, 3.8f))
                e.Graphics.DrawArc(shackle, 166, 31, 20, 25, 180, 180);
            using (GraphicsPath body = Theme.RoundedRectangle(new Rectangle(160, 43, 32, 26), 6))
            using (SolidBrush rose = new SolidBrush(Theme.Accent))
            using (SolidBrush paper = new SolidBrush(Theme.Background))
            {
                e.Graphics.FillPath(rose, body);
                e.Graphics.FillEllipse(paper, 173, 50, 6, 6);
                e.Graphics.FillRectangle(paper, 175, 54, 2, 7);
            }
            e.Graphics.Restore(state);
        }

        private void SelectApp(AppTarget app)
        {
            selectedApp = app;
            chatGptButton.Selected = app == chatGpt;
            claudeButton.Selected = app == claude;
            chatGptButton.Invalidate();
            claudeButton.Invalidate();
            status.Text = "";
        }

        private void SelectDuration(int value, FlatButton selected)
        {
            durationMinutes = value;
            foreach (FlatButton button in durationButtons)
            {
                button.Selected = button == selected;
                button.Invalidate();
            }
            custom.Text = selected == custom ? value + " min" : "Custom";
        }

        private void ChooseDuration()
        {
            using (Form dialog = new Form())
            {
                dialog.SuspendLayout();
                dialog.Text = "Custom timer";
                dialog.ClientSize = new Size(252, 121);
                dialog.AutoScaleDimensions = new SizeF(96, 96);
                dialog.AutoScaleMode = AutoScaleMode.Dpi;
                dialog.FormBorderStyle = FormBorderStyle.FixedDialog;
                dialog.StartPosition = FormStartPosition.CenterParent;
                dialog.ShowInTaskbar = false;
                dialog.MaximizeBox = false;
                dialog.MinimizeBox = false;
                dialog.BackColor = Theme.Background;
                dialog.ForeColor = Theme.Text;
                dialog.Font = Theme.Font(10);
                Label label = new Label { Text = "Minutes", Bounds = new Rectangle(18, 22, 100, 30) };
                NumericUpDown value = new NumericUpDown
                {
                    Minimum = 1, Maximum = 240, Value = durationMinutes, Bounds = new Rectangle(134, 18, 100, 30)
                };
                FlatButton apply = new FlatButton
                {
                    Text = "Set timer", Primary = true, DialogResult = DialogResult.OK,
                    Bounds = new Rectangle(18, 64, 216, 40)
                };
                dialog.Controls.AddRange(new Control[] { label, value, apply });
                dialog.AcceptButton = apply;
                dialog.ActiveControl = value;
                dialog.ResumeLayout(false);
                if (dialog.ShowDialog(this) == DialogResult.OK) SelectDuration((int)value.Value, custom);
            }
        }

        private void ChooseApp(AppTarget app)
        {
            using (OpenFileDialog dialog = new OpenFileDialog
            {
                Filter = "Windows application (*.exe)|*.exe",
                Title = "Choose the " + app.Name + " application"
            })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                app.Executable = Path.GetFullPath(dialog.FileName);
                status.Text = app.Name + " selected";
            }
        }

        private void SetBusy(bool busy)
        {
            chatGptButton.Enabled = !busy;
            claudeButton.Enabled = !busy;
            foreach (FlatButton button in durationButtons) button.Enabled = !busy;
            start.Text = busy ? "Cancel" : "Start session";
        }

        private async Task LaunchSession()
        {
            if (session != null) return;
            AppTarget app = selectedApp;
            launching = true;
            cancelLaunch = false;
            SetBusy(true);
            try
            {
                status.Text = "Opening " + app.Name + "…";
                app.DiscoverInstalledApp();
                IntPtr target = app.FindWindow();
                if (target == IntPtr.Zero && app.TryLaunch())
                {
                    Stopwatch wait = Stopwatch.StartNew();
                    while (target == IntPtr.Zero && wait.Elapsed.TotalSeconds < 30)
                    {
                        await Task.Delay(200);
                        if (cancelLaunch || IsDisposed) return;
                        target = app.FindWindow();
                    }
                }
                if (target == IntPtr.Zero)
                {
                    status.Text = app.Name + " wasn't found.";
                    if (MessageBox.Show(this, "Open " + app.Name + " Desktop and try again, or choose its app file now.",
                        "Choose app file?", MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
                        ChooseApp(app);
                    return;
                }

                session = new StudySession(this, target, durationMinutes * 60, delegate(bool completed)
                {
                    session = null;
                    Show();
                    WindowState = FormWindowState.Normal;
                    Activate();
                    status.Text = completed ? "Session complete" : "Session ended";
                });
                try
                {
                    session.Start();
                    if (session != null) Hide();
                }
                catch { RecoverSession(); throw; }
            }
            catch (Exception e)
            {
                status.Text = "Couldn't start session";
                MessageBox.Show(this, e.Message, "LockIn", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            finally
            {
                launching = false;
                if (!IsDisposed)
                {
                    if (cancelLaunch) status.Text = "";
                    SetBusy(false);
                }
            }
        }

        public void RecoverSession()
        {
            if (session != null) session.End(false);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0312 && m.WParam.ToInt32() == StudySession.HotkeyId) RecoverSession();
            base.WndProc(ref m);
        }
    }

    internal sealed class AppTarget
    {
        public readonly string Name;
        public string Executable;
        private string appId;

        public AppTarget(string name)
        {
            Name = name;
            appId = ConfigurationManager.AppSettings[name + "AppId"];
        }

        public void DiscoverInstalledApp()
        {
            if (!string.IsNullOrEmpty(Executable)) return;
            try
            {
                Type shellType = Type.GetTypeFromProgID("Shell.Application");
                if (shellType == null) return;
                dynamic shell = Activator.CreateInstance(shellType);
                dynamic folder = shell.NameSpace("shell:AppsFolder");
                if (folder == null) return;
                dynamic apps = folder.Items();
                int count = apps.Count;
                for (int i = 0; i < count; i++)
                {
                    dynamic app = apps.Item(i);
                    string name = app.Name;
                    if (!string.Equals(name, Name, StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(name, Name + " Desktop", StringComparison.OrdinalIgnoreCase)) continue;
                    string id = app.Path;
                    if (string.IsNullOrEmpty(id)) continue;
                    appId = id;
                    return;
                }
            }
            catch (COMException) { }
            catch (Microsoft.CSharp.RuntimeBinder.RuntimeBinderException) { }
        }

        public IntPtr FindWindow()
        {
            IntPtr result = IntPtr.Zero;
            Native.EnumWindows(delegate(IntPtr hwnd, IntPtr unused)
            {
                if (!Native.IsCandidate(hwnd)) return true;
                uint pid = Native.WindowPid(hwnd);
                string path = Native.ProcessPath(pid);
                bool matches = !string.IsNullOrEmpty(Executable)
                    ? string.Equals(path, Executable, StringComparison.OrdinalIgnoreCase)
                    : string.Equals(Path.GetFileNameWithoutExtension(path), Name, StringComparison.OrdinalIgnoreCase)
                        || !string.IsNullOrEmpty(appId) && string.Equals(Native.ProcessAppId(pid), appId, StringComparison.OrdinalIgnoreCase);
                if (!matches) return true;
                result = hwnd;
                return false;
            }, IntPtr.Zero);
            return result;
        }

        public bool TryLaunch()
        {
            if (!string.IsNullOrEmpty(Executable))
            {
                Process.Start(new ProcessStartInfo(Executable) { UseShellExecute = true });
                return true;
            }
            if (string.IsNullOrEmpty(appId)) return false;
            string explorer = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
            Process.Start(new ProcessStartInfo(explorer, "\"shell:AppsFolder\\" + appId + "\"") { UseShellExecute = false });
            return true;
        }
    }

    internal sealed class SessionClock
    {
        private readonly Func<ulong> now;
        private readonly ulong started;
        private readonly ulong duration;
        public SessionClock(int seconds, Func<ulong> source)
        {
            if (seconds <= 0 || seconds > 14400) throw new ArgumentOutOfRangeException("seconds");
            now = source; started = now(); duration = (ulong)seconds * 1000;
        }
        public int Remaining { get { ulong elapsed = now() - started; return elapsed >= duration ? 0 : (int)((duration - elapsed + 999) / 1000); } }
        public static string Format(int seconds) { return string.Format("{0:00}:{1:00}", seconds / 60, seconds % 60); }
    }

    internal static class KeyboardRules
    {
        public static bool Block(int key, bool alt, bool control, bool shift, bool windows)
        {
            return key == 0x5B || key == 0x5C || windows || key == 0x7A
                || alt && (key == 0x09 || key == 0x1B || key == 0x73 || key == 0x20)
                || control && key == 0x1B;
        }
        public static bool Emergency(int key, bool alt, bool control, bool shift)
        { return key == 0x7B && alt && control && shift; }
    }

    internal sealed class StudySession
    {
        public const int HotkeyId = 4171;
        private readonly MainForm owner;
        private readonly IntPtr target;
        private readonly int seconds;
        private readonly Action<bool> ended;
        private readonly List<Shield> shields = new List<Shield>();
        private readonly HashSet<int> down = new HashSet<int>();
        private readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
        private RecoveryState state;
        private SessionClock clock;
        private TimerBadge badge;
        private string recoveryPath;
        private IntPtr keyboardHook;
        private IntPtr foregroundHook;
        private Native.HookProc keyboardProc;
        private Native.WinEventProc foregroundProc;
        private bool active;
        private bool ending;
        private bool correcting;
        private bool emergencyQueued;
        private bool hotkeyRegistered;
        private Rectangle bounds;

        public StudySession(MainForm owner, IntPtr target, int seconds, Action<bool> ended)
        { this.owner = owner; this.target = target; this.seconds = seconds; this.ended = ended; }
        public void Start()
        {
            hotkeyRegistered = Native.RegisterHotKey(owner.Handle, HotkeyId, 0x4007, 0x7B);
            state = new RecoveryState { ParentPid = Process.GetCurrentProcess().Id,
                ParentStartTicks = Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks,
                Seconds = seconds, Target = WindowRecord.Capture(target) };
            if (!state.Target.Matches()) throw new InvalidOperationException("The app window could not be verified for restoration.");
            foreach (IntPtr hwnd in Native.DesktopWindows())
                if (!Native.IsRelated(hwnd, target) && !Native.IsIconic(hwnd)) state.Others.Add(WindowRecord.Capture(hwnd));
            recoveryPath = Path.Combine(Path.GetTempPath(), "LockIn-" + Guid.NewGuid().ToString("N") + ".xml");
            Recovery.Save(recoveryPath, state);
            Process.Start(new ProcessStartInfo(Assembly.GetExecutingAssembly().Location, "--watch \"" + recoveryPath + "\"")
                { UseShellExecute = false, CreateNoWindow = true });
            bounds = Screen.FromHandle(target).Bounds;
            foreach (Screen screen in Screen.AllScreens)
            {
                Shield shield = new Shield(screen.Bounds, delegate { BringBack(); });
                shields.Add(shield); shield.Show();
            }
            foreach (WindowRecord record in state.Others) Native.ShowWindowAsync(new IntPtr(record.Handle), 7);
            Native.Fullscreen(target, bounds);
            badge = new TimerBadge(seconds, delegate { End(false); });
            badge.Location = new Point(bounds.Left + (bounds.Width - badge.Width) / 2, bounds.Top + 8);
            badge.Show();
            clock = new SessionClock(seconds, Native.GetTickCount64);
            active = true;
            foreach (int key in new int[] { 0xA0, 0xA1, 0xA2, 0xA3, 0xA4, 0xA5, 0x5B, 0x5C })
                if ((Native.GetAsyncKeyState(key) & 0x8000) != 0) down.Add(key);
            keyboardProc = Keyboard;
            keyboardHook = Native.SetWindowsHookEx(13, keyboardProc, Native.GetModuleHandle(null), 0);
            if (keyboardHook == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows did not allow the focus keyboard controls.");
            foregroundProc = delegate(IntPtr hook, uint evt, IntPtr hwnd, int objectId, int childId, uint thread, uint time)
            {
                if (active && !ending && !Allowed(hwnd)) BringBack();
            };
            foregroundHook = Native.SetWinEventHook(3, 3, IntPtr.Zero, foregroundProc, 0, 0, 0);
            if (foregroundHook == IntPtr.Zero) throw new Win32Exception("Windows did not allow foreground monitoring.");
            timer.Interval = 200; timer.Tick += Tick; timer.Start();
            Tick(null, EventArgs.Empty); BringBack();
        }
        private bool Allowed(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return true;
            if (badge != null && hwnd == badge.Handle) return true;
            if (shields.Any(s => s.Handle == hwnd)) return true;
            return Native.IsRelated(hwnd, target);
        }
        private void Tick(object sender, EventArgs e)
        {
            if (!active || ending) return;
            if (clock.Remaining == 0) { End(true); return; }
            if (!state.Target.Matches()) { End(false); return; }
            badge.Remaining = clock.Remaining;
            Native.Rect rect; Native.GetWindowRect(target, out rect);
            if (Native.IsIconic(target) || !rect.Rectangle.Equals(bounds)) Native.Fullscreen(target, bounds);
            if (!Allowed(Native.GetForegroundWindow())) BringBack();
            Native.SetWindowPos(badge.Handle, new IntPtr(-1), 0, 0, 0, 0, 0x13);
        }
        private void BringBack()
        {
            if (!active || ending || correcting || !Native.IsWindow(target)) return;
            correcting = true;
            try
            {
                IntPtr other = Native.GetForegroundWindow();
                if (!Allowed(other) && Native.IsCandidate(other))
                {
                    if (!Native.IsIconic(other) && !state.Others.Any(r => r.Handle == other.ToInt64() && r.Matches()))
                    {
                        try { state.Others.Add(WindowRecord.Capture(other)); Recovery.Save(recoveryPath, state); }
                        catch { /* Existing snapshots remain available for restoration. */ }
                    }
                    Native.ShowWindowAsync(other, 7);
                }
                Native.Focus(target);
                if (badge != null) Native.SetWindowPos(badge.Handle, new IntPtr(-1), 0, 0, 0, 0, 0x13);
            }
            finally { correcting = false; }
        }
        private IntPtr Keyboard(int code, IntPtr message, IntPtr data)
        {
            if (code >= 0 && active && !ending)
            {
                int key = Marshal.ReadInt32(data);
                int msg = message.ToInt32(); bool press = msg == 0x100 || msg == 0x104;
                if (press) down.Add(key); else down.Remove(key);
                bool alt = down.Contains(0xA4) || down.Contains(0xA5) || down.Contains(0x12);
                bool control = down.Contains(0xA2) || down.Contains(0xA3) || down.Contains(0x11);
                bool shift = down.Contains(0xA0) || down.Contains(0xA1) || down.Contains(0x10);
                bool windows = down.Contains(0x5B) || down.Contains(0x5C);
                if (press && KeyboardRules.Emergency(key, alt, control, shift))
                {
                    if (!emergencyQueued) { emergencyQueued = true; owner.BeginInvoke((Action)delegate { End(false); }); }
                    return new IntPtr(1);
                }
                if (KeyboardRules.Block(key, alt, control, shift, windows)) return new IntPtr(1);
            }
            return Native.CallNextHookEx(keyboardHook, code, message, data);
        }
        public void End(bool completed)
        {
            if (ending) return; ending = true; active = false;
            timer.Stop(); timer.Dispose();
            if (keyboardHook != IntPtr.Zero) Native.UnhookWindowsHookEx(keyboardHook);
            if (foregroundHook != IntPtr.Zero) Native.UnhookWinEvent(foregroundHook);
            if (hotkeyRegistered) Native.UnregisterHotKey(owner.Handle, HotkeyId);
            if (badge != null) badge.Close();
            foreach (Shield shield in shields) shield.Close();
            if (state != null) Recovery.Restore(state);
            if (recoveryPath != null) { try { File.Delete(recoveryPath); } catch { } }
            ended(completed);
        }
    }

    internal sealed class Shield : Form
    {
        private readonly Action focus;
        public Shield(Rectangle bounds, Action focus)
        {
            this.focus = focus; FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false;
            BackColor = Theme.Background; StartPosition = FormStartPosition.Manual; Bounds = bounds;
            TopMost = true; DoubleBuffered = true;
        }
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams { get { CreateParams p = base.CreateParams; p.ExStyle |= 0x08000080; return p; } }
        protected override void OnMouseDown(MouseEventArgs e) { focus(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            using (Font font = Theme.Font(22, FontStyle.Bold))
                TextRenderer.DrawText(e.Graphics, "Session in progress", font, ClientRectangle, Theme.Text,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }

    internal sealed class TimerBadge : Form
    {
        private readonly Label countdown;

        public TimerBadge(int remaining, Action stop)
        {
            SuspendLayout();
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            AutoScaleDimensions = new SizeF(96, 96);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(204, 46);
            BackColor = Theme.Card;
            DoubleBuffered = true;

            countdown = new Label
            {
                Text = SessionClock.Format(remaining),
                Bounds = new Rectangle(31, 1, 73, 44),
                Font = Theme.Font(12, FontStyle.Bold),
                ForeColor = Theme.Text,
                TextAlign = ContentAlignment.MiddleCenter,
                AccessibleName = "Time remaining"
            };
            FlatButton stopButton = new FlatButton
            {
                Text = "Stop",
                Glyph = "\u25a0",
                AccentOutline = true,
                Bounds = new Rectangle(116, 6, 78, 34),
                Font = Theme.Font(9, FontStyle.Bold),
                AccessibleName = "Stop session"
            };
            stopButton.Click += delegate { stop(); };
            Controls.AddRange(new Control[] { countdown, stopButton });
            ResumeLayout(false);
            PerformLayout();
        }

        public int Remaining
        {
            set
            {
                string text = SessionClock.Format(value);
                if (countdown.Text != text) countdown.Text = text;
            }
        }

        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams { get { CreateParams p = base.CreateParams; p.ExStyle |= 0x08000080; return p; } }

        protected override void WndProc(ref Message m)
        {
            // Keep the study app focused while still delivering the Stop click.
            if (m.Msg == 0x0021)
            {
                m.Result = new IntPtr(3);
                return;
            }
            base.WndProc(ref m);
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            if (ClientSize.Width <= 0 || ClientSize.Height <= 0) return;
            using (GraphicsPath path = Theme.RoundedRectangle(ClientRectangle, Height / 2))
            {
                Region previous = Region;
                Region = new Region(path);
                if (previous != null) previous.Dispose();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle outline = Rectangle.Inflate(ClientRectangle, -1, -1);
            using (GraphicsPath path = Theme.RoundedRectangle(outline, outline.Height / 2))
            using (Pen line = new Pen(Theme.Line))
                e.Graphics.DrawPath(line, path);

            GraphicsState state = e.Graphics.Save();
            float scale = ClientSize.Width / 204f;
            e.Graphics.ScaleTransform(scale, scale);
            using (Pen line = new Pen(Theme.Line))
                e.Graphics.DrawLine(line, 109, 12, 109, 34);
            using (Pen icon = new Pen(Theme.Accent, 1.5f))
            {
                e.Graphics.DrawEllipse(icon, 15, 17, 13, 13);
                e.Graphics.DrawLine(icon, 19, 14, 24, 14);
                e.Graphics.DrawLine(icon, 21.5f, 14, 21.5f, 17);
                e.Graphics.DrawLine(icon, 21.5f, 20, 21.5f, 23.5f);
            }
            e.Graphics.Restore(state);
        }
    }

    [Serializable]
    public sealed class RecoveryState
    {
        public int ParentPid;
        public long ParentStartTicks;
        public int Seconds;
        public WindowRecord Target;
        public List<WindowRecord> Others = new List<WindowRecord>();
    }

    [Serializable]
    public sealed class WindowRecord
    {
        public long Handle;
        public uint Pid;
        public long ProcessStart;
        public long Style;
        public bool Topmost;
        public Native.Placement Placement;
        public static WindowRecord Capture(IntPtr hwnd)
        {
            Native.Placement placement = new Native.Placement(); placement.Length = Marshal.SizeOf(typeof(Native.Placement));
            if (!Native.GetWindowPlacement(hwnd, ref placement)) throw new Win32Exception("A window could not be inspected.");
            uint pid = Native.WindowPid(hwnd);
            return new WindowRecord { Handle = hwnd.ToInt64(), Pid = pid, ProcessStart = Native.ProcessStart(pid),
                Style = Native.GetStyle(hwnd, -16).ToInt64(), Topmost = (Native.GetStyle(hwnd, -20).ToInt64() & 8) != 0, Placement = placement };
        }
        public bool Matches()
        {
            IntPtr hwnd = new IntPtr(Handle);
            return Native.IsWindow(hwnd) && Native.WindowPid(hwnd) == Pid && ProcessStart != 0 && Native.ProcessStart(Pid) == ProcessStart;
        }
        public void Restore()
        {
            if (!Matches()) return;
            IntPtr hwnd = new IntPtr(Handle);
            Native.SetStyle(hwnd, -16, new IntPtr(Style));
            Native.SetWindowPos(hwnd, Topmost ? new IntPtr(-1) : new IntPtr(-2), 0, 0, 0, 0, 0x0033 | 0x4000);
            Native.Placement placement = Placement; placement.Length = Marshal.SizeOf(typeof(Native.Placement)); placement.Flags |= 4;
            Native.SetWindowPlacement(hwnd, ref placement);
        }
    }

    internal static class Recovery
    {
        public static void Save(string path, RecoveryState state)
        {
            string next = path + ".new";
            using (FileStream stream = File.Create(next)) new XmlSerializer(typeof(RecoveryState)).Serialize(stream, state);
            if (File.Exists(path)) File.Replace(next, path, null); else File.Move(next, path);
        }
        public static RecoveryState Load(string path)
        { using (FileStream stream = File.OpenRead(path)) return (RecoveryState)new XmlSerializer(typeof(RecoveryState)).Deserialize(stream); }
        public static void Restore(RecoveryState state)
        {
            if (state.Target != null) { try { state.Target.Restore(); } catch { } }
            foreach (WindowRecord record in state.Others) { try { record.Restore(); } catch { } }
        }
        public static void Watch(string path)
        {
            try
            {
                RecoveryState state = Load(path);
                if (state.Seconds < 1 || state.Seconds > 14400) return;
                using (Process parent = Process.GetProcessById(state.ParentPid))
                {
                    if (parent.StartTime.ToUniversalTime().Ticks != state.ParentStartTicks ||
                        !string.Equals(Native.ProcessPath((uint)parent.Id), Assembly.GetExecutingAssembly().Location, StringComparison.OrdinalIgnoreCase)) return;
                    Stopwatch elapsed = Stopwatch.StartNew();
                    while (File.Exists(path))
                    {
                        if (parent.WaitForExit(250)) { if (File.Exists(path)) { Restore(Load(path)); File.Delete(path); } return; }
                        if (elapsed.Elapsed.TotalSeconds > state.Seconds + 60)
                        {
                            // This only ends our own controller if its timer/UI stops responding.
                            parent.Kill(); parent.WaitForExit(5000); if (File.Exists(path)) { Restore(Load(path)); File.Delete(path); } return;
                        }
                    }
                }
            }
            catch { /* The controller also restores its own state during normal shutdown. */ }
        }
    }

    public static class Native
    {
        public delegate bool EnumProc(IntPtr hwnd, IntPtr param);
        public delegate IntPtr HookProc(int code, IntPtr message, IntPtr data);
        public delegate void WinEventProc(IntPtr hook, uint evt, IntPtr hwnd, int objectId, int childId, uint thread, uint time);
        [StructLayout(LayoutKind.Sequential)] public struct Point { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left, Top, Right, Bottom; public Rectangle Rectangle { get { return Rectangle.FromLTRB(Left, Top, Right, Bottom); } } }
        [StructLayout(LayoutKind.Sequential)] public struct Placement { public int Length, Flags, ShowCmd; public Point Min, Max; public Rect Normal; }
        [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
        [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc proc, IntPtr param);
        [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern bool IsZoomed(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr hwnd, uint command);
        [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint process);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int count);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr hwnd, StringBuilder text, int count);
        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
        [DllImport("user32.dll")] public static extern bool GetWindowPlacement(IntPtr hwnd, ref Placement placement);
        [DllImport("user32.dll")] public static extern bool SetWindowPlacement(IntPtr hwnd, ref Placement placement);
        [DllImport("user32.dll")] public static extern bool ShowWindowAsync(IntPtr hwnd, int command);
        [DllImport("user32.dll", SetLastError = true)] public static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr64(IntPtr hwnd, int index);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong32(IntPtr hwnd, int index);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)] private static extern IntPtr SetWindowLongPtr64(IntPtr hwnd, int index, IntPtr value);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)] private static extern int SetWindowLong32(IntPtr hwnd, int index, int value);
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint first, uint second, bool attach);
        [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
        [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int key);
        [DllImport("user32.dll", SetLastError = true)] public static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
        [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr hwnd, int id);
        [DllImport("user32.dll", SetLastError = true)] public static extern IntPtr SetWindowsHookEx(int id, HookProc proc, IntPtr module, uint thread);
        [DllImport("user32.dll")] public static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll")] public static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
        [DllImport("user32.dll")] public static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr module, WinEventProc proc, uint process, uint thread, uint flags);
        [DllImport("user32.dll")] public static extern bool UnhookWinEvent(IntPtr hook);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr GetModuleHandle(string name);
        [DllImport("kernel32.dll")] public static extern ulong GetTickCount64();
        [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
        [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool QueryFullProcessImageName(IntPtr process, uint flags, StringBuilder path, ref int count);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern int GetApplicationUserModelId(IntPtr process, ref uint count, StringBuilder text);
        [DllImport("kernel32.dll")] private static extern bool GetProcessTimes(IntPtr process, out long creation, out long exit, out long kernel, out long user);
        [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out int value, int size);
        public static IntPtr GetStyle(IntPtr hwnd, int index) { return IntPtr.Size == 8 ? GetWindowLongPtr64(hwnd, index) : new IntPtr(GetWindowLong32(hwnd, index)); }
        public static void SetStyle(IntPtr hwnd, int index, IntPtr value) { if (IntPtr.Size == 8) SetWindowLongPtr64(hwnd, index, value); else SetWindowLong32(hwnd, index, value.ToInt32()); }
        public static uint WindowPid(IntPtr hwnd) { uint pid; GetWindowThreadProcessId(hwnd, out pid); return pid; }
        public static string ProcessPath(uint pid)
        {
            IntPtr handle = OpenProcess(0x1000, false, pid); if (handle == IntPtr.Zero) return "";
            try { StringBuilder path = new StringBuilder(32768); int count = path.Capacity; return QueryFullProcessImageName(handle, 0, path, ref count) ? path.ToString() : ""; }
            finally { CloseHandle(handle); }
        }
        public static string ProcessAppId(uint pid)
        {
            IntPtr handle = OpenProcess(0x1000, false, pid); if (handle == IntPtr.Zero) return "";
            try { uint count = 1024; StringBuilder text = new StringBuilder(1024); return GetApplicationUserModelId(handle, ref count, text) == 0 ? text.ToString() : ""; }
            finally { CloseHandle(handle); }
        }
        public static long ProcessStart(uint pid)
        {
            IntPtr handle = OpenProcess(0x1000, false, pid); if (handle == IntPtr.Zero) return 0;
            try { long start, exit, kernel, user; return GetProcessTimes(handle, out start, out exit, out kernel, out user) ? start : 0; }
            finally { CloseHandle(handle); }
        }
        public static bool IsCandidate(IntPtr hwnd)
        {
            if (!IsWindowVisible(hwnd) || GetWindow(hwnd, 4) != IntPtr.Zero || WindowPid(hwnd) == (uint)Process.GetCurrentProcess().Id) return false;
            if ((GetStyle(hwnd, -20).ToInt64() & 0x80) != 0) return false;
            StringBuilder title = new StringBuilder(2); if (GetWindowText(hwnd, title, 2) == 0) return false;
            StringBuilder cls = new StringBuilder(256); GetClassName(hwnd, cls, cls.Capacity);
            if (new string[] { "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "Progman", "WorkerW" }.Contains(cls.ToString())) return false;
            int cloaked; if (DwmGetWindowAttribute(hwnd, 14, out cloaked, 4) == 0 && cloaked != 0) return false;
            return true;
        }
        public static List<IntPtr> DesktopWindows()
        {
            List<IntPtr> windows = new List<IntPtr>();
            EnumWindows(delegate(IntPtr hwnd, IntPtr unused) { if (IsCandidate(hwnd) && ProcessStart(WindowPid(hwnd)) != 0) windows.Add(hwnd); return true; }, IntPtr.Zero);
            return windows;
        }
        public static bool IsRelated(IntPtr hwnd, IntPtr target)
        {
            if (hwnd == IntPtr.Zero || target == IntPtr.Zero) return false;
            if (WindowPid(hwnd) == WindowPid(target)) return true;
            IntPtr owner = GetAncestor(hwnd, 3);
            return owner == target || owner != IntPtr.Zero && WindowPid(owner) == WindowPid(target);
        }
        public static void Fullscreen(IntPtr hwnd, Rectangle bounds)
        {
            if (IsIconic(hwnd) || IsZoomed(hwnd)) ShowWindowAsync(hwnd, 9);
            long style = GetStyle(hwnd, -16).ToInt64();
            SetStyle(hwnd, -16, new IntPtr(style & ~0x21CF0000L));
            if (!SetWindowPos(hwnd, new IntPtr(-1), bounds.Left, bounds.Top, bounds.Width, bounds.Height, 0x4070))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "The app could not be moved to full screen.");
        }
        public static void Focus(IntPtr hwnd)
        {
            if (IsIconic(hwnd)) ShowWindowAsync(hwnd, 9);
            uint ignored; uint foregroundThread = GetWindowThreadProcessId(GetForegroundWindow(), out ignored);
            uint own = GetCurrentThreadId(); bool attached = foregroundThread != 0 && foregroundThread != own && AttachThreadInput(own, foregroundThread, true);
            try { SetForegroundWindow(hwnd); }
            finally { if (attached) AttachThreadInput(own, foregroundThread, false); }
        }
    }

}
