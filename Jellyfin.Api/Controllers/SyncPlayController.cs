using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Threading;
using Jellyfin.Api.Constants;
using Jellyfin.Api.Helpers;
using MediaBrowser.Controller.Net;
using MediaBrowser.Controller.Session;
using MediaBrowser.Controller.SyncPlay;
using MediaBrowser.Controller.SyncPlay.PlaybackRequests;
using MediaBrowser.Model.SyncPlay;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Api.Controllers
{
    /// <summary>
    /// The sync play controller.
    /// </summary>
    [Authorize(Policy = Policies.DefaultAuthorization)]
    public class SyncPlayController : BaseJellyfinApiController
    {
        private readonly ISessionManager _sessionManager;
        private readonly IAuthorizationContext _authorizationContext;
        private readonly ISyncPlayManager _syncPlayManager;

        /// <summary>
        /// Initializes a new instance of the <see cref="SyncPlayController"/> class.
        /// </summary>
        /// <param name="sessionManager">Instance of the <see cref="ISessionManager"/> interface.</param>
        /// <param name="authorizationContext">Instance of the <see cref="IAuthorizationContext"/> interface.</param>
        /// <param name="syncPlayManager">Instance of the <see cref="ISyncPlayManager"/> interface.</param>
        public SyncPlayController(
            ISessionManager sessionManager,
            IAuthorizationContext authorizationContext,
            ISyncPlayManager syncPlayManager)
        {
            _sessionManager = sessionManager;
            _authorizationContext = authorizationContext;
            _syncPlayManager = syncPlayManager;
        }

        /// <summary>
        /// Create a new SyncPlay group.
        /// </summary>
        /// <param name="groupName">The name of the new group.</param>
        /// <param name="visibility">The visibility type of the group.</param>
        /// <param name="invitedUsers">The list of users invited to join the group.</param>
        /// <param name="openPlaybackAccess">The playback access to give to new users.</param>
        /// <param name="openPlaylistAccess">The playlist access to give to new users.</param>
        /// <response code="204">New group created.</response>
        /// <returns>A <see cref="NoContentResult"/> indicating success.</returns>
        [HttpPost("New")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        public ActionResult SyncPlayCreateGroup(
            [FromQuery, Required] string groupName,
            [FromQuery] GroupVisibilityType? visibility,
            [FromQuery] Guid[] invitedUsers,
            [FromQuery] bool? openPlaybackAccess,
            [FromQuery] bool? openPlaylistAccess)
        {
            var currentSession = RequestHelpers.GetSession(_sessionManager, _authorizationContext, Request);
            var newGroupRequest = new NewGroupRequest(
                groupName,
                visibility,
                invitedUsers,
                openPlaybackAccess,
                openPlaylistAccess);
            _syncPlayManager.NewGroup(currentSession, newGroupRequest, CancellationToken.None);
            return NoContent();
        }

        /// <summary>
        /// Join an existing SyncPlay group.
        /// </summary>
        /// <param name="groupId">The sync play group id.</param>
        /// <response code="204">Group join successful.</response>
        /// <returns>A <see cref="NoContentResult"/> indicating success.</returns>
        [HttpPost("Join")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        public ActionResult SyncPlayJoinGroup(
            [FromQuery, Required] Guid groupId)
        {
            var currentSession = RequestHelpers.GetSession(_sessionManager, _authorizationContext, Request);
            var joinRequest = new JoinGroupRequest(groupId);
            _syncPlayManager.JoinGroup(currentSession, groupId, joinRequest, CancellationToken.None);
            return NoContent();
        }

        /// <summary>
        /// Leave the joined SyncPlay group.
        /// </summary>
        /// <response code="204">Group leave successful.</response>
        /// <returns>A <see cref="NoContentResult"/> indicating success.</returns>
        [HttpPost("Leave")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        public ActionResult SyncPlayLeaveGroup()
        {
            var currentSession = RequestHelpers.GetSession(_sessionManager, _authorizationContext, Request);
            _syncPlayManager.LeaveGroup(currentSession, CancellationToken.None);
            return NoContent();
        }

        /// <summary>
        /// Updates the settings of a SyncPlay group.
        /// </summary>
        /// <param name="groupName">The name for the group.</param>
        /// <param name="visibility">The visibility type of the group.</param>
        /// <param name="invitedUsers">The list of users invited to join the group.</param>
        /// <param name="openPlaybackAccess">The playback access to give to new users.</param>
        /// <param name="openPlaylistAccess">The playlist access to give to new users.</param>
        /// <param name="accessListUserIds">The list of user ids whose settings are updated.</param>
        /// <param name="accessListPlayback">The list of new playback permissions.</param>
        /// <param name="accessListPlaylist">The list of new playlist permissions.</param>
        /// <response code="204">Group settings updated.</response>
        /// <returns>A <see cref="NoContentResult"/> indicating success.</returns>
        [HttpPost("Settings")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        public ActionResult SyncPlaySettingsGroup(
            [FromQuery] string? groupName,
            [FromQuery] GroupVisibilityType? visibility,
            [FromQuery] Guid[] invitedUsers,
            [FromQuery] bool? openPlaybackAccess,
            [FromQuery] bool? openPlaylistAccess,
            [FromQuery] Guid[] accessListUserIds,
            [FromQuery] bool[] accessListPlayback,
            [FromQuery] bool[] accessListPlaylist)
        {
            var currentSession = RequestHelpers.GetSession(_sessionManager, _authorizationContext, Request);
            var syncPlayRequest = new UpdateGroupSettingsRequest(
                groupName,
                visibility,
                invitedUsers,
                openPlaybackAccess,
                openPlaylistAccess,
                accessListUserIds,
                accessListPlayback,
                accessListPlaylist);
            _syncPlayManager.UpdateGroupSettings(currentSession, syncPlayRequest, CancellationToken.None);
            return NoContent();
        }

        /// <summary>
        /// Gets all SyncPlay groups.
        /// </summary>
        /// <response code="200">Groups returned.</response>
        /// <returns>An <see cref="IEnumerable{GroupInfoDto}"/> containing the available SyncPlay groups.</returns>
        [HttpGet("List")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public ActionResult<IEnumerable<GroupInfoDto>> SyncPlayGetGroups()
        {
            var currentSession = RequestHelpers.GetSession(_sessionManager, _authorizationContext, Request);
            return Ok(_syncPlayManager.ListGroups(currentSession));
        }

        /// <summary>
        /// Gets all users that have access to SyncPlay.
        /// </summary>
        /// <response code="200">Users returned.</response>
        /// <returns>An <see cref="IEnumerable{UserInfoDto}"/> containing the available users.</returns>
        [HttpGet("ListAvailableUsers")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public ActionResult<IEnumerable<UserInfoDto>> SyncPlayGetAvailableUsers()
        {
            var currentSession = RequestHelpers.GetSession(_sessionManager, _authorizationContext, Request);
            return Ok(_syncPlayManager.ListAvailableUsers(currentSession));
        }

        /// <summary>
        /// Request play in SyncPlay group.
        /// </summary>
        /// <param name="playingQueue">The playing queue. Item ids in the playing queue, comma delimited.</param>
        /// <param name="playingItemPosition">The playing item position from the queue.</param>
        /// <param name="startPositionTicks">The start position ticks.</param>
        /// <response code="204">Play message sent to all group members.</response>
        /// <returns>A <see cref="NoContentResult"/> indicating success.</returns>
        [HttpPost("Play")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        public ActionResult SyncPlayPlay(
            [FromQuery, Required] Guid[] playingQueue,
            [FromQuery, Required] int playingItemPosition,
            [FromQuery, Required] long startPositionTicks)
        {
            var currentSession = RequestHelpers.GetSession(_sessionManager, _authorizationContext, Request);
            var syncPlayRequest = new PlayGroupRequest(playingQueue, playingItemPosition, startPositionTicks);
            _syncPlayManager.HandleRequest(currentSession, syncPlayRequest, CancellationToken.None);
            return NoContent();
        }

        /// <summary>
        /// Request to change playlist item in SyncPlay group.
        /// </summary>
        /// <param name="playlistItemId">The playlist id of the item.</param>
        /// <response code="204">Queue update message sent to all group members.</response>
        /// <returns>A <see cref="NoContentResult"/> indicating success.</returns>
        [HttpPost("SetPlaylistItem")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        public ActionResult SyncPlaySetPlaylistItem(
            [FromQuery, Required] string playlistItemId)
        {
            var currentSession = RequestHelpers.GetSession(_sessionManager, _authorizationContext, Request);
            var syncPlayRequest = new SetPlaylistItemGroupRequest(playlistItemId);
            _syncPlayManager.HandleRequest(currentSession, syncPlayRequest, CancellationToken.None);
            return NoContent();
        }

        /// <summary>
        /// Request to remove items from the playlist in SyncPlay group.
        /// </summary>
        /// <param name="playlistItemIds">The playlist ids of the items to remove.</param>
        /// <response code="204">Queue update message sent to all group members.</response>
        /// <returns>A <see cref="NoContentResult"/> indicating success.</returns>
        [HttpPost("RemoveFromPlaylist")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        public ActionResult SyncPlayRemoveFromPlaylist(
            [FromQuery, Required] string[] playlistItemIds)
        {
            var currentSession = RequestHelpers.GetSession(_sessionManager, _authorizationContext, Request);
            var syncPlayRequest = new RemoveFromPlaylistGroupRequest(playlistItemIds);
            _syncPlayManager.HandleRequest(currentSession, syncPlayRequest, CancellationToken.None);
            return NoContent();
        }

        /// <summary>
        /// Request to move an item in the playlist in SyncPlay group.
        /// </summary>
        /// <param name="playlistItemId">The playlist id of the item to move.</param>
        /// <param name="newIndex">The new position.</param>
        /// <response code="204">Queue update message sent to all group members.</response>
        /// <returns>A <see cref="NoContentResult"/> indicating success.</returns>
        [HttpPost("MovePlaylistItem")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        public ActionResult SyncPlayMovePlaylistItem(
            [FromQuery, Required] string playlistItemId,
            [FromQuery, Required] int newIndex)
        {
            var currentSession = RequestHelpers.GetSession(_sessionManager, _authorizationContext, Request);
            var syncPlayRequest = new MovePlaylistItemGroupRequest(playlistItemId, newIndex);
            _syncPlayManager.HandleRequest(currentSession, syncPlayRequest, CancellationToken.None);
            return NoContent();
        }

        /// <summary>
        /// Request to queue items to the playlist of a SyncPlay group.
        /// </summary>
        /// <param name="itemIds">The items to add.</param>
        /// <param name="mode">The mode in which to enqueue the items.</param>
        /// <response code="204">Queue update message sent to all group members.</response>
        /// <returns>A <see cref="NoContentResult"/> indicating success.</returns>
        [HttpPost("Queue")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        public ActionResult SyncPlayQueue(
            [FromQuery, Required] Guid[] itemIds,
            [FromQuery, Required] GroupQueueMode mode)
        {
            var currentSession = RequestHelpers.GetSession(_sessionManager, _authorizationContext, Request);
            var syncPlayRequest = new QueueGroupRequest(itemIds, mode);
            _syncPlayManager.HandleRequest(currentSession, syncPlayRequest, CancellationToken.None);
            return NoContent();
        }

        /// <summary>
        /// Request unpause in SyncPlay group.
        /// </summary>
        /// <response code="204">Unpause message sent to all group members.</response>
        /// <returns>A <see cref="NoContentResult"/> indicating success.</returns>
        [HttpPost("Unpause")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        public ActionResult SyncPlayUnpause()
        {
            var currentSession = RequestHelpers.GetSession(_sessionManager, _authorizationContext, Request);
            var syncPlayRequest = new UnpauseGroupRequest();
            _syncPlayManager.HandleRequest(currentSession, syncPlayRequest, CancellationToken.None);
            return NoContent();
        }

        /// <summary>
        /// Request pause in SyncPlay group.
        /// </summary>
        /// <response code="204">Pause message sent to all group members.</response>
        /// <returns>A <see cref="NoContentResult"/> indicating success.</returns>
        [HttpPost("Pause")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        public ActionResult SyncPlayPause()
        {
            var currentSession = RequestHelpers.GetSession(_sessionManager, _authorizationContext, Request);
            var syncPlayRequest = new PauseGroupRequest();
            _syncPlayManager.HandleRequest(currentSession, syncPlayRequest, CancellationToken.None);
            return NoContent();
        }

        /// <summary>
        /// Request stop in SyncPlay group.
        /// </summary>
        /// <response code="204">Stop message sent to all group members.</response>
        /// <returns>A <see cref="NoContentResult"/> indicating success.</returns>
        [HttpPost("Stop")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        public ActionResult SyncPlayStop()
        {
            var currentSession = RequestHelpers.GetSession(_sessionManager, _authorizationContext, Request);
            var syncPlayRequest = new StopGroupRequest();
            _syncPlayManager.HandleRequest(currentSession, syncPlayRequest, CancellationToken.None);
            return NoContent();
        }

        /// <summary>
        /// Request seek in SyncPlay group.
        /// </summary>
        /// <param name="positionTicks">The playback position in ticks.</param>
        /// <response code="204">Seek message sent to all group members.</response>
        /// <returns>A <see cref="NoContentResult"/> indicating success.</returns>
        [HttpPost("Seek")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        public ActionResult SyncPlaySeek(
            [FromQuery, Required] long positionTicks)
        {
            var currentSession = RequestHelpers.GetSession(_sessionManager, _authorizationContext, Request);
            var syncPlayRequest = new SeekGroupRequest(positionTicks);
            _syncPlayManager.HandleRequest(currentSession, syncPlayRequest, CancellationToken.None);
            return NoContent();
        }

        /// <summary>
        /// Request group wait in SyncPlay group while buffering.
        /// </summary>
        /// <param name="when">When the request has been made by the client.</param>
        /// <param name="positionTicks">The playback position in ticks.</param>
        /// <param name="isPlaying">Whether the client's playback is playing or not.</param>
        /// <param name="playlistItemId">The playlist item id.</param>
        /// <param name="bufferingDone">Whether the buffering is done.</param>
        /// <response code="204">Buffering message sent to all group members.</response>
        /// <returns>A <see cref="NoContentResult"/> indicating success.</returns>
        [HttpPost("Buffering")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        public ActionResult SyncPlayBuffering(
            [FromQuery, Required] DateTime when,
            [FromQuery, Required] long positionTicks,
            [FromQuery, Required] bool isPlaying,
            [FromQuery, Required] string playlistItemId,
            [FromQuery, Required] bool bufferingDone)
        {
            var currentSession = RequestHelpers.GetSession(_sessionManager, _authorizationContext, Request);
            IGroupPlaybackRequest syncPlayRequest;
            if (!bufferingDone)
            {
                syncPlayRequest = new BufferGroupRequest(when, positionTicks, isPlaying, playlistItemId);
            }
            else
            {
                syncPlayRequest = new ReadyGroupRequest(when, positionTicks, isPlaying, playlistItemId);
            }

            _syncPlayManager.HandleRequest(currentSession, syncPlayRequest, CancellationToken.None);
            return NoContent();
        }

        /// <summary>
        /// Request SyncPlay group to ignore member during group-wait.
        /// </summary>
        /// <param name="ignoreWait">Whether to ignore the member.</param>
        /// <response code="204">Member state updated.</response>
        /// <returns>A <see cref="NoContentResult"/> indicating success.</returns>
        [HttpPost("SetIgnoreWait")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        public ActionResult SyncPlaySetIgnoreWait(
            [FromQuery, Required] bool ignoreWait)
        {
            var currentSession = RequestHelpers.GetSession(_sessionManager, _authorizationContext, Request);
            var syncPlayRequest = new IgnoreWaitGroupRequest(ignoreWait);
            _syncPlayManager.HandleRequest(currentSession, syncPlayRequest, CancellationToken.None);
            return NoContent();
        }

        /// <summary>
        /// Request next track in SyncPlay group.
        /// </summary>
        /// <param name="playlistItemId">The playing item id.</param>
        /// <response code="204">Next track message sent to all group members.</response>
        /// <returns>A <see cref="NoContentResult"/> indicating success.</returns>
        [HttpPost("NextTrack")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        public ActionResult SyncPlayNextTrack(
            [FromQuery, Required] string playlistItemId)
        {
            var currentSession = RequestHelpers.GetSession(_sessionManager, _authorizationContext, Request);
            var syncPlayRequest = new NextTrackGroupRequest(playlistItemId);
            _syncPlayManager.HandleRequest(currentSession, syncPlayRequest, CancellationToken.None);
            return NoContent();
        }

        /// <summary>
        /// Request previous track in SyncPlay group.
        /// </summary>
        /// <param name="playlistItemId">The playing item id.</param>
        /// <response code="204">Previous track message sent to all group members.</response>
        /// <returns>A <see cref="NoContentResult"/> indicating success.</returns>
        [HttpPost("PreviousTrack")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        public ActionResult SyncPlayPreviousTrack(
            [FromQuery, Required] string playlistItemId)
        {
            var currentSession = RequestHelpers.GetSession(_sessionManager, _authorizationContext, Request);
            var syncPlayRequest = new PreviousTrackGroupRequest(playlistItemId);
            _syncPlayManager.HandleRequest(currentSession, syncPlayRequest, CancellationToken.None);
            return NoContent();
        }

        /// <summary>
        /// Request to set repeat mode in SyncPlay group.
        /// </summary>
        /// <param name="mode">The repeat mode.</param>
        /// <response code="204">Play queue update sent to all group members.</response>
        /// <returns>A <see cref="NoContentResult"/> indicating success.</returns>
        [HttpPost("SetRepeatMode")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        public ActionResult SyncPlaySetRepeatMode(
            [FromQuery, Required] GroupRepeatMode mode)
        {
            var currentSession = RequestHelpers.GetSession(_sessionManager, _authorizationContext, Request);
            var syncPlayRequest = new SetRepeatModeGroupRequest(mode);
            _syncPlayManager.HandleRequest(currentSession, syncPlayRequest, CancellationToken.None);
            return NoContent();
        }

        /// <summary>
        /// Request to set shuffle mode in SyncPlay group.
        /// </summary>
        /// <param name="mode">The shuffle mode.</param>
        /// <response code="204">Play queue update sent to all group members.</response>
        /// <returns>A <see cref="NoContentResult"/> indicating success.</returns>
        [HttpPost("SetShuffleMode")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        public ActionResult SyncPlaySetShuffleMode(
            [FromQuery, Required] GroupShuffleMode mode)
        {
            var currentSession = RequestHelpers.GetSession(_sessionManager, _authorizationContext, Request);
            var syncPlayRequest = new SetShuffleModeGroupRequest(mode);
            _syncPlayManager.HandleRequest(currentSession, syncPlayRequest, CancellationToken.None);
            return NoContent();
        }

        /// <summary>
        /// Update session ping.
        /// </summary>
        /// <param name="ping">The ping.</param>
        /// <response code="204">Ping updated.</response>
        /// <returns>A <see cref="NoContentResult"/> indicating success.</returns>
        [HttpPost("Ping")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        public ActionResult SyncPlayPing(
            [FromQuery, Required] double ping)
        {
            var currentSession = RequestHelpers.GetSession(_sessionManager, _authorizationContext, Request);
            var syncPlayRequest = new PingGroupRequest(Convert.ToInt64(ping));
            _syncPlayManager.HandleRequest(currentSession, syncPlayRequest, CancellationToken.None);
            return NoContent();
        }

        /// <summary>
        /// Request related to WebRTC signaling in SyncPlay group.
        /// </summary>
        /// <param name="to">The id of the session to whom to send the message.</param>
        /// <param name="newSession">Whether this is a new-session message.</param>
        /// <param name="sessionLeaving">Whether this is a session-leaving message.</param>
        /// <param name="iceCandidate">The ICE candidate as a string.</param>
        /// <param name="offer">The WebRTC offer as a string.</param>
        /// <param name="answer">The WebRTC answer as a string.</param>
        /// <response code="204">Message has been forwarded.</response>
        /// <returns>A <see cref="NoContentResult"/> indicating success.</returns>
        [HttpPost("WebRTC")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        public ActionResult SyncPlayWebRTC(
            [FromQuery] string? to,
            [FromQuery] bool? newSession,
            [FromQuery] bool? sessionLeaving,
            [FromQuery] string? iceCandidate,
            [FromQuery] string? offer,
            [FromQuery] string? answer)
        {
            var currentSession = RequestHelpers.GetSession(_sessionManager, _authorizationContext, Request);
            var syncPlayRequest = new WebRTCGroupRequest(to, newSession ?? false, sessionLeaving ?? false, iceCandidate, offer, answer);
            _syncPlayManager.HandleWebRTC(currentSession, syncPlayRequest, CancellationToken.None);
            return NoContent();
        }
    }
}
