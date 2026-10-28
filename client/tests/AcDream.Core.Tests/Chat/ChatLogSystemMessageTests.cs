using AcDream.Core.Chat;

namespace AcDream.Core.Tests.Chat;

public sealed class ChatLogSystemMessageTests
{
    [Fact]
    public void OnSystemMessage_IdenticalMessages_PreservesBothEntries()
    {
        var log = new ChatLog();
        log.OnSystemMessage("Unknown command: help", chatType: 0);
        log.OnSystemMessage("Unknown command: help", chatType: 0);

        Assert.Equal(2, log.Snapshot().Length);
    }

    [Fact]
    public void OnSystemMessage_DifferentText_BothEntries()
    {
        var log = new ChatLog();
        log.OnSystemMessage("Welcome to Asheron's Call", chatType: 0);
        log.OnSystemMessage("Use @acecommands to get a complete list of commands.", chatType: 0);

        // Different text — both retained even back-to-back.
        Assert.Equal(2, log.Snapshot().Length);
    }

    [Fact]
    public void OnSystemMessage_IdenticalBurst_PreservesEveryEntry()
    {
        var log = new ChatLog();
        log.OnSystemMessage("Unknown command: help", chatType: 0);
        log.OnSystemMessage("Unknown command: help", chatType: 0);
        log.OnSystemMessage("Unknown command: help", chatType: 0);

        Assert.Equal(3, log.Snapshot().Length);
    }

    [Fact]
    public void OnSystemMessage_RepeatedTextAroundOtherMessage_PreservesOrder()
    {
        var log = new ChatLog();
        log.OnSystemMessage("Unknown command: help", chatType: 0);
        log.OnSystemMessage("Welcome to Asheron's Call", chatType: 0);
        log.OnSystemMessage("Unknown command: help", chatType: 0);

        Assert.Equal(new[] { "Unknown command: help", "Welcome to Asheron's Call", "Unknown command: help" },
            log.Snapshot().Select(e => e.Text));
    }

    [Fact]
    public void OnSystemMessage_IdenticalTextWithDifferentTypes_PreservesBothTypes()
    {
        var log = new ChatLog();
        log.OnSystemMessage("Additional damage: 5", (uint)RetailLogTextType.CombatSelf);
        log.OnSystemMessage("Additional damage: 5", (uint)RetailLogTextType.CombatEnemy);

        Assert.Equal(new[] { (uint)RetailLogTextType.CombatSelf, (uint)RetailLogTextType.CombatEnemy },
            log.Snapshot().Select(e => e.LogTextType));
    }

    [Fact]
    public void RepeatedServerDamageBetweenOrdinaryHits_PreservesAllFourLines()
    {
        var log = new ChatLog();
        var combat = new AcDream.Core.Combat.CombatState();
        using var translator = new CombatChatTranslator(combat, log);

        combat.OnAttackerNotification("Target", AcDream.Core.Combat.CombatHitAdjectives.Slash, 10, 0.1);
        log.OnSystemMessage("Additional damage: 5", (uint)RetailLogTextType.CombatSelf);
        combat.OnAttackerNotification("Target", AcDream.Core.Combat.CombatHitAdjectives.Slash, 10, 0.1);
        log.OnSystemMessage("Additional damage: 5", (uint)RetailLogTextType.CombatSelf);

        ChatEntry[] entries = log.Snapshot();
        Assert.Equal(4, entries.Length);
        Assert.Equal(entries[0].Text, entries[2].Text);
        Assert.Equal(entries[1].Text, entries[3].Text);
        Assert.Equal(new[] { ChatKind.Combat, ChatKind.System, ChatKind.Combat, ChatKind.System },
            entries.Select(e => e.Kind));
    }
}
