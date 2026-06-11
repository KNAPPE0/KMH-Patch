using KMHPatch.Diagnostics;
using KMHPatch.Features.Quests.Dto;
using KMHPatch.Notifications;
using KMHPatch.SubProtocol;

namespace KMHPatch.Features.Quests
{
    // Registers the quest-board sub-protocol handler + exposes the public request and mutation methods dialogs use
    //
    // Mutation methods (Claim / Submit / Cancel) send a small envelope with just the quest id. The server applies
    // the change and broadcasts a fresh kmh.quest.snapshot - so the cache update path is exactly the same as a
    // polled refresh, no per-mutation response handler needed
    //
    // Post (creating a new quest) is wired end-to-end via TryPostDeliverItem / TryPostBounty +
    // Dialog_KMHPostQuest's composer with DefDatabase item picker. Bounty kind has no delivery target (poster
    // Approves manually when satisfied)
    internal static class QuestHandler
    {
        public static void Register()
        {
            KmhDispatcher.RegisterHandler(KmhProtocol.Kind.QuestSnapshot, OnSnapshot);
        }

        public static bool RequestSnapshot()
        {
            return KmhDispatcher.Send(KmhProtocol.Kind.QuestRequest, null);
        }

        public static bool TryClaim(long questId)
        {
            return SendMutation(KmhProtocol.Kind.QuestClaim, questId, "Claim sent");
        }

        public static bool TrySubmit(long questId)
        {
            return SendMutation(KmhProtocol.Kind.QuestSubmit, questId, "Submission sent");
        }

        public static bool TryCancel(long questId)
        {
            return SendMutation(KmhProtocol.Kind.QuestCancel, questId, "Cancel sent");
        }

        // Claimer drops a quest they claimed (it returns to the board). Costs the claimer reputation server-side
        public static bool TryAbandon(long questId)
        {
            return SendMutation(KmhProtocol.Kind.QuestAbandon, questId, "Abandon sent");
        }

        // Claimer reports completion of a verifiable kind (escort/defend/hunt/ build). The server trusts the report
        // and pays the escrowed bounty
        public static bool TryVerify(long questId)
        {
            return SendMutation(KmhProtocol.Kind.QuestVerify, questId, "Completion reported");
        }

        // Claimer submits proof (text + optional https image) for a Custom
        // quest. Moves it to PendingReview for the poster to approve/reject.
        public static bool TrySubmitProof(long questId, string proofText, string proofImageUrl)
        {
            bool sent = KmhDispatcher.Send(KmhProtocol.Kind.QuestSubmitProof, new
            {
                quest_id        = questId,
                proof_text      = proofText ?? "",
                proof_image_url = proofImageUrl ?? "",
            });
            if (sent) KmhNotifications.Neutral("Submitting proof…");
            else      KmhNotifications.Rejected("Not connected to a KMH server");
            return sent;
        }

        // Poster approves or rejects a PendingReview submission, with an optional note shown to the claimer
        public static bool TryReview(long questId, bool approve, string note)
        {
            bool sent = KmhDispatcher.Send(KmhProtocol.Kind.QuestReview, new
            {
                quest_id = questId,
                approve  = approve,
                note     = note ?? "",
            });
            if (sent) KmhNotifications.Positive(approve ? "Approval sent" : "Rejection sent");
            else      KmhNotifications.Rejected("Not connected to a KMH server");
            return sent;
        }

        // Post any quest kind by sending the full draft. The server reads the whole QuestEntry shape (DataAs), so
        // per-kind fields ride along. Returns false if not connected. Server validates per-kind + replies with a
        // chat reason on rejection
        public static bool TryPostDraft(Dto.QuestEntry draft, int expiresInHours)
        {
            if (draft == null) return false;
            if (string.IsNullOrWhiteSpace(draft.Title))
            {
                KmhNotifications.Rejected("Quest needs a title");
                return false;
            }
            // Send the draft fields plus expires_hours (which lives outside the QuestEntry shape). Newtonsoft
            // serializes the draft's JsonProperty names, matching the server's DataAs<QuestEntry>
            var payload = Newtonsoft.Json.Linq.JObject.FromObject(draft);
            payload["expires_hours"] = expiresInHours < 0 ? 0 : expiresInHours;
            bool sent = KmhDispatcher.Send(KmhProtocol.Kind.QuestPost, payload);
            if (sent) KmhNotifications.Neutral("Posting quest…");
            else      KmhNotifications.Rejected("Not connected to a KMH server");
            return sent;
        }

        // Bounty-kind sign-off: poster marks a Submitted Bounty quest as Completed. DeliverItem quests
        // auto-complete server-side via the treasury check inside Submit, so this only ever fires for Bounty-kind
        // quests
        public static bool TryApprove(long questId)
        {
            return SendMutation(KmhProtocol.Kind.QuestApprove, questId, "Approval sent");
        }

        // Bounty kind has no delivery target - server marks it as manual sign-off (poster confirms when satisfied).
        // Minimal envelope. expiresInHours: 0 = never expires; > 0 = auto-expire + refund
        public static bool TryPostBounty(string title, string description, int bountySilver,
                                         string visibility = QuestVisibilityPublic,
                                         int    expiresInHours = 0)
        {
            if (string.IsNullOrWhiteSpace(title))
            {
                KmhNotifications.Rejected("Quest needs a title");
                return false;
            }
            if (bountySilver < 0)
            {
                KmhNotifications.Rejected("Bounty silver cannot be negative");
                return false;
            }
            bool sent = KmhDispatcher.Send(KmhProtocol.Kind.QuestPost, new
            {
                kind          = "bounty",
                visibility    = visibility ?? QuestVisibilityPublic,
                title         = title,
                description   = description ?? "",
                bounty_silver = bountySilver,
                expires_hours = expiresInHours < 0 ? 0 : expiresInHours,
            });
            if (sent) KmhNotifications.Neutral("Posting bounty…");
            else      KmhNotifications.Rejected("Not connected to a KMH server");
            return sent;
        }

        // Visibility constants for the Quest Post envelope. Same snake_case strings QuestEntry uses on the snapshot
        // side
        public const string QuestVisibilityPublic    = "public";
        public const string QuestVisibilityGuildOnly = "guild_only";

        public static bool TryPostDeliverItem(
            string title,
            string description,
            int    bountySilver,
            string targetItemDefName,
            int    targetItemQty,
            string visibility     = QuestVisibilityPublic,
            int    expiresInHours = 0)
        {
            if (string.IsNullOrWhiteSpace(title))
            {
                KmhNotifications.Rejected("Quest needs a title");
                return false;
            }
            if (bountySilver < 0)
            {
                KmhNotifications.Rejected("Bounty silver cannot be negative");
                return false;
            }
            if (string.IsNullOrWhiteSpace(targetItemDefName))
            {
                KmhNotifications.Rejected("Target item is required");
                return false;
            }
            if (targetItemQty <= 0)
            {
                KmhNotifications.Rejected("Target quantity must be greater than 0");
                return false;
            }

            bool sent = KmhDispatcher.Send(KmhProtocol.Kind.QuestPost, new
            {
                kind                 = "deliver_item",
                visibility           = visibility ?? QuestVisibilityPublic,
                title                = title,
                description          = description ?? "",
                bounty_silver        = bountySilver,
                target_item_def_name = targetItemDefName,
                target_item_qty      = targetItemQty,
                expires_hours        = expiresInHours < 0 ? 0 : expiresInHours,
            });
            if (sent) KmhNotifications.Neutral("Posting quest…");
            else      KmhNotifications.Rejected("Not connected to a KMH server");
            return sent;
        }

        private static bool SendMutation(string kind, long questId, string flashOnSent)
        {
            bool sent = KmhDispatcher.Send(kind, new { quest_id = questId });
            if (sent)
            {
                // Optimistic local feedback - authoritative state lands in the next snapshot push. If the request
                // fails server-side (already claimed, etc.) the next snapshot will reflect reality
                KmhNotifications.Positive(flashOnSent);
            }
            else
            {
                KmhNotifications.Rejected("Not connected to a KMH server");
            }
            return sent;
        }

        private static void OnSnapshot(KmhEnvelope env)
        {
            QuestSnapshot snapshot = env?.DataAs<QuestSnapshot>();
            if (snapshot == null)
            {
                KmhLog.Warn("Quest snapshot envelope had no parseable payload, ignoring");
                return;
            }
            QuestCache.Apply(snapshot);
        }
    }
}
