using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace KMHPatch.SubProtocol
{
    // Data stays a JObject on receive, so the dispatcher can route on Kind without an envelope subclass per handler.
    public class KmhEnvelope
    {
        [JsonProperty("kind")]
        public string Kind { get; set; }

        [JsonProperty("v")]
        public int Version { get; set; }

        // Names the logical action, not this delivery of it, so a re-send after an ambiguous write is refused rather than acted on twice.
        [JsonProperty("op", NullValueHandling = NullValueHandling.Ignore)]
        public string OpId { get; set; }

        private JObject _data;
        private object  _raw;
        private bool    _outbound;

        [JsonProperty("data")]
        public JObject Data
        {
            // Materialized only if something reads it, so a send does not allocate the payload as both a JObject and a string.
            get { if (_data == null && _outbound) _data = _raw == null ? new JObject() : JObject.FromObject(_raw); return _data; }
            set { _data = value; _outbound = false; }
        }

        public KmhEnvelope() { }

        public KmhEnvelope(string kind, object data, int version = -1, string opId = null)
        {
            Kind    = kind;
            Version = version < 0 ? KmhProtocol.CurrentVersion : version;
            OpId    = string.IsNullOrEmpty(opId) ? null : opId;
            _raw    = data;
            _outbound = true;
        }

        // Non-null sentinel so a null payload serializes as "data":{}, never "data":null - the wire contract both sides parse.
        private static readonly object EmptyData = new object();

        // Both paths must produce identical bytes, which KmhSelfTest guards.
        public string Serialize()
            => _outbound
                ? JsonConvert.SerializeObject(new Wire { Kind = Kind, Version = Version, OpId = OpId, Data = _raw ?? EmptyData })
                : JsonConvert.SerializeObject(this);

        private sealed class Wire
        {
            [JsonProperty("kind")] public string Kind { get; set; }
            [JsonProperty("v")]    public int    Version { get; set; }
            [JsonProperty("op", NullValueHandling = NullValueHandling.Ignore)] public string OpId { get; set; }
            [JsonProperty("data")] public object Data { get; set; }
        }

        // Typed accessors keep the Newtonsoft dependency contained to this file.
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

        // Empty when absent or malformed: a bad route must read as "no route" rather than throw on the network thread.
        public int[] GetIntArray(string key)
        {
            if (Data == null || !(Data[key] is JArray arr)) return new int[0];
            try
            {
                var outArr = new int[arr.Count];
                for (int i = 0; i < arr.Count; i++) outArr[i] = arr[i].Value<int>();
                return outArr;
            }
            catch { return new int[0]; }
        }

        // Reads a JSON array of longs (empty when absent or malformed), on the same terms as GetIntArray.
        public long[] GetLongArray(string key)
        {
            if (Data == null || !(Data[key] is JArray arr)) return new long[0];
            try
            {
                var outArr = new long[arr.Count];
                for (int i = 0; i < arr.Count; i++) outArr[i] = arr[i].Value<long>();
                return outArr;
            }
            catch { return new long[0]; }
        }

        // Reads a JSON array of strings (empty when absent or malformed), on the same terms as GetIntArray.
        public string[] GetStringArray(string key)
        {
            if (Data == null || !(Data[key] is JArray arr)) return new string[0];
            try
            {
                var outArr = new string[arr.Count];
                for (int i = 0; i < arr.Count; i++) outArr[i] = arr[i].Value<string>() ?? "";
                return outArr;
            }
            catch { return new string[0]; }
        }

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
                // The caller logs, so one malformed message cannot spam the log on every receive.
                return null;
            }
        }
    }
}
