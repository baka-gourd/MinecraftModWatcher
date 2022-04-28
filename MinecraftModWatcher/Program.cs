using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

using CurseforgeManifestsGenerator;

using MinecraftModWatcher;

using Polly;

using Serilog;

class Program
{
    /// <summary>
    /// 
    /// </summary>
    /// <param name="forceRegenerate">If true, will regenerate infos.</param>
    /// <param name="downloadOnly">Download mod only.</param>
    /// <param name="export">Export manifest.</param>
    /// <returns></returns>
    /// <exception cref="NullReferenceException"></exception>
    public static async Task Main(bool forceRegenerate = false, bool downloadOnly = false, bool export = false)
    {
        Log.Logger = new LoggerConfiguration().Enrich.FromLogContext().MinimumLevel.Information().WriteTo.Console()
            .CreateLogger();

        //load config.
        var w = new Watcher(downloadOnly, forceRegenerate);

        await w.Start();
        if (export)
        {
            w.Export();
            return;
        }

        if (!downloadOnly)
        {
            w.StartWatch();
        }

        if (!downloadOnly)
        {
            while (true) Console.ReadLine();
        }
    }
}
