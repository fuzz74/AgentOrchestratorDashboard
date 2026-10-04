namespace OrchDash.Core.Model;

public static class ContextLimit                  // never throws
{
    // session.Content.Result?.ContextWindow; else the largest ContextWindow among the snapshot's sessions whose
    // Content.Model is not null and equals this session's (ordinal); else null.
    public static long? For(RunSnapshot snapshot, Session session)
    {
        var own = session.Content.Result?.ContextWindow;
        if (own is not null)
            return own;

        var model = session.Content.Model;
        if (model is null || snapshot.Sessions.IsDefault)
            return null;

        long? largest = null;
        foreach (var other in snapshot.Sessions)
        {
            var window = other.Content.Result?.ContextWindow;
            if (window is not null && string.Equals(other.Content.Model, model, StringComparison.Ordinal) &&
                (largest is null || window > largest))
                largest = window;
        }
        return largest;
    }
}
