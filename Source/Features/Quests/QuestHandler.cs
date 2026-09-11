using KMHPatch.Diagnostics;
using KMHPatch.Features.Quests.Dto;
using KMHPatch.Notifications;
using KMHPatch.SubProtocol;

namespace KMHPatch.Features.Quests
{
    // Mutations send only the quest id and the server rebroadcasts, so no mutation needs its own response handler.
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

        // The server trusts this report and pays the escrowed bounty.
        public static bool TryVerify(long questId)
        {
            return SendMutation(KmhProtocol.Kind.QuestVerify, questId, "Completion reported");
        }

        // Moves a Custom quest to PendingReview for the poster to approve or reject.
        public static bool TrySubmitProof(long questId, string proofText, string proofImageUrl)
        {
            bool sent = KmhDispatcher.Send(KmhProtocol.Kind.QuestSubmitProof, new
            {
                quest_id        = questId,
                proof_text      = proofText ?? "",
                proof_image_url = proofImageUrl ?? "",
            });
            if (sent) KmhNotifications.Neutral("Submitting proof…");
            else      KmhNotifications.NotConnected();
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
            else      KmhNotifications.NotConnected();
            return sent;
        }

        // Sends the whole QuestEntry shape, so per-kind fields ride along and the server validates them all.
        public static bool TryPostDraft(Dto.QuestEntry draft, int expiresInHours)
        {
            if (draft == null) return false;
            if (string.IsNullOrWhiteSpace(draft.Title))
            {
                KmhNotifications.Rejected("Quest needs a title");
                return false;
            }
            // Built as a JObject because expires_hours lives outside the QuestEntry shape.
            var payload = Newtonsoft.Json.Linq.JObject.FromObject(draft);
            payload["expires_hours"] = expiresInHours < 0 ? 0 : expiresInHours;
            bool sent = KmhDispatcher.Send(KmhProtocol.Kind.QuestPost, payload,
                KmhOpId.For($"quest.post|{draft.Kind}|{draft.Title}|{draft.BountySilver}"));
            if (sent) KmhNotifications.Neutral("Posting quest…");
            else      KmhNotifications.NotConnected();
            return sent;
        }

        // Bounty-kind only: DeliverItem auto-completes server-side on the treasury check inside Submit.
        public static bool TryApprove(long questId)
        {
            return SendMutation(KmhProtocol.Kind.QuestApprove, questId, "Approval sent");
        }

        // No delivery target, so the server marks it manual sign-off; expiresInHours 0 = never, > 0 = expire + refund.
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
            }, KmhOpId.For($"quest.post|bounty|{title}|{bountySilver}"));
            if (sent) KmhNotifications.Neutral("Posting bounty…");
            else      KmhNotifications.NotConnected();
            return sent;
        }

        // The same snake_case strings QuestEntry uses on the snapshot side.
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
            }, KmhOpId.For($"quest.post|deliver_item|{title}|{targetItemDefName}|{targetItemQty}|{bountySilver}"));
            if (sent) KmhNotifications.Neutral("Posting quest…");
            else      KmhNotifications.NotConnected();
            return sent;
        }

        private static bool SendMutation(string kind, long questId, string flashOnSent)
        {
            bool sent = KmhDispatcher.Send(kind, new { quest_id = questId });
            if (sent)
            {
                // Optimistic only; a server-side refusal is corrected by the next snapshot.
                KmhNotifications.Positive(flashOnSent);
            }
            else
            {
                KmhNotifications.NotConnected();
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
