namespace OrchDash.Core.Model;

public interface IProcessLister      // never throws; TaskId, Role and SessionId are null on what it returns
{
    ProcessInfo List(DateTimeOffset now);
}
