namespace OrchDash.Core.Model;

public static class ContextLimit                  // never throws
{
    // session.Content.Result?.ContextWindow; else the largest ContextWindow among the snapshot's sessions whose
    // Content.Model is not null and equals this session's (ordinal); else null.
    public static long? For(RunSnapshot snapshot, Session session) =>
        session.Content.Result?.ContextWindow ?? ForModel(snapshot, session.Content.Model);

    // The largest Result.ContextWindow among the snapshot's sessions whose Content.Model equals model (ordinal);
    // null for a null model or none found (42.3).
    public static long? ForModel(RunSnapshot snapshot, string? model)
    {
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
