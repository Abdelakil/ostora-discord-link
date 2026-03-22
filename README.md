# OSTORA Discord Link

A SwiftlyS2 plugin for Counter-Strike 2 that generates unique linking codes for Discord integration.

## Features

- Generate unique, random alphanumeric codes
- Configurable command and code length
- Modular and extensible architecture
- Clean separation of concerns

## Installation

1. Copy the compiled plugin to your CS2 server's `addons/swiftly/plugins/` directory
2. Copy `config.example.json` to `config.json` and configure as needed
3. Restart your server or reload the plugin

## Configuration

Edit `config.json` in the plugin directory:

```json
{
  "Command": "!link",
  "CodeLength": 8,
  "MessagePrefix": "[OSTORA]",
  "CodeMessage": "Your link code is: {0}"
}
```

### Settings

- **Command**: Chat command that triggers code generation (default: "!link")
- **CodeLength**: Length of the generated code (default: 8, max: 32)
- **MessagePrefix**: Prefix for chat messages (default: "[OSTORA]")
- **CodeMessage**: Message format for displaying the code (default: "Your link code is: {0}")

## Usage

Players can type `!link` in chat to receive a unique linking code. The code will be displayed in their chat window.

## Architecture

The plugin uses a modular design with the following components:

- **Plugin.cs**: Main plugin class that handles initialization
- **Config/PluginConfig.cs**: Configuration model
- **Services/CodeGenerationService.cs**: Handles secure random code generation
- **Services/CommandService.cs**: Processes chat commands and responds to players

## Code Generation

Codes are generated using cryptographically secure random generation with:
- Uppercase letters (excluding ambiguous characters like I, O)
- Numbers (excluding 0, 1 to avoid confusion)
- Configurable length between 1-32 characters

## Future Enhancements

The modular design allows for easy addition of features like:
- Discord bot integration
- Code expiration
- Database storage
- Player linking management

## License

This plugin is part of the OSTORA project.
