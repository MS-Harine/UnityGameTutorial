namespace Blocks.Character
{
    /// <summary>How a character picks its target, scanned within its own targetRadius.</summary>
    public enum TargetingMode
    {
        /// <summary>The nearest damageable in range, whatever it is.</summary>
        NearestDamageable,

        /// <summary>The nearest damageable in range carrying the character's targetTag.</summary>
        TaggedDamageable
    }
}
