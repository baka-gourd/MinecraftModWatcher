using System.Text.Json.Serialization;

namespace MinecraftModWatcher;

public class Info
{
    public string? FileName { get; set; }
    public bool Staged { get; set; }
    public bool Deleted { get; set; }
    public byte[]? Md5 { get; set; }
    public long FileId { get; set; }

    public override bool Equals(object? obj)
    {
        if (obj == null) return false;
        var info = obj as Info;
        if (info == null) return false;
        return string.Concat(Md5!.Select(m => m.ToString("X2"))) == string.Concat(info.Md5!.Select(m => m.ToString("X2")));
    }
}


[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
[JsonSerializable(typeof(Dictionary<long, List<Info>>))]
public partial class InfoContext : JsonSerializerContext
{

}