namespace ProxySammler.Core;

public sealed record SourceResult(SourceDefinition Source, int Found, string? Error);
