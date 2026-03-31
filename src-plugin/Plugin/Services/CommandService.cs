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
        var localizer = _core.Translation.GetPlayerLocalizer(player);
        var prefix = localizer["prefix"];
        
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
                    var message = localizer["database.unavailable", prefix];
                    player.SendChat(message);
                    _core.Logger.LogWarning("Database service is not enabled when player {SteamId} requested unlink", player.SteamID);
                    return HookResult.Handled;
                }

                // Check if player is linked
                var currentLink = _databaseService.GetLinkBySteamIdAsync(player.SteamID).Result;
                if (currentLink == null)
                {
                    var message = localizer["unlink.not_linked", prefix];
                    player.SendChat(message);
                    _core.Logger.LogInformation("Player {SteamId} is not linked to any Discord account", player.SteamID);
                    return HookResult.Handled;
                }

                // Unlink the player
                var unlinked = _databaseService.UnlinkPlayerAsync(player.SteamID).Result;
                if (unlinked)
                {
                    var message = localizer["unlink.success", prefix, currentLink.DiscordUsername, currentLink.DiscordUserId];
                    player.SendChat(message);
                    _core.Logger.LogInformation("Player {SteamId} successfully unlinked from Discord user {DiscordUserId} ({DiscordUsername})", 
                        player.SteamID, currentLink.DiscordUserId, currentLink.DiscordUsername);
                }
                else
                {
                    var message = localizer["unlink.error", prefix];
                    player.SendChat(message);
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
                var message = localizer["database.unavailable", prefix];
                player.SendChat(message);
                _core.Logger.LogWarning("Database service is not enabled when player {SteamId} requested link code", player.SteamID);
                _core.Logger.LogError("DATABASE SERVICE DISABLED - Check database configuration and connection");
                return HookResult.Handled;
            }

            _core.Logger.LogDebug("Database service is enabled, proceeding with link request for player {SteamId}", player.SteamID);

            // Check if player already has an active link
            var existingLink = _databaseService.GetLinkBySteamIdAsync(player.SteamID).Result;
            if (existingLink != null)
            {
                var message = localizer["link.already_linked", prefix, existingLink.DiscordUsername, existingLink.DiscordUserId];
                player.SendChat(message);
                _core.Logger.LogInformation("Player {SteamId} already linked to Discord user {DiscordUserId} ({DiscordUsername})", 
                    player.SteamID, existingLink.DiscordUserId, existingLink.DiscordUsername);
                return HookResult.Handled;
            }

            // Check if player has an active code
            var activeCode = _databaseService.GetActiveCodeAsync(player.SteamID).Result;
            if (activeCode != null)
            {
                var message = localizer["link.has_active_code", prefix, activeCode.Code, activeCode.TimeRemaining];
                player.SendChat(message);
                _core.Logger.LogInformation("Player {SteamId} already has active code {Code} that expires {Expiry}", 
                    player.SteamID, activeCode.Code, activeCode.ExpiresAtDisplay);
                return HookResult.Handled;
            }

            // Generate new link code using database
            var code = _databaseService.GenerateLinkCodeAsync(player.SteamID, player.Controller.PlayerName).Result;
            if (string.IsNullOrEmpty(code))
            {
                var message = localizer["link.error", prefix];
                player.SendChat(message);
                _core.Logger.LogError("Failed to generate link code for player {SteamId}", player.SteamID);
                return HookResult.Handled;
            }

            // Get the created code details for expiry info
            var createdCode = _databaseService.GetCodeByCodeAsync(code).Result;
            var expiryInfo = createdCode?.ExpiresAtDisplay ?? "Never";

            var successMessage = localizer["link.success", prefix, code];
            player.SendChat(successMessage);
            
            if (createdCode?.ExpiresAt > 0)
            {
                var expiryMessage = localizer["link.expiry", prefix, createdCode.TimeRemaining];
                player.SendChat(expiryMessage);
            }

            _core.Logger.LogInformation("Generated link code '{Code}' for player {PlayerName} ({SteamId}), expires: {Expiry}", 
                code, player.Controller.PlayerName, player.SteamID, expiryInfo);
        }
        catch (Exception ex)
        {
            _core.Logger.LogError(ex, "Error processing link command for player {SteamId}", player.SteamID);
            var message = localizer["command.error", prefix];
            player.SendChat(message);
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
        
        var localizer = _core.Translation.GetPlayerLocalizer(player);
        var prefix = localizer["prefix"];
        
        player.SendChat(localizer["permission.check.title", prefix]);
        player.SendChat(localizer["permission.check.steamid", prefix, player.SteamID]);
        player.SendChat(localizer["permission.check.permission", prefix, configPerm]);
        player.SendChat(localizer["permission.check.has_permission", prefix, hasPermission ? "✅ YES" : "❌ NO"]);
        
        // Check if linked in database
        var dbLink = _databaseService.GetLinkBySteamIdAsync(player.SteamID).Result;
        if (dbLink != null)
        {
            player.SendChat(localizer["permission.check.db_linked", prefix, dbLink.DiscordUsername, dbLink.DiscordUserId]);
        }
        else
        {
            player.SendChat(localizer["permission.check.db_not_linked", prefix]);
        }
    }

    [Command("debuglink")]
    public void Command_DebugLink(ICommandContext context)
    {
        if (!context.IsSentByPlayer) return;
        
        var player = context.Sender!;
        var configPerm = _config.CurrentValue.Permissions.LinkedPermission;
        var hasPermission = _core.Permission.PlayerHasPermission(player.SteamID, configPerm);
        
        var localizer = _core.Translation.GetPlayerLocalizer(player);
        var prefix = localizer["prefix"];
        
        player.SendChat(localizer["debug.title", prefix]);
        player.SendChat(localizer["permission.check.steamid", prefix, player.SteamID]);
        player.SendChat(localizer["permission.check.permission", prefix, configPerm]);
        player.SendChat(localizer["permission.check.has_permission", prefix, hasPermission ? "✅ YES" : "❌ NO"]);
        
        // Check if linked in database
        var dbLink = _databaseService.GetLinkBySteamIdAsync(player.SteamID).Result;
        if (dbLink != null)
        {
            player.SendChat(localizer["permission.check.db_linked", prefix, dbLink.DiscordUsername, dbLink.DiscordUserId]);
            player.SendChat($"{prefix}   Linked At: {dbLink.LinkedAt}");
        }
        else
        {
            player.SendChat(localizer["permission.check.db_not_linked", prefix]);
        }
        
        // Check database service status
        player.SendChat(localizer["debug.db_service_enabled", prefix, _databaseService.IsEnabled]);
        player.SendChat(localizer["debug.grant_on_link", prefix, _config.CurrentValue.Permissions.GrantOnLink]);
    }

    [Command("grantperm")]
    public void Command_GrantPerm(ICommandContext context)
    {
        if (!context.IsSentByPlayer) return;
        
        var player = context.Sender!;
        var configPerm = _config.CurrentValue.Permissions.LinkedPermission;
        
        // Grant the permission
        _core.Permission.AddPermission(player.SteamID, configPerm);
        
        var localizer = _core.Translation.GetPlayerLocalizer(player);
        var prefix = localizer["prefix"];
        
        player.SendChat(localizer["grant.success", prefix, configPerm]);
        
        // Verify it was granted
        var hasPermission = _core.Permission.PlayerHasPermission(player.SteamID, configPerm);
        player.SendChat(localizer["grant.verification", prefix, hasPermission ? "✅ YES" : "❌ NO"]);
        
        _core.Logger.LogInformation("Manual permission grant for player {SteamId} - Permission '{Permission}': {HasPermission}", 
            player.SteamID, configPerm, hasPermission);
    }

    [Command("syncperm")]
    public void Command_SyncPerm(ICommandContext context)
    {
        if (!context.IsSentByPlayer) return;
        
        var player = context.Sender!;
        var localizer = _core.Translation.GetPlayerLocalizer(player);
        var prefix = localizer["prefix"];
        
        // Check if player is linked in database
        var dbLink = _databaseService.GetLinkBySteamIdAsync(player.SteamID).Result;
        if (dbLink != null)
        {
            var configPerm = _config.CurrentValue.Permissions.LinkedPermission;
            
            player.SendChat(localizer["sync.start", prefix]);
            player.SendChat(localizer["sync.link_found", prefix, dbLink.DiscordUsername, dbLink.DiscordUserId]);
            
            // Grant the permission
            var granted = _databaseService.GrantLinkedPermissionAsync(player.SteamID, configPerm).Result;
            if (granted)
            {
                player.SendChat(localizer["sync.permission_granted", prefix, configPerm]);
                
                // Verify
                var hasPermission = _core.Permission.PlayerHasPermission(player.SteamID, configPerm);
                player.SendChat(localizer["grant.verification", prefix, hasPermission ? "✅ YES" : "❌ NO"]);
                
                if (hasPermission)
                {
                    player.SendChat(localizer["sync.can_chat", prefix]);
                }
            }
            else
            {
                player.SendChat(localizer["sync.permission_failed", prefix]);
            }
        }
        else
        {
            player.SendChat(localizer["sync.no_link", prefix]);
        }
        
        _core.Logger.LogInformation("Manual permission sync requested by player {SteamId} - Found link: {HasLink}", 
            player.SteamID, dbLink != null);
    }

    [Command("checklink")]
    public void Command_CheckLink(ICommandContext context)
    {
        if (!context.IsSentByPlayer) return;
        
        var player = context.Sender!;
        var configPerm = _config.CurrentValue.Permissions.LinkedPermission;
        var hasPermission = _core.Permission.PlayerHasPermission(player.SteamID, configPerm);
        
        var localizer = _core.Translation.GetPlayerLocalizer(player);
        var prefix = localizer["prefix"];
        
        player.SendChat(localizer["check.title", prefix]);
        player.SendChat(localizer["permission.check.steamid", prefix, player.SteamID]);
        player.SendChat(localizer["permission.check.permission", prefix, configPerm]);
        player.SendChat(localizer["permission.check.has_permission", prefix, hasPermission ? "✅ YES" : "❌ NO"]);
        
        // Check if linked in database
        var dbLink = _databaseService.GetLinkBySteamIdAsync(player.SteamID).Result;
        if (dbLink != null)
        {
            player.SendChat(localizer["permission.check.db_linked", prefix, dbLink.DiscordUsername, dbLink.DiscordUserId]);
            player.SendChat($"{prefix}   Linked At: {dbLink.LinkedAt}");
            
            if (!hasPermission)
            {
                player.SendChat(localizer["check.missing_permission", prefix]);
            }
        }
        else
        {
            player.SendChat(localizer["permission.check.db_not_linked", prefix]);
            player.SendChat(localizer["check.get_link", prefix]);
        }
    }
}
