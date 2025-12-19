using System;
using System.Collections.Generic;
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

        public static void CleanState(AnimatorState state)
        {
            if (state == null || state.behaviours == null) return;

            var driverType = FindTypeByName("VRC.SDK3.Avatars.Components.VRCAvatarParameterDriver");
            if (driverType == null) return;

            foreach (var behaviour in state.behaviours)
            {
                if (behaviour == null) continue;
                if (behaviour.GetType() != driverType) continue;

                UnityEngine.Object.DestroyImmediate(behaviour, true);
            }
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
