using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Dommel;

namespace OstoraDiscordLink.Database.Models;

/// <summary>
/// Discord link code model for storing generated codes and their status
/// Table: discord_link_codes
/// </summary>
[Table("discord_link_codes")]
public sealed class LinkCode
{
    /// <summary>Unique link code (primary key)</summary>
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    [Column("code")]
    public string Code { get; set; } = "";

    /// <summary>Steam ID 64 of the player who generated the code</summary>
    [Column("steam_id")]
    public ulong SteamId { get; set; }

    /// <summary>Player name at time of code generation</summary>
    [Column("player_name")]
    public string PlayerName { get; set; } = "";

    /// <summary>When the code was generated (Unix timestamp)</summary>
    [Column("created_at")]
    public int CreatedAt { get; set; }

    /// <summary>When the code expires (Unix timestamp, 0 = no expiry)</summary>
    [Column("expires_at")]
    public int ExpiresAt { get; set; }

    /// <summary>Discord user ID that linked this code (0 = not linked)</summary>
    [Column("discord_user_id")]
    public ulong DiscordUserId { get; set; }

    /// <summary>When the code was linked to Discord (Unix timestamp, 0 = not linked)</summary>
    [Column("linked_at")]
    public int LinkedAt { get; set; }

    /// <summary>Discord username of the linked user (empty = not linked)</summary>
    [Column("discord_username")]
    public string DiscordUsername { get; set; } = "";

    /// <summary>Whether this code is still valid for linking</summary>
    [Ignore]
    public bool IsValid => DiscordUserId == 0 && (ExpiresAt == 0 || ExpiresAt > DateTimeOffset.UtcNow.ToUnixTimeSeconds());

    /// <summary>Whether this code has expired</summary>
    [Ignore]
    public bool IsExpired => ExpiresAt > 0 && ExpiresAt <= DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    /// <summary>Whether this code has been linked</summary>
    [Ignore]
    public bool IsLinked => DiscordUserId > 0;

    /// <summary>Formatted creation time for display</summary>
    [Ignore]
    public string CreatedAtDisplay => DateTimeOffset.FromUnixTimeSeconds(CreatedAt).ToString("yyyy-MM-dd HH:mm:ss");

    /// <summary>Formatted expiry time for display</summary>
    [Ignore]
    public string ExpiresAtDisplay => ExpiresAt > 0 ? DateTimeOffset.FromUnixTimeSeconds(ExpiresAt).ToString("yyyy-MM-dd HH:mm:ss") : "Never";

    /// <summary>Time remaining until expiry (human readable)</summary>
    [Ignore]
    public string TimeRemaining
    {
        get
        {
            if (ExpiresAt == 0) return "Never expires";
            
            var remaining = ExpiresAt - DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (remaining <= 0) return "Expired";
            
            var hours = remaining / 3600;
            var minutes = (remaining % 3600) / 60;
            
            return hours > 0 ? $"{hours}h {minutes}m" : $"{minutes}m";
        }
    }
}
