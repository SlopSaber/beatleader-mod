#if BSIPA_MODS
using System;
using System.Reflection;
using System.Collections.Generic;
using UnityEngine;
#if BEAT_SABER_1453
using BeatLeader.Replayer.Emulation;
using Zenject;
#endif

namespace BeatLeader.Replayer.Tweaking {
    internal sealed class DesktopCameraTweak : GameTweak {
        private static readonly Type? FlightType = typeof(IPA.Utilities.UnityGame).Assembly.GetType("IPA.Desktop.DesktopFlight");
        private static readonly PropertyInfo? Instance = FlightType?.GetProperty("Instance", BindingFlags.Static | BindingFlags.NonPublic);
        private Behaviour? _flight;
        private bool _wasEnabled;
        private readonly Dictionary<Behaviour, bool> _pointers = new();
#if BEAT_SABER_1453
        [Inject] private readonly PlayerVRControllersManager _controllers = null!;
        [Inject] private readonly VirtualPlayerGameSabers _gameSabers = null!;
        private readonly Dictionary<FirstPersonFlyingController, bool> _nativeFlights = new();
#endif
        internal static bool Requested => Instance?.GetValue(null) is Behaviour flight && flight != null;

        public override void Initialize() {
            _flight = Instance?.GetValue(null) as Behaviour;
            if (_flight == null) return;
            _wasEnabled = _flight.enabled;
            _flight.enabled = false;
#if BEAT_SABER_1453
            foreach (var controller in Resources.FindObjectsOfTypeAll<FirstPersonFlyingController>()) {
                if (controller == null || (controller._controller0 != _controllers.leftHandVRController
                    && controller._controller1 != _controllers.rightHandVRController)) continue;
                _nativeFlights.Add(controller, controller.enabled);
                controller.enabled = false;
            }
#endif
            foreach (var pointer in Resources.FindObjectsOfTypeAll<UnityEngine.EventSystems.BaseInputModule>()) {
                if (pointer == null || pointer.GetType().FullName != "IPA.Desktop.DesktopPointerInput") continue;
                _pointers.Add(pointer, pointer.enabled);
                pointer.enabled = false;
            }
        }

#if BEAT_SABER_1453
        public override void LateInitialize() {
            if (_flight == null) return;
            _gameSabers.RestoreReplayControl();
            Plugin.Log.Notice($"[Replayer] Native FPFC suspended for {_nativeFlights.Count} gameplay controller(s).");
        }
#endif

        public override void Dispose() {
#if BEAT_SABER_1453
            foreach (var controller in _nativeFlights) if (controller.Key != null) controller.Key.enabled = controller.Value;
            _nativeFlights.Clear();
#endif
            foreach (var pointer in _pointers) if (pointer.Key != null) pointer.Key.enabled = pointer.Value;
            _pointers.Clear();
            if (_flight != null) _flight.enabled = _wasEnabled;
            _flight = null;
        }
    }
}
#endif
