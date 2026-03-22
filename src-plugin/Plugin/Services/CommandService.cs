using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OstoraDiscordLink.Config;
using OstoraDiscordLink.Database;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Misc;
using SwiftlyS2.Shared.Players;

namespace OstoraDiscordLink.Services;

public class CommandService
{
    private readonly ISwiftlyCore _core;
    private readonly IOptionsMonitor<PluginConfig> _config;
    private readonly CodeGenerationService _codeGenerationService;
    private readonly DatabaseService _databaseService;

    public CommandService(ISwiftlyCore core, IOptionsMonitor<PluginConfig> config, CodeGenerationService codeGenerationService, DatabaseService databaseService)
    {
        _core = core;
        _config = config;
        _codeGenerationService = codeGenerationService;
        _databaseService = databaseService;

        // Register commands with SwiftlyS2
        core.Registrator.Register(this);
    }

    [ClientChatHookHandler]
    public HookResult OnClientChat(int playerId, string text, bool teamOnly)
    {
        var player = _core.PlayerManager.GetPlayer(playerId);
        if (player == null || player.IsFakeClient)
            return HookResult.Continue;

        var config = _config.CurrentValue;
        
        if (!text.StartsWith(config.Command, StringComparison.OrdinalIgnoreCase) && 
            !text.StartsWith(config.UnlinkCommand, StringComparison.OrdinalIgnoreCase))
            return HookResult.Continue;

        try
        {
            // Handle unlink command
            if (text.StartsWith(config.UnlinkCommand, StringComparison.OrdinalIgnoreCase))
            {
                _core.Logger.LogInformation("Player {PlayerName} ({SteamId}) requested Discord unlink", 
                    player.Controller.PlayerName, player.SteamID);

                // Check if database is available
                if (!_databaseService.IsEnabled)
                {
                    player.SendChat($"{config.MessagePrefix} Database is not available. Please contact an administrator.");
                    _core.Logger.LogWarning("Database service is not enabled when player {SteamId} requested unlink", player.SteamID);
                    return HookResult.Handled;
                }

                // Check if player is linked
                var currentLink = _databaseService.GetLinkBySteamIdAsync(player.SteamID).Result;
                if (currentLink == null)
                {
                    player.SendChat($"{config.MessagePrefix} You are not linked to any Discord account.");
                    _core.Logger.LogInformation("Player {SteamId} is not linked to any Discord account", player.SteamID);
                    return HookResult.Handled;
                }

                // Unlink the player
                var unlinked = _databaseService.UnlinkPlayerAsync(player.SteamID).Result;
                if (unlinked)
                {
                    player.SendChat($"{config.MessagePrefix} Successfully unlinked from Discord: {currentLink.DiscordUsername} ({currentLink.DiscordUserId})");
                    _core.Logger.LogInformation("Player {SteamId} successfully unlinked from Discord user {DiscordUserId} ({DiscordUsername})", 
                        player.SteamID, currentLink.DiscordUserId, currentLink.DiscordUsername);
                }
                else
                {
                    player.SendChat($"{config.MessagePrefix} Error unlinking account. Please try again later.");
                    _core.Logger.LogError("Failed to unlink player {SteamId}", player.SteamID);
                }

                return HookResult.Handled;
            }

            // Handle link command
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

    [Command("checkperm")]
    public void Command_CheckPerm(ICommandContext context)
    {
        if (!context.IsSentByPlayer) return;
        
        var player = context.Sender!;
        var configPerm = _config.CurrentValue.Permissions.LinkedPermission;
        var hasPermission = _core.Permission.PlayerHasPermission(player.SteamID, configPerm);
        
        player.SendChat($"{_config.CurrentValue.MessagePrefix} Permission Check:");
        player.SendChat($"{_config.CurrentValue.MessagePrefix}   SteamID: {player.SteamID}");
        player.SendChat($"{_config.CurrentValue.MessagePrefix}   Permission: '{configPerm}'");
        player.SendChat($"{_config.CurrentValue.MessagePrefix}   Has Permission: {(hasPermission ? "✅ YES" : "❌ NO")}");
        
        // Check if linked in database
        var dbLink = _databaseService.GetLinkBySteamIdAsync(player.SteamID).Result;
        if (dbLink != null)
        {
            player.SendChat($"{_config.CurrentValue.MessagePrefix}   Database Link: ✅ Linked to {dbLink.DiscordUsername} ({dbLink.DiscordUserId})");
        }
        else
        {
            player.SendChat($"{_config.CurrentValue.MessagePrefix}   Database Link: ❌ Not linked");
        }
    }

    [Command("debuglink")]
    public void Command_DebugLink(ICommandContext context)
    {
        if (!context.IsSentByPlayer) return;
        
        var player = context.Sender!;
        var configPerm = _config.CurrentValue.Permissions.LinkedPermission;
        var hasPermission = _core.Permission.PlayerHasPermission(player.SteamID, configPerm);
        
        player.SendChat($"{_config.CurrentValue.MessagePrefix} Debug Link Status:");
        player.SendChat($"{_config.CurrentValue.MessagePrefix}   SteamID: {player.SteamID}");
        player.SendChat($"{_config.CurrentValue.MessagePrefix}   Permission: '{configPerm}'");
        player.SendChat($"{_config.CurrentValue.MessagePrefix}   Has Permission: {(hasPermission ? "✅ YES" : "❌ NO")}");
        
        // Check if linked in database
        var dbLink = _databaseService.GetLinkBySteamIdAsync(player.SteamID).Result;
        if (dbLink != null)
        {
            player.SendChat($"{_config.CurrentValue.MessagePrefix}   Database Link: ✅ Linked to {dbLink.DiscordUsername} ({dbLink.DiscordUserId})");
            player.SendChat($"{_config.CurrentValue.MessagePrefix}   Linked At: {dbLink.LinkedAt}");
        }
        else
        {
            player.SendChat($"{_config.CurrentValue.MessagePrefix}   Database Link: ❌ Not linked");
        }
        
        // Check database service status
        player.SendChat($"{_config.CurrentValue.MessagePrefix}   DB Service Enabled: {_databaseService.IsEnabled}");
        player.SendChat($"{_config.CurrentValue.MessagePrefix}   Grant On Link: {_config.CurrentValue.Permissions.GrantOnLink}");
    }
}
