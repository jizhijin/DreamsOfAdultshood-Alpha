using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

// Integration checks against the supplied main project; editor-only.
[InitializeOnLoad]
public static class Mission6_MainAudit_Jizhijin
{
    const string Out = "docs/Mission6_Jizhijin/", Flag = "Mission6.MainAudit";
    static readonly List<string> checks = new List<string>(), errors = new List<string>();
    static double next, deadline;
    static int step, sceneIndex;
    static string[] otherScenes;
    static bool oldBackground;
    static Keyboard keyboard;
    static Mission6_MainAudit_Jizhijin()
    {
        EditorApplication.update += Tick;
        EditorApplication.playModeStateChanged += Changed;
        Application.logMessageReceived += (message, trace, type) =>
        {
            if (SessionState.GetBool(Flag, false) && (type == LogType.Error || type == LogType.Exception || type == LogType.Assert))
                errors.Add(SceneManager.GetActiveScene().name + ": " + message + "\n" + trace);
        };
    }
    static void Save() { Directory.CreateDirectory(Out); File.WriteAllLines(Out + "main-runtime-verification.txt", checks.Concat(errors.Select(e => "RUNTIME ERROR: " + e))); }
    static void Check(bool ok, string text) { checks.Add((ok ? "PASS: " : "FAIL: ") + text); Save(); if (!ok) throw new Exception(text); }
    public static void Inspect()
    {
        var lines = new List<string>();
        var setup = EditorSceneManager.GetSceneManagerSetup();
        try
        {
            foreach (var item in EditorBuildSettings.scenes.Where(s => s.enabled))
            {
                var scene = EditorSceneManager.OpenScene(item.path, OpenSceneMode.Single);
                int missing = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)).Sum(t => GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject));
                lines.Add((missing == 0 ? "PASS: " : "FAIL: ") + item.path + " | missing scripts: " + missing);
            }
            lines.Add("Build scene order: " + string.Join(" -> ", EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => Path.GetFileNameWithoutExtension(s.path))));
        }
        finally
        {
            Directory.CreateDirectory(Out); File.WriteAllLines(Out + "main-scene-inspection.txt", lines);
            if (setup.Any(s => s.isLoaded && s.isActive)) EditorSceneManager.RestoreSceneManagerSetup(setup);
            else EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }
    }
    public static void Prepare()
    {
        Inspect();
        EditorSceneManager.OpenScene("Assets/Mission6_Jizhijin/Scenes/Mission6_Jizhijin.unity", OpenSceneMode.Single);
    }
    public static void StartSmoke()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        checks.Clear(); errors.Clear();
        SessionState.SetString(Flag + ".previous", AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));
        SessionState.SetBool(Flag, true);
        EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(EditorBuildSettings.scenes.First(s => s.enabled).path);
        EditorApplication.EnterPlaymode();
    }
    static void Changed(PlayModeStateChange state)
    {
        if (!SessionState.GetBool(Flag, false)) return;
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            step = 0; next = EditorApplication.timeSinceStartup + 1.5; deadline = next + 120;
            oldBackground = Application.runInBackground; Application.runInBackground = true;
            SessionState.SetInt(Flag + ".inputBackground", (int)InputSystem.settings.backgroundBehavior);
            SessionState.SetInt(Flag + ".editorInput", (int)InputSystem.settings.editorInputBehaviorInPlayMode);
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            keyboard = InputSystem.AddDevice<Keyboard>();
            keyboard.MakeCurrent();
            SessionState.SetInt(Flag + ".keyboard", keyboard.deviceId);
        }
        if (state == PlayModeStateChange.EnteredEditMode)
        {
            EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(SessionState.GetString(Flag + ".previous", ""));
            SessionState.SetBool(Flag, false);
            var device = InputSystem.GetDeviceById(SessionState.GetInt(Flag + ".keyboard", -1));
            if (device != null) InputSystem.RemoveDevice(device);
            InputSystem.settings.backgroundBehavior = (InputSettings.BackgroundBehavior)SessionState.GetInt(Flag + ".inputBackground", 0);
            InputSystem.settings.editorInputBehaviorInPlayMode = (InputSettings.EditorInputBehaviorInPlayMode)SessionState.GetInt(Flag + ".editorInput", 0);
            Save();
        }
    }
    static void Advance(int value, double seconds = 1.2) { step = value; next = EditorApplication.timeSinceStartup + seconds; }
    static void Finish()
    {
        checks.Add(errors.Count == 0 ? "PASS: no runtime errors across the main integration smoke test" : "FAIL: runtime errors captured");
        Save();
        Time.timeScale = 1; Application.runInBackground = oldBackground; step = 100;
        EditorApplication.ExitPlaymode();
    }
    public static void BuildWindows()
    {
        string location = Path.GetFullPath("../WindowsPlayer/MainCompatibility.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(location));
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray(),
            locationPathName = location, target = BuildTarget.StandaloneWindows64, options = BuildOptions.Development
        });
        Directory.CreateDirectory(Out);
        File.WriteAllText(Out + "main-player-build.txt", report.summary.result + "\nErrors: " + report.summary.totalErrors + "\nWarnings: " + report.summary.totalWarnings + "\nOutput: " + location);
        if (report.summary.result != BuildResult.Succeeded) throw new Exception("Main player build failed");
    }
    static void Tick()
    {
        if (!EditorApplication.isCompiling && !EditorApplication.isUpdating && !EditorApplication.isPlayingOrWillChangePlaymode)
        {
            foreach (string action in new[] { "inspect", "smoke", "build" })
            {
                string path = "Temp/Mission6_MainAudit." + action;
                if (!File.Exists(path)) continue;
                File.Delete(path);
                if (action == "inspect") Prepare(); else if (action == "smoke") StartSmoke(); else BuildWindows();
                return;
            }
        }
        if (!SessionState.GetBool(Flag, false) || !EditorApplication.isPlaying || step == 100 || EditorApplication.timeSinceStartup < next) return;
        try
        {
            if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException();
            switch (step)
            {
                case 0:
                    Check(SceneManager.GetActiveScene().name == "MainMenu", "the Alpha starts at the main menu");
                    var select = UnityEngine.Object.FindFirstObjectByType<LevelSelectMenu>();
                    Check(select != null, "main level selection menu exists");
                    var names = new SerializedObject(select).FindProperty("sceneNames");
                    string[] expectedNames = { "Mission6_Jizhijin", "Mission2_TianyuTang", "Mission1", "ZhuohengWu Mission5", "Mission3", "Mission4" };
                    Check(Enumerable.Range(0, names.arraySize).Select(i => names.GetArrayElementAtIndex(i).stringValue).SequenceEqual(expectedNames), "menu entries retain their serialized scene destinations");
                    int targetIndex = Enumerable.Range(0, names.arraySize).First(i => names.GetArrayElementAtIndex(i).stringValue == "Mission6_Jizhijin");
                    UnityEngine.UI.Button linked = null;
                    foreach (var button in UnityEngine.Object.FindObjectsByType<UnityEngine.UI.Button>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                    {
                        var calls = new SerializedObject(button).FindProperty("m_OnClick.m_PersistentCalls.m_Calls");
                        for (int i = 0; i < calls.arraySize; i++)
                        {
                            var call = calls.GetArrayElementAtIndex(i);
                            if (call.FindPropertyRelative("m_Target").objectReferenceValue == select && call.FindPropertyRelative("m_MethodName").stringValue == "SelectLevel" && call.FindPropertyRelative("m_Arguments.m_IntArgument").intValue == targetIndex) linked = button;
                        }
                    }
                    Check(linked != null, "main menu has a button bound to the serialized Mission6 entry");
                    string label = linked.GetComponentInChildren<TMPro.TMP_Text>(true)?.text ?? linked.GetComponentInChildren<UnityEngine.UI.Text>(true)?.text ?? "";
                    int missionNumber = SceneUtility.GetBuildIndexByScenePath(Mission6_Setup_Jizhijin.ScenePath);
                    Check(label.Replace(" ", "").ToUpperInvariant() == $"MISSION{missionNumber}", "the Mission6 button is labeled with its campaign position");
                    linked.onClick.Invoke(); Advance(1); break;
                case 1:
                    var c = UnityEngine.Object.FindFirstObjectByType<Mission6_Campaign_Jizhijin>();
                    Check(SceneManager.GetActiveScene().name == "Mission6_Jizhijin" && c != null, "main menu selects the updated Mission6 scene");
                    Check(c.Phase == Mission6_Phase_Jizhijin.Planning && c.moving.enabled && Time.timeScale == 1, "entering Mission6 resets time and player controls");
                    Check(Cursor.lockState==CursorLockMode.None&&Cursor.visible&&c.moving.enabled,"first PLAN allows direct mouse clicking while WASD stays active");
                    UnityEngine.InputSystem.InputSystem.QueueStateEvent(keyboard,new UnityEngine.InputSystem.LowLevel.KeyboardState(UnityEngine.InputSystem.Key.Tab));Advance(11,.2);break;
                case 11:
                    var entry=UnityEngine.Object.FindFirstObjectByType<Mission6_Campaign_Jizhijin>();
                    Check(entry.moving.enabled && entry.mouseLook.enabled && (Application.isBatchMode || Cursor.lockState == CursorLockMode.Locked), "TAB can switch the first PLAN to normal mouse look");
                    UnityEngine.InputSystem.InputSystem.QueueStateEvent(keyboard,new UnityEngine.InputSystem.LowLevel.KeyboardState());Advance(111,.1);break;
                case 111:
                    UnityEngine.InputSystem.InputSystem.QueueStateEvent(keyboard,new UnityEngine.InputSystem.LowLevel.KeyboardState(UnityEngine.InputSystem.Key.Tab));Advance(112,.1);break;
                case 112:
                    var free=UnityEngine.Object.FindFirstObjectByType<Mission6_Campaign_Jizhijin>();Check(free.moving.enabled&&!free.mouseLook.enabled&&Cursor.lockState==CursorLockMode.None,"TAB returns to a clickable cursor without disabling PLAN movement");
                    UnityEngine.InputSystem.InputSystem.QueueStateEvent(keyboard,new UnityEngine.InputSystem.LowLevel.KeyboardState(UnityEngine.InputSystem.Key.Digit2));Advance(12,.2);break;
                case 12:
                    var second=UnityEngine.Object.FindFirstObjectByType<Mission6_Campaign_Jizhijin>();
                    Check(second.CurrentLevel.stageNumber==2&&second.FirstSkipped&&second.moving.enabled,"2 shortcut directly enters level two from the released cursor state");
                    second.BeginExploration();
                    UnityEngine.InputSystem.InputSystem.QueueStateEvent(keyboard, new UnityEngine.InputSystem.LowLevel.KeyboardState(UnityEngine.InputSystem.Key.Z));
                    Advance(120, .2); break;
                case 120:
                    var withPhoto = UnityEngine.Object.FindFirstObjectByType<Mission6_Campaign_Jizhijin>();
                    Check(withPhoto.photo.IsVisible && withPhoto.photo.Used, "Z opens the once-per-level photo");
                    UnityEngine.InputSystem.InputSystem.QueueStateEvent(keyboard, new UnityEngine.InputSystem.LowLevel.KeyboardState());
                    Advance(121, .2); break;
                case 121:
                    UnityEngine.InputSystem.InputSystem.QueueStateEvent(keyboard, new UnityEngine.InputSystem.LowLevel.KeyboardState(UnityEngine.InputSystem.Key.Z));
                    Advance(122, .2); break;
                case 122:
                    var closedPhoto = UnityEngine.Object.FindFirstObjectByType<Mission6_Campaign_Jizhijin>();
                    Check(!closedPhoto.photo.IsVisible && closedPhoto.photo.Used && !closedPhoto.photo.TryUsePhoto(), "second Z closes the photo without restoring the use");
                    closedPhoto.OpenPuzzle(closedPhoto.CurrentLevel);
                    Advance(2, .2); break;
                case 2:
                    var campaign = UnityEngine.Object.FindFirstObjectByType<Mission6_Campaign_Jizhijin>();
                    Check(campaign.PuzzleOpen && Time.timeScale == 0 && !campaign.moving.enabled, "main scripts do not override Mission6 answer pause");
                    Check(campaign.puzzles[1].CorrectWords == 0, "the exit buttons are available before any answers");
                    campaign.puzzles[1].transform.Find("MainMenu").GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
                    Advance(20); break;
                case 20:
                    Check(SceneManager.GetActiveScene().name == "MainMenu", "MAIN MENU returns directly from the unanswered crossword");
                    UnityEngine.Object.FindFirstObjectByType<LevelSelectMenu>().SelectLevel(0);
                    Advance(21); break;
                case 21:
                    var fresh = UnityEngine.Object.FindFirstObjectByType<Mission6_Campaign_Jizhijin>();
                    Check(fresh.CurrentLevel.stageNumber == 1, "reopening the mission from the menu starts at level one");
                    fresh.GoToSecondLevel();
                    fresh.BeginExploration();
                    fresh.OpenPuzzle(fresh.CurrentLevel);
                    fresh.puzzles[1].transform.Find("NextMission").GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
                    Advance(3); break;
                case 3:
                    Check(SceneManager.GetActiveScene().name == "Mission2_TianyuTang", "NEXT MISSION opens the flashlight maze without solving the crossword");
                    Check(UnityEngine.Object.FindFirstObjectByType<Moving>() != null && Time.timeScale == 1, "the next mission starts unpaused");
                    var agents = UnityEngine.Object.FindObjectsByType<UnityEngine.AI.NavMeshAgent>(FindObjectsSortMode.None);
                    Check(agents.Length > 0 && agents.All(agent => agent.isOnNavMesh), "flashlight maze enemies bind to their NavMesh");
                    otherScenes = EditorBuildSettings.scenes.Where(s => s.enabled).Skip(3).Select(s => s.path).ToArray();
                    sceneIndex = 0; Advance(4, .1); break;
                case 4:
                    if (sceneIndex == otherScenes.Length) { Finish(); break; }
                    Time.timeScale = 1; SceneManager.LoadScene(otherScenes[sceneIndex]); Advance(5); break;
                case 5:
                    Check(SceneManager.GetActiveScene().path == otherScenes[sceneIndex], "main scene loads: " + SceneManager.GetActiveScene().name);
                    Check(SceneManager.GetActiveScene().GetRootGameObjects().Any(r => r.GetComponentInChildren<Camera>(true) != null), "main scene retains a camera: " + SceneManager.GetActiveScene().name);
                    sceneIndex++; Advance(4, .1); break;
            }
        }
        catch (Exception ex) { checks.Add("FAIL: " + ex); Finish(); }
    }
}
