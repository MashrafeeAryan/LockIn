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
    internal static class Checks
    {
        public static void Run(string output)
        {
            List<string> results = new List<string>(); bool passed = true;
            Action<string, Action> check = delegate(string name, Action run)
            { try { run(); results.Add("PASS " + name); } catch (Exception e) { passed = false; results.Add("FAIL " + name + ": " + e.Message); } };
            check("Countdown uses elapsed ticks and unlocks at its deadline", delegate
            {
                ulong tick = 1000; SessionClock clock = new SessionClock(60, delegate { return tick; });
                Assert(clock.Remaining == 60); tick = 1001; Assert(clock.Remaining == 60);
                tick = 2000; Assert(clock.Remaining == 59); tick = 61000; Assert(clock.Remaining == 0);
                tick = 100000; Assert(clock.Remaining == 0); Assert(SessionClock.Format(5400) == "90:00");
            });
            check("Invalid session durations are rejected", delegate
            {
                bool zero = false, longSession = false;
                try { new SessionClock(0, delegate { return 0; }); } catch (ArgumentOutOfRangeException) { zero = true; }
                try { new SessionClock(14401, delegate { return 0; }); } catch (ArgumentOutOfRangeException) { longSession = true; }
                Assert(zero && longSession);
            });
            check("Distraction shortcuts block while typing and recovery remain available", delegate
            {
                Assert(KeyboardRules.Block(9, true, false, false, false));
                Assert(KeyboardRules.Block(0x73, true, false, false, false));
                Assert(KeyboardRules.Block(0x5B, false, false, false, false));
                Assert(KeyboardRules.Block(0x1B, false, true, true, false));
                Assert(!KeyboardRules.Block(0x41, false, false, false, false));
                Assert(!KeyboardRules.Block(0x43, false, true, false, false));
                Assert(KeyboardRules.Emergency(0x7B, true, true, true));
                Assert(!KeyboardRules.Block(0x7B, true, true, true, false));
            });
            check("Recovery data round-trips without window titles or content", delegate
            {
                string temp = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(output)), "recovery-test.xml");
                try
                {
                    RecoveryState state = new RecoveryState { ParentPid = 123, Seconds = 60, Target = new WindowRecord { Handle = 77, Pid = 999, ProcessStart = 42 } };
                    Recovery.Save(temp, state); RecoveryState restored = Recovery.Load(temp);
                    Assert(restored.Target.Handle == 77 && restored.Seconds == 60 && restored.Target.ProcessStart == 42);
                    Assert(!restored.Target.Matches());
                }
                finally { if (File.Exists(temp)) File.Delete(temp); }
            });
            check("Native fullscreen and restore work on an invisible test window only", delegate
            {
                Rectangle workArea = Screen.PrimaryScreen.WorkingArea;
                using (Form dummy = new Form { Text = "LockIn test window", StartPosition = FormStartPosition.Manual,
                    Location = new Point(workArea.Left + 40, workArea.Top + 40), Size = new Size(420, 320), ShowInTaskbar = false, Opacity = 0 })
                {
                    IntPtr dummyHandle = dummy.Handle; Native.ShowWindowAsync(dummyHandle, 4); Application.DoEvents();
                    WindowRecord before = WindowRecord.Capture(dummy.Handle); Assert(before.Matches(), "Window/process identity could not be verified");
                    try
                    {
                        Rectangle testBounds = new Rectangle(-19000, -19000, 800, 600);
                        Native.Fullscreen(dummy.Handle, testBounds); Application.DoEvents();
                        Native.Rect actual; Native.GetWindowRect(dummy.Handle, out actual); Assert(actual.Rectangle.Equals(testBounds), "Fullscreen bounds: " + actual.Rectangle + "; expected: " + testBounds);
                        Assert((Native.GetStyle(dummy.Handle, -16).ToInt64() & 0x00C00000) == 0, "Caption style was not removed");
                    }
                    finally { before.Restore(); Application.DoEvents(); }
                    Assert(Native.GetStyle(dummy.Handle, -16).ToInt64() == before.Style, "Window style was not restored");
                    Native.Placement restored = new Native.Placement(); restored.Length = Marshal.SizeOf(typeof(Native.Placement));
                    Native.GetWindowPlacement(dummy.Handle, ref restored);
                    Assert(restored.Normal.Left == before.Placement.Normal.Left && restored.Normal.Top == before.Placement.Normal.Top, "Window placement was not restored");
                    dummy.Close();
                }
            });
            check("Launcher renders without starting a session", delegate
            {
                using (MainForm form = new MainForm())
                {
                    form.StartPosition = FormStartPosition.Manual; form.Location = new System.Drawing.Point(-20000, -20000);
                    form.Show(); Application.DoEvents();
                    using (Bitmap bitmap = new Bitmap(form.Width, form.Height)) form.DrawToBitmap(bitmap, new Rectangle(0, 0, form.Width, form.Height));
                    form.Close();
                }
            });
            check("The bottom-right timer collapses, reopens, and still delivers Stop", delegate
            {
                int stops = 0;
                Rectangle screen = new Rectangle(-20800, -20600, 800, 600);
                using (TimerBadge badge = new TimerBadge(1499, screen, delegate { stops++; }))
                {
                    badge.Show();
                    Application.DoEvents();
                    Assert((Native.GetStyle(badge.Handle, -20).ToInt64() & 0x08000000) != 0,
                        "The timer must not steal focus from the study app");
                    Button stop = badge.Controls.OfType<Button>().Single(b => b.Text == "Stop");
                    Rectangle expanded = badge.Bounds;
                    Assert(stop.Visible && expanded.Right < screen.Right && expanded.Bottom < screen.Bottom,
                        "The timer must start expanded in the bottom-right corner");
                    Stopwatch elapsed = Stopwatch.StartNew();
                    while (stop.Visible && elapsed.ElapsedMilliseconds < 4000)
                    {
                        Application.DoEvents();
                        Thread.Sleep(15);
                    }
                    Assert(!stop.Visible && badge.Width == badge.Height, "The timer did not tuck into the strawberry");
                    Assert(badge.Right == expanded.Right && badge.Bottom == expanded.Bottom,
                        "The strawberry moved away from its corner");
                    badge.Remaining = 1480;
                    Button strawberry = badge.Controls.OfType<Button>().Single(b => b.AccessibleName == "Show timer");
                    strawberry.PerformClick();
                    Assert(stop.Visible && badge.Bounds == expanded, "The strawberry did not reopen the timer");
                    Assert(badge.Controls.OfType<Label>().Single().Text == "24:40", "The hidden timer did not stay current");
                    stop.PerformClick();
                    Assert(stops == 1, "Stop did not request an immediate end to the session");
                }
            });
            check("Hard stop accepts five distinct correct answers and rejects a wrong answer", delegate
            {
                DerivativeChallenge challenge = new DerivativeChallenge(new Random(17));
                int stops = 0;
                using (DerivativeDialog dialog = new DerivativeDialog(challenge, delegate { stops++; }))
                {
                    dialog.Location = new Point(-20000, -20000);
                    dialog.Show();
                    Application.DoEvents();
                    TextBox coefficient = dialog.Controls.OfType<TextBox>().Single(t => t.AccessibleName == "Coefficient");
                    TextBox power = dialog.Controls.OfType<TextBox>().Single(t => t.AccessibleName == "Power of x");
                    Button checkAnswer = dialog.Controls.OfType<Button>().Single(b => b.Text == "Check");
                    coefficient.Text = (challenge.Coefficient * challenge.Power + 1).ToString();
                    power.Text = (challenge.Power - 1).ToString();
                    checkAnswer.PerformClick();
                    Assert(challenge.CorrectAnswers == 0 && stops == 0, "A wrong answer advanced the challenge");

                    HashSet<Tuple<int, int>> questions = new HashSet<Tuple<int, int>>();
                    for (int i = 0; i < DerivativeChallenge.QuestionCount; i++)
                    {
                        Assert(questions.Add(Tuple.Create(challenge.Coefficient, challenge.Power)), "The challenge repeated a problem");
                        coefficient.Text = (challenge.Coefficient * challenge.Power).ToString();
                        power.Text = (challenge.Power - 1).ToString();
                        checkAnswer.PerformClick();
                        Assert(stops == (i == 4 ? 1 : 0), "The session did not end at exactly five correct answers");
                    }
                }
            });
            check("Independent watchdog restores an invisible window when its controller exits", delegate
            {
                Rectangle workArea = Screen.PrimaryScreen.WorkingArea;
                using (Form dummy = new Form { Text = "LockIn recovery test", StartPosition = FormStartPosition.Manual,
                    Location = new Point(workArea.Left + 60, workArea.Top + 60), Size = new Size(420, 320), ShowInTaskbar = false, Opacity = 0 })
                {
                    IntPtr dummyHandle = dummy.Handle; Native.ShowWindowAsync(dummyHandle, 4); Application.DoEvents();
                    WindowRecord before = WindowRecord.Capture(dummyHandle);
                    string path = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(output)), "watchdog-test.xml");
                    Process controller = null, watcher = null;
                    try
                    {
                        string exe = Assembly.GetExecutingAssembly().Location;
                        controller = Process.Start(new ProcessStartInfo(exe, "--test-idle") { UseShellExecute = false, CreateNoWindow = true });
                        RecoveryState state = new RecoveryState { ParentPid = controller.Id,
                            ParentStartTicks = controller.StartTime.ToUniversalTime().Ticks, Seconds = 60, Target = before };
                        Recovery.Save(path, state);
                        Native.Fullscreen(dummyHandle, new Rectangle(-19000, -19000, 800, 600));
                        watcher = Process.Start(new ProcessStartInfo(exe, "--watch \"" + path + "\"") { UseShellExecute = false, CreateNoWindow = true });
                        Stopwatch waiting = Stopwatch.StartNew();
                        while (!watcher.HasExited && waiting.Elapsed.TotalSeconds < 8) { Application.DoEvents(); Thread.Sleep(15); }
                        Application.DoEvents(); Assert(watcher.HasExited, "Recovery watchdog did not finish");
                        Assert(!File.Exists(path), "Recovery marker was not cleared");
                        Assert(Native.GetStyle(dummyHandle, -16).ToInt64() == before.Style, "Watchdog did not restore the window style");
                        Native.Placement restored = new Native.Placement(); restored.Length = Marshal.SizeOf(typeof(Native.Placement));
                        Native.GetWindowPlacement(dummyHandle, ref restored);
                        Stopwatch settling = Stopwatch.StartNew();
                        while ((restored.Normal.Left != before.Placement.Normal.Left || restored.Normal.Top != before.Placement.Normal.Top)
                            && settling.ElapsedMilliseconds < 1000)
                        {
                            Application.DoEvents(); Thread.Sleep(15); Native.GetWindowPlacement(dummyHandle, ref restored);
                        }
                        Assert(restored.Normal.Left == before.Placement.Normal.Left && restored.Normal.Top == before.Placement.Normal.Top,
                            "Watchdog placement: " + restored.Normal.Rectangle + "; expected: " + before.Placement.Normal.Rectangle);
                    }
                    finally
                    {
                        if (controller != null) { if (!controller.HasExited) controller.Kill(); controller.Dispose(); }
                        if (watcher != null) { if (!watcher.HasExited) watcher.Kill(); watcher.Dispose(); }
                        before.Restore(); dummy.Close(); if (File.Exists(path)) File.Delete(path);
                    }
                }
            });
            File.WriteAllLines(output, new string[] { passed ? "ALL CHECKS PASSED" : "CHECKS FAILED" }.Concat(results));
            Environment.ExitCode = passed ? 0 : 1;
        }
        private static void Assert(bool condition, string message = "Assertion failed") { if (!condition) throw new InvalidOperationException(message); }
    }
}
