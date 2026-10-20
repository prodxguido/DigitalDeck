namespace DigitalDeck.App;

/// <summary>
/// Icon characters from the Windows system icon font (Segoe Fluent Icons, falling back to
/// Segoe MDL2 Assets).
/// </summary>
/// <remarks>
/// Built from code points rather than written as literals. These live in the Unicode private use
/// area, where the characters carry no meaning outside the icon font and are silently destroyed by
/// any tool along the way that guesses the wrong encoding — a code point survives everything.
/// </remarks>
public static class DeckGlyphs
{
    public static readonly string Folder = FromCodePoint(0xE8B7);
    public static readonly string Page = FromCodePoint(0xE7C3);
    public static readonly string Document = FromCodePoint(0xE8A5);
    public static readonly string Globe = FromCodePoint(0xE774);
    public static readonly string Link = FromCodePoint(0xE71B);
    public static readonly string Play = FromCodePoint(0xE768);
    public static readonly string Stop = FromCodePoint(0xE71A);
    public static readonly string Volume = FromCodePoint(0xE767);
    public static readonly string Video = FromCodePoint(0xE714);
    public static readonly string Camera = FromCodePoint(0xE722);
    public static readonly string Library = FromCodePoint(0xE8F1);
    public static readonly string Search = FromCodePoint(0xE721);
    public static readonly string Settings = FromCodePoint(0xE713);
    public static readonly string Edit = FromCodePoint(0xE70F);
    public static readonly string Add = FromCodePoint(0xE710);
    public static readonly string Star = FromCodePoint(0xE734);
    public static readonly string Sync = FromCodePoint(0xE895);
    public static readonly string Download = FromCodePoint(0xE896);
    public static readonly string Copy = FromCodePoint(0xE8C8);
    public static readonly string Mail = FromCodePoint(0xE715);
    public static readonly string Home = FromCodePoint(0xE80F);
    public static readonly string Info = FromCodePoint(0xE946);

    /// <summary>Default for a tile that has never been given an icon.</summary>
    public static readonly string Button = Page;

    /// <summary>The set offered in the icon picker.</summary>
    public static readonly string[] Palette =
    {
        Folder, Page, Document, Globe, Link, Play, Stop, Volume,
        Video, Camera, Library, Search, Settings, Edit, Add, Star,
        Sync, Download, Copy, Mail, Home, Info
    };

    private static string FromCodePoint(int codePoint) => char.ConvertFromUtf32(codePoint);
}
