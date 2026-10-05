using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using WorkspaceLauncher.Models;

namespace WorkspaceLauncher.Services;

public sealed class WorkspaceStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "WorkspaceLauncher",
        "workspaces.json");

    public WorkspaceConfiguration LoadOrCreate()
    {
        if (!File.Exists(FilePath))
        {
            var defaultConfiguration = CreateDefaultConfiguration();
            Save(defaultConfiguration);
            return defaultConfiguration;
        }

        var json = File.ReadAllText(FilePath);
        using var document = JsonDocument.Parse(json);
        WorkspaceConfiguration configuration;
        if (document.RootElement.ValueKind == JsonValueKind.Array)
        {
            var legacyProfiles = JsonSerializer.Deserialize<List<WorkspaceProfile>>(json, JsonOptions);
            if (legacyProfiles is null || legacyProfiles.Any(profile => profile is null))
            {
                throw new InvalidDataException($"The workspace configuration is invalid: {FilePath}");
            }

            configuration = new WorkspaceConfiguration
            {
                Workspaces = new ObservableCollection<WorkspaceProfile>(legacyProfiles),
                LaunchSequence = new ObservableCollection<Guid>(legacyProfiles.Select(profile => profile.Id))
            };
        }
        else
        {
            configuration = JsonSerializer.Deserialize<WorkspaceConfiguration>(json, JsonOptions)
                ?? throw new InvalidDataException($"The workspace configuration is invalid: {FilePath}");
        }

        if (configuration.Workspaces is null || configuration.LaunchSequence is null ||
            configuration.Workspaces.Any(profile =>
                profile is null ||
                string.IsNullOrWhiteSpace(profile.Name) ||
                profile.DesktopIndex < 0 ||
                profile.Items is null ||
                profile.Items.Any(item => item is null)) ||
            configuration.Workspaces.Select(profile => profile.Id).Distinct().Count() != configuration.Workspaces.Count ||
            configuration.LaunchSequence.Distinct().Count() != configuration.LaunchSequence.Count ||
            configuration.LaunchSequence.Any(id => configuration.Workspaces.All(profile => profile.Id != id)))
        {
            throw new InvalidDataException($"The workspace configuration is invalid: {FilePath}");
        }

        return configuration;
    }

    public void Save(WorkspaceConfiguration configuration)
    {
        var directory = Path.GetDirectoryName(FilePath)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = FilePath + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(configuration, JsonOptions));
        File.Move(temporaryPath, FilePath, overwrite: true);
    }

    private static WorkspaceConfiguration CreateDefaultConfiguration()
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string githubDesktop = Path.Combine(localAppData, "GitHubDesktop", "GitHubDesktop.exe");
        string visualStudioCode = Path.Combine(localAppData, "Programs", "Microsoft VS Code", "Code.exe");

        var workspaces = new ObservableCollection<WorkspaceProfile>
        {
            new WorkspaceProfile
            {
                Name = "Admin",
                DesktopIndex = 0,
                Items =
                [
                    App("Outlook", "olk.exe", "olk"),
                    App("Microsoft Teams", "ms-teams.exe", "ms-teams"),
                    App("ManicTime", Path.Combine(programFiles, "ManicTime", "ManicTime.exe"), "ManicTimeClient", "ManicTime")
                ]
            },
            new WorkspaceProfile
            {
                Name = "Work",
                DesktopIndex = 1,
                Items =
                [
                    Website("GitHub", "https://github.com/orgs/ConsultLink/repositories"),
                    App("GitHub Desktop", File.Exists(githubDesktop) ? githubDesktop : "GitHubDesktop.exe", "GitHubDesktop"),
                    App("Visual Studio Code", File.Exists(visualStudioCode) ? visualStudioCode : "code", "Code"),
                    App("AnyLogic", Path.Combine(programFiles, "AnyLogic 8.9 Professional", "AnyLogic.exe"), "AnyLogic")
                ]
            },
            new WorkspaceProfile
            {
                Name = "Entertainment",
                DesktopIndex = 2,
                Items =
                [
                    App(
                        "YouTube Music",
                        Path.Combine(appData, "Microsoft", "Windows", "Start Menu", "Programs", "NovusTheory", "YouTube Music Desktop App.lnk"))
                ]
            }
        };
        return new WorkspaceConfiguration
        {
            Workspaces = workspaces,
            LaunchSequence = new ObservableCollection<Guid>(workspaces.Select(profile => profile.Id))
        };
    }

    private static LaunchItem App(string name, string target, string processName = "", string windowTitle = "")
    {
        return new LaunchItem
        {
            Kind = LaunchItemKind.Application,
            Name = name,
            Target = target,
            ProcessName = processName,
            WindowTitle = windowTitle
        };
    }

    private static LaunchItem Website(string name, string target)
    {
        return new LaunchItem
        {
            Kind = LaunchItemKind.Website,
            Name = name,
            Target = target
        };
    }
}
