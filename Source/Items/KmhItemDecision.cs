namespace KMHPatch.Items
{
    // Every picker, row and capture path consumes this rather than re-deciding safety on its own.
    internal readonly struct KmhItemDecision
    {
        public readonly bool Allowed;
        public readonly KmhItemReasonCode Code;
        public readonly string Reason;      // human-readable, tooltip-ready
        public readonly bool IconFallback;  // safe item whose icon failed -> display a fallback, don't block

        private KmhItemDecision(bool allowed, KmhItemReasonCode code, string reason, bool iconFallback)
        { Allowed = allowed; Code = code; Reason = reason ?? ""; IconFallback = iconFallback; }

        public static KmhItemDecision Allow(bool iconFallback = false)
            => new KmhItemDecision(true, KmhItemReasonCode.Ok, "", iconFallback);

        public static KmhItemDecision Block(KmhItemReasonCode code, string reason)
            => new KmhItemDecision(false, code, reason, false);
    }
}
