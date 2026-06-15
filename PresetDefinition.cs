using BepInEx.Configuration;
using UnityEngine;

namespace TCGWorkbenchPresets
{
    internal readonly struct PresetDefinition
    {
        internal readonly string Label;
        internal readonly Color TierColor;
        internal readonly ConfigEntry<KeyCode> Key;
        internal readonly ConfigEntry<float> Min;
        internal readonly ConfigEntry<float> Max;

        internal PresetDefinition(string label, Color tierColor,
            ConfigEntry<KeyCode> key, ConfigEntry<float> min, ConfigEntry<float> max)
        {
            Label     = label;
            TierColor = tierColor;
            Key       = key;
            Min       = min;
            Max       = max;
        }
    }
}
