using System.Collections.Generic;
using KMHPatch.SubProtocol;
using KMHPatch.UI;
using UnityEngine;
using Verse;

namespace KMHPatch.Dialogs
{
    // A window, not a KMH-tab section: a section that changes the tab's measured height feeds back and stops drawing.
    public class Dialog_KMHAdvancedTools : Window_KMHBase
    {
        public override Vector2 InitialSize => KMHPatch.UI.DialogLayout.FitToScreen(560f, 560f);

        public Dialog_KMHAdvancedTools()
        {
            doCloseX                = true;
            absorbInputAroundWindow = true;
            forcePause              = false;
            draggable               = true;
            resizeable              = true;
        }

        public static void Open() => Find.WindowStack.Add(new Dialog_KMHAdvancedTools());

        private Vector2 _scroll;

        protected override void DrawContents(Rect rect)
        {
            float y = DialogLayout.DrawTitle(rect, "Advanced & tools");
            DialogLayout.DrawSectionDivider(rect, ref y);

            Rect box   = new Rect(0f, y, rect.width, DialogLayout.BodyHeight(rect, y));
            float viewW = Mathf.Max(1f, box.width - DialogLayout.ScrollbarReserveWidth);

            KmhStack measure = new KmhStack(viewW, true);
            Body(measure);

            Rect view = new Rect(0f, 0f, viewW, Mathf.Max(measure.Y + 8f, box.height));
            Widgets.BeginScrollView(box, ref _scroll, view);
            Body(new KmhStack(viewW, false));
            Widgets.EndScrollView();

            if (DialogLayout.DrawCloseButton(rect)) Close();
        }

        private void Body(KmhStack st)
        {
            st.Header("Tools");

            if (Tool(st, KMHTextures.Log, "View KMH log",
                    "Open the in-game log viewer scoped to KMH messages."))
                Find.WindowStack.Add(new Dialog_KMHLogViewer());

            if (Tool(st, KMHTextures.Treasury, "Storyteller wealth",
                    "What KMH adds to the wealth the storyteller reads, broken down by holding."))
                Dialog_KMHWealthDiagnostic.Open();

            if (KmhDispatcher.IsKmhServer)
            {
                if (Linkable && Tool(st, KMHTextures.About, "Link Discord",
                        "Get a one-time code to link your Discord account - no chat commands needed."))
                    Features.LinkedAccounts.LinkedAccountsHandler.RequestCode();

                if (Tool(st, KMHTextures.About, "KMH Servers",
                        "KMH servers you've connected to, with their KMH and RWT versions."))
                    Find.WindowStack.Add(new Features.Servers.Dialog_KMHServers());

                if (Tool(st, KMHTextures.Ping, "Send KMH ping",
                        "Diagnostic round-trip through the KMH sub-protocol. Result lands in the KMH log."))
                    KmhHandshakeHandler.SendPing();

                if (Tool(st, KMHTextures.About, "Config Enforcement",
                        "See which of your Mod Options this server locks, and restore your own settings."))
                    Find.WindowStack.Add(new Features.Enforcement.Dialog_KMHEnforcement());
            }
            else
            {
                st.Label("<color=grey>Connect to a KMH server for the rest of these tools.</color>");
            }

            // Duplicated from Mod Options on purpose: turning sharing off must not require leaving the game's UI.
            KMHPatchSettings s = KMHPatchMod.Settings;
            if (s == null) return;

            st.Gap(6f);
            st.Header("Log sharing");

            if (Diagnostics.KmhDebugUplink.Active)
            {
                st.Label("<color=#E2C16B>You are sharing your KMH log with this server right now.</color>");
                if (st.Button("Stop sharing my KMH log"))
                    Diagnostics.KmhDebugUplink.StopSharing();
                st.Gap(4f);
            }

            bool prevDebug = s.DebugLogging;
            st.Checkbox("Verbose debug logging", ref s.DebugLogging,
                "Logs detailed KMH diagnostics and per-packet protocol traces. Off by default so normal play stays quiet.");
            if (prevDebug != s.DebugLogging) { Diagnostics.KmhLog.DebugEnabled = s.DebugLogging; KMHPatchMod.SaveSettings(); }

            int mode = s.LogSharingMode;
            string modeName = mode == KMHPatchSettings.LogShareNever     ? "Never"
                            : mode == KMHPatchSettings.LogShareAutomatic ? "Automatically share"
                            :                                              "Ask me each server";
            if (st.ButtonLabeled("When a server asks for my log", modeName))
                Find.WindowStack.Add(new FloatMenu(new List<FloatMenuOption>
                {
                    new FloatMenuOption("Never - refuse without asking me",
                        () => { s.LogSharingMode = KMHPatchSettings.LogShareNever; s.ShareDebugLogsWithServer = false; KMHPatchMod.SaveSettings(); }),
                    new FloatMenuOption("Ask me each server (recommended)",
                        () => { s.LogSharingMode = KMHPatchSettings.LogShareAsk; s.ShareDebugLogsWithServer = false; KMHPatchMod.SaveSettings(); }),
                    new FloatMenuOption("Automatically share with any server that asks",
                        () => { s.LogSharingMode = KMHPatchSettings.LogShareAutomatic; s.ShareDebugLogsWithServer = true; KMHPatchMod.SaveSettings(); }),
                }));
            st.Label(mode == KMHPatchSettings.LogShareNever
                ? "<color=grey>Nothing is ever sent, and you are not prompted.</color>"
                : mode == KMHPatchSettings.LogShareAutomatic
                    ? "<color=grey>Sharing starts as soon as a server asks. It ends when you disconnect, or when you stop it here.</color>"
                    : "<color=grey>You get one prompt per server, per session. Nothing is sent unless you say yes, and that answer is dropped when you disconnect.</color>");
            st.Label("<color=grey>KMH log lines only - never chat, saves, or hardware/system information.</color>");

            if (st.Button("Open KMH log folder")) Application.OpenURL(Diagnostics.KmhLog.LogFolderPath);
        }

        private static bool Linkable
            => KmhDispatcher.IsKmhServer && !Features.LinkedAccounts.LinkedAccountsCache.IsLinked(KmhSession.Me);

        private static bool Tool(KmhStack st, Texture2D icon, string label, string tip)
        {
            Rect r = st.Row(36f);
            if (st.Measuring) return false;
            return IconButton.Draw(new Rect(r.x, r.y, r.width, 30f), icon, label, tip);
        }
    }
}
