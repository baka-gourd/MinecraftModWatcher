using System.Text.Json.Serialization;

namespace MinecraftModWatcher;

public class Info
{
    public string? FileName { get; set; }
    public bool Staged { get; set; }
    public long ProjectId { get; set; }
    public long FileId { get; set; }
}


[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
[JsonSerializable(typeof(List<Info>))]
public partial class InfoContext : JsonSerializerContext
{

}