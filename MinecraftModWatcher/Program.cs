using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

using CurseforgeManifestsGenerator;

using MinecraftModWatcher;

using Serilog;

var logger = new LoggerConfiguration().Enrich.FromLogContext()
    .MinimumLevel.Information()
    .WriteTo.Console()
    .CreateLogger();

logger.Information("Mod Watcher is running!");

void InitResource()
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

InitResource();

logger.Information("Load config...");

var conf = new Conf();

void LoadConf(ref Conf config)
{
    var c = JsonSerializer.Deserialize(File.ReadAllText("./ModWatcher/config.json"), ConfContext.Default.Conf);
    if (c is null)
    {
        logger.Error("Read config error!");
        return;
    }

    config = c;
}

LoadConf(ref conf);

var infos = new List<Info>();
if (!File.Exists("./ModWatcher/info.json"))
{
    File.WriteAllText("./ModWatcher/info.json", JsonSerializer.Serialize(infos, InfoContext.Default.ListInfo));
}
else
{
    infos = JsonSerializer.Deserialize(File.ReadAllText("./ModWatcher/info.json"), InfoContext.Default.ListInfo)!;
    foreach (var info in infos.Where(i => i.Staged))
    {
        if (File.Exists(Path.Combine(conf.WatchFolder!, info.FileName!)))
        {
            File.Copy(Path.Combine(conf.WatchFolder!, info.FileName!), Path.Combine(conf.StageFolder!, info.FileName!), true);
        }

        if (File.Exists(Path.Combine(conf.StageFolder!, info.FileName!)))
        {
            File.Copy(Path.Combine(conf.StageFolder!, info.FileName!), Path.Combine(conf.WatchFolder!, info.FileName!), true);
        }
    }

    var httpCli = new HttpClient();
    httpCli.DefaultRequestHeaders.Add("x-api-key", conf.ApiKey!);

    foreach (var info in infos.Where(i => !i.Staged))
    {
        if (File.Exists(Path.Combine(conf.WatchFolder!, info.FileName!)))
        {
            continue;
        }

        var name = JsonNode.Parse(httpCli.GetStringAsync($"https://api.curseforge.com/v1/mods/{info.ProjectId}").Result)!["data"]!["name"];
        var downloadUrl = JsonNode.Parse(
            httpCli.GetStringAsync($"https://api.curseforge.com/v1/mods/{info.ProjectId}/files/{info.FileId}/download-url").Result)![
            "data"];
        logger.Information("Download {0}...", name!.GetValue<string>());
        await File.WriteAllBytesAsync(Path.Combine(conf.WatchFolder!, Path.GetFileName(downloadUrl!.GetValue<string>())), httpCli.GetByteArrayAsync(downloadUrl.GetValue<string>()).Result);
        Thread.Sleep(1000);
    }
}

var watcher = new FileSystemWatcher(conf.WatchFolder!, "*.*") { EnableRaisingEvents = true };
watcher.NotifyFilter = NotifyFilters.Attributes | NotifyFilters.CreationTime | NotifyFilters.DirectoryName | NotifyFilters.FileName | NotifyFilters.LastAccess
                       | NotifyFilters.LastWrite | NotifyFilters.Security | NotifyFilters.Size;
watcher.Changed += (s, e) => OnFileAct(s, e, UpdateInfos);
watcher.Created += (s, e) => OnFileAct(s, e, UpdateInfos);
watcher.Deleted += (s, e) => OnFileAct(s, e, UpdateInfos);
watcher.Renamed += OnFileRename;

async Task<(long, long, bool)> GetFileInformationAsync(string fullPath, string apiKey)
{
    const string reqUrl = "https://api.curseforge.com/v1/fingerprints";
    var fingerPrint = MurmurHash2.HashNormal(File.ReadAllBytes(fullPath));
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
            logger!.Information("Cannot match {0} on CurseForge.", Path.GetFileName(fullPath));
            return (0, 0, true);
        }

        var match =
            json!.Data.ExactMatches.FirstOrDefault(match => match.File.FileName == Path.GetFileName(fullPath)) ?? json.Data.ExactMatches[0];

        return (match.Id, match.File.Id, false);
    }
    catch
    {
        logger!.Information("{0} is not on CurseForge!", Path.GetFileName(fullPath));
        return (0, 0, true);
    }
}

void UpdateInfos(Info i, bool del)
{
    if (del)
    {
        infos!.RemoveAll(info => info.FileName == i.FileName);
        if (File.Exists(Path.Combine(conf.StageFolder!, i.FileName!)))
        {
            File.Delete(Path.Combine(conf.StageFolder!, i.FileName!));
        }
        File.WriteAllText("./ModWatcher/info.json", JsonSerializer.Serialize(infos, InfoContext.Default.ListInfo));
        return;
    }
    var old = infos!.FirstOrDefault(info => info.FileName == i.FileName);
    if (old is null)
    {
        infos!.Add(i);
    }
    else
    {
        infos.Remove(old);
        infos!.Add(i);
    }

    File.WriteAllText("./ModWatcher/info.json", JsonSerializer.Serialize(infos, InfoContext.Default.ListInfo));
}

void OnFileAct(object s, FileSystemEventArgs e, Action<Info, bool> action)
{
    switch (e.ChangeType)
    {
        case WatcherChangeTypes.Changed:
            logger.Information("Changed: {0}", e.Name);
            break;
        case WatcherChangeTypes.Created:
            logger.Information("Created: {0}", e.Name);
            break;
        case WatcherChangeTypes.Deleted:
            logger.Information("Deleted: {0}", e.Name);
            break;
        case WatcherChangeTypes.Renamed:
            logger.Information("Renamed: {0}", e.Name);
            break;
    }

    var file = e.FullPath;

    if (e.ChangeType == WatcherChangeTypes.Deleted)
    {
        action.Invoke(new Info() { FileId = 0, FileName = Path.GetFileName(file), ProjectId = 0, Staged = false }, true);
        return;
    }

    if (Path.GetExtension(file) is ".jar")
    {
        var (projectId, fileId, stage) = GetFileInformationAsync(file, conf.ApiKey!).Result;
        if (stage)
        {
            action.Invoke(new Info() { FileId = 0, FileName = Path.GetFileName(file), ProjectId = 0, Staged = stage }, false);
        }
        else
        {
            action.Invoke(new Info() { FileId = fileId, FileName = Path.GetFileName(file), ProjectId = projectId, Staged = stage }, false);
        }
    }
    else
    {
        action.Invoke(new Info() { FileId = 0, FileName = Path.GetFileName(file), ProjectId = 0, Staged = true }, false);
    }
}

void OnFileRename(object s, RenamedEventArgs e)
{
    var old = infos!.FirstOrDefault(info => info.FileName == Path.GetFileName(e.OldFullPath));
    infos.Remove(old!);
    old!.FileName = Path.GetFileName(e.FullPath);
    infos!.Add(old);
    File.WriteAllText("./ModWatcher/info.json", JsonSerializer.Serialize(infos, InfoContext.Default.ListInfo));
    if (File.Exists(Path.Combine(conf.StageFolder!, old.FileName!)))
    {
        File.Move(Path.Combine(conf.StageFolder!, Path.GetFileName(e.OldFullPath)), Path.Combine(conf.StageFolder!, Path.GetFileName(e.FullPath)), true);
    }
}

while (true)
{
    Console.ReadLine();
}