using System.Text.Json.Serialization;

namespace MinecraftModWatcher;

public class Conf
{
    public string? ApiKey { get; set; }
    public string? WatchFolder { get; set; }
    public string? StageFolder { get; set; }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
[JsonSerializable(typeof(Conf))]
public partial class ConfContext : JsonSerializerContext
{

}