using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BeatLeader.Models.Replay;
using IPA.Utilities;
using OculusStudios.Platform.Core;
using UnityEngine;

namespace BeatLeader.Core.Managers.ReplayEnhancer
{
    class UserEnhancer
    {
        private static readonly FieldAccessor<PlatformLeaderboardsModel, IPlatform>.Accessor? AccessPlatformUserModel;
        private static readonly object getUserLock = new object();

        private static UserInfo? getUserTask;
        private static IPlatform? _platformUserModel;

        static UserEnhancer()
        {
            try
            {
                AccessPlatformUserModel = FieldAccessor<PlatformLeaderboardsModel, IPlatform>.GetAccessor("_platform");
            }
            catch (Exception ex)
            {
                Plugin.Log.Error($"Error getting PlatformUserModel, GetUserInfo is unavailable: {ex.Message}");
                Plugin.Log.Debug(ex);
            }
        }

        public static void Enhance(Replay replay)
        {
            if (GetUser() is not { } userInfo) return;
            replay.info.playerID = userInfo.platformUserId;
            replay.info.platform = userInfo.platform switch {
                UserInfo.Platform.Steam => "steam",
                UserInfo.Platform.Oculus => "oculuspc",
                _ => string.Empty
            };
            replay.info.playerName = userInfo.userName;
        }

        public static UserInfo? GetUser()
        {
            try
            {
                lock (getUserLock)
                {
                    IPlatform? platformUserModel = GetPlatformUserModel();
                    if (platformUserModel == null)
                    {
                        Plugin.Log.Error("IPlatformUserModel not found, cannot update user info.");
                        return null;
                    }
                    if (getUserTask == null)
                        getUserTask = InternalGetUser(platformUserModel);
                }
                return getUserTask;
            }
            catch (Exception ex)
            {
                Plugin.Log.Error($"Error retrieving UserInfo: {ex.Message}.");
                Plugin.Log.Debug(ex);
                throw;
            }
        }

        private static UserInfo InternalGetUser(IPlatform platformUserModel)
        {
            UserInfo userInfo = new UserInfo(platformUserModel.key switch {
                "steam" => UserInfo.Platform.Steam,
                "oculus" => UserInfo.Platform.Oculus,
                "oculus-mock" => UserInfo.Platform.Oculus,
                "mock" => UserInfo.Platform.Test,
                _ => throw new NotSupportedException($"Unsupported platform: {platformUserModel.key}"),
            }, platformUserModel.user.userId.ToString(), platformUserModel.user.displayName);

            if (userInfo != null)
            {
                Plugin.Log.Debug($"UserInfo found: {userInfo.platformUserId}: {userInfo.userName} on {userInfo.platform}");
            }
            else
                throw new InvalidOperationException("UserInfo is null.");
            return userInfo;
        }

        internal static IPlatform? GetPlatformUserModel()
        {
            if (_platformUserModel != null)
                return _platformUserModel;
            try
            {
                if (AccessPlatformUserModel == null)
                {
                    Plugin.Log.Error("Accessor for 'PlatformLeaderboardsModel._platform' is null, GetUserInfo unavailable.");
                    return null;
                }
                // Need to check for null because there's multiple PlatformLeaderboardsModels (at least sometimes), and one has a null IPlatformUserModel with 'vrmode oculus'
                var leaderboardsModel = Resources.FindObjectsOfTypeAll<PlatformLeaderboardsModel>().Where(p => AccessPlatformUserModel(ref p) != null).LastOrDefault();
                if (leaderboardsModel == null)
                {
                    Plugin.Log.Error("Could not find a 'PlatformLeaderboardsModel', GetUserInfo unavailable.");
                    return null;
                }
                _platformUserModel = AccessPlatformUserModel(ref leaderboardsModel);
            }
            catch (Exception ex)
            {
                Plugin.Log.Error($"Error getting 'IPlatformUserModel', GetUserInfo unavailable: {ex.Message}");
                Plugin.Log.Debug(ex);
            }
            return _platformUserModel;
        }
    }
}
