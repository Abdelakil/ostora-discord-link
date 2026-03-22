import discord
import mysql.connector
import os
import time
from discord.ext import commands, tasks

# --- CONFIG ---
TOKEN = os.getenv('DISCORD_TOKEN')  # Set this environment variable
DB_HOST = "your_db_host"           # Replace with your database host
DB_USER = os.getenv('DB_USER')    # Set this environment variable  
DB_PASS = os.getenv('DB_PASS')    # Set this environment variable
LINKED_ROLE_ID = 1485267755350495302  # Replace with your Discord role ID
LOG_CHANNEL_ID = 1485277470650536026   # Replace with your log channel ID

intents = discord.Intents.default()
intents.message_content = True
intents.members = True 
bot = commands.Bot(command_prefix="!", intents=intents)

def get_conn(db_name=None):
    return mysql.connector.connect(
        host=DB_HOST, user=DB_USER, password=DB_PASS, database=db_name
    )

async def log_to_discord(message):
    """Helper to send logs to your specific Discord channel"""
    channel = bot.get_channel(LOG_CHANNEL_ID)
    if channel:
        await channel.send(f"📋 **[System Log]** {message}")

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

# --- TASK: AUTO-REMOVE ROLE IF UNLINKED IN-GAME ---
@tasks.loop(minutes=5)
async def check_links_integrity():
    try:
        sys_conn = get_conn("information_schema")
        cursor = sys_conn.cursor()
        cursor.execute("SELECT TABLE_SCHEMA FROM TABLES WHERE TABLE_NAME = 'discord_links'")
        db_list = [row[0] for row in cursor.fetchall()]
        sys_conn.close()

        all_linked_ids = set()
        for db in db_list:
            conn = get_conn(db)
            cursor = conn.cursor()
            cursor.execute("SELECT discord_user_id FROM discord_links")
            for row in cursor.fetchall():
                all_linked_ids.add(row[0])
            conn.close()

        for guild in bot.guilds:
            role = guild.get_role(LINKED_ROLE_ID)
            if not role: continue
            
            for member in role.members:
                if member.id not in all_linked_ids:
                    await member.remove_roles(role)
                    await log_to_discord(f"⚠️ Removed stale role from **{member.name}**.")
    except Exception as e:
        print(f"Integrity check error: {e}")

# --- EVENT: CLEAN UP DB IF USER LEAVES DISCORD ---
@bot.event
async def on_member_remove(member):
    try:
        sys_conn = get_conn("information_schema")
        cursor = sys_conn.cursor()
        cursor.execute("SELECT TABLE_SCHEMA FROM TABLES WHERE TABLE_NAME = 'discord_links'")
        db_list = [row[0] for row in cursor.fetchall()]
        sys_conn.close()

        for db in db_list:
            conn = get_conn(db)
            cursor = conn.cursor()
            cursor.execute("SELECT steam_id, player_name FROM discord_links WHERE discord_user_id = %s", (member.id,))
            result = cursor.fetchone()
            
            if result:
                steam_id, player_name = result
                cursor.execute("DELETE FROM discord_links WHERE discord_user_id = %s", (member.id,))
                conn.commit()
                
                # CREATE EVENT FOR REAL-TIME SYNC
                create_link_event(db, steam_id, 'unlink', member.id, str(member))
                
            conn.close()
        
        await log_to_discord(f"👤 **{member.name}** left Discord. User not more linked. Event created for real-time sync.")
    except Exception as e:
        print(f"Leave cleanup error: {e}")

@bot.event
async def on_ready():
    print(f'✅ Logged in as {bot.user.name}')
    if not check_links_integrity.is_running():
        check_links_integrity.start()
    await log_to_discord("🚀 **Bot is Online** and monitoring databases.")
    
# --- COMMAND: LINK ---
@bot.command()
async def link(ctx, code: str):
    now = int(time.time())
    found_db = None
    player_name = None

    try:
        sys_conn = get_conn("information_schema")
        cursor = sys_conn.cursor()
        cursor.execute("SELECT TABLE_SCHEMA FROM TABLES WHERE TABLE_NAME = 'discord_link_codes'")
        db_list = [row[0] for row in cursor.fetchall()]
        sys_conn.close()

        for db_name in db_list:
            try:
                conn = get_conn(db_name)
                cursor = conn.cursor(dictionary=True)
                cursor.execute("SELECT steam_id, player_name FROM discord_link_codes WHERE code = %s AND (expires_at > %s OR expires_at = 0)", (code, now))
                result = cursor.fetchone()

                if result:
                    steam_id = result['steam_id']
                    player_name = result['player_name']
                    
                    # Check if this is a new link or relink
                    cursor.execute("SELECT steam_id FROM discord_links WHERE steam_id = %s", (steam_id,))
                    existing_link = cursor.fetchone()
                    action = 'relink' if existing_link else 'link'
                    
                    cursor.execute("""
                        INSERT INTO discord_links (steam_id, discord_user_id, player_name, discord_username, linked_at)
                        VALUES (%s, %s, %s, %s, %s)
                        ON DUPLICATE KEY UPDATE discord_user_id = %s, discord_username = %s, linked_at = %s
                    """, (steam_id, ctx.author.id, player_name, str(ctx.author), now, ctx.author.id, str(ctx.author), now))
                    cursor.execute("UPDATE discord_link_codes SET discord_user_id = %s, discord_username = %s, linked_at = %s WHERE code = %s", (ctx.author.id, str(ctx.author), now, code))
                    conn.commit()
                    
                    # CREATE EVENT FOR REAL-TIME SYNC
                    create_link_event(db_name, steam_id, action, ctx.author.id, str(ctx.author))
                    
                    found_db = db_name
                    conn.close()
                    break 
                conn.close()
            except Exception as e:
                print(f"Error checking {db_name}: {e}")

        if found_db:
            role = ctx.guild.get_role(LINKED_ROLE_ID)
            if role:
                await ctx.author.add_roles(role)
                await ctx.send(f"✅ **Linked!** {player_name}, you've been given the **{role.name}** role.")
                await log_to_discord(f"🔗 **{player_name}** {action}ed successfully. Event created for real-time sync.")
            else:
                await ctx.send(f"✅ **Linked!** (Role ID error).")
        else:
            await ctx.send("❌ **Invalid/Expired Code.**")
    except Exception as e:
        await log_to_discord(f"❌ **DB Error during link:** {e}")
        await ctx.send("⚠️ Database error.")

bot.run(TOKEN)
