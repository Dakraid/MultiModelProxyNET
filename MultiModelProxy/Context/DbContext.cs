﻿// MultiModelProxy - DbContext.cs
// Created on 2024.11.19
// Last modified at 2024.11.19 13:11

namespace MultiModelProxy.Context;

#region Usings
using Microsoft.EntityFrameworkCore;
using Models;
#endregion

public class ChatContext(DbContextOptions<ChatContext> options) : DbContext(options)
{
    public DbSet<ChainOfThought> ChainOfThoughts { get; set; }
    public DbSet<Chat> Chats { get; set; }
    public DbSet<ChatMessage> ChatMessages { get; set; }
}
