using System;
using System.Collections.Generic;
using UnityEditor;

namespace RoyTheunissen.AudioSyntax
{
    public sealed class MigrationFmodSyntaxToAudioSyntax : Migration
    {
        public override int VersionMigratingTo => 1;

        public override string DisplayName => "FMOD-Syntax to Audio-Syntax";

        public override string Description => "The system has since been updated to support Unity-based audio as " +
                                              "well, and has been renamed from FMOD-Syntax to Audio-Syntax " +
                                              "accordingly. Certain namespaces / classes have been renamed, we need " +
                                              "to make sure those are now updated if necessary. Additionally, " +
                                              "playing audio now by default returns playback instances with audio " +
                                              "system-agnostic types.";

        public override string DocumentationURL =>
            "https://github.com/RoyTheunissen/FMOD-Syntax/wiki/FMOD-Syntax#migrating-from-the-original-fmod-syntax-package-to-audio-syntax";

        protected override void RegisterRefactors(List<Refactor> refactors)
        {
            refactors.Add(new FmodSyntaxNamespaceToAudioSyntaxNamespaceRefactor());
            refactors.Add(new FmodSyntaxOutdatedSystemReferencesRefactor());
            refactors.Add(new FmodSyntaxAudioReferencePlaybackTypeRefactor());
            refactors.Add(new FmodSyntaxAudioFolderRenameRefactor());
            refactors.Add(new FmodSyntaxUpdateSettingsScriptableObjectRefactor());
            refactors.Add(new FmodSyntaxUpdateAssemblyDefinitionsRefactor());
        }
    }

    public abstract class FmodSyntaxToAudioSyntaxRefactor : Refactor
    {
        protected const string FmodSyntaxNamespace = "RoyTheunissen.FMODSyntax";
        protected const string AudioSyntaxNamespace = "RoyTheunissen.AudioSyntax";

        protected const string FmodSyntaxSystemName = "FmodSyntaxSystem";
        protected const string GeneralSystemName = "AudioSyntaxSystem";
        protected const string UnityAudioSystemName = "UnityAudioSyntaxSystem";
        protected const string CullPlaybacksMethod = "CullPlaybacks";
        protected const string StopAllActivePlaybacksMethod = "StopAllActivePlaybacks";
        protected const string StopAllActiveEventPlaybacksMethod = "StopAllActiveEventPlaybacks";
        protected const string UpdateMethod = "Update";
    }

    public sealed class FmodSyntaxNamespaceToAudioSyntaxNamespaceRefactor : FmodSyntaxToAudioSyntaxRefactor
    {
        protected override string NotNecessaryDisplayText =>
            $"There seem to be no more occurrences of the deprecated " +
            $"{FmodSyntaxNamespace} namespace.";

        protected override string IsNecessaryDisplayText => $"The system has detected that the FMOD-Syntax namespace " +
                                                            $"'{FmodSyntaxNamespace}' is being used. This has since been renamed to " +
                                                            $"'{AudioSyntaxNamespace}'.";

        protected override string ConfirmationDialogueText =>
            $"Are you sure you want to automatically replace the {FmodSyntaxNamespace} namespace with the {AudioSyntaxNamespace} namespace?";

        protected override bool CheckIfNecessaryInternal(out Migration.IssueUrgencies urgency)
        {
            bool isNecessary = IsReplacementInScriptsNecessary(FmodSyntaxNamespace, AudioSyntaxNamespace, FileScopes.Everything);

            urgency = Migration.IssueUrgencies.Required;

            return isNecessary;
        }

        protected override void OnPerform()
        {
            ReplaceInScripts(FmodSyntaxNamespace, AudioSyntaxNamespace, FileScopes.Everything);
        }
    }

    public sealed class FmodSyntaxOutdatedSystemReferencesRefactor : FmodSyntaxToAudioSyntaxRefactor
    {
        private readonly Dictionary<string, string> replacements = new()
        {
            { $"{FmodSyntaxSystemName}.{CullPlaybacksMethod}", $"{GeneralSystemName}.{UpdateMethod}" },
            { $"{FmodSyntaxSystemName}.{StopAllActivePlaybacksMethod}",
                $"{GeneralSystemName}.{StopAllActivePlaybacksMethod}" },
            { $"{FmodSyntaxSystemName}.{StopAllActiveEventPlaybacksMethod}",
                $"{GeneralSystemName}.{StopAllActiveEventPlaybacksMethod}" },
        };
        
        [NonSerialized] private string cachedReplacementsDisplayText;
        [NonSerialized] private bool didReplacementsDisplayText;
        private string ReplacementsDisplayText
        {
            get
            {
                if (!didReplacementsDisplayText)
                {
                    didReplacementsDisplayText = true;
                    cachedReplacementsDisplayText = GetDisplayTextForReplacements(replacements);
                }
                return cachedReplacementsDisplayText;
            }
        }

        protected override string IsNecessaryDisplayText => $"There used to be one system called '{FmodSyntaxSystemName}'. This has " +
                                                            $"been replaced by a general system '{GeneralSystemName}' which in turn " +
                                                            $"updates both '{FmodSyntaxSystemName}' and '{UnityAudioSystemName}'. " +
                                                            $"The '{CullPlaybacksMethod}' method has also been renamed " +
                                                            $"to '{UpdateMethod}' because it now does more than just culling " +
                                                            $"playbacks.\n\n" + ReplacementsDisplayText;

        protected override string NotNecessaryDisplayText => $"There seem to be no more outdated references to '{FmodSyntaxSystemName}'.";

        protected override string ConfirmationDialogueText => $"Are you sure you want to automatically update references to the old system " +
                                                              $"'{FmodSyntaxSystemName}' with references to the new system '{GeneralSystemName}' " +
                                                              $"where possible?";

        protected override bool CheckIfNecessaryInternal(out Migration.IssueUrgencies urgency)
        {
            bool isNecessary = AreReplacementsNecessary(replacements, ~FileScopes.GeneratedCode);

            urgency = Migration.IssueUrgencies.Required;
            
            return isNecessary;
        }

        protected override void OnPerform()
        {
            ReplaceInScripts(replacements, ~FileScopes.GeneratedCode);
        }
    }
    
    public sealed class FmodSyntaxAudioReferencePlaybackTypeRefactor : FmodSyntaxToAudioSyntaxRefactor
    {
        private const string OldParameterlessPlaybackType = "FmodParameterlessAudioPlayback";
        private const string OldFmodSpecificPlaybackType = "FmodAudioPlayback";
        
        private const string NewPlaybackType = "IAudioPlayback";

        protected override string IsNecessaryDisplayText => $"Playing an AudioReference assignable via the inspector used to return an instance of '{OldParameterlessPlaybackType}' but given that it now supports Unity native audio as well, it now returns an '{NewPlaybackType}' instead.\n\n" + ReplacementsDisplayText;

        protected override string NotNecessaryDisplayText => $"There seem to be no more outdated references to '{OldParameterlessPlaybackType}' / '{OldFmodSpecificPlaybackType}'.";

        protected override string ConfirmationDialogueText => $"Are you sure you want to automatically update references to " +
                                                              $"'{OldParameterlessPlaybackType}' / '{OldFmodSpecificPlaybackType}' with references to '{NewPlaybackType}' " +
                                                              $"where possible?";
        
        private readonly Dictionary<string, string> replacements = new()
        {
            { OldParameterlessPlaybackType, NewPlaybackType },
            { OldFmodSpecificPlaybackType, NewPlaybackType },
        };
        
        [NonSerialized] private string cachedReplacementsDisplayText;
        [NonSerialized] private bool didCacheReplacementsDisplayText;
        private string ReplacementsDisplayText
        {
            get
            {
                if (!didCacheReplacementsDisplayText)
                {
                    didCacheReplacementsDisplayText = true;
                    cachedReplacementsDisplayText = GetDisplayTextForReplacements(replacements);
                }
                return cachedReplacementsDisplayText;
            }
        }

        protected override bool CheckIfNecessaryInternal(out Migration.IssueUrgencies urgency)
        {
            bool isNecessary = IsReplacementInScriptsNecessary(OldParameterlessPlaybackType, NewPlaybackType, ~FileScopes.GeneratedCode);

            urgency = Migration.IssueUrgencies.Required;
            
            return isNecessary;
        }

        protected override void OnPerform()
        {
            // NOTE: It's fine for generated code to generate types that inherit from FmodParameterlessAudioPlayback / FmodAudioPlayback
            ReplaceInScripts(OldParameterlessPlaybackType, NewPlaybackType, ~FileScopes.GeneratedCode);
            ReplaceInScripts(OldFmodSpecificPlaybackType, NewPlaybackType, ~FileScopes.GeneratedCode);

            // The above refactor actually should not be done for implementations of IOnFmodPlayback's methods.
            // So we will go looking for those and put those back the way they were, that's easier than trying to figure
            // out whether an occurrence of the old playback type is an IOnFmodPlayback method and then not replacing it.
            string onFmodPlaybackRegistrationMethod = "OnFmodPlaybackRegistered({0} ";
            string onFmodPlaybackUnregistrationMethod = "OnFmodPlaybackUnregistered({0} ";
            ReplaceInScripts(
                string.Format(onFmodPlaybackRegistrationMethod, NewPlaybackType),
                string.Format(onFmodPlaybackRegistrationMethod, OldFmodSpecificPlaybackType),
                ~FileScopes.GeneratedCode);
            ReplaceInScripts(
                string.Format(onFmodPlaybackUnregistrationMethod, NewPlaybackType),
                string.Format(onFmodPlaybackUnregistrationMethod, OldFmodSpecificPlaybackType),
                ~FileScopes.GeneratedCode);
        }
    }
    
    public sealed class FmodSyntaxAudioFolderRenameRefactor : FmodSyntaxToAudioSyntaxRefactor
    {
        private const string OldAudioFolderType = "FmodAudioFolder";
        private const string NewAudioFolderType = "AudioFolder";

        protected override string IsNecessaryDisplayText => $"{OldAudioFolderType} has been renamed to {NewAudioFolderType} because it is used for Unity Audio Syntax as well.";

        protected override string NotNecessaryDisplayText => $"There seem to be no more references to '{OldAudioFolderType}'.";

        protected override string ConfirmationDialogueText => $"Are you sure you want to automatically update references to " +
                                                              $"'{OldAudioFolderType}' with references to '{NewAudioFolderType}' " +
                                                              $"where possible?";
        
        private readonly Dictionary<string, string> replacements = new()
        {
            { OldAudioFolderType, NewAudioFolderType },
        };

        protected override bool CheckIfNecessaryInternal(out Migration.IssueUrgencies urgency)
        {
            bool isNecessary = IsReplacementInScriptsNecessary(OldAudioFolderType, NewAudioFolderType, FileScopes.GeneratedCode);

            urgency = Migration.IssueUrgencies.Required;
            
            return isNecessary;
        }

        protected override void OnPerform()
        {
            ReplaceInScripts(OldAudioFolderType, NewAudioFolderType, FileScopes.GeneratedCode);
        }
    }
    
    public sealed class FmodSyntaxUpdateSettingsScriptableObjectRefactor : FmodSyntaxToAudioSyntaxRefactor
    {
        private const string OldSettingsName = "FmodSyntaxSettings";
        private const string NewSettingsName = "AudioSyntaxSettings";

        protected override string IsNecessaryDisplayText => $"The {OldSettingsName} Scriptable Object has been " +
                                                            $"renamed to {NewSettingsName} because it is used for " +
                                                            $"Unity Audio Syntax as well.";

        protected override string NotNecessaryDisplayText => $"There seem to be no more references to {OldSettingsName}.";

        protected override string ConfirmationDialogueText => $"Are you sure you want to automatically update references to " +
                                                              $"'{OldSettingsName}' with references to '{NewSettingsName}' " +
                                                              $"where possible?";

        private readonly Dictionary<string, string> replacements = new()
        {
            { "b66f732db3a804e4eb5ef4765f45f02f", "718c85fc338ba3c409c41b13d62c1d7e" },
        };

        protected override bool CheckIfNecessaryInternal(out Migration.IssueUrgencies urgency)
        {
            bool isNecessary = IsScriptReferenceReplacementNecessary(replacements);

            urgency = Migration.IssueUrgencies.Required;
            
            return isNecessary;
        }

        protected override void OnPerform()
        {
            bool didAnyReplacements = ReplaceScriptReferences(replacements);

            // For tidiness, let's also rename the settings file.
            if (didAnyReplacements)
            {
                AudioSyntaxSettings[] audioSyntaxSettings = AssetLoading.GetAllAssetsOfType<AudioSyntaxSettings>();
                for (int i = 0; i < audioSyntaxSettings.Length; i++)
                {
                    if (!string.Equals(audioSyntaxSettings[i].name, OldSettingsName))
                        continue;

                    string path = AssetDatabase.GetAssetPath(audioSyntaxSettings[i]);
                    AssetDatabase.RenameAsset(path, NewSettingsName);
                }
            }
        }
    }
    
    public sealed class FmodSyntaxUpdateAssemblyDefinitionsRefactor : FmodSyntaxToAudioSyntaxRefactor
    {
        private const string OldAssemblyName = "RoyTheunissen.FMODSyntax";
        private const string NewAssemblyName = "RoyTheunissen.AudioSyntax";

        private const string OldResourceName = "com.roytheunissen.fmod-syntax";
        private const string NewResourceName = "com.roytheunissen.audio-syntax";

        protected override string IsNecessaryDisplayText => $"References to the old assembly {OldAssemblyName} need " +
                                                            $"to be replaced to references to the new assembly " +
                                                            $"{NewAssemblyName}. Version defines that reference " +
                                                            $"{OldResourceName} will also need to be updated to " +
                                                            $"{NewResourceName}";

        protected override string NotNecessaryDisplayText => $"There seem to be no more references to " +
                                                             $"{OldAssemblyName} or {OldResourceName} in assembly " +
                                                             $"definitions.";

        protected override string ConfirmationDialogueText => $"Are you sure you want to automatically update " +
                                                              $"assembly references to '{OldAssemblyName}' with " +
                                                              $"assembly references to '{NewAssemblyName}' and " +
                                                              $"resource references to {OldResourceName} with " +
                                                              $"resource references to {NewResourceName} in " +
                                                              $"Assembly Definition files?";

        private readonly Dictionary<string, string> guidReferenceReplacements = new()
        {
            { "f28b76e6ba53ea24ca2daf55c1581312", "0ba6df2350fc8ee4fbe082c90af10c13" },
        };
        
        private readonly Dictionary<string, string> nameReferenceReplacements = new()
        {
            { OldAssemblyName, NewAssemblyName },
        };
        
        private readonly Dictionary<string, string> resourceReferenceReplacements = new()
        {
            { OldResourceName, NewResourceName },
        };

        protected override bool CheckIfNecessaryInternal(out Migration.IssueUrgencies urgency)
        {
            bool isNecessary = IsAssemblyDefinitionReferenceReplacementNecessary(
                guidReferenceReplacements, nameReferenceReplacements, resourceReferenceReplacements);

            urgency = Migration.IssueUrgencies.Required;
            
            return isNecessary;
        }

        protected override void OnPerform()
        {
            ReplaceAssemblyDefinitionReferences(
                guidReferenceReplacements, nameReferenceReplacements, resourceReferenceReplacements);
        }
    }
}
