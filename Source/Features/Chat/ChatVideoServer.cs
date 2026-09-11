using System;
using System.Collections.Generic;
using KMHPatch.SubProtocol;

namespace KMHPatch.Features.Chat
{
    // Video fetched and served by the KMH server, so nothing has to be installed on this machine.
    internal static class ChatVideoServer
    {
        internal sealed class Answer
        {
            public string Video = "", Title = "", Error = "";
            public bool Done;
        }

        private static readonly Dictionary<string, Answer> Waiting = new Dictionary<string, Answer>(StringComparer.Ordinal);
        private static bool _offered;

        public static bool Available => _offered && KmhDispatcher.IsKmhServer;

        public static void Configure(bool offered) => _offered = offered;

        public static void Register()
        {
            KmhDispatcher.RegisterHandler(KmhProtocol.Kind.VideoResolved, OnResolved);
        }

        public static Answer Resolve(string watchUrl, int height)
        {
            var answer = new Answer();
            Waiting[watchUrl] = answer;
            if (!KmhDispatcher.Send(KmhProtocol.Kind.VideoResolve, new { watch_url = watchUrl, height }))
            {
                answer.Error = "could not ask the server";
                answer.Done = true;
                Waiting.Remove(watchUrl);
            }
            return answer;
        }

        public static void Forget(string watchUrl) => Waiting.Remove(watchUrl ?? "");

        public static void Clear()
        {
            Waiting.Clear();
            _offered = false;
        }

        private static void OnResolved(KmhEnvelope env)
        {
            string watchUrl = env.GetString("watch_url") ?? "";
            if (!Waiting.TryGetValue(watchUrl, out Answer answer)) return;
            Waiting.Remove(watchUrl);

            string reason = env.GetString("reason") ?? "";
            string video = env.GetString("video") ?? "";
            int port = env.GetInt("port", 0);
            answer.Title = env.GetString("title") ?? "";

            if (reason.Length > 0 || video.Length == 0 || port <= 0)
            {
                answer.Error = reason.Length > 0 ? reason : "the server had nothing to play";
            }
            else
            {
                answer.Video = StreamUrl(TCPNetwork.Network.Ip, port, video);
                if (answer.Video.Length == 0) answer.Error = "the server sent an address this client will not use";
            }
            answer.Done = true;
        }

        // The host is the one already connected to, never one the answer names.
        internal static string StreamUrl(string host, int port, string token)
        {
            if (string.IsNullOrWhiteSpace(host) || port <= 0 || string.IsNullOrEmpty(token)) return "";
            foreach (char c in token)
                if (!char.IsLetterOrDigit(c)) return "";
            return $"http://{host}:{port}/{token}.mp4";
        }
    }
}
