using System;
using System.Linq;
using System.Xml.Linq;

namespace KMHPatch.Features.Enforcement
{
    // Preserve-personal merge: server config wins, but keep the player's value
    // for any field the heuristic calls personal. Falls back to server text on a
    // parse error, so a bad config can never produce broken XML.
    internal static class ConfigMerge
    {
        public static string MergePreservingPersonal(string serverText, string playerText)
        {
            if (string.IsNullOrEmpty(playerText)) return serverText;
            try
            {
                XDocument sDoc = XDocument.Parse(serverText);
                XElement sSet = sDoc.Descendants("ModSettings").FirstOrDefault();
                XElement pSet = XDocument.Parse(playerText).Descendants("ModSettings").FirstOrDefault();
                if (sSet == null || pSet == null) return serverText;

                bool changed = false;

                // Server fields that are personal: take the player's value if they have one.
                foreach (XElement sField in sSet.Elements().ToList())
                {
                    if (!ConfigHeuristics.IsPersonalField(sField.Name.LocalName)) continue;
                    XElement pField = pSet.Elements().FirstOrDefault(e => e.Name == sField.Name);
                    if (pField == null) continue;
                    sField.ReplaceWith(new XElement(pField));
                    changed = true;
                }

                // Personal fields the player has that the server omitted: keep them.
                foreach (XElement pField in pSet.Elements())
                {
                    if (!ConfigHeuristics.IsPersonalField(pField.Name.LocalName)) continue;
                    if (sSet.Elements().Any(e => e.Name == pField.Name)) continue;
                    sSet.Add(new XElement(pField));
                    changed = true;
                }

                if (!changed) return serverText;
                return "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n" + sDoc.ToString(SaveOptions.None);
            }
            catch { return serverText; }
        }
    }
}
