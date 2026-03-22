using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OstoraDiscordLink.Config;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Misc;

namespace OstoraDiscordLink.Services;

public class CommandService
{
    private readonly ISwiftlyCore _core;
    private readonly IOptionsMonitor<PluginConfig> _config;
    private readonly CodeGenerationService _codeGenerationService;

    public CommandService(
        ISwiftlyCore core,
        IOptionsMonitor<PluginConfig> config,
        CodeGenerationService codeGenerationService)
    {
        _core = core;
        _config = config;
        _codeGenerationService = codeGenerationService;

        // Log current config values for debugging
        var currentConfig = _config.CurrentValue;
        _core.Logger.LogInformation($"OSTORA Discord Link - Config loaded: Command='{currentConfig.Command}', CodeLength={currentConfig.CodeLength}, Prefix='{currentConfig.MessagePrefix}'");

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
            var code = _codeGenerationService.GenerateCode(config.CodeLength);
            var message = string.Format(config.CodeMessage, code);
            
            if (!string.IsNullOrEmpty(config.MessagePrefix))
                message = $"{config.MessagePrefix} {message}";

            player.SendChat(message);
            _core.Logger.LogInformation($"Generated link code '{code}' for player {player.Controller.PlayerName} ({player.SteamID})");
        }
        catch (Exception ex)
        {
            _core.Logger.LogError(ex, $"Error generating link code for player {player.Controller.PlayerName} ({player.SteamID})");
            player.SendChat($"{config.MessagePrefix} Error generating link code. Please try again.");
        }

        return HookResult.Handled;
    }
}
