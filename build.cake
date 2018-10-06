#tool "nuget:?package=xunit.runner.console"
#tool "nuget:?package=gitreleasemanager"
#l "tools/scripts/backup.cake"
#l "tools/scripts/constants.cake"
#l "tools/scripts/bass.cake"
using System.Collections.Generic;
using static System.Text.RegularExpressions.Regex;

#region Fields
var target = Argument("target", "Default");
var configuration = Argument("configuration", Release);
var version = Argument<string>("appversion", null);
var tag = Argument<string>("apptag", null);
var chocoVersion = tag?.Substring(1) ?? "";

readonly var ChocoPkgPath = tempFolder + File($"captura.{chocoVersion}.nupkg");

var prerelease = false;

var buildNo = EnvironmentVariable("APPVEYOR_BUILD_NUMBER");

// Deploy on Release Tag builds
var deploy = configuration == Release && !string.IsNullOrWhiteSpace(tag);
#endregion

#region Functions
void HandleTag()
{
    // Not a Tag Build
    if (string.IsNullOrWhiteSpace(tag))
        return;

    const string StableVersionRegex = @"^v\d+\.\d+\.\d+$";
    const string PrereleaseVersionRegex = @"^v\d+\.\d+\.\d+-[^\s]+$";

    // Stable Release
    if (IsMatch(tag, StableVersionRegex))
    {
        version = tag.Substring(1);
    }
    // Prerelease
    else if (IsMatch(tag, PrereleaseVersionRegex))
    {
        prerelease = true;

        version = tag.Split('-')[0].Substring(1);

        Information("Update version");
    }
    else throw new ArgumentException("Invalid Tag Format", "Tag");
}

void UpdateVersion(string AssemblyInfoPath)
{
    var content = FileRead(AssemblyInfoPath);

    var start = content.IndexOf("AssemblyVersion");
    var end = content.IndexOf(")", start);

    var replace = content.Replace(content.Substring(start, end - start + 1), $"AssemblyVersion(\"{version}\")");

    FileWrite(AssemblyInfoPath, replace);
}

string FileRead(string FileName) => System.IO.File.ReadAllText(FileName);

void FileWrite(string FileName, string Content) => System.IO.File.WriteAllText(FileName, Content);

void HandleVersion()
{
    var assemblyInfoFile = File("Properties/AssemblyInfo.cs");
    var uiAssemblyInfo = sourceFolder + Directory("Captura") + assemblyInfoFile;
    var consoleAssemblyInfo = sourceFolder + Directory("Captura.Console") + assemblyInfoFile;

    void DoUpdate()
    {
        // Update AssemblyInfo files
        CreateBackup(uiAssemblyInfo, tempFolder + File("AssemblyInfo.cs"));
        CreateBackup(consoleAssemblyInfo, tempFolder + File("console.cs"));

        UpdateVersion(uiAssemblyInfo);
        UpdateVersion(consoleAssemblyInfo);
    }

    if (string.IsNullOrWhiteSpace(version))
    {
        // Read from AssemblyInfo
        version = ParseAssemblyInfo(uiAssemblyInfo).AssemblyVersion;

        // Dev build
        if (version == "0.0.0" && !string.IsNullOrWhiteSpace(buildNo))
        {
            version = $"0.0.{buildNo}";

            DoUpdate();
        }
    }
    else DoUpdate();
}

void EmbedApiKeys()
{
    var apiKeysPath = sourceFolder + File("Captura.Core/ApiKeys.cs");
    const string imgurEnv = "imgur_client_id";

    // Embed Api Keys in Release builds
    if (configuration == Release && HasEnvironmentVariable(imgurEnv))
    {
        Information("Embedding Api Keys from Environment Variables ...");

        CreateBackup(apiKeysPath, tempFolder + File("ApiKeys.cs"));

        var apiKeysOriginalContent = FileRead(apiKeysPath);

        var newContent = apiKeysOriginalContent.Replace($"Get(\"{imgurEnv}\")", $"\"{EnvironmentVariable(imgurEnv)}\"");

        FileWrite(apiKeysPath, newContent);
    }
}

IEnumerable<ConvertableDirectoryPath> EnumerateOutputFolders()
{
    var outputProjectNames = new[] { "Captura.Console", "Captura", "Tests" };

    foreach (var output in outputProjectNames)
    {
        yield return sourceFolder + Directory(output) + Directory("bin") + Directory(configuration);
    }
}

// Restores native dlls
void NativeRestore()
{
    Information("Restoring Native libraries...");

    var bass = RestoreBass();
    var bassmix = RestoreBassMix();

    foreach (var output in EnumerateOutputFolders())
    {
        var path = output;

        if (configuration == Release)
        {
            path += Directory("lib");
        }

        EnsureDirectoryExists(path);

        CopyFileToDirectory(bass, path);
        CopyFileToDirectory(bassmix, path);
    }
}

void CopyLicenses()
{
    foreach (var output in EnumerateOutputFolders())
    {
        var path = output + Directory("licenses");

        CopyDirectory(licensesFolder, path);
    }
}

void PopulateOutput()
{
    // Copy License files
    CopyDirectory(licensesFolder, distFolder + Directory("licenses"));

    var consoleBinFolder = sourceFolder + Directory("Captura.Console/bin") + Directory(configuration);
    var uiBinFolder = sourceFolder + Directory("Captura/bin") + Directory(configuration);
    
    // Copy Languages
    CopyDirectory(uiBinFolder + Directory("Languages"), distFolder + Directory("languages"));

    // Copy executables and config files
    CopyFiles(consoleBinFolder.Path + "/*.exe*", distFolder);
    CopyFiles(uiBinFolder.Path + "/*.exe*", distFolder);

    // Copy Keymap files
    CopyDirectory(uiBinFolder + Directory("keymaps"), distFolder + Directory("keymaps"));

    // For Debug builds
    if (configuration != Release)
    {
        // Assemblies, Symbol Files and XML Documentation
        foreach (var extension in new [] { ".dll", ".pdb", ".xml" })
        {
            CopyFiles(consoleBinFolder.Path + "/*" + extension, distFolder);
            CopyFiles(uiBinFolder.Path + "/*" + extension, distFolder);
        }
    }
    else
    {
        CopyDirectory(consoleBinFolder + Directory("lib"), distFolder + Directory("lib"));
        CopyDirectory(uiBinFolder + Directory("lib"), distFolder + Directory("lib"));
    }
}

void PackChoco(string Tag, string Version)
{
    var checksum = CalculateFileHash(PortablePath).ToHex();

    var chocoInstallScript = chocoFolder + File("tools/chocolateyinstall.ps1");

    var originalContent = FileRead(chocoInstallScript);

    var newContent = $"$tag = '{Tag}'; $checksum = '{checksum}'; {originalContent}";

    CreateBackup(chocoInstallScript, tempFolder + File("cinst.ps1"));

    FileWrite(chocoInstallScript, newContent);

    ChocolateyPack(chocoFolder + File("captura.nuspec"), new ChocolateyPackSettings
    {
        Version = Version,
        ArgumentCustomization = Args => Args.Append($"--outputdirectory {tempFolder}")
    });
}
#endregion

#region Setup / Teardown
Setup(context =>
{
    EnsureDirectoryExists(tempFolder);
    EnsureDirectoryExists(distFolder);

    HandleTag();

    HandleVersion();

    EmbedApiKeys();
});

Teardown(context =>
{
    RestoreBackups();
});
#endregion

#region Tasks
var cleanTask = Task("Clean").Does(() =>
{
    MSBuild(slnPath, settings =>
    {
        settings.SetConfiguration(configuration)
            .SetVerbosity(Verbosity.Minimal)
            .WithTarget("Clean");
    });
});

var nugetRestoreTask = Task("Nuget-Restore").Does(() => NuGetRestore(slnPath));

var buildTask = Task("Build")
    .IsDependentOn(nugetRestoreTask)
    .Does(() =>
{
    MSBuild(slnPath, settings =>
    {
        settings.SetConfiguration(configuration)
            .SetVerbosity(Verbosity.Minimal)
            .WithTarget("Rebuild");
    });

    NativeRestore();

    CopyLicenses();
});

var cleanOutputTask = Task("Clean-Output").Does(() => CleanDirectory(distFolder));

var populateOutputTask = Task("Populate-Output")
    .IsDependentOn(cleanOutputTask)
    .IsDependentOn(buildTask)
    .Does(() => PopulateOutput());

var packPortableTask = Task("Pack-Portable")
    .IsDependentOn(populateOutputTask)
    .Does(() => Zip(distFolder, PortablePath));

var packSetupTask = Task("Pack-Setup")
    .WithCriteria(configuration == Release)
    .IsDependentOn(populateOutputTask)
    .Does(() =>
{
    const string InnoScriptPath = "Inno.iss";

    InnoSetup(InnoScriptPath, new InnoSetupSettings
    {
        QuietMode = InnoSetupQuietMode.Quiet,
        ArgumentCustomization = Args => Args.Append($"/DMyAppVersion={version}")
    });
});

var deployGitHubTask = Task("Deploy-GitHub")
    .WithCriteria(deploy)
    .IsDependentOn(packPortableTask)
    .IsDependentOn(packSetupTask)
    .Does(() => 
{
    var releaseNotesPath = tempFolder + File("release_notes.md");
    const string changelogUrl = "https://mathewsachin.github.io/Captura/changelog";

    FileWrite(releaseNotesPath, $"[Changelog]({changelogUrl})");

    const string RepoOwner = "MathewSachin";
    const string RepoName = "Captura";

    GitReleaseManagerCreate(RepoOwner,
        EnvironmentVariable("git_key"),
        RepoOwner,
        RepoName,
        new GitReleaseManagerCreateSettings
        {
            Name = $"Captura {tag}",
            InputFilePath = releaseNotesPath,
            Prerelease = prerelease,
            Assets = $"{PortablePath},{SetupPath}"
        });

    GitReleaseManagerPublish(RepoOwner,
        EnvironmentVariable("git_key"),
        RepoOwner,
        RepoName,
        tag);
});

var packChocoTask = Task("Pack-Choco")
    .WithCriteria(deploy)
    .IsDependentOn(packPortableTask)
    .IsDependentOn(deployGitHubTask)
    .Does(() => PackChoco(tag, chocoVersion));

var deployChocoTask = Task("Deploy-Choco")
    .WithCriteria(deploy)
    .IsDependentOn(packChocoTask)
    .IsDependentOn(deployGitHubTask)
    .Does(() =>
{
    ChocolateyPush(ChocoPkgPath, new ChocolateyPushSettings
    {
        ApiKey = EnvironmentVariable("choco_key")
    });
});

var testTask = Task("Test")
    .IsDependentOn(buildTask)
    .Does(() => XUnit2(sourceFolder + File($"Tests/bin/{configuration}/Captura.Tests.dll")));

var defaultTask = Task("Default").IsDependentOn(populateOutputTask);

var installInnoTask = Task("Install-Inno")
    .WithCriteria(configuration == Release)
    .Does(() => ChocolateyInstall("innosetup", new ChocolateyInstallSettings
    {
        ArgumentCustomization = Args => Args.Append("--no-progress")
    }));
#endregion

#region AppVeyor
class Artifact
{
    public Artifact(string Name, string Path)
    {
        this.Name = Name;
        this.Path = Path;
    }

    public string Name { get; }
    public string Path { get; }
}

var artifacts = new []
{
    new Artifact("Portable", PortablePath),
    new Artifact("Setup", SetupPath),
    new Artifact("Chocolatey", ChocoPkgPath)
};

Task("CI")
    .WithCriteria(AppVeyor.IsRunningOnAppVeyor)
    .IsDependentOn(testTask)
    .IsDependentOn(packPortableTask)
    .IsDependentOn(installInnoTask)
    .IsDependentOn(packSetupTask)
    .IsDependentOn(packChocoTask)
    .IsDependentOn(deployGitHubTask)
    .IsDependentOn(deployChocoTask)
    .Does(() =>
{
    if (deploy)
    {
        AppVeyor.UpdateBuildVersion($"{version}.{buildNo}");
    }

    foreach (var artifact in artifacts)
    {
        if (FileExists(artifact.Path))
        {
            AppVeyor.UploadArtifact(artifact.Path, new AppVeyorUploadArtifactsSettings
            {
                DeploymentName = artifact.Name
            });
        }
    }
});
#endregion

// Start
RunTarget(target);