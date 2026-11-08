# BetterGit CLI

The core engine for BetterGit, built with .NET and `LibGit2Sharp`. This console application handles all the heavy lifting for repository management, versioning, and safe history traversal.

the core concept is to simplify version control by eliminating standard Git complexities i dont need
instead using it as a non-destructive save/load/backup system.
and adding some new features like publishing backups to maultiple remote repositories simultaneously.
all whithout preventing the user from using git directly if they want to.


## Prerequisites
* .NET 10.0 SDK

## Building
```pwsh
dotnet build
```

## Publishing and startup environment

```pwsh
dotnet publish -c Release -f net10.0 -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish/win-x64
```

Deploy the **whole publish directory**, including the native DLLs beside `BetterGit.exe`.
Managed assemblies are bundled, but native libraries deliberately remain outside the bundle.
Enabling `IncludeNativeLibrariesForSelfExtract` makes .NET extract libraries before C# starts,
so a bad inherited `TMP` or `TEMP` can prevent the application from repairing its environment.
The same directory-based deployment applies to `linux-x64` and its native libraries.

At startup, BetterGit validates `USERPROFILE`, `LOCALAPPDATA`, `APPDATA`, `TMP` and `TEMP`
on Windows (`HOME`, `TMPDIR`, `TMP` and `TEMP` on other platforms). It expands nested
`%NAME%` references, preserves usable custom absolute paths, creates missing directories,
and replaces relative paths, unresolved references and inaccessible directories with OS defaults
or another usable temporary path. Temporary directories must pass a write check.
For example, a literal `TMP=%AppData_Dir%\Local\Temp` is expanded if that custom variable
exists; otherwise BetterGit uses a valid `TEMP` or the user's local temporary directory.
No path is resolved relative to the repository.

These changes use **process scope only** and are inherited by child Git processes. They expire
when BetterGit exits; the parent terminal and User/Machine registry settings are untouched.
If no valid required directory is available, startup reports an error on stderr and exits with code 1.
`environment-manager.exe` is useful for inspecting persistent settings but is not a runtime dependency.
In PowerShell, inspect variables with `$env:TMP` and `$env:TEMP`; `%TMP%` is cmd.exe syntax.

Run the Windows environment regression checks, optionally including the published executable:

```pwsh
dotnet run --project tests/RuntimeEnvironment.Tests -- publish/win-x64/BetterGit.exe
```

To check real repository operations with malformed or missing temporary variables, use the
published executable directly. This test runs read operations only, checks for literal folders
after every operation, and checks for recreation by background processes while idle:

```pwsh
python tests/test_repository_environment.py --repo A:\BetterVscode --clean
```

`--clean` deletes only the literal `%AppData_Dir%` cache folders at the supplied repository
root and its `BetterGitNet` child, if present. Omit it to require a clean starting state.
The runner needs Python 3.12 or later and must run as the repository owner.

BetterGit repairs only its own process and children. An already running editor, language
server, shell or .NET SDK build can still use its previously inherited, unexpanded `TMP`.
For example, `JB/AssemblyShadowCopies` contains ReSharper analyzer caches and `NuGetScratch`
is created by .NET package tooling. Those processes must receive a valid absolute `TMP`
before they start. Correct the launching shell with `$env:TMP = $env:TEMP` (after validating
`$env:TEMP`) and restart affected tools; changing a saved registry value does not update
processes that are already running. Do not run a self-extracting diagnostic tool with the
malformed variable, since that diagnostic tool can itself recreate the literal directory.

## Usage
*   **Initialize:**
    ```pwsh
    BetterGit.exe init "A:/Path/To/Project" [--node] [--deno]
    ```
*   **Save Changes:**
    ```pwsh
    BetterGit.exe save "Commit message" [--major|--minor]
    ```
*   **Undo Last Save:**
    ```pwsh
    BetterGit.exe undo
    ```
    *Moves the current state to an archive branch and resets HEAD to the parent commit.*
*   **Redo:**
    ```pwsh
    BetterGit.exe redo
    ```
    *Restores the state from the most recent archive branch.*
*   **Get Tree Data (JSON):**
    ```pwsh
    BetterGit.exe get-tree-data
    ```
*   **Add Remote Mirror:**
    ```pwsh
    BetterGit.exe remote add <name> <url> [--group <g>] [--provider <p>] [--branch <b>] [--public|--private]
    ```
    *Creates a Git remote and stores the matching BetterGit metadata in `.betterGit/project.toml`.*
*   **Change Remote Metadata:**
    ```pwsh
    BetterGit.exe remote set-meta <name> [--group <g>] [--provider <p>] [--branch <b>] [--public|--private]
    ```

## CLI output encoding

BetterGit writes stdout and stderr as UTF-8 without a byte-order mark, including when
launched with redirected pipes by VS Code. Consumers should decode both streams as
UTF-8. This preserves Unicode commit messages, file paths, file contents and warnings
regardless of the Windows console code page. JSON serialization still escapes literal
control characters already present in repository data; no history rewrite is needed.

Run the output regression check against a built or published executable:

```pwsh
python tests/test_console_utf8.py --exe bin/Release/net10.0/BetterGit.exe
```

The check creates and removes its own temporary repository. It checks exact Unicode
text in timeline and archive messages, changed file paths, JSON and stderr warnings,
and file content, plus strict JSON parsing and escaping of an existing control character.
Use `--scratch-root <directory>` to place the fixture in a specific writable directory.

## Notice

This project was generated entirely with AI.
