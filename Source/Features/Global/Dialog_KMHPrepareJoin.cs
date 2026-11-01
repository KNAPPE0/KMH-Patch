using System;
using System.Collections.Generic;
using KMHPatch.Features.Mods;
using KMHPatch.UI;
using RimWorld;
using UnityEngine;
using Verse;

namespace KMHPatch.Features.Global
{
    internal sealed class Dialog_KMHPrepareJoin : Window
    {
        private readonly ServerCardDto _review;
        private readonly HashSet<string> _optional = new HashSet<string>(StringComparer.Ordinal);
        private bool _acceptUnverified;
        private Vector2 _scroll;
        private float _contentH = 400f;

        public Dialog_KMHPrepareJoin() : this(null) { }

        public Dialog_KMHPrepareJoin(ServerCardDto review)
        {
            _review = review;
            doCloseX = true;
            forcePause = false;
            absorbInputAroundWindow = false;
            closeOnAccept = false;
            closeOnClickedOutside = false;
            draggable = true;
            resizeable = true;
        }

        public override Vector2 InitialSize => DialogLayout.FitToScreen(640f, 560f);

        private bool Reviewing => _review != null && !KmhJoinPreparation.IsFor(_review.ServerId);

        public override void DoWindowContents(Rect inRect)
        {
            windowRect.width = Mathf.Max(windowRect.width, 460f);
            windowRect.height = Mathf.Max(windowRect.height, 360f);

            Text.Font = GameFont.Medium;
            string name = Reviewing ? _review.Name : KmhJoinPreparation.ServerName;
            string title = KmhText.Get("KMH_Prep_Title", "Prepare to join {0}", KmhDisplayText.Inert(name ?? "", 48));
            Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width - 30f, 34f), title.Truncate(inRect.width - 30f));
            Text.Font = GameFont.Small;

            Rect body = new Rect(inRect.x, inRect.y + 40f, inRect.width, inRect.height - 40f - 44f);
            Rect footer = new Rect(inRect.x, inRect.yMax - 36f, inRect.width, 34f);
            if (Reviewing) { DrawReview(body); DrawReviewButtons(footer); FitToReview(); }
            else { DrawProgress(body); DrawProgressButtons(footer); }

            // Auto-closes after joining, or it stays open over RimWorld Together's colony pages.
            if (!Reviewing && KmhJoinPreparation.Finished)
            {
                if (_finishedAt < 0f) _finishedAt = Time.realtimeSinceStartup;
                else if (Time.realtimeSinceStartup - _finishedAt > 3f) Close(false);
            }
        }

        private float _finishedAt = -1f;

        private void DrawReview(Rect rect)
        {
            ModProfileClient.EnsureFetched(_review);
            ModProfileClient.Entry fetched = ModProfileClient.Peek(_review.ServerId);
            ModComparison c = fetched?.Comparison;
            ModProfileDto profile = fetched?.Profile;
            float w = rect.width - 20f;
            Rect view = new Rect(0f, 0f, w, _contentH);
            Widgets.BeginScrollView(rect, ref _scroll, view);
            float y = 0f;

            y = Para(y, w, KmhText.Get("KMH_Prep_ReviewIntro", "Colony Nexus can set this game up for the server and join it. This is what it will do:"));

            if (!KmhRwtVersions.Supports(_review.RwtVersion))
            {
                y = Heading(y, w, KmhText.Get("KMH_Prep_ReviewRwt", "RimWorld Together"));
                string mine = string.IsNullOrEmpty(KmhRwtVersions.Mine) ? "?" : KmhRwtVersions.Mine;
                switch (Features.Rwt.RwtCopies.DecideForDisplay(_review.RwtVersion))
                {
                    case Features.Rwt.RwtSwitchKind.UseWorkshop:
                        // The copy in Mods may be the player's own (RWT's own updater writes one), not KMH's.
                        y = Features.Rwt.RwtCopies.IsKmhCopy(Features.Rwt.RwtCopies.LocalCopy)
                            ? Para(y, w, KmhText.Get("KMH_Prep_ReviewRwtWorkshop", "Switch back to your Steam Workshop copy, which is {0} (you are running {1}). Nothing to download: the copy Colony Nexus put in your Mods folder is set aside.", _review.RwtVersion, mine))
                            : Para(y, w, KmhText.Get("KMH_Prep_ReviewRwtWorkshopOwn", "Switch back to your Steam Workshop copy, which is {0} (you are running {1}). Nothing to download: your own copy in the Mods folder is set aside, and \"Restore my mod list\" puts it back.", _review.RwtVersion, mine));
                        break;
                    case Features.Rwt.RwtSwitchKind.UsePrevious:
                        y = Para(y, w, KmhText.Get("KMH_Prep_ReviewRwtPrevious", "Switch to RimWorld Together {0}, which Colony Nexus installed for another server before (you are running {1}). Nothing to download.", _review.RwtVersion, mine));
                        break;
                    case Features.Rwt.RwtSwitchKind.RestartOnly:
                        y = Para(y, w, KmhText.Get("KMH_Prep_ReviewRwtStaged", "RimWorld Together {0} is already installed and loads after a restart (you are running {1}).", _review.RwtVersion, mine));
                        break;
                    default:
                        y = Para(y, w, KmhText.Get("KMH_Prep_ReviewRwtBody", "Download RimWorld Together {0} from its official release page and install it in your Mods folder (you have {1}). Your Steam Workshop copy is not touched.",
                            _review.RwtVersion, mine));
                        y = Para(y, w, KmhText.Get("KMH_Prep_ReviewRwtWhile", "While it is in your Mods folder, RimWorld loads it in every mod list. \"Restore my mod list\", or preparing a server on your Workshop version, switches back without downloading."), dim: true);
                        Features.Rwt.RwtReleaseCatalog.EnsureLoaded();
                        Features.Rwt.RwtReleaseCatalog.EnsureExact(_review.RwtVersion);
                        Features.Rwt.RwtInstallPlan plan = Features.Rwt.RwtResolver.PlanClient(Features.Rwt.RwtVersion.From(_review.RwtVersion), Features.Rwt.RwtVersion.From(KmhRwtVersions.Mine));
                        if (plan.CanInstall && plan.Unusual) y = Para(y, w, plan.UnusualText(), warn: true);
                        else if (plan.CanInstall && plan.Release.PublishedAt != default)
                            y = Para(y, w, KmhText.Get("KMH_Prep_RwtPublished", "Published on GitHub on {0}.", plan.Release.PublishedAt.UtcDateTime.ToString("yyyy-MM-dd")), dim: true);
                        if (plan.CanInstall && plan.NeedsOptIn)
                        {
                            string ask = plan.Verifiable
                                ? KmhText.Get("KMH_Prep_ReviewRwtOptIn", "Install this RimWorld Together version anyway")
                                : KmhText.Get("KMH_Prep_ReviewUnverified", "Install it although GitHub published no checksum for this older release");
                            Rect cb = new Rect(0f, y, w, Text.CalcHeight(ask, w - 30f) + 4f);
                            Widgets.CheckboxLabeled(cb, ask, ref _acceptUnverified);
                            y += cb.height + 4f;
                        }
                        break;
                }
            }

            if (c != null)
            {
                List<ModEntryDto> subscribe = c.Subscribable();
                KmhWorkshopDetails.Ensure(WorkshopIds(subscribe, c.OptionalSubscribable()));
                if (subscribe.Count > 0)
                {
                    y = Heading(y, w, KmhText.Get("KMH_Prep_ReviewSubscribe", "Subscribe on the Steam Workshop ({0})", subscribe.Count));
                    foreach (string line in SubscribeLines(subscribe)) y = Para(y, w, "  • " + line, warn: line.StartsWith("!", StringComparison.Ordinal));
                    y = Para(y, w, KmhText.Get("KMH_Browser_Subscribe_IdsNote", "The names come from the server. Steam subscribes to the Workshop numbers, which you can look up on Steam first."), dim: true);
                    if (KmhWorkshopDetails.LastFailed)
                        y = Para(y, w, KmhText.Get("KMH_Prep_SteamTitlesUnread", "Steam's own titles for these items could not be read, so only the server's names are shown. Check the Workshop numbers on Steam before you start."), warn: true);
                    if (!KmhWorkshop.Available)
                        y = Para(y, w, KmhText.Get("KMH_Prep_ReviewNoSteam", "Steam is not running or not signed in. Start it before you begin, or preparation stops at this step."), warn: true);
                }

                if (c.Shadowing.Count > 0)
                {
                    y = Heading(y, w, KmhText.Get("KMH_Prep_ReviewUnsubscribe", "Unsubscribe on the Steam Workshop ({0})", c.Shadowing.Count));
                    foreach (ModShadow s in c.Shadowing)
                        y = Para(y, w, "  • " + KmhText.Get("KMH_Prep_ReviewShadow", "{0}  (Workshop {1}) has the same mod id as {2} (Workshop {3}), which this server uses. RimWorld loads only one of them, and it can be this one.",
                            KmhDisplayText.Inert(s.Copy.Name, 64), KmhDisplayText.Inert(s.Copy.WorkshopId, 20), KmhDisplayText.Inert(s.Server.Name, 64), KmhDisplayText.Inert(s.Server.Id, 20)));
                    y = Para(y, w, KmhText.Get("KMH_Prep_ReviewUnsubscribeNote", "Only once the server's copy is on disk. You can subscribe to it again from its Workshop page at any time."), dim: true);
                    if (!KmhWorkshop.Available && c.Subscribable().Count == 0)
                        y = Para(y, w, KmhText.Get("KMH_Prep_ReviewNoSteam", "Steam is not running or not signed in. Start it before you begin, or preparation stops at this step."), warn: true);
                }

                List<ModEntryDto> optional = OptionalChoices(c, profile);
                if (optional.Count > 0)
                {
                    y = Heading(y, w, KmhText.Get("KMH_Prep_ReviewOptional", "Optional mods this server allows (only if you tick them)"));
                    foreach (ModEntryDto e in optional)
                    {
                        string key = KmhModPlan.OptInKey(e);
                        bool on = _optional.Contains(key);
                        // A Workshop item Steam refuses cannot be ticked; one not yet checked waits for Steam.
                        KmhWorkshopDetails.Item d = e.Subscribable ? KmhWorkshopDetails.Get(e.Id) : null;
                        bool locked = d != null ? d.Refused : e.Subscribable && !KmhWorkshopDetails.LastFailed;
                        string label = e.Subscribable ? SubscribeLine(e, d) : KmhDisplayText.Inert(e.Name, 64);
                        Rect cb = new Rect(0f, y, w, Mathf.Max(26f, Text.CalcHeight(label, w - 30f)));
                        if (locked) GUI.color = new Color(1f, 1f, 1f, 0.45f);
                        Widgets.CheckboxLabeled(cb, label, ref on);
                        GUI.color = Color.white;
                        if (on && !locked) _optional.Add(key); else _optional.Remove(key);
                        y += cb.height + 2f;
                    }
                }

                if (c.MissingDisabled.Count > 0)
                {
                    y = Heading(y, w, KmhText.Get("KMH_Prep_ReviewEnable", "Switch on (already installed)"));
                    y = Para(y, w, NameList(c.MissingDisabled));
                }
                if (c.Disallowed.Count > 0)
                {
                    y = Heading(y, w, KmhText.Get("KMH_Prep_ReviewDisable", "Switch off (this server does not allow them)"));
                    y = Para(y, w, string.Join(", ", Inert(c.Disallowed).ToArray()));
                }
                if (c.OrderMismatch)
                    y = Para(y, w, KmhText.Get("KMH_Prep_ReviewOrder", "Set your load order to the server's."));
                if (c.MissingExpansions.Count > 0)
                    y = Para(y, w, KmhText.Get("KMH_Prep_ReviewExpansion", "Cannot be fixed here - an expansion you do not own: {0}", NameList(c.MissingExpansions)), warn: true);
                if (c.MissingLocal.Count > 0)
                    y = Para(y, w, KmhText.Get("KMH_Prep_Manual", "These mods are not on the Steam Workshop and have to be installed by hand: {0}", NameList(c.MissingLocal)), warn: true);
            }
            else if (!ModPolicyNames.EnforcesNothing(_review.ModPolicy))
                y = Para(y, w, KmhText.Get("KMH_Prep_ReviewNoList", "The server's mod list could not be read yet. Colony Nexus reads it again when preparation starts."), dim: true);

            y = Heading(y, w, KmhText.Get("KMH_Prep_ReviewThen", "Then"));
            y = Para(y, w, KmhText.Get("KMH_Prep_ReviewRestart", "RimWorld restarts to load the new setup (a second time only if newly downloaded mods have to be found first). Colony Nexus checks what actually loaded, then joins after a 5-second countdown you can stop."));
            y = Para(y, w, KmhText.Get("KMH_Prep_ReviewKeep", "Your current mod list is kept. \"Restore my mod list\" in the browser puts it back at any time."), dim: true);

            _reviewNeed = y + 10f;
            _contentH = Mathf.Max(_reviewNeed, rect.height);
            Widgets.EndScrollView();
        }

        private float _reviewNeed, _fittedTo = -1f;

        private void FitToReview()
        {
            if (_reviewNeed <= 0f || Mathf.Abs(_reviewNeed - _fittedTo) <= 1f) return;
            _fittedTo = _reviewNeed;
            float want = Mathf.Clamp(_reviewNeed + 40f + 44f + Margin * 2f, 360f, Verse.UI.screenHeight - 32f);
            if (Mathf.Abs(want - windowRect.height) < 2f) return;
            float centre = windowRect.center.y;
            windowRect.height = want;
            windowRect.y = Mathf.Clamp(centre - want / 2f, 0f, Mathf.Max(0f, Verse.UI.screenHeight - want));
        }

        // Names are server-supplied but Steam acts on ids, so each id is shown to expose a familiar name on another item.
        internal static List<string> SubscribeLines(List<ModEntryDto> mods)
        {
            List<string> lines = new List<string>();
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (ModEntryDto e in mods)
                if (e != null && seen.Add(e.Id ?? ""))
                    lines.Add(SubscribeLine(e, KmhWorkshopDetails.Get(e.Id)));
            return lines;
        }

        // Steam's title beside the server's name; a line that starts with "!" is drawn as a warning.
        internal static string SubscribeLine(ModEntryDto e, KmhWorkshopDetails.Item d)
        {
            string line = KmhText.Get("KMH_Browser_Subscribe_Item", "{0}  (Workshop {1})", KmhDisplayText.Inert(e.Name, 64), KmhDisplayText.Inert(e.Id, 20));
            if (d == null) return line;
            if (d.Refused)
                return "! " + line + " - " + KmhText.Get("KMH_Prep_SteamRefused", "Steam does not list this number as a RimWorld mod");
            List<string> flags = new List<string>();
            if (KmhWorkshopDetails.NameDiffers(e.Name, d.Title))
                flags.Add(KmhText.Get("KMH_Prep_SteamTitle", "Steam calls it \"{0}\"", KmhDisplayText.Inert(d.Title, 64)));
            if (d.Unlisted) flags.Add(KmhText.Get("KMH_Prep_SteamUnlisted", "not public on the Workshop"));
            if (d.IsNew(DateTime.UtcNow)) flags.Add(KmhText.Get("KMH_Prep_SteamNew", "published less than two weeks ago"));
            return flags.Count == 0 ? line : "! " + line + " - " + string.Join("; ", flags.ToArray());
        }

        private static List<string> WorkshopIds(params List<ModEntryDto>[] lists)
        {
            List<string> ids = new List<string>();
            foreach (List<ModEntryDto> list in lists)
                foreach (ModEntryDto e in list) if (e != null && e.Subscribable) ids.Add(e.Id);
            return ids;
        }

        // Consent covers Steam's own record of each required item, so Start waits for it and refuses one Steam refuses.
        internal static string WorkshopRefusal(List<ModEntryDto> required)
        {
            List<string> ids = WorkshopIds(required);
            if (ids.Count == 0) return null;
            if (KmhWorkshopDetails.Pending(ids))
                return KmhText.Get("KMH_Prep_WaitForSteam", "Checking the Workshop items with Steam...");
            foreach (ModEntryDto e in required)
            {
                KmhWorkshopDetails.Item d = e != null && e.Subscribable ? KmhWorkshopDetails.Get(e.Id) : null;
                if (d != null && d.Refused)
                    return KmhText.Get("KMH_Prep_WorkshopRefused", "Steam does not list {0} (Workshop {1}) as a RimWorld mod, so it cannot be subscribed.", KmhDisplayText.Inert(e.Name, 48), KmhDisplayText.Inert(e.Id, 20));
            }
            return null;
        }

        private static List<ModEntryDto> OptionalChoices(ModComparison c, ModProfileDto profile)
        {
            List<ModEntryDto> list = new List<ModEntryDto>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ModEntryDto e in c.OptionalSubscribable()) if (seen.Add(e.Name)) list.Add(e);
            if (profile != null)
                foreach (string n in c.OptionalNotInstalled)
                {
                    ModEntryDto e = profile.EntryFor(n);
                    if (e != null && seen.Add(e.Name) && ModComparison.IsInstalled(e, KmhLocalMods.InstalledCached())) list.Add(e);
                }
            return list;
        }

        private void DrawReviewButtons(Rect r)
        {
            string refusal = KmhJoinPreparation.RefusalToStart();
            if (KmhJoinPreparation.Active && !KmhJoinPreparation.IsFor(_review.ServerId))
                refusal = KmhText.Get("KMH_Prep_Busy", "Colony Nexus is already preparing {0}. Cancel that first.", KmhDisplayText.Inert(KmhJoinPreparation.ServerName, 48));
            // Consent covers what was shown, so it cannot be given before the server's list has been read and shown.
            ModProfileClient.Entry fetched = ModProfileClient.Peek(_review.ServerId);
            if (refusal == null && !ModPolicyNames.EnforcesNothing(_review.ModPolicy) && (fetched == null || fetched.Pending || fetched.Comparison == null))
                refusal = KmhText.Get("KMH_Prep_WaitForList", "Waiting for the server's mod list, so you can see what would change before agreeing to it.");
            if (refusal == null && fetched?.Comparison != null)
                refusal = WorkshopRefusal(fetched.Comparison.Subscribable());

            string startLabel = KmhText.Get("KMH_Prep_Start", "Prepare and join"), cancelLabel = KmhText.Get("KMH_Prep_Cancel", "Cancel");
            float startW = DialogLayout.ButtonW(startLabel, 220f, r.width * 0.45f), cancelW = DialogLayout.ButtonW(cancelLabel, 120f, r.width * 0.25f);
            Rect start = new Rect(r.xMax - startW, r.y, startW, r.height);
            Rect cancel = new Rect(start.x - cancelW - 10f, r.y, cancelW, r.height);
            if (Widgets.ButtonText(cancel, cancelLabel)) Close();
            if (refusal != null)
            {
                GUI.color = new Color(1f, 1f, 1f, 0.45f);
                Widgets.ButtonText(start, startLabel, active: false);
                GUI.color = Color.white;
                TooltipHandler.TipRegion(start, refusal);
                Text.Font = GameFont.Tiny;
                GUI.color = new Color(0.9f, 0.7f, 0.45f);
                DialogLayout.LabelFit(new Rect(r.x, r.y, Mathf.Max(0f, cancel.x - r.x - 8f), r.height), refusal);
                GUI.color = Color.white;
                Text.Font = GameFont.Small;
                return;
            }
            if (Widgets.ButtonText(start, startLabel))
                KmhJoinPreparation.Start(_review, new List<string>(_optional), _acceptUnverified, fetched?.Profile, ShownToGo(fetched?.Comparison));
        }

        // Exactly the copies the review listed under Unsubscribe; one found later is asked about on its own.
        internal static List<string> ShownToGo(ModComparison c)
        {
            List<string> ids = new List<string>();
            if (c != null) foreach (ModShadow s in c.Shadowing) if (!ids.Contains(s.Copy.WorkshopId)) ids.Add(s.Copy.WorkshopId);
            return ids;
        }

        private void DrawProgress(Rect rect)
        {
            if (!KmhJoinPreparation.Failed && (KmhJoinPreparation.Active || KmhJoinPreparation.Finished))
            {
                float h = KmhLoadingPanel.Height(rect.width, true);
                DrawPanel(new Rect(rect.x, rect.y, rect.width, h));
                rect.yMin += h + 8f;
            }
            float w = rect.width - 20f;
            Rect view = new Rect(0f, 0f, w, _contentH);
            Widgets.BeginScrollView(rect, ref _scroll, view);
            float y = 0f;
            string head = KmhJoinPreparation.Headline;
            if (!string.IsNullOrEmpty(head))
                y = Para(y, w, head, warn: KmhJoinPreparation.Failed);
            y += 4f;
            foreach (PrepLine line in KmhJoinPreparation.Lines) y = DrawLine(y, w, line);
            _contentH = Mathf.Max(y + 10f, rect.height);
            Widgets.EndScrollView();
        }

        private static void DrawPanel(Rect r)
        {
            float progress = Overall(KmhJoinPreparation.Lines, out PrepLine current, out int index, out int total);
            string status = KmhJoinPreparation.Finished ? KmhText.Get("KMH_Load_Joined", "Joined")
                          : current != null ? current.Label : KmhJoinPreparation.Headline;
            string detail = current != null ? KmhText.Get("KMH_Load_Step", "Step {0} of {1}", index, total) : null;
            KmhLoadingPanel.Draw(r, status, progress, detail);
        }

        internal static float Overall(IEnumerable<PrepLine> lines, out PrepLine current, out int index, out int total)
        {
            int done = 0;
            total = 0; index = 0; current = null;
            foreach (PrepLine line in lines)
            {
                total++;
                if (line.State == PrepLineState.Done || line.State == PrepLineState.Skipped) done++;
                else if (current == null && (line.State == PrepLineState.Working || line.State == PrepLineState.Waiting)) { current = line; index = total; }
            }
            float share = current != null && current.Progress >= 0f ? Mathf.Clamp01(current.Progress) : 0f;
            return total == 0 ? -1f : Mathf.Clamp01((done + share) / total);
        }

        private static float DrawLine(float y, float w, PrepLine line)
        {
            string mark;
            Color colour;
            switch (line.State)
            {
                case PrepLineState.Done:    mark = "✓"; colour = new Color(0.55f, 0.85f, 0.55f); break;
                case PrepLineState.Skipped: mark = "–"; colour = new Color(0.6f, 0.6f, 0.6f); break;
                case PrepLineState.Working: mark = "…"; colour = new Color(0.9f, 0.85f, 0.55f); break;
                case PrepLineState.Waiting: mark = "||"; colour = new Color(0.9f, 0.75f, 0.45f); break;
                case PrepLineState.Failed:  mark = "✗"; colour = new Color(0.95f, 0.45f, 0.45f); break;
                default:                    mark = "○"; colour = new Color(0.6f, 0.6f, 0.6f); break;
            }
            GUI.color = colour;
            Widgets.Label(new Rect(0f, y, 22f, 24f), mark);
            GUI.color = line.State == PrepLineState.Pending ? new Color(0.7f, 0.7f, 0.7f) : Color.white;
            Widgets.Label(new Rect(24f, y, w - 24f, 24f), line.Label.Truncate(w - 24f));
            GUI.color = Color.white;
            y += 24f;
            if (!string.IsNullOrEmpty(line.Detail))
            {
                Text.Font = GameFont.Tiny;
                float h = Text.CalcHeight(line.Detail, w - 24f);
                GUI.color = new Color(0.78f, 0.78f, 0.78f);
                Widgets.Label(new Rect(24f, y, w - 24f, h), line.Detail);
                GUI.color = Color.white;
                Text.Font = GameFont.Small;
                y += h + 2f;
            }
            if (line.State == PrepLineState.Working && line.Progress >= 0f)
            {
                DialogLayout.DrawProgress(new Rect(24f, y + 3f, w - 24f, 5f), line.Progress);
                y += 14f;
            }
            return y + 6f;
        }

        private void DrawProgressButtons(Rect r)
        {
            float x = r.xMax;
            Rect Next(float width) { x -= width; Rect b = new Rect(x, r.y, width, r.height); x -= 8f; return b; }
            // Measured from each label with the old width as a floor so longer translations fit their buttons.
            Rect NextFor(string label, float min) => Next(DialogLayout.ButtonW(label, min, r.width * 0.4f));
            string restore = KmhText.Get("KMH_Prep_Restore", "Restore my mod list");

            if (KmhJoinPreparation.Finished || !KmhJoinPreparation.Active)
            {
                string close = KmhText.Get("KMH_Prep_Close", "Close");
                if (Widgets.ButtonText(NextFor(close, 120f), close)) Close();
                DrawRestore(NextFor(restore, 200f), restartAfter: true);
                return;
            }

            if (KmhJoinPreparation.Failed)
            {
                string retry = KmhText.Get("KMH_Prep_Retry", "Retry");
                if (Widgets.ButtonText(NextFor(retry, 120f), retry)) KmhJoinPreparation.Retry();
            }
            else if (KmhJoinPreparation.AwaitingRestart)
            {
                string restartNow = KmhText.Get("KMH_Prep_RestartNow", "Restart now");
                if (Widgets.ButtonText(NextFor(restartNow, 160f), restartNow)) KmhJoinPreparation.RestartNow();
            }
            else if (KmhJoinPreparation.AwaitingUnsubscribe.Count > 0)
            {
                string unsubscribe = KmhText.Get("KMH_Prep_UnsubscribeGo", "Unsubscribe and carry on");
                if (Widgets.ButtonText(NextFor(unsubscribe, 200f), unsubscribe)) KmhJoinPreparation.ApproveUnsubscribe();
            }
            else if (KmhJoinPreparation.JoinCountdownLeft >= 0)
            {
                string joinNow = KmhText.Get("KMH_Prep_JoinNow", "Join now"), dontJoin = KmhText.Get("KMH_Prep_DontJoin", "Don't join yet");
                if (Widgets.ButtonText(NextFor(joinNow, 120f), joinNow)) KmhJoinPreparation.JoinNowInstead();
                if (Widgets.ButtonText(NextFor(dontJoin, 150f), dontJoin)) KmhJoinPreparation.StopAutoJoin();
            }

            string cancel = KmhText.Get("KMH_Prep_Cancel", "Cancel");
            if (Widgets.ButtonText(NextFor(cancel, 110f), cancel)) KmhJoinPreparation.Cancel();
            if (KmhJoinPreparation.Failed) DrawRestore(NextFor(restore, 200f), restartAfter: false);
            Rect hide = new Rect(r.x, r.y, Mathf.Max(80f, x - r.x), r.height);
            if (hide.width >= 80f && Widgets.ButtonText(new Rect(r.x, r.y, 100f, r.height), KmhText.Get("KMH_Prep_Hide", "Hide")))
                Close();
            TooltipHandler.TipRegion(new Rect(r.x, r.y, 100f, r.height), KmhText.Get("KMH_Prep_HideTip", "Preparation keeps going. Reopen it from the server browser."));
        }

        private static void DrawRestore(Rect b, bool restartAfter)
        {
            if (!KmhJoinPreparation.CanRestoreModList) return;
            if (!Widgets.ButtonText(b, KmhText.Get("KMH_Prep_Restore", "Restore my mod list"))) return;
            if (!KmhJoinPreparation.RestoreModList(out string why, out var outcome))
            {
                Messages.Message(KmhText.Get("KMH_Prep_RestoreFailed", "Your mod list could not be restored: {0}", KmhDisplayText.Inert(why, 160)), MessageTypeDefOf.RejectInput, false);
                return;
            }
            Dialog_KMHServerBrowser.AfterRestore(outcome,
                () => { try { GenCommandLine.Restart(); } catch (Exception ex) { Diagnostics.KmhLog.Warn($"Restart after restoring the mod list failed: {ex.Message}"); } });
        }

        private static float Heading(float y, float w, string text)
        {
            y += 6f;
            GUI.color = new Color(0.85f, 0.85f, 0.65f);
            float h = Text.CalcHeight(text, w);
            Widgets.Label(new Rect(0f, y, w, h), text);
            GUI.color = Color.white;
            return y + h + 2f;
        }

        private static float Para(float y, float w, string text, bool dim = false, bool warn = false)
        {
            if (dim) GUI.color = new Color(0.72f, 0.72f, 0.72f);
            if (warn) GUI.color = new Color(0.95f, 0.6f, 0.5f);
            float h = Text.CalcHeight(text, w);
            Widgets.Label(new Rect(0f, y, w, h), text);
            GUI.color = Color.white;
            return y + h + 4f;
        }

        private static string NameList(List<ModEntryDto> mods)
        {
            List<string> names = new List<string>();
            foreach (ModEntryDto e in mods) names.Add(KmhDisplayText.Inert(e.Name, 64));
            return string.Join(", ", names.ToArray());
        }

        private static List<string> Inert(List<string> names)
        {
            List<string> list = new List<string>();
            foreach (string n in names) list.Add(KmhDisplayText.Inert(n, 64));
            return list;
        }
    }
}
