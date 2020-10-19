namespace MediaBrowser.Model.SyncPlay
{
    /// <summary>
    /// Class UserGroupAccess.
    /// </summary>
    public class UserGroupAccess
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="UserGroupAccess"/> class.
        /// </summary>
        /// <param name="playbackAccess">Whether the user has playback access.</param>
        /// <param name="playlistAccess">Whether the user has playlist access.</param>
        public UserGroupAccess(bool playbackAccess, bool playlistAccess)
        {
            PlaybackAccess = playbackAccess;
            PlaylistAccess = playlistAccess;
        }

        /// <summary>
        /// Gets a value indicating whether the user has playback access.
        /// </summary>
        /// <value><c>true</c> if user has access to playback; <c>false</c> otherwise.</value>
        public bool PlaybackAccess { get; }

        /// <summary>
        /// Gets a value indicating whether the user has playlist access.
        /// </summary>
        /// <value><c>true</c> if user has access to playlist; <c>false</c> otherwise.</value>
        public bool PlaylistAccess { get; }
    }
}
