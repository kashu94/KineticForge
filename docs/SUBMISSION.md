# Kinetic Forge: submission pack

Everything to paste into Devpost, the video plan, and the upload checklist. Facts below match what the game does today; keep them that way if you edit.

## Tagline (140 characters or fewer)

Forge a bell's voice with your hands: tap, cut and mute bronze until its four sharp voices ring one true chord.

## About the project (paste into Devpost)

**Kinetic Forge is a hands-only VR game about tuning bronze by ear.**

A patron commissions a relic that must sing a chord. The pour leaves its four voices ringing sharp. You reach out and tap the plate with your fingertip, and glowing filings slide onto the lines where it is still, drawing the plate's vibration modes in sand. Then you cut. Where a voice moves most, a chisel cut lowers its pitch most. Where the sand has gathered, a cut does nothing to that voice. Cuts only ever lower pitch and cannot be undone, so overshooting leaves a voice flat for good.

You tune by ear and by eye. A tuning fork answers every strike with the target pitches, so a voice that is off beats against its fork: a wobble that slows and fades as it comes true. A tuner plate beside the anvil shows each voice in cents, the in-tune band, and a ghost marker for where your next cut would take it. Your other hand has its own job: press a fingertip on the relic and it mutes the voices that move most under your finger, exactly as touching a bell silences it, so you can isolate one voice and hear whether it is off. When the last voice comes true, the whole chord blooms.

Finished relics are quenched, then hang on the Resonance Wall, where each rings the chord exactly as you tuned it. Tune well and it sounds right; tune badly and it sounds it. The wall plays your latest relics in order, so your work becomes a short piece of music. Six commissions, from open fifths to minor sevenths, each harder than the last.

**Hands only, and seated-friendly.** Everything works without a controller: fingertip taps, point-and-pinch, thumb taps, and a thumb tap on the other hand to change tool. Bracing your body steadies the chisel, and a loose stance cracks cold metal. The first relic is a guided lesson with a checklist, a hint ring, a verdict on every cut and a torch that undoes mistakes.

**What is technically new.** Nothing is animated or sampled. Each relic is a vibrating plate whose four modes are simulated: strikes excite the modes in proportion to their shape at the point of contact, the sand follows the modes' gradient, and the bronze sound is synthesised sample by sample on the audio thread from those same modes, with the tuning fork as a second set of oscillators. The cutting model is simplified but real: a voice's sensitivity at a point is its mode shape squared, which is how bell founders decide where to remove metal. The tuning math, the tap detector and the commissions are unit-tested, including that every commission is solvable within its cut budget.

**Challenges.** Making a physical idea legible in under a minute, so we cut the design down to one loop with a guided first relic. And keeping hands-only input reliable: a tap must be fast, from the front and over the plate, and a resting finger never strikes twice.

**Built with** Unity 6, URP, Meta XR SDK 207, OpenXR, TextMesh Pro. Scanned props and textures from Poly Haven (CC0); fonts Cinzel and Inter (SIL Open Font License). All code, shaders and audio synthesis are our own.

## Video plan (target 2:00 to 2:20; must be under 3:00, public on YouTube or Vimeo)

Real gameplay only. Show your hands clearly. One short caption per beat. No logo first.

| Time | Shot | Caption |
|---|---|---|
| 0:00 to 0:05 | Hook: close-up, sand snaps onto the plate's lines as the chord blooms in tune | (none, let the sound play) |
| 0:05 to 0:09 | Title over live footage | KINETIC FORGE: tune bronze with your hands |
| 0:09 to 0:20 | The pour: crucible tips, metal fills the mold | A commission: sing a perfect chord |
| 0:20 to 0:40 | Finger taps the relic; the sand moves; the tuner rows light up | Tap the bronze. Every spot rings different voices |
| 0:40 to 1:05 | Chisel cut, a needle drops toward the green band, the wobble in the sound slows | Cut where a voice moves most. Cuts are final |
| 1:05 to 1:25 | Other hand presses the plate and mutes a voice; one voice rings alone | Mute with your other hand to hear one voice |
| 1:25 to 1:40 | The last needle enters the band, the chord blooms | In tune |
| 1:40 to 1:55 | Quench with steam, relic hangs on the wall, the wall plays your relics in order | Your relics become music |
| 1:55 to 2:10 | Quick cuts: guided first relic, torch undoing a cut, a harder commission | Six commissions. Hands only |
| 2:10 to 2:20 | End card | Kinetic Forge. Meta Quest 3. Hands-only |

If you can only record in the Unity Editor or a simulator, say so in the video description: "Captured in Unity with simulated hand input." The rules accept Quest or simulator footage; do not present Editor footage as headset footage.

## Upload checklist (in order)

1. **Release key (once).** On Linux, create the key and the signing file as described in the README under Building for Quest. Back up the keystore and its password.
2. **Build.** In Unity run Create Sandbox Scene (Replace), Bake Forge Lighting, then **Kinetic Forge > Quest Build > Build Release APK**. Not the Test APK: it carries the diagnostics panel. Confirm no DEBUG_SIGNED warning in the Console.
3. **Dashboard.** At the Meta developer dashboard create the app, with package name `com.kashifastudios.kineticforge` (locked at the first upload). Upload the APK, and create a release channel named exactly **Competition**. Assign the build to it. Get the invite or access link for judges. Menu names can differ slightly; follow the dashboard's prompts.
4. **Video.** Upload as public to YouTube or Vimeo. Keep it under 3 minutes.
5. **Devpost.** Paste the tagline and the text above, add the video link, the Competition channel link and screenshots.
6. **Check as a stranger.** Open the invite link from a different account or a private window before submitting.
7. **Submit before the deadline:** Nov 18, 12:00 PM PST (Nov 19, 01:30 AM IST). After that, no changes are allowed.
