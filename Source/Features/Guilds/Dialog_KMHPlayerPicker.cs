using System;
using System.Collections.Generic;
using KMHPatch.Features.Guilds.Dto;
using KMHPatch.UI;
using UnityEngine;
using Verse;

namespace KMHPatch.Features.Guilds
{
    // Invite picker: searchable server roster of guildless players (online first, offline invitable too).
    public class Dialog_KMHPlayerPicker : Window_KMHBase
    {
        public override Vector2 InitialSize => new Vector2(460f, 560f);

        private readonly Action<string> _onPick;
        private string  _search = "";
        private Vector2 _scroll;

        public Dialog_KMHPlayerPicker(Action<string> onPick)
        {
            _onPick = onPick;
            doCloseX                = true;
            absorbInputAroundWindow = true;
            forcePause              = false;
            draggable               = true;

            EnableAutoRefresh(() => GuildHandler.RequestInvitables());
        }

        protected override void DrawContents(Rect rect)
        {
            float y = DialogLayout.DrawTitle(rect, "Invite a player");

            DialogLayout.LabelTrunc(new Rect(0f, y, rect.width, 20f),
                "<color=grey>Players not in a guild. Offline players keep the invite until they respond.</color>");
            y += 24f;

            _search = Widgets.TextField(new Rect(0f, y, rect.width - 118f, 28f), _search ?? "");
            if (Widgets.ButtonText(new Rect(rect.width - 110f, y, 110f, 28f), "By name…"))
            {
                Find.WindowStack.Add(new Dialog_KMHTextInput(
                    title:        "Invite by exact name",
                    confirmLabel: "Invite",
                    initial:      _search ?? "",
                    maxChars:     64,
                    rejectEmpty:  true,
                    onConfirm:    n => { _onPick?.Invoke(n); Close(); }));
            }
            y += 34f;

            List<InvitablePlayerDto> all = GuildHandler.Invitables;
            List<InvitablePlayerDto> shown = new List<InvitablePlayerDto>();
            string q = (_search ?? "").Trim();
            foreach (InvitablePlayerDto p in all)
                if (q.Length == 0 || p.Username.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)
                    shown.Add(p);

            Rect view = new Rect(0f, y, rect.width, rect.height - y - DialogLayout.FooterReserve);
            float rowH = 32f;
            Rect inner = new Rect(0f, 0f, view.width - 16f, Math.Max(shown.Count * rowH, view.height));
            Widgets.BeginScrollView(view, ref _scroll, inner);
            float yy = 0f;
            foreach (InvitablePlayerDto p in shown)
            {
                Rect row = new Rect(0f, yy, inner.width, rowH - 2f);
                if (yy % (rowH * 2) < rowH) Widgets.DrawLightHighlight(row);

                string dot = p.Online ? "<color=#7CD37C>●</color>" : "<color=grey>●</color>";
                DialogLayout.LabelTrunc(new Rect(6f, yy + 5f, inner.width - 100f, 22f),
                    $"{dot} {p.Username}" + (p.Online ? "" : "  <color=grey>(offline)</color>"));
                if (Widgets.ButtonText(new Rect(inner.width - 84f, yy + 2f, 80f, 26f), "Invite"))
                {
                    _onPick?.Invoke(p.Username);
                    Close();
                }
                yy += rowH;
            }
            if (shown.Count == 0)
                DialogLayout.LabelTrunc(new Rect(6f, 4f, inner.width - 12f, 22f),
                    all.Count == 0 ? "<color=grey>Loading players…</color>"
                                   : "<color=grey>No guildless player matches that search.</color>");
            Widgets.EndScrollView();

            if (DialogLayout.DrawCloseButton(rect)) Close();
        }
    }
}
