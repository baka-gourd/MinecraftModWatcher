using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

using CurseforgeManifestsGenerator;

using MinecraftModWatcher;

using Serilog;
using Serilog.Core;

class Program
{
    public static async Task Main(string[] args)
    {
        Log.Logger = new LoggerConfiguration().Enrich.FromLogContext().MinimumLevel.Information().WriteTo.Console()
            .CreateLogger();

        Log.Logger.Information("Welcome to ModWatcher!");

        //load config.
        InitResource();
        var conf = new Conf();
        LoadConf(ref conf);

        //init dictionary
        var infos = new Dictionary<long, List<Info>>();

        //get all files md5
        var filesMd5 = Directory.GetFiles(conf.WatchFolder!).ToList().Select(f => string.Concat(MD5.HashData(File.ReadAllBytes(f)).Select(m => m.ToString("X2")))).ToList();

        if (!File.Exists("./ModWatcher/info.json"))
        {
            await File.WriteAllTextAsync("./ModWatcher/info.json", JsonSerializer.Serialize(infos, InfoContext.Default.DictionaryInt64ListInfo));
            foreach (var file in Directory.GetFiles(conf.WatchFolder!))
            {
                OnFileAct(new object(), new FileSystemEventArgs(WatcherChangeTypes.Created, Path.GetFullPath(conf.WatchFolder!), Path.GetFileName(file)), UpdateInfos);
                Thread.Sleep(1000);
            }
        }
        else
        {
            infos = JsonSerializer.Deserialize(await File.ReadAllTextAsync("./ModWatcher/info.json"), InfoContext.Default.DictionaryInt64ListInfo)!;

            //download mods
            var httpCli = new HttpClient();
            httpCli.DefaultRequestHeaders.Add("x-api-key", conf.ApiKey!);

            foreach (var (projectId, info) in infos)
            {
                foreach (var i in info)
                {
                    if (filesMd5.Contains(string.Concat(i.Md5!.Select(m => m.ToString("X2")))))
                    {
                        //delete mod
                        if (i.Deleted)
                        {
                            File.Delete(Path.Combine(conf.WatchFolder!, i.FileName!));
                        }
                        continue;
                    }

                    if (i.Deleted)
                    {
                        continue;
                    }

                    if (i.Staged)
                    {
                        File.Copy(Path.Combine(conf.StageFolder!, Path.GetFileName(i.FileName!)), Path.Combine(conf.WatchFolder!, Path.GetFileName(i.FileName!)), true);
                    }
                    else
                    {
                        var downloadUrl = JsonNode.Parse(
                            httpCli.GetStringAsync($"https://api.curseforge.com/v1/mods/{projectId}/files/{i.FileId}/download-url").Result)![
                            "data"];
                        Log.Logger.Information("Download {0}...", i.FileName!);
                        await File.WriteAllBytesAsync(Path.Combine(conf.WatchFolder!, Path.GetFileName(i.FileName!)), httpCli.GetByteArrayAsync(downloadUrl!.GetValue<string>()).Result);
                        Thread.Sleep(1000);
                    }
                }
            }
        }

        var watcher = new FileSystemWatcher(conf.WatchFolder!, "*.*") { EnableRaisingEvents = true };
        watcher.NotifyFilter = NotifyFilters.Attributes | NotifyFilters.CreationTime | NotifyFilters.DirectoryName | NotifyFilters.FileName | NotifyFilters.LastAccess
                               | NotifyFilters.LastWrite | NotifyFilters.Security | NotifyFilters.Size;
        watcher.Changed += (s, e) => OnFileAct(s, e, UpdateInfos);
        watcher.Created += (s, e) => OnFileAct(s, e, UpdateInfos);
        watcher.Deleted += (s, e) => OnFileAct(s, e, UpdateInfos);
        watcher.Renamed += OnFileRename;

        void UpdateInfos(long p, Info input, bool del, byte[] bytes)
        {
            var info = infos.FirstOrDefault(pair => pair.Value.Exists(i => i.FileName == input.FileName));
            if (del)
            {
                var tmp = info!.Value.FirstOrDefault(inf => inf.FileName == input.FileName);
                info.Value.Remove(tmp!);
                tmp!.Deleted = true;
                info.Value.Add(tmp);
                if (File.Exists(Path.Combine(conf!.StageFolder!, input.FileName!)))
                {
                    File.Delete(Path.Combine(conf.StageFolder!, input.FileName!));
                }
                infos[info.Key] = info.Value;
                File.WriteAllText("./ModWatcher/info.json", JsonSerializer.Serialize(infos, InfoContext.Default.DictionaryInt64ListInfo));
                return;
            }

            if (infos.TryGetValue(p, out var value))
            {
                if (value.Contains(input))
                {
                    if (!input.Deleted)
                    {
                        var old = info!.Value.FirstOrDefault(i => i.FileName == input.FileName);
                        value.Remove(old!);
                        old!.Deleted = false;
                        value.Add(old);

                        infos[p] = value;
                        File.WriteAllText("./ModWatcher/info.json", JsonSerializer.Serialize(infos, InfoContext.Default.DictionaryInt64ListInfo));
                    }
                    return;
                }
                value.Add(input);
                infos[p] = value;
                File.WriteAllText("./ModWatcher/info.json", JsonSerializer.Serialize(infos, InfoContext.Default.DictionaryInt64ListInfo));
                if (input.Staged)
                {
                    File.WriteAllBytes(Path.Combine(conf.StageFolder!, input.FileName!), bytes);
                }
                return;
            }

            infos.Add(p, new List<Info>() { input });
            if (input.Staged)
            {
                File.WriteAllBytes(Path.Combine(conf.StageFolder!, input.FileName!), bytes);
            }
            File.WriteAllText("./ModWatcher/info.json", JsonSerializer.Serialize(infos, InfoContext.Default.DictionaryInt64ListInfo));
        }

        void OnFileAct(object s, FileSystemEventArgs e, Action<long, Info, bool, byte[]> action)
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
                var (projectId, fileId, stage) = GetFileInformationAsync(bytes, Path.GetFileName(file), conf.ApiKey!).Result;
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

        void OnFileRename(object s, RenamedEventArgs e)
        {
            var info = infos.FirstOrDefault(pair => pair.Value.Exists(i => i.FileName == Path.GetFileName(e.OldFullPath)));
            var old = info!.Value.FirstOrDefault(i => i.FileName == Path.GetFileName(e.OldFullPath));
            info.Value.Remove(old!);
            old!.FileName = Path.GetFileName(e.FullPath);
            info.Value.Add(old);

            infos[info.Key] = info.Value;

            File.WriteAllText("./ModWatcher/info.json", JsonSerializer.Serialize(infos, InfoContext.Default.DictionaryInt64ListInfo));
            if (File.Exists(Path.Combine(conf.StageFolder!, old.FileName!)))
            {
                File.Move(Path.Combine(conf.StageFolder!, Path.GetFileName(e.OldFullPath)), Path.Combine(conf.StageFolder!, Path.GetFileName(e.FullPath)), true);
            }
        }

        while (args.Length > 0)
        {
            Console.ReadLine();
        }
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
            File.WriteAllText("./ModWatcher/config.json", JsonSerializer.Serialize(new Conf { ApiKey = "", StageFolder = "./ModWatcher/stage", WatchFolder = "./mods" }, ConfContext.Default.Conf));
        }
    }

    private static void LoadConf(ref Conf config)
    {
        var c = JsonSerializer.Deserialize(File.ReadAllText("./ModWatcher/config.json"), ConfContext.Default.Conf);
        if (c is null)
        {
            Log.Logger.Error("Read config error!");
            return;
        }

        config = c;
    }

    private static async Task<(long, long, bool)> GetFileInformationAsync(byte[] bytes, string name, string apiKey)
    {
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
    }
}