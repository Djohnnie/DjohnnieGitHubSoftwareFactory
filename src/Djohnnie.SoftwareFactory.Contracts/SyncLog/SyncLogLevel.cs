namespace Djohnnie.SoftwareFactory.Contracts.SyncLog;

/// <summary>How serious a sync log event is; stored as its number, so a filter can ask for a level and up.</summary>
public enum SyncLogLevel
{
    Information = 0,
    Warning = 1,
    Error = 2
}
