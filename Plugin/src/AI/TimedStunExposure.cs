using System;
using System.Collections.Generic;

namespace MahoragaLC.AI
{
    internal sealed class TimedStunExposure
    {
        private readonly Dictionary<Type, int> _exposures = new Dictionary<Type, int>();

        internal float Register(Type source, out bool wheelTurns)
        {
            _exposures.TryGetValue(source, out int count);
            wheelTurns = count == 2;
            if (count < 3)
            {
                _exposures[source] = ++count;
            }
            return count >= 3 ? 0.5f : 1f;
        }
    }
}
