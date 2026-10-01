namespace QiadHelper.Bot.Configuration;

/// <summary>
///     How QIAD staff drive the case workflow. The live QIAD helper uses slash commands; this
///     build shipped the same actions as buttons. The mode is a deployment-level choice because
///     command registration happens once at gateway-ready — changing it needs a restart.
/// </summary>
public enum StaffWorkflowMode
{
    /// <summary>Staff-action buttons on the queue/thread/forum posts; no workflow slash commands.</summary>
    Buttons,

    /// <summary>Workflow slash commands (/add-decision, /issue-verdict, …); no staff-action buttons.</summary>
    Commands,

    /// <summary>Both — buttons AND the workflow commands (useful during a transition).</summary>
    Both
}

/// <summary>Bound from the "Bot" configuration section.</summary>
public sealed class BotOptions
{
    public const string SectionName = "Bot";

    /// <summary>
    ///     False lets dev/staging run the web panel without touching Discord (design §7.1).
    ///     A missing bot token has the same effect, with a warning.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>The QIAD guild. DB config (key "MainGuildId") wins over this fallback.</summary>
    public ulong MainGuildId { get; set; }

    /// <summary>Commands register only here when the environment is Development.</summary>
    public ulong? DevelopmentGuildId { get; set; }

    /// <summary>
    ///     Whether staff drive the workflow via buttons, slash commands, or both.
    ///     Default <see cref="StaffWorkflowMode.Buttons" />. Changing it requires a restart.
    /// </summary>
    public StaffWorkflowMode StaffWorkflowMode { get; set; } = StaffWorkflowMode.Buttons;

    /// <summary>Fallback error-log channel; DB config (key "ErrorLogChannelId") wins.</summary>
    public ulong ErrorLogChannelId { get; set; }

    /// <summary>
    ///     Discord user IDs treated as Director regardless of guild roles. Bootstrap/dev escape
    ///     hatch: rank normally comes from the QiadRankRoles mapping, which is empty on a fresh
    ///     install and unavailable when the bot is offline.
    /// </summary>
    public List<ulong> DeveloperUserIds { get; set; } = [];
}
