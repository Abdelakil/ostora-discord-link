namespace OstoraDiscordLink.Config;

public class PluginConfig
{
    public string Command { get; set; } = "!link";
    public int CodeLength { get; set; } = 8;
    public string MessagePrefix { get; set; } = "[OSTORA]";
    public string CodeMessage { get; set; } = "Your link code is: {0}";
}
