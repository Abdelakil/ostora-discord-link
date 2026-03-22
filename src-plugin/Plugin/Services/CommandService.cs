using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OstoraDiscordLink.Config;
using OstoraDiscordLink.Database;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Misc;

namespace OstoraDiscordLink.Services;

public class CommandService
{
    private readonly ISwiftlyCore _core;
    private readonly IOptionsMonitor<PluginConfig> _config;
    private readonly CodeGenerationService _codeGenerationService;
    private readonly DatabaseService _databaseService;

    public CommandService(
        ISwiftlyCore core,
        IOptionsMonitor<PluginConfig> config,
        CodeGenerationService codeGenerationService,
        DatabaseService databaseService)
    {
        _core = core;
        _config = config;
        _codeGenerationService = codeGenerationService;
        _databaseService = databaseService;

        // Log current config values for debugging
        var currentConfig = _config.CurrentValue;
        _core.Logger.LogInformation("OSTORA Discord Link - Config loaded: Command='{Command}', CodeLength={CodeLength}, Prefix='{MessagePrefix}', Database='{DatabaseConnection}', Expiry={ExpiryMinutes}min", 
            currentConfig.Command, currentConfig.CodeLength, currentConfig.MessagePrefix, currentConfig.Database.Connection, currentConfig.CodeSettings.ExpiryMinutes);

        core.Registrator.Register(this);
    }

    [ClientChatHookHandler]
    public HookResult OnClientChat(int playerId, string text, bool teamOnly)
    {
        var player = _core.PlayerManager.GetPlayer(playerId);
        if (player == null || player.IsFakeClient)
            return HookResult.Continue;

        var config = _config.CurrentValue;
        
        if (!text.StartsWith(config.Command, StringComparison.OrdinalIgnoreCase))
            return HookResult.Continue;

        try
        {
            _core.Logger.LogInformation("Player {PlayerName} ({SteamId}) requested Discord link code", 
                player.Controller.PlayerName, player.SteamID);

            // Check if database is available
            if (!_databaseService.IsEnabled)
            {
                player.SendChat($"{config.MessagePrefix} Database is not available. Please contact an administrator.");
                _core.Logger.LogWarning("Database service is not enabled when player {SteamId} requested link code", player.SteamID);
                _core.Logger.LogError("DATABASE SERVICE DISABLED - Check database configuration and connection");
                return HookResult.Handled;
            }

            _core.Logger.LogDebug("Database service is enabled, proceeding with link request for player {SteamId}", player.SteamID);

            // Check if player already has an active link
            var existingLink = _databaseService.GetLinkBySteamIdAsync(player.SteamID).Result;
            if (existingLink != null)
            {
                var message = string.Format("{0} You are already linked to Discord: {1} ({2})", 
                    config.MessagePrefix, existingLink.DiscordUsername, existingLink.DiscordUserId);
                player.SendChat(message);
                _core.Logger.LogInformation("Player {SteamId} already linked to Discord user {DiscordUserId} ({DiscordUsername})", 
                    player.SteamID, existingLink.DiscordUserId, existingLink.DiscordUsername);
                return HookResult.Handled;
            }

            // Check if player has an active code
            var activeCode = _databaseService.GetActiveCodeAsync(player.SteamID).Result;
            if (activeCode != null)
            {
                var message = string.Format("{0} You already have an active code: {1} (expires: {2})", 
                    config.MessagePrefix, activeCode.Code, activeCode.TimeRemaining);
                player.SendChat(message);
                _core.Logger.LogInformation("Player {SteamId} already has active code {Code} that expires {Expiry}", 
                    player.SteamID, activeCode.Code, activeCode.ExpiresAtDisplay);
                return HookResult.Handled;
            }

            // Generate new link code using database
            var code = _databaseService.GenerateLinkCodeAsync(player.SteamID, player.Controller.PlayerName).Result;
            if (string.IsNullOrEmpty(code))
            {
                player.SendChat($"{config.MessagePrefix} Error generating link code. Please try again later.");
                _core.Logger.LogError("Failed to generate link code for player {SteamId}", player.SteamID);
                return HookResult.Handled;
            }

            // Get the created code details for expiry info
            var createdCode = _databaseService.GetCodeByCodeAsync(code).Result;
            var expiryInfo = createdCode?.ExpiresAtDisplay ?? "Never";

            var successMessage = string.Format(config.CodeMessage, code);
            if (!string.IsNullOrEmpty(config.MessagePrefix))
                successMessage = $"{config.MessagePrefix} {successMessage}";

            player.SendChat(successMessage);
            
            if (createdCode?.ExpiresAt > 0)
            {
                var expiryMessage = $"{config.MessagePrefix} This code expires in: {createdCode.TimeRemaining}";
                player.SendChat(expiryMessage);
            }

            _core.Logger.LogInformation("Generated link code '{Code}' for player {PlayerName} ({SteamId}), expires: {Expiry}", 
                code, player.Controller.PlayerName, player.SteamID, expiryInfo);
        }
        catch (Exception ex)
        {
            _core.Logger.LogError(ex, "Error processing link command for player {SteamId}", player.SteamID);
            player.SendChat($"{_config.CurrentValue.MessagePrefix} Error processing request. Please try again.");
        }

        return HookResult.Handled;
    }
}
