using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OstoraDiscordLink.Config;
using OstoraDiscordLink.Database;
using OstoraDiscordLink.Services;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Events;
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

    public override async void Load(bool hotReload)
    {
        Core = base.Core;

        Config = BuildConfigService<PluginConfig>("config.json", "OstoraDiscordLink");

        // Initialize database service
        var config = Config.CurrentValue;
        Core.Logger.LogInformation("Raw config values - Database.Connection: '{DbConnection}', CodeSettings.ExpiryMinutes: {ExpiryMinutes}", 
            config.Database.Connection, config.CodeSettings.ExpiryMinutes);
        
        Core.Logger.LogInformation("Full config dump - Command: '{Command}', UnlinkCommand: '{UnlinkCommand}', CodeLength: {CodeLength}, Prefix: '{Prefix}', DB: '{DB}', Expiry: {Expiry}, Purge: {PurgeDays}, GrantOnLink: {GrantOnLink}, RevokeOnUnlink: {RevokeOnUnlink}, Permission: '{Permission}'", 
            config.Command, config.UnlinkCommand, config.CodeLength, config.MessagePrefix, config.Database.Connection, config.CodeSettings.ExpiryMinutes, config.Database.PurgeDays, config.Permissions.GrantOnLink, config.Permissions.RevokeOnUnlink, config.Permissions.LinkedPermission);
        
        DatabaseService = new DatabaseService(
            Core, 
            config.Database.Connection,
            config.CodeSettings.ExpiryMinutes,
            config.Database.PurgeDays,
            Config
        );

        // Initialize other services
        CodeGenerationService = new CodeGenerationService();
        CommandService = new CommandService(Core, Config, CodeGenerationService, DatabaseService);

        // Initialize database service
        await DatabaseService.InitializeAsync();

        // Set up event handler for player connections to grant permissions
        Core.Registrator.Register(this);

        Core.Logger.LogInformation("OSTORA Discord Link plugin loaded successfully!");
    }

    [EventListener<EventDelegates.OnClientSteamAuthorize>]
    public void OnClientSteamAuthorize(IOnClientSteamAuthorizeEvent e)
    {
        Task.Run(async () =>
        {
            // Wait a bit for the player to fully connect
            await Task.Delay(5000);
            
            var player = Core.PlayerManager.GetPlayer(e.PlayerId);
            if (player == null) return;

            // Check if player has an existing Discord link
            var existingLink = await DatabaseService.GetLinkBySteamIdAsync(player.SteamID);
            if (existingLink != null)
            {
                var config = Config.CurrentValue;
                if (config?.Permissions?.GrantOnLink == true && !string.IsNullOrEmpty(config?.Permissions?.LinkedPermission))
                {
                    // Grant permission if they have a Discord link
                    await DatabaseService.GrantLinkedPermissionAsync(player.SteamID, config.Permissions.LinkedPermission);
                    Core.Logger.LogInformation("Granted Discord linked permission to connecting player {PlayerName} ({SteamId})", 
                        player.Controller.PlayerName, player.SteamID);
                }
            }
        });
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
