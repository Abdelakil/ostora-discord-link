namespace OstoraDiscordLink.Config;

public class PluginConfig
{
    public string Command { get; set; } = "!link";
    public string UnlinkCommand { get; set; } = "!unlink";
    public int CodeLength { get; set; } = 8;
    public string MessagePrefix { get; set; } = "[OSTORA]";
    public string CodeMessage { get; set; } = "Your link code is: {0}";
    public DatabaseSettings Database { get; set; } = new();
    public CodeSettings CodeSettings { get; set; } = new();
    public PermissionSettings Permissions { get; set; } = new();
}

public class DatabaseSettings
{
    /// <summary>Database connection name (from SwiftlyS2's database.jsonc)</summary>
    public string Connection { get; set; } = "default_connection";
    
    /// <summary>Days to keep old records (0 = forever)</summary>
    public int PurgeDays { get; set; } = 30;
}

public class CodeSettings
{
    /// <summary>Code expiry time in minutes (0 = never expires)</summary>
    public int ExpiryMinutes { get; set; } = 15;
    
    /// <summary>Maximum attempts to generate a unique code</summary>
    public int MaxGenerationAttempts { get; set; } = 10;
}

public class PermissionSettings
{
    /// <summary>Permission to grant when Discord account is linked</summary>
    public string LinkedPermission { get; set; } = "ostora.discord.linked";
    
    /// <summary>Whether to automatically grant permission on linking</summary>
    public bool GrantOnLink { get; set; } = true;
    
    /// <summary>Whether to automatically revoke permission on unlinking</summary>
    public bool RevokeOnUnlink { get; set; } = true;
}
