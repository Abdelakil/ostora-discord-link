using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OstoraDiscordLink.Config;
using OstoraDiscordLink.Database;
using OstoraDiscordLink.Services;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Plugins;

namespace OstoraDiscordLink;

[PluginMetadata(Id = "ostora.discordlink", Version = "1.0.0", Name = "OSTORA Discord Link", Author = "OSTORA", Description = "Generate unique linking codes for Discord integration")]
public sealed partial class Plugin(ISwiftlyCore core) : BasePlugin(core)
{
    public static new ISwiftlyCore Core { get; private set; } = null!;

    internal IOptionsMonitor<PluginConfig> Config { get; private set; } = null!;
    internal CodeGenerationService CodeGenerationService { get; private set; } = null!;
    internal CommandService CommandService { get; private set; } = null!;
    internal DatabaseService DatabaseService { get; private set; } = null!;

    public override void Load(bool hotReload)
    {
        Core = base.Core;

        Config = BuildConfigService<PluginConfig>("config.json", "OstoraDiscordLink");

        // Initialize database service
        var config = Config.CurrentValue;
        Core.Logger.LogInformation("Raw config values - Database.Connection: '{DbConnection}', CodeSettings.ExpiryMinutes: {ExpiryMinutes}", 
            config.Database.Connection, config.CodeSettings.ExpiryMinutes);
        
        DatabaseService = new DatabaseService(
            Core, 
            config.Database.Connection,
            config.CodeSettings.ExpiryMinutes,
            config.Database.PurgeDays
        );

        // Initialize other services
        CodeGenerationService = new CodeGenerationService();
        CommandService = new CommandService(Core, Config, CodeGenerationService, DatabaseService);

        // Initialize database asynchronously
        Task.Run(async () => 
        {
            await DatabaseService.InitializeAsync();
            
            if (DatabaseService.IsEnabled)
            {
                var stats = await DatabaseService.GetStatsAsync();
                Core.Logger.LogInformation("Database Stats - Total codes: {TotalCodes}, Active: {ActiveCodes}, Linked: {LinkedCodes}, Links: {TotalLinks}", 
                    stats.TotalCodes, stats.ActiveCodes, stats.LinkedCodes, stats.TotalLinks);
                
                // Log database configuration for verification
                var config = Config.CurrentValue;
                Core.Logger.LogInformation("Database Configuration - Connection: '{Connection}', Expiry: {ExpiryMinutes}min, Purge: {PurgeDays}days", 
                    config.Database.Connection, config.CodeSettings.ExpiryMinutes, config.Database.PurgeDays);
            }
        });

        Core.Logger.LogInformation("OSTORA Discord Link plugin loaded successfully!");
    }

    private IOptionsMonitor<T> BuildConfigService<T>(string fileName, string sectionName) where T : class, new()
    {
        Core.Configuration
            .InitializeJsonWithModel<T>(fileName, sectionName)
            .Configure(cfg => cfg.AddJsonFile(Core.Configuration.GetConfigPath(fileName), optional: false, reloadOnChange: true));

        ServiceCollection services = new();
        services.AddSwiftly(Core)
            .AddOptions<T>()
            .BindConfiguration(sectionName);

        var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IOptionsMonitor<T>>();
    }

    public override void Unload()
    {
        Core.Logger.LogInformation("OSTORA Discord Link plugin unloaded!");
    }
}
