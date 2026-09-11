using System;
using KMHPatch.Diagnostics;
using UnityEngine;
using UnityEngine.Video;

namespace KMHPatch.Features.Chat
{
    // One video at a time, and only on a click: starting one contacts the host and tells it the player's IP.
    internal static class ChatVideoPlayer
    {
        private enum State { None, Preparing, Playing, Failed }

        private static GameObject   _host;
        private static VideoPlayer  _player;
        private static RenderTexture _target;
        private static AudioSource  _audio;

        private static string _url = "";
        // Every window asks about the LINK; the player is handed media urls that link never mentions.
        private static string _stream = "";
        private static bool   _firstFrameSeen;
        private static int    _limitSeconds;
        private static State  _state = State.None;
        private static string _error = "";
        private static float  _startedAt;
        private static bool   _paused;
        private static bool   _hasAudioTrack;

        private static bool  _muted;
        private static float _volume = DefaultVolume;
        private static bool  _loop = true;
        private static bool  _prefsLoaded;
        private static float _prefsDirtyAt = -1f;

        public const float DefaultVolume = 0.6f;

        // Owner ceiling in seconds, 0 = none. Checked once prepared and before Play, so a long clip is refused, not cut off.
        private static int _maxSeconds;

        public static void SetMaxSeconds(int seconds) => _maxSeconds = seconds < 0 ? 0 : seconds;
        public static int MaxSeconds => _maxSeconds;

        // Pure so the rule can be checked without a running game.
        internal static bool ExceedsLimit(double lengthSeconds, int maxSeconds)
            => maxSeconds > 0 && lengthSeconds > 0d && lengthSeconds > maxSeconds;

        // Prepare can hang forever on a host that accepts the connection and then says nothing.
        private const float PrepareTimeout = 30f;

        // A dragged volume slider waits for the drag to settle rather than writing settings 60 times a second.
        private const float PrefsSaveDelay = 1f;

        public static bool IsPlaying(string url) => _state == State.Playing && _url == url;
        public static bool IsPreparing(string url) => _state == State.Preparing && _url == url;
        public static bool IsActive(string url) => _url == url && (_state == State.Preparing || _state == State.Playing);
        public static string FailureFor(string url) => _state == State.Failed && _url == url ? _error : null;

        public static string Url => _url;
        public static bool AnythingPlaying => _state == State.Playing;

        // One list, because a copy per window goes stale the moment another window is added.
        private static readonly System.Type[] Watchers =
        {
            typeof(Comms.Dialog_KMHComms),
            typeof(Comms.Dialog_KMHChatPopout),
            typeof(Dialog_KMHVideo),
            typeof(Dialog_KMHVideoWindow),
        };

        // Exposed so a test can catch a window missing from the list.
        internal static System.Type[] WatcherTypes => Watchers;

        // A video with nowhere to draw is a decoder and a download still running for nothing.
        public static void StopIfNobodyWatching()
        {
            try
            {
                if (Verse.Find.WindowStack != null)
                    foreach (System.Type t in Watchers)
                        if (Verse.Find.WindowStack.IsOpen(t)) return;
            }
            catch { }
            Stop();
        }
        public static bool Muted => _muted;
        public static bool Paused => _paused;
        public static bool Looping => _loop;
        public static float Volume => _volume;

        // A clip that reached its end is not stalled, and calling that "buffering" for ever looks like a broken video.
        public static bool Buffering
        {
            get { try { return _state == State.Playing && !_paused && !Finished && _player != null && !_player.isPlaying; } catch { return false; } }
        }

        public static bool Finished
        {
            get
            {
                try
                {
                    if (_state != State.Playing || _paused || _player == null || _loop || !_player.isPrepared) return false;
                    return !_player.isPlaying && _player.length > 0d && _player.time >= _player.length - 0.25d;
                }
                catch { return false; }
            }
        }

        // Through a RenderTexture: what VideoPlayer hands out in APIOnly mode is bottom-up here and drew upside down.
        public static Texture Frame => _state == State.Playing ? _target : null;

        public static int Width  => _player != null && _player.width  > 0 ? (int)_player.width  : 16;
        public static int Height => _player != null && _player.height > 0 ? (int)_player.height : 9;

        public static double Position
        {
            get { try { return _state == State.Playing && _player != null ? _player.time : 0d; } catch { return 0d; } }
        }

        public static double Length
        {
            get { try { return _player != null && _player.isPrepared ? _player.length : 0d; } catch { return 0d; } }
        }

        // A live stream has no length and nothing to scrub through, so the seek bar is hidden rather than drawn dead.
        public static bool CanSeek
        {
            get { try { return _state == State.Playing && _player != null && _player.canSetTime && Length > 0.5d; } catch { return false; } }
        }

        // Starting a different video stops the current one.
        public static void Play(string url) => Play(url, url, _maxSeconds);

        public static void Play(string key, string mediaUrl, int limitSeconds)
        {
            if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(mediaUrl)) return;
            if (_url == key && _stream == mediaUrl && (_state == State.Preparing || _state == State.Playing)) return;

            LoadPrefs();
            Stop();
            _url = key; _stream = mediaUrl;
            _limitSeconds = limitSeconds < 0 ? 0 : limitSeconds;
            _state = State.Preparing; _error = ""; _paused = false; _hasAudioTrack = false;
            _firstFrameSeen = false;
            _startedAt = Time.realtimeSinceStartup;
            KmhLog.Info($"Chat video: preparing {ChatImageCache.HostOf(key)}");

            try
            {
                EnsureHost();
                _player.source = VideoSource.Url;
                _player.url = mediaUrl;
                _player.isLooping = _loop;
                _player.playOnAwake = false;
                _player.renderMode = VideoRenderMode.RenderTexture;

                // Unity does not wire audio up for a url source: routing must be set before Prepare or it plays silently.
                _player.audioOutputMode = VideoAudioOutputMode.AudioSource;
                _player.controlledAudioTrackCount = 1;
                _player.EnableAudioTrack(0, true);
                _player.SetTargetAudioSource(0, _audio);
                ApplyAudio();

                _player.Prepare();
            }
            catch (Exception ex) { Fail(ex.Message); }
        }

        public static void TogglePause()
        {
            if (_state != State.Playing || _player == null) return;
            try
            {
                // At the end there is nothing to pause: the button is the only way back to the start.
                if (Finished)
                {
                    if (_player.canSetTime) _player.time = 0d;
                    _player.Play();
                    TryPlayAudio();
                    _paused = false;
                    return;
                }
                if (_paused) { _player.Play(); TryPlayAudio(); }
                else _player.Pause();
                _paused = !_paused;
            }
            catch (Exception ex) { KmhLog.Warn($"Chat video: pause toggle threw - {ex.Message}"); }
        }

        public static void ToggleMute()
        {
            LoadPrefs();
            _muted = !_muted;
            ApplyAudio();
            MarkPrefsDirty();
        }

        public static void SetVolume(float v)
        {
            LoadPrefs();
            float clamped = ClampVolume(v);
            bool changed = !Mathf.Approximately(clamped, _volume);
            _volume = clamped;
            // Touching the volume at all is a request to hear it, even at the level it already had.
            if (_muted && _volume > 0f) { _muted = false; changed = true; }
            if (!changed) return;
            ApplyAudio();
            MarkPrefsDirty();
        }

        public static void ToggleLoop()
        {
            LoadPrefs();
            _loop = !_loop;
            try { if (_player != null) _player.isLooping = _loop; } catch { }
            MarkPrefsDirty();
        }

        public static void Seek(double seconds)
        {
            if (!CanSeek) return;
            try
            {
                float target = Mathf.Clamp((float)seconds, 0f, (float)Math.Max(0d, Length - 0.1d));
                _player.time = target;
                // A seek off the end of a non-looping clip leaves it stopped with no way back, so a scrub resumes.
                if (_paused) { _player.Play(); TryPlayAudio(); _paused = false; }
            }
            catch (Exception ex) { KmhLog.Warn($"Chat video: seek threw - {ex.Message}"); }
        }

        public static void Nudge(double delta) => Seek(SeekTarget(Position, delta, Length));

        // Polled once per frame from the KMH update pump, like the image cache.
        public static void Tick()
        {
            SavePrefsIfSettled();

            if (_state == State.Playing) { ApplyAudio(); NoteFirstFrame(); return; }
            if (_state != State.Preparing) return;

            if (_player == null) { Fail("player went away"); return; }

            if (Time.realtimeSinceStartup - _startedAt > PrepareTimeout)
            {
                Fail("timed out preparing - the host may not allow direct playback");
                return;
            }

            if (!_player.isPrepared) return;

            // The only moment a long clip can be refused rather than interrupted.
            double prepared = 0d;
            try { prepared = _player.length; } catch { }
            if (ExceedsLimit(prepared, _limitSeconds))
            {
                Fail($"too long to play here - {FormatTime(prepared)}, and this server's limit is {FormatTime(_limitSeconds)}");
                return;
            }

            try
            {
                // Sized from the video itself, so a portrait clip is not stretched into a letterbox.
                int w = Mathf.Clamp((int)_player.width, 16, 1920);
                int h = Mathf.Clamp((int)_player.height, 16, 1080);
                if (_target == null || _target.width != w || _target.height != h)
                {
                    ReleaseTarget();
                    _target = new RenderTexture(w, h, 0);
                    _target.Create();
                }
                _player.targetTexture = _target;

                // The track count is only known once prepared, so routing is re-applied here.
                _hasAudioTrack = _player.audioTrackCount > 0;
                if (_hasAudioTrack)
                {
                    _player.EnableAudioTrack(0, true);
                    _player.SetTargetAudioSource(0, _audio);
                }
                ApplyAudio();

                KmhLog.Info($"Chat video: prepared {_player.width}x{_player.height} @{_player.frameRate:0}fps, {_player.audioTrackCount} audio track(s)");
                _player.Play();
                TryPlayAudio();
                _paused = false;
                _state = State.Playing;
                KmhLog.Info("Chat video: playback started");
            }
            catch (Exception ex) { Fail(ex.Message); }
        }

        public static void Stop()
        {
            try { _player?.Stop(); } catch { }
            try { _audio?.Stop(); } catch { }
            ReleaseTarget();
            _url = ""; _stream = ""; _state = State.None; _error = ""; _paused = false; _hasAudioTrack = false;
            _firstFrameSeen = false;
        }

        private static void ReleaseTarget()
        {
            if (_target == null) return;
            try { if (_player != null) _player.targetTexture = null; } catch { }
            try { _target.Release(); UnityEngine.Object.Destroy(_target); } catch { }
            _target = null;
        }

        private static void NoteFirstFrame()
        {
            if (_firstFrameSeen || _player == null) return;
            try
            {
                if (_player.frame <= 0) return;
                _firstFrameSeen = true;
                KmhLog.Info($"Chat video: first frame ({_player.width}x{_player.height})");
            }
            catch { }
        }

        // Tears the host object down rather than leaving a hidden GameObject and a decoder alive.
        public static void Clear()
        {
            Stop();
            SavePrefs();
            try { if (_host != null) UnityEngine.Object.Destroy(_host); } catch { }
            _host = null; _player = null; _audio = null;
        }

        // Re-read so a change in Mod Options is heard on the video already playing.
        public static void SyncFromSettings()
        {
            _prefsLoaded = false;
            _prefsDirtyAt = -1f;
            LoadPrefs();
            try { if (_player != null) _player.isLooping = _loop; } catch { }
            ApplyAudio();
        }


        public static float ClampVolume(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);

        // Clamped to just inside the end: seeking exactly to length ends the clip instead of showing that moment.
        public static double SeekTarget(double current, double delta, double length)
        {
            if (length <= 0d) return 0d;
            double t = current + delta;
            if (t < 0d) return 0d;
            double last = length - 0.1d;
            return t > last ? Math.Max(0d, last) : t;
        }

        public static string FormatTime(double seconds)
        {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0d) seconds = 0d;
            int total = (int)seconds;
            int h = total / 3600, m = (total % 3600) / 60, s = total % 60;
            return h > 0 ? $"{h}:{m:00}:{s:00}" : $"{m}:{s:00}";
        }

        // Shared by the inline row and the fullscreen window so neither stretches a portrait clip.
        public static Rect FitInto(int videoW, int videoH, Rect box)
        {
            float aspect = videoH > 0 ? (float)videoW / videoH : 16f / 9f;
            if (aspect <= 0f || float.IsNaN(aspect)) aspect = 16f / 9f;

            float w = box.width, h = w / aspect;
            if (h > box.height) { h = box.height; w = h * aspect; }
            return new Rect(box.x + (box.width - w) * 0.5f, box.y + (box.height - h) * 0.5f, w, h);
        }


        private static void EnsureHost()
        {
            if (_host != null && _player != null) return;

            // Kept out of scene enumeration and alive across scene changes, so the world map does not kill a video.
            _host = new GameObject("KMH_ChatVideo") { hideFlags = HideFlags.HideAndDontSave };
            UnityEngine.Object.DontDestroyOnLoad(_host);

            _audio = _host.AddComponent<AudioSource>();
            _audio.playOnAwake = false;
            ConfigureAsUiAudio(_audio);

            _player = _host.AddComponent<VideoPlayer>();
            _player.playOnAwake = false;
            _player.errorReceived += (vp, msg) => Fail(msg);
        }

        // spatialBlend alone misses RimWorld's zoom-driven listener low-pass and reverb; a source must opt out of those.
        internal static void ConfigureAsUiAudio(AudioSource a)
        {
            if (a == null) return;
            a.spatialBlend          = 0f;
            a.bypassListenerEffects = true;
            a.bypassReverbZones     = true;
            a.reverbZoneMix         = 0f;
            a.dopplerLevel          = 0f;
            a.panStereo             = 0f;
            a.spread                = 0f;
            a.rolloffMode           = AudioRolloffMode.Linear;
            a.minDistance           = 1f;
            a.maxDistance           = 1f;
        }

        // Some platforms route no samples unless the AudioSource is started alongside the player.
        private static void TryPlayAudio()
        {
            if (!_hasAudioTrack) return;
            try { _audio?.Play(); } catch { }
        }

        // Scaled by RimWorld's master volume, or a video is the one loud thing left when a player turns everything down.
        private static void ApplyAudio()
        {
            if (_audio == null) return;
            LoadPrefs();
            float master = 1f;
            try { master = Mathf.Clamp01(Verse.Prefs.VolumeMaster); } catch { }
            _audio.mute = _muted;
            _audio.volume = _volume * master;
        }

        private static void LoadPrefs()
        {
            if (_prefsLoaded) return;
            _prefsLoaded = true;
            try
            {
                KMHPatchSettings s = KMHPatchMod.Settings;
                if (s == null) { _prefsLoaded = false; return; }   // settings not up yet; try again next call
                _muted  = s.ChatVideoMuted;
                _volume = ClampVolume(s.ChatVideoVolume);
                _loop   = s.ChatVideoLoop;
            }
            catch { }
        }

        private static void MarkPrefsDirty()
        {
            try { _prefsDirtyAt = Time.realtimeSinceStartup; } catch { _prefsDirtyAt = -1f; }
        }

        private static void SavePrefsIfSettled()
        {
            if (_prefsDirtyAt < 0f) return;
            if (Time.realtimeSinceStartup - _prefsDirtyAt < PrefsSaveDelay) return;
            _prefsDirtyAt = -1f;
            SavePrefs();
        }

        private static void SavePrefs()
        {
            _prefsDirtyAt = -1f;
            try
            {
                KMHPatchSettings s = KMHPatchMod.Settings;
                if (s == null) return;
                s.ChatVideoMuted  = _muted;
                s.ChatVideoVolume = _volume;
                s.ChatVideoLoop   = _loop;
                KMHPatchMod.SaveSettings();
            }
            catch (Exception ex) { KmhLog.Warn($"Chat video: saving playback settings threw - {ex.Message}"); }
        }

        private static void Fail(string why)
        {
            _state = State.Failed;
            _paused = false;
            _error = Shorten(why);
            KmhLog.Warn($"Chat video: {ChatImageCache.HostOf(_url)} - {why}");   // the full text, once, in the log
            ReleaseTarget();
        }

        // Unity reports failures with the whole url in them, and a signed media url is a thousand characters.
        internal static string Shorten(string why)
        {
            if (string.IsNullOrEmpty(why)) return "could not play";

            int link = why.IndexOf("http", StringComparison.OrdinalIgnoreCase);
            string text = (link >= 0 ? why.Substring(0, link) : why).Trim().TrimEnd(':', '-', ' ');
            if (text.Length == 0) text = "could not play";
            return text.Length <= 120 ? text : text.Substring(0, 117) + "…";
        }
    }
}
