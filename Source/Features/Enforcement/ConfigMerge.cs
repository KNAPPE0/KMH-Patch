using System;
using System.Linq;
using System.Xml.Linq;

namespace KMHPatch.Features.Enforcement
{
    // Any parse error falls back to the server text, so a bad config can never emit broken XML.
    internal static class ConfigMerge
    {
        public static string MergePreservingPersonal(string serverText, string playerText)
        {
            if (string.IsNullOrEmpty(playerText)) return serverText;
            try
            {
                XDocument sDoc = ParseNoDtd(serverText);
                XElement sSet = sDoc.Descendants("ModSettings").FirstOrDefault();
                XElement pSet = ParseNoDtd(playerText).Descendants("ModSettings").FirstOrDefault();
                if (sSet == null || pSet == null) return serverText;

                bool changed = false;

                foreach (XElement sField in sSet.Elements().ToList())
                {
                    if (!ConfigHeuristics.IsPersonalField(sField.Name.LocalName)) continue;
                    XElement pField = pSet.Elements().FirstOrDefault(e => e.Name == sField.Name);
                    if (pField == null) continue;
                    sField.ReplaceWith(new XElement(pField));
                    changed = true;
                }

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

        // No DTD and no resolver: server text never expands entities or reaches for another file.
        private static XDocument ParseNoDtd(string text)
        {
            var settings = new System.Xml.XmlReaderSettings { DtdProcessing = System.Xml.DtdProcessing.Prohibit, XmlResolver = null };
            using (var reader = System.Xml.XmlReader.Create(new System.IO.StringReader(text), settings))
                return XDocument.Load(reader);
        }
    }
}
