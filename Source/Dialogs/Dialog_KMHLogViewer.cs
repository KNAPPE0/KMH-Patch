using System.Collections.Generic;
using System.IO;
using KMHPatch.Diagnostics;
using UnityEngine;
using Verse;

namespace KMHPatch.Dialogs
{
    // In-game tail viewer for kmh-patch.log.
    public class Dialog_KMHLogViewer : Window_KMHBase
    {
        public override Vector2 InitialSize => new Vector2(820f, 540f);

        // Keep large logs responsive.
        private const int MaxLines = 400;

        private List<string> _lines = new List<string>();
        private Vector2      _scroll;
        private string       _statusLine;

        public Dialog_KMHLogViewer()
        {
            doCloseX                = true;
            absorbInputAroundWindow = true;
            forcePause              = false;
            draggable               = true;
            resizeable              = true;

            ReloadLines();
        }

        protected override void DrawContents(Rect inRect)
        {
            const float headerH = 32f;
            const float footerH = 38f;
            const float padding = 6f;

            Text.Font   = GameFont.Medium;
            Widgets.Label(new Rect(0f, 0f, inRect.width, headerH), "KMH Log");
            Text.Font   = GameFont.Small;

            if (!string.IsNullOrEmpty(_statusLine))
            {
                Text.Anchor = TextAnchor.UpperRight;
                Widgets.Label(new Rect(0f, 0f, inRect.width, headerH), _statusLine);
                Text.Anchor = TextAnchor.UpperLeft;
            }

            Widgets.DrawLineHorizontal(0f, headerH, inRect.width);

            Rect bodyRect = new Rect(
                0f,
                headerH + padding,
                inRect.width,
                inRect.height - headerH - footerH - padding * 2f);

            Widgets.DrawMenuSection(bodyRect);
            Rect bodyInner = bodyRect.ContractedBy(4f);

            float lineH       = Text.LineHeight;
            float contentH    = _lines.Count * lineH + 8f;
            Rect  viewRect    = new Rect(0f, 0f, bodyInner.width - 16f, Mathf.Max(contentH, bodyInner.height));

            Widgets.BeginScrollView(bodyInner, ref _scroll, viewRect);
            try
            {
                float y = 0f;
                foreach (string line in _lines)
                {
                    Color prev = GUI.color;
                    if (line.Contains("[ERROR]"))      GUI.color = new Color(1f, 0.55f, 0.55f);
                    else if (line.Contains("[WARN]"))  GUI.color = new Color(1f, 0.85f, 0.55f);

                    Widgets.Label(new Rect(0f, y, viewRect.width, lineH), line);

                    GUI.color = prev;
                    y += lineH;
                }
            }
            finally
            {
                Widgets.EndScrollView();
            }

            Rect footerRect = new Rect(0f, inRect.height - footerH, inRect.width, footerH);

            float btnH = 30f;
            float btnW = 130f;
            float btnY = footerRect.y + (footerH - btnH) / 2f;

            if (Widgets.ButtonText(new Rect(0f, btnY, btnW, btnH), "Refresh"))
            {
                ReloadLines();
            }
            if (Widgets.ButtonText(new Rect(btnW + padding, btnY, btnW, btnH), "Open folder"))
            {
                Application.OpenURL(KmhLog.LogFolderPath);
            }
            if (Widgets.ButtonText(new Rect(footerRect.width - btnW, btnY, btnW, btnH), "Close"))
            {
                Close();
            }
        }

        private void ReloadLines()
        {
            _lines.Clear();
            string path = KmhLog.LogFilePath;

            if (!File.Exists(path))
            {
                _lines.Add($"(no log file at {path} yet)");
                _statusLine = "<color=grey>no file</color>";
                return;
            }

            try
            {
                // Read safely while KMH may still be writing.
                using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (StreamReader sr = new StreamReader(fs))
                {
                    string line;
                    Queue<string> tail = new Queue<string>(MaxLines);
                    while ((line = sr.ReadLine()) != null)
                    {
                        if (tail.Count == MaxLines) tail.Dequeue();
                        tail.Enqueue(line);
                    }
                    _lines.AddRange(tail);
                }

                _statusLine = $"<color=grey>{_lines.Count} line(s), tail of {Path.GetFileName(path)}</color>";

                // Show newest entries first after reload.
                _scroll = new Vector2(0f, float.MaxValue);
            }
            catch (System.Exception ex)
            {
                _lines.Add($"(error reading log: {ex.Message})");
                _statusLine = "<color=#FF7777>read error</color>";
            }
        }
    }
}