using System;
using System.Collections.Generic;

namespace MahoragaLC.Config
{
    /// <summary>
    /// Pure parser for comma-separated "MoonName:Weight" config strings.
    /// Kept independent of Unity/BepInEx types for unit testing.
    /// </summary>
    public static class MoonRarityParser
    {
        public static Dictionary<string, int> Parse(string? raw)
        {
            var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(raw))
            {
                return result;
            }

            string[] tokens = raw.Split(',', StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < tokens.Length; i++)
            {
                string token = tokens[i].Trim();
                int sep = token.LastIndexOf(':');
                if (sep <= 0 || sep >= token.Length - 1)
                {
                    continue;
                }

                string key = token.Substring(0, sep).Trim();
                string valuePart = token.Substring(sep + 1).Trim();
                if (string.IsNullOrEmpty(key))
                {
                    continue;
                }

                if (int.TryParse(valuePart, out int weight))
                {
                    result[key] = Math.Max(0, weight);
                }
            }

            return result;
        }
    }
}
