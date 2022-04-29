using System.IO.Compression;
using CurseforgeManifestsGenerator;
using Polly;
using Serilog;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.IO;

namespace MinecraftModWatcher;

/// <summary>
/// 
/// </summary>
public class Watcher
{
    /// <summary>
    /// 
    /// </summary>
    private FileSystemWatcher? FileSystemWatcher { get; set; }
    /// <summary>
    /// 
    /// </summary>
    private bool DownloadOnly { get; set; }
    /// <summary>
    /// 
    /// </summary>
    private bool ForceRegenerate { get; set; }

    /// <summary>
    /// 
    /// </summary>
    private Conf? Conf { get; set; }

    /// <summary>
    /// 
    /// </summary>
    private Dictionary<long, List<Info>> Infos { get; set; } = new Dictionary<long, List<Info>>();

    /// <summary>
    /// 
    /// </summary>
    /// <param name="downloadOnly"></param>
    /// <param name="forceRegenerate"></param>
    public Watcher(bool downloadOnly, bool forceRegenerate)
    {
        DownloadOnly = downloadOnly;
        ForceRegenerate = forceRegenerate;
    }

    /// <summary>
    /// 
    /// </summary>
    /// <exception cref="NullReferenceException"></exception>
    public async Task Start()
    {
        InitResource();
        Conf = LoadConf();

        if (Conf is null)
        {
            throw new NullReferenceException("Config is null.");
        }

        Log.Logger.Information("Config loaded.");

        //init dictionary
        Infos = new Dictionary<long, List<Info>>();

        Log.Logger.Information("Prepare existed files...");
        //get all files md5
        var filesMd5 = Directory.GetFiles(Conf.WatchFolder!).ToList().Select(f => string.Concat(MD5.HashData(File.ReadAllBytes(f)).Select(m => m.ToString("X2")))).ToList();

        Log.Logger.Information("Done.");

        if (!File.Exists("./ModWatcher/info.json") || ForceRegenerate)
        {
            Log.Logger.Warning("Cannot find info, generating...");
            await File.WriteAllTextAsync("./ModWatcher/info.json", JsonSerializer.Serialize(Infos, InfoContext.Default.DictionaryInt64ListInfo));
            foreach (var file in Directory.GetFiles(Conf.WatchFolder!))
            {
                OnFileAct(new FileSystemEventArgs(WatcherChangeTypes.Created, Path.GetFullPath(Conf.WatchFolder!), Path.GetFileName(file)), UpdateInfos);
                Thread.Sleep(1000);
            }
        }
        else
        {
            Infos = JsonSerializer.Deserialize(await File.ReadAllTextAsync("./ModWatcher/info.json"), InfoContext.Default.DictionaryInt64ListInfo)!;

            Log.Logger.Information("info loaded.");
            //download mods
            var httpCli = new HttpClient();
            httpCli.DefaultRequestHeaders.Add("x-api-key", Conf.ApiKey!);

            foreach (var (projectId, info) in Infos)
            {
                foreach (var i in info)
                {
                    if (filesMd5.Contains(string.Concat(i.Md5!.Select(m => m.ToString("X2")))))
                    {
                        //delete mod
                        if (i.Deleted)
                        {
                            Log.Logger.Information("Deleting {0}", i.FileName);
                            File.Delete(Path.Combine(Conf.WatchFolder!, i.FileName!));
                        }
                        continue;
                    }

                    if (i.Deleted)
                    {
                        continue;
                    }

                    if (i.Staged)
                    {
                        File.Copy(Path.Combine(Conf.StageFolder!, Path.GetFileName(i.FileName!)), Path.Combine(Conf.WatchFolder!, Path.GetFileName(i.FileName!)), true);
                    }
                    else
                    {
                        var policy = Policy.Handle<AggregateException>().Or<HttpRequestException>().Or<TimeoutException>().Or<IOException>().WaitAndRetry(3, _ => new TimeSpan(0, 0, 3), (e, s, t, c) => { Log.Logger.Error("Download error. Retry in {sleepDuration}. {retry}/{max}", s, t, 3); });
                        await policy.Execute(async () =>
                        {
                            var downloadUrl = JsonNode.Parse(
                                httpCli.GetStringAsync($"https://api.curseforge.com/v1/mods/{projectId}/files/{i.FileId}/download-url").Result)![
                                "data"];
                            Log.Logger.Information("Download {0}...", i.FileName!);
                            await File.WriteAllBytesAsync(Path.Combine(Conf.WatchFolder!, Path.GetFileName(i.FileName!)), await httpCli.GetByteArrayAsync(downloadUrl!.GetValue<string>()));
                            Thread.Sleep(1000);
                        });
                    }
                }
            }
        }
    }

    /// <summary>
    /// 
    /// </summary>
    public async Task Export()
    {
        if (File.Exists("./ModWatcher/export.zip"))
        {
            File.Delete("./ModWatcher/export.zip");
        }
        await using var stream = File.Create("./ModWatcher/export.zip");
        var archive = new ZipArchive(stream, ZipArchiveMode.Update);
        var files = new List<string>();
        archive.Comment = "Auto generated zip.";

        var root = new DirectoryInfo(Conf!.MinecraftFolder);
        foreach (var directory in root.EnumerateDirectories())
        {
            if (Conf.IgnoreDirectories.Contains(directory.Name))
            {
                continue;
            }

            files.AddRange(Directory.GetFiles(directory.FullName, "*.*", SearchOption.AllDirectories).Where(s =>
            {
                foreach (var ignoreFile in Conf.IgnoreFiles)
                {
                    if (!s.Contains(ignoreFile))
                    {
                        return true;
                    }

                    return false;
                }

                return false;
            }));
        }

        files.AddRange(Directory.GetFiles(root.FullName, "*.*", SearchOption.TopDirectoryOnly).Where(s =>
        {
            foreach (var ignoreFile in Conf.IgnoreFiles)
            {
                if (!s.Contains(ignoreFile))
                {
                    return true;
                }

                return false;
            }

            return false;
        }));

        var mods = new List<CFile>();
        foreach (var (id, info) in Infos)
        {
            foreach (var i in info)
            {
                if (i.Deleted) continue;
                if (i.Staged)
                {
                    files.Add($"ModWatcher/stage/{i.FileName}");
                    continue;
                }
                var cf = new CFile() { ProjectId = id, FileRequired = true, FileId = i.FileId };
                mods.Add(cf);
            }
        }

        var outM = Conf!.Manifest;
        outM!.Files = mods.ToArray();
        await File.WriteAllTextAsync("./ModWatcher/manifest.json", JsonSerializer.Serialize(outM, new ManifestContext(new JsonSerializerOptions() { WriteIndented = true }).Manifest));

        archive.CreateEntryFromFile("./ModWatcher/manifest.json", "manifest.json");
        var prefix = Path.GetFullPath(Conf.MinecraftFolder);
        foreach (var file1 in files)
        {
            var inFile = "overrides/" + file1.Replace(prefix!, "").Replace("\\", "/").Replace("ModWatcher/stage", "mods");
            Log.Logger.Information("Add {0}", inFile);
            archive.CreateEntryFromFile(file1, inFile);
        }

        archive.Dispose();
    }
    /// <summary>
    /// 
    /// </summary>
    public void StartWatch()
    {
        FileSystemWatcher = new FileSystemWatcher(Conf!.WatchFolder!, "*.*")
        {
            EnableRaisingEvents = true,
            NotifyFilter = NotifyFilters.Attributes | NotifyFilters.CreationTime | NotifyFilters.DirectoryName | NotifyFilters.FileName | NotifyFilters.LastAccess
                           | NotifyFilters.LastWrite | NotifyFilters.Security | NotifyFilters.Size
        };
        FileSystemWatcher.Changed += (_, e) => OnFileAct(e, UpdateInfos);
        FileSystemWatcher.Created += (_, e) => OnFileAct(e, UpdateInfos);
        FileSystemWatcher.Deleted += (_, e) => OnFileAct(e, UpdateInfos);
        FileSystemWatcher.Renamed += OnFileRename;

        Log.Logger.Information("Watched folder.");
    }

    private static void InitResource()
    {
        if (!Directory.Exists("./ModWatcher"))
        {
            Directory.CreateDirectory("./ModWatcher");
        }

        if (!Directory.Exists("./ModWatcher/stage"))
        {
            Directory.CreateDirectory("./ModWatcher/stage");
        }

        if (!Directory.Exists("./mods"))
        {
            Directory.CreateDirectory("./mods");
        }

        if (!File.Exists("./ModWatcher/config.json"))
        {
            File.WriteAllText("./ModWatcher/config.json", JsonSerializer.Serialize(new Conf
            {
                ApiKey = "",
                StageFolder = "./ModWatcher/stage",
                WatchFolder = "./mods",
                Manifest = new Manifest()
                {
                    Author = "NULL",
                    Files = Array.Empty<CFile>(),
                    ManifestType = "minecraftModpack",
                    ManifestVersion = 1,
                    Minecraft = new Minecraft()
                    {
                        ModLoaders = new[] { new ModLoader() { Id = "forge-14.23.5.2855", Primary = true } },
                        Version = "1.12.2"
                    },
                    Name = "G",
                    Overrides = "overrides",
                    Version = "1.0"
                },
                IgnoreDirectories = new[] { "screenshots", "saves", "local", "logs", "fonts", "crash-reports", "caches", "cache", ".mixin.out", "mods", "ModWatcher" },
                IgnoreFiles = new[] { "usercache.json", "usernamecache.json" },
                MinecraftFolder = "./"
            }, ConfContext.Default.Conf));
        }
    }

    private static Conf? LoadConf()
    {
        var c = JsonSerializer.Deserialize(File.ReadAllText("./ModWatcher/config.json"), ConfContext.Default.Conf);
        if (c is null)
        {
            Log.Logger.Error("Read config error!");
            return null;
        }

        return c;
    }

    private static async Task<(long, long, bool)> GetFileInformationAsync(byte[] bytes, string name, string apiKey)
    {
        var policy = Policy.Handle<Exception>().WaitAndRetry(5, _ => new TimeSpan(0, 0, 3), (e, s, t, c) => { Log.Logger.Error("Fetch error. Retry in {sleepDuration}. {retry}/{max}", s, t, 5); });
        const string reqUrl = "https://api.curseforge.com/v1/fingerprints";
        var fingerPrint = MurmurHash2.HashNormal(bytes);
        var req = new HttpRequestMessage
        {
            Method = HttpMethod.Post,
            RequestUri = new Uri(reqUrl),
            Content = new StringContent(
                @"{
            ""fingerprints"": [
            replace
            ]
        }".Replace("replace", fingerPrint.ToString())
                , Encoding.UTF8, "application/json")
        };
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        req.Headers.Add("x-api-key", apiKey);
        var rtn = await policy.Execute(async () =>
        {
            var resp = await new HttpClient().SendAsync(req);
            try
            {
                var json = await JsonSerializer.DeserializeAsync(await resp.Content.ReadAsStreamAsync(), ResponseContext.Default.Response);
                if (json is null)
                {
                    Log.Logger!.Information("Cannot match {0} on CurseForge.", name);
                    return (0, 0, true);
                }

                var match =
                    json!.Data!.ExactMatches!.FirstOrDefault(match => match.File!.FileName == name) ?? json.Data!.ExactMatches![0];

                return (match.Id, match.File!.Id, false);
            }
            catch
            {
                Log.Logger!.Information("{0} is not on CurseForge!", name);
                return (0, 0, true);
            }
        });

        return rtn;
    }

    private void UpdateInfos(long p, Info input, bool del, byte[] bytes)
    {
        var info = Infos.FirstOrDefault(pair => pair.Value.Exists(i => i.FileName == input.FileName));
        if (del)
        {
            var tmp = info!.Value.FirstOrDefault(inf => inf.FileName == input.FileName);
            info.Value.Remove(tmp!);
            tmp!.Deleted = true;
            info.Value.Add(tmp);
            if (File.Exists(Path.Combine(Conf!.StageFolder!, input.FileName!)))
            {
                File.Delete(Path.Combine(Conf.StageFolder!, input.FileName!));
            }
            Infos[info.Key] = info.Value;
            File.WriteAllText("./ModWatcher/info.json", JsonSerializer.Serialize(Infos, InfoContext.Default.DictionaryInt64ListInfo));
            return;
        }

        if (Infos.TryGetValue(p, out var value))
        {
            if (value.Contains(input))
            {
                if (!input.Deleted)
                {
                    var old = info!.Value.FirstOrDefault(i => i.FileName == input.FileName);
                    value.Remove(old!);
                    old!.Deleted = false;
                    value.Add(old);

                    Infos[p] = value;
                    File.WriteAllText("./ModWatcher/info.json", JsonSerializer.Serialize(Infos, InfoContext.Default.DictionaryInt64ListInfo));
                }
                return;
            }
            value.Add(input);
            Infos[p] = value;
            File.WriteAllText("./ModWatcher/info.json", JsonSerializer.Serialize(Infos, InfoContext.Default.DictionaryInt64ListInfo));
            if (input.Staged)
            {
                File.WriteAllBytes(Path.Combine(Conf!.StageFolder!, input.FileName!), bytes);
            }
            return;
        }

        Infos.Add(p, new List<Info>() { input });
        if (input.Staged)
        {
            File.WriteAllBytes(Path.Combine(Conf!.StageFolder!, input.FileName!), bytes);
        }
        File.WriteAllText("./ModWatcher/info.json", JsonSerializer.Serialize(Infos, InfoContext.Default.DictionaryInt64ListInfo));
    }

    private void OnFileRename(object s, RenamedEventArgs e)
    {
        var info = Infos.FirstOrDefault(pair => pair.Value.Exists(i => i.FileName == Path.GetFileName(e.OldFullPath)));
        var old = info!.Value.FirstOrDefault(i => i.FileName == Path.GetFileName(e.OldFullPath));
        info.Value.Remove(old!);
        old!.FileName = Path.GetFileName(e.FullPath);
        info.Value.Add(old);

        Infos[info.Key] = info.Value;

        File.WriteAllText("./ModWatcher/info.json", JsonSerializer.Serialize(Infos, InfoContext.Default.DictionaryInt64ListInfo));
        if (File.Exists(Path.Combine(Conf!.StageFolder!, old.FileName!)))
        {
            File.Move(Path.Combine(Conf.StageFolder!, Path.GetFileName(e.OldFullPath)), Path.Combine(Conf!.StageFolder!, Path.GetFileName(e.FullPath)), true);
        }
    }

    private void OnFileAct(FileSystemEventArgs e, Action<long, Info, bool, byte[]> action)
    {
        switch (e.ChangeType)
        {
            case WatcherChangeTypes.Changed:
                Log.Logger.Information("Changed: {0}", e.Name);
                break;
            case WatcherChangeTypes.Created:
                Log.Logger.Information("Created: {0}", e.Name);
                break;
            case WatcherChangeTypes.Deleted:
                Log.Logger.Information("Deleted: {0}", e.Name);
                break;
            case WatcherChangeTypes.Renamed:
                Log.Logger.Information("Renamed: {0}", e.Name);
                break;
        }

        var file = e.FullPath;

        if (e.ChangeType == WatcherChangeTypes.Deleted)
        {
            action.Invoke(0, new Info() { FileId = 0, FileName = Path.GetFileName(file), Staged = false, Deleted = true, Md5 = Array.Empty<byte>() }, true, Array.Empty<byte>());
            return;
        }

        var bytes = File.ReadAllBytes(file);
        if (Path.GetExtension(file) is ".jar")
        {
            var (projectId, fileId, stage) = GetFileInformationAsync(bytes, Path.GetFileName(file), Conf!.ApiKey!).Result;
            if (stage)
            {
                action.Invoke(0, new Info() { FileId = 0, FileName = Path.GetFileName(file), Staged = stage, Deleted = false, Md5 = MD5.HashData(bytes) }, false, bytes);
            }
            else
            {
                action.Invoke(projectId, new Info() { FileId = fileId, FileName = Path.GetFileName(file), Staged = stage, Deleted = false, Md5 = MD5.HashData(bytes) }, false, bytes);
            }
        }
        else
        {
            action.Invoke(0, new Info() { FileId = 0, FileName = Path.GetFileName(file), Staged = true, Deleted = false, Md5 = MD5.HashData(bytes) }, false, bytes);
        }
    }
}