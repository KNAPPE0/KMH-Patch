using System;
using System.Collections.Generic;

namespace KMHPatch.UI
{
    // Single source of pickable items + shared-picker opener; sources are safety-filtered so blocked defs never appear.
    internal static class KmhItemPickerService
    {
        // All safe, pickable item defs (already safety-filtered by ItemDefBrowser). -1 count = unlimited.
        public static Dictionary<string, int> AllPickableItems() => ItemDefBrowser.AllPickableItems();

        // Defensive re-filter of a caller-supplied source (e.g. live caravan/treasury counts) so nothing unsafe slips
        // into a picker even if the caller's source was built elsewhere.
        public static Dictionary<string, int> SafeSource(Dictionary<string, int> source)
        {
            Dictionary<string, int> outp = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            if (source == null) return outp;
            foreach (KeyValuePair<string, int> kv in source)
                if (KmhItemDisplayService.IsAllowed(kv.Key)) outp[kv.Key] = kv.Value;
            return outp;
        }

        // Open the shared two-step picker for a safety-filtered source. Payload stacks (full-state vault items) ride
        // along unfiltered - they're already-owned stock and the server re-verifies every withdraw.
        public static void Open(string title, string pickActionLabel, Dictionary<string, int> source,
                                Action<string, int> onPick, Func<Dictionary<string, int>> refreshSource = null,
                                IList<Items.KmhThingPayload> payloads = null,
                                Action<Items.KmhThingPayload, int> onPickPayload = null,
                                Func<IList<Items.KmhThingPayload>> refreshPayloads = null,
                                bool closeOnPick = false)
        {
            Verse.Find.WindowStack.Add(new Dialog_KMHItemPicker(
                title, pickActionLabel, SafeSource(source), onPick,
                refreshSource == null ? (Func<Dictionary<string, int>>)null : () => SafeSource(refreshSource()),
                payloads, onPickPayload, refreshPayloads, closeOnPick));
        }
    }
}
