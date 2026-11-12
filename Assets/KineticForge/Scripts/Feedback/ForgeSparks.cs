using System;
using KineticForge.Core;
using UnityEngine;

namespace KineticForge.Feedback
{
    /// <summary>
    /// Spark showers from chisel strikes: streaking sparks that bounce off the floor, colored by the metal's heat and
    /// thickened by resonance charge, a flash ring on every carved mark that turns gold when the strike rode the pulse, a
    /// dark shard burst on cracks, and a fountain when a relic is forged.
    /// </summary>
    /// <remarks>
    /// Builds its particle systems on Awake and emits with <see cref="ParticleSystem.Emit(ParticleSystem.EmitParams, int)"/>,
    /// so strikes never allocate. One instance serves every relic in the scene.
    /// </remarks>
    [DisallowMultipleComponent]
    [AddComponentMenu("Kinetic Forge/Feedback/Forge Sparks")]
    public sealed class ForgeSparks : MonoBehaviour
    {
        [SerializeField, Tooltip("Additive material for sparks (Kinetic Forge/Forge Glow).")]
        Material _sparkMaterial = null;

        [SerializeField, Tooltip("Additive ring material for strike flashes (Forge Glow with a ring thickness).")]
        Material _flashMaterial = null;

        [SerializeField, Range(0, 200), Tooltip("Sparks of a full-strength strike on hot metal.")]
        int _sparksPerStrike = 48;

        [SerializeField, Range(0, 600), Tooltip("Sparks in the fountain of a forged relic.")]
        int _forgedFountain = 260;

        [SerializeField, Tooltip("World height of the floor the sparks bounce on.")]
        float _floorHeight = 0f;

        [SerializeField, Range(0.3f, 1f), Tooltip("Strike timing at or above which the flash turns gold.")]
        float _rhythmThreshold = 0.6f;

        ParticleSystem _sparks;
        ParticleSystem _flash;
        Action<RelicResonanceEvent> _onResonated;
        Action<RelicForgedEvent> _onForged;

        void Awake()
        {
            _onResonated = HandleResonated;
            _onForged = HandleForged;
            _sparks = CreateSystem("Strike Sparks", _sparkMaterial, 900, true);
            _flash = CreateSystem("Strike Flash", _flashMaterial, 16, false);
        }

        void OnEnable()
        {
            ForgeEvents.RelicResonated += _onResonated;
            ForgeEvents.RelicForged += _onForged;
        }

        void OnDisable()
        {
            ForgeEvents.RelicResonated -= _onResonated;
            ForgeEvents.RelicForged -= _onForged;
        }

        void HandleResonated(RelicResonanceEvent payload)
        {
            Color hot = Color.Lerp(new Color(1f, 0.55f, 0.18f), new Color(1f, 0.9f, 0.55f), payload.Heat);
            int count = Mathf.RoundToInt(_sparksPerStrike * payload.Strength * (0.45f + 0.55f * payload.Heat) *
                                         (1f + 0.6f * Mathf.Clamp01(payload.Charge)));
            Burst(payload.Point, payload.Normal, count, 1.4f, 3.8f, 55f, hot, 0.004f, 0.009f);

            if (payload.CarvedMark >= 0)
            {
                bool inRhythm = payload.Timing >= _rhythmThreshold;
                Color ring = inRhythm ? new Color(1f, 0.75f, 0.25f) : new Color(0.45f, 0.75f, 1f);
                ring *= Mathf.Lerp(0.5f, 1.2f, payload.Quality);
                Flash(payload.Point + payload.Normal * 0.004f, ring, inRhythm ? 0.16f : 0.1f);
            }

            if (payload.Cracked)
            {
                Burst(payload.Point, payload.Normal, 30, 0.6f, 2f, 80f, new Color(0.6f, 0.15f, 0.05f), 0.006f, 0.012f);
                Flash(payload.Point, new Color(1f, 0.25f, 0.05f), 0.22f);
            }
        }

        void HandleForged(RelicForgedEvent payload)
        {
            if (payload.Relic == null)
            {
                return;
            }

            Vector3 origin = payload.Relic.worldCenterOfMass;
            bool fractured = payload.Grade == ForgeGrade.Fractured;
            Color color = fractured ? new Color(0.7f, 0.2f, 0.05f) : new Color(1f, 0.8f, 0.4f);
            Burst(origin, Vector3.up, _forgedFountain, 1.5f, 4.5f, 35f, color, 0.005f, 0.012f);
            Flash(origin, fractured ? new Color(1f, 0.3f, 0.05f) : new Color(1.2f, 0.95f, 0.5f), 0.6f);
        }

        void Burst(Vector3 origin, Vector3 direction, int count, float minSpeed, float maxSpeed, float cone, Color color,
            float minSize, float maxSize)
        {
            if (_sparks == null || count <= 0)
            {
                return;
            }

            Vector3 axis = direction.sqrMagnitude > 1e-6f ? direction.normalized : Vector3.up;
            ParticleSystem.EmitParams emit = new ParticleSystem.EmitParams { applyShapeToPosition = false };
            for (int i = 0; i < count; i++)
            {
                Vector3 spread = Vector3.Slerp(axis, UnityEngine.Random.onUnitSphere, UnityEngine.Random.value * cone / 180f);
                if (Vector3.Dot(spread, axis) < 0f)
                {
                    spread = Vector3.Reflect(spread, axis);
                }

                emit.position = origin;
                emit.velocity = spread.normalized * UnityEngine.Random.Range(minSpeed, maxSpeed);
                emit.startSize = UnityEngine.Random.Range(minSize, maxSize);
                emit.startLifetime = UnityEngine.Random.Range(0.35f, 1.1f);
                emit.startColor = color * UnityEngine.Random.Range(0.75f, 1.15f);
                _sparks.Emit(emit, 1);
            }
        }

        void Flash(Vector3 position, Color color, float size)
        {
            if (_flash == null)
            {
                return;
            }

            ParticleSystem.EmitParams emit = new ParticleSystem.EmitParams
            {
                applyShapeToPosition = false,
                position = position,
                velocity = Vector3.zero,
                startSize = size,
                startLifetime = 0.35f,
                startColor = color,
            };
            _flash.Emit(emit, 1);
        }

        ParticleSystem CreateSystem(string systemName, Material material, int maxParticles, bool sparks)
        {
            GameObject host = new GameObject(systemName);
            host.transform.SetParent(transform, false);
            ParticleSystem system = host.AddComponent<ParticleSystem>();
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            ParticleSystem.MainModule main = system.main;
            main.playOnAwake = false;
            main.loop = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = maxParticles;
            main.gravityModifier = sparks ? 0.9f : 0f;
            main.startSpeed = 0f;

            ParticleSystem.EmissionModule emission = system.emission;
            emission.enabled = false;

            ParticleSystem.ShapeModule shape = system.shape;
            shape.enabled = false;

            ParticleSystem.ColorOverLifetimeModule color = system.colorOverLifetime;
            color.enabled = true;
            Gradient gradient = new Gradient();
            gradient.SetKeys(
                sparks
                    ? new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(1f, 0.6f, 0.25f), 0.35f), new GradientColorKey(new Color(0.8f, 0.15f, 0.05f), 1f) }
                    : new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.8f, 0.5f), new GradientAlphaKey(0f, 1f) });
            color.color = gradient;

            ParticleSystemRenderer renderer = host.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            if (sparks)
            {
                renderer.renderMode = ParticleSystemRenderMode.Stretch;
                renderer.velocityScale = 0.035f;
                renderer.lengthScale = 1.5f;

                ParticleSystem.CollisionModule collision = system.collision;
                collision.enabled = true;
                collision.type = ParticleSystemCollisionType.Planes;
                GameObject floor = new GameObject("Spark Floor");
                floor.transform.SetParent(host.transform, false);
                floor.transform.position = new Vector3(0f, _floorHeight, 0f);
                collision.SetPlane(0, floor.transform);
                collision.dampen = 0.45f;
                collision.bounce = 0.35f;
                collision.lifetimeLoss = 0.15f;
            }
            else
            {
                renderer.renderMode = ParticleSystemRenderMode.Billboard;
                ParticleSystem.SizeOverLifetimeModule size = system.sizeOverLifetime;
                size.enabled = true;
                size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.25f, 1f, 1f));
            }

            system.Play();
            return system;
        }
    }
}
