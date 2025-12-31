using System.Collections.Generic;
using JaxTools.StateSync.Utility;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace JaxTools.StateSync
{
    public static class StateSyncBuilder
    {
        private const string LogPrefix = "[StateSync] ";

        public static void BuildRemoteSync(
            AnimatorController controller,
            int layerIndex,
            string remotePrefix,
            Dictionary<int, int> assignedNumbers,
            string localTreeName,
            string remoteTreeName,
            string remoteParameterName,
            bool removeDriversFromRemote,
            bool addDriverForLocalSyncState,
            bool packIntoStateMachine,
            bool matchTransitionTimes
        )
        {
            if (controller == null)
            {
                Debug.LogError(LogPrefix + "BuildRemoteSync aborted: controller is null.");
                return;
            }
            if (layerIndex < 0 || layerIndex >= controller.layers.Length)
            {
                Debug.LogError(LogPrefix + "BuildRemoteSync aborted: layer index is out of range.");
                return;
            }

            var root = controller.layers[layerIndex].stateMachine;
            if (root == null)
            {
                Debug.LogError(LogPrefix + "BuildRemoteSync aborted: selected layer has no state machine.");
                return;
            }

            try
            {
                int localId = FindStateIdByName(root, localTreeName);
                int remoteId = FindStateIdByName(root, remoteTreeName);
                int defaultId = root.defaultState != null ? root.defaultState.GetInstanceID() : 0;

                float minLocalX = float.PositiveInfinity;
                Traverse(root, (parent, child) =>
                {
                    minLocalX = Mathf.Min(minLocalX, child.position.x);
                });

                var clonedByNumber = new Dictionary<int, AnimatorState>();
                var cloneParents = new Dictionary<int, AnimatorStateMachine>();
                var originalsByNumber = new Dictionary<int, AnimatorState>();
                float maxCloneX = float.NegativeInfinity;
                Traverse(root, (parent, child) =>
                {
                    var state = child.state;
                    if (state == null) return;

                    int stateId = state.GetInstanceID();
                    if (!assignedNumbers.TryGetValue(stateId, out int number) || number < 0) return;
                    if (stateId == defaultId || stateId == localId || stateId == remoteId) return;

                    string baseName = string.IsNullOrEmpty(remotePrefix)
                        ? state.name
                        : $"{remotePrefix}{state.name}";
                    string newName = MakeUniqueStateName(parent, baseName);

                    var clone = JaxTools.StateSync.Utility.AnimatorTools.CloneState(
                        state,
                        parent,
                        newName,
                        removeDriversFromRemote
                    );
                    if (clone == null) return;

                    Vector3 pos = child.position;
                    pos.x = -pos.x;
                    UpdateChildPosition(parent, clone, pos);
                    maxCloneX = Mathf.Max(maxCloneX, pos.x);

                    // Ensure cloned states start disconnected.
                    clone.transitions = System.Array.Empty<AnimatorStateTransition>();

                    clonedByNumber[number] = clone;
                    originalsByNumber[number] = state;
                    cloneParents[clone.GetInstanceID()] = parent;
                });

                if (assignedNumbers == null || assignedNumbers.Count == 0)
                    Debug.LogWarning(LogPrefix + "No assigned numbers found; no states will be cloned.");
                if (clonedByNumber.Count == 0)
                    Debug.LogWarning(LogPrefix + "No clone states created. Check assigned numbers and exclusions.");

                const float MinCloneGap = 50f;
                if (clonedByNumber.Count > 0 && minLocalX < float.PositiveInfinity && maxCloneX > minLocalX - MinCloneGap)
                {
                    float shift = maxCloneX - (minLocalX - MinCloneGap);
                    foreach (var clonePair in clonedByNumber)
                    {
                        var clone = clonePair.Value;
                        if (clone == null) continue;
                        var parent = cloneParents.TryGetValue(clone.GetInstanceID(), out var p) ? p : root;
                        if (!TryGetStatePosition(parent, clone.GetInstanceID(), out var pos))
                            continue;
                        pos.x -= shift;
                        UpdateChildPosition(parent, clone, pos);
                    }
                }

                EnsureIntParameter(controller, remoteParameterName);
                var targetMachine = root;
                if (packIntoStateMachine)
                    targetMachine = PackRemoteStates(root, remoteId, clonedByNumber, cloneParents, "Remote Sync");

                ConnectClonedStates(clonedByNumber, originalsByNumber, remoteParameterName, matchTransitionTimes);
                ConnectRemoteTree(targetMachine, remoteId, clonedByNumber, remoteParameterName);

                if (addDriverForLocalSyncState)
                    AddDriversToOriginalStates(controller, layerIndex, root, assignedNumbers, remoteParameterName);

                EditorUtility.SetDirty(controller);
                AssetDatabase.SaveAssets();
                Debug.Log(LogPrefix + "BuildRemoteSync completed.");
            }
            catch (System.Exception ex)
            {
                Debug.LogException(ex);
                Debug.LogError(LogPrefix + "BuildRemoteSync failed with an exception.");
            }
        }

        public static void BuildRemoteSyncBoolean(
            AnimatorController controller,
            int layerIndex,
            string remotePrefix,
            Dictionary<int, int> assignedNumbers,
            string localTreeName,
            string remoteTreeName,
            IReadOnlyList<string> booleanParameters,
            bool removeDriversFromRemote,
            bool addDriverForLocalSyncState,
            bool packIntoStateMachine,
            bool matchTransitionTimes
        )
        {
            if (controller == null)
            {
                Debug.LogError(LogPrefix + "BuildRemoteSyncBoolean aborted: controller is null.");
                return;
            }
            if (layerIndex < 0 || layerIndex >= controller.layers.Length)
            {
                Debug.LogError(LogPrefix + "BuildRemoteSyncBoolean aborted: layer index is out of range.");
                return;
            }

            var root = controller.layers[layerIndex].stateMachine;
            if (root == null)
            {
                Debug.LogError(LogPrefix + "BuildRemoteSyncBoolean aborted: selected layer has no state machine.");
                return;
            }

            try
            {
                int localId = FindStateIdByName(root, localTreeName);
                int remoteId = FindStateIdByName(root, remoteTreeName);
                int defaultId = root.defaultState != null ? root.defaultState.GetInstanceID() : 0;

                float minLocalX = float.PositiveInfinity;
                Traverse(root, (parent, child) =>
                {
                    minLocalX = Mathf.Min(minLocalX, child.position.x);
                });

                var clonedByNumber = new Dictionary<int, AnimatorState>();
                var cloneParents = new Dictionary<int, AnimatorStateMachine>();
                var originalsByNumber = new Dictionary<int, AnimatorState>();
                float maxCloneX = float.NegativeInfinity;
                Traverse(root, (parent, child) =>
                {
                    var state = child.state;
                    if (state == null) return;

                    int stateId = state.GetInstanceID();
                    if (!assignedNumbers.TryGetValue(stateId, out int number) || number < 0) return;
                    if (stateId == defaultId || stateId == localId || stateId == remoteId) return;

                    string baseName = string.IsNullOrEmpty(remotePrefix)
                        ? state.name
                        : $"{remotePrefix}{state.name}";
                    string newName = MakeUniqueStateName(parent, baseName);

                    var clone = JaxTools.StateSync.Utility.AnimatorTools.CloneState(
                        state,
                        parent,
                        newName,
                        removeDriversFromRemote
                    );
                    if (clone == null) return;

                    Vector3 pos = child.position;
                    pos.x = -pos.x;
                    UpdateChildPosition(parent, clone, pos);
                    maxCloneX = Mathf.Max(maxCloneX, pos.x);

                    // Ensure cloned states start disconnected.
                    clone.transitions = System.Array.Empty<AnimatorStateTransition>();

                    clonedByNumber[number] = clone;
                    originalsByNumber[number] = state;
                    cloneParents[clone.GetInstanceID()] = parent;
                });

                if (assignedNumbers == null || assignedNumbers.Count == 0)
                    Debug.LogWarning(LogPrefix + "No assigned numbers found; no states will be cloned.");
                if (clonedByNumber.Count == 0)
                    Debug.LogWarning(LogPrefix + "No clone states created. Check assigned numbers and exclusions.");

                const float MinCloneGap = 50f;
                if (clonedByNumber.Count > 0 && minLocalX < float.PositiveInfinity && maxCloneX > minLocalX - MinCloneGap)
                {
                    float shift = maxCloneX - (minLocalX - MinCloneGap);
                    foreach (var clonePair in clonedByNumber)
                    {
                        var clone = clonePair.Value;
                        if (clone == null) continue;
                        var parent = cloneParents.TryGetValue(clone.GetInstanceID(), out var p) ? p : root;
                        if (!TryGetStatePosition(parent, clone.GetInstanceID(), out var pos))
                            continue;
                        pos.x -= shift;
                        UpdateChildPosition(parent, clone, pos);
                    }
                }

                if (booleanParameters != null)
                {
                    foreach (var param in booleanParameters)
                        EnsureBoolParameter(controller, param);
                }

                var targetMachine = root;
                if (packIntoStateMachine)
                    targetMachine = PackRemoteStates(root, remoteId, clonedByNumber, cloneParents, "Remote Sync");

                ConnectClonedStatesBool(clonedByNumber, originalsByNumber, booleanParameters, matchTransitionTimes);
                ConnectRemoteTreeBool(targetMachine, remoteId, clonedByNumber, booleanParameters);

                if (addDriverForLocalSyncState)
                    AddDriversToOriginalStatesBool(controller, layerIndex, root, assignedNumbers, booleanParameters);

                EditorUtility.SetDirty(controller);
                AssetDatabase.SaveAssets();
                Debug.Log(LogPrefix + "BuildRemoteSyncBoolean completed.");
            }
            catch (System.Exception ex)
            {
                Debug.LogException(ex);
                Debug.LogError(LogPrefix + "BuildRemoteSyncBoolean failed with an exception.");
            }
        }

        private static void Traverse(AnimatorStateMachine sm, System.Action<AnimatorStateMachine, ChildAnimatorState> onState)
        {
            foreach (var cs in sm.states)
                onState?.Invoke(sm, cs);

            foreach (var csm in sm.stateMachines)
            {
                if (csm.stateMachine == null) continue;
                Traverse(csm.stateMachine, onState);
            }
        }

        private static int FindStateIdByName(AnimatorStateMachine root, string name)
        {
            if (root == null || string.IsNullOrWhiteSpace(name)) return 0;
            int foundId = 0;
            Traverse(root, (parent, child) =>
            {
                if (child.state == null) return;
                if (foundId != 0) return;
                if (string.Equals(child.state.name, name, System.StringComparison.Ordinal))
                    foundId = child.state.GetInstanceID();
            });
            return foundId;
        }

        private static string MakeUniqueStateName(AnimatorStateMachine parent, string baseName)
        {
            if (parent == null) return baseName;
            string name = baseName;
            int suffix = 1;
            while (HasStateName(parent, name))
            {
                name = $"{baseName}_{suffix}";
                suffix++;
            }
            return name;
        }

        private static bool HasStateName(AnimatorStateMachine parent, string name)
        {
            foreach (var cs in parent.states)
            {
                if (cs.state != null && cs.state.name == name)
                    return true;
            }
            return false;
        }

        private static void UpdateChildPosition(AnimatorStateMachine parent, AnimatorState state, Vector3 position)
        {
            if (parent == null || state == null) return;

            var states = parent.states;
            for (int i = 0; i < states.Length; i++)
            {
                if (states[i].state == state)
                {
                    states[i].position = position;
                    parent.states = states;
                    return;
                }
            }
        }

        private static void AddDriversToOriginalStates(
            AnimatorController controller,
            int layerIndex,
            AnimatorStateMachine root,
            Dictionary<int, int> assignedNumbers,
            string remoteParameterName
        )
        {
            if (controller == null || root == null) return;
            if (assignedNumbers == null || assignedNumbers.Count == 0) return;
            if (string.IsNullOrWhiteSpace(remoteParameterName)) return;

            var states = JaxTools.StateSync.Utility.AnimatorTools.GetStates(root);
            foreach (var entry in states)
            {
                if (entry.State == null) continue;
                int stateId = entry.State.GetInstanceID();
                if (!assignedNumbers.TryGetValue(stateId, out int number)) continue;

                DriverUtility.AddDriver(
                    controller,
                    layerIndex,
                    entry.Path,
                    DriverUtility.TYPE_SET,
                    DriverUtility.DestinationType.Int,
                    remoteParameterName,
                    number,
                    localOnly: false
                );
            }
        }

        private static void AddDriversToOriginalStatesBool(
            AnimatorController controller,
            int layerIndex,
            AnimatorStateMachine root,
            Dictionary<int, int> assignedNumbers,
            IReadOnlyList<string> booleanParameters
        )
        {
            if (controller == null || root == null) return;
            if (assignedNumbers == null || assignedNumbers.Count == 0) return;
            if (booleanParameters == null || booleanParameters.Count == 0) return;

            var states = JaxTools.StateSync.Utility.AnimatorTools.GetStates(root);
            foreach (var entry in states)
            {
                if (entry.State == null) continue;
                int stateId = entry.State.GetInstanceID();
                if (!assignedNumbers.TryGetValue(stateId, out int number)) continue;

                var bits = BinaryTools.ToBooleanArray(number, booleanParameters.Count);
                DriverUtility.AddBooleanDriverEntries(
                    controller,
                    layerIndex,
                    entry.Path,
                    booleanParameters,
                    bits,
                    localOnly: false
                );
            }
        }

        private static void EnsureIntParameter(AnimatorController controller, string name)
        {
            if (controller == null || string.IsNullOrWhiteSpace(name)) return;
            foreach (var p in controller.parameters)
            {
                if (p != null && p.name == name) return;
            }

            controller.AddParameter(new AnimatorControllerParameter
            {
                name = name,
                type = AnimatorControllerParameterType.Int
            });
        }

        private static void EnsureBoolParameter(AnimatorController controller, string name)
        {
            if (controller == null || string.IsNullOrWhiteSpace(name)) return;
            foreach (var p in controller.parameters)
            {
                if (p != null && p.name == name) return;
            }

            controller.AddParameter(new AnimatorControllerParameter
            {
                name = name,
                type = AnimatorControllerParameterType.Bool
            });
        }

        private static void ConnectClonedStates(
            Dictionary<int, AnimatorState> clones,
            Dictionary<int, AnimatorState> originals,
            string parameterName,
            bool matchTransitionTimes
        )
        {
            if (clones == null || clones.Count == 0) return;
            if (string.IsNullOrWhiteSpace(parameterName)) return;

            foreach (var fromPair in clones)
            {
                var fromState = fromPair.Value;
                if (fromState == null) continue;

                AnimatorState originalFrom = null;
                originals?.TryGetValue(fromPair.Key, out originalFrom);

                foreach (var toPair in clones)
                {
                    if (fromPair.Key == toPair.Key) continue;
                    var toState = toPair.Value;
                    if (toState == null) continue;

                    var transition = fromState.AddTransition(toState);
                    transition.hasExitTime = false;
                    transition.hasFixedDuration = true;
                    transition.duration = 0f;
                    transition.AddCondition(AnimatorConditionMode.Equals, toPair.Key, parameterName);

                    if (matchTransitionTimes && originalFrom != null &&
                        originals != null && originals.TryGetValue(toPair.Key, out var originalTo) &&
                        originalTo != null)
                    {
                        var originalTransition = FindTransition(originalFrom, originalTo);
                        if (originalTransition != null)
                            CopyTransitionTiming(originalTransition, transition);
                    }
                }
            }
        }

        private static void ConnectClonedStatesBool(
            Dictionary<int, AnimatorState> clones,
            Dictionary<int, AnimatorState> originals,
            IReadOnlyList<string> booleanParameters,
            bool matchTransitionTimes
        )
        {
            if (clones == null || clones.Count == 0) return;
            if (booleanParameters == null) return;

            foreach (var fromPair in clones)
            {
                var fromState = fromPair.Value;
                if (fromState == null) continue;

                AnimatorState originalFrom = null;
                originals?.TryGetValue(fromPair.Key, out originalFrom);

                foreach (var toPair in clones)
                {
                    if (fromPair.Key == toPair.Key) continue;
                    var toState = toPair.Value;
                    if (toState == null) continue;

                    var transition = fromState.AddTransition(toState);
                    transition.hasExitTime = false;
                    transition.hasFixedDuration = true;
                    transition.duration = 0f;

                    AddBooleanConditions(transition, toPair.Key, booleanParameters);

                    if (matchTransitionTimes && originalFrom != null &&
                        originals != null && originals.TryGetValue(toPair.Key, out var originalTo) &&
                        originalTo != null)
                    {
                        var originalTransition = FindTransition(originalFrom, originalTo);
                        if (originalTransition != null)
                            CopyTransitionTiming(originalTransition, transition);
                    }
                }
            }
        }

        private static AnimatorStateTransition FindTransition(AnimatorState from, AnimatorState to)
        {
            if (from == null || to == null) return null;
            foreach (var transition in from.transitions)
            {
                if (transition != null && transition.destinationState == to)
                    return transition;
            }
            return null;
        }

        private static void CopyTransitionTiming(AnimatorStateTransition source, AnimatorStateTransition target)
        {
            if (source == null || target == null) return;
            target.hasExitTime = source.hasExitTime;
            target.hasFixedDuration = source.hasFixedDuration;
            target.duration = source.duration;
            target.exitTime = source.exitTime;
            target.offset = source.offset;
        }

        private static void ConnectRemoteTree(
            AnimatorStateMachine root,
            int remoteStateId,
            Dictionary<int, AnimatorState> clones,
            string parameterName
        )
        {
            if (root == null || remoteStateId == 0) return;
            if (clones == null || clones.Count == 0) return;
            if (string.IsNullOrWhiteSpace(parameterName)) return;

            var remoteState = FindStateById(root, remoteStateId);
            if (remoteState == null) return;

            foreach (var pair in clones)
            {
                if (pair.Value == null) continue;
                var transition = remoteState.AddTransition(pair.Value);
                transition.hasExitTime = false;
                transition.hasFixedDuration = true;
                transition.duration = 0f;
                transition.AddCondition(AnimatorConditionMode.Equals, pair.Key, parameterName);
            }
        }

        private static void ConnectRemoteTreeBool(
            AnimatorStateMachine root,
            int remoteStateId,
            Dictionary<int, AnimatorState> clones,
            IReadOnlyList<string> booleanParameters
        )
        {
            if (root == null || remoteStateId == 0) return;
            if (clones == null || clones.Count == 0) return;
            if (booleanParameters == null) return;

            var remoteState = FindStateById(root, remoteStateId);
            if (remoteState == null) return;

            foreach (var pair in clones)
            {
                if (pair.Value == null) continue;
                var transition = remoteState.AddTransition(pair.Value);
                transition.hasExitTime = false;
                transition.hasFixedDuration = true;
                transition.duration = 0f;
                AddBooleanConditions(transition, pair.Key, booleanParameters);
            }
        }

        private static void AddBooleanConditions(
            AnimatorStateTransition transition,
            int value,
            IReadOnlyList<string> booleanParameters
        )
        {
            if (transition == null || booleanParameters == null) return;
            if (booleanParameters.Count == 0) return;

            var bits = BinaryTools.ToBooleanArray(value, booleanParameters.Count);
            for (int i = 0; i < booleanParameters.Count; i++)
            {
                string param = booleanParameters[i];
                if (string.IsNullOrWhiteSpace(param)) continue;

                transition.AddCondition(
                    bits[i] ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot,
                    0f,
                    param
                );
            }
        }


        private static AnimatorState FindStateById(AnimatorStateMachine root, int stateId)
        {
            AnimatorState found = null;
            Traverse(root, (parent, child) =>
            {
                if (found != null) return;
                if (child.state != null && child.state.GetInstanceID() == stateId)
                    found = child.state;
            });
            return found;
        }

        private static AnimatorStateMachine PackRemoteStates(
            AnimatorStateMachine root,
            int remoteStateId,
            Dictionary<int, AnimatorState> clones,
            Dictionary<int, AnimatorStateMachine> cloneParents,
            string packName
        )
        {
            if (root == null) return root;
            var packed = root.AddStateMachine(packName, new Vector3(-250f, 0f));

            if (remoteStateId != 0)
            {
                if (TryFindStateWithParent(root, remoteStateId, out var parent, out var pos))
                    MoveState(parent, packed, remoteStateId, pos);
            }

            foreach (var pair in clones)
            {
                var state = pair.Value;
                if (state == null) continue;
                var parent = cloneParents.TryGetValue(state.GetInstanceID(), out var p) ? p : root;
                if (!TryGetStatePosition(parent, state.GetInstanceID(), out var pos))
                    pos = Vector3.zero;
                MoveState(parent, packed, state.GetInstanceID(), pos);
            }

            if (remoteStateId != 0)
            {
                var remoteState = FindStateById(packed, remoteStateId);
                if (remoteState != null)
                    packed.defaultState = remoteState;
            }

            return packed;
        }

        private static bool TryFindStateWithParent(
            AnimatorStateMachine root,
            int stateId,
            out AnimatorStateMachine parent,
            out Vector3 position
        )
        {
            parent = null;
            position = Vector3.zero;
            return TryFindStateWithParentInternal(root, stateId, ref parent, ref position);
        }

        private static bool TryFindStateWithParentInternal(
            AnimatorStateMachine root,
            int stateId,
            ref AnimatorStateMachine parent,
            ref Vector3 position
        )
        {
            if (root == null) return false;

            foreach (var child in root.states)
            {
                if (child.state != null && child.state.GetInstanceID() == stateId)
                {
                    parent = root;
                    position = child.position;
                    return true;
                }
            }

            foreach (var csm in root.stateMachines)
            {
                if (csm.stateMachine == null) continue;
                if (TryFindStateWithParentInternal(csm.stateMachine, stateId, ref parent, ref position))
                    return true;
            }

            return false;
        }

        private static bool TryGetStatePosition(AnimatorStateMachine parent, int stateId, out Vector3 position)
        {
            position = Vector3.zero;
            if (parent == null) return false;
            foreach (var child in parent.states)
            {
                if (child.state != null && child.state.GetInstanceID() == stateId)
                {
                    position = child.position;
                    return true;
                }
            }
            return false;
        }

        private static void MoveState(
            AnimatorStateMachine from,
            AnimatorStateMachine to,
            int stateId,
            Vector3 position
        )
        {
            if (from == null || to == null) return;
            AnimatorState state = null;

            var fromStates = new List<ChildAnimatorState>(from.states);
            for (int i = fromStates.Count - 1; i >= 0; i--)
            {
                if (fromStates[i].state != null && fromStates[i].state.GetInstanceID() == stateId)
                {
                    state = fromStates[i].state;
                    fromStates.RemoveAt(i);
                    break;
                }
            }

            if (state == null) return;
            from.states = fromStates.ToArray();

            var toStates = new List<ChildAnimatorState>(to.states)
            {
                new ChildAnimatorState { state = state, position = position }
            };
            to.states = toStates.ToArray();
        }

        private static void AddTransitionIntoPacked(
            AnimatorStateMachine root,
            AnimatorStateMachine packed,
            string isLocalParameterName
        )
        {
            if (root == null || packed == null) return;
            if (string.IsNullOrWhiteSpace(isLocalParameterName)) return;

            var transition = root.AddStateMachineTransition(packed);
            transition.AddCondition(AnimatorConditionMode.IfNot, 0f, isLocalParameterName);
        }
    }
}
