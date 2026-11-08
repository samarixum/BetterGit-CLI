namespace BetterGit;

/// <summary>
/// Validates application-data and temporary paths without changing the user's saved environment.
/// </summary>
internal static class RuntimeEnvironment {
    /* :: :: Initialization :: START :: */

    /// <summary>
    /// Expands inherited path references and supplies missing or invalid variables for this process
    /// and its children. Windows defaults come from OS known folders, not the working directory.
    /// </summary>
    internal static void Initialize() {
        string profile = EnsureDirectoryVariable(
            name: OperatingSystem.IsWindows() ? "USERPROFILE" : "HOME",
            fallback: GetProfileDirectory()
        );

        string tempFallback;
        if (OperatingSystem.IsWindows()) {
            string localAppData = EnsureDirectoryVariable(
                name: "LOCALAPPDATA",
                fallback: GetKnownFolderOrDefault(
                    folder: Environment.SpecialFolder.LocalApplicationData,
                    fallback: Path.Combine(profile, "AppData", "Local")
                )
            );
            EnsureDirectoryVariable(
                name: "APPDATA",
                fallback: GetKnownFolderOrDefault(
                    folder: Environment.SpecialFolder.ApplicationData,
                    fallback: Path.Combine(profile, "AppData", "Roaming")
                )
            );
            tempFallback = Path.Combine(localAppData, "Temp");
        } else {
            tempFallback = "/tmp";
        }

        // Validate both before assigning either: a valid TEMP can rescue a broken TMP and vice versa.
        // Write probes catch directories that exist but cannot actually hold temporary files.
        string? tmp = TryPrepareDirectory(Environment.GetEnvironmentVariable("TMP"), requireWritable: true);
        string? temp = TryPrepareDirectory(Environment.GetEnvironmentVariable("TEMP"), requireWritable: true);
        string? tmpDir = OperatingSystem.IsWindows()
            ? null
            : TryPrepareDirectory(Environment.GetEnvironmentVariable("TMPDIR"), requireWritable: true);
        string writableTemp = tmpDir ?? temp ?? tmp
            ?? TryPrepareDirectory(tempFallback, requireWritable: true)
            ?? throw new IOException("No writable temporary directory is available. Check TEMP, TMP and directory permissions.");

        SetProcessVariable(name: "TMP", value: tmp ?? writableTemp);
        SetProcessVariable(name: "TEMP", value: temp ?? writableTemp);
        if (!OperatingSystem.IsWindows()) {
            SetProcessVariable(name: "TMPDIR", value: tmpDir ?? writableTemp);
        }
    }

    /* :: :: Initialization :: END :: */
    // //
    /* :: :: Path Validation :: START :: */

    ///
    // Keep usable custom paths; replace missing, relative, unresolved or inaccessible values.
    ///
    private static string EnsureDirectoryVariable(string name, string fallback) {
        string path = TryPrepareDirectory(Environment.GetEnvironmentVariable(name), requireWritable: false)
            ?? TryPrepareDirectory(fallback, requireWritable: false)
            ?? throw new IOException($"Cannot determine a valid directory for {name}.");
        SetProcessVariable(name: name, value: path);
        return path;
    }

    ///
    // Expand nested %NAME% references with a bound so cycles cannot hang startup. Never turn
    // relative paths into absolute paths: that would recreate literal folders inside the repository.
    ///
    private static string? TryPrepareDirectory(string? value, bool requireWritable) {
        if (string.IsNullOrWhiteSpace(value)) {
            return null;
        }

        try {
            string expanded = value;
            for (int i = 0; i < 16; i++) {
                string next = Environment.ExpandEnvironmentVariables(expanded);
                if (next == expanded) {
                    break;
                }
                expanded = next;
            }

            if (System.Text.RegularExpressions.Regex.IsMatch(expanded, "%[^%]+%")
                || !Path.IsPathFullyQualified(expanded)) {
                return null;
            }

            string path = Path.GetFullPath(expanded);
            Directory.CreateDirectory(path);
            if (requireWritable) {
                // DeleteOnClose avoids leaving probes behind, including when a later operation fails.
                using FileStream probe = new FileStream(
                    path: Path.Combine(path, $".bettergit-{Guid.NewGuid():N}.tmp"),
                    mode: FileMode.CreateNew,
                    access: FileAccess.Write,
                    share: FileShare.None,
                    bufferSize: 1,
                    options: FileOptions.DeleteOnClose
                );
                probe.WriteByte(0);
            }
            return path;
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Security.SecurityException) {
            return null;
        }
    }

    ///
    // Known folders honor Windows folder redirection even when shell variables are missing.
    ///
    private static string GetKnownFolderOrDefault(Environment.SpecialFolder folder, string fallback) {
        // Some managed folder lookups return the inherited variable verbatim. Validate that
        // result too, rather than allowing a nonempty but malformed value to defeat the fallback.
        return TryPrepareDirectory(Environment.GetFolderPath(folder), requireWritable: false) ?? fallback;
    }

    ///
    // Managed known-folder lookup can itself fail when USERPROFILE is missing. Ask Windows
    // for the current token's profile directly, before repairing the remaining known folders.
    ///
    private static string GetProfileDirectory() {
        if (!OperatingSystem.IsWindows()) {
            return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        using System.Security.Principal.WindowsIdentity identity = System.Security.Principal.WindowsIdentity.GetCurrent();
        int length = 0;
        GetUserProfileDirectory(identity.AccessToken, null, ref length);
        if (length > 0) {
            System.Text.StringBuilder buffer = new System.Text.StringBuilder(capacity: length);
            if (GetUserProfileDirectory(identity.AccessToken, buffer, ref length)) {
                return buffer.ToString();
            }
        }
        string? managedProfile = TryPrepareDirectory(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), requireWritable: false
        );
        if (managedProfile != null) {
            return managedProfile;
        }

        // Restricted/service tokens can have no loaded profile. Windows also supplies this
        // pair at logon; concatenation preserves HOMEPATH's leading slash without dropping the drive.
        return Environment.GetEnvironmentVariable("HOMEDRIVE") + Environment.GetEnvironmentVariable("HOMEPATH");
    }

    // This OS API reads the current user's profile; it does not load profiles or change settings.
    [System.Runtime.InteropServices.DllImport("userenv.dll", EntryPoint = "GetUserProfileDirectoryW", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool GetUserProfileDirectory(
        Microsoft.Win32.SafeHandles.SafeAccessTokenHandle token,
        System.Text.StringBuilder? profileDirectory,
        ref int length
    );

    ///
    // Always select Process explicitly: these repairs must never write User/Machine registry values.
    ///
    private static void SetProcessVariable(string name, string value) {
        Environment.SetEnvironmentVariable(name, value, EnvironmentVariableTarget.Process);
    }

    /* :: :: Path Validation :: END :: */
}
