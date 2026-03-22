# 🎮 OSTORA Discord Link Plugin

A high-performance **CS2 SwiftlyS2 plugin** that generates unique linking codes for Discord integration. This plugin serves as the in-game component for a complete Steam-to-Discord linking system with database persistence and stateful code management.

---

## 🚀 Key Features

### **Stateful Linking System**
- **Persistent Codes**: Same player gets same code across server restarts
- **15-Minute Expiry**: Automatic code expiration with real-time countdown
- **Database Storage**: Full MySQL/PostgreSQL/SQLite integration
- **Multi-Database Support**: Tracks database instance with `db_name` column

### **Advanced Functionality**
- **Configurable Command**: Default `!link` command (customizable)
- **Real-time Expiry**: Shows remaining time (e.g., "14m 30s")
- **Comprehensive Logging**: Full debugging and error tracking
- **Automatic Cleanup**: Purges expired codes and old records

### **Production Ready**
- **Optimized Performance**: Efficient database queries with proper indexing
- **Error Handling**: Graceful fallbacks when database unavailable
- **Modular Architecture**: Clean separation of concerns
- **Cross-Platform**: Supports MySQL, PostgreSQL, and SQLite

---

## 🛠️ Database Schema

The plugin creates and manages two tables for complete linking functionality:

### 1. `discord_link_codes` (Temporary Codes)
| Column | Type | Description |
| :--- | :--- | :--- |
| **code** | VARCHAR(32) | **Primary Key** - 8-character link code |
| **steam_id** | BIGINT | Player's SteamID64 |
| **player_name** | VARCHAR(64) | Player name at code generation |
| **created_at** | INT | Unix timestamp when code was created |
| **expires_at** | INT | Unix timestamp when code expires (0 = never) |
| **discord_user_id** | BIGINT | Discord user ID (0 = not linked) |
| **linked_at** | INT | Unix timestamp when linked (0 = not linked) |
| **discord_username** | VARCHAR(64) | Discord username (empty = not linked) |
| **db_name** | VARCHAR(64) | Database instance name for tracking |

### 2. `discord_links` (Permanent Associations)
| Column | Type | Description |
| :--- | :--- | :--- |
| **steam_id** | BIGINT | **Primary Key** - SteamID64 |
| **discord_user_id** | BIGINT | Discord user ID |
| **discord_username** | VARCHAR(64) | Discord username |
| **player_name** | VARCHAR(128) | Player name at linking |
| **linked_at** | INT | Unix timestamp when linked |

### 3. `discord_link_events` (Real-time Sync Events)
| Column | Type | Description |
| :--- | :--- | :--- |
| **id** | BIGINT | **Primary Key** - Auto increment |
| **steam_id** | BIGINT | Player's SteamID64 |
| **action** | VARCHAR(16) | Event type: 'link', 'unlink', 'relink' |
| **discord_user_id** | BIGINT | Discord user ID |
| **discord_username** | VARCHAR(64) | Discord username |
| **permission** | VARCHAR(128) | Permission to grant/revoke |
| **created_at** | BIGINT | Unix timestamp when event created |
| **processed** | BOOLEAN | Whether event has been processed |
| **player_name** | VARCHAR(64) | Player name at time of linking |
| **discord_username** | VARCHAR(64) | Discord username at time of linking |
| **linked_at** | INT | Unix timestamp of link creation |

### **Database Indexes**
- `idx_link_codes_steam` - Fast lookup by Steam ID
- `idx_link_codes_discord` - Fast lookup by Discord ID
- `idx_link_codes_expires` - Find expired codes
- `idx_links_steam` - Fast Steam ID lookups
- `idx_links_discord` - Fast Discord ID lookups
- `idx_events_processed` - Find unprocessed events
- `idx_events_steam` - Fast event lookups by Steam ID

---

## � Deployment (Docker / Portainer)
*Recommended for VPS environments using Portainer.*

1.  **Prepare Files:** Place `bot.py` in `/opt/ostora/bot/`.
2.  **Create Stack:** In Portainer, create a new stack named `ostora-bot` with the following Compose:

```yaml
version: '3.8'
services:
  bot:
    image: python:3.11-slim
    container_name: ostora_discord_bot
    restart: unless-stopped
    volumes:
      - /opt/ostora/bot:/app
    working_dir: /app
    command: sh -c "pip install discord.py mysql-connector-python && python bot.py"
    environment:
      DISCORD_TOKEN: "YOUR_BOT_TOKEN"
      DB_USER: "pterodactyl"
      DB_PASS: "YOUR_DB_PASSWORD"
    networks:
      - pterodactyl_net

networks:
  pterodactyl_net:
    external: true
```

---

## �️ Deployment (Non-Docker / Standard Linux)
Use this if running directly on the host OS.

**Install Dependencies:**

```bash
sudo apt update && sudo apt install python3-pip -y
pip install discord.py mysql-connector-python
```

**Edit bot.py:**
Change DB_HOST = "pterodactyl_db" to DB_HOST = "127.0.0.1".

**Setup Environment Variables:**

```bash
export DISCORD_TOKEN="YOUR_BOT_TOKEN"
export DB_USER="pterodactyl"
export DB_PASS="YOUR_DB_PASSWORD"
```

**Run with Systemd (For 24/7 Uptime):**
Create a service file: `sudo nano /etc/systemd/system/ostora-bot.service`

```ini
[Unit]
Description=Ostora Discord Link Bot
After=network.target

[Service]
User=root
WorkingDirectory=/opt/ostora/bot
ExecStart=/usr/bin/python3 bot.py
Restart=always
# Pass Env Vars here if not using a .env file
Environment=DISCORD_TOKEN=YOUR_TOKEN
Environment=DB_USER=pterodactyl
Environment=DB_PASS=YOUR_PASS

[Install]
WantedBy=multi-user.target
```

```bash
sudo systemctl enable --now ostora-bot
```

---

## �📋 Installation

### **Prerequisites**
- **SwiftlyS2** CS2 server framework
- **Database** (MySQL, PostgreSQL, or SQLite)
- **Database Connection** configured in SwiftlyS2's `database.jsonc`

### **Steps**
1. **Download Plugin**: Get the latest `OSTORA-Discord-Link.dll` from releases
2. **Install Plugin**: Copy to `addons/swiftly/plugins/` directory
3. **Configure Database**: Ensure database connection exists in SwiftlyS2
4. **Configure Plugin**: Copy and edit `config.json`
5. **Restart Server**: Restart CS2 server or reload plugins

---

## ⚙️ Configuration

Edit `config.json` in the plugin directory:

```json
{
  "OstoraDiscordLink": {
    "Command": "!link",
    "CodeLength": 8,
    "MessagePrefix": "[OSTORA]",
    "CodeMessage": "Your link code is: {0}",
    "Database": {
      "Connection": "default_connection",
      "PurgeDays": 30
    },
    "CodeSettings": {
      "ExpiryMinutes": 15,
      "MaxGenerationAttempts": 10
    }
  }
}
```

### **Configuration Options**

#### **Basic Settings**
- **Command**: Chat command for code generation (default: `"!link"`)
- **CodeLength**: Length of generated codes (default: `8`, max: `32`)
- **MessagePrefix**: Chat message prefix (default: `"[OSTORA]"`)
- **CodeMessage**: Message format with `{0}` placeholder for code

#### **Database Settings**
- **Connection**: Database connection name from SwiftlyS2 config (default: `"default_connection"`)
- **PurgeDays**: Days to keep old records (default: `30`, `0` = forever)

#### **Code Settings**
- **ExpiryMinutes**: Code expiry time in minutes (default: `15`, `0` = never expires)
- **MaxGenerationAttempts**: Max attempts to generate unique code (default: `10`)

---

## 🎮 Usage

### **Player Experience**
1. **Generate Code**: Player types `!link` in chat
2. **Receive Code**: Plugin displays unique code with expiry time
3. **Stateful Behavior**: Same player gets same code until expiry or linking
4. **Expiry Display**: Shows real-time countdown (e.g., "expires: 12m 45s")

### **Chat Examples**
```
[OSTORA] Your link code is: ABCDEFGH (expires: 14m 30s)
[OSTORA] You already have an active code: ABCDEFGH (expires: 12m 15s)
[OSTORA] You are already linked to Discord: PlayerName (123456789)
[OSTORA] Database is not available. Please contact an administrator.
```

### **Admin Features**
- **Database Statistics**: View linking stats in server logs
- **Error Logging**: Comprehensive error tracking and debugging
- **Configuration Hot-Reload**: Changes apply without server restart

---

## 🏗️ Architecture

### **Plugin Structure**
```
Plugin/
├── Plugin.cs              # Main plugin initialization
├── Config/
│   └── PluginConfig.cs     # Configuration models
├── Database/
│   ├── DatabaseService.cs  # Database operations
│   ├── Models/
│   │   ├── LinkCode.cs     # Link code model
│   │   └── DiscordLink.cs  # Discord link model
└── Services/
    ├── CommandService.cs   # Chat command handling
    └── CodeGenerationService.cs # Legacy code generation
```

### **Key Components**

#### **DatabaseService**
- **Connection Management**: Handles database connections and pooling
- **Code Management**: Creates, retrieves, and manages link codes
- **Link Operations**: Manages permanent Discord associations
- **Automatic Cleanup**: Purges expired codes and old records

#### **CommandService**
- **Chat Processing**: Intercepts and processes player chat commands
- **State Management**: Enforces one-code-per-player rule
- **User Feedback**: Provides informative chat messages
- **Error Handling**: Graceful handling of database issues

#### **LinkCode Model**
- **Data Validation**: Ensures data integrity and constraints
- **Computed Properties**: Expiry calculations and display formatting
- **Database Mapping**: ORM integration with proper column mapping

---

## 🔧 Database Integration

### **Supported Databases**
- **MySQL/MariaDB**: Production-ready with full feature support
- **PostgreSQL**: Advanced features and performance optimization
- **SQLite**: Lightweight option for single-server deployments

### **Connection Configuration**
Database connections are managed through SwiftlyS2's `database.jsonc`:

```json
{
  "connections": {
    "default_connection": {
      "type": "mysql",
      "host": "localhost",
      "port": 3306,
      "database": "game_database",
      "username": "db_user",
      "password": "db_password"
    }
  }
}
```

### **Automatic Setup**
- **Table Creation**: Automatically creates tables on first startup
- **Schema Updates**: Safely adds new columns to existing tables
- **Index Creation**: Optimizes performance with proper indexes
- **Error Recovery**: Handles setup failures gracefully

---

## 📊 Monitoring & Logging

### **Log Levels**
- **Information**: Normal operations (code generation, linking)
- **Warning**: Non-critical issues (database unavailable)
- **Error**: Critical failures (database connection lost)
- **Debug**: Detailed troubleshooting information

### **Key Log Messages**
```
Database connection test successful - Type: MySqlConnection
Generated link code 'ABCDEFGH' for player Player (76561197960287930)
Player 76561197960287930 already has active code ABCDEFGH
Successfully linked code 'ABCDEFGH' - Steam: 76561197960287930 to Discord: Player#1234
Database Stats - Total codes: 150, Active: 25, Linked: 125, Links: 125
```

---

## 🚀 Performance & Scalability

### **Optimizations**
- **Connection Pooling**: Efficient database connection management
- **Indexed Queries**: Fast lookups for Steam ID and Discord ID
- **Batch Operations**: Efficient cleanup and maintenance tasks
- **Memory Management**: Minimal memory footprint

### **Scalability Features**
- **Multi-Database**: Supports multiple game server databases
- **High Concurrency**: Handles multiple simultaneous code requests
- **Automatic Cleanup**: Prevents database bloat over time
- **Configurable Retention**: Flexible data retention policies

---

## 🔍 Troubleshooting

### **Common Issues**

#### **Database Connection Failed**
- **Check**: Database connection name in config
- **Verify**: SwiftlyS2 `database.jsonc` configuration
- **Test**: Database server accessibility and credentials

#### **Code Generation Fails**
- **Check**: Database service initialization logs
- **Verify**: Database permissions (SELECT, INSERT, UPDATE)
- **Test**: Database table creation and structure

#### **Expiry Not Working**
- **Check**: `ExpiryMinutes` configuration setting
- **Verify**: Server time synchronization
- **Test**: Database `expires_at` column values

### **Debug Mode**
Enable detailed logging by setting log level to `Debug` in SwiftlyS2 configuration.

---

## 🤝 Discord Integration

This plugin is designed to work with a complementary Discord bot that:
- **Scans Databases**: Automatically discovers databases with `discord_link_codes` tables
- **Processes Links**: Validates codes and creates permanent associations
- **Assigns Roles**: Automatically assigns Discord roles upon successful linking
- **Multi-Tenant**: Handles multiple game servers from one bot instance

### **Bot Requirements**
- **Message Content Intent**: For processing verification commands
- **Server Members Intent**: For role assignment
- **Database Access**: Read/write access to game databases
- **Role Hierarchy**: Bot role must be higher than assigned roles

### **Real-time Event Integration**
The bot must create events in the `discord_link_events` table for real-time permission synchronization:

```python
def create_link_event(db_name, steam_id, action, discord_user_id, discord_username, permission='ostora.chatguard.use'):
    """Create a link event for real-time sync with game server"""
    try:
        conn = get_conn(db_name)
        cursor = conn.cursor()
        cursor.execute("""
            INSERT INTO discord_link_events (steam_id, action, discord_user_id, discord_username, permission, created_at, processed)
            VALUES (%s, %s, %s, %s, %s, UNIX_TIMESTAMP(), FALSE)
        """, (steam_id, action, discord_user_id, discord_username, permission))
        conn.commit()
        conn.close()
        print(f"Created link event: {action} for Steam {steam_id}, Discord {discord_user_id}")
    except Exception as e:
        print(f"Error creating link event: {e}")
```

### **Complete Bot Example**
See `bot-example.py` for a complete implementation with:
- **Event Creation**: Automatic event creation for link/unlink/relink actions
- **Real-time Sync**: 2-second permission updates to game server
- **Error Handling**: Comprehensive error handling and logging
- **Multi-Database Support**: Handles multiple game servers
- **Role Management**: Automatic Discord role assignment

### **Bot Setup Instructions**
1. **Install Dependencies**: `pip install discord.py mysql-connector-python`
2. **Environment Variables**:
   ```bash
   export DISCORD_TOKEN="your_bot_token"
   export DB_USER="your_db_user"
   export DB_PASS="your_db_password"
   ```
3. **Configure Settings**: Update `DB_HOST`, `LINKED_ROLE_ID`, `LOG_CHANNEL_ID`
4. **Deploy Bot**: Run with Python 3.8+ and ensure database access

---

## 📝 License

This plugin is part of the **OSTORA project** and is released under the project's license terms.

---

## 🤝 Contributing

Contributions are welcome! Please ensure:
- **Code Quality**: Follow existing code patterns and conventions
- **Testing**: Test changes thoroughly before submission
- **Documentation**: Update documentation for new features
- **Compatibility**: Maintain backward compatibility where possible

---

## 🆘 Support

For support and issues:
1. **Check Logs**: Review server logs for detailed error information
2. **Verify Configuration**: Ensure all settings are correct
3. **Test Database**: Verify database connectivity and permissions
4. **Report Issues**: Include logs and configuration details in bug reports
