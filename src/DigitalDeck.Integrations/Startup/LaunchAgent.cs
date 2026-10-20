using System.Xml.Linq;

namespace DigitalDeck.Integrations.Startup;

/// <summary>
/// The macOS way of asking to be opened at login: a small XML file in <c>~/Library/LaunchAgents</c>.
/// </summary>
/// <remarks>
/// The equivalent of the <c>Run</c> key on Windows, and about as involved. What is here is only the
/// file's contents and how to read them back — writing it is <see cref="MacStartupRegistration"/> —
/// because the shape of the plist is the part worth pinning: a malformed one is not rejected with
/// an error, it is ignored, and the app simply never opens at login with nothing anywhere to say
/// why.
/// <para>
/// <c>RunAtLoad</c> and nothing else on purpose. <c>KeepAlive</c> would have launchd restart the
/// deck every time somebody closes it, which is not what "open at login" means to a person.
/// </para>
/// </remarks>
public static class LaunchAgent
{
    /// <summary>Reverse-DNS label launchd knows the job by, and the file's name.</summary>
    public const string Label = "com.digitaldeck.deck";

    /// <summary>The plist that asks launchd to open <paramref name="executable"/> at login.</summary>
    public static string Plist(string executable)
    {
        var document = new XDocument(
            new XDocumentType("plist", "-//Apple//DTD PLIST 1.0//EN",
                "http://www.apple.com/DTDs/PropertyList-1.0.dtd", null),
            new XElement("plist",
                new XAttribute("version", "1.0"),
                new XElement("dict",
                    new XElement("key", "Label"),
                    new XElement("string", Label),
                    new XElement("key", "ProgramArguments"),
                    new XElement("array", new XElement("string", executable)),
                    new XElement("key", "RunAtLoad"),
                    new XElement("true"))));

        return document.ToString();
    }

    /// <summary>
    /// The program an existing plist opens, or null when the file is not one of ours to read.
    /// </summary>
    /// <remarks>
    /// Answering null for anything unexpected rather than throwing: a plist somebody edited by hand
    /// is their business, and the honest report is "I cannot tell you what this points at", which
    /// is what the settings screen shows.
    /// </remarks>
    public static string? ProgramIn(string plist)
    {
        try
        {
            var arguments = XDocument.Parse(plist)
                .Descendants("key")
                .FirstOrDefault(key => key.Value == "ProgramArguments")?
                .ElementsAfterSelf()
                .FirstOrDefault();

            var program = arguments?.Name == "array"
                ? arguments.Elements("string").FirstOrDefault()?.Value
                : null;

            return string.IsNullOrWhiteSpace(program) ? null : program;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
