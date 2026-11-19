using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using UnityEditor;

namespace Pistol.EditorTools
{
    // Shares a WebGL build through a Cloudflare quick tunnel with two processes outside Unity:
    // serve.py (a small Python web server next to this script) and cloudflared. They don't live in the editor,
    // so script reloads and entering Play don't touch them: the link keeps working and downloads aren't cut.
    // cloudflared writes its log to a file; the public URL is read from there (also after a script reload).
    // Both are stopped by Stop and when Unity quits; leftovers from a crashed editor are killed on the next start.
    // Used by WebShareWindow (Pistol > Web Share).
    [InitializeOnLoad]
    public static class WebShareProcesses
    {
        public enum State { Stopped, WaitingForLink, Ready, Failed }

        public static State Current { get; private set; }
        public static string PublicUrl { get; private set; } = "";
        public static string LocalUrl { get; private set; } = "";
        public static string Error { get; private set; } = "";
        public static bool IsRunning => Current == State.WaitingForLink || Current == State.Ready;

        // Raised on the main thread when the state or the URL changes (not every poll)
        public static event Action Changed;

        const double LinkTimeout = 45;
        const double PollInterval = 0.5;
        static readonly Regex UrlPattern = new Regex(@"https://[-a-z0-9]+\.trycloudflare\.com", RegexOptions.IgnoreCase);
        static readonly bool IsWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

        // Process ids and start times survive script reloads (SessionState) and editor crashes (EditorPrefs)
        const string Key = "Pistol.WebShare.";
        static string LogFile => Path.Combine(Path.GetTempPath(), "pistol-webshare-cloudflared.log");

        static double startedAt, lastPoll;

        static WebShareProcesses()
        {
            EditorApplication.update += Poll;
            EditorApplication.quitting += Stop;
            if (SessionState.GetBool(Key + "Active", false))
            {
                // After a script reload: the processes are still there, pick the state up again
                PublicUrl = SessionState.GetString(Key + "Url", "");
                LocalUrl = SessionState.GetString(Key + "Local", "");
                startedAt = EditorApplication.timeSinceStartup - SessionState.GetFloat(Key + "Age", 0f);
                Current = string.IsNullOrEmpty(PublicUrl) ? State.WaitingForLink : State.Ready;
            }
            else KillSaved();   // the editor was closed or crashed while sharing
        }

        // ---------- Start / stop ----------
        public static void Start(string buildFolder, int preferredPort)
        {
            Stop();
            string python = FindPython(out string pythonArgs);
            string cloudflared = FindCloudflared();
            string script = Path.GetFullPath("Assets/_Project/Scripts/Editor/WebShare/serve.py");
            if (python == null) { Fail("Python 3 не найден. " + PythonInstallHint); return; }
            if (cloudflared == null) { Fail("cloudflared не найден. " + CloudflaredInstallHint); return; }
            if (!File.Exists(Path.Combine(buildFolder ?? "", "index.html"))) { Fail("В папке нет index.html"); return; }

            int port = IsPortFree(preferredPort) ? preferredPort : FreePort();
            try { File.Delete(LogFile); } catch { }
            try
            {
                Launch("Server", python, $"{pythonArgs}\"{script}\" \"{buildFolder}\" {port}");
                Launch("Tunnel", cloudflared, $"tunnel --no-autoupdate --url http://127.0.0.1:{port} --logfile \"{LogFile}\"");
            }
            catch (Exception e) { Stop(); Fail("Не удалось запустить: " + e.Message); return; }

            LocalUrl = $"http://localhost:{port}/";
            PublicUrl = "";
            startedAt = EditorApplication.timeSinceStartup;
            Set(State.WaitingForLink);
        }

        public static void Stop()
        {
            KillSaved();
            PublicUrl = LocalUrl = Error = "";
            Set(State.Stopped);
        }

        // ---------- Polling (only while sharing) ----------
        static void Poll()
        {
            if (!IsRunning || EditorApplication.timeSinceStartup - lastPoll < PollInterval) return;
            lastPoll = EditorApplication.timeSinceStartup;
            SessionState.SetFloat(Key + "Age", (float)(EditorApplication.timeSinceStartup - startedAt));

            bool server = Alive("Server"), tunnel = Alive("Tunnel");
            if (!server || !tunnel)
            {
                FailWithLog((server ? "cloudflared" : "Сервер") + " завершился", withLog: !tunnel);
                return;
            }
            if (Current == State.WaitingForLink)
            {
                var match = UrlPattern.Match(ReadLog());
                if (match.Success && !match.Value.StartsWith("https://api.", StringComparison.OrdinalIgnoreCase))
                {
                    PublicUrl = match.Value;
                    Set(State.Ready);
                }
                else if (EditorApplication.timeSinceStartup - startedAt > LinkTimeout)
                    FailWithLog("Cloudflare не дал ссылку за 45 с", withLog: true);
            }
        }

        // The last lines of the cloudflared log, for the window
        public static string LogTail(int lines)
        {
            var all = ReadLog().Split('\n');
            int from = Math.Max(0, all.Length - lines);
            return string.Join("\n", all, from, all.Length - from).TrimEnd();
        }

        static string ReadLog()
        {
            try
            {
                using (var stream = new FileStream(LogFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var reader = new StreamReader(stream))
                    return reader.ReadToEnd();
            }
            catch { return ""; }
        }

        static string LastLogLine()
        {
            string tail = LogTail(1);
            return tail.Length > 160 ? tail.Substring(0, 160) + "…" : tail;
        }

        // Stops both processes and fails; the last line of cloudflared's log tells why (read before Stop clears it)
        static void FailWithLog(string what, bool withLog)
        {
            string last = withLog ? LastLogLine() : "";
            Stop();
            Fail(what + (last != "" ? ": " + last : ""));
        }

        static void Fail(string message)
        {
            Error = message;
            Set(State.Failed);
        }

        static void Set(State state)
        {
            Current = state;
            SessionState.SetBool(Key + "Active", IsRunning);
            SessionState.SetString(Key + "Url", PublicUrl);
            SessionState.SetString(Key + "Local", LocalUrl);
            try { Changed?.Invoke(); }
            catch (Exception e) { UnityEngine.Debug.LogException(e); }
        }

        // ---------- Processes ----------
        // The output goes nowhere (a shell redirect), so nothing can fill up a pipe that no one reads after a reload.
        // On macOS the shell is replaced by the program (exec), so the saved id is the program itself; on Windows
        // the saved id is cmd.exe and the whole tree is killed.
        static void Launch(string role, string exe, string args)
        {
            var info = IsWindows
                ? new ProcessStartInfo("cmd.exe", $"/c \"\"{exe}\" {args} >NUL 2>&1\"")
                : new ProcessStartInfo("/bin/sh", $"-c 'exec \"{exe}\" {args.Replace("'", "'\\''")} >/dev/null 2>&1'");
            info.UseShellExecute = false;
            info.CreateNoWindow = true;
            var process = Process.Start(info);
            EditorPrefs.SetInt(Key + role + "Pid", process.Id);
            EditorPrefs.SetString(Key + role + "Started", process.StartTime.Ticks.ToString());
        }

        static Process Saved(string role)
        {
            int pid = EditorPrefs.GetInt(Key + role + "Pid", 0);
            if (pid <= 0) return null;
            try
            {
                var process = Process.GetProcessById(pid);
                // The same process, not another one that got its id later
                if (process.HasExited || process.StartTime.Ticks.ToString() != EditorPrefs.GetString(Key + role + "Started", "")) return null;
                return process;
            }
            catch { return null; }
        }

        static bool Alive(string role) => Saved(role) != null;

        static void KillSaved()
        {
            foreach (var role in new[] { "Tunnel", "Server" })
            {
                var process = Saved(role);
                if (process != null)
                {
                    try
                    {
                        if (IsWindows) Process.Start(new ProcessStartInfo("taskkill", $"/T /F /PID {process.Id}") { UseShellExecute = false, CreateNoWindow = true })?.WaitForExit(3000);
                        else process.Kill();
                    }
                    catch { }
                }
                EditorPrefs.DeleteKey(Key + role + "Pid");
                EditorPrefs.DeleteKey(Key + role + "Started");
            }
        }

        // ---------- Tools and ports ----------
        public static string PythonInstallHint => IsWindows ? "winget install Python.Python.3.12" : "xcode-select --install";
        public static string CloudflaredInstallHint => IsWindows ? "winget install Cloudflare.cloudflared" : "brew install cloudflared";

        public static string FindCloudflared() => FindExe(IsWindows ? new[] { "cloudflared.exe" } : new[] { "cloudflared" },
            IsWindows ? new[] { Environment.ExpandEnvironmentVariables(@"%LOCALAPPDATA%\Microsoft\WinGet\Links"), Environment.ExpandEnvironmentVariables(@"%ProgramFiles%\cloudflared") }
                      : new[] { "/opt/homebrew/bin", "/usr/local/bin" });

        // Windows: the "py" launcher wants "-3"; elsewhere python3
        public static string FindPython(out string args)
        {
            args = "";
            if (!IsWindows) return FindExe(new[] { "python3" }, new[] { "/usr/bin", "/opt/homebrew/bin", "/usr/local/bin" });
            string py = FindExe(new[] { "py.exe" }, new[] { Environment.ExpandEnvironmentVariables(@"%WINDIR%") });
            if (py != null) { args = "-3 "; return py; }
            return FindExe(new[] { "python.exe", "python3.exe" }, new string[0]);
        }

        // GUI apps on macOS don't get the shell PATH, so the usual folders are checked too
        static string FindExe(string[] names, string[] extraDirs)
        {
            var dirs = new List<string>((Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator));
            dirs.AddRange(extraDirs);
            foreach (var dir in dirs)
                foreach (var name in names)
                {
                    if (string.IsNullOrWhiteSpace(dir)) continue;
                    try
                    {
                        string path = Path.Combine(dir.Trim(), name);
                        if (File.Exists(path)) return path;
                    }
                    catch { }
                }
            return null;
        }

        static bool IsPortFree(int port)
        {
            if (port < 1024 || port > 65535) return false;
            try
            {
                var listener = new TcpListener(IPAddress.Loopback, port);
                listener.Start();
                listener.Stop();
                return true;
            }
            catch { return false; }
        }

        static int FreePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }
    }
}
