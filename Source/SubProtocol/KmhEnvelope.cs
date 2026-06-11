using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace KMHPatch.SubProtocol
{
    // Wire envelope shared by every KMH message.
    //
    // Stored as JObject (not a typed Data of T) on receive so the dispatcher can route on Kind without each handler
    // needing its own envelope subclass. Handlers cast Data to their typed payload via ToObject<T>() once they know
    // what Kind they got
    public class KmhEnvelope
    {
        [JsonProperty("kind")]
        public string Kind { get; set; }

        [JsonProperty("v")]
        public int Version { get; set; }

        [JsonProperty("data")]
        public JObject Data { get; set; }

        public KmhEnvelope() { }

        public KmhEnvelope(string kind, object data, int version = -1)
        {
            Kind    = kind;
            Version = version < 0 ? KmhProtocol.CurrentVersion : version;
            // Always normalize to JObject so consumers can do uniform lookups regardless of what type the caller
            // passed in
            Data    = data == null ? new JObject() : JObject.FromObject(data);
        }

        public string Serialize() => JsonConvert.SerializeObject(this);

        // Typed accessors so handler code can read fields without taking a direct dependency on
        // Newtonsoft.Json.Linq.JObject - keeps the Newtonsoft import contained to this file
        public int GetInt(string key, int defaultValue = 0)
        {
            if (Data == null || Data[key] == null) return defaultValue;
            try { return Data.Value<int>(key); } catch { return defaultValue; }
        }

        public string GetString(string key, string defaultValue = null)
        {
            if (Data == null || Data[key] == null) return defaultValue;
            try { return Data.Value<string>(key); } catch { return defaultValue; }
        }

        public bool GetBool(string key, bool defaultValue = false)
        {
            if (Data == null || Data[key] == null) return defaultValue;
            try { return Data.Value<bool>(key); } catch { return defaultValue; }
        }

        // Escape hatch for typed payloads - handler that knows the schema gets the convenience of a typed object
        public T DataAs<T>() where T : class
        {
            if (Data == null) return null;
            try { return Data.ToObject<T>(); } catch { return null; }
        }

        public static KmhEnvelope TryParse(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            try
            {
                return JsonConvert.DeserializeObject<KmhEnvelope>(json);
            }
            catch
            {
                // Caller logs - we don't want one malformed message to spam the log every receive. The intercept
                // path turns null into a no-op
                return null;
            }
        }
    }
}
