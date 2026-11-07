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

            if (args.Length == 2 && args[0] == "--preview")
            {
                using (MainForm preview = new MainForm())
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
        public static readonly Color Background = Color.FromArgb(18, 24, 23);
        public static readonly Color Card = Color.FromArgb(28, 36, 34);
        public static readonly Color Line = Color.FromArgb(57, 69, 64);
        public static readonly Color Text = Color.FromArgb(240, 244, 234);
        public static readonly Color Muted = Color.FromArgb(167, 183, 172);
        public static readonly Color Accent = Color.FromArgb(205, 239, 146);
        public static Font Font(float size, FontStyle style = FontStyle.Regular)
        { return new Font("Segoe UI", size, style); }
    }

    internal sealed class FlatButton : Button
    {
        public bool Primary;
        public bool Selected;
        public FlatButton()
        {
            FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0;
            Cursor = Cursors.Hand; Font = Theme.Font(11, FontStyle.Bold);
            SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Color fill = Primary || Selected ? Theme.Accent : Theme.Card;
            if (!Enabled) fill = Theme.Line;
            using (SolidBrush brush = new SolidBrush(fill)) e.Graphics.FillRectangle(brush, ClientRectangle);
            if (!Primary && !Selected) using (Pen pen = new Pen(Theme.Line))
                e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
            TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle,
                Primary || Selected ? Theme.Background : Theme.Text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            if (Focused) ControlPaint.DrawFocusRectangle(e.Graphics, new Rectangle(5, 5, Width - 10, Height - 10));
        }
    }

    internal sealed class MainForm : Form
    {
        private readonly NumericUpDown minutes = new NumericUpDown();
        private readonly RadioButton hideMode = new RadioButton();
        private readonly RadioButton closeMode = new RadioButton();
        private readonly FlatButton start = new FlatButton();
        private readonly FlatButton browse = new FlatButton();
        private readonly FlatButton cancel = new FlatButton();
        private readonly Label status;
        private readonly Label selectedApp;
        private readonly List<FlatButton> presets = new List<FlatButton>();
        private readonly AppTarget app = new AppTarget();
        private StudySession session;
        private bool cancelLaunch;
        private bool launching;

        public MainForm()
        {
            Text = "LockIn"; ClientSize = new Size(780, 674);
            FormBorderStyle = FormBorderStyle.FixedSingle; MaximizeBox = false;
            AutoScaleMode = AutoScaleMode.None; StartPosition = FormStartPosition.CenterScreen;
            BackColor = Theme.Background; ForeColor = Theme.Text; Font = Theme.Font(10);
            DoubleBuffered = true;
            AddLabel("FOCUS STUDY  /  WINDOWS", 40, 30, 650, 22, 9, Theme.Accent, FontStyle.Bold);
            AddLabel("Make room", 35, 76, 550, 70, 30, Theme.Text, FontStyle.Bold);
            AddLabel("to learn.", 35, 141, 550, 70, 30, Theme.Text, FontStyle.Bold);
            AddLabel("One app. One timer. Your attention back where you want it.", 42, 214, 700, 26, 11, Theme.Muted);
            Panel card = new Panel { Location = new Point(40, 266), Size = new Size(700, 110), BackColor = Theme.Card };
            Controls.Add(card);
            Label logo = new Label { Text = "C", TextAlign = ContentAlignment.MiddleCenter,
                Location = new Point(18, 25), Size = new Size(54, 54), BackColor = Theme.Accent,
                ForeColor = Theme.Background, Font = Theme.Font(22, FontStyle.Bold) };
            card.Controls.Add(logo);
            selectedApp = new Label { Text = "ChatGPT desktop app", Location = new Point(90, 20), Size = new Size(410, 29),
                Font = Theme.Font(14, FontStyle.Bold), ForeColor = Theme.Text };
            card.Controls.Add(selectedApp);
            card.Controls.Add(new Label { Text = "Opens your installed app in full screen.", Location = new Point(91, 56),
                Size = new Size(430, 28), Font = Theme.Font(10), ForeColor = Theme.Muted });
            browse.Text = "Choose .exe"; browse.Location = new Point(548, 34); browse.Size = new Size(133, 42);
            browse.Click += delegate { ChooseApp(); }; card.Controls.Add(browse);
            AddLabel("SESSION LENGTH", 40, 399, 300, 23, 9, Theme.Muted, FontStyle.Bold);
            int[] lengths = { 25, 45, 60 };
            for (int i = 0; i < lengths.Length; i++)
            {
                int value = lengths[i];
                FlatButton button = new FlatButton { Text = value + " min", Location = new Point(40 + i * 116, 431),
                    Size = new Size(104, 43), Selected = value == 45 };
                button.Click += delegate { minutes.Value = value; };
                Controls.Add(button); presets.Add(button);
            }
            minutes.Location = new Point(566, 438); minutes.Size = new Size(97, 33);
            minutes.Minimum = 1; minutes.Maximum = 240; minutes.Value = 45;
            minutes.BackColor = Theme.Card; minutes.ForeColor = Theme.Text;
            minutes.Font = Theme.Font(13); minutes.BorderStyle = BorderStyle.FixedSingle;
            minutes.ValueChanged += delegate { foreach (FlatButton button in presets)
                { button.Selected = button.Text == minutes.Value + " min"; button.Invalidate(); } };
            Controls.Add(minutes); AddLabel("min", 676, 441, 50, 30, 10, Theme.Muted);
            hideMode.Text = "Hide + block other apps, then restore them";
            hideMode.Location = new Point(42, 495); hideMode.Size = new Size(690, 28); hideMode.Checked = true;
            closeMode.Text = "Ask other apps to close; allow save prompts first";
            closeMode.Location = new Point(42, 526); closeMode.Size = new Size(690, 28);
            Controls.Add(hideMode); Controls.Add(closeMode);
            start.Text = "Study ChatGPT  >"; start.Primary = true;
            start.Location = new Point(40, 573); start.Size = new Size(700, 53);
            start.Click += async delegate { await LaunchSession(); }; Controls.Add(start);
            status = AddLabel("Recovery shortcut: Ctrl + Alt + Shift + F12", 40, 640, 700, 25, 9, Theme.Muted);
            cancel.Text = "Cancel"; cancel.Size = new Size(95, 30); cancel.Location = new Point(645, 636);
            cancel.Visible = false; cancel.Click += delegate { cancelLaunch = true; }; Controls.Add(cancel);
            FormClosing += delegate(object sender, FormClosingEventArgs e)
            {
                if (session != null && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; return; }
                cancelLaunch = true; RecoverSession();
            };
            SystemEventsSetup();
        }

        private void SystemEventsSetup()
        {
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged += DisplayChanged;
            Disposed += delegate { Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= DisplayChanged; };
        }
        private void DisplayChanged(object sender, EventArgs e)
        { if (session != null && IsHandleCreated) BeginInvoke((Action)delegate { RecoverSession(); status.Text = "Displays changed. Session unlocked."; }); }
        private Label AddLabel(string text, int x, int y, int width, int height, float size, Color color, FontStyle style = FontStyle.Regular)
        {
            Label label = new Label { Text = text, Location = new Point(x, y), Size = new Size(width, height),
                Font = Theme.Font(size, style), ForeColor = color, BackColor = Color.Transparent };
            Controls.Add(label); return label;
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (Pen pen = new Pen(Theme.Line, 9)) e.Graphics.DrawEllipse(pen, 625, 99, 84, 84);
            using (Pen pen = new Pen(Theme.Accent, 9)) e.Graphics.DrawArc(pen, 625, 99, 84, 84, -90, 245);
            using (Font font = Theme.Font(12)) TextRenderer.DrawText(e.Graphics, "focus", font, new Rectangle(625, 121, 85, 35),
                Theme.Accent, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
        private void ChooseApp()
        {
            using (OpenFileDialog dialog = new OpenFileDialog { Filter = "Windows application (*.exe)|*.exe", Title = "Choose ChatGPT's executable" })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                app.Executable = Path.GetFullPath(dialog.FileName);
                selectedApp.Text = Path.GetFileNameWithoutExtension(dialog.FileName);
                status.Text = "Selected app: " + Path.GetFileName(dialog.FileName);
            }
        }
        private void SetBusy(bool busy)
        {
            start.Enabled = !busy; browse.Enabled = !busy; minutes.Enabled = !busy;
            hideMode.Enabled = !busy; closeMode.Enabled = !busy;
            foreach (FlatButton button in presets) button.Enabled = !busy;
            cancel.Visible = busy; cancel.BringToFront();
        }
        private async Task LaunchSession()
        {
            if (launching || session != null) return;
            launching = true; cancelLaunch = false; SetBusy(true);
            try
            {
                status.Text = "Opening ChatGPT...";
                IntPtr target = app.FindWindow();
                if (target == IntPtr.Zero) app.Launch();
                Stopwatch wait = Stopwatch.StartNew();
                while (target == IntPtr.Zero && wait.Elapsed.TotalSeconds < 30 && !cancelLaunch)
                { await Task.Delay(200); target = app.FindWindow(); }
                if (cancelLaunch || IsDisposed) return;
                if (target == IntPtr.Zero) throw new InvalidOperationException("ChatGPT's window was not found. Open ChatGPT and sign in, then try again. You can also use Choose .exe.");
                List<IntPtr> others = Native.DesktopWindows().Where(h => !Native.IsRelated(h, target)).ToList();
                if (closeMode.Checked && others.Count > 0)
                {
                    DialogResult answer = MessageBox.Show(this,
                        "LockIn will ask " + others.Count + " other desktop windows to close. Apps may ask you to save.\n\nNo processes will be force-closed. Continue?",
                        "Close other apps", MessageBoxButtons.OKCancel, MessageBoxIcon.Information);
                    if (answer != DialogResult.OK) return;
                    foreach (IntPtr window in others) Native.PostMessage(window, Native.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
                    MessageBox.Show(this, "Finish any save prompts in your other apps.\n\nPress OK here when you are ready to start. Apps that stay open will be minimized and blocked.",
                        "Save your work first", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                if (!Native.IsWindow(target)) throw new InvalidOperationException("ChatGPT closed before the session began. Please reopen it.");
                int duration = (int)minutes.Value;
                session = new StudySession(this, target, duration * 60, delegate(bool completed)
                {
                    session = null; Show(); WindowState = FormWindowState.Normal; Activate();
                    status.Text = completed ? "Session complete. Your desktop is restored." : "Session unlocked. Your desktop is restored.";
                });
                try { session.Start(); Hide(); }
                catch { RecoverSession(); throw; }
            }
            catch (Exception e) { status.Text = "Could not start. Your desktop is unlocked."; MessageBox.Show(this, e.Message, "LockIn", MessageBoxButtons.OK, MessageBoxIcon.Information); }
            finally { launching = false; if (!IsDisposed) SetBusy(false); }
        }
        public void RecoverSession() { if (session != null) session.End(false); }
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0312 && m.WParam.ToInt32() == StudySession.HotkeyId) RecoverSession();
            base.WndProc(ref m);
        }
    }

    internal sealed class AppTarget
    {
        public string Executable;
        public string AppId = ConfigurationManager.AppSettings["ChatGPTAppId"] ?? "OpenAI.ChatGPT-Desktop_2p2nqsd0c76g0!App";
        public IntPtr FindWindow()
        {
            IntPtr result = IntPtr.Zero;
            Native.EnumWindows(delegate(IntPtr hwnd, IntPtr unused)
            {
                if (!Native.IsCandidate(hwnd) || Native.IsIconic(hwnd) && !Native.IsWindowVisible(hwnd)) return true;
                uint pid = Native.WindowPid(hwnd);
                string path = Native.ProcessPath(pid);
                bool matches = !string.IsNullOrEmpty(Executable)
                    ? string.Equals(path, Executable, StringComparison.OrdinalIgnoreCase)
                    : string.Equals(Native.ProcessAppId(pid), AppId, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(Path.GetFileNameWithoutExtension(path), "ChatGPT", StringComparison.OrdinalIgnoreCase);
                if (!matches) return true;
                result = hwnd; return false;
            }, IntPtr.Zero);
            return result;
        }
        public void Launch()
        {
            if (!string.IsNullOrEmpty(Executable)) Process.Start(new ProcessStartInfo(Executable) { UseShellExecute = true });
            else Process.Start(new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"),
                "shell:AppsFolder\\" + AppId) { UseShellExecute = false });
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
        private Rectangle bounds;

        public StudySession(MainForm owner, IntPtr target, int seconds, Action<bool> ended)
        { this.owner = owner; this.target = target; this.seconds = seconds; this.ended = ended; }
        public void Start()
        {
            if (!Native.RegisterHotKey(owner.Handle, HotkeyId, 0x4007, 0x7B))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "The recovery shortcut is unavailable. Close the app using it and retry.");
            state = new RecoveryState { ParentPid = Process.GetCurrentProcess().Id,
                ParentStartTicks = Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks,
                Seconds = seconds, Target = WindowRecord.Capture(target) };
            if (!state.Target.Matches()) throw new InvalidOperationException("ChatGPT's window could not be verified for safe restoration.");
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
            badge = new TimerBadge(); badge.Location = new Point(bounds.Left + (bounds.Width - badge.Width) / 2, bounds.Top + 8); badge.Show();
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
            badge.Remaining = clock.Remaining; badge.Invalidate();
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
            Native.UnregisterHotKey(owner.Handle, HotkeyId);
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
            using (Font font = Theme.Font(27, FontStyle.Bold)) TextRenderer.DrawText(e.Graphics, "Your attention has a place.", font,
                new Rectangle(20, Height / 2 - 45, Width - 40, 70), Theme.Text, TextFormatFlags.HorizontalCenter);
            using (Font font = Theme.Font(11)) TextRenderer.DrawText(e.Graphics, "ChatGPT is open on your study screen. This display will return when your timer ends.", font,
                new Rectangle(30, Height / 2 + 25, Width - 60, 70), Theme.Muted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.WordBreak);
        }
    }

    internal sealed class TimerBadge : Form
    {
        public int Remaining;
        public TimerBadge()
        {
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; TopMost = true;
            StartPosition = FormStartPosition.Manual; Size = new Size(290, 40); BackColor = Theme.Background; DoubleBuffered = true;
        }
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams { get { CreateParams p = base.CreateParams; p.ExStyle |= 0x08000080; return p; } }
        protected override void OnPaint(PaintEventArgs e)
        {
            using (Pen pen = new Pen(Theme.Line)) e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
            using (Font font = Theme.Font(11, FontStyle.Bold)) TextRenderer.DrawText(e.Graphics, "STUDY SESSION   /   " + SessionClock.Format(Remaining), font,
                ClientRectangle, Theme.Accent, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
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
        public const uint WM_CLOSE = 0x10;
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
        [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hwnd, uint msg, IntPtr wp, IntPtr lp);
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
                throw new Win32Exception(Marshal.GetLastWin32Error(), "ChatGPT could not be moved to full screen.");
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
