using BeatLeader.Utils;
using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace BeatLeader.Interop {
    internal static class PlaylistsLibInterop {
        #region TryRefreshSongs

        public static async Task<bool> TryRefreshPlaylistsAsync(bool fullRefresh, CancellationToken token) {
            try {
                token.ThrowIfCancellationRequested();
                var assembly = AppDomain.CurrentDomain.GetAssemblies().SingleOrDefault(assembly => assembly.GetName().Name == "BeatSaberPlaylistsLib");
                var managerType = assembly!.GetType("BeatSaberPlaylistsLib.PlaylistManager");
                var method = managerType.GetMethod("RefreshPlaylistsAsync", BindingFlags.Instance | BindingFlags.Public,
                    null, new[] { typeof(bool), typeof(CancellationToken) }, null);
                if (method == null) return TryRefreshPlaylists(fullRefresh);

                var manager = managerType.GetProperty("DefaultManager", BindingFlags.Static | BindingFlags.Public)!.GetValue(null);
                var task = (Task)method.Invoke(manager, new object[] { fullRefresh, token });
                await task;
                token.ThrowIfCancellationRequested();
                var result = task.GetType().GetProperty("Result")!.GetValue(task);
                if (result!.GetType().GetProperty("Exception")!.GetValue(result) is Exception error) {
                    Plugin.Log.Debug($"RefreshPlaylists completed with file failures: {error}");
                }
                return true;
            } catch (OperationCanceledException) when (token.IsCancellationRequested) {
                throw;
            } catch (Exception e) {
                Plugin.Log.Debug($"RefreshPlaylists failed: {e}");
                return false;
            }
        }

        public static bool TryRefreshPlaylists(bool fullRefresh) {
            try {
                var assembly = AppDomain.CurrentDomain.GetAssemblies().SingleOrDefault(assembly => assembly.GetName().Name == "BeatSaberPlaylistsLib");
                var playlistManagerType = assembly!.GetType("BeatSaberPlaylistsLib.PlaylistManager");
                var defaultManagerField = playlistManagerType.GetProperty("DefaultManager", BindingFlags.Static | BindingFlags.Public);
                var refreshMethodInfo = playlistManagerType.GetMethod("RefreshPlaylists", BindingFlags.Instance | BindingFlags.Public);

                var manager = defaultManagerField!.GetValue(null);
                refreshMethodInfo!.Invoke(manager, new object[] {fullRefresh});
                return true;
            } catch (Exception e) {
                Plugin.Log.Debug($"RefreshPlaylists failed: {e}");
                return false;
            }
        }

        #endregion

        #region TryRefreshSongs

        public static async Task<BeatmapLevelPack?> TryFindPlaylistAsync(string filename, CancellationToken token) {
            try {
                token.ThrowIfCancellationRequested();
                var assembly = AppDomain.CurrentDomain.GetAssemblies().SingleOrDefault(assembly => assembly.GetName().Name == "BeatSaberPlaylistsLib");
                var managerType = assembly!.GetType("BeatSaberPlaylistsLib.PlaylistManager");
                var method = managerType.GetMethod("GetAllPlaylistsAsync", BindingFlags.Instance | BindingFlags.Public,
                    null, new[] { typeof(bool), typeof(CancellationToken) }, null);
                if (method == null) return TryFindPlaylist(filename);

                var manager = managerType.GetProperty("DefaultManager", BindingFlags.Static | BindingFlags.Public)!.GetValue(null);
                var task = (Task)method.Invoke(manager, new object[] { false, token });
                await task;
                token.ThrowIfCancellationRequested();
                var result = task.GetType().GetProperty("Result")!.GetValue(task);
                var playlists = (object[])result!.GetType().GetProperty("Playlists")!.GetValue(result);
                var playlistType = assembly.GetType("BeatSaberPlaylistsLib.Types.Playlist");
                var filenameProperty = playlistType.GetProperty("Filename", BindingFlags.Instance | BindingFlags.Public);
                var playlist = playlists.FirstOrDefault(p => (string)filenameProperty!.GetValue(p) == filename);
                if (playlist == null) return null;

                return (BeatmapLevelPack)playlistType.GetProperty("PlaylistLevelPack", BindingFlags.Instance | BindingFlags.Public)!.GetValue(playlist);
            } catch (OperationCanceledException) when (token.IsCancellationRequested) {
                throw;
            } catch (Exception e) {
                Plugin.Log.Debug($"TryFindPlaylist failed: {e}");
                return null;
            }
        }

        public static BeatmapLevelPack? TryFindPlaylist(string filename) {
            try {
                var assembly = AppDomain.CurrentDomain.GetAssemblies().SingleOrDefault(assembly => assembly.GetName().Name == "BeatSaberPlaylistsLib");
                var playlistManagerType = assembly!.GetType("BeatSaberPlaylistsLib.PlaylistManager");
                
                var defaultManagerField = playlistManagerType.GetProperty("DefaultManager", BindingFlags.Static | BindingFlags.Public);
                
                var getAllMethodInfo = playlistManagerType.GetMethod("GetAllPlaylists", 
                    ReflectionUtils.DefaultFlags, Array.Empty<Type>());

                var manager = defaultManagerField!.GetValue(null);
                var playlists = (object[])getAllMethodInfo!.Invoke(manager, Array.Empty<object>());

                var playlistType = assembly!.GetType("BeatSaberPlaylistsLib.Types.Playlist");
                var filenameField = playlistType.GetProperty("Filename", BindingFlags.Instance | BindingFlags.Public);

                var playlistWrapper = playlists.FirstOrDefault(p => (string)filenameField!.GetValue(p) == filename);
                if (playlistWrapper == null) {
                    return null;
                }

                var levelPackField = playlistType.GetProperty("PlaylistLevelPack", BindingFlags.Instance | BindingFlags.Public);

                return (BeatmapLevelPack)levelPackField.GetValue(playlistWrapper);
            } catch (Exception e) {
                Plugin.Log.Debug($"TryFindPlaylist failed: {e}");
                return null;
            }
        }

        #endregion
    }
}
