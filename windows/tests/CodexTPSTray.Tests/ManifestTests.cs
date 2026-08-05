using System.IO;
using System.Xml.Linq;
using Xunit;

namespace CodexTPSTray.Tests;

public class ManifestTests
{
    private static string GetProjectDirectory()
    {
        string baseDir = AppContext.BaseDirectory;
        DirectoryInfo? dir = new DirectoryInfo(baseDir);
        for (int i = 0; i < 5 && dir != null; i++)
        {
            dir = dir.Parent;
        }
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "src", "CodexTPSTray");
    }

    private static readonly XNamespace AsmV1Ns = "urn:schemas-microsoft-com:asm.v1";
    private static readonly XNamespace AsmV2Ns = "urn:schemas-microsoft-com:asm.v2";
    private static readonly XNamespace AsmV3Ns = "urn:schemas-microsoft-com:asm.v3";
    private static readonly XNamespace DpiAwareNs2005 = "http://schemas.microsoft.com/SMI/2005/WindowsSettings";
    private static readonly XNamespace DpiAwareNs2016 = "http://schemas.microsoft.com/SMI/2016/WindowsSettings";

    [Fact]
    public void ProjectFile_ReferencesApplicationManifest()
    {
        string projectDir = GetProjectDirectory();
        string csprojPath = Path.Combine(projectDir, "CodexTPSTray.csproj");

        Assert.True(File.Exists(csprojPath), $"Project file not found at {csprojPath}");
        string csprojContent = File.ReadAllText(csprojPath);

        Assert.Contains("<ApplicationManifest>app.manifest</ApplicationManifest>", csprojContent);
    }

    [Fact]
    public void Manifest_XmlIsValidAndContainsPerMonitorV2()
    {
        string projectDir = GetProjectDirectory();
        string manifestPath = Path.Combine(projectDir, "app.manifest");

        Assert.True(File.Exists(manifestPath), $"Application manifest not found at {manifestPath}");

        var doc = XDocument.Load(manifestPath);
        var root = doc.Root;
        Assert.NotNull(root);
        Assert.Equal(AsmV1Ns + "assembly", root!.Name);

        var application = root.Element(AsmV3Ns + "application");
        Assert.NotNull(application);

        var windowsSettings = application?.Element(AsmV3Ns + "windowsSettings");
        Assert.NotNull(windowsSettings);

        // Modern PerMonitorV2 declaration (Windows 10+)
        var dpiAwareness = windowsSettings?.Element(DpiAwareNs2016 + "dpiAwareness");
        Assert.NotNull(dpiAwareness);
        Assert.Contains("PerMonitorV2", dpiAwareness!.Value);

        // Legacy Per-Monitor DPI for Windows 8.1 fallback
        var dpiAware = windowsSettings?.Element(DpiAwareNs2005 + "dpiAware");
        Assert.NotNull(dpiAware);
        Assert.Contains("true/pm", dpiAware!.Value);
    }

    [Fact]
    public void Manifest_UsesAsInvoker_NoUACPrompt()
    {
        string projectDir = GetProjectDirectory();
        string manifestPath = Path.Combine(projectDir, "app.manifest");

        Assert.True(File.Exists(manifestPath));

        var doc = XDocument.Load(manifestPath);
        var root = doc.Root;
        Assert.NotNull(root);

        var trustInfo = root?.Element(AsmV2Ns + "trustInfo");
        Assert.NotNull(trustInfo);

        var security = trustInfo?.Element(AsmV2Ns + "security");
        Assert.NotNull(security);

        var requestedPrivileges = security?.Element(AsmV3Ns + "requestedPrivileges");
        Assert.NotNull(requestedPrivileges);

        var requestedExecutionLevel = requestedPrivileges?.Element(AsmV3Ns + "requestedExecutionLevel");
        Assert.NotNull(requestedExecutionLevel);

        var levelAttr = requestedExecutionLevel?.Attribute("level");
        Assert.NotNull(levelAttr);
        Assert.Equal("asInvoker", levelAttr!.Value);

        var uiAccessAttr = requestedExecutionLevel?.Attribute("uiAccess");
        Assert.NotNull(uiAccessAttr);
        Assert.Equal("false", uiAccessAttr!.Value);
    }
}
