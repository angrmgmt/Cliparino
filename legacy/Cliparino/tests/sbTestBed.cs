#region Usings

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Drawing;
using System.Net.Http;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using Streamer.bot.Common.Events;
using Streamer.bot.Plugin.Interface;
using Streamer.bot.Plugin.Interface.Enums;
using Streamer.bot.Plugin.Interface.Model;

using Twitch.Common.Models.Api;

#endregion

public class CPHInline : CPHInlineBase {
    public bool Execute() {
        return true;
    }

    public bool TestAuth() {
        var authToken = CPH.TwitchOAuthToken;

        CPH.SendMessage("Beginning authentication test.");

        if (string.IsNullOrEmpty(authToken)) {
            CPH.SendMessage("Failed to acquire token.");
            CPH.SetGlobalVar("OAuthToken", string.Empty);
            CPH.LogInfo("OAuth token is null or empty.");

            return false;
        }

        CPH.SendMessage($"Token is {authToken}.");
        CPH.SetGlobalVar("OAuthToken", authToken);
        CPH.LogInfo("OAuth token acquired successfully.");

        return true;
    }

    public bool TestChat() {
        CPH.SendMessage("Chat worky.");

        return true;
    }

    public bool TestArgs() {
        CPH.TryGetArg("command", out string command);
        CPH.TryGetArg("input0", out string argument);

        CPH.SendMessage($"command: {command}, arg: {argument}");

        return true;
    }

    public bool TestText() {
        const string sourceName = "myTextSource";
        var sceneName = CPH.ObsGetCurrentScene();
        var itemProps = CPH.ObsGetSceneItemProperties(sceneName, sourceName);

        if (string.IsNullOrEmpty(itemProps)) return false;

        CPH.LogInfo(itemProps);

        return true;
    }

    public bool TestClasses() {
        var td = new ThingDoer();
        var doneThing = td.DoThing(5);

        if (string.IsNullOrEmpty(doneThing)) return false;

        CPH.LogInfo(doneThing);

        return true;
    }

    public bool TestClasses2() {
        var doneThing = TheDoerOfThings.DoThing(5);

        return !string.IsNullOrEmpty(doneThing);
    }
    
    public bool TestOBSFunctions() {
        var success = TestScene.Create(CPH);

        return success;
    }
}

public class ThingDoer {
    private bool IsMade { get; } = true;

    public string DoThing(int a) {
        if (!IsMade) return $"Unable to process request until IsMade is true. At Present, IsMade: {IsMade}";

        var idk = $"You passed in a(n): {a}";

        return idk;
    }
}

public static class TheDoerOfThings {
    public static string DoThing(int a) =>
        $"You passed in a(n): {(!string.IsNullOrEmpty(a.ToString())
                                ? a.ToString()
                                : "invalid object to which to do the thing.")}";
}

public static class TestScene {
    public static IInlineInvokeProxy CPH;
    private static Guid sceneUuid = Guid.Empty;
    private const string SceneName = "TestScene";

    public static bool Create(IInlineInvokeProxy cph) {
        CPH = cph;

        const string requestType = "CreateScene";
        var requestData = new { sceneName = SceneName };
        var obsRequest = new { requestType, requestData };
        string response = CPH.ObsSendRaw(obsRequest.requestType, JsonConvert.SerializeObject(requestData));
        var responseGuidObj = JsonConvert.DeserializeAnonymousType(response, new { sceneUuid = string.Empty });

        if (responseGuidObj == null || string.IsNullOrEmpty(responseGuidObj.sceneUuid)) {
            CPH.LogError($"Failed to retrieve scene UUID for {SceneName}.");
			CPH.LogInfo(string.Join("\n",
									"OBS responded with:",
									response,
									"to input:",
									JsonConvert.SerializeObject(new {requestType, requestData})));

            return false;
        }

        sceneUuid = Guid.Parse(responseGuidObj.sceneUuid);
        string currentScene = CPH.ObsGetCurrentScene();

        var responseSceneList = CPH.ObsSendRaw("GetSceneList", "{}");
        var scenesList = JsonConvert.DeserializeObject<OBSSceneList>(responseSceneList);

        if (scenesList != null) {
            bool doesSceneSourceExist = scenesList.scenes
                                                   .Any(x => x.sceneName.Equals(SceneName));

            CPH.LogInfo($"The value of doesSceneSourceExist is {doesSceneSourceExist}");

            if (doesSceneSourceExist && scenesList.scenes.Any(x => Guid.Parse(x.sceneUuid).Equals(sceneUuid))) {
                CPH.LogInfo($"Rejoice, {SceneName} was discovered in {currentScene}!");

                return true;
            }

            CPH.LogInfo($"{SceneName} source may be lost forever... was not found in {currentScene}.");
        } else {
            CPH.LogError("The list returned from OBS was null.");
            return false;
        }

        return false;
    }
    
	public class OBSSceneList {
		public string currentPreviewSceneName { get; set; }
		public string currentPreviewSceneUuid { get; set; }
		public string currentProgramSceneName { get; set; }
		public string currentProgramSceneUuid { get; set; }
		public List<OBSSceneListScene> scenes { get; set; }
	}

	public class OBSSceneListScene {
		public int sceneIndex { get; set; }
		public string sceneName { get; set; }
		public string sceneUuid { get; set; }
	}
}