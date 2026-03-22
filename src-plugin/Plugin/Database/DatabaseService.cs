using Dapper;
using Dommel;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OstoraDiscordLink.Config;
using OstoraDiscordLink.Database.Models;
using SwiftlyS2.Shared;
using System.Data;

namespace OstoraDiscordLink.Database;

/// <summary>
/// Database service for Discord Link plugin
/// Manages link codes and player-discord associations
/// </summary>
public sealed class DatabaseService
{
    private readonly ISwiftlyCore _core;
    private readonly string _connectionName;
    private readonly int _codeExpiryMinutes;
    private readonly int _purgeDays;
    private readonly IOptionsMonitor<PluginConfig> _config;

    public bool IsEnabled { get; private set; }

    public const string LinkCodesTableName = "discord_link_codes";
    public const string LinksTableName = "discord_links";

    public DatabaseService(ISwiftlyCore core, string connectionName, int codeExpiryMinutes, int purgeDays, IOptionsMonitor<PluginConfig> config)
    {
        _core = core;
        _connectionName = connectionName;
        _codeExpiryMinutes = codeExpiryMinutes;
        _purgeDays = purgeDays;
        _config = config;
    }

    /// <summary>
    /// Initialize the database and create tables
    /// </summary>
    public async Task InitializeAsync()
    {
        try
        {
            _core.Logger.LogInformation("Initializing Discord Link database service...");
            _core.Logger.LogInformation("Database connection name: {ConnectionName}", _connectionName);
            _core.Logger.LogInformation("Code expiry minutes: {ExpiryMinutes}", _codeExpiryMinutes);
            _core.Logger.LogInformation("Purge days: {PurgeDays}", _purgeDays);

            // Test database connection first
            try
            {
                using var testConnection = _core.Database.GetConnection(_connectionName);
                testConnection.Open();
                _core.Logger.LogInformation("Database connection test successful - Type: {DbType}, Connection: {ConnectionString}", 
                    testConnection.GetType().Name, testConnection.ConnectionString?.Substring(0, Math.Min(50, testConnection.ConnectionString?.Length ?? 0)) + "...");
            }
            catch (Exception ex)
            {
                _core.Logger.LogError(ex, "Database connection test failed for connection '{ConnectionName}'", _connectionName);
                IsEnabled = false;
                return;
            }

            // Create tables directly (no migrations)
            await CreateTablesAsync();

            IsEnabled = true;
            _core.Logger.LogInformation("Discord Link database initialized successfully. Tables: {Tables}", 
                $"{LinkCodesTableName}, {LinksTableName}");

            // Clean up expired codes and old data
            await CleanupExpiredCodesAsync();
            await PurgeOldRecordsAsync();
            
            // Fix existing codes with no expiry
            await FixExistingCodesAsync();
        }
        catch (Exception ex)
        {
            _core.Logger.LogError(ex, "Failed to initialize Discord Link database");
            IsEnabled = false;
        }
    }

    /// <summary>
    /// Create database tables directly
    /// </summary>
    private async Task CreateTablesAsync()
    {
        using var connection = _core.Database.GetConnection(_connectionName);
        connection.Open();

        // Create link codes table (only if doesn't exist)
        var createLinkCodesTable = @"
            CREATE TABLE IF NOT EXISTS discord_link_codes (
                code VARCHAR(32) NOT NULL PRIMARY KEY,
                steam_id BIGINT NOT NULL,
                player_name VARCHAR(64) NOT NULL DEFAULT '',
                created_at INT NOT NULL,
                expires_at INT NOT NULL DEFAULT 0,
                discord_user_id BIGINT NOT NULL DEFAULT 0,
                linked_at INT NOT NULL DEFAULT 0,
                discord_username VARCHAR(64) NOT NULL DEFAULT '',
                db_name VARCHAR(64) NOT NULL DEFAULT ''
            )";

        await connection.ExecuteAsync(createLinkCodesTable);

        // Add db_name column to existing tables (if missing)
        try
        {
            var addColumnSql = "ALTER TABLE discord_link_codes ADD COLUMN IF NOT EXISTS db_name VARCHAR(64) NOT NULL DEFAULT ''";
            await connection.ExecuteAsync(addColumnSql);
            _core.Logger.LogInformation("Verified db_name column exists in discord_link_codes table");
        }
        catch (Exception ex)
        {
            _core.Logger.LogDebug("Could not add db_name column (may already exist): {Message}", ex.Message);
        }

        // Create indexes for link codes
        var createIndexes = @"
            CREATE INDEX IF NOT EXISTS idx_link_codes_steam ON discord_link_codes(steam_id);
            CREATE INDEX IF NOT EXISTS idx_link_codes_discord ON discord_link_codes(discord_user_id);
            CREATE INDEX IF NOT EXISTS idx_link_codes_created ON discord_link_codes(created_at);
            CREATE INDEX IF NOT EXISTS idx_link_codes_expires ON discord_link_codes(expires_at)";

        await connection.ExecuteAsync(createIndexes);

        // Create simplified links table (only if doesn't exist)
        var createLinksTable = @"
            CREATE TABLE IF NOT EXISTS discord_links (
                steam_id BIGINT NOT NULL,
                discord_user_id BIGINT NOT NULL,
                player_name VARCHAR(64) NOT NULL DEFAULT '',
                discord_username VARCHAR(64) NOT NULL DEFAULT '',
                linked_at INT NOT NULL,
                PRIMARY KEY (steam_id, discord_user_id)
            )";

        await connection.ExecuteAsync(createLinksTable);

        // Create indexes for links
        var createLinkIndexes = @"
            CREATE INDEX IF NOT EXISTS idx_links_steam ON discord_links(steam_id);
            CREATE INDEX IF NOT EXISTS idx_links_discord ON discord_links(discord_user_id);
            CREATE INDEX IF NOT EXISTS idx_links_linked ON discord_links(linked_at)";

        await connection.ExecuteAsync(createLinkIndexes);

        _core.Logger.LogInformation("Database tables verified and ready");
    }

    /// <summary>
    /// Generate a new unique link code for a player
    /// </summary>
    public async Task<string?> GenerateLinkCodeAsync(ulong steamId, string playerName)
    {
        if (!IsEnabled)
        {
            _core.Logger.LogWarning("Database not enabled, cannot generate link code");
            return null;
        }

        try
        {
            // Check if player already has an active code
            var existingCode = await GetActiveCodeAsync(steamId);
            if (existingCode != null)
            {
                _core.Logger.LogInformation("Player {SteamId} already has active code: {Code}", steamId, existingCode.Code);
                return existingCode.Code;
            }

            // Generate a unique code
            var code = await GenerateUniqueCodeAsync();
            if (code == null)
            {
                _core.Logger.LogError("Failed to generate unique code for player {SteamId}", steamId);
                return null;
            }

            // Calculate expiry time
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var expiresAt = _codeExpiryMinutes > 0 ? (int)(now + (_codeExpiryMinutes * 60)) : 0;

            _core.Logger.LogDebug("Code expiry calculation - Now: {Now}, ExpiryMinutes: {ExpiryMinutes}, CalculatedExpiresAt: {ExpiresAt}", 
                now, _codeExpiryMinutes, expiresAt);

            // Create and save the link code
            var linkCode = new LinkCode
            {
                Code = code,
                SteamId = steamId,
                PlayerName = playerName,
                CreatedAt = (int)now,
                ExpiresAt = expiresAt,
                DbName = GetDatabaseName()
            };

            using var connection = _core.Database.GetConnection(_connectionName);
            connection.Open();
            await connection.InsertAsync(linkCode);

            _core.Logger.LogInformation("Generated link code '{Code}' for player {PlayerName} ({SteamId}), expires: {Expiry}", 
                code, playerName, steamId, linkCode.ExpiresAtDisplay);

            return code;
        }
        catch (Exception ex)
        {
            _core.Logger.LogError(ex, "Error generating link code for player {SteamId}", steamId);
            return null;
        }
    }

    /// <summary>
    /// Get an active (unlinked, unexpired) code for a player
    /// </summary>
    public async Task<LinkCode?> GetActiveCodeAsync(ulong steamId)
    {
        if (!IsEnabled) return null;

        try
        {
            using var connection = _core.Database.GetConnection(_connectionName);
            connection.Open();

            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var sql = @"
                SELECT 
                    code AS Code,
                    steam_id AS SteamId, 
                    player_name AS PlayerName, 
                    created_at AS CreatedAt, 
                    expires_at AS ExpiresAt, 
                    discord_user_id AS DiscordUserId, 
                    linked_at AS LinkedAt, 
                    discord_username AS DiscordUsername,
                    db_name AS DbName
                FROM discord_link_codes 
                WHERE steam_id = @steamId AND discord_user_id = 0 AND (expires_at = 0 OR expires_at > @now)
                LIMIT 1";

            var result = await connection.QuerySingleOrDefaultAsync<LinkCode>(sql, new { steamId = steamId, now });

            if (result != null)
            {
                _core.Logger.LogInformation("Retrieved code data - Code: '{Code}', CreatedAt: {CreatedAt}, ExpiresAt: {ExpiresAt}, Now: {Now}, IsValid: {IsValid}", 
                    result.Code, result.CreatedAt, result.ExpiresAt, now, result.IsValid);
                _core.Logger.LogInformation("Expiry display - ExpiresAtDisplay: '{ExpiresAtDisplay}', TimeRemaining: '{TimeRemaining}'", 
                    result.ExpiresAtDisplay, result.TimeRemaining);
            }

            return result;
        }
        catch (Exception ex)
        {
            _core.Logger.LogError(ex, "Error getting active code for player {SteamId}", steamId);
            return null;
        }
    }

    /// <summary>
    /// Get link code by code string
    /// </summary>
    public async Task<LinkCode?> GetCodeByCodeAsync(string code)
    {
        if (!IsEnabled) return null;

        try
        {
            using var connection = _core.Database.GetConnection(_connectionName);
            connection.Open();

            var sql = @"
                SELECT 
                    code, steam_id, player_name, created_at, expires_at, discord_user_id, linked_at, discord_username, db_name 
                FROM discord_link_codes 
                WHERE code = @code
                LIMIT 1";

            var linkCode = await connection.QuerySingleOrDefaultAsync<LinkCode>(sql, new { code });
            
            if (linkCode != null)
            {
                _core.Logger.LogDebug("Retrieved code '{Code}' for SteamId {SteamId}", code, linkCode.SteamId);
            }

            return linkCode;
        }
        catch (Exception ex)
        {
            _core.Logger.LogError(ex, "Error retrieving code '{Code}'", code);
            return null;
        }
    }

    /// <summary>
    /// Link a Discord user to a Steam account using a code
    /// </summary>
    public async Task<bool> LinkCodeAsync(string code, ulong discordUserId, string discordUsername)
    {
        if (!IsEnabled) return false;

        try
        {
            using var connection = _core.Database.GetConnection(_connectionName);
            connection.Open();

            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            // Find the code
            var linkCode = await GetCodeByCodeAsync(code);
            if (linkCode == null)
            {
                _core.Logger.LogWarning("Link code '{Code}' not found", code);
                return false;
            }

            if (linkCode.DiscordUserId != 0)
            {
                _core.Logger.LogWarning("Link code '{Code}' already linked to Discord user {DiscordUserId}", code, linkCode.DiscordUserId);
                return false;
            }

            if (linkCode.ExpiresAt > 0 && linkCode.ExpiresAt <= now)
            {
                _core.Logger.LogWarning("Link code '{Code}' expired at {ExpiresAt}", code, linkCode.ExpiresAt);
                return false;
            }

            // Create permanent link
            var discordLink = new DiscordLink
            {
                SteamId = linkCode.SteamId,
                DiscordUserId = discordUserId,
                PlayerName = linkCode.PlayerName,
                DiscordUsername = discordUsername,
                LinkedAt = (int)now
            };

            await connection.InsertAsync(discordLink);

            // Update the original code
            linkCode.DiscordUserId = discordUserId;
            linkCode.DiscordUsername = discordUsername;
            linkCode.LinkedAt = (int)now;
            await connection.UpdateAsync(linkCode);

            _core.Logger.LogInformation("Successfully linked code '{Code}' - Steam: {SteamId} to Discord: {DiscordUserId} ({DiscordUsername})", 
                code, linkCode.SteamId, discordUserId, discordUsername);

            // Grant permission if configured
            if (_config?.CurrentValue?.Permissions?.GrantOnLink == true && !string.IsNullOrEmpty(_config?.CurrentValue?.Permissions?.LinkedPermission))
            {
                await GrantLinkedPermissionAsync(linkCode.SteamId, _config.CurrentValue.Permissions.LinkedPermission);
            }

            return true;
        }
        catch (Exception ex)
        {
            _core.Logger.LogError(ex, "Error linking code '{Code}' to Discord user {DiscordUserId}", code, discordUserId);
            return false;
        }
    }

    /// <summary>
    /// Get Discord link by Steam ID
    /// </summary>
    public async Task<DiscordLink?> GetLinkBySteamIdAsync(ulong steamId)
    {
        if (!IsEnabled) return null;

        try
        {
            using var connection = _core.Database.GetConnection(_connectionName);
            connection.Open();

            var sql = @"
                SELECT 
                    steam_id, discord_user_id, player_name, discord_username, linked_at 
                FROM discord_links 
                WHERE steam_id = @steam_id
                LIMIT 1";

            var link = await connection.QuerySingleOrDefaultAsync<DiscordLink>(sql, new { steam_id = steamId });
            
            if (link != null)
            {
                _core.Logger.LogDebug("Found Discord link for Steam {SteamId}: {DiscordUser} ({DiscordUserId})", 
                    steamId, link.DiscordUsername, link.DiscordUserId);
            }

            return link;
        }
        catch (Exception ex)
        {
            _core.Logger.LogError(ex, "Error getting Discord link for Steam {SteamId}", steamId);
            return null;
        }
    }

    /// <summary>
    /// Get Discord link by Discord user ID
    /// </summary>
    public async Task<DiscordLink?> GetLinkByDiscordIdAsync(ulong discordUserId)
    {
        if (!IsEnabled) return null;

        try
        {
            using var connection = _core.Database.GetConnection(_connectionName);
            connection.Open();

            var sql = @"
                SELECT 
                    steam_id, discord_user_id, player_name, discord_username, linked_at 
                FROM discord_links 
                WHERE discord_user_id = @discord_user_id
                LIMIT 1";

            var link = await connection.QuerySingleOrDefaultAsync<DiscordLink>(sql, new { discord_user_id = discordUserId });

            if (link != null)
            {
                _core.Logger.LogDebug("Found Discord link for Discord user {DiscordUserId}: Steam {SteamId} ({PlayerName})", 
                    discordUserId, link.SteamId, link.PlayerName);
            }

            return link;
        }
        catch (Exception ex)
        {
            _core.Logger.LogError(ex, "Error getting Discord link for Discord user {DiscordUserId}", discordUserId);
            return null;
        }
    }

    /// <summary>
    /// Get the database name from connection string
    /// </summary>
    private string GetDatabaseName()
    {
        try
        {
            using var connection = _core.Database.GetConnection(_connectionName);
            var connectionString = connection.ConnectionString;
            
            if (string.IsNullOrEmpty(connectionString))
                return "unknown";

            // Parse different connection string formats
            if (connectionString.Contains("Database="))
            {
                var parts = connectionString.Split(';');
                foreach (var part in parts)
                {
                    if (part.StartsWith("Database="))
                        return part.Substring(9);
                }
            }
            else if (connectionString.Contains("database="))
            {
                var parts = connectionString.Split(';');
                foreach (var part in parts)
                {
                    if (part.StartsWith("database="))
                        return part.Substring(9);
                }
            }
            
            // For SQLite, use the file path
            if (connectionString.Contains("Data Source="))
            {
                var parts = connectionString.Split(';');
                foreach (var part in parts)
                {
                    if (part.StartsWith("Data Source="))
                    {
                        var filePath = part.Substring(12);
                        return Path.GetFileNameWithoutExtension(filePath);
                    }
                }
            }
            
            return "default";
        }
        catch (Exception ex)
        {
            _core.Logger.LogWarning(ex, "Failed to get database name");
            return "unknown";
        }
    }

    /// <summary>
    /// Generate a unique code that doesn't exist in the database
    /// </summary>
    private async Task<string?> GenerateUniqueCodeAsync()
    {
        const int maxAttempts = 10;
        var random = new Random();
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";

        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            var code = new string(Enumerable.Repeat(chars, 8)
                .Select(s => s[random.Next(s.Length)]).ToArray());

            // Check if code already exists
            var existingCode = await GetCodeByCodeAsync(code);
            if (existingCode == null)
            {
                return code;
            }
        }

        _core.Logger.LogError("Failed to generate unique code after {Attempts} attempts", maxAttempts);
        return null;
    }

    /// <summary>
    /// Fix existing codes that have no expiry set
    /// </summary>
    private async Task FixExistingCodesAsync()
    {
        if (!IsEnabled) return;

        try
        {
            using var connection = _core.Database.GetConnection(_connectionName);
            connection.Open();

            // Update codes that have expires_at = 0 (never expires) to have proper expiry
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var newExpiry = _codeExpiryMinutes > 0 ? (int)(now + (_codeExpiryMinutes * 60)) : 0;
            
            if (_codeExpiryMinutes > 0)
            {
                var sql = "UPDATE discord_link_codes SET expires_at = @expires_at WHERE expires_at = 0 AND discord_user_id = 0";
                var updated = await connection.ExecuteAsync(sql, new { expires_at = newExpiry });

                if (updated > 0)
                {
                    _core.Logger.LogInformation("Fixed {Count} existing codes with proper expiry ({Minutes} minutes)", updated, _codeExpiryMinutes);
                }
            }
        }
        catch (Exception ex)
        {
            _core.Logger.LogError(ex, "Error fixing existing codes");
        }
    }

    /// <summary>
    /// Clean up expired codes
    /// </summary>
    private async Task CleanupExpiredCodesAsync()
    {
        if (!IsEnabled) return;

        try
        {
            using var connection = _core.Database.GetConnection(_connectionName);
            connection.Open();

            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var sql = "DELETE FROM discord_link_codes WHERE expires_at > 0 AND expires_at <= @now";
            var deleted = await connection.ExecuteAsync(sql, new { now });

            if (deleted > 0)
            {
                _core.Logger.LogInformation("Cleaned up {Count} expired link codes", deleted);
            }
        }
        catch (Exception ex)
        {
            _core.Logger.LogError(ex, "Error cleaning up expired codes");
        }
    }

    /// <summary>
    /// Purge old records
    /// </summary>
    private async Task PurgeOldRecordsAsync()
    {
        if (!IsEnabled || _purgeDays <= 0) return;

        try
        {
            using var connection = _core.Database.GetConnection(_connectionName);
            connection.Open();

            var cutoffTimestamp = (int)DateTimeOffset.UtcNow.AddDays(-_purgeDays).ToUnixTimeSeconds();

            // Purge old linked codes
            var sql = "DELETE FROM discord_link_codes WHERE linked_at > 0 AND linked_at < @cutoff";
            var deletedCodes = await connection.ExecuteAsync(sql, new { cutoff = cutoffTimestamp });

            if (deletedCodes > 0)
            {
                _core.Logger.LogInformation("Purged {Count} old linked codes (>{Days} days)", deletedCodes, _purgeDays);
            }
        }
        catch (Exception ex)
        {
            _core.Logger.LogError(ex, "Error purging old records");
        }
    }

    /// <summary>
    /// Unlink a player's Discord account
    /// </summary>
    public async Task<bool> UnlinkPlayerAsync(ulong steamId)
    {
        if (!IsEnabled) return false;

        try
        {
            using var connection = _core.Database.GetConnection(_connectionName);
            connection.Open();

            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            // Get existing link
            var existingLink = await GetLinkBySteamIdAsync(steamId);
            if (existingLink == null)
            {
                _core.Logger.LogInformation("Player {SteamId} is not linked to any Discord account", steamId);
                return false;
            }

            // Delete the permanent link
            var deleteLinkSql = "DELETE FROM discord_links WHERE steam_id = @steam_id";
            await connection.ExecuteAsync(deleteLinkSql, new { steam_id = steamId });

            // Also clean up any active codes for this player
            var deleteCodesSql = "DELETE FROM discord_link_codes WHERE steam_id = @steam_id";
            await connection.ExecuteAsync(deleteCodesSql, new { steam_id = steamId });

            _core.Logger.LogInformation("Successfully unlinked player {SteamId} from Discord user {DiscordUserId} ({DiscordUsername})", 
                steamId, existingLink.DiscordUserId, existingLink.DiscordUsername);

            // Revoke permission if configured
            if (_config?.CurrentValue?.Permissions?.RevokeOnUnlink == true && !string.IsNullOrEmpty(_config?.CurrentValue?.Permissions?.LinkedPermission))
            {
                await RevokeLinkedPermissionAsync(steamId, _config.CurrentValue.Permissions.LinkedPermission);
            }

            return true;
        }
        catch (Exception ex)
        {
            _core.Logger.LogError(ex, "Error unlinking player {SteamId}", steamId);
            return false;
        }
    }

    /// <summary>
    /// Grant permission to a player when Discord account is linked
    /// </summary>
    public async Task<bool> GrantLinkedPermissionAsync(ulong steamId, string permission)
    {
        if (!IsEnabled) return false;

        try
        {
            // Check if player is online
            var player = _core.PlayerManager.GetAllPlayers().FirstOrDefault(p => p.SteamID == steamId);
            if (player == null)
            {
                _core.Logger.LogDebug("Player {SteamId} is not online, cannot grant permission", steamId);
                return false;
            }

            // Grant permission using SwiftlyS2's permission system
            _core.Permission.AddPermission(steamId, permission);
            
            _core.Logger.LogInformation("Granted permission '{Permission}' to player {SteamId} ({PlayerName})", 
                permission, steamId, player.Controller.PlayerName);
            
            return true;
        }
        catch (Exception ex)
        {
            _core.Logger.LogError(ex, "Error granting permission '{Permission}' to player {SteamId}", permission, steamId);
            return false;
        }
    }

    /// <summary>
    /// Revoke permission from a player when Discord account is unlinked
    /// </summary>
    public async Task<bool> RevokeLinkedPermissionAsync(ulong steamId, string permission)
    {
        if (!IsEnabled) return false;

        try
        {
            // Check if player is online
            var player = _core.PlayerManager.GetAllPlayers().FirstOrDefault(p => p.SteamID == steamId);
            if (player == null)
            {
                _core.Logger.LogDebug("Player {SteamId} is not online, cannot revoke permission", steamId);
                return false;
            }

            // Revoke permission using SwiftlyS2's permission system
            _core.Permission.RemovePermission(steamId, permission);
            
            _core.Logger.LogInformation("Revoked permission '{Permission}' from player {SteamId} ({PlayerName})", 
                permission, steamId, player.Controller.PlayerName);
            
            return true;
        }
        catch (Exception ex)
        {
            _core.Logger.LogError(ex, "Error revoking permission '{Permission}' from player {SteamId}", permission, steamId);
            return false;
        }
    }

    /// <summary>
    /// Get database statistics
    /// </summary>
    public async Task<DatabaseStats> GetStatsAsync()
    {
        if (!IsEnabled) return new DatabaseStats();

        try
        {
            using var connection = _core.Database.GetConnection(_connectionName);
            connection.Open();

            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            var totalCodes = await connection.QuerySingleOrDefaultAsync<int>("SELECT COUNT(*) FROM discord_link_codes");
            var activeCodes = await connection.QuerySingleOrDefaultAsync<int>(
                "SELECT COUNT(*) FROM discord_link_codes WHERE discord_user_id = 0 AND (expires_at = 0 OR expires_at > @now)", 
                new { now });
            var linkedCodes = await connection.QuerySingleOrDefaultAsync<int>("SELECT COUNT(*) FROM discord_link_codes WHERE discord_user_id > 0");
            var totalLinks = await connection.QuerySingleOrDefaultAsync<int>("SELECT COUNT(*) FROM discord_links");

            return new DatabaseStats
            {
                TotalCodes = totalCodes,
                ActiveCodes = activeCodes,
                LinkedCodes = linkedCodes,
                TotalLinks = totalLinks
            };
        }
        catch (Exception ex)
        {
            _core.Logger.LogError(ex, "Error getting database statistics");
            return new DatabaseStats();
        }
    }
}

/// <summary>
/// Database statistics
/// </summary>
public sealed class DatabaseStats
{
    public int TotalCodes { get; set; }
    public int ActiveCodes { get; set; }
    public int LinkedCodes { get; set; }
    public int TotalLinks { get; set; }
}
