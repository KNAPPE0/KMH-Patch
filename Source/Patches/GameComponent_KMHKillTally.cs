using System.Collections.Generic;
using Verse;

namespace KMHPatch.Patches
{
    // A GameComponent so hunt progress is saved with the game and survives a reload.
    public class GameComponent_KMHKillTally : GameComponent
    {
        private Dictionary<string, int> _kills = new Dictionary<string, int>(System.StringComparer.OrdinalIgnoreCase);
        private int _colonistDeaths;

        public GameComponent_KMHKillTally(Game game) { }

        public static GameComponent_KMHKillTally Current => Verse.Current.Game?.GetComponent<GameComponent_KMHKillTally>();

        public int KillsOf(string defName)
            => !string.IsNullOrEmpty(defName) && _kills.TryGetValue(defName, out int n) ? n : 0;

        public int ColonistDeaths => _colonistDeaths;
        public void BumpDeath() => _colonistDeaths++;

        public void Bump(string key)
        {
            if (string.IsNullOrEmpty(key)) return;
            _kills.TryGetValue(key, out int n);
            _kills[key] = n + 1;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref _kills, "kmhKills", LookMode.Value, LookMode.Value);
            Scribe_Values.Look(ref _colonistDeaths, "kmhColonistDeaths", 0);
            // Scribe drops the case-insensitive comparer on load.
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
                _kills = new Dictionary<string, int>(_kills ?? new Dictionary<string, int>(),
                                                     System.StringComparer.OrdinalIgnoreCase);
        }
    }
}
