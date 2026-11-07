using System;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace LockInSetup
{
    internal static class Installer
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            bool firstInstance;
            using (Mutex mutex = new Mutex(true, "Local\\LockIn.Setup", out firstInstance))
            {
                if (!firstInstance)
                {
                    MessageBox.Show("LockIn Setup is already open.", "LockIn Setup");
                    return;
                }

                if (MessageBox.Show("Install LockIn and add a desktop shortcut?", "LockIn Setup",
                    MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;

                try
                {
                    string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                    string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                    if (string.IsNullOrEmpty(localAppData) || string.IsNullOrEmpty(desktop))
                        throw new IOException("Windows could not locate your app or desktop folder.");

                    Install(Path.Combine(localAppData, "Programs", "LockIn"), desktop);
                    MessageBox.Show("Open the LockIn shortcut on your desktop.", "LockIn is installed",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception error)
                {
                    MessageBox.Show("Setup couldn't finish.\n\n" + error.Message, "LockIn Setup",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                    Environment.ExitCode = 1;
                }
            }
        }

        internal static void Install(string installDirectory, string desktopDirectory)
        {
            if (!Directory.Exists(desktopDirectory))
                throw new IOException("Your Windows desktop folder is unavailable.");

            Directory.CreateDirectory(installDirectory);
            string executable = Path.Combine(installDirectory, "LockIn.exe");
            string config = executable + ".config";
            if (!File.Exists(config)) InstallFile("LockIn.exe.config", config);
            InstallFile("LockIn.exe", executable);

            Type shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null) throw new InvalidOperationException("Windows shortcut support is unavailable.");
            dynamic shell = Activator.CreateInstance(shellType);
            dynamic shortcut = shell.CreateShortcut(Path.Combine(desktopDirectory, "LockIn.lnk"));
            shortcut.TargetPath = executable;
            shortcut.WorkingDirectory = installDirectory;
            shortcut.IconLocation = executable + ",0";
            shortcut.Description = "Start a LockIn study session";
            shortcut.Save();
        }

        private static void InstallFile(string resourceName, string destination)
        {
            string staged = destination + "." + Guid.NewGuid().ToString("N") + ".new";
            try
            {
                using (Stream payload = typeof(Installer).Assembly.GetManifestResourceStream(resourceName))
                {
                    if (payload == null) throw new InvalidOperationException("The setup file is incomplete.");
                    using (FileStream file = new FileStream(staged, FileMode.CreateNew, FileAccess.Write))
                        payload.CopyTo(file);
                }

                if (File.Exists(destination)) File.Replace(staged, destination, null);
                else File.Move(staged, destination);
            }
            finally
            {
                if (File.Exists(staged)) File.Delete(staged);
            }
        }
    }
}
