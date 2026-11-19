using UnityEditor;
using UnityEngine;

namespace Pistol.EditorTools
{
    // Shares a WebGL build by a public link (Cloudflare quick tunnel): pick the build folder, press Start, copy the link.
    // The work is done by WebShareProcesses outside Unity; this window only shows it and repaints when it changes.
    // Menu: Pistol > Web Share.
    public sealed class WebShareWindow : EditorWindow
    {
        const string FolderKey = "Pistol.WebShare.Folder";
        const string PortKey = "Pistol.WebShare.Port";
        const int LogLines = 30;

        bool showLog;
        Vector2 logScroll;
        string cloudflared, python;

        [MenuItem("Pistol/Web Share")]
        static void Open() => GetWindow<WebShareWindow>("Web Share");

        void OnEnable()
        {
            WebShareProcesses.Changed += Repaint;
            Detect();
        }

        void OnDisable() => WebShareProcesses.Changed -= Repaint;

        // Installed while the window was open: look again when it gets focus, not on every repaint
        void OnFocus() => Detect();

        void Detect()
        {
            cloudflared = WebShareProcesses.FindCloudflared();
            python = WebShareProcesses.FindPython(out _);
        }

        void OnGUI()
        {
            EditorGUILayout.HelpBox("Раздаёт WebGL-сборку по ссылке https://….trycloudflare.com. Ссылка работает, пока открыт Unity; " +
                                    "правка скриптов и Play её не прерывают.", MessageType.Info);
            if (python == null)
                EditorGUILayout.HelpBox("Нужен Python 3: " + WebShareProcesses.PythonInstallHint, MessageType.Error);
            if (cloudflared == null)
                EditorGUILayout.HelpBox("Нужен cloudflared: " + WebShareProcesses.CloudflaredInstallHint, MessageType.Error);

            bool running = WebShareProcesses.IsRunning;
            string folder = EditorPrefs.GetString(FolderKey, "");
            int port = EditorPrefs.GetInt(PortKey, 8000);

            using (new EditorGUI.DisabledScope(running))
            {
                EditorGUILayout.LabelField("Папка сборки (с index.html)", EditorStyles.boldLabel);
                using (new EditorGUILayout.HorizontalScope())
                {
                    string edited = EditorGUILayout.TextField(folder);
                    if (edited != folder) EditorPrefs.SetString(FolderKey, folder = edited);
                    if (GUILayout.Button("Выбрать…", GUILayout.Width(80)))
                    {
                        string picked = EditorUtility.OpenFolderPanel("Папка WebGL-сборки", folder, "");
                        if (!string.IsNullOrEmpty(picked)) EditorPrefs.SetString(FolderKey, picked);
                        GUIUtility.ExitGUI();
                    }
                }
                int editedPort = EditorGUILayout.IntField(new GUIContent("Порт", "Если занят — берётся свободный"), port);
                if (editedPort != port) EditorPrefs.SetInt(PortKey, editedPort);
            }

            bool folderOk = !string.IsNullOrEmpty(folder) && System.IO.File.Exists(System.IO.Path.Combine(folder, "index.html"));
            if (!string.IsNullOrEmpty(folder) && !folderOk)
                EditorGUILayout.HelpBox("В этой папке нет index.html: выберите папку, куда Unity собрал WebGL.", MessageType.Warning);

            EditorGUILayout.Space(6);
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(running || !folderOk || python == null || cloudflared == null))
                    if (GUILayout.Button("▶  Start", GUILayout.Height(28))) WebShareProcesses.Start(folder, port);
                using (new EditorGUI.DisabledScope(!running))
                    if (GUILayout.Button("■  Stop", GUILayout.Height(28))) WebShareProcesses.Stop();
            }

            EditorGUILayout.Space(6);
            switch (WebShareProcesses.Current)
            {
                case WebShareProcesses.State.WaitingForLink:
                    EditorGUILayout.HelpBox("Сервер запущен, ждём ссылку от Cloudflare (обычно 5–15 с)…", MessageType.None);
                    break;
                case WebShareProcesses.State.Ready:
                    EditorGUILayout.HelpBox("Ссылка готова. Открываться начнёт через 10–20 с.", MessageType.None);
                    break;
                case WebShareProcesses.State.Failed:
                    EditorGUILayout.HelpBox(WebShareProcesses.Error, MessageType.Error);
                    break;
            }

            Url("Ссылка", WebShareProcesses.PublicUrl, true);
            Url("На этом компьютере", WebShareProcesses.LocalUrl, false);

            EditorGUILayout.Space(6);
            showLog = EditorGUILayout.Foldout(showLog, "Лог cloudflared (последние строки)", true);
            if (showLog)
            {
                logScroll = EditorGUILayout.BeginScrollView(logScroll, GUILayout.Height(160));
                EditorGUILayout.SelectableLabel(WebShareProcesses.LogTail(LogLines), EditorStyles.textArea, GUILayout.ExpandHeight(true));
                EditorGUILayout.EndScrollView();
                if (GUILayout.Button("Обновить лог")) Repaint();
            }
        }

        static void Url(string title, string url, bool copy)
        {
            if (string.IsNullOrEmpty(url)) return;
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.SelectableLabel(url, EditorStyles.textField, GUILayout.Height(EditorGUIUtility.singleLineHeight + 2));
                if (copy && GUILayout.Button("Копировать", GUILayout.Width(90))) EditorGUIUtility.systemCopyBuffer = url;
                if (GUILayout.Button("Открыть", GUILayout.Width(70))) Application.OpenURL(url);
            }
        }
    }
}
