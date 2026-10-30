using System;
using System.Collections.Generic;

namespace KMHPatch.UI
{
    internal static class KmhItemPickerService
    {
        public static Dictionary<string, int> AllPickableItems() => ItemDefBrowser.AllPickableItems();

        // Re-filtered even though callers filter too, so a source built elsewhere cannot smuggle a blocked def in.
        public static Dictionary<string, int> SafeSource(Dictionary<string, int> source)
        {
            Dictionary<string, int> outp = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            if (source == null) return outp;
            foreach (KeyValuePair<string, int> kv in source)
                if (KmhItemDisplayService.IsAllowed(kv.Key)) outp[kv.Key] = kv.Value;
            return outp;
        }

        // Payload stacks ride unfiltered: already-owned stock, and the server re-verifies every withdraw.
        public static void Open(string title, string pickActionLabel, Dictionary<string, int> source,
                                Action<string, int> onPick, Func<Dictionary<string, int>> refreshSource = null,
                                IList<Items.KmhThingPayload> payloads = null,
                                Action<Items.KmhThingPayload, int> onPickPayload = null,
                                Func<IList<Items.KmhThingPayload>> refreshPayloads = null,
                                bool closeOnPick = false, bool intoThisGame = false)
        {
            // A withdraw keeps owned rows this game cannot load, greyed with the reason, rather than hiding them.
            Func<Dictionary<string, int>, Dictionary<string, int>> filter = intoThisGame ? (Func<Dictionary<string, int>, Dictionary<string, int>>)Owned : SafeSource;
            Verse.Find.WindowStack.Add(new Dialog_KMHItemPicker(
                title, pickActionLabel, filter(source), onPick,
                refreshSource == null ? (Func<Dictionary<string, int>>)null : () => filter(refreshSource()),
                payloads, onPickPayload, refreshPayloads, closeOnPick, intoThisGame));
        }

        private static Dictionary<string, int> Owned(Dictionary<string, int> source)
            => source == null ? new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
                              : new Dictionary<string, int>(source, StringComparer.OrdinalIgnoreCase);
    }
}
