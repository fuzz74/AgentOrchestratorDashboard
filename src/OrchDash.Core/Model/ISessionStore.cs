namespace OrchDash.Core.Model;

public interface ISessionStore       // one provider store; called on the store's poll thread; never throws
{
    StoreData? Read(string sessionId, string? workDir);   // null: the store has nothing for this id
}
