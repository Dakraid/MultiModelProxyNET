// MultiModelProxy - DatabaseModels.cs
// Created on 2024.11.19
// Last modified at 2024.11.19 13:11

namespace MultiModelProxy.Models;

public class ChainOfThought
{
    public int ChainOfThoughtId { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public required string Content { get; set; }
}

public class Chat
{
    public int ChatId { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public required List<ChatMessage> ChatMessages { get; set; }
}

public class ChatMessage
{
    public int ChatMessageId { get; set; }
    public required string Content { get; set; }
    public required string Role { get; set; }

    public int ChatId { get; set; }
    public Chat Chat { get; set; }
}
