using Tomlyn;
using Tomlyn.Model;

namespace BetterGit;

/// <summary>
/// Reads and updates version metadata stored in BetterGit TOML files.
/// </summary>
public class VersionService : IVersionService {
    private readonly string _repoPath;

    /* :: :: Constructors :: START :: */

    public VersionService(string repoPath) {
        _repoPath = repoPath;
    }

    /* :: :: Constructors :: END :: */
    // //
    /* :: :: Methods :: START :: */

    public (long Major, long Minor, long Patch, bool IsAlpha, bool IsBeta) GetCurrentVersion() {
        (long Major, long Minor, long Patch, bool IsAlpha, bool IsBeta, WebProjectOptions WebProject) state = ReadCurrentVersionState();
        return (state.Major, state.Minor, state.Patch, state.IsAlpha, state.IsBeta);
    }

    private (long Major, long Minor, long Patch, bool IsAlpha, bool IsBeta, WebProjectOptions WebProject) ReadCurrentVersionState() {
        BetterGitConfigPaths.MigrateMetaTomlToProjectTomlIfNeeded(_repoPath);

        string projectFile = BetterGitConfigPaths.GetProjectTomlPath(_repoPath);
        string legacyMetaFile = BetterGitConfigPaths.GetLegacyMetaTomlPath(_repoPath);
        long major = 0, minor = 0, patch = 0;
        bool isAlpha = false;
        bool isBeta = false;
        WebProjectOptions webProject = new WebProjectOptions();
        bool projectExists = File.Exists(projectFile);
        bool legacyExists = File.Exists(legacyMetaFile);

        // 1. Read TOML
        string? fileToRead = projectExists ? projectFile : (legacyExists ? legacyMetaFile : null);
        if (fileToRead != null) {
            try {
                TomlTable model = TomlSupport.ReadTable(fileToRead);
                
                if (model.ContainsKey("version") && !model.ContainsKey("patch")) {
                    patch = (long)model["version"];
                } else {
                    if (model.ContainsKey("major")) major = (long)model["major"];
                    if (model.ContainsKey("minor")) minor = (long)model["minor"];
                    if (model.ContainsKey("patch")) patch = (long)model["patch"];
                    if (model.ContainsKey("isAlpha")) isAlpha = (bool)model["isAlpha"];
                    if (model.ContainsKey("isBeta")) isBeta = (bool)model["isBeta"];
                    if (model.ContainsKey("isNodeProject")) webProject = webProject with { IsNodeProject = (bool)model["isNodeProject"] };
                    if (model.ContainsKey("isDenoProject")) webProject = webProject with { IsDenoProject = (bool)model["isDenoProject"] };
                }
            } catch { /* Ignore corrupt, start from 0.0.0 */ }
        }

        // 1b. Sync with web project config files if needed.
        IReadOnlyList<string> configPaths = WebProjectSupport.GetExistingConfigPaths(_repoPath);
        if (configPaths.Count > 0) {
            foreach (string configPath in configPaths) {
                if (!WebProjectSupport.TryReadVersion(configPath, out long configMajor, out long configMinor, out long configPatch, out bool configAlpha, out bool configBeta)) {
                    continue;
                }

                if (IsHigherVersion(configMajor, configMinor, configPatch, major, minor, patch)) {
                    major = configMajor;
                    minor = configMinor;
                    patch = configPatch;
                    isAlpha = configAlpha;
                    isBeta = configBeta;
                }
            }
        }

        WebProjectOptions detectedProject = WebProjectSupport.Detect(_repoPath);
        webProject = webProject with {
            IsNodeProject = webProject.IsNodeProject || detectedProject.IsNodeProject,
            IsDenoProject = webProject.IsDenoProject || detectedProject.IsDenoProject
        };

        return (major, minor, patch, isAlpha, isBeta, webProject);
    }

    public string IncrementVersion(VersionChangeType changeType = VersionChangeType.Patch, string? manualVersion = null) {
        // This creates/updates a '.betterGit/project.toml' file (public/committed)
        BetterGitConfigPaths.MigrateMetaTomlToProjectTomlIfNeeded(_repoPath);
        BetterGitConfigPaths.EnsureBetterGitDirExists(_repoPath);

        (long Major, long Minor, long Patch, bool IsAlpha, bool IsBeta, WebProjectOptions WebProject) state = ReadCurrentVersionState();
        long major = state.Major;
        long minor = state.Minor;
        long patch = state.Patch;
        bool isAlpha = state.IsAlpha;
        bool isBeta = state.IsBeta;
        WebProjectOptions webProject = state.WebProject;

        string projectFile = BetterGitConfigPaths.GetProjectTomlPath(_repoPath);
        TomlTable toml = ReadProjectTomlModel(projectFile);

        // 2. Increment Logic
        if (changeType == VersionChangeType.Manual && !string.IsNullOrWhiteSpace(manualVersion)) {
             string v = manualVersion;
             // Reset flags
             isAlpha = false;
             isBeta = false;

             if (v.EndsWith("-A")) {
                isAlpha = true;
                v = v.Substring(0, v.Length - 2);
             } else if (v.EndsWith("-B")) {
                isBeta = true;
                v = v.Substring(0, v.Length - 2);
             }

             string[] parts = v.Split('.');
             if (parts.Length >= 1) long.TryParse(parts[0], out major); else major = 0;
             if (parts.Length >= 2) long.TryParse(parts[1], out minor); else minor = 0;
             if (parts.Length >= 3) long.TryParse(parts[2], out patch); else patch = 0;

        } else if (changeType == VersionChangeType.Major) {
            major++;
            minor = 0;
            patch = 0;
        } else if (changeType == VersionChangeType.Minor) {
            minor++;
            patch = 0;
        } else if (changeType == VersionChangeType.Patch) {
            patch++;
        }
        // If None, do nothing

        // 3. Write TOML
        toml["major"] = major;
        toml["minor"] = minor;
        toml["patch"] = patch;
        toml["isAlpha"] = isAlpha;
        toml["isBeta"] = isBeta;
        toml["isNodeProject"] = webProject.IsNodeProject;
        toml["isDenoProject"] = webProject.IsDenoProject;
        File.WriteAllText(projectFile, TomlSupport.WriteTable(toml));

        string versionString = $"{major}.{minor}.{patch}";
        if (isAlpha) versionString += "-A";
        else if (isBeta) versionString += "-B";

        // 4. Update web project config files if they exist (Preserving Formatting)
        foreach (string configPath in WebProjectSupport.GetExistingConfigPaths(_repoPath)) {
            WebProjectSupport.UpdateVersionField(configPath, versionString);
        }

        return $"v{versionString}";
    }

    public void SetChannel(string channel) {
        BetterGitConfigPaths.MigrateMetaTomlToProjectTomlIfNeeded(_repoPath);
        BetterGitConfigPaths.EnsureBetterGitDirExists(_repoPath);

        string projectFile = BetterGitConfigPaths.GetProjectTomlPath(_repoPath);
        TomlTable toml = ReadProjectTomlModel(projectFile);

        // Update flags
        toml["isAlpha"] = false;
        toml["isBeta"] = false;

        if (channel.ToLower() == "alpha") {
            toml["isAlpha"] = true;
        } else if (channel.ToLower() == "beta") {
            toml["isBeta"] = true;
        }

        File.WriteAllText(projectFile, TomlSupport.WriteTable(toml));
        Console.WriteLine($"Channel set to: {channel}");
    }

    /* :: :: Methods :: END :: */

    private static bool IsHigherVersion(long candidateMajor, long candidateMinor, long candidatePatch, long currentMajor, long currentMinor, long currentPatch) {
        if (candidateMajor > currentMajor) {
            return true;
        }

        if (candidateMajor < currentMajor) {
            return false;
        }

        if (candidateMinor > currentMinor) {
            return true;
        }

        if (candidateMinor < currentMinor) {
            return false;
        }

        return candidatePatch > currentPatch;
    }

    private static TomlTable ReadProjectTomlModel(string projectTomlPath) {
        if (!File.Exists(projectTomlPath)) {
            return new TomlTable();
        }

        return TomlSupport.ReadTable(projectTomlPath);
    }
}
