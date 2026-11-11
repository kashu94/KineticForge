# Kinetic Forge

A Meta Quest 3 crafting game by Kashifa Studios, built for the Meta VR Start Developer Competition 2026.

You forge ancient relics with **Dual-Scale Input**:

- **Macro: your stance.** Body tracking reads how grounded you are: crouch depth, torso lean, stance width, balance over your feet and stillness. A braced stance makes a heavy relic steady in your hands. Standing tall makes it wobble and kick.
- **Micro: your thumb.** Sliding your thumb along the side of your index finger steers a chisel point across the relic. A thumb tap strikes.

A strike's precision is your aim times your stance. Carve all twelve rune marks to forge the first relic.

## Requirements

- Unity **6000.3.28f1** with Android Build Support (OpenJDK, SDK and NDK)
- Meta Quest 3, for hand and body tracking. Everything also runs in the Editor with keyboard and mouse fallbacks.

Packages are listed in `Packages/manifest.json`: Meta XR Core SDK, Interaction SDK and Haptics 207.0.1, Oculus XR Plugin, URP and the Input System.

## Quick start

1. Open the project in Unity 6000.3.28f1.
2. Choose **Kinetic Forge > Create Sandbox Scene**.
3. Press Play. The intro and calibration run first; the carving phase follows.

Use **Advance Phase** on the Forge Game Manager to skip ahead while testing.

## Controls

| Action | Quest, hand tracking | Quest, Touch controllers | Editor |
|---|---|---|---|
| Brace your stance | Crouch, lean in, widen your feet | Same | Hold Space, or press B to toggle |
| Grab the relic | Close your hand near it | Squeeze the grip | Held automatically by the Relic Hold Simulator |
| Aim the chisel | Slide your thumb along your index finger | Thumbstick | Right-drag the mouse |
| Strike | Tap your thumb on your index finger | Index trigger | Left-click |
| Nudge the relic (test) | – | – | N |

## Headless setup and tests

Close the Editor first. The first command rebuilds the sandbox and applies Meta's Project Setup Tool fixes for Android. The second only rebuilds the scene. The last two run the test suites:

```bash
Unity -batchmode -nographics -projectPath . -buildTarget Android \
  -executeMethod KineticForge.EditorTools.KineticForgeBatch.SetUpQuestSandbox -logFile setup.log

Unity -batchmode -nographics -projectPath . \
  -executeMethod KineticForge.EditorTools.KineticForgeBatch.RebuildSandboxScene -logFile rebuild.log

Unity -batchmode -nographics -projectPath . -runTests -testPlatform EditMode -testResults editmode.xml
Unity -batchmode -nographics -projectPath . -runTests -testPlatform PlayMode -testResults playmode.xml
```

In the Editor, the same tests are under **Window > General > Test Runner**.

## Code layout

All code lives in `Assets/KineticForge`, one assembly per module:

| Assembly | Responsibility |
|---|---|
| `KineticForge.Core` | Exception-isolated event bus, game flow with pause handling, logging, filters and math |
| `KineticForge.Tracking` | Stance tracking and solver, micro-gesture tracking, hand skeleton reading, Editor input fallbacks |
| `KineticForge.Physics` | Stance-driven relic stabilization, physical two-hand grip, hold simulator for testing |
| `KineticForge.Crafting` | Hand grabbing, rune carving surface, carving cursor, chisel strikes and the precision model |
| `KineticForge.Platform` | Quest frame pacing, headset session monitoring, and a guard that keeps scenes playable where Meta's native plugin is missing (for example the Linux Editor) |
| `KineticForge.Editor` | Live inspectors, sandbox scene builder and batch entry points |

Modules talk through `ForgeEvents`, so each one can be tested and replaced on its own. Every Meta-specific path compiles only when the Meta XR Core SDK is installed, and logs a structured warning instead of failing when a component is missing.
