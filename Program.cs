using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
using Spectre.Console;

namespace RecRoomDownloader;

class Program
{
    const string AppId = "471710";
    const string DepotId = "471711";
    const string UsernameFile = "steam_username.txt";
    const string PasswordFile = "steam_password.txt";

    record BuildInfo(string BuildDate, string ManifestId)
    {
        public override string ToString() => $"{BuildDate} - {ManifestId}";
    }

    enum ModLoader
    {
        BepInEx,
        MelonLoader,
        Skip
    }

    static async Task Main()
    {
        var builds = await LoadBuilds();
        if (builds is not { Count: > 0 })
        {
            WriteError("Failed to fetch builds.");
            await Task.Delay(3000);
            return;
        }

        WriteInfo($"Loaded {builds.Count} builds.");

        var username = await LoadOrPrompt(UsernameFile, () =>
            AnsiConsole.Ask<string>("What's your [green]Steam username[/]?"));

        var password = await LoadOrPrompt(PasswordFile, () =>
            AnsiConsole.Prompt(new TextPrompt<string>("What's your [green]Steam password[/]?").Secret()));

        WriteInfo($"Hello, [green]{username}[/]!");

        var selectedBuild = AnsiConsole.Prompt(
            new SelectionPrompt<BuildInfo>()
                .Title("Select a [green]build[/]")
                .PageSize(8)
                .EnableSearch()
                .SearchPlaceholderText("Type to filter...")
                .AddChoices(builds));

        WriteInfo($"Selected build [green]{selectedBuild.BuildDate}[/]");
        WriteInfo($"Downloading manifest [green]{selectedBuild.ManifestId}[/]...");

        var folderName = ParseFolderName(selectedBuild.BuildDate);
        var outputDir = $"builds/{folderName}";

        await RunDepotDownloader(username, password, outputDir, selectedBuild.ManifestId);

        await File.WriteAllTextAsync($"{outputDir}/steam_appid.txt", AppId);
        WriteInfo($"Successfully downloaded [green]{selectedBuild.BuildDate}[/]!");

        if (await AnsiConsole.ConfirmAsync("Add .bat launch files? (screen/VR)"))
            await WriteLaunchScripts(outputDir);

        await InstallModLoader(outputDir);

        WriteInfo("Done!");
        await Task.Delay(3000);
    }

    static async Task<List<BuildInfo>?> LoadBuilds()
    {
        WriteInfo("Fetching [green]builds.json[/] from GitHub...");

        using var http = new HttpClient();
        http.DefaultRequestHeaders.Add("User-Agent", "RecRoomDownloader");

        try
        {
            var json = await http.GetStringAsync("https://raw.githubusercontent.com/TheIcy/RecRoomDownloader-Data/main/builds.json");
            return JsonSerializer.Deserialize<List<BuildInfo>>(json);
        }
        catch (HttpRequestException e)
        {
            WriteError($"Failed to fetch builds.json: {e.StatusCode}");
            return null;
        }
    }

    static async Task<string> LoadOrPrompt(string filePath, Func<string> prompt)
    {
        if (File.Exists(filePath))
        {
            WriteInfo($"Loaded cached value from [grey]{filePath}[/].");
            return await File.ReadAllTextAsync(filePath);
        }

        var value = prompt();
        await File.WriteAllTextAsync(filePath, value);
        return value;
    }

    static string ParseFolderName(string buildDate)
    {
        var date = DateTime.ParseExact(buildDate, "d MMMM yyyy - HH:mm:ss UTC", null);
        return date.ToString("d MMMM yyyy (HH-mm-ss UTC)");
    }

    static async Task RunDepotDownloader(string username, string password, string outputDir, string manifestId)
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "DepotDownloader.exe",
                Arguments =
                    $"-username {username} -password {password} -dir \"{outputDir}\" -app {AppId} -depot {DepotId} -manifest {manifestId} -max-downloads 64",
                RedirectStandardOutput = true,
                RedirectStandardInput = true
            }
        };

        process.Start();

        await AnsiConsole.Progress()
            .Columns(new ProgressBarColumn(), new PercentageColumn(), new SpinnerColumn())
            .StartAsync(async ctx =>
            {
                var task = ctx.AddTask("Downloading");
                task.IsIndeterminate = true;

                while (await process.StandardOutput.ReadLineAsync() is { } line)
                {
                    line = line.Trim();
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    if (line.Contains("Use the Steam Mobile App to confirm your sign in"))
                    {
                        task.IsIndeterminate = false;
                        task.StopTask();

                        AnsiConsole.MarkupLine(
                            "[yellow]⚠[/] Steam Guard: check your [green]Steam Mobile App[/] and tap [green]Approve[/].");
                        AnsiConsole.MarkupLine("[grey]Waiting for approval...[/]");

                        task = ctx.AddTask("Downloading");
                        task.IsIndeterminate = true;
                        continue;
                    }

                    if (line.Contains("Enter 2 factor auth code from your authenticator app"))
                    {
                        var code = AnsiConsole.Ask<string>("Enter [green]2FA code[/]:");
                        await process.StandardInput.WriteLineAsync(code);
                        continue;
                    }

                    if (line.Contains("Enter the code sent to your email address"))
                    {
                        var code = AnsiConsole.Ask<string>("Enter [green]Steam Guard email code[/]:");
                        await process.StandardInput.WriteLineAsync(code);
                        continue;
                    }

                    var match = Regex.Match(line, @"([\d.]+)%");
                    if (match.Success && double.TryParse(match.Groups[1].Value, out var percent))
                    {
                        task.IsIndeterminate = false;
                        task.Value = percent;
                    }
                }

                await process.WaitForExitAsync();
                task.StopTask();
            });
    }

    static async Task WriteLaunchScripts(string outputDir)
    {
        var exeName = File.Exists($"{outputDir}/RecRoom.exe") ? "RecRoom.exe" : "Recroom_Release.exe";

        await Task.WhenAll(
            File.WriteAllTextAsync($"{outputDir}/Launch_ScreenMode.bat",
                $"@echo off\nstart {exeName} +forcemode:screen"),
            File.WriteAllTextAsync($"{outputDir}/Launch_VR.bat", $"@echo off\nstart {exeName} +forcemode:vr")
        );

        WriteInfo("Created launch .bat files.");
    }

    static async Task InstallModLoader(string outputDir)
    {
        var choice = AnsiConsole.Prompt(
            new SelectionPrompt<ModLoader>()
                .Title("Install a [green]mod loader[/]?")
                .AddChoices(ModLoader.BepInEx, ModLoader.MelonLoader, ModLoader.Skip));

        switch (choice)
        {
            case ModLoader.BepInEx:
                await InstallBepInEx(outputDir);
                break;
            case ModLoader.MelonLoader:
                await InstallMelonLoader(outputDir);
                break;
            case ModLoader.Skip:
                WriteInfo("Skipping mod loader installation.");
                break;
        }
    }

    static async Task InstallBepInEx(string outputDir)
    {
        WriteInfo("Fetching latest [green]BepInEx[/] release from GitHub...");

        using var http = new HttpClient();
        http.DefaultRequestHeaders.Add("User-Agent", "RecRoomDownloader");

        var json = await http.GetStringAsync("https://api.github.com/repos/BepInEx/BepInEx/releases/latest");
        using var doc = JsonDocument.Parse(json);

        var asset = doc.RootElement
            .GetProperty("assets")
            .EnumerateArray()
            .FirstOrDefault(a => a.GetProperty("name").GetString() is { } name &&
                                 name.StartsWith("BepInEx_win_x64_", StringComparison.OrdinalIgnoreCase));

        if (asset.ValueKind == JsonValueKind.Undefined)
        {
            WriteError("Could not find BepInEx_win_x64_*.zip in the latest release assets.");
            return;
        }

        var downloadUrl = asset.GetProperty("browser_download_url").GetString()!;
        var version = doc.RootElement.GetProperty("tag_name").GetString();
        WriteInfo($"Downloading [green]BepInEx {version}[/]...");

        var zipBytes = await AnsiConsole.Progress()
            .Columns(new ProgressBarColumn(), new PercentageColumn(), new SpinnerColumn())
            .StartAsync(async ctx =>
            {
                var task = ctx.AddTask("Downloading BepInEx");

                using var response = await http.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
                var total = response.Content.Headers.ContentLength ?? -1L;
                await using var stream = await response.Content.ReadAsStreamAsync();
                using var ms = new MemoryStream();
                var buffer = new byte[81920];
                long downloaded = 0;
                int read;

                while ((read = await stream.ReadAsync(buffer)) > 0)
                {
                    await ms.WriteAsync(buffer.AsMemory(0, read));
                    downloaded += read;
                    if (total > 0)
                        task.Value = downloaded * 100.0 / total;
                }

                task.StopTask();
                return ms.ToArray();
            });

        WriteInfo("Extracting [green]BepInEx[/]...");
        await using var zip = new ZipArchive(new MemoryStream(zipBytes));
        await zip.ExtractToDirectoryAsync(outputDir, overwriteFiles: true);
        WriteInfo("BepInEx installed.");
    }

    static async Task InstallMelonLoader(string outputDir)
    {
        WriteInfo("Fetching latest [green]MelonLoader[/] release from GitHub...");

        using var http = new HttpClient();
        http.DefaultRequestHeaders.Add("User-Agent", "RecRoomDownloader");

        var json = await http.GetStringAsync("https://api.github.com/repos/LavaGang/MelonLoader/releases/latest");
        using var doc = JsonDocument.Parse(json);

        var asset = doc.RootElement
            .GetProperty("assets")
            .EnumerateArray()
            .FirstOrDefault(a => a.GetProperty("name").GetString() is { } name &&
                                 name.StartsWith("MelonLoader.x64.zip", StringComparison.OrdinalIgnoreCase));

        if (asset.ValueKind == JsonValueKind.Undefined)
        {
            WriteError("Could not find MelonLoader.x64.zip in the latest release assets.");
            return;
        }

        var downloadUrl = asset.GetProperty("browser_download_url").GetString()!;
        var version = doc.RootElement.GetProperty("tag_name").GetString();
        WriteInfo($"Downloading [green]MelonLoader {version}[/]...");

        var zipBytes = await AnsiConsole.Progress()
            .Columns(new ProgressBarColumn(), new PercentageColumn(), new SpinnerColumn())
            .StartAsync(async ctx =>
            {
                var task = ctx.AddTask("Downloading MelonLoader");

                using var response = await http.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
                var total = response.Content.Headers.ContentLength ?? -1L;
                await using var stream = await response.Content.ReadAsStreamAsync();
                using var ms = new MemoryStream();
                var buffer = new byte[81920];
                long downloaded = 0;
                int read;

                while ((read = await stream.ReadAsync(buffer)) > 0)
                {
                    await ms.WriteAsync(buffer.AsMemory(0, read));
                    downloaded += read;
                    if (total > 0)
                        task.Value = downloaded * 100.0 / total;
                }

                task.StopTask();
                return ms.ToArray();
            });

        WriteInfo("Extracting [green]MelonLoader[/]...");
        await using var zip = new ZipArchive(new MemoryStream(zipBytes));
        await zip.ExtractToDirectoryAsync(outputDir, overwriteFiles: true);
        WriteInfo("MelonLoader installed.");
    }

    static void WriteInfo(string msg) => AnsiConsole.MarkupLine($"[green]✓[/] {msg}");
    static void WriteError(string error) => AnsiConsole.MarkupLineInterpolated($"[bold red]✗ Error:[/] {error}");
}