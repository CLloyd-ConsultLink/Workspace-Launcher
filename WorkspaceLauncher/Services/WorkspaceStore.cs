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

    public string FilePath { get; }

    public WorkspaceStore(string? filePath = null)
    {
        FilePath = filePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WorkspaceLauncher",
            "workspaces.json");
    }

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
                Workspaces = new ObservableCollection<WorkspaceProfile>(legacyProfiles)
            };
            AddLegacySequence(configuration, legacyProfiles.Select(profile => profile.Id), null);
        }
        else
        {
            configuration = JsonSerializer.Deserialize<WorkspaceConfiguration>(json, JsonOptions)
                ?? throw new InvalidDataException($"The workspace configuration is invalid: {FilePath}");
            bool hasSequences = document.RootElement.EnumerateObject()
                .Any(property => string.Equals(property.Name, "Sequences", StringComparison.OrdinalIgnoreCase));
            if (!hasSequences)
            {
                var legacy = JsonSerializer.Deserialize<LegacySequenceConfiguration>(json, JsonOptions)
                    ?? throw new InvalidDataException($"The workspace configuration is invalid: {FilePath}");
                if (legacy.LaunchSequence is null)
                {
                    throw new InvalidDataException($"The workspace configuration is invalid: {FilePath}");
                }
                AddLegacySequence(configuration, legacy.LaunchSequence, legacy.EndDesktopIndex);
            }
            else if (!document.RootElement.EnumerateObject()
                .Any(property => string.Equals(property.Name, "DefaultSequenceId", StringComparison.OrdinalIgnoreCase)))
            {
                configuration.DefaultSequenceId = configuration.SelectedSequenceId;
            }
        }

        NormalizeSelection(configuration);
        Validate(configuration);
        return configuration;
    }

    private void Validate(WorkspaceConfiguration configuration)
    {
        if (configuration.Workspaces is null || configuration.Sequences is null ||
            configuration.Workspaces.Any(profile =>
                profile is null ||
                profile.Id == Guid.Empty ||
                string.IsNullOrWhiteSpace(profile.Name) ||
                profile.DesktopIndex < 0 ||
                profile.Items is null ||
                profile.Items.Any(item => item is null)) ||
            configuration.Workspaces.Select(profile => profile.Id).Distinct().Count() != configuration.Workspaces.Count ||
            configuration.Sequences.Any(sequence =>
                sequence is null || sequence.Id == Guid.Empty ||
                string.IsNullOrWhiteSpace(sequence.Name) ||
                sequence.WorkspaceIds is null || sequence.EndDesktopIndex < 0 ||
                sequence.WorkspaceIds.Distinct().Count() != sequence.WorkspaceIds.Count ||
                sequence.WorkspaceIds.Any(id => configuration.Workspaces.All(profile => profile.Id != id))) ||
            configuration.Sequences.Select(sequence => sequence.Id).Distinct().Count() != configuration.Sequences.Count ||
            configuration.Sequences.Select(sequence => sequence.Name.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != configuration.Sequences.Count ||
            (configuration.SelectedSequenceId is Guid selectedId &&
             configuration.Sequences.All(sequence => sequence.Id != selectedId)) ||
            (configuration.DefaultSequenceId is Guid defaultId &&
             configuration.Sequences.All(sequence => sequence.Id != defaultId)))
        {
            throw new InvalidDataException($"The workspace configuration is invalid: {FilePath}");
        }
    }

    public void Save(WorkspaceConfiguration configuration)
    {
        NormalizeSelection(configuration);
        Validate(configuration);
        var directory = Path.GetDirectoryName(FilePath)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = FilePath + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(configuration, JsonOptions));
        File.Move(temporaryPath, FilePath, overwrite: true);
    }

    private static WorkspaceConfiguration CreateDefaultConfiguration()
    {
        return new WorkspaceConfiguration();
    }

    private static void AddLegacySequence(WorkspaceConfiguration configuration, IEnumerable<Guid> workspaceIds, int? endDesktopIndex)
    {
        var sequence = new LaunchSequence
        {
            Name = "Default sequence",
            WorkspaceIds = new ObservableCollection<Guid>(workspaceIds),
            EndDesktopIndex = endDesktopIndex
        };
        configuration.Sequences.Add(sequence);
        configuration.SelectedSequenceId = sequence.Id;
        configuration.DefaultSequenceId = sequence.Id;
    }

    private static void NormalizeSelection(WorkspaceConfiguration configuration)
    {
        HashSet<Guid> sequenceIds = configuration.Sequences.Select(sequence => sequence.Id).ToHashSet();
        if (configuration.SelectedSequenceId is Guid selectedId && !sequenceIds.Contains(selectedId))
        {
            configuration.SelectedSequenceId = null;
        }
        if (configuration.DefaultSequenceId is Guid defaultId && !sequenceIds.Contains(defaultId))
        {
            configuration.DefaultSequenceId = null;
        }
    }

    private sealed class LegacySequenceConfiguration
    {
        public ObservableCollection<Guid> LaunchSequence { get; set; } = [];
        public int? EndDesktopIndex { get; set; }
    }
}
