using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace RoyTheunissen.AudioSyntax
{
    /// <summary>
    /// Utilities for adding/removing scripting define symbols. Unity provides a method called
    /// PlayerSettings.SetScriptingDefineSymbols to do exactly this, but it has the undocumented behaviour that if your
    /// project uses build profiles, and the current build profile overrides the project settings, then it sets the
    /// scripting define symbols only on that build profile, and not on the other build profiles nor in the
    /// Project Settings themselves. This is not the desired behaviour. We 
    /// </summary>
    public static class ScriptingDefineSymbolUtilities
    {
        public static bool UpdateScriptingDefineSymbol(string symbol, bool shouldExist)
        {
            bool wasSuccessful = true;
            
            // First update the player settings
            string playerSettingsPath = Path.Combine(
                Application.dataPath.GetParentDirectory(), "ProjectSettings", "ProjectSettings.asset").ToUnityPath();
            bool successfullyUpdatedPlayerSettings =
                UpdateScriptingDefineSymbolInFile(symbol, shouldExist, playerSettingsPath, false);
            if (!successfullyUpdatedPlayerSettings)
                wasSuccessful = false;
            
            // Then update the build profiles
            string[] buildProfileGuids = AssetDatabase.FindAssets("t:buildprofile");
            for (int i = 0; i < buildProfileGuids.Length; i++)
            {
                string buildProfilePath = AssetDatabase.GUIDToAssetPath(buildProfileGuids[i]).GetAbsolutePath();
                bool successfullyUpdatedBuildProfile =
                    UpdateScriptingDefineSymbolInFile(symbol, shouldExist, buildProfilePath, true);
                
                if (!successfullyUpdatedBuildProfile)
                    wasSuccessful = false;
            }

            return wasSuccessful;
        }

        private static bool UpdateScriptingDefineSymbolInFile(string symbol, bool shouldExist, string filePath, bool isBuildProfile)
        {
            if (!File.Exists(filePath))
            {
                Debug.LogError($"Tried to update Scripting Define Symbols in a way that is compatible with Build " +
                               $"Profiles but the file '{filePath}' could not be found.");
                return false;
            }

            string[] lines = File.ReadAllLines(filePath);

            const string scriptingDefineSymbolsStartKeyword = "scriptingDefineSymbols:";
            int startIndex = Array.FindIndex(
                lines, s => !string.IsNullOrEmpty(s) && s.Contains(scriptingDefineSymbolsStartKeyword));

            if (startIndex == -1)
            {
                // Build profiles may not be overriding project settings, in which case they don't need to have their
                // scripting define symbols updated, because we already update the scripting define symbols in the project
                // settings. If we are trying to write to the project settings themselves however, it's a problem if we
                // can't figure out where the scripting define symbols are at.
                if (!isBuildProfile)
                {
                    Debug.LogError(
                        $"Could not update scripting define symbols because the scripting define symbols could " +
                        $"not be found inside '{filePath}'");
                }
                return false;
            }
            
            const string buildProfileLinePrefix = "    - line: '|";

            string firstLine = lines[startIndex];
            if (isBuildProfile)
                firstLine = firstLine.Substring(buildProfileLinePrefix.Length);
            
            string terminationIndentation = firstLine.GetWhitespaceSucceeding(0, false);

            int lineIndex = startIndex + 1;
            bool symbolWasChangedOnAnyPlatform = false;
            while (true)
            {
                string line = lines[lineIndex];
                
                if (isBuildProfile)
                {
                    line = line.Substring(buildProfileLinePrefix.Length);
                    
                    // The line ends with a single quote ' so we need to remove that.
                    line = line.Substring(0, line.Length - 1);
                }
                
                string indentation = line.GetWhitespaceSucceeding(0, false);

                bool isPlatformSymbolsLine = !string.Equals(
                    indentation, terminationIndentation, StringComparison.OrdinalIgnoreCase);
                
                if (!isPlatformSymbolsLine)
                    break;

                const string symbolsStartKeyword = ": ";
                int symbolsStartIndex = line.IndexOf(symbolsStartKeyword, StringComparison.Ordinal);

                if (symbolsStartIndex == -1)
                {
                    Debug.LogError($"Tried to read platform symbols from '{filePath}' line #{lineIndex} but " +
                                   $"it was not formatted as expected.");
                    break;
                }

                symbolsStartIndex += symbolsStartKeyword.Length;
                string platformText = line.Substring(0, symbolsStartIndex);
                string symbolsText = line.Substring(symbolsStartIndex);

                const char symbolSeparator = ';';
                List<string> symbols = symbolsText.Split(symbolSeparator, StringSplitOptions.RemoveEmptyEntries).ToList();

                bool didChangeSymbols = false;

                // Either add or remove the specified symbol
                if (shouldExist)
                {
                    if (!symbol.Contains(symbol))
                    {
                        didChangeSymbols = true;
                        symbols.Add(symbol);
                    }
                }
                else
                    didChangeSymbols = symbols.Remove(symbol);
                
                if (didChangeSymbols)
                    symbolWasChangedOnAnyPlatform = true;
                
                if (didChangeSymbols)
                {
                    symbolsText = string.Join(symbolSeparator, symbols);
                    line = isBuildProfile ? buildProfileLinePrefix : string.Empty;
                    line += platformText + symbolsText;
                    
                    // For build profiles, the line must end with a single quote.
                    if (isBuildProfile)
                        line += "'";
                    
                    lines[lineIndex] = line;
                }
                
                lineIndex++;
            }
            
            if (symbolWasChangedOnAnyPlatform)
            {
                File.WriteAllLines(filePath, lines);
                AssetDatabase.Refresh();
            }

            return true;
        }
    }
}
