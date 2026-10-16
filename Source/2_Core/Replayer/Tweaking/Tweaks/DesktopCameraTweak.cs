#if BSIPA_MODS
using System;
using System.Reflection;
using System.Collections.Generic;
using UnityEngine;

namespace BeatLeader.Replayer.Tweaking {
    internal sealed class DesktopCameraTweak : GameTweak {
        private static readonly Type? FlightType = typeof(IPA.Utilities.UnityGame).Assembly.GetType("IPA.Desktop.DesktopFlight");
        private static readonly PropertyInfo? Instance = FlightType?.GetProperty("Instance", BindingFlags.Static | BindingFlags.NonPublic);
        private Behaviour? _flight;
        private bool _wasEnabled;
        private readonly Dictionary<Behaviour, bool> _pointers = new();
        internal static bool Requested => Instance?.GetValue(null) is Behaviour flight && flight != null;

        public override void Initialize() {
            _flight = Instance?.GetValue(null) as Behaviour;
            if (_flight == null) return;
            _wasEnabled = _flight.enabled;
            _flight.enabled = false;
            foreach (var pointer in Resources.FindObjectsOfTypeAll<UnityEngine.EventSystems.BaseInputModule>()) {
                if (pointer == null || pointer.GetType().FullName != "IPA.Desktop.DesktopPointerInput") continue;
                _pointers.Add(pointer, pointer.enabled);
                pointer.enabled = false;
            }
        }

        public override void Dispose() {
            foreach (var pointer in _pointers) if (pointer.Key != null) pointer.Key.enabled = pointer.Value;
            _pointers.Clear();
            if (_flight != null) _flight.enabled = _wasEnabled;
            _flight = null;
        }
    }
}
#endif
