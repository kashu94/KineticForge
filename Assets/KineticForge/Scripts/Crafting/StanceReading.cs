using System;
using KineticForge.Core;
using KineticForge.Physics;

namespace KineticForge.Crafting
{
    /// <summary>
    /// Current stance stability for crafting components: taken from the relic's <see cref="RelicStabilizer"/> when there
    /// is one, otherwise from <see cref="ForgeEvents.StanceChanged"/>. Call <see cref="Enable"/> and
    /// <see cref="Disable"/> from the owner's <c>OnEnable</c> and <c>OnDisable</c>.
    /// </summary>
    internal sealed class StanceReading
    {
        readonly Action<StanceChangedEvent> _onStanceChanged;
        RelicStabilizer _stabilizer;
        float _busStability;
        bool _subscribed;

        public StanceReading()
        {
            _onStanceChanged = payload => _busStability = payload.Stability;
        }

        /// <summary>Latest stance stability in [0, 1].</summary>
        public float Stability => _stabilizer != null ? _stabilizer.TargetStability : _busStability;

        /// <summary>Starts reading, preferring <paramref name="stabilizer"/> when it is not null.</summary>
        /// <param name="stabilizer">The relic's stabilizer, or null.</param>
        public void Enable(RelicStabilizer stabilizer)
        {
            _stabilizer = stabilizer;
            if (!_subscribed)
            {
                ForgeEvents.StanceChanged += _onStanceChanged;
                _subscribed = true;
            }
        }

        /// <summary>Stops reading.</summary>
        public void Disable()
        {
            if (_subscribed)
            {
                ForgeEvents.StanceChanged -= _onStanceChanged;
                _subscribed = false;
            }
        }
    }
}
