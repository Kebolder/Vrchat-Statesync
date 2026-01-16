using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

public static class DriverUtility
{
    // Driver "type" numeric mapping 
    public const int TYPE_SET    = 0;
    public const int TYPE_ADD    = 1;
    public const int TYPE_RANDOM = 2;
    public const int TYPE_COPY   = 4;

    // Destination parameter type (inside the AnimatorController)
    public enum DestinationType
    {
        Int,
        Boolean,
        Float
    }

    /// <summary>
    /// Adds (or reuses) a VRCAvatarParameterDriver on the target state and appends one parameter entry.
    /// statePath supports nested state machines with "/" (e.g. "Locomotion/Idle").
    /// Rules:
    /// - valueMin/valueMax apply only when driverType == Random AND destination is Int/Float
    /// - chance applies only when driverType == Random AND destination is Boolean (chance to be true)
    /// - sourceParam is used only for Copy
    /// </summary>
    public static bool AddDriver(
        AnimatorController controller,
        int layerIndex,
        string statePath,
        int driverType,
        DestinationType destinationType,
        string destinationParam,
        float value = 0f,
        string sourceParam = null,
        float valueMin = 0f,
        float valueMax = 0f,
        float chance = 0f,
        bool localOnly = false,
        bool createNewBehaviour = false
    )
    {
        if (controller == null)
        {
            Debug.LogError("[DriverUtility] controller is null.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(statePath))
        {
            Debug.LogError("[DriverUtility] statePath is empty.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(destinationParam))
        {
            Debug.LogError("[DriverUtility] destinationParam is empty.");
            return false;
        }

        if (!IsSupportedDriverType(driverType))
        {
            Debug.LogError($"[DriverUtility] Unsupported driverType: {driverType}");
            return false;
        }

        if (layerIndex < 0 || layerIndex >= controller.layers.Length)
        {
            Debug.LogError($"[DriverUtility] layerIndex out of range: {layerIndex}");
            return false;
        }

        var state = FindStateByPath(controller.layers[layerIndex].stateMachine, statePath);
        if (state == null)
        {
            Debug.LogError($"[DriverUtility] State not found: '{statePath}' on layer '{controller.layers[layerIndex].name}'.");
            return false;
        }

        EnsureControllerParameter(controller, destinationParam, destinationType);

        var driverTypeObj = FindTypeByName("VRC.SDK3.Avatars.Components.VRCAvatarParameterDriver");
        if (driverTypeObj == null)
        {
            Debug.LogError("[DriverUtility] VRCAvatarParameterDriver type not found (VRChat SDK missing?).");
            return false;
        }

        var driver = GetOrAddBehaviour(state, driverTypeObj, createNewBehaviour);
        if (driver == null)
        {
            Debug.LogError("[DriverUtility] Failed to get/add VRCAvatarParameterDriver on state.");
            return false;
        }

        if (HasMatchingDriverEntry(
            driver,
            driverType,
            destinationType,
            destinationParam,
            value,
            sourceParam,
            valueMin,
            valueMax,
            chance,
            localOnly
        ))
        {
            return true;
        }

        AppendDriverEntry(
            driver,
            driverType,
            destinationType,
            destinationParam,
            value,
            sourceParam,
            valueMin,
            valueMax,
            chance,
            localOnly
        );

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        return true;
    }

    /// <summary>
    /// Adds (or reuses) a VRCAvatarParameterDriver on the target state and appends
    /// multiple boolean entries in the given order. values are applied in the same order
    /// as destinationParams (index 0 is the first checkbox in the UI).
    /// </summary>
    public static bool AddBooleanDriverEntries(
        AnimatorController controller,
        int layerIndex,
        string statePath,
        IReadOnlyList<string> destinationParams,
        IReadOnlyList<bool> values,
        bool localOnly = false,
        bool createNewBehaviour = false
    )
    {
        if (controller == null)
        {
            Debug.LogError("[DriverUtility] controller is null.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(statePath))
        {
            Debug.LogError("[DriverUtility] statePath is empty.");
            return false;
        }

        if (destinationParams == null || destinationParams.Count == 0)
        {
            Debug.LogError("[DriverUtility] destinationParams is empty.");
            return false;
        }

        if (values == null || values.Count == 0)
        {
            Debug.LogError("[DriverUtility] values is empty.");
            return false;
        }

        if (destinationParams.Count != values.Count)
        {
            Debug.LogError("[DriverUtility] destinationParams and values length mismatch.");
            return false;
        }

        if (layerIndex < 0 || layerIndex >= controller.layers.Length)
        {
            Debug.LogError($"[DriverUtility] layerIndex out of range: {layerIndex}");
            return false;
        }

        var state = FindStateByPath(controller.layers[layerIndex].stateMachine, statePath);
        if (state == null)
        {
            Debug.LogError($"[DriverUtility] State not found: '{statePath}' on layer '{controller.layers[layerIndex].name}'.");
            return false;
        }

        for (int i = 0; i < destinationParams.Count; i++)
            EnsureControllerParameter(controller, destinationParams[i], DestinationType.Boolean);

        var driverTypeObj = FindTypeByName("VRC.SDK3.Avatars.Components.VRCAvatarParameterDriver");
        if (driverTypeObj == null)
        {
            Debug.LogError("[DriverUtility] VRCAvatarParameterDriver type not found (VRChat SDK missing?).");
            return false;
        }

        var driver = GetOrAddBehaviour(state, driverTypeObj, createNewBehaviour);
        if (driver == null)
        {
            Debug.LogError("[DriverUtility] Failed to get/add VRCAvatarParameterDriver on state.");
            return false;
        }

        for (int i = 0; i < destinationParams.Count; i++)
        {
            string param = destinationParams[i];
            float value = values[i] ? 1f : 0f;

            if (HasMatchingDriverEntry(
                driver,
                TYPE_SET,
                DestinationType.Boolean,
                param,
                value,
                null,
                0f,
                0f,
                0f,
                localOnly
            ))
            {
                continue;
            }

            AppendDriverEntry(
                driver,
                TYPE_SET,
                DestinationType.Boolean,
                param,
                value,
                null,
                0f,
                0f,
                0f,
                localOnly
            );
        }

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        return true;
    }

    // ----------------------------
    // Internals
    // ----------------------------

    private static bool IsSupportedDriverType(int t)
        => t == TYPE_SET || t == TYPE_ADD || t == TYPE_RANDOM || t == TYPE_COPY;

    private static void EnsureControllerParameter(AnimatorController controller, string name, DestinationType destType)
    {
        var existing = controller.parameters.FirstOrDefault(p => p != null && p.name == name);
        if (existing != null) return;

        var p = new AnimatorControllerParameter { name = name };
        switch (destType)
        {
            case DestinationType.Int:     p.type = AnimatorControllerParameterType.Int; break;
            case DestinationType.Boolean: p.type = AnimatorControllerParameterType.Bool; break;
            case DestinationType.Float:   p.type = AnimatorControllerParameterType.Float; break;
        }
        controller.AddParameter(p);
    }

    private static AnimatorState FindStateByPath(AnimatorStateMachine root, string path)
    {
        if (root == null) return null;

        var parts = path.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return null;

        AnimatorStateMachine currentSM = root;

        for (int i = 0; i < parts.Length - 1; i++)
        {
            string smName = parts[i];
            var next = currentSM.stateMachines.FirstOrDefault(x => x.stateMachine != null && x.stateMachine.name == smName).stateMachine;
            if (next == null) return null;
            currentSM = next;
        }

        string stateName = parts[parts.Length - 1];
        var found = currentSM.states.FirstOrDefault(s => s.state != null && s.state.name == stateName).state;
        return found;
    }

    private static UnityEngine.Object GetOrAddBehaviour(AnimatorState state, Type behaviourType, bool createNew)
    {
        if (!createNew)
        {
            var existing = state.behaviours?.FirstOrDefault(b => b != null && b.GetType() == behaviourType);
            if (existing != null) return existing;
        }

        var typeOverload = typeof(AnimatorState).GetMethod("AddStateMachineBehaviour", new[] { typeof(Type) });
        if (typeOverload != null)
            return typeOverload.Invoke(state, new object[] { behaviourType }) as UnityEngine.Object;

        var generic = typeof(AnimatorState)
            .GetMethods()
            .FirstOrDefault(m => m.Name == "AddStateMachineBehaviour" && m.IsGenericMethodDefinition && m.GetParameters().Length == 0);

        if (generic != null)
            return generic.MakeGenericMethod(behaviourType).Invoke(state, null) as UnityEngine.Object;

        return null;
    }

    private static void AppendDriverEntry(
        UnityEngine.Object driver,
        int driverType,
        DestinationType destinationType,
        string destinationParam,
        float value,
        string sourceParam,
        float valueMin,
        float valueMax,
        float chance,
        bool localOnly
    )
    {
        var so = new SerializedObject(driver);
        so.Update();

        var parameters = so.FindProperty("parameters");
        if (parameters == null || !parameters.isArray)
        {
            Debug.LogError("[DriverUtility] 'parameters' array not found on driver (SDK mismatch?).");
            return;
        }

        int idx = parameters.arraySize;
        parameters.arraySize = idx + 1;

        var p = parameters.GetArrayElementAtIndex(idx);

        // Common fields
        SetIntRelative(p, "type", driverType);
        SetStringRelative(p, "name", destinationParam);
        SetStringRelative(p, "source", "");

        SetFloatRelative(p, "value", value);
        SetFloatRelative(p, "valueMin", 0f);
        SetFloatRelative(p, "valueMax", 0f);
        SetFloatRelative(p, "chance", 0f);

        SetBoolRelative(p, "convertRange", false);
        SetBoolRelative(p, "localOnly", localOnly);
        SetFloatRelative(p, "sourceMin", 0f);
        SetFloatRelative(p, "sourceMax", 0f);
        SetFloatRelative(p, "destMin", 0f);
        SetFloatRelative(p, "destMax", 0f);

        // Rules
        if (driverType == TYPE_COPY)
        {
            if (!string.IsNullOrWhiteSpace(sourceParam))
                SetStringRelative(p, "source", sourceParam);
        }

        if (driverType == TYPE_RANDOM)
        {
            if (destinationType == DestinationType.Boolean)
            {
                // chance = probability of true
                SetFloatRelative(p, "chance", chance);
            }
            else
            {
                // min/max for Int/Float random
                SetFloatRelative(p, "valueMin", valueMin);
                SetFloatRelative(p, "valueMax", valueMax);
            }
        }

        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static bool HasMatchingDriverEntry(
        UnityEngine.Object driver,
        int driverType,
        DestinationType destinationType,
        string destinationParam,
        float value,
        string sourceParam,
        float valueMin,
        float valueMax,
        float chance,
        bool localOnly
    )
    {
        var so = new SerializedObject(driver);
        so.Update();

        var parameters = so.FindProperty("parameters");
        if (parameters == null || !parameters.isArray) return false;

        for (int i = 0; i < parameters.arraySize; i++)
        {
            var p = parameters.GetArrayElementAtIndex(i);
            if (p == null) continue;

            int type = GetIntRelative(p, "type");
            string name = GetStringRelative(p, "name");
            string source = GetStringRelative(p, "source");
            float v = GetFloatRelative(p, "value");
            float vMin = GetFloatRelative(p, "valueMin");
            float vMax = GetFloatRelative(p, "valueMax");
            float ch = GetFloatRelative(p, "chance");
            bool local = GetBoolRelative(p, "localOnly");

            if (type != driverType) continue;
            if (!string.Equals(name, destinationParam, StringComparison.Ordinal)) continue;
            if (local != localOnly) continue;

            if (driverType == TYPE_COPY)
            {
                if (!string.Equals(source, sourceParam ?? "", StringComparison.Ordinal)) continue;
            }

            if (driverType == TYPE_RANDOM)
            {
                if (destinationType == DestinationType.Boolean)
                {
                    if (!Mathf.Approximately(ch, chance)) continue;
                }
                else
                {
                    if (!Mathf.Approximately(vMin, valueMin)) continue;
                    if (!Mathf.Approximately(vMax, valueMax)) continue;
                }
            }
            else
            {
                if (!Mathf.Approximately(v, value)) continue;
            }

            return true;
        }

        return false;
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

    private static void SetIntRelative(SerializedProperty parent, string rel, int v)
    {
        var p = parent.FindPropertyRelative(rel);
        if (p != null) p.intValue = v;
    }

    private static void SetFloatRelative(SerializedProperty parent, string rel, float v)
    {
        var p = parent.FindPropertyRelative(rel);
        if (p != null) p.floatValue = v;
    }

    private static void SetBoolRelative(SerializedProperty parent, string rel, bool v)
    {
        var p = parent.FindPropertyRelative(rel);
        if (p != null) p.boolValue = v;
    }

    private static void SetStringRelative(SerializedProperty parent, string rel, string v)
    {
        var p = parent.FindPropertyRelative(rel);
        if (p != null) p.stringValue = v ?? "";
    }

    private static int GetIntRelative(SerializedProperty parent, string rel)
    {
        var p = parent.FindPropertyRelative(rel);
        return p != null ? p.intValue : 0;
    }

    private static float GetFloatRelative(SerializedProperty parent, string rel)
    {
        var p = parent.FindPropertyRelative(rel);
        return p != null ? p.floatValue : 0f;
    }

    private static bool GetBoolRelative(SerializedProperty parent, string rel)
    {
        var p = parent.FindPropertyRelative(rel);
        return p != null && p.boolValue;
    }

    private static string GetStringRelative(SerializedProperty parent, string rel)
    {
        var p = parent.FindPropertyRelative(rel);
        return p != null ? p.stringValue : "";
    }
}
