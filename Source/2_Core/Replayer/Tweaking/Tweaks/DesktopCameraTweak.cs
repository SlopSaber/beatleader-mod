#if BSIPA_MODS
using System;
using System.Reflection;
using UnityEngine;

namespace BeatLeader.Replayer.Tweaking {
    internal sealed class DesktopCameraTweak : GameTweak {
        private static readonly Type? FlightType = typeof(IPA.Utilities.UnityGame).Assembly.GetType("IPA.Desktop.DesktopFlight");
        private static readonly PropertyInfo? Instance = FlightType?.GetProperty("Instance", BindingFlags.Static | BindingFlags.NonPublic);
        private Behaviour? _flight;
        private bool _wasEnabled;
        internal static bool Requested => Instance?.GetValue(null) is Behaviour flight && flight != null;

        public override void Initialize() {
            _flight = Instance?.GetValue(null) as Behaviour;
            if (_flight == null) return;
            _wasEnabled = _flight.enabled;
            _flight.enabled = false;
        }

        public override void Dispose() {
            if (_flight != null) _flight.enabled = _wasEnabled;
            _flight = null;
        }
    }
}
#endif
