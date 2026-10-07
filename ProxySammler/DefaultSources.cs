namespace ProxySammler.App;

internal static class DefaultSources
{
    public static string Text
    {
        get
        {
            using var stream = typeof(DefaultSources).Assembly.GetManifestResourceStream("ProxySammler.DefaultSources.txt")
                ?? throw new InvalidOperationException("Die eingebettete Standarddatei sources.txt fehlt.");
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
    }
}
