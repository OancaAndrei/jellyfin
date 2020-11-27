using System;
using System.Collections.Generic;
using System.Threading;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.SyncPlay;

namespace MediaBrowser.Controller.SyncPlay
{
    /// <summary>
    /// Interface ISyncPlayManager.
    /// </summary>
    public interface ISyncPlayManager
    {
        /// <summary>
        /// Creates a new group.
        /// </summary>
        /// <param name="session">The session that's creating the group.</param>
        /// <param name="request">The request.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        void NewGroup(SessionInfo session, NewGroupRequest request, CancellationToken cancellationToken);

        /// <summary>
        /// Adds the session to a group.
        /// </summary>
        /// <param name="session">The session.</param>
        /// <param name="groupId">The group identifier.</param>
        /// <param name="request">The request.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        void JoinGroup(SessionInfo session, Guid groupId, JoinGroupRequest request, CancellationToken cancellationToken);

        /// <summary>
        /// Removes the session from a group.
        /// </summary>
        /// <param name="session">The session.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        void LeaveGroup(SessionInfo session, CancellationToken cancellationToken);

        /// <summary>
        /// Updates the settings of a group.
        /// </summary>
        /// <param name="session">The session.</param>
        /// <param name="request">The request.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        void UpdateGroupSettings(SessionInfo session, UpdateGroupSettingsRequest request, CancellationToken cancellationToken);

        /// <summary>
        /// Gets list of available groups for a session.
        /// </summary>
        /// <param name="session">The session.</param>
        /// <returns>The list of available groups.</returns>
        List<GroupInfoDto> ListGroups(SessionInfo session);

        /// <summary>
        /// Gets list of available users that have access to SyncPlay.
        /// </summary>
        /// <param name="session">The session.</param>
        /// <returns>The list of available users.</returns>
        List<UserInfoDto> ListAvailableUsers(SessionInfo session);

        /// <summary>
        /// Handle a request by a session in a group.
        /// </summary>
        /// <param name="session">The session.</param>
        /// <param name="request">The request.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        void HandleRequest(SessionInfo session, IGroupPlaybackRequest request, CancellationToken cancellationToken);

        /// <summary>
        /// Handle a WebRTC related message sent by a session.
        /// </summary>
        /// <param name="session">The session.</param>
        /// <param name="request">The requested action.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        void HandleWebRTC(SessionInfo session, WebRTCGroupRequest request, CancellationToken cancellationToken);
    }
}
