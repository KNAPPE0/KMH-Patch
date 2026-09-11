using System;
using UnityEngine;
using Verse;

namespace KMHPatch.UI
{
    // Laid out twice per frame, measure then draw: sizing a scroll view from the previous frame is a feedback loop.
    internal sealed class KmhStack
    {
        public bool  Measuring;
        public float Width;
        public float Y;

        public KmhStack(float width, bool measuring) { Width = Mathf.Max(1f, width); Measuring = measuring; }

        private const float Pad = 4f;

        public void Gap(float h) => Y += h;

        // Pitched by the font actually drawing it; a hardcoded height overlaps the next row as UI scale grows.
        public static float LabelRowHeight(float lineHeight) => Mathf.Max(22f, Mathf.Ceil(lineHeight) + 2f);

        public float LabelRow => LabelRowHeight(Text.LineHeight);

        // A raw row. Callers that draw their own widgets use this and honour Measuring themselves.
        public Rect Row(float h)
        {
            Rect r = new Rect(0f, Y, Width, h);
            Y += h;
            return r;
        }

        // Counts draws actually performed, so a measure pass can be proven to paint nothing.
        public int DrawOps;

        // A primitive rather than a caller-drawn Row, because a caller that forgets Measuring paints the body twice.
        public void ValueRow(string label, string value) => ValueRow(label, value, LabelRow);

        // Height is a parameter so the no-draw-while-measuring rule can be proven without a live font.
        internal void ValueRow(string label, string value, float h)
        {
            if (!Measuring)
            {
                DrawOps++;
                DialogLayout.LabelTrunc(new Rect(0f, Y, Width * 0.6f, h), label, TextAnchor.MiddleLeft);
                DialogLayout.LabelTrunc(new Rect(Width * 0.6f, Y, Width * 0.4f, h), value, TextAnchor.MiddleRight);
            }
            Y += h;
        }

        public void Header(string text)
        {
            GameFont prev = Text.Font;
            Text.Font = GameFont.Medium;
            float h = Text.CalcHeight(text, Width);
            if (!Measuring) Widgets.Label(new Rect(0f, Y, Width, h), text);
            Text.Font = prev;
            Y += h + 2f;
            Divider();
        }

        public void Divider()
        {
            if (!Measuring)
            {
                Color old = GUI.color;
                GUI.color = new Color(0.35f, 0.35f, 0.35f);
                Widgets.DrawLineHorizontal(0f, Y + 3f, Width);
                GUI.color = old;
            }
            Y += 8f;
        }

        // Wrapping text. Height comes from Text.CalcHeight at the real width, so it can never be clipped.
        public void Label(string text, Color? color = null)
        {
            if (string.IsNullOrEmpty(text)) return;
            float h = Text.CalcHeight(text, Width);
            if (!Measuring)
            {
                Color old = GUI.color;
                if (color.HasValue) GUI.color = color.Value;
                Widgets.Label(new Rect(0f, Y, Width, h), text);
                GUI.color = old;
            }
            Y += h + 2f;
        }

        public void Checkbox(string label, ref bool value, string tooltip = null)
        {
            float h = Mathf.Max(24f, Text.CalcHeight(label, Width - 40f));
            if (!Measuring)
            {
                Rect r = new Rect(0f, Y, Width, h);
                if (!string.IsNullOrEmpty(tooltip))
                {
                    if (Mouse.IsOver(r)) Widgets.DrawHighlight(r);
                    TooltipHandler.TipRegion(r, tooltip);
                }
                Widgets.CheckboxLabeled(r, label, ref value);
            }
            Y += h + Pad;
        }

        public bool Button(string label, float height = 30f)
        {
            bool clicked = false;
            if (!Measuring) clicked = Widgets.ButtonText(new Rect(0f, Y, Width, height), label);
            Y += height + Pad;
            return clicked;
        }

        public bool ButtonLabeled(string label, string buttonText, float height = 28f)
        {
            bool clicked = false;
            float btnW = Mathf.Clamp(Width * 0.5f, 120f, 420f);
            if (!Measuring)
            {
                DialogLayout.LabelTrunc(new Rect(0f, Y, Mathf.Max(0f, Width - btnW - 8f), height), label, TextAnchor.MiddleLeft);
                clicked = Widgets.ButtonText(new Rect(Width - btnW, Y, btnW, height), buttonText);
            }
            Y += height + Pad;
            return clicked;
        }

        public string TextField(string label, string value, float labelW = 0f, float height = 28f)
        {
            if (labelW <= 0f) labelW = Mathf.Min(220f, Width * 0.45f);
            string result = value ?? "";
            if (!Measuring)
            {
                DialogLayout.LabelTrunc(new Rect(0f, Y, labelW, height), label, TextAnchor.MiddleLeft);
                result = Widgets.TextField(new Rect(labelW + 6f, Y, Mathf.Max(40f, Width - labelW - 6f), height), result);
            }
            Y += height + Pad;
            return result;
        }

        public string ColorRow(string label, string value, Color inUse, float height = 24f)
        {
            string result = value ?? "";
            if (!Measuring)
            {
                Widgets.Label(new Rect(0f, Y, 150f, height), label);
                Widgets.DrawBoxSolid(new Rect(156f, Y + 4f, 16f, 16f), inUse);
                result = Widgets.TextField(new Rect(180f, Y, Mathf.Min(110f, Mathf.Max(40f, Width - 184f)), height - 2f), result);
            }
            Y += height + Pad;
            return result;
        }
    }
}
