using System;
using Godot;
using CncTrainer.Save;

namespace CncTrainer.UI;

/// <summary>The save files: load one, delete one, and in the game write a
/// new one or over an old one.</summary>
public partial class SavePanel : VBoxContainer
{
    public event Action Closed;
    /// <summary>Set in the game: writes the workshop to a file under a name.</summary>
    public Func<string, string, bool> Write;

    private VBoxContainer _list;
    private LineEdit _name;

    public override void _Ready()
    {
        MenuUi.Heading(this, Write != null ? "Kaydet / Yükle" : "Kayıt yükle");
        if (Write != null)
        {
            var row = new HBoxContainer();
            AddChild(row);
            _name = new LineEdit { PlaceholderText = "Kayıt adı", CustomMinimumSize = new Vector2(380f, 40f), Text = $"Kayıt {DateTime.Now:dd.MM HH:mm}" };
            row.AddChild(_name);
            MenuUi.Button(row, "Yeni kayıt", () => WriteTo(SaveGame.NewPath(), _name.Text));
        }
        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(640f, 360f) };
        AddChild(scroll);
        _list = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        scroll.AddChild(_list);
        MenuUi.Button(this, "Geri", () => Closed?.Invoke());
        VisibilityChanged += () =>
        {
            if (Visible)
                Fill();
        };
        Fill();
    }

    private void WriteTo(string path, string name)
    {
        if (Write(path, string.IsNullOrWhiteSpace(name) ? "Kayıt" : name.Trim()))
            Fill();
    }

    private void Fill()
    {
        foreach (Node child in _list.GetChildren())
            child.QueueFree();
        var saves = SaveGame.List();
        if (saves.Count == 0)
            _list.AddChild(new Label { Text = "Henüz kayıt yok." });
        foreach (var save in saves)
        {
            var row = new HBoxContainer();
            _list.AddChild(row);
            row.AddChild(new Label { Text = $"{save.Name}\n{save.Time:dd.MM.yyyy HH:mm}  ·  {save.Scenario}", CustomMinimumSize = new Vector2(330f, 0f) });
            MenuUi.Button(row, "Yükle", () => Session.Start(GetTree(), new StartRequest(SaveFile: save.Path))).CustomMinimumSize = new Vector2(90f, 40f);
            if (Write != null)
                MenuUi.Button(row, "Üzerine yaz", () => WriteTo(save.Path, save.Name)).CustomMinimumSize = new Vector2(120f, 40f);
            MenuUi.Button(row, "Sil", () =>
            {
                SaveGame.Delete(save.Path);
                Fill();
            }).CustomMinimumSize = new Vector2(60f, 40f);
        }
    }
}
