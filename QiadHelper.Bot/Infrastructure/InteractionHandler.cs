using System.Reflection;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using QiadHelper.Bot.Configuration;
using QiadHelper.Bot.Modules;
using QiadHelper.Bot.Services;
using QiadHelper.Core.Data.Configuration;
using QiadHelper.Core.Services;

namespace QiadHelper.Bot.Infrastructure;

/// <summary>
///     Command registry and central interaction router (copied from QDF, collapsed to one guild).
///     Modules are discovered by assembly scan; commands are registered per guild on Ready
///     (dev guild in Development, the QIAD guild otherwise), wiping stale commands first.
/// </summary>
public sealed class InteractionHandler
{
    // Slash commands that should defer ephemerally (private responses). Includes the direct-action
    // workflow commands (they call the service then FollowupAsync).
    private static readonly HashSet<string> _ephemeralDeferGroups = new(StringComparer.OrdinalIgnoreCase)
                                                                    {
                                                                        "restrict",
                                                                        "unrestrict",
                                                                        "restrictions",
                                                                        "qiad-sync-panels",
                                                                        "open-case",
                                                                        "claim-case",
                                                                        "overturn",
                                                                        "execute",
                                                                        "deny-void"
                                                                    };

    // Slash commands that send their OWN first response (a modal, or an ephemeral error) and so
    // must NOT be auto-deferred — RespondAsync / RespondWithModalAsync require a fresh,
    // un-acknowledged interaction. These are the modal-opening workflow commands.
    private static readonly HashSet<string> _modalOpeningSlashCommands = new(StringComparer.OrdinalIgnoreCase)
                                                                         {
                                                                             "add-decision",
                                                                             "issue-verdict",
                                                                             "reject-case",
                                                                             "close-misc",
                                                                             "void-request",
                                                                             "misc-file"
                                                                         };

    // Context commands that send their OWN first response and therefore must NOT be
    // auto-deferred. QIAD currently has no such context commands; add names here when one appears.
    private static readonly HashSet<string> _modalOpeningContextCommands = new(StringComparer.OrdinalIgnoreCase);

    // The workflow-command module, captured at InitializeAsync; registered to Discord only when
    // StaffWorkflowMode != Buttons.
    private ModuleInfo? _workflowModule;

    private readonly IChannelConfigService _channelConfig;
    private readonly DiscordSocketClient _client;
    private readonly IHostEnvironment _env;
    private readonly IBotErrorLogService _errorLog;
    private readonly InteractionService _interactions;
    private readonly ILogger<InteractionHandler> _logger;
    private readonly BotOptions _options;
    private readonly IServiceProvider _services;

    public InteractionHandler(
        DiscordSocketClient client,
        InteractionService interactions,
        IServiceProvider services,
        IOptions<BotOptions> options,
        IHostEnvironment env,
        IChannelConfigService channelConfig,
        IBotErrorLogService errorLog,
        ILogger<InteractionHandler> logger)
    {
        _client = client;
        _interactions = interactions;
        _services = services;
        _logger = logger;
        _options = options.Value;
        _env = env;
        _channelConfig = channelConfig;
        _errorLog = errorLog;
    }

    public async Task InitializeAsync()
    {
        _client.InteractionCreated += HandleInteractionAsync;
        _interactions.Log += OnLogAsync;

        // Add every module so their component/modal handlers are always live at runtime; the
        // workflow module's SLASH COMMANDS are then registered (or not) with Discord per mode.
        await _interactions.AddModulesAsync(Assembly.GetExecutingAssembly(), _services);
        _workflowModule = _interactions.Modules
            .FirstOrDefault(m => m.Name == nameof(WorkflowCommandModule));
    }

    public async Task RegisterCommandsAsync()
    {
        try
        {
            // Guild ID from database config (persisted) wins, fallback to appsettings
            var mainGuildId = await GetConfigValueAsync(QiadConfigKeys.MainGuildId, _options.MainGuildId);

            // Always clear all existing commands first to prevent duplicates
            await UnregisterAllCommandsAsync(mainGuildId);

            var isDev = _env.IsDevelopment();
            var devGuildId = _options.DevelopmentGuildId ?? 0UL;

            var targetGuildId = isDev && devGuildId != 0 ? devGuildId : mainGuildId;

            // Register the workflow slash commands only when staff use them. Their component/modal
            // handlers stay live regardless (they were added in InitializeAsync); we only omit the
            // module from Discord command registration in Buttons mode.
            var registerWorkflow = _options.StaffWorkflowMode != StaffWorkflowMode.Buttons;
            var modules = registerWorkflow || _workflowModule == null
                ? _interactions.Modules.ToArray()
                : _interactions.Modules.Where(m => m != _workflowModule).ToArray();

            if (targetGuildId != 0)
            {
                await _interactions.AddModulesToGuildAsync(targetGuildId, true, modules);
                _logger.LogInformation(
                    "Registered {Count} module(s) to guild {GuildId} (workflow commands={Workflow}, dev={IsDev})",
                    modules.Length, targetGuildId, registerWorkflow, isDev);
            }
            else
            {
                // Fallback: register globally if no guild is configured
                _logger.LogWarning(
                    "No guild ID configured in database or appsettings - registering commands globally. " +
                    "Configure MainGuildId via the admin panel.");
                await _interactions.AddModulesGloballyAsync(true, modules);
                _logger.LogInformation(
                    "Registered {Count} module(s) globally (workflow commands={Workflow})",
                    modules.Length, registerWorkflow);
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to register interaction commands");
        }
    }

    /// <summary>
    ///     Gets a configuration value from the database, falling back to a default value.
    /// </summary>
    private async Task<ulong> GetConfigValueAsync(string key, ulong fallback)
    {
        try
        {
            var dbValue = await _channelConfig.GetChannelIdAsync(key);
            if (dbValue != 0)
            {
                _logger.LogDebug("Using {Key} {Value} from database configuration", key, dbValue);

                return dbValue;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch {Key} from database", key);
        }

        if (fallback != 0)
        {
            _logger.LogDebug("Using {Key} {Value} from fallback (database not configured)", key, fallback);
        }

        return fallback;
    }

    private async Task UnregisterAllCommandsAsync(ulong mainGuildId)
    {
        var globalDeleted = 0;
        var guildDeleted = 0;

        // Clear global commands
        try
        {
            var existingGlobal = await _client.Rest.GetGlobalApplicationCommands();
            foreach (var cmd in existingGlobal)
            {
                try
                {
                    await cmd.DeleteAsync();
                    globalDeleted++;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed deleting global command {CommandName}", cmd.Name);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed fetching global application commands for deletion");
        }

        // Clear commands from all guilds we may have registered to in the past
        var guildsToClean = new HashSet<ulong>();

        if (_options.DevelopmentGuildId is > 0)
        {
            guildsToClean.Add(_options.DevelopmentGuildId.Value);
        }

        if (_options.MainGuildId != 0)
        {
            guildsToClean.Add(_options.MainGuildId);
        }

        if (mainGuildId != 0)
        {
            guildsToClean.Add(mainGuildId);
        }

        foreach (var guildId in guildsToClean)
        {
            try
            {
                var existingGuild = await _client.Rest.GetGuildApplicationCommands(guildId);
                foreach (var cmd in existingGuild)
                {
                    try
                    {
                        await cmd.DeleteAsync();
                        guildDeleted++;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed deleting guild command {CommandName} for {GuildId}",
                            cmd.Name, guildId);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed fetching guild application commands for {GuildId}", guildId);
            }
        }

        _logger.LogInformation(
            "Unregistered {GlobalCount} global and {GuildCount} guild commands before re-registration.",
            globalDeleted, guildDeleted);
    }

    private async Task HandleInteractionAsync(SocketInteraction interaction)
    {
        try
        {
            // Determine if this command should defer ephemerally
            var shouldDeferEphemeral = false;
            string? commandName = null;

            if (interaction is SocketSlashCommand slash)
            {
                shouldDeferEphemeral = _ephemeralDeferGroups.Contains(slash.CommandName);
                commandName = slash.CommandName;

                // Include subcommand if present
                var subCommand = slash.Data.Options?.FirstOrDefault()?.Name;
                if (!string.IsNullOrEmpty(subCommand))
                {
                    commandName = $"{slash.CommandName} {subCommand}";
                }
            }

            // Global defer for all application commands (slash, user, message) not yet acknowledged.
            // Modal-opening slash commands are exempt: they must own their first response.
            if (interaction.Type == InteractionType.ApplicationCommand && interaction is SocketSlashCommand slashCmd &&
                !slashCmd.HasResponded && !_modalOpeningSlashCommands.Contains(slashCmd.CommandName))
            {
                try { await slashCmd.DeferAsync(shouldDeferEphemeral); }
                catch
                {
                }
            }
            else if (interaction.Type == InteractionType.ApplicationCommand &&
                     interaction is SocketUserCommand userCmd && !userCmd.HasResponded
                     && !_modalOpeningContextCommands.Contains(userCmd.CommandName))
            {
                try { await userCmd.DeferAsync(true); }
                catch { }
            }
            else if (interaction.Type == InteractionType.ApplicationCommand &&
                     interaction is SocketMessageCommand msgCmd && !msgCmd.HasResponded
                     && !_modalOpeningContextCommands.Contains(msgCmd.CommandName))
            {
                try { await msgCmd.DeferAsync(true); }
                catch { }
            }

            var ctx = new SocketInteractionContext(_client, interaction);
            var result = await _interactions.ExecuteCommandAsync(ctx, _services);

            if (!result.IsSuccess)
            {
                _logger.LogWarning("Interaction execution failed: {Reason}", result.ErrorReason);

                var errorContext = BotErrorContext.FromInteraction(interaction, commandName);

                await _errorLog.LogErrorAsync(
                    "Command Execution Failed",
                    result.ErrorReason ?? "Unknown error",
                    result is ExecuteResult execResult ? execResult.Exception : null,
                    errorContext);

                if (interaction is IDiscordInteraction discordInt && discordInt.HasResponded)
                {
                    // Follow up since we deferred
                    await ctx.Interaction.FollowupAsync($"Error: {result.ErrorReason}", ephemeral: true);
                }
                else if (interaction is IDiscordInteraction discordInt2 && !discordInt2.HasResponded)
                {
                    // If defer failed earlier, send initial response
                    await discordInt2.RespondAsync($"Error: {result.ErrorReason}", ephemeral: true);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception while handling interaction");

            await _errorLog.LogErrorAsync(
                "Unhandled Bot Exception",
                ex.Message,
                ex,
                BotErrorContext.FromInteraction(interaction));

            try
            {
                if (interaction is IDiscordInteraction di)
                {
                    if (di.HasResponded)
                    {
                        await di.FollowupAsync("An unexpected error occurred.", ephemeral: true);
                    }
                    else
                    {
                        await di.RespondAsync("An unexpected error occurred.", ephemeral: true);
                    }
                }
            }
            catch { }
        }
    }

    private Task OnLogAsync(LogMessage msg)
    {
        var level = msg.Severity switch
        {
            LogSeverity.Critical => LogLevel.Critical,
            LogSeverity.Error    => LogLevel.Error,
            LogSeverity.Warning  => LogLevel.Warning,
            LogSeverity.Info     => LogLevel.Information,
            LogSeverity.Verbose  => LogLevel.Debug,
            LogSeverity.Debug    => LogLevel.Trace,
            _                    => LogLevel.Debug
        };
        _logger.Log(level, msg.Exception, "InteractionService: {Message}", msg.Message);

        return Task.CompletedTask;
    }
}
