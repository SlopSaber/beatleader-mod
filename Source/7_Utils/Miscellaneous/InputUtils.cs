using System;
using UnityEngine;
using System.Linq;

namespace BeatLeader.Utils {
    internal static class InputUtils {
        #region FPFC

        public static bool UsesFPFC => OverrideUsesFPFC ?? HasFpfcArg;

        public static bool? OverrideUsesFPFC { get; set; }

#if BSIPA_MODS
        public static bool HasFpfcArg => BeatLeader.Replayer.Tweaking.DesktopCameraTweak.Requested;
#else
        public static readonly bool HasFpfcArg = Environment
            .GetCommandLineArgs()
            .Contains("fpfc");
#endif

        #endregion

        #region Cursor

        public static void SwitchCursor() {
            EnableCursor(!Cursor.visible);
        }

        public static void EnableCursor(bool enable) {
            Cursor.visible = enable;
            Cursor.lockState = enable ? CursorLockMode.None : CursorLockMode.Locked;
        }

        #endregion
    }
}