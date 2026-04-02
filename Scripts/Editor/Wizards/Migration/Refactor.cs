using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using Object = UnityEngine.Object;

namespace RoyTheunissen.AudioSyntax
{
    /// <summary>
    /// Base class for one of several refactors that are part of a migration that the Migration Wizard can perform.
    /// For example renaming the namespaces from FMOD-Syntax to Audio-Syntax.
    /// </summary>
    public abstract class Refactor
    {
        private static readonly string[] AudioSyntaxBasePaths =
        {
            "FMOD-Syntax/",
            "FMOD-Syntax/",
            "com.roytheunissen.fmod-syntax/",
            "com.roytheunissen.audio-syntax/",
        };

        [Flags]
        protected enum FileScopes
        {
            GeneratedCode = 1 << 0,
            Everything = ~0,
        }
        
        private bool isNecessary;
        public bool IsNecessary => isNecessary;

        private Migration.IssueUrgencies urgency;
        public Migration.IssueUrgencies Urgency => urgency;

        protected abstract string NotNecessaryDisplayText { get; }
        protected abstract string IsNecessaryDisplayText { get; }
        
        protected abstract string ConfirmationDialogueText { get; }

        public delegate void RefactorPerformedHandler(Refactor refactor);
        public static event RefactorPerformedHandler RefactorPerformedEvent;

        public void CheckIfNecessary()
        {
            isNecessary = CheckIfNecessaryInternal(out urgency);
        }

        protected abstract bool CheckIfNecessaryInternal(out Migration.IssueUrgencies urgency);
        
        private void PerformInternal(bool dispatchEvent)
        {
            OnPerform();

            if (dispatchEvent)
                RefactorPerformedEvent?.Invoke(this);
        }

        private void Perform()
        {
            PerformInternal(true);
        }

        public void PerformAsPartOfBatch()
        {
            // The intention was to not dispatch an event for every individual refactor when doing Auto Fix All,
            // but for robustness, perhaps it *is* best to re-evaluate all the refactors and see if they are still
            // necessary. Perhaps certain refactors will cause other refactors to not be necessary.
            PerformInternal(true);
        }

        protected abstract void OnPerform();

        public void OnGUI()
        {
            if (!isNecessary)
            {
                DrawingUtilities.HelpBoxAffirmative(NotNecessaryDisplayText);
                return;
            }

            EditorGUILayout.HelpBox(
                IsNecessaryDisplayText,
                Urgency == Migration.IssueUrgencies.Required ? MessageType.Error : MessageType.Warning);
            bool shouldPerform = GUILayout.Button("Fix Automatically");
            if (shouldPerform)
            {
                bool confirmed = EditorUtility.DisplayDialog("Automatic Refactor Confirmation",
                    ConfirmationDialogueText +
                    "\n\nWe recommend that you commit your changes to " +
                    $"version control first so that you don't lose any work.",
                    "Yes, I have saved my work.", "No");
                    
                if (confirmed)
                    Perform();
            }
        }

        // NOTE: This is also used in FmodCodeGenerator to ensure that event renames are not performed inside the
        // package itself. Generally speaking it is useful to exclude files to automatically refactor.
        public static bool IsProjectRelativePathInsideThisPackage(string projectRelativePath)
        {
            if (projectRelativePath.StartsWith("Assets/"))
                projectRelativePath = projectRelativePath.RemoveAssetsPrefix();
            else if (projectRelativePath.StartsWith("Packages/"))
                projectRelativePath = projectRelativePath.RemovePrefix("Packages/");

            // WE are allowed to reference it, for example in this very script :V
            for (int j = 0; j < AudioSyntaxBasePaths.Length; j++)
            {
                if (projectRelativePath.StartsWith(AudioSyntaxBasePaths[j]))
                    return true;
            }

            return false;
        }

        private static bool IsAssetInsideThisPackage(Object asset)
        {
            string assetPath = AssetDatabase.GetAssetPath(asset);

            return IsProjectRelativePathInsideThisPackage(assetPath);
        }
        
        private bool IsScriptGeneratedCode(MonoScript monoScript)
        {
            // The new convention dictates that generated files should be called .g and then whatever file extension
            // they have, for example: .g.cs
            if (monoScript.name.EndsWith(".g"))
                return true;
            
            // We didn't use to use that convention, but we did consistently feature this text in our generated files.
            return monoScript.text.Contains("/// GENERATED: ");
        }
        
        private bool IsScriptInsideScope(MonoScript monoScript, FileScopes scope)
        {
            // Do not ever refactor scripts that are part of this package itself.
            if (IsAssetInsideThisPackage(monoScript))
                return false;

            if (!scope.HasFlag(FileScopes.GeneratedCode) && IsScriptGeneratedCode(monoScript))
                return false;

            return true;
        }

        private bool IsContainedInScripts(string text, FileScopes scope)
        {
            MonoScript[] monoScripts = AssetLoading.GetAllAssetsOfType<MonoScript>();
            for (int i = 0; i < monoScripts.Length; i++)
            {
                if (!IsScriptInsideScope(monoScripts[i], scope))
                    continue;

                string scriptText = monoScripts[i].text;
                if (scriptText.Contains(text))
                {
                    return true;
                }
            }

            return false;
        }
        
        protected bool IsReplacementInScriptsNecessary(string from, string to, FileScopes scope)
        {
            return IsContainedInScripts(from, scope);
        }

        protected bool AreReplacementsNecessary(Dictionary<string, string> replacements, FileScopes scope)
        {
            foreach (KeyValuePair<string, string> oldTextNewTextPair in replacements)
            {
                if (IsContainedInScripts(oldTextNewTextPair.Key, scope))
                    return true;
            }

            return false;
        }

        protected void ReplaceInScripts(string oldText, string newText, FileScopes scope, bool partOfBatch = false)
        {
            MonoScript[] monoScripts = AssetLoading.GetAllAssetsOfType<MonoScript>();
            for (int i = 0; i < monoScripts.Length; i++)
            {
                if (!IsScriptInsideScope(monoScripts[i], scope))
                    continue;

                string scriptText = monoScripts[i].text;
                bool hasIncorrectUsingInFile = scriptText.Contains(oldText);
                if (!hasIncorrectUsingInFile)
                    continue;

                scriptText = scriptText.Replace(oldText, newText);
                monoScripts[i].SetText(scriptText);
            }

            if (!partOfBatch)
                AssetDatabase.Refresh();
        }

        protected void ReplaceInScripts(Dictionary<string, string> replacements, FileScopes scope)
        {
            foreach (KeyValuePair<string, string> oldTextNewTextPair in replacements)
            {
                ReplaceInScripts(oldTextNewTextPair.Key, oldTextNewTextPair.Value, scope, true);
            }

            AssetDatabase.Refresh();
        }

        protected string GetDisplayTextForReplacements(Dictionary<string, string> replacements)
        {
            string text = string.Empty;
            int count = replacements.Count;
            int index = 0;
            foreach (KeyValuePair<string,string> oldTextNewTextPair in replacements)
            {
                text += oldTextNewTextPair.Key + " \u2192 " + oldTextNewTextPair.Value;

                if (index < count - 1)
                    text += "\n";
                
                index++;
            }

            return text;
        }
        
        protected static bool IsScriptReferenceReplacementNecessary(Dictionary<string, string> guidReplacements)
        {
            // NOTE: This only replaces them in Scriptable Objects because thankfully that is the only use case that
            // we have. You could do something similar for MonoBehaviours but then you would have to check
            // prefabs and scenes.
            
            ScriptableObject[] scriptableObjects = AssetLoading.GetAllAssetsOfType<ScriptableObject>();
            for (int scriptableObjectIndex = 0; scriptableObjectIndex < scriptableObjects.Length; scriptableObjectIndex++)
            {
                ScriptableObject scriptableObject = scriptableObjects[scriptableObjectIndex];
                
                if (IsAssetInsideThisPackage(scriptableObject))
                    continue;

                // Determine the script of this Scriptable Object
                MonoScript script = MonoScript.FromScriptableObject(scriptableObject);
                string scriptPath = AssetDatabase.GetAssetPath(script);
                string scriptGuid = AssetDatabase.AssetPathToGUID(scriptPath);

                // Check if this asset uses a script that is supposed to be replaced.
                if (guidReplacements.ContainsKey(scriptGuid))
                    return true;
            }

            return false;
        }

        protected static bool ReplaceScriptReferences(Dictionary<string, string> guidReplacements, bool partOfBatch = false)
        {
            // NOTE: This only replaces them in Scriptable Objects because thankfully that is the only use case that
            // we have. You could do something similar for MonoBehaviours but then you would have to check
            // prefabs and scenes.

            bool didAnyReplacements = false;
            
            ScriptableObject[] scriptableObjects = AssetLoading.GetAllAssetsOfType<ScriptableObject>();
            const string scriptReferenceFormat =
                "m_Script: {fileID: 11500000, guid: #guid#, type: 3}";
            for (int scriptableObjectIndex = 0; scriptableObjectIndex < scriptableObjects.Length; scriptableObjectIndex++)
            {
                ScriptableObject scriptableObject = scriptableObjects[scriptableObjectIndex];
                
                if (IsAssetInsideThisPackage(scriptableObject))
                    continue;

                // Determine the script of this Scriptable Object
                MonoScript script = MonoScript.FromScriptableObject(scriptableObject);
                string scriptPath = AssetDatabase.GetAssetPath(script);
                string scriptGuid = AssetDatabase.AssetPathToGUID(scriptPath);

                // Check if this asset uses a script that is supposed to be replaced.
                if (!guidReplacements.TryGetValue(scriptGuid, out string replacementGuid))
                    continue;
                
                string scriptableObjectPath = AssetDatabase.GetAssetPath(scriptableObject).GetAbsolutePath();

                // Replace the script reference in the corresponding .asset file
                string[] lines = File.ReadAllLines(scriptableObjectPath);
                string outdatedReferenceText = scriptReferenceFormat.Replace(
                    "#guid#", scriptGuid, StringComparison.OrdinalIgnoreCase);
                string updatedReferenceText = scriptReferenceFormat.Replace(
                    "#guid#", replacementGuid, StringComparison.OrdinalIgnoreCase);
                bool replacedAnyLines = false;
                for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++)
                {
                    if (lines[lineIndex].Contains(outdatedReferenceText))
                    {
                        lines[lineIndex] = lines[lineIndex].Replace(outdatedReferenceText, updatedReferenceText);
                        replacedAnyLines = true;
                    }
                }
                
                if (replacedAnyLines)
                {
                    File.WriteAllLines(scriptableObjectPath, lines);
                    didAnyReplacements = true;
                }
            }

            if (!partOfBatch && didAnyReplacements)
                AssetDatabase.Refresh();

            return didAnyReplacements;
        }
        
        protected static bool IsAssemblyDefinitionReferenceReplacementNecessary(
            Dictionary<string, string> guidReplacements, Dictionary<string, string> nameReplacements,
            Dictionary<string, string> resourceReplacements = null)
        {
            AssemblyDefinitionAsset[] assemblyDefinitionAssets = AssetLoading.GetAllAssetsOfType<AssemblyDefinitionAsset>();
            for (int asmdefIndex = 0; asmdefIndex < assemblyDefinitionAssets.Length; asmdefIndex++)
            {
                AssemblyDefinitionAsset assemblyDefinitionAsset = assemblyDefinitionAssets[asmdefIndex];
                
                if (IsAssetInsideThisPackage(assemblyDefinitionAsset))
                    continue;
                
                string assemblyDefinitionAssetPath = AssetDatabase.GetAssetPath(assemblyDefinitionAsset);
                string text = File.ReadAllText(assemblyDefinitionAssetPath);

                // First check if any specified GUID replacements are necessary.
                const string guidFormat = "GUID:{0}";
                foreach (KeyValuePair<string,string> guidReplacement in guidReplacements)
                {
                    string from = string.Format(guidFormat, guidReplacement.Key);
                    
                    if (text.Contains(from))
                        return true;
                }
                
                // Then check if any specified name replacements are necessary
                foreach (KeyValuePair<string,string> nameReplacement in nameReplacements)
                {
                    if (text.Contains(nameReplacement.Key))
                        return true;
                }
                
                // Then check if any specified resource replacements in the version defines are necessary.
                if (resourceReplacements != null)
                {
                    foreach (KeyValuePair<string,string> resourceReplacement in resourceReplacements)
                    {
                        if (text.Contains(resourceReplacement.Key))
                            return true;
                    }
                }
            }

            return false;
        }
        
        protected static bool ReplaceAssemblyDefinitionReferences(
            Dictionary<string, string> guidReplacements, Dictionary<string, string> nameReplacements,
            Dictionary<string, string> resourceReplacements = null,
            bool partOfBatch = false)
        {
            bool didModifyAnyFiles = false;
            
            AssemblyDefinitionAsset[] assemblyDefinitionAssets = AssetLoading.GetAllAssetsOfType<AssemblyDefinitionAsset>();
            for (int asmdefIndex = 0; asmdefIndex < assemblyDefinitionAssets.Length; asmdefIndex++)
            {
                AssemblyDefinitionAsset assemblyDefinitionAsset = assemblyDefinitionAssets[asmdefIndex];
                
                if (IsAssetInsideThisPackage(assemblyDefinitionAsset))
                    continue;

                bool didModifyText = false;
                
                string assemblyDefinitionAssetPath = AssetDatabase.GetAssetPath(assemblyDefinitionAsset);
                string text = File.ReadAllText(assemblyDefinitionAssetPath);

                // First perform any specified GUID replacements.
                const string guidFormat = "GUID:{0}";
                foreach (KeyValuePair<string,string> guidReplacement in guidReplacements)
                {
                    string from = string.Format(guidFormat, guidReplacement.Key);
                    
                    if (!text.Contains(from))
                        continue;

                    string to = string.Format(guidFormat, guidReplacement.Value);

                    text = text.Replace(from, to);

                    didModifyText = true;
                }
                
                // Then perform any specified name replacements.
                foreach (KeyValuePair<string,string> nameReplacement in nameReplacements)
                {
                    if (!text.Contains(nameReplacement.Key))
                        continue;

                    text = text.Replace(nameReplacement.Key, nameReplacement.Value);
                    
                    didModifyText = true;
                }
                
                // Then perform any specified resource replacements in the version defines.
                if (resourceReplacements != null)
                {
                    foreach (KeyValuePair<string,string> resourceReplacement in resourceReplacements)
                    {
                        if (!text.Contains(resourceReplacement.Key))
                            continue;

                        text = text.Replace(resourceReplacement.Key, resourceReplacement.Value);
                    
                        didModifyText = true;
                    }
                }
                
                if (didModifyText)
                {
                    File.WriteAllText(assemblyDefinitionAssetPath, text);
                    didModifyAnyFiles = true;
                }
            }

            if (!partOfBatch && didModifyAnyFiles)
                AssetDatabase.Refresh();

            return didModifyAnyFiles;
        }
    }
}
