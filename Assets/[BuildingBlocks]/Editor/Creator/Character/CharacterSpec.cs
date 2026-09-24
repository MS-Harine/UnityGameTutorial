using System;
using UnityEditor;
using UnityEngine;
using Blocks.Attack;
using Blocks.Movement;
using Blocks.Character;
using System.Collections.Generic;

namespace Blocks
{
    /// <summary>Starting preset for a new character: a ready-made Player or melee Enemy, an empty shell, or none.</summary>
    enum CharacterTemplate
    {
        None,
        Player,
        MeleeEnemy,
        Empty
    }

    /// <summary>Whether the character spawns as the player (tagged, with the camera following) or as an enemy.</summary>
    enum CharacterMode
    {
        Player,
        Enemy
    }

    /// <summary>
    /// One configurable stat on a character: its type, max and start values, regen rate, and bar color.
    /// The short constructor fills the color and start value from the stat type's defaults.
    /// </summary>
    sealed class StatEntry
    {
        public StatType Type;
        public float MaxValue;
        public float StartValue;
        public float RegenRate;
        public Color Color;

        public StatEntry(StatType type, float maxValue, float regenRate = 0f)
            : this(type, maxValue, maxValue, regenRate, StatTypeDefaults.ColorFor(type)) { }

        public StatEntry(StatType type, float maxValue, float startValue, float regenRate, Color color)
        {
            Type = type;
            MaxValue = maxValue;
            StartValue = startValue;
            RegenRate = regenRate;
            Color = color;
        }
    }

    /// <summary>
    /// All the choices that define a character before it spawns: name, mode, stats, elimination behavior,
    /// movement and attack abilities, targeting, and sprite. Shared by the manual wizard and the AI flow,
    /// and consumed by the spawner.
    /// </summary>
    sealed class CharacterSpec
    {
        public string Name = "NewCharacter";
        public CharacterTemplate Template = CharacterTemplate.None;
        public CharacterMode Mode = CharacterMode.Player;

        public readonly List<StatEntry> Stats = new();

        public EliminationBehavior OnEliminated = EliminationBehavior.Respawn;
        public float EliminationDelay;

        public readonly List<Type> MovementAbilityTypes = new();
        public Type AttackAbilityType;

        public TargetingMode TargetingMode = TargetingMode.NearestDamageable;
        public string TargetTag = "Player";
        public float TargetRadius = 8f;

        /// <summary>
        /// Whether the character hunts for something to hit. Only armed enemies do — they need a target to
        /// walk up to and swing at. The player aims by facing where it is heading, so the sample Player
        /// keeps targeting switched off; turning it on would make it spin toward whatever wandered closest.
        /// </summary>
        public bool IsTargetingEnabled => AttackAbilityType != null && Mode == CharacterMode.Enemy;

        public Sprite Sprite;
        public string SpriteAiPrompt = string.Empty;
    }

    /// <summary>
    /// Fills a spec with the defaults for a chosen template (Player, melee Enemy, or Empty), resolving the
    /// ability components by name from the project.
    /// </summary>
    static class CharacterTemplates
    {
        public static void ApplyTo(CharacterSpec spec, CharacterTemplate template)
        {
            spec.Template = template;
            spec.Stats.Clear();
            spec.MovementAbilityTypes.Clear();
            spec.AttackAbilityType = null;

            switch (template)
            {
                case CharacterTemplate.Player:
                    spec.Mode = CharacterMode.Player;
                    spec.Name = "Player";
                    spec.Stats.Add(new StatEntry(StatType.Health, 100f));
                    spec.Stats.Add(new StatEntry(StatType.Stamina, 100f, regenRate: 10f));
                    spec.OnEliminated = EliminationBehavior.Respawn;
                    spec.EliminationDelay = 3f;
                    // The two abilities the sample Player.prefab ships with. PlayerMovementAbility is one
                    // component for walk, run and jump — it supersedes the separate Walk/Jump examples and
                    // warns if they run alongside it, so a new Player must not get those instead.
                    AddMovementByName(spec, "PlayerMovementAbility");
                    spec.AttackAbilityType = FindAttackByName("PlayerAttackAbility");
                    break;

                case CharacterTemplate.MeleeEnemy:
                    spec.Mode = CharacterMode.Enemy;
                    spec.Name = "MeleeEnemy";
                    spec.Stats.Add(new StatEntry(StatType.Health, 30f));
                    spec.OnEliminated = EliminationBehavior.Destroy;
                    spec.EliminationDelay = 0.5f;
                    AddMovementByName(spec, "EnemyMovementAbility");
                    spec.AttackAbilityType = FindAttackByName("EnemyMeleeAttackAbility");
                    spec.TargetingMode = TargetingMode.TaggedDamageable;
                    spec.TargetTag = "Player";
                    spec.TargetRadius = 7f;
                    break;

                case CharacterTemplate.Empty:
                    spec.Mode = CharacterMode.Player;
                    spec.Name = "NewCharacter";
                    spec.Stats.Add(new StatEntry(StatType.Health, 100f));
                    spec.OnEliminated = EliminationBehavior.Respawn;
                    spec.EliminationDelay = 0f;
                    break;
            }
        }

        static void AddMovementByName(CharacterSpec spec, string typeName)
        {
            Type type = FindMovementByName(typeName);
            if (type != null && !spec.MovementAbilityTypes.Contains(type))
            {
                spec.MovementAbilityTypes.Add(type);
            }
        }

        static Type FindMovementByName(string typeName)
        {
            foreach (Type t in TypeCache.GetTypesDerivedFrom<MovementAbility>())
            {
                if (!t.IsAbstract && t.Name == typeName) return t;
            }
            return null;
        }

        static Type FindAttackByName(string typeName)
        {
            foreach (Type t in TypeCache.GetTypesDerivedFrom<AttackAbility>())
            {
                if (!t.IsAbstract && t.Name == typeName) return t;
            }
            return null;
        }
    }

    /// <summary>
    /// A Unity-serializable snapshot of a <see cref="CharacterSpec"/>. The Creator window can only carry
    /// state across a domain reload through serialized fields, but CharacterSpec holds System.Type
    /// references and readonly lists that Unity cannot serialize. This mirrors the spec with ability types
    /// stored as assembly-qualified name strings, so an in-progress character survives a recompile.
    /// </summary>
    [Serializable]
    sealed class CharacterSpecData
    {
        public string Name;
        public CharacterTemplate Template;
        public CharacterMode Mode;
        public List<StatEntryData> Stats = new();
        public EliminationBehavior OnEliminated;
        public float EliminationDelay;
        public List<string> MovementAbilityTypes = new();
        public string AttackAbilityType;
        public TargetingMode TargetingMode;
        public string TargetTag;
        public float TargetRadius;
        public Sprite Sprite;
        public string SpriteAiPrompt;

        public static CharacterSpecData From(CharacterSpec spec)
        {
            var data = new CharacterSpecData
            {
                Name = spec.Name,
                Template = spec.Template,
                Mode = spec.Mode,
                OnEliminated = spec.OnEliminated,
                EliminationDelay = spec.EliminationDelay,
                AttackAbilityType = spec.AttackAbilityType?.AssemblyQualifiedName,
                TargetingMode = spec.TargetingMode,
                TargetTag = spec.TargetTag,
                TargetRadius = spec.TargetRadius,
                Sprite = spec.Sprite,
                SpriteAiPrompt = spec.SpriteAiPrompt,
            };

            foreach (StatEntry stat in spec.Stats)
                data.Stats.Add(StatEntryData.From(stat));
            foreach (Type t in spec.MovementAbilityTypes)
                if (t != null) data.MovementAbilityTypes.Add(t.AssemblyQualifiedName);

            return data;
        }

        public CharacterSpec ToSpec()
        {
            var spec = new CharacterSpec
            {
                Name = Name,
                Template = Template,
                Mode = Mode,
                OnEliminated = OnEliminated,
                EliminationDelay = EliminationDelay,
                AttackAbilityType = Resolve<AttackAbility>(AttackAbilityType),
                TargetingMode = TargetingMode,
                TargetTag = TargetTag,
                TargetRadius = TargetRadius,
                Sprite = Sprite,
                SpriteAiPrompt = SpriteAiPrompt,
            };

            spec.Stats.Clear();
            if (Stats != null)
                foreach (StatEntryData stat in Stats)
                    spec.Stats.Add(stat.ToEntry());

            if (MovementAbilityTypes != null)
                foreach (string key in MovementAbilityTypes)
                {
                    Type t = Resolve<MovementAbility>(key);
                    if (t != null && !spec.MovementAbilityTypes.Contains(t))
                        spec.MovementAbilityTypes.Add(t);
                }

            return spec;
        }

        /// <summary>
        /// Resolves a stored type key back to a Type. Prefers the assembly-qualified name, then falls back
        /// to matching a known subclass by full or simple name, so an ability that was renamed between the
        /// edit and the reload is dropped rather than throwing.
        /// </summary>
        static Type Resolve<T>(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;

            Type direct = Type.GetType(key);
            if (direct != null) return direct;

            foreach (Type t in TypeCache.GetTypesDerivedFrom<T>())
            {
                if (t.IsAbstract) continue;
                if (t.AssemblyQualifiedName == key || t.FullName == key || t.Name == key) return t;
            }
            return null;
        }
    }

    /// <summary>Unity-serializable form of a <see cref="StatEntry"/> for carrying a character draft across a domain reload.</summary>
    [Serializable]
    struct StatEntryData
    {
        public StatType Type;
        public float MaxValue;
        public float StartValue;
        public float RegenRate;
        public Color Color;

        public static StatEntryData From(StatEntry e) => new StatEntryData
        {
            Type = e.Type,
            MaxValue = e.MaxValue,
            StartValue = e.StartValue,
            RegenRate = e.RegenRate,
            Color = e.Color,
        };

        public StatEntry ToEntry() => new StatEntry(Type, MaxValue, StartValue, RegenRate, Color);
    }
}
