// Channel Points Handler (CPH)
//
// This is a stub with the CPH class's methods that were used in Cliparino, so that the signatures of functions work.

using Streamer.bot.Plugin.Interface.Model;
using Twitch.Common.Models.Api;

public static class CPH {
    // Properties
    public static extern string TwitchClientId { get; set; }
    public static extern string TwitchOAuthToken { get; set; }
    
    // Core Methods
    // Action Queues Methods
    public static extern void PauseActionQueue(string name);
    public static extern void PauseAllActionQueues();
    public static extern void ResumeActionQueue(string name, bool clear = false);
    public static extern void ResumeAllActionQueues(bool clear = false);

    // Action Methods
    public static extern bool ActionExists(string actionName);
    public static extern void DisableAction(string actionName);
    public static extern void DisableActionById(string actionId);
    public static extern void EnableAction(string actionName);
    public static extern void EnableActionById(string actionId);
    public static extern List<ActionData> GetActions();
    public static extern bool RunAction(string actionName, bool runImmediately = true);
    public static extern bool RunActionById(string actionId, bool runImmediately = true);

    // Arguments Methods
    public static extern void SetArgument(string variableName, object value);
    public static extern bool TryGetArg<T>(string argName, out T value);
    public static extern bool TryGetArg(string argName, out Object value);

    // C# Methods
    public static extern bool ExecuteMethod(string executeCode, string methodName);

    // Commands Methods
    public static extern void CommandAddToAllUserCooldowns(string id, int seconds);
    public static extern void CommandAddToGlobalCooldown(string id, int seconds);
    public static extern void CommandAddToUserCooldown(string id, string userId, Platform platform, int seconds);
    public static extern int CommandGetCounter(string commandId);
    public static extern CommandCounter CommandGetUserCounter(string userLogin, Platform platform, string commandId);
    public static extern CommandCounter CommandGetUserCounterById(string userId, Platform platform, string commandId);
    public static extern void CommandRemoveAllUserCooldowns(string id);
    public static extern void CommandRemoveGlobalCooldown(string id);
    public static extern void CommandRemoveUserCooldown(string id, string userId, Platform platform);
    public static extern void CommandResetAllUserCooldowns(string id);
    public static extern void CommandResetAllUserCounters(string commandId);
    public static extern void CommandResetCounter(string commandId);
    public static extern void CommandResetGlobalCooldown(string id);
    public static extern void CommandResetUserCooldown(string id, string userId, Platform platform);
    public static extern void CommandResetUserCounter(string commandId, string userId, Platform platform);
    public static extern void CommandResetUsersCounters(string userId, Platform platform, bool persisted);
    public static extern void CommandSetGlobalCooldownDuration(string id, int seconds);
    public static extern void CommandSetUserCooldownDuration(string id, int seconds);
    public static extern void DisableCommand(string id);
    public static extern void EnableCommand(string id);
    public static extern List<CommandData> GetCommands();

    // Events Methods
    public static extern EventType GetEventType();
    public static extern EventSource GetSource();

    // Globals Methods
    public static extern void ClearNonPersistedGlobals();
    public static extern void ClearNonPersistedUserGlobals();
    public static extern T GetGlobalVar<T>(string varName, bool persisted = true);
    public static extern List<GlobalVariableValue> GetGlobalVarValues(bool persisted = true);
    public static extern void SetGlobalVar(string varName, object value, bool persisted = true);
    public static extern void SetUserVar(string userName, string varName, object value, bool persisted = true);
    public static extern void UnsetAllUsersVar(string varName, bool persisted = true);
    public static extern void UnsetGlobalVar(string varName, bool persisted = true);

    // Logging Methods
    public static extern void LogDebug(string logLine);
    public static extern void LogError(string logLine);
    public static extern void LogInfo(string logLine);
    public static extern void LogVerbose(string logLine);
    public static extern void LogWarn(string logLine);

    // MIDI Methods
    public static extern void MidiSendControlChange(Guid deviceId, int channel, int controller, int value);
    public static extern void MidiSendControlChangeByName(string name, int channel, int controller, int value);
    public static extern void MidiSendNoteOn(Guid deviceId, int channel, int note, int velocity, int duration = 0, bool sendNoteOff = false);
    public static extern void MidiSendNoteOnByName(string name, int channel, int note, int velocity, int duration = 0, bool sendNoteOff = false);
    public static extern void MidiSendRaw(Guid deviceId, int command, int channel, int data1, int data2);
    public static extern void MidiSendRawByName(string name, int command, int channel, int data1, int data2);

    // Misc Methods
    public static extern int Between(int min, int max);
    public static extern string EscapeString(string text);
    public static extern string GetVersion();
    public static extern Double NextDouble();
    public static extern string UrlEncode(string text);
    public static extern void Wait(int milliseconds);

    // Quotes Methods
    public static extern int AddQuoteForTrovo(string userId, string quote, bool captureGame = false);
    public static extern int AddQuoteForTwitch(string userId, string quote, bool captureGame = false);
    public static extern int AddQuoteForYouTube(string userId, string quote);
    public static extern bool DeleteQuote(int quoteId);
    public static extern QuoteData GetQuote(int quoteId);
    public static extern int GetQuoteCount();

    // Sounds Methods
    public static extern Double PlaySound(string fileName, Single volume = 1, bool finishBeforeContinuing = false, string name = "", bool useFileName = true);
    public static extern Double PlaySoundFromFolder(string path, Single volume = 1, bool recursive = false, bool finishBeforeContinuing = false, string name = "", bool useFileName = true);
    public static extern void StopAllSoundPlayback();
    public static extern void StopSoundPlayback(string soundName);

    // System Methods
    public static extern void KeyboardPress(string keyPress);
    public static extern void ShowToastNotification(string id, string title, string message, string attribution, string iconPath);
    public static extern void ShowToastNotification(string title, string message, string attribution, string iconPath);

    // Timers Methods
    public static extern void DisableTimer(string timerName);
    public static extern void DisableTimerById(string timerId);
    public static extern void EnableTimerById(string timerId);
    public static extern bool GetTimerState(string timerId);
    public static extern void SetTimerInterval(string timerId, int interval);

    // Triggers Methods
    public static extern bool RegisterCustomTrigger(string triggerName, string eventName, String[] categories);
    public static extern void TriggerCodeEvent(string eventName, string json);
    public static extern void TriggerCodeEvent(string eventName, Dictionary<string, object> args);
    public static extern void TriggerCodeEvent(string eventName, bool useArgs = true);

    // UDP Methods
    public static extern int BroadcastUdp(int port, object data);

    // Users Methods
    public static extern bool AddGroup(string groupName);
    public static extern bool AddUserIdToGroup(string userId, Platform platform, string groupName);
    public static extern bool AddUserToGroup(string userName, Platform platform, string groupName);
    public static extern bool ClearUsersFromGroup(string groupName);
    public static extern bool DeleteGroup(string groupName);
    public static extern List<string> GetGroups();
    public static extern bool GroupExists(string groupName);
    public static extern bool RemoveUserFromGroup(string userName, Platform platform, string groupName);
    public static extern bool RemoveUserIdFromGroup(string userId, Platform platform, string groupName);
    public static extern bool UserIdInGroup(string userId, Platform platform, string groupName);
    public static extern bool UserInGroup(string userName, Platform platform, string groupName);
    public static extern List<GroupUser> UsersInGroup(string groupName);

    // Websocket Methods
    public static extern void WebsocketBroadcastJson(string data);
    public static extern void WebsocketBroadcastString(string data);
    public static extern void WebsocketConnect(int connection = 0);
    public static extern void WebsocketCustomServerBroadcast(string data, string sessionId, int connection = 0);
    public static extern void WebsocketCustomServerCloseAllSessions(int connection = 0);
    public static extern void WebsocketCustomServerCloseSession(string sessionId, int connection = 0);
    public static extern int WebsocketCustomServerGetConnectionByName(string name);
    public static extern bool WebsocketCustomServerIsListening(int connection = 0);
    public static extern void WebsocketCustomServerStart(int connection = 0);
    public static extern void WebsocketCustomServerStop(int connection = 0);
    public static extern void WebsocketDisconnect(int connection = 0);
    public static extern bool WebsocketIsConnected(int connection = 0);
    public static extern void WebsocketSend(Byte[] data, int connection = 0);
    public static extern void WebsocketSend(string data, int connection = 0);

    // OBS Methods
    // Filters Methods
    public static extern void ObsHideFilter(string scene, string source, string filterName, int connection = 0);
    public static extern void ObsHideFilter(string scene, string filterName, int connection = 0);
    public static extern void ObsHideScenesFilters(string scene, int connection = 0);
    public static extern void ObsHideSourcesFilters(string scene, string source, int connection = 0);
    public static extern bool ObsIsFilterEnabled(string scene, string source, string filterName, int connection = 0);
    public static extern bool ObsIsFilterEnabled(string scene, string filterName, int connection = 0);
    public static extern void ObsSetFilterState(string scene, string source, string filterName, int state, int connection = 0);
    public static extern void ObsSetFilterState(string scene, string filterName, int state, int connection = 0);
    public static extern void ObsSetRandomFilterState(string scene, string source, int state, int connection = 0);
    public static extern void ObsSetRandomFilterState(string scene, int state, int connection = 0);
    public static extern void ObsShowFilter(string scene, string source, string filterName, int connection = 0);
    public static extern void ObsShowFilter(string scene, string filterName, int connection = 0);
    public static extern void ObsToggleFilter(string scene, string source, string filterName, int connection = 0);
    public static extern void ObsToggleFilter(string scene, string filterName, int connection = 0);

    // Groups Methods
    public static extern List<string> ObsGetGroupSources(string scene, string groupName, int connection = 0);
    public static extern void ObsHideGroupsSources(string scene, string groupName, int connection = 0);
    public static extern string ObsSetRandomGroupSourceVisible(string scene, string groupName, int connection = 0);

    // Media Methods
    public static extern void ObsMediaNext(string scene, string source, int connection = 0);
    public static extern void ObsMediaPause(string scene, string source, int connection = 0);
    public static extern void ObsMediaPlay(string scene, string source, int connection = 0);
    public static extern void ObsMediaPrevious(string scene, string source, int connection = 0);
    public static extern void ObsMediaRestart(string scene, string source, int connection = 0);
    public static extern void ObsMediaStop(string scene, string source, int connection = 0);
    public static extern void ObsSetImageSourceFile(string scene, string source, string file, int connection = 0);
    public static extern void ObsSetMediaSourceFile(string scene, string source, string file, int connection = 0);
    public static extern void ObsSetMediaState(string scene, string source, int state, int connection = 0);

    // Raw Methods
    public static extern string ObsSendBatchRaw(string data, bool haltOnFailure = false, int executionType = 0, int connection = 0);
    public static extern string ObsSendRaw(string requestType, string data, int connection = 0);

    // Recording Methods
    public static extern bool ObsIsRecording(int connection = 0);
    public static extern void ObsPauseRecording(int connection = 0);
    public static extern void ObsResumeRecording(int connection = 0);
    public static extern void ObsStartRecording(int connection = 0);

    // Replay Methods
    public static extern void ObsReplayBufferSave(int connection = 0);
    public static extern void ObsReplayBufferStart(int connection = 0);
    public static extern void ObsReplayBufferStop(int connection = 0);
    public static extern void ObsSetReplayBufferState(int state, int connection = 0);

    // Scenes Methods
    public static extern string ObsGetCurrentScene(int connection = 0);
    public static extern string ObsGetSceneItemProperties(string scene, string source, int connection = 0);
    public static extern void ObsHideSceneSources(string scene, int connection = 0);
    public static extern string ObsSetRandomSceneSourceVisible(string scene, int connection = 0);
    public static extern void ObsSetScene(string sceneName, int connection = 0);
    public static extern bool ObsTakeScreenshot(string source, string path, int quality = -1, int connection = 0);

    // Sources Methods
    public static extern void ObsHideSource(string scene, string source, int connection = 0);
    public static extern bool ObsIsSourceVisible(string scene, string source, int connection = 0);
    public static extern void ObsSetBrowserSource(string scene, string source, string url, int connection = 0);
    public static extern void ObsSetColorSourceColor(string scene, string source, string hexColor, int connection = 0);
    public static extern void ObsSetColorSourceColor(string scene, string source, int a, int r, int g, int b, int connection = 0);
    public static extern void ObsSetColorSourceRandomColor(string scene, string source, int connection = 0);
    public static extern void ObsSetGdiText(string scene, string source, string text, int connection = 0);
    public static extern void ObsSetSourceMuteState(string scene, string source, int state, int connection = 0);
    public static extern void ObsSetSourceVisibility(string scene, string source, bool visible, int connection = 0);
    public static extern void ObsSetSourceVisibilityState(string scene, string source, int state, int connection = 0);
    public static extern void ObsShowSource(string scene, string source, int connection = 0);
    public static extern void ObsSourceMute(string scene, string source, int connection = 0);
    public static extern void ObsSourceMuteToggle(string scene, string source, int connection = 0);
    public static extern void ObsSourceUnMute(string scene, string source, int connection = 0);

    // Streaming Methods
    public static extern bool ObsIsStreaming(int connection = 0);
    public static extern void ObsStartStreaming(int connection = 0);
    public static extern void ObsStopStreaming(int connection = 0);

    // Uncategorized Methods
    public static extern bool ObsConnect(int connection = 0);
    public static extern long ObsConvertColorHex(string colorHex);
    public static extern long ObsConvertRgb(int a, int r, int g, int b);
    public static extern void ObsDisconnect(int connection = 0);
    public static extern int ObsGetConnectionByName(string name);
    public static extern bool ObsIsConnected(int connection = 0);

    // Twitch Methods
    // Chat Methods
    public static extern void SendAction(string action, bool useBot = true, bool fallback = true);
    public static extern void SendMessage(string message, bool useBot = true, bool fallback = true);
    public static extern bool SendWhisper(string userName, string message, bool bot = true);
    public static extern void TwitchAnnounce(string message, bool bot = false, string color = "", bool fallback = false);
    public static extern bool TwitchClearChatMessages(bool bot = true);
    public static extern bool TwitchDeleteChatMessage(string messageId, bool bot = true);
    public static extern void TwitchReplyToMessage(string message, string replyId, bool useBot = true, bool fallback = true);

    // Clips Methods
    public static extern ClipData CreateClip();
    public static extern StreamMarker CreateStreamMarker(string description);
    public static extern List<ClipData> GetAllClips(Boolean? isFeatured);
    public static extern List<ClipData> GetClips(int count, Boolean? isFeatured);
    public static extern List<ClipData> GetClipsForGame(int gameId, TimeSpan duration, int count, Boolean? isFeatured);
    public static extern List<ClipData> GetClipsForGame(int gameId, TimeSpan duration, Boolean? isFeatured);
    public static extern List<ClipData> GetClipsForGame(int gameId, DateTime start, DateTime end, int count, Boolean? isFeatured);
    public static extern List<ClipData> GetClipsForGame(int gameId, DateTime start, DateTime end, Boolean? isFeatured);
    public static extern List<ClipData> GetClipsForGame(int gameId, int count, Boolean? isFeatured);
    public static extern List<ClipData> GetClipsForUser(string username, TimeSpan duration, int count, Boolean? isFeatured);
    public static extern List<ClipData> GetClipsForUser(string username, TimeSpan duration, Boolean? isFeatured);
    public static extern List<ClipData> GetClipsForUser(string username, DateTime start, DateTime end, int count, Boolean? isFeatured);
    public static extern List<ClipData> GetClipsForUser(string username, DateTime start, DateTime end, Boolean? isFeatured);
    public static extern List<ClipData> GetClipsForUser(string userName, int count, Boolean? isFeatured);
    public static extern List<ClipData> GetClipsForUser(string username, Boolean? isFeatured);
    public static extern List<ClipData> GetClipsForUserById(string userId, TimeSpan duration, int count, Boolean? isFeatured);
    public static extern List<ClipData> GetClipsForUserById(string? userId, TimeSpan duration, Boolean? isFeatured);
    public static extern List<ClipData> GetClipsForUserById(string userId, DateTime start, DateTime end, int count, Boolean? isFeatured);
    public static extern List<ClipData> GetClipsForUserById(string userId, DateTime start, DateTime end, Boolean? isFeatured);

    //// Misc Methods
    //public static extern string get_TwitchOAuthToken();
    
    // User Methods
    public static extern TwitchUserInfo TwitchGetBot();
    public static extern TwitchUserInfo TwitchGetBroadcaster();
    public static extern TwitchUserInfoEx TwitchGetExtendedUserInfoById(string userId);
    public static extern TwitchUserInfoEx TwitchGetExtendedUserInfoByLogin(string userLogin);
    public static extern TwitchUserInfo TwitchGetUserInfoById(string userId);
    public static extern TwitchUserInfo TwitchGetUserInfoByLogin(string userLogin);
    public static extern bool TwitchIsUserSubscribed(string userId, out String tier);

    // CPH Subclasses
    public class ActionData {
        public extern Guid Id { get; set; }
        public extern string Name { get; set; }
        public extern bool Enabled { get; set; }
        public extern string Group { get; set; }
        public extern string Queue { get; set; }
        public extern Guid QueueId { get; set; }
    }

    public class CommandCounter {
        public extern string UserId { get; set; }
        public extern string UserLogin { get; set; }
        public extern string UserName { get; set; }
        public extern string Platform { get; set; }
        public extern int Count { get; set; }
        public extern int Source { get; set; }
    }

    public class CommandData {
        public extern Guid Id { get; set; }
        public extern string Name { get; set; }
        public extern bool Enabled { get; set; }
        public extern string Group { get; set; }
        public extern int Mode { get; set; }
        public extern List<string> Commands { get; set; }
        public extern string RegexCommand { get; set; }
        public extern bool CaseSensitive { get; set; }
        public extern List<string> Sources { get; set; }
    }

    public class EventType;
    public class EventSource;

    public class GlobalVariableValue {
        public extern string VariableName { get; set; }
        public extern object Value { get; set; }

        public extern DateTime LastWrite { get; set; }
    }

    public class GroupUser {
        public extern string Id { get; set; }
        public extern string Type { get; set; }
        public extern string Login { get; set; }
        public extern string Username { get; set; }
    }

    public class QuoteData {
        public extern DateTime Timestamp { get; set; }
        public extern int Id { get; set; }
        public extern string UserId { get; set; }
        public extern string User { get; set; }
        public extern string Platform { get; set; }
        public extern string GameId { get; set; }
        public extern string GameName { get; set; }
        public extern string Quote { get; set; }
    }

    public class StreamMarker {
        public extern string Id { get; set; }
        public extern DateTime CreatedAt { get; set; }
        public extern string Description { get; set; }
        //Position in seconds of stream marker
        public extern int Position { get; set; }
    }

    public class TwitchUserInfo {
        public extern string UserName { get; set; }
        public extern string UserLogin { get; set; }
        public extern string UserId { get; set; }
        public extern DateTime LastActive { get; set; }
        public extern DateTime PreviousActive { get; set; }
        public extern bool IsSubscribed { get; set; }
        public extern string SubscriptionTier { get; set; }
        public extern bool IsModerator { get; set; }
        public extern bool IsVip { get; set; }
    }
}

public class Platform { }