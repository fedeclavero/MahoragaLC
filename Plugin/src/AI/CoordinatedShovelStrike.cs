using System.Collections.Generic;

namespace MahoragaLC.AI
{
    internal sealed class CoordinatedShovelStrike
    {
        private const int RequiredPlayers = 4;
        private const float OpeningWindow = 0.75f;
        private const float FollowUpWindow = 3f;
        private const float MinimumRepeatTime = 0.45f;

        private readonly Dictionary<ulong, float> _lastHitByPlayer = new Dictionary<ulong, float>(RequiredPlayers);
        private float _openedAt;
        private int _requiredHits;
        private int _hits;

        internal bool Register(ulong player, float now, int stage)
        {
            float elapsed = now - _openedAt;
            if (_hits == 0 || elapsed > FollowUpWindow ||
                (_lastHitByPlayer.Count < RequiredPlayers && elapsed > OpeningWindow))
            {
                Clear();
                elapsed = 0f;
                _openedAt = now;
                _requiredHits = RequiredPlayers + stage;
            }

            if (_lastHitByPlayer.TryGetValue(player, out float previous))
            {
                if (_lastHitByPlayer.Count < RequiredPlayers || now - previous < MinimumRepeatTime)
                {
                    return false;
                }
            }
            else if (_lastHitByPlayer.Count >= RequiredPlayers || elapsed > OpeningWindow)
            {
                return false;
            }

            _lastHitByPlayer[player] = now;
            _hits++;
            return _lastHitByPlayer.Count >= RequiredPlayers && _hits >= _requiredHits;
        }

        internal void Clear()
        {
            _hits = 0;
            _lastHitByPlayer.Clear();
        }
    }
}
