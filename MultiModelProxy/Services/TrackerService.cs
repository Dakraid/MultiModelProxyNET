// MultiModelProxy - TrackerService.cs
// Created on 2025.01.13
// Last modified at 2025.01.13 11:01

namespace MultiModelProxy.Services;

public interface ITrackerService
{
    void IncrementCoTRound();
    void IncrementResponseRound();
    void ResetCoTRound();
    void ResetResponseRound();
    int GetCoTRound();
    int GetResponseRound();
    void SetLastCotMessage(string message);
    void SetLastUserMessage(string message);
    string GetLastCotMessage();
    string GetLastUserMessage();
}

public class TrackerService : ITrackerService
{
    private int _coTRound;
    private int _responseRound;
    private string _lastCotMessage = string.Empty;
    private string _lastUserMessage = string.Empty;

    public void IncrementCoTRound() => Interlocked.Increment(ref _coTRound);

    public void IncrementResponseRound() => Interlocked.Increment(ref _responseRound);

    public void ResetCoTRound() => _coTRound = 0;
    
    public void ResetResponseRound() => _responseRound = 0;

    public int GetCoTRound() => _coTRound;

    public int GetResponseRound() => _responseRound;

    public void SetLastCotMessage(string message) => _lastCotMessage = message;

    public void SetLastUserMessage(string message) => _lastUserMessage = message;
    
    public string GetLastCotMessage() => _lastCotMessage;
    
    public string GetLastUserMessage() => _lastUserMessage;
}
