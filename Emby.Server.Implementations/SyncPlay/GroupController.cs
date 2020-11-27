using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Entities;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;
using MediaBrowser.Controller.SyncPlay;
using MediaBrowser.Controller.SyncPlay.GroupStates;
using MediaBrowser.Controller.SyncPlay.Queue;
using MediaBrowser.Model.SyncPlay;
using Microsoft.Extensions.Logging;

namespace Emby.Server.Implementations.SyncPlay
{
    /// <summary>
    /// Class GroupController.
    /// </summary>
    /// <remarks>
    /// Class is not thread-safe, external locking is required when accessing methods.
    /// </remarks>
    public class GroupController : IGroupController, IGroupStateContext
    {
        /// <summary>
        /// The logger.
        /// </summary>
        private readonly ILogger<GroupController> _logger;

        /// <summary>
        /// The logger factory.
        /// </summary>
        private readonly ILoggerFactory _loggerFactory;

        /// <summary>
        /// The user manager.
        /// </summary>
        private readonly IUserManager _userManager;

        /// <summary>
        /// The session manager.
        /// </summary>
        private readonly ISessionManager _sessionManager;

        /// <summary>
        /// The library manager.
        /// </summary>
        private readonly ILibraryManager _libraryManager;

        /// <summary>
        /// The participants, or members of the group.
        /// </summary>
        private readonly Dictionary<string, GroupMember> _participants =
            new Dictionary<string, GroupMember>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// The group access list.
        /// </summary>
        private readonly GroupAccessList _accessList;

        /// <summary>
        /// The list of invited users.
        /// </summary>
        private List<Guid> _invitedUsers;

        /// <summary>
        /// The internal group state.
        /// </summary>
        private IGroupState _state;

        /// <summary>
        /// Initializes a new instance of the <see cref="GroupController" /> class.
        /// </summary>
        /// <param name="loggerFactory">The logger factory.</param>
        /// <param name="userManager">The user manager.</param>
        /// <param name="sessionManager">The session manager.</param>
        /// <param name="libraryManager">The library manager.</param>
        public GroupController(
            ILoggerFactory loggerFactory,
            IUserManager userManager,
            ISessionManager sessionManager,
            ILibraryManager libraryManager)
        {
            _loggerFactory = loggerFactory;
            _userManager = userManager;
            _sessionManager = sessionManager;
            _libraryManager = libraryManager;
            _logger = loggerFactory.CreateLogger<GroupController>();

            _state = new IdleGroupState(loggerFactory);
            _accessList = new GroupAccessList();
            _invitedUsers = new List<Guid>();
        }

        /// <summary>
        /// Gets the default ping value used for sessions.
        /// </summary>
        /// <value>The default ping.</value>
        public long DefaultPing { get; } = 500;

        /// <summary>
        /// Gets the maximum time offset error accepted for dates reported by clients, in milliseconds.
        /// </summary>
        /// <value>The maximum time offset error.</value>
        public long TimeSyncOffset { get; } = 2000;

        /// <summary>
        /// Gets the maximum offset error accepted for position reported by clients, in milliseconds.
        /// </summary>
        /// <value>The maximum offset error.</value>
        public long MaxPlaybackOffset { get; } = 500;

        /// <summary>
        /// Gets the group identifier.
        /// </summary>
        /// <value>The group identifier.</value>
        public Guid GroupId { get; } = Guid.NewGuid();

        /// <summary>
        /// Gets the group name.
        /// </summary>
        /// <value>The group name.</value>
        public string GroupName { get; private set; }

        /// <summary>
        /// Gets the group visibility type.
        /// </summary>
        /// <value>The group visibility.</value>
        public GroupVisibilityType Visibility { get; private set; }

        /// <summary>
        /// Gets the group identifier.
        /// </summary>
        /// <value>The group identifier.</value>
        public PlayQueueManager PlayQueue { get; } = new PlayQueueManager();

        /// <summary>
        /// Gets the runtime ticks of current playing item.
        /// </summary>
        /// <value>The runtime ticks of current playing item.</value>
        public long RunTimeTicks { get; private set; }

        /// <summary>
        /// Gets or sets the position ticks.
        /// </summary>
        /// <value>The position ticks.</value>
        public long PositionTicks { get; set; }

        /// <summary>
        /// Gets or sets the last activity.
        /// </summary>
        /// <value>The last activity.</value>
        public DateTime LastActivity { get; set; }

        /// <summary>
        /// Adds the session to the group.
        /// </summary>
        /// <param name="session">The session.</param>
        private void AddSession(SessionInfo session)
        {
            _participants.TryAdd(
                session.Id,
                new GroupMember(session)
                {
                    Ping = DefaultPing,
                    IsBuffering = false
                });

            // Set permissions if this is a new user
            _accessList.TouchPermissions(session.UserId);
        }

        /// <summary>
        /// Removes the session from the group.
        /// </summary>
        /// <param name="session">The session.</param>
        private void RemoveSession(SessionInfo session)
        {
            _participants.Remove(session.Id);

            // Preserve permissions of invited users.
            if (!_invitedUsers.Contains(session.UserId))
            {
                // Clear only when all sessions of same user left.
                var clearUser = true;
                foreach (var participant in _participants.Values)
                {
                    if (participant.Session.UserId.Equals(session.UserId))
                    {
                        clearUser = false;
                        break;
                    }
                }

                if (clearUser)
                {
                    _accessList.ClearPermissions(session.UserId);
                }
            }
        }

        /// <summary>
        /// Filters sessions of this group.
        /// </summary>
        /// <param name="from">The current session.</param>
        /// <param name="type">The filtering type.</param>
        /// <returns>The list of sessions matching the filter.</returns>
        private IEnumerable<SessionInfo> FilterSessions(SessionInfo from, SyncPlayBroadcastType type)
        {
            return type switch
            {
                SyncPlayBroadcastType.CurrentSession => new SessionInfo[] { from },
                SyncPlayBroadcastType.AllGroup => _participants
                    .Values
                    .Select(session => session.Session),
                SyncPlayBroadcastType.AllExceptCurrentSession => _participants
                    .Values
                    .Select(session => session.Session)
                    .Where(session => !session.Id.Equals(from.Id, StringComparison.OrdinalIgnoreCase)),
                SyncPlayBroadcastType.AllReady => _participants
                    .Values
                    .Where(session => !session.IsBuffering)
                    .Select(session => session.Session),
                _ => Enumerable.Empty<SessionInfo>()
            };
        }

        /// <summary>
        /// Checks if a given user can access a given item, that is, the user has access to a folder where the item is stored.
        /// </summary>
        /// <param name="user">The user.</param>
        /// <param name="item">The item.</param>
        /// <returns><c>true</c> if the user can access the item, <c>false</c> otherwise.</returns>
        private bool HasAccessToItem(User user, BaseItem item)
        {
            var collections = _libraryManager.GetCollectionFolders(item)
                .Select(folder => folder.Id.ToString("N", CultureInfo.InvariantCulture));
            return collections.Intersect(user.GetPreference(PreferenceKind.EnabledFolders)).Any();
        }

        /// <summary>
        /// Checks if a given user can access all items of a given queue, that is,
        /// the user has the required minimum parental access and has access to all required folders.
        /// </summary>
        /// <param name="user">The user.</param>
        /// <param name="queue">The queue.</param>
        /// <returns><c>true</c> if the user can access all the items in the queue, <c>false</c> otherwise.</returns>
        private bool HasAccessToQueue(User user, IReadOnlyList<Guid> queue)
        {
            // Check if queue is empty.
            if (queue == null || queue.Count == 0)
            {
                return true;
            }

            foreach (var itemId in queue)
            {
                var item = _libraryManager.GetItemById(itemId);
                if (user.MaxParentalAgeRating.HasValue && item.InheritedParentalRatingValue > user.MaxParentalAgeRating)
                {
                    return false;
                }

                if (!user.HasPermission(PermissionKind.EnableAllFolders) && !HasAccessToItem(user, item))
                {
                    return false;
                }
            }

            return true;
        }

        private bool AllUsersHaveAccessToQueue(IReadOnlyList<Guid> queue)
        {
            // Check if queue is empty.
            if (queue == null || queue.Count == 0)
            {
                return true;
            }

            // Get list of users.
            var users = _participants
                .Values
                .Select(participant => _userManager.GetUserById(participant.Session.UserId));

            // Find problematic users.
            var usersWithNoAccess = users.Where(user => !HasAccessToQueue(user, queue));

            // All users must be able to access the queue.
            return !usersWithNoAccess.Any();
        }

        /// <inheritdoc />
        public bool IsGroupEmpty() => _participants.Count == 0;

        /// <inheritdoc />
        public bool CanUserJoin(Guid userId)
        {
            if (_accessList.IsAdministrator(userId))
            {
                return true;
            }
            else if (Visibility.Equals(GroupVisibilityType.Public))
            {
                return true;
            }
            else if (Visibility.Equals(GroupVisibilityType.InviteOnly))
            {
                return _invitedUsers.Contains(userId);
            }

            return false;
        }

        /// <inheritdoc />
        public void CreateGroup(SessionInfo session, NewGroupRequest request, CancellationToken cancellationToken)
        {
            // Setup settings and access list
            GroupName = request.GroupName;
            Visibility = request.Visibility ?? GroupVisibilityType.Public;
            if (Visibility.Equals(GroupVisibilityType.InviteOnly))
            {
                _invitedUsers = request.InvitedUsers?.ToList() ?? _invitedUsers;
            }

            _accessList.AddAdministrator(session.UserId);
            _accessList.OpenPlaybackAccess = request.OpenPlaybackAccess ?? true;
            _accessList.OpenPlaylistAccess = request.OpenPlaylistAccess ?? true;

            // Add session to group
            AddSession(session);

            var sessionIsPlayingAnItem = session.FullNowPlayingItem != null;

            RestartCurrentItem();

            if (sessionIsPlayingAnItem)
            {
                var playlist = session.NowPlayingQueue.Select(item => item.Id).ToList();
                PlayQueue.Reset();
                PlayQueue.SetPlaylist(playlist);
                PlayQueue.SetPlayingItemById(session.FullNowPlayingItem.Id);
                RunTimeTicks = session.FullNowPlayingItem.RunTimeTicks ?? 0;
                PositionTicks = session.PlayState.PositionTicks ?? 0;

                // Maintain playstate.
                var waitingState = new WaitingGroupState(_loggerFactory)
                {
                    ResumePlaying = !session.PlayState.IsPaused
                };
                SetState(waitingState);
            }

            var updateSession = NewSyncPlayGroupUpdate(GroupUpdateType.GroupJoined, GetInfo());
            SendGroupUpdate(session, SyncPlayBroadcastType.CurrentSession, updateSession, cancellationToken);

            _state.SessionJoined(this, _state.Type, session, cancellationToken);

            _logger.LogInformation("Session {SessionId} created group {GroupId}.", session.Id, GroupId.ToString());
        }

        /// <inheritdoc />
        public void UpdateSettings(SessionInfo session, UpdateGroupSettingsRequest request, CancellationToken cancellationToken)
        {
            if (!_accessList.IsAdministrator(session.UserId))
            {
                _logger.LogInformation("Session {SessionId} is not an administrator of group {GroupId}.", session.Id, GroupId.ToString());
                return;
            }

            GroupName = request.GroupName ?? GroupName;
            Visibility = request.Visibility ?? Visibility;
            _invitedUsers = request.InvitedUsers?.ToList() ?? _invitedUsers;
            _accessList.OpenPlaybackAccess = request.OpenPlaybackAccess ?? _accessList.OpenPlaybackAccess;
            _accessList.OpenPlaylistAccess = request.OpenPlaylistAccess ?? _accessList.OpenPlaylistAccess;

            // Update access list for given users
            var userIds = request.AccessListUserIds;
            var playbackAccessList = request.AccessListPlayback;
            var playlistAccessList = request.AccessListPlaylist;
            if (userIds != null && playbackAccessList != null && playlistAccessList != null)
            {
                // Make sure arrays have same length
                if (userIds.Count == playbackAccessList.Count && userIds.Count == playlistAccessList.Count)
                {
                    for (var i = 0; i < userIds.Count; i++)
                    {
                        _accessList.SetPermissions(userIds[i], playbackAccessList[i], playlistAccessList[i]);
                    }
                }
            }

            var groupUpdate = NewSyncPlayGroupUpdate(GroupUpdateType.GroupUpdate, GetInfo());
            SendGroupUpdate(session, SyncPlayBroadcastType.AllGroup, groupUpdate, cancellationToken);

            _logger.LogInformation("Session {SessionId} updated the settings of group {GroupId}.", session.Id, GroupId.ToString());
        }

        /// <inheritdoc />
        public void SessionJoin(SessionInfo session, JoinGroupRequest request, CancellationToken cancellationToken)
        {
            if (!CanUserJoin(session.UserId))
            {
                return;
            }

            AddSession(session);

            var updateSession = NewSyncPlayGroupUpdate(GroupUpdateType.GroupJoined, GetInfo());
            SendGroupUpdate(session, SyncPlayBroadcastType.CurrentSession, updateSession, cancellationToken);

            var groupUpdate = NewSyncPlayGroupUpdate(GroupUpdateType.GroupUpdate, GetInfo());
            SendGroupUpdate(session, SyncPlayBroadcastType.AllExceptCurrentSession, groupUpdate, cancellationToken);

            var updateOthers = NewSyncPlayGroupUpdate(GroupUpdateType.UserJoined, session.UserName);
            SendGroupUpdate(session, SyncPlayBroadcastType.AllExceptCurrentSession, updateOthers, cancellationToken);

            _state.SessionJoined(this, _state.Type, session, cancellationToken);

            _logger.LogInformation("Session {SessionId} joined group {GroupId}.", session.Id, GroupId.ToString());
        }

        /// <inheritdoc />
        public void SessionRestore(SessionInfo session, JoinGroupRequest request, CancellationToken cancellationToken)
        {
            var updateSession = NewSyncPlayGroupUpdate(GroupUpdateType.GroupJoined, GetInfo());
            SendGroupUpdate(session, SyncPlayBroadcastType.CurrentSession, updateSession, cancellationToken);

            var groupUpdate = NewSyncPlayGroupUpdate(GroupUpdateType.GroupUpdate, GetInfo());
            SendGroupUpdate(session, SyncPlayBroadcastType.AllExceptCurrentSession, groupUpdate, cancellationToken);

            var updateOthers = NewSyncPlayGroupUpdate(GroupUpdateType.UserJoined, session.UserName);
            SendGroupUpdate(session, SyncPlayBroadcastType.AllExceptCurrentSession, updateOthers, cancellationToken);

            _state.SessionJoined(this, _state.Type, session, cancellationToken);

            _logger.LogInformation("Session {SessionId} re-joined group {GroupId}.", session.Id, GroupId.ToString());
        }

        /// <inheritdoc />
        public void SessionLeave(SessionInfo session, CancellationToken cancellationToken)
        {
            _state.SessionLeaving(this, _state.Type, session, cancellationToken);

            // Notify WebRTC peers
            var webRTCUpdate = new WebRTCUpdate(session.Id, false, true, string.Empty, string.Empty, string.Empty);
            var webRTCGroupUpdate = NewSyncPlayGroupUpdate(GroupUpdateType.WebRTC, webRTCUpdate);
            SendGroupUpdate(session, SyncPlayBroadcastType.AllExceptCurrentSession, webRTCGroupUpdate, cancellationToken);

            RemoveSession(session);

            var updateSession = NewSyncPlayGroupUpdate(GroupUpdateType.GroupLeft, GroupId.ToString());
            SendGroupUpdate(session, SyncPlayBroadcastType.CurrentSession, updateSession, cancellationToken);

            var groupUpdate = NewSyncPlayGroupUpdate(GroupUpdateType.GroupUpdate, GetInfo());
            SendGroupUpdate(session, SyncPlayBroadcastType.AllExceptCurrentSession, groupUpdate, cancellationToken);

            var updateOthers = NewSyncPlayGroupUpdate(GroupUpdateType.UserLeft, session.UserName);
            SendGroupUpdate(session, SyncPlayBroadcastType.AllExceptCurrentSession, updateOthers, cancellationToken);

            _logger.LogInformation("Session {SessionId} left group {GroupId}.", session.Id, GroupId.ToString());
        }

        /// <inheritdoc />
        public void HandleRequest(SessionInfo session, IGroupPlaybackRequest request, CancellationToken cancellationToken)
        {
            if (!_accessList.CheckRequest(session, request))
            {
                // User is not allowed to make this request.
                _logger.LogInformation("Rejecting {RequestType} requested by session {SessionId} in group {GroupId}.", request.Type, session.Id, GroupId.ToString());
                return;
            }

            // The server's job is to maintain a consistent state for clients to reference
            // and notify clients of state changes. The actual syncing of media playback
            // happens client side. Clients are aware of the server's time and use it to sync.
            _logger.LogInformation("Session {SessionId} requested {RequestType} in group {GroupId} that is {StateType}.", session.Id, request.Type, GroupId.ToString(), _state.Type);
            request.Apply(this, _state, session, cancellationToken);
        }

        /// <inheritdoc />
        public GroupInfoDto GetInfo()
        {
            var participants = _participants.Values.Select(session => session.Session.Id).Distinct();
            var userIds = participants.Select(sessionId => _participants[sessionId].Session.UserId);
            var userNames = participants.Select(sessionId => _participants[sessionId].Session.UserName);

            return new GroupInfoDto(
                GroupId,
                GroupName,
                Visibility,
                _invitedUsers,
                _accessList.GetAdministrators(),
                _accessList.OpenPlaybackAccess,
                _accessList.OpenPlaylistAccess,
                _state.Type,
                userIds.ToList(),
                userNames.ToList(),
                _accessList.GetAccessList(),
                DateTime.UtcNow);
        }

        /// <inheritdoc />
        public bool HasAccessToPlayQueue(User user)
        {
            var items = PlayQueue.GetPlaylist().Select(item => item.ItemId).ToList();
            return HasAccessToQueue(user, items);
        }

        /// <inheritdoc />
        public void HandleWebRTC(SessionInfo session, WebRTCGroupRequest request, CancellationToken cancellationToken)
        {
            var webRTCUpdate = new WebRTCUpdate(
                session.Id,
                request.NewSession,
                request.SessionLeaving,
                request.ICECandidate,
                request.Offer,
                request.Answer);

            var groupUpdate = NewSyncPlayGroupUpdate(GroupUpdateType.WebRTC, webRTCUpdate);
            if (string.IsNullOrEmpty(request.To))
            {
                SendGroupUpdate(session, SyncPlayBroadcastType.AllExceptCurrentSession, groupUpdate, cancellationToken);
            }
            else if (_participants.ContainsKey(request.To))
            {
                var toSession = _participants[request.To].Session;
                SendGroupUpdate(toSession, SyncPlayBroadcastType.CurrentSession, groupUpdate, cancellationToken);
            }
            else
            {
                _logger.LogWarning("Cannot send WebRTC message from session {SessionId} in group {GroupId}. Recipient {Recipient} not found.", session.Id, GroupId.ToString(), request.To);
            }
        }

        /// <inheritdoc />
        public void SetIgnoreGroupWait(SessionInfo session, bool ignoreGroupWait)
        {
            if (_participants.TryGetValue(session.Id, out GroupMember value))
            {
                value.IgnoreGroupWait = ignoreGroupWait;
            }
        }

        /// <inheritdoc />
        public void SetState(IGroupState state)
        {
            _logger.LogInformation("Group {GroupId} switching from {FromStateType} to {ToStateType}.", GroupId.ToString(), _state.Type, state.Type);
            this._state = state;
        }

        /// <inheritdoc />
        public Task SendGroupUpdate<T>(SessionInfo from, SyncPlayBroadcastType type, GroupUpdate<T> message, CancellationToken cancellationToken)
        {
            IEnumerable<Task> GetTasks()
            {
                foreach (var session in FilterSessions(from, type))
                {
                    yield return _sessionManager.SendSyncPlayGroupUpdate(session, message, cancellationToken);
                }
            }

            return Task.WhenAll(GetTasks());
        }

        /// <inheritdoc />
        public Task SendCommand(SessionInfo from, SyncPlayBroadcastType type, SendCommand message, CancellationToken cancellationToken)
        {
            IEnumerable<Task> GetTasks()
            {
                foreach (var session in FilterSessions(from, type))
                {
                    yield return _sessionManager.SendSyncPlayCommand(session, message, cancellationToken);
                }
            }

            return Task.WhenAll(GetTasks());
        }

        /// <inheritdoc />
        public SendCommand NewSyncPlayCommand(SendCommandType type)
        {
            return new SendCommand(
                GroupId,
                PlayQueue.GetPlayingItemPlaylistId(),
                LastActivity,
                type,
                PositionTicks,
                DateTime.UtcNow);
        }

        /// <inheritdoc />
        public GroupUpdate<T> NewSyncPlayGroupUpdate<T>(GroupUpdateType type, T data)
        {
            return new GroupUpdate<T>(GroupId, type, data);
        }

        /// <inheritdoc />
        public long SanitizePositionTicks(long? positionTicks)
        {
            var ticks = positionTicks ?? 0;
            return Math.Clamp(ticks, 0, RunTimeTicks);
        }

        /// <inheritdoc />
        public void UpdatePing(SessionInfo session, long ping)
        {
            if (_participants.TryGetValue(session.Id, out GroupMember value))
            {
                value.Ping = ping;
            }
        }

        /// <inheritdoc />
        public long GetHighestPing()
        {
            long max = long.MinValue;
            foreach (var session in _participants.Values)
            {
                max = Math.Max(max, session.Ping);
            }

            return max;
        }

        /// <inheritdoc />
        public void SetBuffering(SessionInfo session, bool isBuffering)
        {
            if (_participants.TryGetValue(session.Id, out GroupMember value))
            {
                value.IsBuffering = isBuffering;
            }
        }

        /// <inheritdoc />
        public void SetAllBuffering(bool isBuffering)
        {
            foreach (var session in _participants.Values)
            {
                session.IsBuffering = isBuffering;
            }
        }

        /// <inheritdoc />
        public bool IsBuffering()
        {
            foreach (var session in _participants.Values)
            {
                if (session.IsBuffering && !session.IgnoreGroupWait)
                {
                    return true;
                }
            }

            return false;
        }

        /// <inheritdoc />
        public bool SetPlayQueue(IReadOnlyList<Guid> playQueue, int playingItemPosition, long startPositionTicks)
        {
            // Ignore on empty queue or invalid item position.
            if (playQueue.Count == 0 || playingItemPosition >= playQueue.Count || playingItemPosition < 0)
            {
                return false;
            }

            // Check if participants can access the new playing queue.
            if (!AllUsersHaveAccessToQueue(playQueue))
            {
                return false;
            }

            PlayQueue.Reset();
            PlayQueue.SetPlaylist(playQueue);
            PlayQueue.SetPlayingItemByIndex(playingItemPosition);
            var item = _libraryManager.GetItemById(PlayQueue.GetPlayingItemId());
            RunTimeTicks = item.RunTimeTicks ?? 0;
            PositionTicks = startPositionTicks;
            LastActivity = DateTime.UtcNow;

            return true;
        }

        /// <inheritdoc />
        public bool SetPlayingItem(string playlistItemId)
        {
            var itemFound = PlayQueue.SetPlayingItemByPlaylistId(playlistItemId);

            if (itemFound)
            {
                var item = _libraryManager.GetItemById(PlayQueue.GetPlayingItemId());
                RunTimeTicks = item.RunTimeTicks ?? 0;
            }
            else
            {
                RunTimeTicks = 0;
            }

            RestartCurrentItem();

            return itemFound;
        }

        /// <inheritdoc />
        public bool RemoveFromPlayQueue(IReadOnlyList<string> playlistItemIds)
        {
            var playingItemRemoved = PlayQueue.RemoveFromPlaylist(playlistItemIds);
            if (playingItemRemoved)
            {
                var itemId = PlayQueue.GetPlayingItemId();
                if (!itemId.Equals(Guid.Empty))
                {
                    var item = _libraryManager.GetItemById(itemId);
                    RunTimeTicks = item.RunTimeTicks ?? 0;
                }
                else
                {
                    RunTimeTicks = 0;
                }

                RestartCurrentItem();
            }

            return playingItemRemoved;
        }

        /// <inheritdoc />
        public bool MoveItemInPlayQueue(string playlistItemId, int newIndex)
        {
            return PlayQueue.MovePlaylistItem(playlistItemId, newIndex);
        }

        /// <inheritdoc />
        public bool AddToPlayQueue(IReadOnlyList<Guid> newItems, GroupQueueMode mode)
        {
            // Ignore on empty list.
            if (newItems.Count == 0)
            {
                return false;
            }

            // Check if participants can access the new playing queue.
            if (!AllUsersHaveAccessToQueue(newItems))
            {
                return false;
            }

            if (mode.Equals(GroupQueueMode.QueueNext))
            {
                PlayQueue.QueueNext(newItems);
            }
            else
            {
                PlayQueue.Queue(newItems);
            }

            return true;
        }

        /// <inheritdoc />
        public void RestartCurrentItem()
        {
            PositionTicks = 0;
            LastActivity = DateTime.UtcNow;
        }

        /// <inheritdoc />
        public bool NextItemInQueue()
        {
            var update = PlayQueue.Next();
            if (update)
            {
                var item = _libraryManager.GetItemById(PlayQueue.GetPlayingItemId());
                RunTimeTicks = item.RunTimeTicks ?? 0;
                RestartCurrentItem();
                return true;
            }
            else
            {
                return false;
            }
        }

        /// <inheritdoc />
        public bool PreviousItemInQueue()
        {
            var update = PlayQueue.Previous();
            if (update)
            {
                var item = _libraryManager.GetItemById(PlayQueue.GetPlayingItemId());
                RunTimeTicks = item.RunTimeTicks ?? 0;
                RestartCurrentItem();
                return true;
            }
            else
            {
                return false;
            }
        }

        /// <inheritdoc />
        public void SetRepeatMode(GroupRepeatMode mode)
        {
            PlayQueue.SetRepeatMode(mode);
        }

        /// <inheritdoc />
        public void SetShuffleMode(GroupShuffleMode mode)
        {
            PlayQueue.SetShuffleMode(mode);
        }

        /// <inheritdoc />
        public PlayQueueUpdate GetPlayQueueUpdate(PlayQueueUpdateReason reason)
        {
            var startPositionTicks = PositionTicks;

            if (_state.Type.Equals(GroupStateType.Playing))
            {
                var currentTime = DateTime.UtcNow;
                var elapsedTime = currentTime - LastActivity;
                // Elapsed time is negative if event happens
                // during the delay added to account for latency.
                // In this phase clients haven't started the playback yet.
                // In other words, LastActivity is in the future,
                // when playback unpause is supposed to happen.
                // Adjust ticks only if playback actually started.
                startPositionTicks += Math.Max(elapsedTime.Ticks, 0);
            }

            return new PlayQueueUpdate(
                reason,
                PlayQueue.LastChange,
                PlayQueue.GetPlaylist(),
                PlayQueue.PlayingItemIndex,
                startPositionTicks,
                PlayQueue.ShuffleMode,
                PlayQueue.RepeatMode);
        }
    }
}
