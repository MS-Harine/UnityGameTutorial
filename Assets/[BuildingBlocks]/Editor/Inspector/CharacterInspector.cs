using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Blocks.Character;

namespace Blocks
{
    /// <summary>
    /// Custom inspector for BuildingBlocksCharacter. Replaces the default fields with the Building Blocks
    /// module cards (stats, elimination, movement, targeting, attack, hit reaction), plus anything an
    /// optional sample has registered.
    /// </summary>
    [CustomEditor(typeof(BuildingBlocksCharacter))]
    [CanEditMultipleObjects]
    sealed class BuildingBlocksCharacterEditor : Editor
    {
        /// <summary>
        /// One card in the inspector: its sort position, and a factory rather than an instance because each
        /// inspector needs cards of its own to bind to its own SerializedObject.
        /// </summary>
        readonly struct CardEntry
        {
            public readonly int Order;
            public readonly Func<ModuleCard> Make;

            public CardEntry(int order, Func<ModuleCard> make)
            {
                Order = order;
                Make = make;
            }
        }

        static readonly List<CardEntry> k_Cards = new List<CardEntry>();

        static BuildingBlocksCharacterEditor()
        {
            RegisterCard(10, () => new StatsCard());
            RegisterCard(20, () => new EliminationCard());
            RegisterCard(30, () => new MovementCard());
            RegisterCard(40, () => new TargetingCard());
            RegisterCard(50, () => new AttackCard());
            RegisterCard(60, () => new HitReactionCard());
        }

        /// <summary>
        /// Adds a card to the character inspector. The built-in cards run 10 to 60, so a negative order puts
        /// a card above all of them. An optional sample registers its own from an
        /// <see cref="InitializeOnLoadAttribute"/> hook, which is how the Assistant card appears only once
        /// that sample is imported.
        /// </summary>
        /// <param name="order">Sort position among the cards.</param>
        /// <param name="make">Builds the card. Called once per inspector.</param>
        public static void RegisterCard(int order, Func<ModuleCard> make)
        {
            if (make == null) return;

            k_Cards.Add(new CardEntry(order, make));
            k_Cards.Sort((a, b) => a.Order.CompareTo(b.Order));
        }

        public override VisualElement CreateInspectorGUI()
        {
            var root = new VisualElement();
            root.AddToClassList("blocks-inspector");
            root.style.paddingTop = 4;

            BBStyles.Apply(root);

            foreach (CardEntry entry in k_Cards)
                AddCard(root, entry.Make());

            return root;
        }

        void AddCard(VisualElement parent, ModuleCard card)
        {
            card.Bind(serializedObject);
            parent.Add(card);
        }
    }

    /// <summary>
    /// Assigns the Building Blocks thumbnail as the script icon for BuildingBlocksCharacter once, on editor load.
    /// </summary>
    [InitializeOnLoad]
    static class CharacterScriptIconSetup
    {
        const string k_IconPath = "Assets/[BuildingBlocks]/Art/UI/thumbnail.png";

        static CharacterScriptIconSetup()
        {
            EditorApplication.delayCall += Apply;
        }

        static void Apply()
        {
            var icon = AssetDatabase.LoadAssetAtPath<Texture2D>(k_IconPath);
            if (icon == null) return;

            foreach (var script in MonoImporter.GetAllRuntimeMonoScripts())
            {
                if (script.GetClass() != typeof(BuildingBlocksCharacter)) continue;

                var path = AssetDatabase.GetAssetPath(script);
                var importer = AssetImporter.GetAtPath(path) as MonoImporter;
                if (importer == null) return;

                if (importer.GetIcon() == icon) return;

                importer.SetIcon(icon);
                importer.SaveAndReimport();
                return;
            }
        }
    }
}
