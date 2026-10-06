// MIT License
// Copyright (c) 2026 nemurigi 

using System;
using System.IO;
using System.Reflection;
using UdonSharp;
using UdonSharp.Compiler;
using UdonSharpEditor;
using UnityEditor;
using UnityEngine;

/// <summary>
/// UdonSharpBehaviour のスクリプトがインポートされた際、
/// 対応する UdonSharpProgramAsset が存在しない場合に自動生成する。
/// </summary>
public class UdonSharpProgramAssetAutoGenerator : AssetPostprocessor
{
    /// <summary>
    /// UdonSharpSettings.autoCompileOnModify を reflection で取得する。
    /// </summary>
    private static bool GetAutoCompileOnModify()
    {
        try
        {
            Assembly udonSharpEditorAssembly = typeof(UdonSharpEditorUtility).Assembly;
            Type settingsType = udonSharpEditorAssembly.GetType("UdonSharpEditor.UdonSharpSettings");
            if (settingsType == null)
                return false;

            MethodInfo getSettingsMethod = settingsType.GetMethod("GetSettings", BindingFlags.Public | BindingFlags.Static);
            if (getSettingsMethod == null)
                return false;

            object settingsInstance = getSettingsMethod.Invoke(null, null);
            if (settingsInstance == null)
                return false;

            FieldInfo autoCompileField = settingsType.GetField("autoCompileOnModify", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (autoCompileField == null)
                return false;

            object fieldValue = autoCompileField.GetValue(settingsInstance);
            return fieldValue is bool enabled && enabled;
        }
        catch (Exception ex)
        {
            Debug.LogError($"UdonSharpProgramAssetAutoGenerator: Failed to read UdonSharpSettings.autoCompileOnModify via reflection.\n{ex}");
            return false;
        }
    }

    private static void OnPostprocessAllAssets(
        string[] importedAssets,
        string[] deletedAssets,
        string[] movedAssets,
        string[] movedFromAssetPaths,
        bool didDomainReload)
    {
        if (!didDomainReload)
            return;

        // 1.対象スクリプトの探索と UdonSharpProgramAsset の生成
        bool createdAnyProgramAsset = false;

        foreach (string importedAssetPath in importedAssets)
        {
            if (string.IsNullOrEmpty(importedAssetPath))
                continue;

            MonoScript script = AssetDatabase.LoadAssetAtPath<MonoScript>(importedAssetPath);
            if (script == null)
                continue;

                Type scriptClass = script.GetClass();
                // abstract クラス、または UdonSharpBehaviour を継承していないクラスはスキップ
                if (scriptClass == null || scriptClass.IsAbstract || !typeof(UdonSharpBehaviour).IsAssignableFrom(scriptClass))
                    continue;

                if (UdonSharpEditorUtility.GetUdonSharpProgramAsset(scriptClass) != null)
                    continue;

            string programAssetPath = Path.ChangeExtension(importedAssetPath, ".asset")?.Replace('\\', '/');
            if (string.IsNullOrEmpty(programAssetPath) || !programAssetPath.StartsWith("Assets/", StringComparison.Ordinal))
                continue;

            if (AssetDatabase.LoadMainAssetAtPath(programAssetPath) != null)
                continue;

            UdonSharpProgramAsset programAsset = ScriptableObject.CreateInstance<UdonSharpProgramAsset>();
            programAsset.sourceCsScript = script;

            try
            {
                AssetDatabase.CreateAsset(programAsset, programAssetPath);
                AssetDatabase.ImportAsset(programAssetPath, ImportAssetOptions.ForceSynchronousImport);

                if (AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(programAssetPath) == null)
                {
                    Debug.LogError($"UdonSharpProgramAssetAutoGenerator: Failed to create program asset at '{programAssetPath}' for '{importedAssetPath}'.");
                    continue;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"UdonSharpProgramAssetAutoGenerator: Exception while creating program asset at '{programAssetPath}' for '{importedAssetPath}'.\n{ex}");
                continue;
            }

            createdAnyProgramAsset = true;
        }

        // 2.生成があった場合のみ反映とコンパイル
        if (!createdAnyProgramAsset)
            return;

        AssetDatabase.Refresh();

        bool compileAfterGeneration = GetAutoCompileOnModify();
        if (!compileAfterGeneration)
            return;

        try
        {
            UdonSharpCompilerV1.CompileSync();
        }
        catch (Exception ex)
        {
            Debug.LogError($"UdonSharpProgramAssetAutoGenerator: Compile failed after generating program assets.\n{ex}");
        }
    }
}