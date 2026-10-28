using System.Text.Json.Serialization;

namespace AcDream.Launcher.Core.Profiles;

public sealed class ServerProfile
{
    [JsonRequired]
    public string Name { get; set; } = string.Empty;

    [JsonRequired]
    public string Host { get; set; } = string.Empty;

    [JsonRequired]
    public int Port { get; set; }

    public List<AccountProfile> Accounts { get; set; } = [];

    /// <summary>True once this server's account list has been created or reviewed by this launcher.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool AccountListInitialized { get; set; }
}
