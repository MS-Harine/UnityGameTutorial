namespace Blocks
{
    public static class AgentPrompts
    {
        #region Agent identity & system prompt

        public const string AgentId = "building_blocks_proposal_agent";
        public const string AgentName = "Building Blocks Proposal Agent";
        public const string AgentDescription = "Designs Building Blocks characters and abilities by inferring " +
                                               "a spec from user intent and rendering it in the Creator " +
                                               "window for review.";

        public const string SystemPrompt =
            "You design Building Blocks characters and abilities inside the Unity Editor. You never build anything " +
            "yourself: you infer a spec and call exactly one render tool, and the Editor spawns / writes / applies it " +
            "after the user approves.\n" +
            "\n" +
            "Discover what already exists before you propose anything:\n" +
            "- New character: call BuildingBlocks.ReadAbilityCatalog(\"movement\") AND " +
            "BuildingBlocks.ReadAbilityCatalog(\"attack\").\n" +
            "- New ability: call BuildingBlocks.ListAbilityTemplates, then BuildingBlocks.ReadAbilityCatalog(kind).\n" +
            "Each render tool's parameters document every field — follow them.\n" +
            "\n" +
            "Call exactly one render tool:\n" +
            "- BuildingBlocks.RenderCharacterProposal — a new player or enemy.\n" +
            "- BuildingBlocks.RenderAbilityProposal — a new movement or attack ability.\n" +
            "- Character already SELECTED in the Editor: call BuildingBlocks.ReadSelectedCharacter " +
            "FIRST, then BuildingBlocks.RenderCharacterEdit (a PATCH — only the fields that change) for a change, or " +
            "BuildingBlocks.RenderCharacterAnswer for a question.\n" +
            "\n" +
            "Hard rules:\n" +
            "- NEVER spawn anything into the scene. NEVER write or edit files. A render tool is the ONLY way to " +
            "deliver a design.\n" +
            "- Use ONLY ability class names returned by ReadAbilityCatalog / ListAbilities. If what you want isn't " +
            "there, leave the slot empty and propose it via the Suggested* fields — never invent a class name.\n" +
            "- Every RenderCharacterProposal MUST fill all three Suggested* fields — SuggestedMovementAbility, " +
            "SuggestedAttackAbility, AND SuggestedSprite — each with a { Title, Prompt }, even for a complete-looking " +
            "character. They are optional creative extensions on the review screen, not gaps to leave empty.\n" +
            "- The ability Template field accepts ONLY the values from BuildingBlocks.ListAbilityTemplates " +
            "(InstantAction / TimedBurst / ContinuousHold). These are lifecycle shapes, not behaviours like " +
            "'Projectile' or 'Beam'.\n" +
            "- Rationale / Summary text is user-facing: write plain language, and don't name the lifecycle template " +
            "enum or internal systems that aren't scripts the user can open.\n" +
            "- Only name templates, abilities, runtime systems, or files that appeared in a tool response. If you " +
            "didn't pull it from a tool, don't write it.";

        #endregion

        #region Validate & fix skill

        /// <summary>
        /// Folder/frontmatter name of the character validation skill. The skill ships in this sample's
        /// TemplateSkills folder; once imported, the Assistant discovers SKILL.md files anywhere under
        /// Assets, so the prompt can reference it by name alone.
        /// </summary>
        public const string ValidateSkillName = "validate-building-blocks-character";

        /// <summary>
        /// The chat message sent by the "Validate and fix" component menu — intentionally minimal so the chat
        /// stays clean on screen. All instructions live in the skill; the selected object and its current
        /// setup are supplied through the attached snapshot.
        /// </summary>
        public static readonly string ValidatePrompt = $"Use the \"{ValidateSkillName}\" skill.";

        #endregion

        #region Ability catalog tool

        public const string ToolReadAbilityCatalog =
            "Returns the full picture of one ability kind in the project: every concrete subclass with its " +
            "source, plus the abstract parent (MovementAbility or AttackAbility) and the CharacterAbility " +
            "lifecycle base. Use this BEFORE proposing a new ability so your rationale can recommend an " +
            "existing class if one fits, describe deltas if a similar one exists, or cite the " +
            "parent-class API if nothing is close. One call returns the whole vocabulary for this kind.";

        public const string ParamReadAbilityCatalogKind =
            "\"movement\" or \"attack\". Picks which family of abilities (and which abstract " +
            "parent) to dump.";

        public const string ReadAbilityCatalogGuidance =
            "Use this to decide what to put in the proposal's `rationale` + `tips`:\n" +
            "- If one of the concrete classes below already covers the user's intent, your tips should recommend that class (with configuration values) instead of creating a near-duplicate.\n" +
            "- If a close-but-different class exists, your tips should call out which one you started from and what differs.\n" +
            "- If nothing fits, your tips should cite specific APIs from the abstract parent + CharacterAbility below that the user will need in the body.";

        public const string ReadAbilityCatalogLifecycleCaption =
            "Lifecycle base (CharacterAbility) — protected hooks (OnAbilityStarted/Stopped, OnUpdate, OnFixedUpdate, BindInput, Character helpers):";

        #endregion
    }

    public static class CharacterProposalPrompts
    {
        #region List abilities tool

        public const string ToolListAbilities =
            "Lists the MovementAbility and AttackAbility subclasses that currently exist in the project, with " +
            "a one-line description of each. ALWAYS call this before RenderCharacterProposal and use only " +
            "the names returned here in MovementAbilityNames / AttackAbilityName — anything else won't " +
            "resolve in the project. If the catalog is missing something the user wants, leave the slot " +
            "empty and propose it via SuggestedMovementAbility / SuggestedAttackAbility instead.";

        public const string ListAbilitiesFooter =
            "Only use names from the lists above for MovementAbilityNames / AttackAbilityName. If a name " +
            "you want isn't here, leave the slot empty and propose it via SuggestedMovementAbility / " +
            "SuggestedAttackAbility so the user can build it.";

        #endregion

        #region Render proposal tool & parameters

        public const string ToolRenderCharacterProposal =
            "Renders the AI's proposed character spec inside the Building Blocks Creator window so the user " +
            "can review, edit, sprite-pick, and spawn it. Call this AFTER inferring the spec from the " +
            "user's intent. DO NOT spawn anything yourself and DO NOT call the " +
            "Write tool — the Editor spawns the character into the scene from the approved spec. Ability " +
            "components are referenced by class name (e.g. 'JumpAbility', 'EnemyMeleeAttackAbility'); the " +
            "Editor resolves them to types and flags any that don't exist in the project.";

        public const string ParamUserPrompt =
            "The user's original intent that triggered this proposal. Echoed back in the review screen.";

        public const string ParamRationale =
            "ONLY the differences from the script defaults — HP/Range/regen/targeting tweaks. " +
            "1–2 sentences. Do NOT restate the abilities here (those are visible in the " +
            "editable Movement / Attack rows). Example: 'HP 30 instead of the 100 default " +
            "and TargetRadius 6 instead of 8 — both pushed down so it reads small and " +
            "short-sighted.' Empty string if you accepted all defaults.";

        public const string ParamProposal =
            "The proposed character spec. Fields:\n" +
            "- Kind: \"player\" or \"enemy\". Determines spawn mode (Player gets the Player " +
            "tag and camera follow rig; Enemy does not).\n" +
            "- Name: GameObject name when spawned (PascalCase, e.g. 'RogueAssassin').\n" +
            "- Summary: REQUIRED 1–2 sentence framing of the character as a whole — the " +
            "role/feel, not the numbers. Example: 'A fragile melee grunt that closes " +
            "distance fast and folds under return fire — built to swarm, not duel.' Shown " +
            "at the top of the green 'WHAT'S IN <NAME>' card. Distinct from Rationale " +
            "(which is the deltas-only stat tweak summary). Empty string only if you truly " +
            "have nothing to add beyond the obvious archetype.\n" +
            "- Stats: list of { Type (\"Health\" | \"Mana\" | \"Stamina\" or custom StatType " +
            "name), MaxValue, RegenRate }. Most characters need at least Health.\n" +
            "- OnEliminated: \"Respawn\" | \"Disable\" | \"Destroy\".\n" +
            "- EliminationDelay: seconds before the elimination behavior fires.\n" +
            "- MovementAbilityNames: class names of existing MovementAbility subclasses to " +
            "attach (e.g. ['WalkAbility','JumpAbility']). Use only names returned by " +
            "BuildingBlocks.ReadAbilityCatalog / ListAbilities. Leave empty for stationary " +
            "characters.\n" +
            "- AttackAbilityName: class name of an existing AttackAbility subclass to attach " +
            "(e.g. 'EnemyMeleeAttackAbility'). Empty string for non-attackers.\n" +
            "- TargetingMode: \"NearestDamageable\" | \"TaggedDamageable\". Only relevant if " +
            "AttackAbilityName is set.\n" +
            "- TargetTag: tag to seek (used by TaggedDamageable). Enemies typically target " +
            "\"Player\".\n" +
            "- TargetRadius: search radius in meters.\n" +
            "- SuggestedMovementAbility: REQUIRED { Title, Prompt }. Always populate — even " +
            "when an attached movement ability already covers the archetype — with a " +
            "creative extension that fits the character's flavor (a wall-cling for a " +
            "nimble character, a charge for a brute, a burrow for a goblin). The review " +
            "screen frames these as optional creative extensions, not as gaps the user " +
            "needs to fill. Title is a 2–4 word noun phrase (e.g. 'Wall-cling jump'). " +
            "Prompt is the one-liner intent a user would type into the AI Movement Ability " +
            "card.\n" +
            "- SuggestedAttackAbility: REQUIRED { Title, Prompt } same shape, for building " +
            "an attack ability. Always populate with a creative extension that " +
            "fits the archetype (e.g. 'Poison shank' for a goblin).\n" +
            "- SuggestedSprite: REQUIRED { Title, Prompt }. The Editor sends \"Generate a " +
            "sprite with <Prompt>\" to the full Unity AI Assistant when the user clicks " +
            "Build with AI. Title is a 2–4 word label shown on the card (typically the " +
            "character's archetype, e.g. 'Goblin Grunt'). Prompt is a single descriptive " +
            "phrase covering subject + style + view + pose + background — do NOT include " +
            "the words 'generate' or 'sprite' (the Editor prefixes them). Example Prompt: " +
            "'a small green goblin in tattered leather, 2D pixel-art side view, idle pose, " +
            "transparent background'. Always populate, even for a generic player.\n" +
            "- Tips: optional list of 0–3 tail-advice sentences for things that don't fit any " +
            "other field (gotchas, tag requirements, unresolved-ability warnings). " +
            "Examples:\n" +
            "    • 'Will only target characters tagged \"Player\". Tag your player GameObject " +
            "if you haven't.'\n" +
            "    • 'You left AttackAbilityName empty — this character won't fight back until " +
            "you attach one.'\n" +
            "  Each tip is one sentence, plain prose, no markdown. Omit entirely if you have " +
            "nothing useful to add.";

        #endregion

        #region Prompt builders

        public static string BuildDesignCharacter(string intent)
        {
            return $"Design a Building Blocks character based on this intent: " +
                   $"\"{intent}\".\n\n" +
                   "IMPORTANT: This request is coming from the Building Blocks Creator window. " +
                   "DO NOT spawn anything in the scene. DO NOT write any files. " +
                   "Instead, infer the full character spec (including whether this is a Player or an Enemy) " +
                   "and call the `BuildingBlocks.RenderCharacterProposal` tool so the user can review and " +
                   "approve it. " +
                   "Include a 1–2 sentence rationale explaining why you picked these stats, abilities, and " +
                   "defaults. " +
                   "The Editor will spawn the character into the scene from the approved spec.";
        }

        public static string BuildSpritePrompt(string description) =>
            $"Generate a sprite with {description}";

        #endregion
    }

    public static class AbilityProposalPrompts
    {
        #region Lifecycle templates

        public const string ToolListAbilityTemplates =
            "Lists the lifecycle templates available for new abilities — these are the ONLY valid values for " +
            "the Template field on RenderAbilityProposal. Each entry has the lifecycle shape and the " +
            "wizard's default timer / cooldown / activation values. Also lists the valid Trigger (OnInput " +
            "/ Auto) enum values. ALWAYS call this before " +
            "RenderAbilityProposal so the rationale you write is grounded in real template names — do not " +
            "invent template names like 'Projectile' or 'Beam' that aren't in this list.";

        public const string ListAbilityTemplatesHeader =
            "Ability lifecycle templates (Template field on RenderAbilityProposal):";

        public const string ShapeInstantAction =
            "Trigger → strike once → cooldown → can trigger again. Examples: sword swing, single shot, throw, enemy auto-melee.";

        public const string ShapeTimedBurst =
            "Trigger → runs for a fixed window → auto-stops → cooldown. Examples: spin attack, short combo, burst fire, fixed-charge shot (fire in OnAbilityStopped).";

        public const string ShapeContinuousHold =
            "Trigger → stays active until something stops it (input released, AI decides, stat depleted). Examples: aim, block, hold-to-fire, channelled beam, flamethrower. Pairs naturally with per-second stat drain.";

        public const string ListAbilityTemplatesTriggerLine =
            "Trigger (Trigger field): OnInput (player presses/holds a key — default for player abilities) | Auto (driven by game logic in OnUpdate — enemies, bosses, ambient effects).";

        public const string ListAbilityTemplatesFooter =
            "These are the only valid template names. Do not invent others.";

        #endregion

        #region Render proposal tool & parameters

        public const string ToolRenderAbilityProposal =
            "Renders the AI's proposed ability spec inside the Building Blocks Creator window so the user can " +
            "review, edit, and approve it. Call this AFTER ListAbilityTemplates AND " +
            "ReadAbilityCatalog(kind) — the catalog gives you every existing concrete subclass plus the " +
            "abstract parent, so your rationale + ExistingMatch + FromScratchSnippets can be grounded in " +
            "real code. DO NOT call the Write tool — the Editor writes the file from the approved spec " +
            "using the same generator the wizard uses. The proposal carries every field the wizard's spec " +
            "carries, a short rationale, an optional ExistingMatch (when a class in the catalog already " +
            "covers or nearly covers the intent), and an optional list of FromScratchSnippets (specific " +
            "code-shaped suggestions for the body if the user builds new).";

        public const string ParamUserPrompt =
            "The user's original intent that triggered this proposal. Echoed back in the review screen.";

        public const string ParamRationale =
            "A short 1–2 sentence rationale explaining WHY you picked this template and these " +
            "defaults for a from-scratch build. Shown in the 'BUILD FROM SCRATCH' card " +
            "above the editable spec. Keep it tight — code-shaped guidance belongs in " +
            "FromScratchSnippets, and existing-class advice belongs in ExistingMatch.";

        public const string ParamProposal =
            "The proposed ability spec. Fields:\n" +
            "- Kind: \"movement\" or \"attack\".\n" +
            "- Template: \"InstantAction\" (fires once, cools down), \"TimedBurst\" (runs for " +
            "a fixed duration, then auto-stops), \"ContinuousHold\" (stays active until " +
            "stopped). Lifecycle is orthogonal to Trigger.\n" +
            "- ClassName: PascalCase ending in 'Ability' (e.g. 'WallJumpAbility').\n" +
            "- Namespace: default 'Blocks.Movement.Examples' for movement / 'Blocks.Attack' " +
            "for attack.\n" +
            "- OutputFolderPath: default 'Assets/[BuildingBlocks]/Runtime/Movement/Examples' " +
            "for movement / 'Assets/[BuildingBlocks]/Runtime/Attack' for attack.\n" +
            "- InputFieldName: camelCase, e.g. 'jumpAction'. Ignored when Trigger=Auto.\n" +
            "- Trigger: \"OnInput\" (player presses/holds a key — the default for player " +
            "abilities) or \"Auto\" (driven by game logic — picks for enemy/boss/AI " +
            "abilities or ambient effects with no input). When the user's intent mentions " +
            "an enemy, boss, AI, or describes behavior with no input verb (\"chases\", " +
            "\"patrols\", \"emits aura\"), pick Auto. When in doubt for a player ability, " +
            "pick OnInput.\n" +
            "- UseStat: true if the ability consumes a stat.\n" +
            "- StatType: \"Health\" | \"Mana\" | \"Stamina\".\n" +
            "- StatCost: one-shot amount, or per-second drain if StatPerSecond=true.\n" +
            "- StatPerSecond: true for beams/channels, false for swings/shots/jumps.\n" +
            "- Damage / Range: attack only.\n" +
            "- ExistingMatch: OPTIONAL. Set ONLY when a concrete subclass returned by " +
            "ReadAbilityCatalog already covers the intent (same or nearly the same), so " +
            "the user can reuse it instead of writing a near-duplicate. Set to null " +
            "otherwise — do not invent a match. Fields:\n" +
            "    · ClassName: exact name of the existing class (e.g. " +
            "'ProjectileAttackAbility').\n" +
            "    · Summary: one-sentence description of what the existing class does today, " +
            "grounded in its source.\n" +
            "    · ConfigurationTips: 0–2 sentences for how to USE IT AS-IS — concrete values " +
            "to set in the Inspector (e.g. 'Drop it on the Player and set Damage=15, " +
            "Cooldown=1, Range=8.'). Empty list if none.\n" +
            "    · ModificationTips: 0–2 sentences for how to TWEAK IT to better fit the " +
            "user's intent — name the override / delta in plain prose (e.g. 'Subclass it " +
            "and override OnHit to spawn an AoE explosion.'). Empty list if none.\n" +
            "- FromScratchSnippets: OPTIONAL list of 1–3 short plain-prose sentences shown as " +
            "bullets in the 'BUILD FROM SCRATCH' card. Cite specific APIs from the " +
            "abstract parent / CharacterAbility the user will need in the body (e.g. 'Use " +
            "Character.HitBox(offset, size, Damage, HitReaction.PushBack(speed)) for damage + shove and " +
            "Character.NotifyAttackPerformed() so animation triggers fire.'). One sentence " +
            "per snippet, no markdown, no code fences. Omit / empty if the rationale " +
            "already says enough.";

        #endregion

        #region Prompt builders

        public static string BuildDesignAbility(string intent)
        {
            return $"Design a Building Blocks ability based on this intent: \"{intent}\".\n\n" +
                   "IMPORTANT: This request is coming from the Building Blocks Creator window. " +
                   "DO NOT write the file with the Write tool. " +
                   "Instead, infer the full spec and call the `BuildingBlocks.RenderAbilityProposal` tool so " +
                   "the user can review and approve it. " +
                   "Include a 1–2 sentence rationale explaining why you picked the template and key defaults. " +
                   "The Editor will write the script from the approved spec using the same generator the " +
                   "wizard uses.";
        }

        public static string BuildCodegenPrompt(string assetPath) =>
            $"Write `{assetPath}` from the approved spec attached below. " +
            "Honor every field verbatim, do not re-infer it, and do not " +
            "call `BuildingBlocks.RenderAbilityProposal`. " +
            "Use the `Write` tool to create the file and fill the body with a real implementation derived " +
            "from the original intent in the attachment.";

        #endregion
    }

    public static class CharacterEditPrompts
    {
        #region Prompt builder

        public static string BuildTweakCharacter(string intent) =>
            $"Tweak the currently selected Building Blocks character. The user " +
            $"said: \"{intent}\".\n\n" +
            "IMPORTANT: This request is coming from the Building Blocks character Inspector. " +
            "First call `BuildingBlocks.ReadSelectedCharacter` to read the character's current state. " +
            "DO NOT spawn anything in the scene and DO NOT write any files.\n\n" +
            "Then decide which ONE tool to call:\n" +
            "- If the user is ASKING A QUESTION about the character (how much HP, which abilities, why it " +
            "behaves a certain way, what a setting does), call `BuildingBlocks.RenderCharacterAnswer` " +
            "with a concise plain-language answer grounded in the values you just read. Do NOT propose " +
            "any change.\n" +
            "- If the user is REQUESTING A CHANGE, infer ONLY the fields that should change and call " +
            "`BuildingBlocks.RenderCharacterEdit` so the user can review and apply the diff in the " +
            "Inspector, with a 1–2 sentence rationale. The Editor applies the approved changes to the " +
            "selected component.";

        #endregion

        #region Tool & parameter descriptions

        public const string ToolReadSelectedCharacter =
            "Reads the Building Blocks character currently selected in the Editor: its name, player/enemy " +
            "tag, every stat (type / max / regen), elimination behavior, the movement/targeting/attack " +
            "enable flags, gravity + slope tuning, targeting mode / tag / radius, and the movement + " +
            "attack ability components attached to it. ALWAYS call this FIRST for an edit request so " +
            "RenderCharacterEdit only changes fields that genuinely differ from what's already there. " +
            "Returns an error string if nothing (or a non-character) is selected.";

        public const string ToolReadComponentFields =
            "Lists the EDITABLE fields of a component on the selected character's object or children, with " +
            "each field's display label, serialized property path, type, and CURRENT value (plus the " +
            "valid options for enums). Call this BEFORE putting anything in RenderCharacterEdit's " +
            "ComponentEdits so the Component / Field / Index names are exact and the values you propose " +
            "are honest deltas from what's already there. Only simple value fields are listed (numbers, " +
            "booleans, text, enums, colors, Vector2/3); object references (prefabs, input actions, " +
            "transforms), curves, and arrays are intentionally omitted because they can't be set from " +
            "text. Type names come from BuildingBlocks.ReadSelectedCharacter.";

        public const string ToolRenderCharacterEdit =
            "Renders a proposed edit to the SELECTED Building Blocks character as an inline before→after diff " +
            "in the Inspector, with Apply / Discard buttons. Call this AFTER ReadSelectedCharacter (and " +
            "ReadAbilityCatalog if abilities are involved). This is a PATCH: set ONLY the fields that " +
            "should change — every field left null / omitted is kept as-is. DO NOT spawn anything and DO " +
            "NOT call the Write tool; the Editor applies the approved changes to the live component " +
            "through Undo. Ability names must be concrete subclasses that exist in the project (from " +
            "ReadAbilityCatalog / ListAbilities); names that don't resolve are surfaced to the user as " +
            "suggestions instead of being applied.";

        public const string ToolRenderCharacterAnswer =
            "Answers a QUESTION about the SELECTED Building Blocks character WITHOUT changing it — the answer " +
            "is shown as read-only text in the Inspector. Call this INSTEAD of RenderCharacterEdit when " +
            "the user is asking about the character (how much HP it has, which abilities are attached, " +
            "why it behaves a certain way, what a setting does) rather than requesting a change. ALWAYS " +
            "call ReadSelectedCharacter FIRST so the answer is grounded in the character's real values. " +
            "Do NOT propose any edit from this tool.";

        public const string ParamReadComponentFieldsComponentType =
            "Component type name to inspect, e.g. \"DashAbility\", \"Rigidbody2D\", " +
            "\"SpriteRenderer\". As listed by ReadSelectedCharacter. Case-insensitive.";

        public const string ParamReadComponentFieldsIndex =
            "Which instance of that type to inspect when more than one exists on the object " +
            "(0-based, in hierarchy order). Defaults to 0.";

        public const string ParamRenderCharacterEditUserPrompt =
            "The user's original tweak intent. Echoed back in the Inspector.";

        public const string ParamRenderCharacterEditRationale =
            "A 1–2 sentence rationale for the changes — why these deltas serve the intent. " +
            "Shown above the diff. Keep it tight and only mention real fields/abilities.";

        public const string ParamRenderCharacterEditProposal =
            "The patch. ALL fields optional; null/omit = keep current value. Fields:\n" +
            "- Name: rename the GameObject (omit to keep).\n" +
            "- Stats: list of { Type (\"Health\"|\"Mana\"|\"Stamina\" or a custom StatType " +
            "name), MaxValue, RegenRate (optional) }. A stat whose Type already exists is " +
            "UPDATED; a new Type is ADDED. Stats are never removed. Omit the list to leave " +
            "all stats alone.\n" +
            "- OnEliminated: \"Respawn\"|\"Disable\"|\"Destroy\". EliminationDelay: seconds.\n" +
            "- MovementEnabled / TargetingEnabled / AttackEnabled: toggle a whole module " +
            "on/off.\n" +
            "- Gravity (negative, e.g. -20), FallGravityMultiplier, MaxFallSpeed, " +
            "GroundSlopeLimit, WallSlopeLimit: movement tuning.\n" +
            "- TargetingMode: \"NearestDamageable\"|\"TaggedDamageable\". TargetTag: tag to " +
            "seek. TargetRadius: search radius in meters.\n" +
            "- AddMovementAbilities: class names of existing MovementAbility subclasses to " +
            "attach (skipped if already present).\n" +
            "- RemoveMovementAbilities: class names of MovementAbility subclasses to detach.\n" +
            "- AttackAbilityName: class name of an existing AttackAbility subclass to set " +
            "(replaces any current one). Use \"none\" or \"clear\" to remove the attack " +
            "ability. Omit to leave the attack ability unchanged.\n" +
            "- ComponentEdits: list of individual field edits on components ALREADY on the " +
            "object (abilities like DashAbility/JumpAbility, or Rigidbody2D, " +
            "SpriteRenderer, etc.). Each is { Component (type name), Index (0 unless " +
            "multiple), Field (serialized property path), Value (text) }. Component, " +
            "Index, and Field MUST come from a prior BuildingBlocks.ReadComponentFields " +
            "call — do NOT guess field names. Value is coerced by the field's type: " +
            "number, true/false, an enum option from ReadComponentFields, #RRGGBB color, " +
            "or \"x,y[,z]\" vector. Edits that don't resolve or are no-ops are dropped and " +
            "noted to the user. Use this for tuning (\"make his dash longer\" → " +
            "DashAbility duration; \"hit harder\" → ProjectileAttackAbility damage), NOT " +
            "for adding/removing whole abilities (use Add/Remove/AttackAbilityName for " +
            "that).\n" +
            "- Suggestions: optional list of { Title, Prompt } creative extensions the user " +
            "could build as new movement / attack abilities (use " +
            "this for abilities that don't exist yet — DON'T invent them as Add names).\n" +
            "- Tips: optional 0–3 plain-prose sentences for gotchas (tag requirements, etc.).";

        public const string ParamRenderCharacterAnswerUserPrompt =
            "The user's original question. Echoed back in the Inspector.";

        public const string ParamRenderCharacterAnswerAnswer =
            "A concise, plain-language answer grounded in the values from " +
            "ReadSelectedCharacter. 1–4 sentences of prose — no markdown headers, bullet " +
            "lists, or code fences.";

        #endregion

        #region Editor-side captions & warnings

        public const string ReadSelectedCharacterFooter =
            "Call BuildingBlocks.ReadAbilityCatalog(\"movement\"/\"attack\") if you need to add or swap abilities, then RenderCharacterEdit with ONLY the fields that should change.";

        public const string InspectableComponentsInstruction =
            "Components on this object you can tune field-by-field — call BuildingBlocks.ReadComponentFields(\"<Type>\") to see their editable fields + current values, then put changes in RenderCharacterEdit's ComponentEdits:";

        public const string ReadComponentFieldsEditInstruction =
            "To change one, add a ComponentEdit { Component, Index, Field, Value } to RenderCharacterEdit. Value is text: a number, true/false, an enum option above, #RRGGBB, or \"x,y[,z]\".";

        public const string Rigidbody2DOverrideWarning =
            "BuildingBlocksCharacter drives movement and gravity itself, so some Rigidbody2D fields (gravity scale, velocity, body type) may have no runtime effect. Tune the character's own gravity / fall fields for movement feel.";

        #endregion
    }
}
