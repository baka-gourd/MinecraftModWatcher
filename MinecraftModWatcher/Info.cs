using System.Text.Json.Serialization;

namespace MinecraftModWatcher;

/// <summary>
/// 
/// </summary>
public class Info
{
    /// <summary>
    /// 
    /// </summary>
    public string? FileName { get; set; }
    /// <summary>
    /// 
    /// </summary>
    public bool Staged { get; set; }
    /// <summary>
    /// 
    /// </summary>
    public bool Deleted { get; set; }
    /// <summary>
    /// 
    /// </summary>
    public byte[]? Md5 { get; set; }
    /// <summary>
    /// 
    /// </summary>
    public long FileId { get; set; }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="obj"></param>
    /// <returns></returns>
    public override bool Equals(object? obj)
    {
        if (obj is not Info info) return false;
        return string.Concat(Md5!.Select(m => m.ToString("X2"))) == string.Concat(info.Md5!.Select(m => m.ToString("X2")));
    }

    /// <summary>
    /// 
    /// </summary>
    /// <returns></returns>
    public override int GetHashCode()
    {
        // ReSharper disable once BaseObjectGetHashCodeCallInGetHashCode
        return base.GetHashCode();
    }
}


/// <summary>
/// 
/// </summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
[JsonSerializable(typeof(Dictionary<long, List<Info>>))]
public partial class InfoContext : JsonSerializerContext
{

}