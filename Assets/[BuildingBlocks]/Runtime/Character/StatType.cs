namespace Blocks.Character
{
    /// <summary>
    /// Registry of all stat types. Add entries here to define new stats.
    /// Health is special: the stat carrying this type drives elimination when it empties.
    /// </summary>
    public enum StatType
    {
        Health = 0,
        Mana = 1,
        Stamina = 2
    }
}
