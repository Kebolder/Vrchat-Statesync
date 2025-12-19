using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace JaxTools.StateSync
{
    public class JaxStateSyncToolWindow : EditorWindow
    {
        private AnimatorController controller;

        private int selectedLayerIndex = -1;

        private Vector2 layerScroll;
        private Vector2 stateScroll;
        private Vector2 windowScroll;

        private readonly List<StateEntry> cachedStates = new();
        private bool needsRebuildStates;

        private const int MaxVisibleStateEntries = 4;
        private const int MaxVisibleLayerEntries = 4;
        private const float EntryBoxHeight = 36f;
        private const float EntrySpacing = 1f;
        private const float StateNameMinWidth = 280f;
        private const float AssignInputWidth = 44f;
        private const float StateTagBoxWidth = 90f;
        private const string StateButtonFrameHex = "dadada";
        private const string StateLeftEmptyHex = "B8B8B8";

        // UI colors
        private static readonly Color Orange = ColorFromHex("ff6e00");
        private static readonly Color LightGray = ColorFromHex("FFFFFF");
        private static readonly Color SectionGray = ColorFromHex("D1D1D1");
        private static readonly Color ConflictRed = ColorFromHex("D94A4A");
        private static readonly Color LocalTagColor = ColorFromHex("59e5ff");
        private static readonly Color RemoteTagColor = ColorFromHex("f5ff59");

        private GUIStyle stateBoxStyle;
        private GUIStyle stateLabelStyle;
        private GUIStyle stateTagStyle;
        private GUIStyle stateNameBoxStyle;
        private GUIStyle stateNameButtonStyle;
        private GUIStyle stateLeftBoxStyle;
        private GUIStyle stateButtonFrameStyle;
        private GUIStyle assignLabelStyle;
        private int cachedStatesHash;

        private string parameterPrefix = "Remote_";
        private string stateFilterPrefix = "";
        private int remoteParameterIndex = -1;
        private string remoteParameterName = "";
        private bool removeDriversFromRemote;
        private bool addDriverForLocalSyncState;
        private bool packIntoStateMachine;
        private bool matchTransitionTimes;
        private string clearStatePrefix = "Remote_";

        private readonly Dictionary<int, int> manualStateAssignments = new();
        private readonly Dictionary<int, string> manualStateAssignmentText = new();
        private readonly List<string> conflictStateNames = new();
        private int localStartStateId;
        private int remoteStartStateId;
        private string localTreeName = "Local Tree";
        private string remoteTreeName = "Remote Tree";
        private string pendingFocusControl;

        private static Color ColorFromHex(string hex)
        {
            if (string.IsNullOrEmpty(hex)) return Color.white;
            if (hex[0] == '#') hex = hex[1..];
            if (hex.Length != 6) return Color.white;

            byte r = 0;
            byte g = 0;
            byte b = 0;
            bool ok =
                byte.TryParse(hex.Substring(0, 2), System.Globalization.NumberStyles.HexNumber, null, out r) &&
                byte.TryParse(hex.Substring(2, 2), System.Globalization.NumberStyles.HexNumber, null, out g) &&
                byte.TryParse(hex.Substring(4, 2), System.Globalization.NumberStyles.HexNumber, null, out b);

            if (!ok) return Color.white;

            return new Color(r / 255f, g / 255f, b / 255f);
        }

        private struct StateEntry
        {
            public string Path;
            public AnimatorState State;
        }

        [MenuItem("Jax's Tools/State Sync Tool")]
        public static void Open()
        {
            var w = GetWindow<JaxStateSyncToolWindow>("State Sync Tool");
            w.minSize = new Vector2(520, 320);
            w.Show();
        }

        private void OnGUI()
        {
            windowScroll = EditorGUILayout.BeginScrollView(windowScroll);
            EditorGUILayout.LabelField("Animator Controller", EditorStyles.boldLabel);

            var newController = (AnimatorController)EditorGUILayout.ObjectField(
                "Controller",
                controller,
                typeof(AnimatorController),
                false
            );

            if (newController != controller)
            {
                controller = newController;
                selectedLayerIndex = -1;
                cachedStates.Clear();
                needsRebuildStates = true;
            }

            EditorGUILayout.Space(8);

            if (controller == null)
            {
                EditorGUILayout.HelpBox("Assign an AnimatorController to show its layers.", MessageType.Info);
                EditorGUILayout.EndScrollView();
                return;
            }

            var layers = controller.layers;
            if (layers == null || layers.Length == 0)
            {
                EditorGUILayout.HelpBox("This controller has no layers.", MessageType.Warning);
                EditorGUILayout.EndScrollView();
                return;
            }

            if (selectedLayerIndex < 0 || selectedLayerIndex >= layers.Length)
            {
                selectedLayerIndex = 0;
                needsRebuildStates = true;
            }

            var prevSectionBg = GUI.backgroundColor;
            GUI.backgroundColor = SectionGray;
            EditorGUILayout.BeginVertical(GUI.skin.box);
            GUI.backgroundColor = prevSectionBg;

            EditorGUILayout.LabelField("Layers", EditorStyles.boldLabel);

            int visibleLayerCount = Mathf.Min(MaxVisibleLayerEntries, layers.Length);
            float layerListHeight =
                (visibleLayerCount * EntryBoxHeight) +
                ((visibleLayerCount - 1) * EntrySpacing) +
                8f;
            bool useLayerScroll = layers.Length > MaxVisibleLayerEntries;

            if (useLayerScroll)
                layerScroll = EditorGUILayout.BeginScrollView(layerScroll, GUILayout.Height(layerListHeight));

            for (int i = 0; i < layers.Length; i++)
            {
                var layerName = string.IsNullOrEmpty(layers[i].name) ? "<Unnamed Layer>" : layers[i].name;

                var prevBg = GUI.backgroundColor;
                if (i == selectedLayerIndex) GUI.backgroundColor = new Color(0.85f, 0.85f, 0.85f);

                if (GUILayout.Button($"{i + 1}. {layerName}", GUILayout.Height(28)))
                {
                    selectedLayerIndex = i;
                    needsRebuildStates = true;
                }

                GUI.backgroundColor = prevBg;
            }
            if (useLayerScroll)
                EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(8);

            var rootSM = layers[selectedLayerIndex].stateMachine;
            if (rootSM == null)
            {
                EditorGUILayout.HelpBox("Selected layer has no state machine.", MessageType.Warning);
                EditorGUILayout.EndScrollView();
                return;
            }

            if (needsRebuildStates)
            {
                pendingFocusControl = GUI.GetNameOfFocusedControl();
                RebuildStates(rootSM);
                needsRebuildStates = false;
                stateScroll = Vector2.zero;
                if (!string.IsNullOrEmpty(pendingFocusControl))
                    EditorGUI.FocusTextInControl(pendingFocusControl);
            }
            else
            {
                UpdateStateCacheIfChanged(rootSM);
            }

            prevSectionBg = GUI.backgroundColor;
            GUI.backgroundColor = SectionGray;
            EditorGUILayout.BeginVertical(GUI.skin.box);
            GUI.backgroundColor = prevSectionBg;

            EditorGUILayout.LabelField($"States ({layers[selectedLayerIndex].name})", EditorStyles.boldLabel);
            DrawStatesList(rootSM);

            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(8);

            prevSectionBg = GUI.backgroundColor;
            GUI.backgroundColor = SectionGray;
            EditorGUILayout.BeginVertical(GUI.skin.box);
            GUI.backgroundColor = prevSectionBg;

            EditorGUILayout.LabelField("Settings", EditorStyles.boldLabel);
            DrawRemoteParameterDropdown();
            parameterPrefix = EditorGUILayout.TextField("Remote state prefix", parameterPrefix);

            EditorGUILayout.Space(4);
            removeDriversFromRemote = EditorGUILayout.ToggleLeft("Remove drivers from remote", removeDriversFromRemote);
            addDriverForLocalSyncState = EditorGUILayout.ToggleLeft("Add drivers for local sync state", addDriverForLocalSyncState);
            packIntoStateMachine = EditorGUILayout.ToggleLeft("Pack into StateMachine", packIntoStateMachine);
            matchTransitionTimes = EditorGUILayout.ToggleLeft("Match transition times", matchTransitionTimes);

            EditorGUILayout.Space(8);
            if (GUILayout.Button("Create Remote sync", GUILayout.Height(28)))
            {
                StateSyncBuilder.BuildRemoteSync(
                    controller,
                    selectedLayerIndex,
                    parameterPrefix,
                    BuildAssignedNumberMap(),
                    localTreeName,
                    remoteTreeName,
                    remoteParameterName,
                    removeDriversFromRemote,
                    addDriverForLocalSyncState,
                    packIntoStateMachine,
                    matchTransitionTimes
                );
            }

            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(8);

            prevSectionBg = GUI.backgroundColor;
            GUI.backgroundColor = SectionGray;
            EditorGUILayout.BeginVertical(GUI.skin.box);
            GUI.backgroundColor = prevSectionBg;

            EditorGUILayout.LabelField("Utilities", EditorStyles.boldLabel);
            clearStatePrefix = EditorGUILayout.TextField("Clear state prefix", clearStatePrefix);

            EditorGUILayout.Space(4);
            if (GUILayout.Button("Clear states with prefix", GUILayout.Height(28)))
            {
                string prefixToUse = string.IsNullOrWhiteSpace(clearStatePrefix) ? parameterPrefix : clearStatePrefix;
                Undo.RegisterCompleteObjectUndo(controller, "Clear states with prefix");
                ClearStatesWithPrefix(rootSM, prefixToUse);
                needsRebuildStates = true;
            }

            EditorGUILayout.EndVertical();

            EditorGUILayout.EndScrollView();
        }

        private void DrawStatesList(AnimatorStateMachine rootSM)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Filter out", GUILayout.Width(80));
            stateFilterPrefix = EditorGUILayout.TextField(stateFilterPrefix);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Local Tree", GUILayout.Width(80));
            localTreeName = EditorGUILayout.TextField(localTreeName);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Remote Tree", GUILayout.Width(80));
            remoteTreeName = EditorGUILayout.TextField(remoteTreeName);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(4);

            var visibleStates = GetFilteredStates();
            if (visibleStates.Count == 0)
            {
                EditorGUILayout.HelpBox("No states found on this layer.", MessageType.Info);
                return;
            }

            EnsureStateStyles();
            var defaultState = rootSM.defaultState;
            var usedAssignments = new Dictionary<int, int>();
            var assignmentToName = new Dictionary<int, string>();
            conflictStateNames.Clear();
            localStartStateId = FindStateIdByName(visibleStates, localTreeName);
            remoteStartStateId = FindStateIdByName(visibleStates, remoteTreeName);
            SortStates(visibleStates, defaultState);

            int visibleCount = Mathf.Min(MaxVisibleStateEntries, visibleStates.Count);
            int heightCount = Mathf.Max(1, MaxVisibleStateEntries + 1);
            float listHeight =
                (heightCount * EntryBoxHeight) +
                ((heightCount - 1) * EntrySpacing) +
                8f;

            bool useScroll = visibleStates.Count > MaxVisibleStateEntries;

            EditorGUILayout.BeginVertical(GUILayout.Height(listHeight));

            if (useScroll)
                stateScroll = EditorGUILayout.BeginScrollView(stateScroll, false, true);

            for (int i = 0; i < visibleStates.Count; i++)
            {
                var entry = visibleStates[i];
                bool isDefault = (defaultState != null && entry.State == defaultState);

                GUILayout.Space(i == 0 ? 0 : EntrySpacing);

                int stateId = entry.State != null ? entry.State.GetInstanceID() : 0;
                int assignedNumber = -1;
                bool isConflict = false;
                bool isNonNumbered = IsNonNumberedState(isDefault, stateId);
                if (!isNonNumbered)
                {
                    int autoNumber = ParseTrailingNumber(entry.State != null ? entry.State.name : entry.Path);
                    assignedNumber = GetAssignedNumber(stateId, autoNumber);
                    isConflict = assignedNumber >= 0 && usedAssignments.ContainsKey(assignedNumber);
                    if (!isConflict && assignedNumber >= 0)
                    {
                        usedAssignments[assignedNumber] = stateId;
                        assignmentToName[assignedNumber] = entry.State != null ? entry.State.name : entry.Path;
                    }
                    else if (isConflict && assignedNumber >= 0)
                    {
                        if (assignmentToName.TryGetValue(assignedNumber, out string existing))
                        {
                            if (!conflictStateNames.Contains(existing))
                                conflictStateNames.Add(existing);
                        }

                        string currentName = entry.State != null ? entry.State.name : entry.Path;
                        if (!conflictStateNames.Contains(currentName))
                            conflictStateNames.Add(currentName);
                    }
                }

                var prevBg = GUI.backgroundColor;
                if (isConflict)
                    GUI.backgroundColor = ConflictRed;
                else
                    GUI.backgroundColor = isDefault ? Orange : LightGray;

                var prevOuterBg = GUI.backgroundColor;
                GUI.backgroundColor = ColorFromHex(StateButtonFrameHex);
                EditorGUILayout.BeginVertical(stateButtonFrameStyle);
                GUI.backgroundColor = prevOuterBg;

                EditorGUILayout.BeginHorizontal(GUILayout.Height(EntryBoxHeight));

                string leftTag = GetLeftTagLabel(isDefault, stateId, assignedNumber, isConflict);
                var prevLeftBg = GUI.backgroundColor;
                GUI.backgroundColor = GetLeftTagColor(isDefault, stateId, assignedNumber);
                GUILayout.Label(leftTag, stateLeftBoxStyle, GUILayout.Width(StateTagBoxWidth), GUILayout.Height(EntryBoxHeight));
                GUI.backgroundColor = prevLeftBg;
                GUILayout.Space(2f);

                EditorGUILayout.BeginVertical(GUILayout.MinWidth(StateNameMinWidth), GUILayout.ExpandWidth(true));
                var prevButtonBg = GUI.backgroundColor;
                GUI.backgroundColor = GetStateButtonColor(isDefault, stateId, isConflict);
                if (GUILayout.Button(
                    entry.Path ?? "",
                    stateNameButtonStyle,
                    GUILayout.Height(EntryBoxHeight),
                    GUILayout.ExpandWidth(true)
                ))
                {
                    SelectStateInAnimator(entry.State);
                }
                GUI.backgroundColor = prevButtonBg;
                EditorGUILayout.EndVertical();

                GUILayout.Space(6f);
                DrawCentered(() =>
                {
                    EditorGUILayout.BeginHorizontal();
                    if (!isNonNumbered)
                    {
                        GUILayout.Label("Assign #", assignLabelStyle, GUILayout.Width(60));
                        string currentNumberText = manualStateAssignmentText.TryGetValue(stateId, out string storedText)
                            ? storedText
                            : (assignedNumber >= 0 ? assignedNumber.ToString() : "");
                        string controlName = $"AssignNumber_{stateId}";
                        GUI.SetNextControlName(controlName);
                        string nextText = EditorGUILayout.TextField(currentNumberText, GUILayout.Width(AssignInputWidth));
                        if (nextText != currentNumberText)
                        {
                            if (string.IsNullOrWhiteSpace(nextText))
                            {
                                manualStateAssignments.Remove(stateId);
                                manualStateAssignmentText.Remove(stateId);
                            }
                            else if (int.TryParse(nextText, out int parsed))
                            {
                                manualStateAssignments[stateId] = parsed;
                                manualStateAssignmentText[stateId] = nextText;
                            }
                            else
                            {
                                manualStateAssignmentText[stateId] = nextText;
                            }
                        }
                    }

                    EditorGUILayout.EndHorizontal();
                }, EntryBoxHeight);

                EditorGUILayout.EndHorizontal();
                EditorGUILayout.EndVertical();

                GUI.backgroundColor = prevBg;
            }

            if (useScroll)
                EditorGUILayout.EndScrollView();

            EditorGUILayout.EndVertical();

            if (conflictStateNames.Count > 0)
            {
                string conflictList = string.Join(", ", conflictStateNames);
                EditorGUILayout.HelpBox(
                    $"State number conflicts detected: {conflictList}",
                    MessageType.Error
                );
            }

            if (localStartStateId == 0 || remoteStartStateId == 0)
            {
                string missing = localStartStateId == 0 && remoteStartStateId == 0
                    ? "Local and Remote Start"
                    : (localStartStateId == 0 ? "Local Start" : "Remote Start");
                EditorGUILayout.HelpBox(
                    $"No {missing} set. This might not be correct.",
                    MessageType.Warning
                );
            }
        }

        private void RebuildStates(AnimatorStateMachine root)
        {
            cachedStates.Clear();
            var states = JaxTools.StateSync.Utility.AnimatorTools.GetStates(root);
            foreach (var entry in states)
                cachedStates.Add(new StateEntry { Path = entry.Path, State = entry.State });
            cachedStatesHash = JaxTools.StateSync.Utility.AnimatorTools.GetStatesHash(root);
        }

        private void UpdateStateCacheIfChanged(AnimatorStateMachine root)
        {
            int currentHash = JaxTools.StateSync.Utility.AnimatorTools.GetStatesHash(root);
            if (currentHash != cachedStatesHash)
            {
                RebuildStates(root);
            }
        }

        private void EnsureStateStyles()
        {
            if (stateBoxStyle == null)
            {
                stateBoxStyle = new GUIStyle(EditorStyles.helpBox)
                {
                    padding = new RectOffset(10, 10, 4, 4)
                };
            }

            if (stateLabelStyle == null)
            {
                stateLabelStyle = new GUIStyle(EditorStyles.label)
                {
                    fontSize = 15,
                    alignment = TextAnchor.MiddleLeft,
                    fontStyle = FontStyle.Bold
                };
            }

            if (stateTagStyle == null)
            {
                stateTagStyle = new GUIStyle(EditorStyles.miniBoldLabel)
                {
                    alignment = TextAnchor.MiddleLeft
                };
            }

            if (stateNameBoxStyle == null)
            {
                stateNameBoxStyle = new GUIStyle(GUI.skin.box)
                {
                    padding = new RectOffset(10, 10, 4, 4)
                };
            }

            if (stateNameButtonStyle == null)
            {
                stateNameButtonStyle = new GUIStyle(GUI.skin.button)
                {
                    alignment = TextAnchor.MiddleLeft,
                    padding = new RectOffset(10, 10, 4, 4),
                    fontStyle = FontStyle.Bold,
                    fontSize = 15,
                    richText = true,
                    margin = new RectOffset(0, 0, 0, 0)
                };
            }

            if (stateLeftBoxStyle == null)
            {
                stateLeftBoxStyle = new GUIStyle(GUI.skin.box)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontStyle = FontStyle.Bold,
                    fontSize = 12,
                    margin = new RectOffset(0, 0, 0, 0),
                    padding = new RectOffset(4, 4, 2, 2)
                };
                stateLeftBoxStyle.normal.textColor = Color.white;
                stateLeftBoxStyle.hover.textColor = Color.white;
                stateLeftBoxStyle.active.textColor = Color.white;
                stateLeftBoxStyle.focused.textColor = Color.white;
            }

            if (stateButtonFrameStyle == null)
            {
                stateButtonFrameStyle = new GUIStyle(GUI.skin.box)
                {
                    padding = new RectOffset(2, 2, 2, 2),
                    margin = new RectOffset(0, 0, 0, 0)
                };
            }

            if (assignLabelStyle == null)
            {
                assignLabelStyle = new GUIStyle(EditorStyles.label)
                {
                    fontSize = 13,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleLeft
                };
            }
        }

        private void DrawRemoteParameterDropdown()
        {
            var intParams = JaxTools.StateSync.Utility.AnimatorTools.GetParametersByType(
                controller,
                AnimatorControllerParameterType.Int
            );

            string[] options;
            if (intParams.Count == 0)
            {
                options = new[] { "<No int parameters>" };
                remoteParameterIndex = 0;
                remoteParameterName = "";
            }
            else
            {
                options = new string[intParams.Count];
                for (int i = 0; i < intParams.Count; i++)
                    options[i] = intParams[i].name;

                if (remoteParameterIndex < 0 || remoteParameterIndex >= options.Length)
                    remoteParameterIndex = 0;

                remoteParameterName = options[remoteParameterIndex];
            }

            remoteParameterIndex = EditorGUILayout.Popup("Remote Parameter", remoteParameterIndex, options);
            if (intParams.Count > 0 && remoteParameterIndex >= 0 && remoteParameterIndex < options.Length)
                remoteParameterName = options[remoteParameterIndex];
        }

        private List<StateEntry> GetFilteredStates()
        {
            if (string.IsNullOrWhiteSpace(stateFilterPrefix))
                return cachedStates;

            var filtered = new List<StateEntry>();
            foreach (var entry in cachedStates)
            {
                if (entry.State == null) continue;
                if (!entry.State.name.StartsWith(stateFilterPrefix, System.StringComparison.Ordinal))
                    filtered.Add(entry);
            }

            return filtered;
        }

        private Dictionary<int, int> BuildAssignedNumberMap()
        {
            var map = new Dictionary<int, int>();
            int defaultId = 0;
            if (controller != null && selectedLayerIndex >= 0 && selectedLayerIndex < controller.layers.Length)
            {
                var root = controller.layers[selectedLayerIndex].stateMachine;
                if (root != null && root.defaultState != null)
                    defaultId = root.defaultState.GetInstanceID();
            }

            foreach (var entry in cachedStates)
            {
                if (entry.State == null) continue;
                int stateId = entry.State.GetInstanceID();
                if (stateId == defaultId || stateId == localStartStateId || stateId == remoteStartStateId)
                    continue;

                int autoNumber = ParseTrailingNumber(entry.State.name);
                int assignedNumber = GetAssignedNumber(stateId, autoNumber);
                if (assignedNumber >= 0)
                    map[stateId] = assignedNumber;
            }

            return map;
        }

        private static void DrawCentered(System.Action draw, float height)
        {
            GUILayout.BeginVertical(GUILayout.Height(height));
            GUILayout.FlexibleSpace();
            draw();
            GUILayout.FlexibleSpace();
            GUILayout.EndVertical();
        }

        private static int FindStateIdByName(List<StateEntry> states, string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return 0;
            foreach (var entry in states)
            {
                if (entry.State == null) continue;
                if (string.Equals(entry.State.name, name, System.StringComparison.Ordinal))
                    return entry.State.GetInstanceID();
            }
            return 0;
        }

        private void SortStates(List<StateEntry> states, AnimatorState defaultState)
        {
            if (states == null || states.Count <= 1) return;
            int defaultId = defaultState != null ? defaultState.GetInstanceID() : 0;
            states.Sort((a, b) =>
            {
                int aPriority = GetStateSortPriority(a.State, defaultId);
                int bPriority = GetStateSortPriority(b.State, defaultId);
                int cmp = aPriority.CompareTo(bPriority);
                if (cmp != 0) return cmp;
                return string.Compare(a.Path, b.Path, System.StringComparison.Ordinal);
            });
        }

        private int GetStateSortPriority(AnimatorState state, int defaultId)
        {
            if (state == null) return 100;
            int id = state.GetInstanceID();
            if (id == defaultId) return 0;
            if (id == localStartStateId) return 1;
            if (id == remoteStartStateId) return 2;
            return 3;
        }

        private bool IsNonNumberedState(bool isDefault, int stateId)
        {
            return isDefault || stateId == localStartStateId || stateId == remoteStartStateId;
        }

        private int GetAssignedNumber(int stateId, int autoNumber)
        {
            if (stateId != 0 && manualStateAssignments.TryGetValue(stateId, out int manual))
                return manual;

            return autoNumber;
        }

        private string GetLeftTagLabel(bool isDefault, int stateId, int assignedNumber, bool isConflict)
        {
            if (isConflict)
                return "Conflicting";
            if (isDefault)
                return "Default";
            if (stateId == localStartStateId)
                return "Local";
            if (stateId == remoteStartStateId)
                return "Remote";
            if (assignedNumber >= 0)
                return $"#{assignedNumber}";
            return "Not Assigned";
        }

        private Color GetLeftTagColor(bool isDefault, int stateId, int assignedNumber)
        {
            if (isDefault)
                return Orange;
            if (stateId == localStartStateId)
                return LocalTagColor;
            if (stateId == remoteStartStateId)
                return RemoteTagColor;
            if (assignedNumber >= 0)
                return LightGray;
            return ColorFromHex(StateLeftEmptyHex);
        }

        private Color GetStateButtonColor(bool isDefault, int stateId, bool isConflict)
        {
            if (isConflict)
                return ConflictRed;
            if (isDefault)
                return Orange;
            if (stateId == localStartStateId)
                return LocalTagColor;
            if (stateId == remoteStartStateId)
                return RemoteTagColor;
            return LightGray;
        }

        private static int ParseTrailingNumber(string name)
        {
            if (string.IsNullOrEmpty(name)) return -1;

            int i = name.Length - 1;
            while (i >= 0 && char.IsDigit(name[i])) i--;
            if (i == name.Length - 1) return -1;

            string digits = name[(i + 1)..];
            return int.TryParse(digits, out int value) ? value : -1;
        }

        private void SelectStateInAnimator(AnimatorState state)
        {
            if (state == null) return;
            if (controller != null)
                AssetDatabase.OpenAsset(controller);

            Selection.activeObject = state;
            EditorGUIUtility.PingObject(state);
        }

        private static int ClearStatesWithPrefix(AnimatorStateMachine root, string prefix)
        {
            if (root == null || string.IsNullOrEmpty(prefix)) return 0;
            return ClearStatesWithPrefixRecursive(root, prefix);
        }

        private static int ClearStatesWithPrefixRecursive(AnimatorStateMachine sm, string prefix)
        {
            int removed = 0;
            var states = sm.states;
            for (int i = states.Length - 1; i >= 0; i--)
            {
                var state = states[i].state;
                if (state != null && state.name.StartsWith(prefix, System.StringComparison.Ordinal))
                {
                    sm.RemoveState(state);
                    removed++;
                }
            }

            var childStateMachines = sm.stateMachines;
            for (int i = 0; i < childStateMachines.Length; i++)
                removed += ClearStatesWithPrefixRecursive(childStateMachines[i].stateMachine, prefix);

            return removed;
        }

    }
}
