using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Godot;

namespace CncTrainer.Addons;

/// <summary>
/// Shows the /// summary of a C# class and of each of its exported members
/// in the inspector, as a small grey line above the property. Godot takes
/// such descriptions only from GDScript; this reads them from the XML
/// documentation file the C# build writes (GenerateDocumentationFile in
/// Linux.csproj), so a rebuild is enough to see a new description.
/// </summary>
[Tool]
public partial class DescriptionInspectorPlugin : EditorInspectorPlugin
{
    private const string DocFile = "res://.godot/mono/temp/bin/Debug/Linux.xml";

    private readonly Dictionary<string, string> _summaries = new();
    private ulong _loadedStamp;
    private List<string> _classes = new();

    private void Reload()
    {
        string path = ProjectSettings.GlobalizePath(DocFile);
        ulong stamp = FileAccess.FileExists(DocFile) ? FileAccess.GetModifiedTime(DocFile) : 0;
        if (stamp == _loadedStamp)
            return;
        _loadedStamp = stamp;
        _summaries.Clear();
        if (stamp == 0)
            return;
        foreach (var member in XDocument.Load(path).Descendants("member"))
        {
            string id = member.Attribute("name")?.Value ?? "";
            var summary = member.Element("summary");
            if (summary == null || id.Length < 3 || id.Contains('('))
                continue;
            // "F:CncTrainer.Spec.AxisSpec.MinLimit" -> "AxisSpec.MinLimit"; "T:CncTrainer.Spec.AxisSpec" -> "AxisSpec".
            var parts = id[2..].Split('.');
            string key = id[0] == 'T' ? parts[^1] : $"{parts[^2]}.{parts[^1]}";
            string text = Regex.Replace(string.Concat(summary.Nodes().Select(Text)), @"\s+", " ").Trim();
            if (text.Length > 0)
                _summaries[key] = text;
        }
    }

    // Element text, with <see cref="X.Y"/> and <paramref name="p"/> shown as their names.
    private static string Text(XNode node) => node switch
    {
        XText text => text.Value,
        XElement { Name.LocalName: "see" or "seealso" } see => (see.Attribute("cref")?.Value ?? see.Attribute("langword")?.Value ?? "").Split('.')[^1],
        XElement { Name.LocalName: "paramref" or "typeparamref" } p => p.Attribute("name")?.Value ?? "",
        XElement element => string.Concat(element.Nodes().Select(Text)),
        _ => "",
    };

    /// <summary>The C# classes behind an object, most derived first, from its script files.</summary>
    private static List<string> ClassChain(GodotObject obj)
    {
        var chain = new List<string>();
        for (var script = obj?.GetScript().As<Script>(); script != null; script = script.GetBaseScript())
            if (script.ResourcePath.EndsWith(".cs"))
                chain.Add(script.ResourcePath.GetFile().GetBaseName());
        return chain;
    }

    public override bool _CanHandle(GodotObject @object) => ClassChain(@object).Count > 0;

    public override void _ParseBegin(GodotObject @object)
    {
        Reload();
        _classes = ClassChain(@object);
        if (_classes.Select(c => _summaries.GetValueOrDefault(c)).FirstOrDefault(s => s != null) is string about)
            AddCustomControl(Line(about, 0.75f));
    }

    public override bool _ParseProperty(GodotObject @object, Variant.Type type, string name, PropertyHint hintType, string hintString,
        PropertyUsageFlags usageFlags, bool wide)
    {
        foreach (string owner in _classes)
            if (_summaries.TryGetValue($"{owner}.{name}", out string text))
            {
                AddCustomControl(Line(text, 0.6f));
                break;
            }
        return false;
    }

    private static Label Line(string text, float brightness)
    {
        var label = new Label
        {
            Text = text,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            Modulate = new Color(brightness, brightness, brightness * 1.05f),
            TooltipText = text,
            MouseFilter = Control.MouseFilterEnum.Pass,
        };
        label.AddThemeFontSizeOverride("font_size", 11);
        return label;
    }
}
