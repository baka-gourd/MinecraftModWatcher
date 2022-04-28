using System.Text.Json.Serialization;

using CurseforgeManifestsGenerator;

namespace MinecraftModWatcher;

public class Conf
{
    public string? ApiKey { get; set; }
    public string? WatchFolder { get; set; }
    public string? StageFolder { get; set; }
    public Manifest? Manifest { get; set; }
    public string MinecraftFolder { get; set; }
    public string[] IgnoreDirectories { get; set; }
    public string[] IgnoreFiles { get; set; }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
[JsonSerializable(typeof(Conf))]
public partial class ConfContext : JsonSerializerContext
{

}