# Kinetic Forge

A Meta Quest 3 crafting game by Kashifa Studios, built for the Meta VR Start Developer Competition 2026.

You forge ancient relics with **Dual-Scale Input**:

- **Macro: your stance.** Body tracking reads how grounded you are: crouch depth, torso lean, stance width, balance over your feet and stillness. A braced stance makes a heavy relic steady in your hands. Standing tall makes it wobble and kick.
- **Micro: your thumb.** Sliding your thumb along the side of your index finger steers a chisel point across the relic. A thumb tap strikes.

Forging is **voicing**: each relic is commissioned to sing a chord, but the pour leaves its four voices sharp. You cut metal where it lowers the right voices, listening for the wobble to fade, until the relic rings true. Stance steadies the chisel, so your body decides how finely you can cut.

## Resonant Forging

The relic is a resonating body, simulated rather than animated:

- **Pulse and rhythm.** The relic has a slow pulse, about one beat per second. A strike that lands with the pulse pumps energy in, like pushing a swing; one against it drains energy. Charge deepens every carve. A ring closes on the mark and turns gold at the right moment. Hot metal pulses slower, so the tempo rises as the relic cools.
- **Your body is the damper.** Charge sways the relic and shakes the chisel as much as an unbraced stance lets it. Too much charge through a loose stance cracks the relic, and cold metal cracks easily.
- **Chladni figures.** Each strike rings four plate modes in proportion to their shape at the contact point. Their nodal lines glow on the face where sand would gather on a bowed plate.
- **Modal sound.** The relic rings like a bronze bell, synthesized sample by sample from its modes: strikes on different spots sound different, hot metal thuds, cold metal rings, and the hum swells with the charge.
- **Heat.** The relic starts hot from the furnace and cools by Newton's law. In-rhythm work keeps it hot. Hot metal carves deep and forgives.
- **Real carving.** Each carved mark dents the tablet's mesh and cuts glowing grooves between rune marks. Quality, integrity and best rhythm streak give a grade from S to C, or Fractured.

An in-world guide teaches all of this while you play.

## Voicing: the full loop

1. **The commission.** A patron asks for a chord (open fifths, major, minor, suspended, major seventh, minor seventh), each harder than the last. The pour leaves the relic's four voices, one per plate mode, ringing sharp. A crucible lowers on its chain and pours molten bronze; the relic fills its mold white-hot.
2. **Ring it.** Reach out and tap the relic with your finger; as your finger nears it, the tuner's ghost markers follow it, so you can see what a cut would do before you touch. Every spot on the face rings different voices, and glowing filings slide onto the nodal lines of what rings. A **tuning fork** answers each strike with the target pitches, so a voice that is off beats against its fork: a wobble that slows and fades as the voice comes true.
3. **Tune it.** Pick the chisel and cut metal. The first relic is a guided lesson with a checklist, a hint ring that marks a good cut, a verdict on every cut (good, too far, wasted) and three re-melts: swap to the torch and strike to undo your last cut. Later commissions have fewer re-melts, and the last has none. Where a voice moves most (the bare patches, never the sand) a cut lowers its pitch most. Every cut is final and cuts only lower pitch, so overshooting leaves a voice flat for good. The **tuner** beside the anvil shows each voice in cents, the in-tune band, and a ghost marker for where the next cut would take it. Rhythm is optional: a charged relic cuts coarser, a calm one finer. Bracing visibly grounds the charge; a loose stance throws sparks and cracks cold metal. When every voice is in tune the relic settles; cut again to refine, or let it finish.
4. **Quench.** Pinch on the result screen: the relic is plunged into the water trough in a burst of steam and a long hiss, then rises and sings the chord exactly as you tuned it.
5. **The Resonance Wall.** Every quenched relic hangs on the wall and keeps the chord you left it with, between sessions. Point and pinch to ring them; a relic you tuned badly sounds it. Each forge brings the next of six commissions.
6. **Mixed reality.** Pinch with both hands for a moment to forge in your own room: the hall dissolves into passthrough and the anvil, hearths, trough, wall and relic stay.

The model is simplified, not a finite element simulation: the sensitivity of a voice at a point is the square of its plate mode shape there, which follows how bell founders tune. It lives in `VoicingMath`, which is unit-tested, including that every commission is solvable within its cut budget.

## Requirements

- Unity **6000.3.28f1** with Android Build Support (OpenJDK, SDK and NDK)
- Meta Quest 3, for hand and body tracking. Everything also runs in the Editor with keyboard and mouse fallbacks.

Packages are listed in `Packages/manifest.json`: Meta XR Core SDK, Interaction SDK and Haptics 207.0.1, Oculus XR Plugin, URP and the Input System.

## Quick start

1. Open the project in Unity 6000.3.28f1.
2. Choose **Kinetic Forge > Create Sandbox Scene**.
3. Choose **Kinetic Forge > Bake Forge Lighting**. The bake takes a few minutes; without it the hall renders dark and flat, and Quest builds refuse to start.
4. Press Play. The intro and calibration run first; the carving phase follows.

Use **Advance Phase** on the Forge Game Manager to skip ahead while testing.

## Controls

| Action | Quest, hand tracking | Quest, Touch controllers | Editor |
|---|---|---|---|
| Brace your stance | Crouch, lean in, widen your feet | Same | Hold Space, or press B to toggle |
| Grab the relic | Close your hand near it | Squeeze the grip | Held automatically by the Relic Hold Simulator |
| Aim the chisel | Point at the relic; slide your thumb along your index finger for fine control | Thumbstick | Right-drag the mouse |
| Ring or cut (strike) | Tap the relic with your index fingertip, or point and pinch, or tap your thumb on your index finger | Index trigger | Left-click |
| Change tool: hammer, chisel, torch (undo) | Tap the thumb of your other hand | Left index trigger | T |
| Quench, then forge again (result screen) | Pinch | Index trigger | Left-click |
| Ring a relic on the Resonance Wall | Point at it and pinch | – | Left-click it |
| Mixed reality (forge in your room) | Pinch with both hands for a second | – | M (previews by hiding the hall) |
| Nudge the relic (test) | – | – | N |
| Diagnostics panel | Test builds only | – | F1 |

## Headless setup and tests

Close the Editor first. The first command rebuilds the sandbox and applies Meta's Project Setup Tool fixes for Android. The second only rebuilds the scene, and the third bakes its lighting; it needs a graphics device (no `-nographics`), because the reflection probe is rendered. The fourth compiles every Kinetic Forge shader variant for Quest. The last two run the test suites:

```bash
Unity -batchmode -nographics -projectPath . -buildTarget Android \
  -executeMethod KineticForge.EditorTools.KineticForgeBatch.SetUpQuestSandbox -logFile setup.log

Unity -batchmode -nographics -projectPath . \
  -executeMethod KineticForge.EditorTools.KineticForgeBatch.RebuildSandboxScene -logFile rebuild.log

Unity -batchmode -projectPath . \
  -executeMethod KineticForge.EditorTools.KineticForgeBatch.BakeSandboxLighting -logFile bake.log

Unity -batchmode -nographics -projectPath . \
  -executeMethod KineticForge.EditorTools.KineticForgeBatch.ValidateShaders -logFile shaders.log

Unity -batchmode -nographics -projectPath . -runTests -testPlatform EditMode -testResults editmode.xml
Unity -batchmode -nographics -projectPath . -runTests -testPlatform PlayMode -testResults playmode.xml
```

In the Editor, the same tests are under **Window > General > Test Runner**.

## Building for Quest

**Kinetic Forge > Quest Build** holds four commands:

| Command | What it does |
|---|---|
| Apply Quest Player Settings | IL2CPP, ARM64 only, Vulkan only, Android API 32+, linear color, ASTC, APK output, multiview, and Quest 2, Pro, 3 and 3S as target devices |
| Run Preflight Check | Lists anything that would break on the headset or at upload, including the scene's body tracking permission, passthrough and floor-level origin |
| Build Release APK | Applies the settings, refuses to build while the preflight fails, raises the version code by one (Meta needs a higher code for every upload) and writes `Builds/KineticForge-<version>.apk` |
| Build Test APK (Diagnostics Panel) | A release build, uploadable to a release channel, that also shows an in-headset diagnostics panel to the right of the forge: frame rate, game phase, stance source, body tracking permission, hand tracking, passthrough, and warning and error counts with the latest error. Never upload it to the `Competition` channel |
| Build Development APK | A debuggable, profiler-ready build without changing the version code |

Headless, with no Editor open on the project:

```bash
Unity -batchmode -nographics -projectPath . -buildTarget Android \
  -executeMethod KineticForge.EditorTools.KineticForgeBatch.BuildQuestApk -logFile build.log
```

Add `-kfDiagnostics` for a test build or `-kfDevelopment` for a development build. In the Editor, F1 shows the diagnostics panel.

### Release signing

Meta's dashboard rejects APKs signed with the debug key. Create a release key once, using the JDK installed with Unity:

```bash
~/Unity/Hub/Editor/6000.3.28f1/Editor/Data/PlaybackEngines/AndroidPlayer/OpenJDK/bin/keytool -genkeypair -v \
  -keystore ~/kineticforge-release.keystore -alias kineticforge -keyalg RSA -keysize 2048 -validity 10000
```

Then create `UserSettings/KineticForgeSigning.json`. The `UserSettings` folder is git-ignored, so the passwords stay on this machine. The keystore uses PKCS12, where the key password is the keystore password:

```json
{
  "keystorePath": "~/kineticforge-release.keystore",
  "keystorePassword": "<your password>",
  "keyAlias": "kineticforge",
  "keyPassword": "<your password>"
}
```

The environment variables `KF_KEYSTORE`, `KF_KEYSTORE_PASS`, `KF_KEY_ALIAS` and `KF_KEY_PASS` override the file. Signing settings are restored after every build, so no path or password reaches `ProjectSettings`.

**Back up the keystore and its password** (for example in a password manager, and on a second machine). Meta ties the app to this key; without it you can never upload an update.

### Installing on the headset

- **Meta Horizon Developer Dashboard (no cable).** Create the app, then upload the release APK to a release channel. The competition requires a channel named exactly `Competition`. Add your own account to the channel, and the app appears in the headset's Library. This is the path judges use, so test it early.
- **Meta Quest Developer Hub (USB-C on another computer).** Drag the APK onto the connected headset. Its Device Logs tab shows the game's log lines, which all start with `[KineticForge]`.

## Code layout

All code lives in `Assets/KineticForge`, one assembly per module:

| Assembly | Responsibility |
|---|---|
| `KineticForge.Core` | Exception-isolated event bus, game flow with pause handling, logging, filters and math |
| `KineticForge.Tracking` | Stance tracking and solver, micro-gesture tracking, hand skeleton reading, Editor input fallbacks |
| `KineticForge.Physics` | Stance-driven relic stabilization, physical two-hand grip, hold simulator for testing |
| `KineticForge.Crafting` | Hand grabbing, pointer aiming, rune carving surface, carving cursor, chisel strikes, the precision model, relic resonance (pulse, heat, cracks, grade), voicing (commissions, cuts, tuning, settling), the cymatic sigil puzzle and the carvable relic mesh |
| `KineticForge.Feedback` | Modal relic synth with a tuning fork, the voice tuner, Chladni sand, grounding flow, sparks, flames, embers, firelight, the rhythm ring, molten pour, quench, Resonance Wall, mixed reality, the in-world guide and the test-build diagnostics panel |
| `KineticForge.Platform` | Quest frame pacing, headset session monitoring, and a guard that keeps scenes playable where Meta's native plugin is missing (for example the Linux Editor) |
| `KineticForge.Editor` | Live inspectors, sandbox and forge environment builders, the scanned asset library and world-scaled meshes, lighting bake, URP setup, the Quest build pipeline with its preflight check, and batch entry points |

The hall is built from scanned, real-world-scale CC0 assets from [Poly Haven](https://polyhaven.com) in `Assets/KineticForge/ThirdParty/PolyHaven`, credited, together with the fonts, in [`ThirdParty/CREDITS.md`](Assets/KineticForge/ThirdParty/CREDITS.md). Its light is baked (moonlight through a barred window, a lantern, the furnace mouth's glow), while the three fires flicker in real time; light probes and a box-projected reflection probe carry the room onto the relic.

**Kinetic Forge > Reset Saved Progress** clears the saved commission and the Resonance Wall, so the next run starts with the lesson.

Shaders live in `Assets/KineticForge/Shaders`: Forge Metal (hot, carvable, resonating metal with a mottled oxide skin when hot), Forge Glow (additive flames, sparks and rings), Forge Panel (the smoked-glass plates behind world text), Forge Gauge (the tuner's per-voice gauges) and Forge Text (test-build labels).

World text is TextMesh Pro, set in Cinzel for titles and Inter for instructions, both under the SIL Open Font License (`Assets/KineticForge/Fonts`). Their font assets are built by the sandbox builder with every character the game prints, then made static, so no glyph is rasterized on the headset. TMP's essential resources ship in `Assets/TextMesh Pro`.

Modules talk through `ForgeEvents`, so each one can be tested and replaced on its own. Every Meta-specific path compiles only when the Meta XR Core SDK is installed, and logs a structured warning instead of failing when a component is missing.
