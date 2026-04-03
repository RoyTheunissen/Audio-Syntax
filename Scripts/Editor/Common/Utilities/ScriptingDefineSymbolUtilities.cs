using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Build;
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
        private const string ScriptingDefineSymbolsStartKeyword = "scriptingDefineSymbols:";
        private const string BuildProfileProjectSettingsOverrideLineSuffix = "'";
        private const string BuildProfileLinePrefix = "    - line: '|";
        private const string EmptyScriptingDefineSymbolsKeyword = "{}";
        private const string SymbolsStartKeyword = ": ";
        private const char SymbolSeparator = ';';
        
        private static string PlayerSettingsFilePath => Path.Combine(
            Application.dataPath.GetParentDirectory(), "ProjectSettings", "ProjectSettings.asset").ToUnityPath();
        
        /// <summary>
        /// Get a list of all valid named build targets, with *known* invalid named build targets filtered out.
        /// This may include some platforms that are actually obsolete, but having scripting define symbols for these
        /// platforms should do no harm (I've tested it). Setting it up this way should be future-proof, as opposed to
        /// hardcoding a list of valid build targets which would become obsolete the next time a platform is added.
        /// </summary>
        private static string[] GetBuildPlatformTargetNames()
        {
            // First just get a list of all named build targets.
            FieldInfo[] staticNamedBuildTargetFields =
                typeof(NamedBuildTarget).GetFields(BindingFlags.Static | BindingFlags.Public);
            List<NamedBuildTarget> namedBuildTargets = new();
            for (int i = 0; i < staticNamedBuildTargetFields.Length; i++)
            {
                if (staticNamedBuildTargetFields[i].FieldType != typeof(NamedBuildTarget))
                    continue;

                NamedBuildTarget namedBuildTarget = (NamedBuildTarget)staticNamedBuildTargetFields[i].GetValue(null);
                namedBuildTargets.Add(namedBuildTarget);
            }
            
            // Now try and find out which ones are tagged as obsolete.
            List<string> obsoleteNamedBuildTargetNames = new();
            for (int i = 0; i < staticNamedBuildTargetFields.Length; i++)
            {
                if (staticNamedBuildTargetFields[i].FieldType != typeof(NamedBuildTarget))
                    continue;

                NamedBuildTarget namedBuildTarget = (NamedBuildTarget)staticNamedBuildTargetFields[i].GetValue(null);

                bool isTaggedAsObsolete = staticNamedBuildTargetFields[i].HasAttribute<ObsoleteAttribute>();
                
                if (isTaggedAsObsolete)
                {
                    int occurrences = namedBuildTargets.Count(nbt => nbt == namedBuildTarget);
                    if (occurrences > 1)
                    {
                        // Sometimes an obsolete build target is not assigned a unique value, but is instead assigned
                        // to a valid build target instead. If this is the case, then the value in question would occur
                        // several times. We don't then want to add the name of this valid build target to the list of
                        // obsolete build targets, so in this case we should just use the name of the field instead.
                        obsoleteNamedBuildTargetNames.Add(staticNamedBuildTargetFields[i].Name);
                    }
                    else
                        obsoleteNamedBuildTargetNames.Add(namedBuildTarget.TargetName);
                }
            }

            FieldInfo validNamesField = typeof(NamedBuildTarget).GetField(
                "k_ValidNames", BindingFlags.Static | BindingFlags.NonPublic);
            
            List<string> validNames = ((string[])validNamesField.GetValue(null)).ToList();
            
            // Try to determine actually valid named build targets.
            string[] knownInvalidNames = { "", "FakePlatform", "Server" };
            for (int i = validNames.Count - 1; i >= 0; i--)
            {
                if (knownInvalidNames.Contains(validNames[i]))
                    validNames.RemoveAt(i);
                if (obsoleteNamedBuildTargetNames.Contains(validNames[i]))
                    validNames.RemoveAt(i);
            }
            validNames.Sort(StringComparer.Ordinal);

            return validNames.ToArray();
        }

        private static string[] GetBuildProfilePaths()
        {
            string[] buildProfileGuids = AssetDatabase.FindAssets("t:buildprofile");
            string[] buildProfilePaths = buildProfileGuids
                .Select(bpg => AssetDatabase.GUIDToAssetPath(bpg).GetAbsolutePath()).ToArray();
            return buildProfilePaths;
        }

        public static bool IsScriptingDefineSymbolDefined(string symbol)
        {
            // First check the player settings
            bool isDefinedInPlayerSettings =
                IsScriptingDefineSymbolDefinedInFile(symbol, PlayerSettingsFilePath, false);
            if (!isDefinedInPlayerSettings)
                return false;
            
            // Then check the build profiles
            string[] buildProfilePaths = GetBuildProfilePaths();
            for (int i = 0; i < buildProfilePaths.Length; i++)
            {
                bool isDefinedInBuildProfile =
                    IsScriptingDefineSymbolDefinedInFile(symbol, buildProfilePaths[i], true);

                if (!isDefinedInBuildProfile)
                    return false;
            }

            return true;
        }
        
        private static bool IsScriptingDefineSymbolDefinedInFile(string symbol, string filePath, bool isBuildProfile)
        {
            if (!File.Exists(filePath))
            {
                Debug.LogError($"Tried to determine if Scripting Define Symbol '{symbol}' is defined in file " +
                               $"'{filePath}' but the file could not be found.");
                return false;
            }

            List<string> lines = File.ReadAllLines(filePath).ToList();
            
            int startIndex = lines.FindIndex(
                s => !string.IsNullOrEmpty(s) && s.Contains(ScriptingDefineSymbolsStartKeyword));

            if (startIndex == -1)
            {
                // Build profiles may not be overriding project settings, in which case they don't need to have their
                // scripting define symbols updated, because we already update the scripting define symbols in the project
                // settings. If we are trying to write to the project settings themselves however, it's a problem if we
                // can't figure out where the scripting define symbols are at.
                if (!isBuildProfile)
                {
                    Debug.LogError(
                        $"Could not determine if scripting define symbol '{symbol}' is defined in file '{filePath}' " +
                        $"because the scripting define symbols could not be found at all.");
                }
                return false;
            }
            

            string firstLine = lines[startIndex];
            if (isBuildProfile)
                firstLine = firstLine.Substring(BuildProfileLinePrefix.Length);

            // If the scripting define symbols line contains a {} then that means that there are none. This is a special
            // case, and then we need to construct a list of named build targets with every scripting define symbol.
            if (firstLine.Contains(EmptyScriptingDefineSymbolsKeyword))
                return false;
            
            string terminationIndentation = firstLine.GetWhitespaceSucceeding(0, false);

            int lineIndex = startIndex + 1;
            while (true)
            {
                string line = lines[lineIndex];
                
                if (isBuildProfile)
                {
                    line = line.Substring(BuildProfileLinePrefix.Length);
                    
                    // The line ends with a single quote ' so we need to remove that.
                    line = line.Substring(0, line.Length - 1);
                }
                
                string indentation = line.GetWhitespaceSucceeding(0, false);

                bool isPlatformSymbolsLine = !string.Equals(
                    indentation, terminationIndentation, StringComparison.OrdinalIgnoreCase);
                
                if (!isPlatformSymbolsLine)
                    break;
                
                int symbolsStartIndex = line.IndexOf(SymbolsStartKeyword, StringComparison.Ordinal);

                if (symbolsStartIndex == -1)
                {
                    Debug.LogError($"Tried to read platform symbols from '{filePath}' line #{lineIndex} but " +
                                   $"it was not formatted as expected.");
                    break;
                }

                symbolsStartIndex += SymbolsStartKeyword.Length;
                string symbolsText = line.Substring(symbolsStartIndex);
                
                List<string> symbols = symbolsText.Split(SymbolSeparator, StringSplitOptions.RemoveEmptyEntries).ToList();

                if (!symbols.Contains(symbol))
                    return false;
                
                lineIndex++;
            }

            return true;
        }
        
        public static bool UpdateScriptingDefineSymbol(string symbol, bool shouldExist)
        {
            bool wasSuccessful = true;
            
            // First update the player settings
            bool successfullyUpdatedPlayerSettings =
                UpdateScriptingDefineSymbolInFile(symbol, shouldExist, PlayerSettingsFilePath, false);
            if (!successfullyUpdatedPlayerSettings)
                wasSuccessful = false;
            
            // Then update the build profiles
            string[] buildProfilePaths = GetBuildProfilePaths();
            for (int i = 0; i < buildProfilePaths.Length; i++)
            {
                bool successfullyUpdatedBuildProfile =
                    UpdateScriptingDefineSymbolInFile(symbol, shouldExist, buildProfilePaths[i], true);
                
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

            List<string> lines = File.ReadAllLines(filePath).ToList();
            
            int startIndex = lines.FindIndex(
                s => !string.IsNullOrEmpty(s) && s.Contains(ScriptingDefineSymbolsStartKeyword));

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
            

            string firstLine = lines[startIndex];
            if (isBuildProfile)
                firstLine = firstLine.Substring(BuildProfileLinePrefix.Length);

            // If the scripting define symbols line contains a {} then that means that there are none. This is a special
            // case, and then we need to construct a list of named build targets with every scripting define symbol.
            bool didNotHaveAnyScriptingDefineSymbols = false;
            if (firstLine.Contains(EmptyScriptingDefineSymbolsKeyword))
            {
                didNotHaveAnyScriptingDefineSymbols = true;
                
                // Remove the {}
                firstLine = firstLine.Replace(EmptyScriptingDefineSymbolsKeyword, "");
                lines[startIndex] = (isBuildProfile ? BuildProfileLinePrefix : string.Empty) + firstLine;
                
                // Add a line for every build platform target name
                string[] buildPlatformTargetNames = GetBuildPlatformTargetNames();
                for (int i = buildPlatformTargetNames.Length - 1; i >= 0; i--)
                {
                    string buildPlatformTargetLine = isBuildProfile ? BuildProfileLinePrefix : string.Empty;
                    buildPlatformTargetLine += "     " + buildPlatformTargetNames[i] + ": ";
                    if (isBuildProfile)
                        buildPlatformTargetLine += BuildProfileProjectSettingsOverrideLineSuffix;
                    lines.Insert(startIndex + 1, buildPlatformTargetLine);
                }
                
                // Now we can proceed as normal and add the scripting define symbols to every platform.
            }
            
            string terminationIndentation = firstLine.GetWhitespaceSucceeding(0, false);

            int lineIndex = startIndex + 1;
            bool symbolWasChangedOnAnyPlatform = didNotHaveAnyScriptingDefineSymbols;
            while (true)
            {
                string line = lines[lineIndex];
                
                if (isBuildProfile)
                {
                    line = line.Substring(BuildProfileLinePrefix.Length);
                    
                    // The line ends with a single quote ' so we need to remove that.
                    line = line.Substring(0, line.Length - 1);
                }
                
                string indentation = line.GetWhitespaceSucceeding(0, false);

                bool isPlatformSymbolsLine = !string.Equals(
                    indentation, terminationIndentation, StringComparison.OrdinalIgnoreCase);
                
                if (!isPlatformSymbolsLine)
                    break;
                
                int symbolsStartIndex = line.IndexOf(SymbolsStartKeyword, StringComparison.Ordinal);

                if (symbolsStartIndex == -1)
                {
                    Debug.LogError($"Tried to read platform symbols from '{filePath}' line #{lineIndex} but " +
                                   $"it was not formatted as expected.");
                    break;
                }

                symbolsStartIndex += SymbolsStartKeyword.Length;
                string platformText = line.Substring(0, symbolsStartIndex);
                string symbolsText = line.Substring(symbolsStartIndex);
                
                List<string> symbols = symbolsText.Split(SymbolSeparator, StringSplitOptions.RemoveEmptyEntries).ToList();

                bool didChangeSymbols = false;

                // Either add or remove the specified symbol
                if (shouldExist)
                {
                    if (!symbolsText.Contains(symbol))
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
                    symbolsText = string.Join(SymbolSeparator, symbols);
                    line = isBuildProfile ? BuildProfileLinePrefix : string.Empty;
                    line += platformText + symbolsText;
                    
                    // For build profiles, the line must end with a certain suffix.
                    if (isBuildProfile)
                        line += BuildProfileProjectSettingsOverrideLineSuffix;
                    
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
