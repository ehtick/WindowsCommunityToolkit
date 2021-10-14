#module nuget:?package=Cake.LongPath.Module&version=1.0.1

#addin nuget:?package=Cake.FileHelpers&version=4.0.1
#addin nuget:?package=Cake.Powershell&version=1.0.1
#addin nuget:?package=Cake.GitVersioning&version=3.4.220

#tool nuget:?package=MSTest.TestAdapter&version=2.2.7
#tool nuget:?package=vswhere&version=2.8.4

using System;
using System.Linq;
using System.Text.RegularExpressions;

//////////////////////////////////////////////////////////////////////
// ARGUMENTS
//////////////////////////////////////////////////////////////////////

var target = Argument("target", "Default");
var configuration = Argument("configuration", "Release");

//////////////////////////////////////////////////////////////////////
// VERSIONS
//////////////////////////////////////////////////////////////////////

var inheritDocVersion = "2.5.2";

//////////////////////////////////////////////////////////////////////
// VARIABLES
//////////////////////////////////////////////////////////////////////

var baseDir = MakeAbsolute(Directory("../")).ToString();
var buildDir = baseDir + "/build";
var Solution = baseDir + "/Windows Community Toolkit.sln";
var toolsDir = buildDir + "/tools";

var binDir = baseDir + "/bin";
var nupkgDir = binDir + "/nupkg";

var taefBinDir = baseDir + $"/UITests/UITests.Tests.TAEF/bin/{configuration}/net5.0-windows10.0.19041/win10-x86";

var styler = toolsDir + "/XamlStyler.Console/tools/xstyler.exe";
var stylerFile = baseDir + "/settings.xamlstyler";

string Version = null;

var inheritDoc = toolsDir + "/InheritDoc/tools/InheritDoc.exe";

// Ignoring NerdBank until this is merged and we can use a new version of inheridoc:
// https://github.com/firesharkstudios/InheritDoc/pull/27
var inheritDocExclude = "Nerdbank.GitVersioning.ManagedGit.GitRepository";

//////////////////////////////////////////////////////////////////////
// METHODS
//////////////////////////////////////////////////////////////////////

void VerifyHeaders(bool Replace)
{
    var header = FileReadText("header.txt") + "\r\n";
    bool hasMissing = false;

    Func<IFileSystemInfo, bool> exclude_objDir =
        fileSystemInfo => !fileSystemInfo.Path.Segments.Contains("obj");

    var files = GetFiles(baseDir + "/**/*.cs", new GlobberSettings { Predicate = exclude_objDir }).Where(file =>
    {
        var path = file.ToString();
        return !(path.EndsWith(".g.cs") || path.EndsWith(".i.cs") || System.IO.Path.GetFileName(path).Contains("TemporaryGeneratedFile") || System.IO.Path.GetFullPath(path).Contains("Generated Files"));
    });

    Information("\nChecking " + files.Count() + " file header(s)");
    foreach(var file in files)
    {
        var oldContent = FileReadText(file);
        if(oldContent.Contains("// <auto-generated>"))
        {
           continue;
        }
        var rgx = new Regex("^(//.*\r?\n)*\r?\n");
        var newContent = header + rgx.Replace(oldContent, "");

        if(!newContent.Equals(oldContent, StringComparison.Ordinal))
        {
            if(Replace)
            {
                Information("\nUpdating " + file + " header...");
                FileWriteText(file, newContent);
            }
            else
            {
                Error("\nWrong/missing header on " + file);
                hasMissing = true;
            }
        }
    }

    if(!Replace && hasMissing)
    {
        throw new Exception("Please run UpdateHeaders.bat or '.\\build.ps1 -Target UpdateHeaders' and commit the changes.");
    }
}

void RetrieveVersion()
{
    Information("\nRetrieving version...");
    Version = GitVersioningGetVersion().NuGetPackageVersion;
    Information("\nBuild Version: " + Version);
}

void UpdateToolsPath(MSBuildSettings buildSettings)
{
    // Workaround for https://github.com/cake-build/cake/issues/2128
	var vsInstallation = VSWhereLatest(new VSWhereLatestSettings { Requires = "Microsoft.Component.MSBuild", IncludePrerelease = false });

	if (vsInstallation != null)
	{
		buildSettings.ToolPath = vsInstallation.CombineWithFilePath(@"MSBuild\Current\Bin\MSBuild.exe");
		if (!FileExists(buildSettings.ToolPath))
			buildSettings.ToolPath = vsInstallation.CombineWithFilePath(@"MSBuild\15.0\Bin\MSBuild.exe");
	}
}

//////////////////////////////////////////////////////////////////////
// DEFAULT TASK
//////////////////////////////////////////////////////////////////////

Task("Clean")
    .Description("Clean the output folder")
    .Does(() =>
{
    if(DirectoryExists(binDir))
    {
        Information("\nCleaning Working Directory");
        CleanDirectory(binDir);
    }
    else
    {
        CreateDirectory(binDir);
    }
});

Task("Verify")
    .Description("Run pre-build verifications")
    .IsDependentOn("Clean")
    .Does(() =>
{
    VerifyHeaders(false);

    StartPowershellFile("./Find-WindowsSDKVersions.ps1");
});

Task("Version")
    .Description("Updates the version information in all Projects")
    .Does(() =>
{
    RetrieveVersion();
});

Task("BuildProjects")
    .Description("Build all projects")
    .IsDependentOn("Version")
    .Does(() =>
{
    EnsureDirectoryExists(nupkgDir);

    Information("\nRestoring Solution Dependencies");

    var buildSettings = new MSBuildSettings
    {
        MaxCpuCount = 0,
        PlatformTarget = PlatformTarget.MSIL
    }
    .SetConfiguration(configuration)
    .WithTarget("Restore");
	
    UpdateToolsPath(buildSettings);

    MSBuild(Solution, buildSettings);

    Information("\nBuilding Solution");

    // Build once with normal dependency ordering
    buildSettings = new MSBuildSettings
    {
        MaxCpuCount = 0,
        PlatformTarget = PlatformTarget.MSIL
    }
    .SetConfiguration(configuration)
    .EnableBinaryLogger()
    .WithTarget("Build");

    UpdateToolsPath(buildSettings);

    MSBuild(Solution, buildSettings);
});

Task("InheritDoc")
    .Description("Updates <inheritdoc /> tags from base classes, interfaces, and similar methods")
    .IsDependentOn("BuildProjects")
    .Does(() =>
{
    Information("\nDownloading InheritDoc...");
    var installSettings = new NuGetInstallSettings
    {
        ExcludeVersion = true,
        Version = inheritDocVersion,
        OutputDirectory = toolsDir
    };

    NuGetInstall(new[] {"InheritDoc"}, installSettings);

    var args = new ProcessArgumentBuilder()
                .AppendSwitchQuoted("-b", baseDir)
                .AppendSwitch("-o", "")
                .AppendSwitchQuoted("-x", inheritDocExclude);

    var result = StartProcess(inheritDoc, new ProcessSettings { Arguments = args });

    if (result != 0)
    {
        throw new InvalidOperationException("InheritDoc failed!");
    }

    Information("\nFinished generating documentation with InheritDoc");
});

Task("Build")
    .Description("Build all projects runs InheritDoc")
    .IsDependentOn("Verify")
    .IsDependentOn("BuildProjects")
    .IsDependentOn("InheritDoc");

Task("Package")
    .Description("Pack the NuPkg")
    .Does(() =>
{
    // Invoke the pack target in the end
    var buildSettings = new MSBuildSettings
    {
        MaxCpuCount = 0,
        PlatformTarget = PlatformTarget.MSIL
    }
    .SetConfiguration(configuration)
    .WithTarget("Pack")
    .WithProperty("PackageOutputPath", nupkgDir);

    UpdateToolsPath(buildSettings);

    MSBuild(Solution, buildSettings);
});

public string getMSTestAdapterPath(){
    var nugetPaths = GetDirectories("./tools/MSTest.TestAdapter*/build/_common");

    if(nugetPaths.Count == 0){
        throw new Exception(
            "Cannot locate the MSTest test adapter. " +
            "You might need to add '#tool nuget:?package=MSTest.TestAdapter&version=2.2.7' " +
            "to the top of your build.cake file.");
    }

    return nugetPaths.Last().ToString();
}

Task("Test")
    .Description("Runs all Unit Tests")
    .Does(() =>
{
    Information("\nRunning Unit Tests");
    var vswhere = VSWhereLatest(new VSWhereLatestSettings
    {
        IncludePrerelease = false
    });

    var testSettings = new VSTestSettings
    {
        ToolPath = vswhere + "/Common7/IDE/CommonExtensions/Microsoft/TestWindow/vstest.console.exe",
        TestAdapterPath = getMSTestAdapterPath(),
        ArgumentCustomization = arg => arg.Append("/logger:trx;LogFileName=VsTestResultsUwp.trx /framework:FrameworkUap10 /Blame:CollectDump;DumpType=full --diag:diag.log"),
    };

    VSTest(baseDir + $"/**/{configuration}/**/UnitTests.*.appxrecipe", testSettings);
}).DoesForEach(GetFiles(baseDir + "/**/UnitTests.*NetCore.csproj"), (file) =>
{
    Information("\nRunning NetCore Unit Tests");
    var testSettings = new DotNetCoreTestSettings
    {
        Configuration = configuration,
        NoBuild = true,
        Loggers = new[] { "trx;LogFilePrefix=VsTestResults" },
        Verbosity = DotNetCoreVerbosity.Normal,
        ArgumentCustomization = arg => arg.Append($"-s {baseDir}/.runsettings /p:Platform=AnyCPU"),
    };
    DotNetCoreTest(file.FullPath, testSettings);
}).DoesForEach(GetFiles(baseDir + "/**/UnitTests.SourceGenerators.csproj"), (file) =>
{
    Information("\nRunning NetCore Source Generator Unit Tests");
    var testSettings = new DotNetCoreTestSettings
    {
        Configuration = configuration,
        NoBuild = true,
        Loggers = new[] { "trx;LogFilePrefix=VsTestResults" },
        Verbosity = DotNetCoreVerbosity.Normal,
        ArgumentCustomization = arg => arg.Append($"-s {baseDir}/.runsettings /p:Platform=AnyCPU"),
    };
    DotNetCoreTest(file.FullPath, testSettings);
}).DeferOnError();

Task("UITest")
	.Description("Runs all UI Tests")
    .Does(() =>
{
    var file = GetFiles(taefBinDir + "/UITests.Tests.TAEF.dll").FirstOrDefault();

    Information("\nRunning TAEF Interaction Tests");

    var result = StartProcess(System.IO.Path.GetDirectoryName(file.FullPath) + "/TE.exe", file.FullPath + " /screenCaptureOnError /enableWttLogging /logFile:UITestResults.wtl");
    if (result != 0)
    {
        throw new InvalidOperationException("TAEF Tests failed!");
    }
}).DeferOnError();

Task("SmokeTest")
    .Description("Runs all Smoke Tests")
    .IsDependentOn("Version")
    .Does(() =>
{
    // Need to do full NuGet restore here to grab proper UWP dependencies...
    NuGetRestore(baseDir + "/SmokeTests/SmokeTest.csproj");

    var buildSettings = new MSBuildSettings()
    {
        Restore = true,
    }
    .WithProperty("NuGetPackageVersion", Version);

    MSBuild(baseDir + "/SmokeTests/SmokeTests.proj", buildSettings);
}).DeferOnError();

Task("MSTestUITest")
    .Description("Runs UITests using MSTest")
    .DoesForEach(GetFiles(baseDir + "/**/UITests.*.MSTest.csproj"), (file) =>
{
    Information("\nRunning UI Interaction Tests");

    var testSettings = new DotNetCoreTestSettings
    {
        Configuration = configuration,
        NoBuild = true,
        Loggers = new[] { "trx;LogFilePrefix=VsTestResults" },
        Verbosity = DotNetCoreVerbosity.Normal
    };
    DotNetCoreTest(file.FullPath, testSettings);
});

//////////////////////////////////////////////////////////////////////
// TASK TARGETS
//////////////////////////////////////////////////////////////////////

Task("Default")
    .IsDependentOn("Build")
    .IsDependentOn("Test")
    //.IsDependentOn("UITest")
    .IsDependentOn("Package");

Task("UpdateHeaders")
    .Description("Updates the headers in *.cs files")
    .Does(() =>
{
    VerifyHeaders(true);
});

Task("StyleXaml")
    .Description("Ensures XAML Formatting is Clean")
    .Does(() =>
{
    Information("\nDownloading XamlStyler...");
    var installSettings = new NuGetInstallSettings
    {
        ExcludeVersion  = true,
        OutputDirectory = toolsDir
    };

    NuGetInstall(new[] {"xamlstyler.console"}, installSettings);

    Func<IFileSystemInfo, bool> exclude_objDir =
        fileSystemInfo => !fileSystemInfo.Path.Segments.Contains("obj");

    var files = GetFiles(baseDir + "/**/*.xaml", new GlobberSettings { Predicate = exclude_objDir });
    Information("\nChecking " + files.Count() + " file(s) for XAML Structure");
    foreach(var file in files)
    {
        StartProcess(styler, "-f \"" + file + "\" -c \"" + stylerFile + "\"");
    }
});

//////////////////////////////////////////////////////////////////////
// EXECUTION
//////////////////////////////////////////////////////////////////////

RunTarget(target);
