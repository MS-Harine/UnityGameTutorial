---
name: validate-building-blocks-character
description: Audit and repair a Building Blocks character GameObject. Use whenever asked to validate, fix, check, or set up a Building Blocks character — it checks the selected GameObject for the components a working player or enemy needs and adds the ones that are missing.
enabled: true
---

# Validate & Fix a Building Blocks Character

Check the **selected** GameObject for missing components and add them. Be decisive: inspect, fix, report. Do not deliberate, weigh trade-offs, or write long explanations.

## Steps
1. List the components on the selected GameObject (check children too).
2. Decide the role — **Player** or **Enemy** — from the tag and the abilities present.
3. Add every required component from the role's list below that is missing. Add it directly with your editor tools.
4. Report in 1–2 lines: what you added, and "everything else is already set up."

## Required components

**Every character**
- `BuildingBlocksCharacter`
- `Rigidbody2D` and a `Collider2D`
- `SpriteRenderer`
- `Animator` + `CharacterAnimator`
- A `MovementAbility`

**Player** (tagged `Player`)
- `WalkAbility`, `JumpAbility`
- `PlayerAttackAbility`

**Enemy**
- `EnemyMovementAbility`
- One attack ability — `EnemyMeleeAttackAbility` or `ProjectileAttackAbility`

If the role is genuinely ambiguous, ask once, then proceed.

## Rules
- Focus on **missing components**. Add them; don't remove anything the user already has.
- Also enable the GameObject if it's disabled.
- Don't invent assets (sprites, controllers, projectile prefabs). If one is needed and absent, mention it in one line and move on.
