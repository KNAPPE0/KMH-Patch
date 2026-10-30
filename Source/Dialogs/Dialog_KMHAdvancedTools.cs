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

            if (Tool(st, KMHTextures.Log, KmhText.Get("KMH_Report_Button", "Report a bug or suggestion"),
                    KmhText.Get("KMH_Report_ButtonTip", "Tell the server owner and the Colony Nexus team what went wrong or what you would like. A screenshot and diagnostics can go with it - you see both before anything is sent.")))
                Features.Reports.KmhReportClient.Open();

            if (Tool(st, KMHTextures.Log, KmhText.Get("KMH_MyReports_Title", "My reports"),
                    KmhText.Get("KMH_MyReports_Tip", "Every report sent from this game: whether it reached the server and the developers, and what the developers did with it.")))
                Features.Reports.Dialog_KMHMyReports.Open();

            if (Tool(st, KMHTextures.Log, "View Colony Nexus log",
                    "Open the in-game log viewer showing only Colony Nexus messages."))
                Find.WindowStack.Add(new Dialog_KMHLogViewer());

            if (Tool(st, KMHTextures.Treasury, "Storyteller wealth",
                    "What Colony Nexus adds to the wealth the storyteller reads, broken down by holding."))
                Dialog_KMHWealthDiagnostic.Open();

            // On request only: run during the handshake, its durable flushes froze the game while a socket waited.
            // A developer's tool: it resets client caches, so it is offered only with dev mode or debug logging on.
            if ((Prefs.DevMode || Diagnostics.KmhLog.DebugEnabled) && Tool(st, KMHTextures.Log, "Run Colony Nexus self-test",
                    "Checks Colony Nexus's own logic and writes the result to its log. Takes a moment and talks to nothing."))
                Diagnostics.KmhSelfTest.RunOnRequest();

            // Owner only: the server refuses everyone else, and a button that always refuses is worse than none.
            if (Features.Admin.Dialog_KMHServerConfig.AvailableToMe
                && Tool(st, KMHTextures.Log, "Server settings",
                        "Owner only. Read and change every server config; the server saves and reloads each one."))
                Features.Admin.Dialog_KMHServerConfig.Open();

            if (KmhDispatcher.IsKmhServer)
            {
                if (Linkable && Tool(st, KMHTextures.About, "Account & links",
                        "Discord is not linked yet. Manage every Colony Nexus link in one place."))
                    Dialog_KMHAccount.Open();

                if (Tool(st, KMHTextures.About, KmhText.Get("KMH_Servers_Title", "Colony Nexus servers"),
                        "Colony Nexus servers you've connected to, with their Colony Nexus and RimWorld Together versions."))
                    Find.WindowStack.Add(new Features.Servers.Dialog_KMHServers());

                if (Tool(st, KMHTextures.Ping, "Send a test ping",
                        "A diagnostic round trip through the Colony Nexus protocol. The result lands in the Colony Nexus log."))
                    KmhHandshakeHandler.SendPing();

                if (Tool(st, KMHTextures.About, "Config Enforcement",
                        "See which of your Mod Options this server locks, and restore your own settings."))
                    Find.WindowStack.Add(new Features.Enforcement.Dialog_KMHEnforcement());

                // Kept out of the KMH tab header, which the whole tab is sized around.
                if (!string.IsNullOrEmpty(KmhDispatcher.LastReceivedKind))
                {
                    System.TimeSpan since = System.DateTime.UtcNow - KmhDispatcher.LastReceivedAt;
                    st.Label($"<color=#A9AFB9>Last Colony Nexus packet: '{KmhDispatcher.LastReceivedKind}' " +
                             $"({(int)since.TotalSeconds}s ago, {KmhDispatcher.ReceivedCount} total this session)</color>");
                }
            }
            else
            {
                st.Label("<color=#A9AFB9>Connect to a Colony Nexus server for the rest of these tools.</color>");
            }

            // Duplicated from Mod Options on purpose: turning sharing off must not require leaving the game's UI.
            KMHPatchSettings s = KMHPatchMod.Settings;
            if (s == null) return;

            st.Gap(6f);
            st.Header(KmhText.Get("KMH_Perf_Header", "Performance"));
            foreach (string line in KmhPerfOverlay.PanelLines(Diagnostics.KmhPerfSampler.Read(),
                         KmhClientTransportStats.Snapshot(), Diagnostics.KmhMainThread.Backlog))
                st.Label(line);
            if (KmhDispatcher.IsKmhServer)
                st.Label($"<color=#A9AFB9>Server link: {KmhApiClient.LinkSummary}.</color>");
            bool overlay = s.ShowPerfOverlay;
            st.Checkbox(KmhText.Get("KMH_Perf_Overlay", "Show frame rate and game speed in the corner of the map"), ref s.ShowPerfOverlay,
                KmhText.Get("KMH_Perf_OverlayTip", "A small line over the map: frames per second, the slowest 1% of frames, ticks per second against the speed you picked, and what Colony Nexus itself costs each frame."));
            if (overlay != s.ShowPerfOverlay) KMHPatchMod.SaveSettings();
            bool animated = s.AnimatedVisuals, reduced = s.ReduceMotion, screen = s.ColonyNexusLoadingScreen, shaders = s.ShaderEffects;
            st.Checkbox(KmhText.Get("KMH_Visuals_LoadingScreen", "Colony Nexus loading screen"), ref s.ColonyNexusLoadingScreen,
                KmhText.Get("KMH_Visuals_LoadingScreenTip", "While joining or leaving a Colony Nexus server, the loading screen shows Colony Nexus, with real progress where RimWorld reports it. Off keeps RimWorld's own loading screen."));
            st.Checkbox(KmhText.Get("KMH_Visuals_Animated", "Animated Colony Nexus visuals"), ref s.AnimatedVisuals,
                KmhText.Get("KMH_Visuals_AnimatedTip", "The moving parts of Colony Nexus loading screens: the orbiting moon, the glow, the stars and the progress bars. Off shows them standing still."));
            st.Checkbox(KmhText.Get("KMH_Visuals_ReduceMotion", "Reduce motion"), ref s.ReduceMotion,
                KmhText.Get("KMH_Visuals_ReduceMotionTip", "Keeps the progress bars moving but stops the orbiting moon, the pulsing glow and the twinkling stars."));
            if (Compat.KmhGameVersion.IsCoreClr)
                st.Checkbox(KmhText.Get("KMH_Visuals_Shaders", "RimWorld 1.7 shader effects (experimental)"), ref s.ShaderEffects,
                    KmhText.Get("KMH_Visuals_ShadersTip", "Draws the light that crosses the Colony Nexus name with RimWorld 1.7's own graphics engine. Off draws the same effect without it."));
            if (animated != s.AnimatedVisuals || reduced != s.ReduceMotion || screen != s.ColonyNexusLoadingScreen || shaders != s.ShaderEffects)
                KMHPatchMod.SaveSettings();
            if (st.Button(KmhText.Get("KMH_Load_PreviewButton", "Preview the loading screen")))
                Find.WindowStack.Add(new Dialog_KMHLoadingPreview());

            bool offer = s.OfferErrorReports;
            st.Checkbox(KmhText.Get("KMH_ErrorOffer_Setting", "Offer to report Colony Nexus errors"), ref s.OfferErrorReports,
                KmhText.Get("KMH_ErrorOffer_SettingTip", "When Colony Nexus itself hits an error, a small window offers a report already filled in. Once per kind of error, at most three times a session."));
            if (offer != s.OfferErrorReports) KMHPatchMod.SaveSettings();

            st.Gap(6f);
            st.Header("Log sharing");

            if (Diagnostics.KmhDebugUplink.Active)
            {
                st.Label("<color=#f0c04f>You are sharing your Colony Nexus log with this server right now.</color>");
                if (st.Button("Stop sharing my Colony Nexus log"))
                    Diagnostics.KmhDebugUplink.StopSharing();
                st.Gap(4f);
            }

            bool prevDebug = s.DebugLogging;
            st.Checkbox("Verbose debug logging", ref s.DebugLogging,
                "Logs detailed Colony Nexus diagnostics and per-packet protocol traces. Off by default so normal play stays quiet.");
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
                ? "<color=#A9AFB9>Nothing is ever sent, and you are not prompted.</color>"
                : mode == KMHPatchSettings.LogShareAutomatic
                    ? "<color=#A9AFB9>Sharing starts as soon as a server asks. It ends when you disconnect, or when you stop it here.</color>"
                    : "<color=#A9AFB9>You get one prompt per server, per session. Nothing is sent unless you say yes, and that answer is dropped when you disconnect.</color>");
            st.Label("<color=#A9AFB9>Colony Nexus log lines only - never chat, saves, or hardware/system information.</color>");

            if (st.Button("Open Colony Nexus log folder")) Application.OpenURL(Diagnostics.KmhLog.LogFolderPath);
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
