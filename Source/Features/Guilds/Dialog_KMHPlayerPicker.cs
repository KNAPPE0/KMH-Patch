using System;
using System.Collections.Generic;
using KMHPatch.Features.Guilds.Dto;
using KMHPatch.UI;
using UnityEngine;
using Verse;

namespace KMHPatch.Features.Guilds
{
    public class Dialog_KMHPlayerPicker : Window_KMHBase
    {
        public override Vector2 InitialSize => KMHPatch.UI.DialogLayout.FitToScreen(460f, 560f);

        private readonly Action<string> _onPick;
        private string  _search = "";
        private readonly UI.KmhFilteredView<InvitablePlayerDto> _view = new UI.KmhFilteredView<InvitablePlayerDto>();
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

            y += DialogLayout.LabelWrapped(0f, y, rect.width,
                "<color=#A9AFB9>Players not in a guild. Offline players keep the invite until they respond.</color>") + 4f;

            _search = DialogLayout.SearchField(new Rect(0f, y, rect.width - 118f, 28f), _search, "Search players");
            if (Widgets.ButtonText(new Rect(rect.width - 110f, y, 110f, 28f), "By name"))
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
            string q = (_search ?? "").Trim();
            List<InvitablePlayerDto> shown = _view.Get(all, q, () =>
            {
                List<InvitablePlayerDto> outList = new List<InvitablePlayerDto>();
                if (all != null)
                    foreach (InvitablePlayerDto p in all)
                        if (q.Length == 0 || p.Username.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)
                            outList.Add(p);
                return outList;
            });

            Rect view = new Rect(0f, y, rect.width, DialogLayout.BodyHeight(rect, y));
            float rowH = 32f;
            Rect inner = new Rect(0f, 0f, view.width - 16f, Math.Max(shown.Count * rowH, view.height));
            Widgets.BeginScrollView(view, ref _scroll, inner);
            float yy = 0f;
            foreach (InvitablePlayerDto p in shown)
            {
                Rect row = new Rect(0f, yy, inner.width, rowH - 2f);
                if (yy % (rowH * 2) < rowH) Widgets.DrawLightHighlight(row);

                string dot = p.Online ? "<color=#4cc38a>●</color>" : "<color=#A9AFB9>●</color>";
                DialogLayout.LabelTrunc(new Rect(6f, yy + 5f, inner.width - 100f, 22f),
                    $"{dot} {p.Username}" + (p.Online ? "" : "  <color=#A9AFB9>(offline)</color>"));
                if (Widgets.ButtonText(new Rect(inner.width - 84f, yy + 2f, 80f, 26f), "Invite"))
                {
                    _onPick?.Invoke(p.Username);
                    Close();
                }
                yy += rowH;
            }
            if (shown.Count == 0)
                DialogLayout.LabelTrunc(new Rect(6f, 4f, inner.width - 12f, 22f),
                    all.Count == 0 ? "<color=#A9AFB9>Loading players…</color>"
                                   : "<color=#A9AFB9>No guildless player matches that search.</color>");
            Widgets.EndScrollView();

            if (DialogLayout.DrawCloseButton(rect)) Close();
        }
    }
}
