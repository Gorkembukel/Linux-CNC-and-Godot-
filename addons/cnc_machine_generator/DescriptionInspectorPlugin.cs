using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Godot;

namespace CncTrainer.Addons;

/// <summary>
/// Gives each exported member of a C# script its /// summary as the
/// inspector tooltip, the way Godot does for documented GDScript; an enum
/// property also lists what each value means. Godot takes such descriptions
/// only from GDScript; this reads them from the XML
/// documentation file the C# build writes (GenerateDocumentationFile in
/// Linux.csproj), so a rebuild is enough to see a new description.
/// </summary>
[Tool]
public partial class DescriptionInspectorPlugin : EditorInspectorPlugin
{
    private const string DocFile = "res://.godot/mono/temp/bin/Debug/Linux.xml";

    private readonly Dictionary<string, string> _summaries = new();
    // Type name -> its documented fields in order: for an enum, its values.
    private readonly Dictionary<string, List<(string Name, string Text)>> _fields = new();
    private ulong _loadedStamp;

    private void Reload()
    {
        string path = ProjectSettings.GlobalizePath(DocFile);
        ulong stamp = FileAccess.FileExists(DocFile) ? FileAccess.GetModifiedTime(DocFile) : 0;
        if (stamp == _loadedStamp)
            return;
        _loadedStamp = stamp;
        _summaries.Clear();
        _fields.Clear();
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
            if (text.Length == 0)
                continue;
            _summaries[key] = text;
            if (id[0] == 'F')
            {
                if (!_fields.TryGetValue(parts[^2], out var list))
                    _fields[parts[^2]] = list = new List<(string, string)>();
                list.Add((parts[^1], text));
            }
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

    private sealed record Tip(string Text, List<(string Name, string Text)> Values);

    // Property name -> its tooltip, per edited object, filled while the inspector parses it.
    private readonly Dictionary<ulong, Dictionary<string, Tip>> _tips = new();

    public override void _ParseBegin(GodotObject @object)
    {
        Reload();
        _tips[@object.GetInstanceId()] = new Dictionary<string, Tip>();
    }

    public override bool _ParseProperty(GodotObject @object, Variant.Type type, string name, PropertyHint hintType, string hintString,
        PropertyUsageFlags usageFlags, bool wide)
    {
        string text = ClassChain(@object).Select(owner => _summaries.GetValueOrDefault($"{owner}.{name}")).FirstOrDefault(s => s != null);
        var values = hintType == PropertyHint.Enum ? EnumValues(hintString) : new List<(string, string)>();
        if ((text != null || values.Count > 0) && _tips.TryGetValue(@object.GetInstanceId(), out var tips))
            tips[name] = new Tip(text, values);
        return false;
    }

    // The property editors exist only once parsing is over: give them their tooltips then.
    public override void _ParseEnd(GodotObject @object)
    {
        ulong id = @object.GetInstanceId();
        if (_tips.Remove(id, out var tips) && tips.Count > 0)
            Callable.From(() => ApplyTooltips(id, tips)).CallDeferred();
    }

    private static void ApplyTooltips(ulong id, Dictionary<string, Tip> tips)
    {
        foreach (var editor in EditorInterface.Singleton.GetBaseControl().FindChildren("*", "EditorProperty", true, false).OfType<EditorProperty>())
        {
            if (editor.GetEditedObject()?.GetInstanceId() != id || !tips.TryGetValue(editor.GetEditedProperty(), out var tip))
                continue;
            if (tip.Text != null)
                editor.TooltipText = DocSymbol(editor.GetEditedProperty(), tip.Text);
            // An enum's values explain themselves in its dropdown, one tooltip per item.
            foreach (var options in editor.FindChildren("*", "OptionButton", true, false).OfType<OptionButton>())
                for (int i = 0; i < options.ItemCount; i++)
                {
                    string shown = options.GetItemText(i).Replace(" ", "");
                    var value = tip.Values.FirstOrDefault(v => string.Equals(v.Name, shown, System.StringComparison.OrdinalIgnoreCase));
                    if (value.Text != null)
                        options.SetItemTooltip(i, value.Text);
                }
        }
    }

    // Godot reads a property editor's tooltip as a doc id, "type|class|name|json";
    // only local symbols take their description from the json, so that is the way in.
    private static string DocSymbol(string name, string text) =>
        $"local_variable||{name}|" + Json.Stringify(new Godot.Collections.Dictionary { ["description"] = text });

    /// <summary>The documented values of the enum an inspector enum hint ("A,B:2,C") lists.</summary>
    private List<(string Name, string Text)> EnumValues(string hintString)
    {
        var names = hintString.Split(',').Select(n => n.Split(':')[0].Trim()).Where(n => n.Length > 0).ToHashSet();
        return _fields.Values
            .Where(list => list.Count > 0 && list.All(v => names.Contains(v.Name)))
            .OrderByDescending(list => list.Count)
            .FirstOrDefault() ?? new List<(string, string)>();
    }
}
