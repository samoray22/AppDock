namespace AppBox.Core;

public static class InternetShortcut
{
    public static string ReadTarget(string file)
    {
        if (new FileInfo(file).Length > 65536) throw new ArgumentException("This internet shortcut is too large.");
        var inSection = false;
        string? target = null;
        foreach (var raw in File.ReadLines(file))
        {
            var line = raw.Trim();
            if (line.StartsWith('[')) { inSection = line.Equals("[InternetShortcut]", StringComparison.OrdinalIgnoreCase); continue; }
            if (!inSection || !line.StartsWith("URL=", StringComparison.OrdinalIgnoreCase)) continue;
            if (target is not null) throw new ArgumentException("This internet shortcut contains more than one destination.");
            target = line[4..].Trim();
        }
        if (target is null || target.Any(char.IsControl) || !Uri.TryCreate(target, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) || uri.Host.Length == 0 || uri.UserInfo.Length > 0)
            throw new ArgumentException("Only HTTP and HTTPS internet shortcuts are supported.");
        return uri.AbsoluteUri;
    }
}
