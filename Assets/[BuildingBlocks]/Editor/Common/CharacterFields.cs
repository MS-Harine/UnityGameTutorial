namespace Blocks
{
    /// <summary>
    /// Serialized property names on BuildingBlocksCharacter, so editor tooling can address its fields
    /// through SerializedProperty without hard-coding strings at each call site.
    /// </summary>
    public static class CharacterFields
    {
        public const string Stats = "stats";
        public const string OnEliminated = "onEliminated";
        public const string Delay = "delay";
        public const string ControlDelayAfterRespawn = "controlDelayAfterRespawn";
        public const string IsMovementEnabled = "isMovementEnabled";
        public const string IsTargetingEnabled = "isTargetingEnabled";
        public const string IsAttackEnabled = "isAttackEnabled";
        public const string Gravity = "gravity";
        public const string FallGravityMultiplier = "fallGravityMultiplier";
        public const string MaxFallSpeed = "maxFallSpeed";
        public const string GroundSlopeLimit = "groundSlopeLimit";
        public const string WallSlopeLimit = "wallSlopeLimit";
        public const string TargetingMode = "targetingMode";
        public const string TargetTag = "targetTag";
        public const string TargetRadius = "targetRadius";
        public const string TargetMemoryDuration = "targetMemoryDuration";
        public const string InvulnerabilityDuration = "invulnerabilityDuration";

        /// <summary>
        /// Serialized property names for a single entry in the character's Stats list.
        /// </summary>
        public static class Stat
        {
            public const string Type = "type";
            public const string Color = "color";
            public const string MaxValue = "maxValue";
            public const string StartValue = "startValue";
            public const string RegenRate = "regenRate";
            public const string RegenDelay = "regenDelay";
        }
    }
}
