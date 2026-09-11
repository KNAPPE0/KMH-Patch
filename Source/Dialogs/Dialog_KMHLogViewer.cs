using System.Collections.Generic;
using System.IO;
using KMHPatch.Diagnostics;
using UnityEngine;
using Verse;

namespace KMHPatch.Dialogs
{
    public class Dialog_KMHLogViewer : Window_KMHBase
    {
        protected override bool ClosesOnSessionEnd => false;

        public override Vector2 InitialSize => KMHPatch.UI.DialogLayout.FitToScreen(820f, 540f);

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

            // Separate rects: one shared rect anchored left and right only looks like two columns while it is wide.
            float statusW = string.IsNullOrEmpty(_statusLine) ? 0f : Mathf.Min(260f, inRect.width * 0.5f);
            float titleW  = Mathf.Max(0f, inRect.width - statusW - 8f);

            Text.Font = GameFont.Medium;
            UI.DialogLayout.LabelTrunc(new Rect(0f, 0f, titleW, headerH), "KMH Log");
            Text.Font = GameFont.Small;

            if (statusW > 0f)
                UI.DialogLayout.LabelTrunc(new Rect(inRect.width - statusW, 0f, statusW, headerH),
                                           _statusLine, TextAnchor.UpperRight);

            Widgets.DrawLineHorizontal(0f, headerH, inRect.width);

            Rect bodyRect = new Rect(
                0f,
                headerH + padding,
                inRect.width,
                Mathf.Max(UI.DialogLayout.MinBodyHeight, inRect.height - headerH - footerH - padding * 2f));

            Widgets.DrawMenuSection(bodyRect);
            Rect bodyInner = bodyRect.ContractedBy(4f);

            float lineH       = Text.LineHeight;
            float contentH    = _lines.Count * lineH + 8f;
            Rect  viewRect    = new Rect(0f, 0f,
                Mathf.Max(1f, bodyInner.width - UI.DialogLayout.ScrollbarReserveWidth),
                Mathf.Max(contentH, bodyInner.height));

            Widgets.BeginScrollView(bodyInner, ref _scroll, viewRect);
            try
            {
                // On-screen rows only; 400 labels per repaint is flat waste.
                UI.DialogLayout.VisibleRange(_scroll, bodyInner.height, lineH, _lines.Count, out int first, out int last);
                Color prev = GUI.color;
                for (int i = first; i < last; i++)
                {
                    string line = _lines[i];
                    if (line.Contains("[ERROR]"))     GUI.color = new Color(1f, 0.55f, 0.55f);
                    else if (line.Contains("[WARN]")) GUI.color = new Color(1f, 0.85f, 0.55f);
                    else                              GUI.color = prev;

                    // Truncated, not wrapped: Widgets.Label wraps by default and loses the remainder in a one-line rect.
                    UI.DialogLayout.LabelTrunc(new Rect(0f, i * lineH, viewRect.width, lineH), line);
                }
                GUI.color = prev;
            }
            finally
            {
                Widgets.EndScrollView();
            }

            Rect footerRect = new Rect(0f, inRect.height - footerH, inRect.width, footerH);

            float btnH = 30f;
            float btnY = footerRect.y + (footerH - btnH) / 2f;

            // Buttons share the row and shrink; at fixed widths a narrow window makes them overlap and mis-click.
            float btnW = Mathf.Clamp((footerRect.width - padding * 2f) / 3f, 60f, 130f);

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