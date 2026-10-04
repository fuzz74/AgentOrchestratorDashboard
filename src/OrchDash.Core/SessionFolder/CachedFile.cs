namespace OrchDash.Core.SessionFolder;

// What a file gave when it had this stamp.
internal sealed record CachedFile<T>(FileStamp Stamp, T Value);
