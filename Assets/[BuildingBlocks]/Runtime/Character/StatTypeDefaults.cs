using UnityEngine;

namespace Blocks.Character
{
    /// <summary>
    /// The bar colour each stat type gets in the HUD. A type with no entry here falls back to a hue
    /// derived from its name, so a newly added <see cref="StatType"/> still reads as its own bar.
    /// </summary>
    public static class StatTypeDefaults
    {
        public static Color ColorFor(StatType type)
        {
            switch (type)
            {
                case StatType.Health: return new Color(0.85f, 0.25f, 0.30f, 1f);
                case StatType.Mana: return new Color(0.30f, 0.55f, 0.95f, 1f);
                case StatType.Stamina: return new Color(0.40f, 0.80f, 0.40f, 1f);
                default:
                    int hash = type.ToString().GetHashCode();
                    float hue = Mathf.Repeat((hash & 0xFFFF) / 65535f, 1f);
                    return Color.HSVToRGB(hue, 0.65f, 0.9f);
            }
        }
    }
}
