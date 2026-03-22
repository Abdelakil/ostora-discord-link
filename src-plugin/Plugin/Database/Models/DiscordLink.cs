using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Dommel;

namespace OstoraDiscordLink.Database.Models;

/// <summary>
/// Discord link model for storing permanent player-discord associations
/// Table: discord_links
/// </summary>
[Table("discord_links")]
public sealed class DiscordLink
{
    /// <summary>Steam ID 64 of the player (primary key)</summary>
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    [Column("steam_id")]
    public ulong SteamId { get; set; }

    /// <summary>Discord user ID (primary key)</summary>
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    [Column("discord_user_id")]
    public ulong DiscordUserId { get; set; }

    /// <summary>Player name at time of linking</summary>
    [Column("player_name")]
    public string PlayerName { get; set; } = "";

    /// <summary>Discord username at time of linking</summary>
    [Column("discord_username")]
    public string DiscordUsername { get; set; } = "";

    /// <summary>When the link was created (Unix timestamp)</summary>
    [Column("linked_at")]
    public int LinkedAt { get; set; }

    /// <summary>Formatted link time for display</summary>
    [Ignore]
    public string LinkedAtDisplay => DateTimeOffset.FromUnixTimeSeconds(LinkedAt).ToString("yyyy-MM-dd HH:mm:ss");
}
