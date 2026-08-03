using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using WindowsGSH.Core.Modules;
using WindowsGSH.Core.Readiness;
using WindowsGSH.Core.Servers;

namespace WindowsGSH.Modules.Smalland;

public sealed class SmallandModule :
    ManifestBackedGameServerModule,
    IModuleExistingServerImportCapability,
    IModuleReadinessCapability
{
    private const string ConfigFileName = "smalland_conf.json";
    private const string StartScriptName = "start-server.bat";
    private const string ExecutablePath = "SMALLAND/Binaries/Win64/SMALLANDServer-Win64-Shipping.exe";
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public override ModuleCapabilities Capabilities => Manifest.ToCapabilities(
        supportsQuery: false,
        supportsRcon: false);

    public override ServerDisplayInfo GetDisplayInfo(ServerInstance instance)
    {
        return new ServerDisplayInfo(
            IpAddress: GetSetting(instance, "network.publicIp", "0.0.0.0"),
            Port: GetSetting(instance, "network.port", "7777"),
            MaxPlayers: GetSetting(instance, "server.maxPlayers", "16"));
    }

    public override Task<InstallPlan> CreateInstallPlanAsync(ServerInstance instance, CancellationToken cancellationToken)
    {
        return Task.FromResult(new InstallPlan(
            "steamcmd",
            $"+force_install_dir \"{instance.InstallPath}\" +login anonymous +app_update 808040 validate +quit",
            instance.InstallPath,
            [
                "Smalland dedicated server Steam app: 808040.",
                "WindowsGSH writes smalland_conf.json before start.",
                "The module reads Epic Online Services values from the shipped start-server.bat when present."
            ]));
    }

    public override async Task<ProcessStartInfo> CreateStartInfoAsync(ServerInstance instance, CancellationToken cancellationToken)
    {
        await WriteConfigFileSettingsAsync(instance, cancellationToken).ConfigureAwait(false);

        var executable = Path.Combine(instance.InstallPath, NormalizePath(ExecutablePath));
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = instance.InstallPath,
            Arguments = BuildLaunchArguments(instance),
            UseShellExecute = !ConsoleInputStrategyPolicy.UsesRedirectedStreams(Runtime),
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardInput = ConsoleInputStrategyPolicy.UsesRedirectedStreams(Runtime),
            RedirectStandardOutput = ConsoleInputStrategyPolicy.UsesRedirectedStreams(Runtime),
            RedirectStandardError = ConsoleInputStrategyPolicy.UsesRedirectedStreams(Runtime)
        };

        return startInfo;
    }

    public override bool IsInstallValid(ServerInstance instance)
    {
        return File.Exists(Path.Combine(instance.InstallPath, NormalizePath(ExecutablePath)));
    }

    public override string? GetConsoleLogPath(ServerInstance instance)
    {
        return Path.Combine(instance.InstallPath, "SMALLAND", "Saved", "Logs");
    }

    protected override string BuildLaunchArguments(ServerInstance instance)
    {
        var config = LoadConfig(instance);
        var settings = instance.Settings;
        var args = new StringBuilder();

        args.Append("/Game/Maps/WorldGame/WorldGame_Smalland");
        args.Append("?SERVERNAME=").Append(QuoteUrlOption(GetSetting(instance, "server.name", "Smalland Dedicated Server")));
        args.Append("?WORLDNAME=").Append(QuoteUrlOption(GetSetting(instance, "server.worldName", "World")));

        var password = GetSetting(settings, "server.password", GetNodeString(config, "Password", string.Empty));
        if (!string.IsNullOrWhiteSpace(password))
        {
            args.Append("?PASSWORD=").Append(QuoteUrlOption(password));
        }

        AppendFlag(args, "FRIENDLYFIRE", GetBool(settings, "game.friendlyFire", GetNodeBool(config, "FriendlyFire", false)));
        AppendFlag(args, "PEACEFULMODE", GetBool(settings, "game.peacefulMode", GetNodeBool(config, "PeacefullMode", false)));
        AppendFlag(args, "KEEPINVENTORY", GetBool(settings, "game.keepInventory", GetNodeBool(config, "KeepInventory", false)));
        AppendFlag(args, "NODETERIORATION", GetBool(settings, "game.noDeterioration", GetNodeBool(config, "NoDeterioration", false)));
        AppendFlag(args, "PRIVATE", GetBool(settings, "server.private", GetNodeBool(config, "Private", false)));

        args.Append("?lengthofdayseconds=").Append(GetInt(settings, "game.lengthOfDaySeconds", GetNodeInt(config, "LengthOfDaySeconds", 1800)));
        args.Append("?lengthofseasonseconds=").Append(GetInt(settings, "game.lengthOfSeasonSeconds", GetNodeInt(config, "LengthOfSeasonSeconds", 10800)));
        args.Append("?creaturehealthmodifier=").Append(GetInt(settings, "game.creatureHealthModifier", GetNodeInt(config, "CreatureHealthModifier", 100)));
        args.Append("?creaturedamagemodifier=").Append(GetInt(settings, "game.creatureDamageModifier", GetNodeInt(config, "CreatureDamageModifier", 100)));
        args.Append("?nourishmentlossmodifier=").Append(GetInt(settings, "game.nourishmentLossModifier", GetNodeInt(config, "NourishmentLossModifier", 100)));
        args.Append("?falldamagemodifier=").Append(GetInt(settings, "game.fallDamageModifier", GetNodeInt(config, "FalldamageModifier", 100)));

        var engineKeys = ReadEngineKeys(instance.InstallPath);
        var deploymentId = GetNodeStringOrFallback(config, "DeploymentId", engineKeys.DeploymentId);
        var clientId = GetNodeStringOrFallback(config, "ClientId", engineKeys.ClientId);
        var clientSecret = GetNodeStringOrFallback(config, "ClientSecret", engineKeys.ClientSecret);
        var privateKey = GetNodeStringOrFallback(config, "PrivateKey", engineKeys.PrivateKey);

        args.Append(" -ini:Engine:[EpicOnlineServices]:DeploymentId=").Append(deploymentId);
        args.Append(" -ini:Engine:[EpicOnlineServices]:DedicatedServerClientId=").Append(clientId);
        args.Append(" -ini:Engine:[EpicOnlineServices]:DedicatedServerClientSecret=").Append(clientSecret);
        if (!string.IsNullOrWhiteSpace(privateKey))
        {
            args.Append(" -ini:Engine:[EpicOnlineServices]:DedicatedServerPrivateKey=").Append(privateKey);
        }

        args.Append(" -port=").Append(GetInt(settings, "network.port", 7777));

        if (GetBool(settings, "launch.log", true))
        {
            args.Append(" -log");
        }

        var additionalArguments = SanitizeAdditionalArguments(GetSetting(settings, "server.additionalArguments", string.Empty));
        if (!string.IsNullOrWhiteSpace(additionalArguments))
        {
            args.Append(' ').Append(additionalArguments);
        }

        return args.ToString();
    }

    public override Task<IReadOnlyDictionary<string, object?>> ReadConfigFileSettingsAsync(
        ServerInstance instance,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var settings = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        var config = LoadConfig(instance);

        CopyString(config, settings, "Password", "server.password");
        CopyBool(config, settings, "FriendlyFire", "game.friendlyFire");
        CopyBool(config, settings, "PeacefullMode", "game.peacefulMode");
        CopyBool(config, settings, "KeepInventory", "game.keepInventory");
        CopyBool(config, settings, "NoDeterioration", "game.noDeterioration");
        CopyBool(config, settings, "Private", "server.private");
        CopyNumber(config, settings, "LengthOfDaySeconds", "game.lengthOfDaySeconds");
        CopyNumber(config, settings, "LengthOfSeasonSeconds", "game.lengthOfSeasonSeconds");
        CopyNumber(config, settings, "CreatureHealthModifier", "game.creatureHealthModifier");
        CopyNumber(config, settings, "CreatureDamageModifier", "game.creatureDamageModifier");
        CopyNumber(config, settings, "NourishmentLossModifier", "game.nourishmentLossModifier");
        CopyNumber(config, settings, "FalldamageModifier", "game.fallDamageModifier");
        return Task.FromResult<IReadOnlyDictionary<string, object?>>(settings);
    }

    public override Task WriteConfigFileSettingsAsync(ServerInstance instance, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var config = LoadConfigForWrite(instance);
        var engineKeys = ReadEngineKeys(instance.InstallPath);

        SetString(config, "Password", GetSetting(instance.Settings, "server.password", GetNodeString(config, "Password", string.Empty)));
        SetBool(config, "FriendlyFire", GetBool(instance.Settings, "game.friendlyFire", GetNodeBool(config, "FriendlyFire", false)));
        SetBool(config, "PeacefullMode", GetBool(instance.Settings, "game.peacefulMode", GetNodeBool(config, "PeacefullMode", false)));
        SetBool(config, "KeepInventory", GetBool(instance.Settings, "game.keepInventory", GetNodeBool(config, "KeepInventory", false)));
        SetBool(config, "NoDeterioration", GetBool(instance.Settings, "game.noDeterioration", GetNodeBool(config, "NoDeterioration", false)));
        SetBool(config, "Private", GetBool(instance.Settings, "server.private", GetNodeBool(config, "Private", false)));
        SetNumber(config, "LengthOfDaySeconds", GetInt(instance.Settings, "game.lengthOfDaySeconds", GetNodeInt(config, "LengthOfDaySeconds", 1800)));
        SetNumber(config, "LengthOfSeasonSeconds", GetInt(instance.Settings, "game.lengthOfSeasonSeconds", GetNodeInt(config, "LengthOfSeasonSeconds", 10800)));
        SetNumber(config, "CreatureHealthModifier", GetInt(instance.Settings, "game.creatureHealthModifier", GetNodeInt(config, "CreatureHealthModifier", 100)));
        SetNumber(config, "CreatureDamageModifier", GetInt(instance.Settings, "game.creatureDamageModifier", GetNodeInt(config, "CreatureDamageModifier", 100)));
        SetNumber(config, "NourishmentLossModifier", GetInt(instance.Settings, "game.nourishmentLossModifier", GetNodeInt(config, "NourishmentLossModifier", 100)));
        SetNumber(config, "FalldamageModifier", GetInt(instance.Settings, "game.fallDamageModifier", GetNodeInt(config, "FalldamageModifier", 100)));
        SetString(config, "DeploymentId", GetNodeStringOrFallback(config, "DeploymentId", engineKeys.DeploymentId));
        SetString(config, "ClientId", GetNodeStringOrFallback(config, "ClientId", engineKeys.ClientId));
        SetString(config, "ClientSecret", GetNodeStringOrFallback(config, "ClientSecret", engineKeys.ClientSecret));
        SetString(config, "PrivateKey", GetNodeStringOrFallback(config, "PrivateKey", engineKeys.PrivateKey));

        var configPath = GetConfigPath(instance.InstallPath);
        Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
        File.WriteAllText(configPath, config.ToJsonString(JsonOptions) + Environment.NewLine, Utf8NoBom);
        return Task.CompletedTask;
    }

    public bool CanImport(string path)
    {
        return ResolveImportInstallPath(path) != null;
    }

    public async Task<ModuleExistingServerImportProbe> PreviewImportAsync(
        string path,
        CancellationToken cancellationToken)
    {
        var installPath = ResolveImportInstallPath(path) ?? path;
        var settings = GetConfigFields().ToDictionary(
            field => field.Key,
            field => field.DefaultValue,
            StringComparer.OrdinalIgnoreCase);

        var probe = new ServerInstance(
            "smalland-import",
            Path.GetFileName(installPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
            Id,
            installPath,
            installPath,
            GetConfigPath(installPath),
            settings);

        foreach (var pair in await ReadConfigFileSettingsAsync(probe, cancellationToken).ConfigureAwait(false))
        {
            settings[pair.Key] = pair.Value;
        }

        foreach (var pair in ReadStartScriptSettings(installPath))
        {
            settings[pair.Key] = pair.Value;
        }

        var warnings = new List<string>();
        if (!File.Exists(GetConfigPath(installPath)))
        {
            warnings.Add("smalland_conf.json was not found. WindowsGSH will create one from module defaults on first start.");
        }

        if (!File.Exists(GetStartScriptPath(installPath)))
        {
            warnings.Add("start-server.bat was not found. Epic Online Services values could not be imported from the shipped script.");
        }

        return new ModuleExistingServerImportProbe(
            GetSetting(settings, "server.name", Path.GetFileName(installPath)),
            installPath,
            settings,
            warnings);
    }

    public Task<IReadOnlyList<ReadinessCheckResult>> CheckReadinessAsync(
        ServerInstance instance,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var checks = new List<ReadinessCheckResult>();
        var executablePath = Path.Combine(instance.InstallPath, NormalizePath(ExecutablePath));
        checks.Add(File.Exists(executablePath)
            ? ReadinessCheckResult.Pass("Smalland executable", $"Found: {executablePath}")
            : ReadinessCheckResult.Fail("Smalland executable", $"Missing SMALLANDServer-Win64-Shipping.exe. Run install/update with SteamCMD app 808040."));

        var configPath = GetConfigPath(instance.InstallPath);
        checks.Add(File.Exists(configPath)
            ? ReadinessCheckResult.Pass("Smalland config", $"Found: {configPath}")
            : ReadinessCheckResult.Info("Smalland config", "smalland_conf.json will be created before the server starts."));

        if (File.Exists(configPath) && !TryParseConfig(configPath, out var configError))
        {
            checks.Add(ReadinessCheckResult.Fail(
                "Smalland config JSON",
                $"smalland_conf.json could not be parsed: {configError}"));
        }

        var startScriptPath = GetStartScriptPath(instance.InstallPath);
        if (File.Exists(startScriptPath))
        {
            checks.Add(ReadinessCheckResult.Pass("Smalland start script", $"Found: {startScriptPath}"));
        }
        else if (HasRequiredEngineKeys(LoadConfig(instance)))
        {
            checks.Add(ReadinessCheckResult.Pass("Smalland EOS config", "Required Epic Online Services values are present in smalland_conf.json."));
        }
        else
        {
            checks.Add(ReadinessCheckResult.Warning("Smalland EOS config", "start-server.bat was not found and smalland_conf.json is missing required Epic Online Services values."));
        }

        return Task.FromResult<IReadOnlyList<ReadinessCheckResult>>(checks);
    }

    private static JsonObject LoadConfig(ServerInstance instance)
    {
        return LoadConfig(instance.InstallPath);
    }

    private static JsonObject LoadConfig(string installPath)
    {
        var path = GetConfigPath(installPath);
        return File.Exists(path) ? ParseConfigFile(path) : CreateDefaultConfig();
    }

    private static JsonObject LoadConfigForWrite(ServerInstance instance)
    {
        var path = GetConfigPath(instance.InstallPath);
        return File.Exists(path) ? ParseConfigFile(path) : CreateDefaultConfig();
    }

    private static JsonObject ParseConfigFile(string path)
    {
        try
        {
            return JsonNode.Parse(File.ReadAllText(path)) as JsonObject
                ?? throw new InvalidDataException($"Smalland config file root must be a JSON object: {path}");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException(
                $"Smalland config file is not valid JSON and was not modified: {path}",
                ex);
        }
    }

    private static bool TryParseConfig(string path, out string error)
    {
        try
        {
            if (JsonNode.Parse(File.ReadAllText(path)) is not JsonObject)
            {
                error = "Root value must be a JSON object.";
                return false;
            }

            error = string.Empty;
            return true;
        }
        catch (JsonException ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static JsonObject CreateDefaultConfig()
    {
        return new JsonObject
        {
            ["Password"] = string.Empty,
            ["FriendlyFire"] = false,
            ["PeacefullMode"] = false,
            ["KeepInventory"] = false,
            ["NoDeterioration"] = false,
            ["Private"] = false,
            ["LengthOfDaySeconds"] = 1800,
            ["LengthOfSeasonSeconds"] = 10800,
            ["CreatureHealthModifier"] = 100,
            ["CreatureDamageModifier"] = 100,
            ["NourishmentLossModifier"] = 100,
            ["FalldamageModifier"] = 100,
            ["DeploymentId"] = string.Empty,
            ["ClientId"] = string.Empty,
            ["ClientSecret"] = string.Empty,
            ["PrivateKey"] = string.Empty
        };
    }

    private static bool HasRequiredEngineKeys(JsonObject config)
    {
        return !string.IsNullOrWhiteSpace(GetNodeString(config, "DeploymentId", string.Empty)) &&
               !string.IsNullOrWhiteSpace(GetNodeString(config, "ClientId", string.Empty)) &&
               !string.IsNullOrWhiteSpace(GetNodeString(config, "ClientSecret", string.Empty));
    }

    private static Dictionary<string, object?> ReadStartScriptSettings(string installPath)
    {
        var settings = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        var values = ReadBatchSetValues(GetStartScriptPath(installPath));
        if (values.Count == 0)
        {
            return settings;
        }

        SetIfPresent(settings, values, "SERVERNAME", "server.name");
        SetIfPresent(settings, values, "WORLDNAME", "server.worldName");
        SetIfPresent(settings, values, "PASSWORD", "server.password");
        SetBoolIfPresent(settings, values, "FRIENDLYFIRE", "game.friendlyFire");
        SetBoolIfPresent(settings, values, "PEACEFULMODE", "game.peacefulMode");
        SetBoolIfPresent(settings, values, "KEEPINVENTORY", "game.keepInventory");
        SetBoolIfPresent(settings, values, "NODETERIORATION", "game.noDeterioration");
        SetBoolIfPresent(settings, values, "PRIVATE", "server.private");
        SetIntIfPresent(settings, values, "LENGTHOFDAYSECONDS", "game.lengthOfDaySeconds");
        SetIntIfPresent(settings, values, "LENGTHOFSEASONSECONDS", "game.lengthOfSeasonSeconds");
        SetIntIfPresent(settings, values, "CREATUREHEALTHMODIFIER", "game.creatureHealthModifier");
        SetIntIfPresent(settings, values, "CREATUREDAMAGEMODIFIER", "game.creatureDamageModifier");
        SetIntIfPresent(settings, values, "NOURISHMENTLOSSMODIFIER", "game.nourishmentLossModifier");
        SetIntIfPresent(settings, values, "FALLDAMAGEMODIFIER", "game.fallDamageModifier");
        SetIntIfPresent(settings, values, "PORT", "network.port");
        return settings;
    }

    private static EngineKeys ReadEngineKeys(string installPath)
    {
        var values = ReadBatchSetValues(GetStartScriptPath(installPath));
        return new EngineKeys(
            GetBatchValue(values, "DEPLOYMENTID"),
            GetBatchValue(values, "CLIENTID"),
            GetBatchValue(values, "CLIENTSECRET"),
            GetBatchValue(values, "PRIVATEKEY"));
    }

    private static Dictionary<string, string> ReadBatchSetValues(string path)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(path))
        {
            return values;
        }

        foreach (var line in File.ReadLines(path))
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith("set ", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var assignment = trimmed[4..];
            var index = assignment.IndexOf('=');
            if (index <= 0)
            {
                continue;
            }

            values[assignment[..index].Trim()] = assignment[(index + 1)..].Trim().Trim('"');
        }

        return values;
    }

    private static string GetBatchValue(IReadOnlyDictionary<string, string> values, string key)
    {
        return values.TryGetValue(key, out var value) ? value : string.Empty;
    }

    private static void SetIfPresent(
        Dictionary<string, object?> settings,
        IReadOnlyDictionary<string, string> values,
        string sourceKey,
        string targetKey)
    {
        if (values.TryGetValue(sourceKey, out var value))
        {
            settings[targetKey] = value;
        }
    }

    private static void SetBoolIfPresent(
        Dictionary<string, object?> settings,
        IReadOnlyDictionary<string, string> values,
        string sourceKey,
        string targetKey)
    {
        if (values.TryGetValue(sourceKey, out var value))
        {
            settings[targetKey] = ToBool(value, false);
        }
    }

    private static void SetIntIfPresent(
        Dictionary<string, object?> settings,
        IReadOnlyDictionary<string, string> values,
        string sourceKey,
        string targetKey)
    {
        if (values.TryGetValue(sourceKey, out var value) &&
            int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            settings[targetKey] = parsed;
        }
    }

    private static void AppendFlag(StringBuilder builder, string flagName, bool enabled)
    {
        if (enabled)
        {
            builder.Append('?').Append(flagName);
        }
    }

    private static void CopyString(JsonObject source, Dictionary<string, object?> target, string sourceKey, string targetKey)
    {
        if (source[sourceKey] is JsonValue value && value.TryGetValue<string>(out var parsed))
        {
            target[targetKey] = parsed;
        }
    }

    private static void CopyBool(JsonObject source, Dictionary<string, object?> target, string sourceKey, string targetKey)
    {
        if (source[sourceKey] is JsonValue value && value.TryGetValue<bool>(out var parsed))
        {
            target[targetKey] = parsed;
        }
    }

    private static void CopyNumber(JsonObject source, Dictionary<string, object?> target, string sourceKey, string targetKey)
    {
        if (source[sourceKey] is JsonValue value && value.TryGetValue<int>(out var parsed))
        {
            target[targetKey] = parsed;
        }
    }

    private static void SetString(JsonObject obj, string key, string value) => obj[key] = value;

    private static void SetBool(JsonObject obj, string key, bool value) => obj[key] = value;

    private static void SetNumber(JsonObject obj, string key, int value) => obj[key] = value;

    private static string GetNodeString(JsonObject obj, string key, string fallback)
    {
        return obj[key] is JsonValue value && value.TryGetValue<string>(out var parsed) ? parsed : fallback;
    }

    private static string GetNodeStringOrFallback(JsonObject obj, string key, string fallback)
    {
        var value = GetNodeString(obj, key, string.Empty);
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }

    private static bool GetNodeBool(JsonObject obj, string key, bool fallback)
    {
        return obj[key] is JsonValue value && value.TryGetValue<bool>(out var parsed) ? parsed : fallback;
    }

    private static int GetNodeInt(JsonObject obj, string key, int fallback)
    {
        return obj[key] is JsonValue value && value.TryGetValue<int>(out var parsed) ? parsed : fallback;
    }

    private static bool GetBool(IReadOnlyDictionary<string, object?> settings, string key, bool fallback)
    {
        if (!settings.TryGetValue(key, out var value) || value == null)
        {
            return fallback;
        }

        return ToBool(value, fallback);
    }

    private static int GetInt(IReadOnlyDictionary<string, object?> settings, string key, int fallback)
    {
        if (!settings.TryGetValue(key, out var value) || value == null)
        {
            return fallback;
        }

        return int.TryParse(value.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : fallback;
    }

    private static bool ToBool(object value, bool fallback)
    {
        return value switch
        {
            bool boolean => boolean,
            string text when string.Equals(text, "1", StringComparison.OrdinalIgnoreCase) => true,
            string text when string.Equals(text, "0", StringComparison.OrdinalIgnoreCase) => false,
            string text when bool.TryParse(text, out var parsed) => parsed,
            int number => number != 0,
            long number => number != 0,
            _ => fallback
        };
    }

    private static string SanitizeAdditionalArguments(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var remaining = new List<string>();
        var tokens = SplitCommandLine(value);
        for (var i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];
            if (IsModuleManagedArgument(token))
            {
                if (!token.Contains('=') &&
                    i + 1 < tokens.Count &&
                    !tokens[i + 1].StartsWith("-", StringComparison.Ordinal))
                {
                    i++;
                }

                continue;
            }

            remaining.Add(token);
        }

        return string.Join(' ', remaining);
    }

    private static bool IsModuleManagedArgument(string token)
    {
        var argumentName = token.Split('=', 2)[0];
        return argumentName.Equals("-port", StringComparison.OrdinalIgnoreCase) ||
               argumentName.Equals("-log", StringComparison.OrdinalIgnoreCase) ||
               argumentName.Equals("-ini:Engine:[EpicOnlineServices]:DeploymentId", StringComparison.OrdinalIgnoreCase) ||
               argumentName.Equals("-ini:Engine:[EpicOnlineServices]:DedicatedServerClientId", StringComparison.OrdinalIgnoreCase) ||
               argumentName.Equals("-ini:Engine:[EpicOnlineServices]:DedicatedServerClientSecret", StringComparison.OrdinalIgnoreCase) ||
               argumentName.Equals("-ini:Engine:[EpicOnlineServices]:DedicatedServerPrivateKey", StringComparison.OrdinalIgnoreCase) ||
               token.StartsWith("/Game/Maps/WorldGame/WorldGame_Smalland", StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<string> SplitCommandLine(string value)
    {
        var tokens = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;
        foreach (var c in value)
        {
            if (c == '"')
            {
                inQuotes = !inQuotes;
                current.Append(c);
                continue;
            }

            if (char.IsWhiteSpace(c) && !inQuotes)
            {
                AddToken(tokens, current);
                continue;
            }

            current.Append(c);
        }

        AddToken(tokens, current);
        return tokens;
    }

    private static void AddToken(List<string> tokens, StringBuilder current)
    {
        if (current.Length == 0)
        {
            return;
        }

        tokens.Add(current.ToString());
        current.Clear();
    }

    private static string QuoteUrlOption(string value)
    {
        return "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
    }

    private static string? ResolveImportInstallPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            return null;
        }

        var candidates = new[]
        {
            path,
            Path.Combine(path, "serverfiles")
        };

        return candidates.FirstOrDefault(candidate => File.Exists(Path.Combine(candidate, NormalizePath(ExecutablePath))));
    }

    private static string GetConfigPath(string installPath) => Path.Combine(installPath, ConfigFileName);

    private static string GetStartScriptPath(string installPath) => Path.Combine(installPath, StartScriptName);

    private static string NormalizePath(string path)
    {
        return path.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
    }

    private sealed record EngineKeys(string DeploymentId, string ClientId, string ClientSecret, string PrivateKey);
}
