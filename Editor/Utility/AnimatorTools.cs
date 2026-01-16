using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace JaxTools.StateSync.Utility
{
    public static class AnimatorTools
    {
        public struct StateEntry
        {
            public string Path;
            public AnimatorState State;
        }

        public static AnimatorState CloneState(AnimatorState source, AnimatorStateMachine target, string newName, bool cleanDriver)
        {
            if (source == null || target == null) return null;

            var cloned = target.AddState(string.IsNullOrEmpty(newName) ? source.name : newName, Vector3.zero);
            EditorUtility.CopySerialized(source, cloned);
            if (!string.IsNullOrEmpty(newName))
                cloned.name = newName;

            if (cleanDriver)
                CleanState(cloned);

            return cloned;
        }

        public static AnimatorController CloneAnimator(
            AnimatorController source,
            string prefix = "StateSynced_"
        )
        {
            if (source == null)
            {
                Debug.LogError("[StateSync] CloneAnimator failed: source controller is null.");
                return null;
            }
            if (string.IsNullOrEmpty(prefix)) prefix = "StateSynced_";

            string sourcePath = AssetDatabase.GetAssetPath(source);
            if (string.IsNullOrEmpty(sourcePath))
            {
                Debug.LogWarning("[StateSync] CloneAnimator: source has no asset path, cloning in-memory only.");
                return UnityEngine.Object.Instantiate(source);
            }

            string directory = Path.GetDirectoryName(sourcePath);
            if (string.IsNullOrEmpty(directory))
            {
                Debug.LogWarning("[StateSync] CloneAnimator: source path has no directory, cloning in-memory only.");
                return UnityEngine.Object.Instantiate(source);
            }

            string newName = $"{prefix}{source.name}";
            string newPath = Path.Combine(directory, $"{newName}.controller");
            newPath = AssetDatabase.GenerateUniqueAssetPath(newPath);

            try
            {
                if (!AssetDatabase.CopyAsset(sourcePath, newPath))
                {
                    Debug.LogError($"[StateSync] CloneAnimator failed to copy asset: {sourcePath} -> {newPath}");
                    return UnityEngine.Object.Instantiate(source);
                }

                AssetDatabase.ImportAsset(newPath);
                var clone = AssetDatabase.LoadAssetAtPath<AnimatorController>(newPath);
                if (clone != null && clone.name != newName)
                {
                    clone.name = newName;
                    EditorUtility.SetDirty(clone);
                    AssetDatabase.SaveAssets();
                }

                Debug.Log($"[StateSync] CloneAnimator created: {newPath}");
                return clone;
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                Debug.LogError("[StateSync] CloneAnimator encountered an exception; using in-memory clone.");
                return UnityEngine.Object.Instantiate(source);
            }
        }

        public static void CleanState(AnimatorState state)
        {
            if (state == null || state.behaviours == null) return;

            var driverType = FindTypeByName("VRC.SDK3.Avatars.Components.VRCAvatarParameterDriver");
            if (driverType == null) return;

            // Detach driver behaviours from the cloned state without destroying them.
            // Destroying shared behaviour instances can remove drivers from the original state too.
            var so = new SerializedObject(state);
            var behavioursProp = so.FindProperty("m_Behaviours");
            if (behavioursProp == null || !behavioursProp.isArray) return;

            for (int i = behavioursProp.arraySize - 1; i >= 0; i--)
            {
                var element = behavioursProp.GetArrayElementAtIndex(i);
                var obj = element?.objectReferenceValue;
                if (obj == null || obj.GetType() != driverType) continue;

                behavioursProp.DeleteArrayElementAtIndex(i);
                if (i < behavioursProp.arraySize &&
                    behavioursProp.GetArrayElementAtIndex(i).objectReferenceValue == null)
                    behavioursProp.DeleteArrayElementAtIndex(i);
            }

            so.ApplyModifiedPropertiesWithoutUndo();
        }

        public static List<StateEntry> GetStates(AnimatorStateMachine root)
        {
            var results = new List<StateEntry>();
            Traverse(root, "", (path, st) => results.Add(new StateEntry { Path = path, State = st }));
            return results;
        }

        public static List<AnimatorControllerParameter> GetParametersByType(
            AnimatorController controller,
            AnimatorControllerParameterType type
        )
        {
            var results = new List<AnimatorControllerParameter>();
            if (controller == null || controller.parameters == null) return results;

            foreach (var p in controller.parameters)
            {
                if (p != null && p.type == type)
                    results.Add(p);
            }

            return results;
        }

        public static int GetStatesHash(AnimatorStateMachine root)
        {
            var hash = new HashCode();
            Traverse(root, "", (path, st) =>
            {
                hash.Add(path);
                if (st != null) hash.Add(st.GetInstanceID());
            });
            return hash.ToHashCode();
        }

        private static void Traverse(AnimatorStateMachine sm, string parentPath, Action<string, AnimatorState> onState)
        {
            foreach (var cs in sm.states)
            {
                if (cs.state == null) continue;

                var path = string.IsNullOrEmpty(parentPath)
                    ? cs.state.name
                    : $"{parentPath}/{cs.state.name}";

                onState?.Invoke(path, cs.state);
            }

            foreach (var csm in sm.stateMachines)
            {
                if (csm.stateMachine == null) continue;

                var nextParent = string.IsNullOrEmpty(parentPath)
                    ? csm.stateMachine.name
                    : $"{parentPath}/{csm.stateMachine.name}";

                Traverse(csm.stateMachine, nextParent, onState);
            }
        }

        private static Type FindTypeByName(string fullName)
        {
            var t = Type.GetType(fullName);
            if (t != null) return t;

            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    t = asm.GetType(fullName, false);
                    if (t != null) return t;
                }
                catch { }
            }
            return null;
        }
    }
}
