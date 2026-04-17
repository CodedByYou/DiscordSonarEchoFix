using DiscordEchoFix.Audio;

namespace DiscordEchoFix.App;

internal sealed class SettingsForm : Form
{
    private readonly Settings _settings;
    private readonly DiscordSessionController _controller;
    private readonly CheckedListBox _list = new()
    {
        Dock = DockStyle.Fill,
        CheckOnClick = true,
        IntegralHeight = false,
        Font = new System.Drawing.Font("Segoe UI", 9f)
    };
    private readonly List<string> _endpoints = new();
    private readonly CheckBox _autoMuteToggle = new()
    {
        Text = "Auto-mute Discord on selected devices (poll every 2s)",
        Dock = DockStyle.Top,
        AutoSize = true,
        Padding = new Padding(8, 8, 8, 4)
    };

    public SettingsForm(Settings settings, DiscordSessionController controller)
    {
        _settings = settings;
        _controller = controller;

        Text = "Discord Sonar Echo Fix - Devices";
        Width = 720;
        Height = 480;
        StartPosition = FormStartPosition.CenterScreen;
        MinimizeBox = false;
        MaximizeBox = false;
        FormBorderStyle = FormBorderStyle.Sizable;
        ShowInTaskbar = false;

        var help = new Label
        {
            Dock = DockStyle.Top,
            AutoSize = false,
            Height = 60,
            Padding = new Padding(8),
            Text = "CHECKED = Discord will be muted on this output device (good for screenshare-loopback paths).\r\n" +
                   "UNCHECKED = Discord left alone here (this is how you HEAR friends — Sonar Chat etc.).\r\n" +
                   "Default rule: skip any device whose name contains 'Sonar' but not 'Microphone'."
        };

        _autoMuteToggle.Checked = _settings.AutoMuteEnabled;
        _autoMuteToggle.CheckedChanged += (_, _) => _settings.AutoMuteEnabled = _autoMuteToggle.Checked;

        var btnRefresh = new Button { Text = "Refresh device list", AutoSize = true };
        btnRefresh.Click += (_, _) => Populate();

        var btnResetDefaults = new Button { Text = "Reset to defaults", AutoSize = true };
        btnResetDefaults.Click += (_, _) =>
        {
            _settings.EndpointOverrides.Clear();
            Populate();
        };

        var btnSave = new Button { Text = "Save", AutoSize = true, DialogResult = DialogResult.OK };
        btnSave.Click += (_, _) =>
        {
            CommitFromUi();
            _settings.Save();
            DialogResult = DialogResult.OK;
            Close();
        };

        var btnCancel = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };

        var buttonRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            Height = 44,
            Padding = new Padding(8)
        };
        buttonRow.Controls.AddRange(new Control[] { btnSave, btnCancel, btnResetDefaults, btnRefresh });

        Controls.Add(_list);
        Controls.Add(buttonRow);
        Controls.Add(_autoMuteToggle);
        Controls.Add(help);

        AcceptButton = btnSave;
        CancelButton = btnCancel;

        Populate();
    }

    private void Populate()
    {
        _list.Items.Clear();
        _endpoints.Clear();
        var overrides = _settings.EndpointOverrides;

        foreach (var name in _controller.ListEndpoints().OrderBy(n => n))
        {
            _endpoints.Add(name);
            var shouldMute = DiscordSessionController.ShouldMuteOn(name, overrides);
            var label = name + (overrides.ContainsKey(name) ? "   (overridden)" : "");
            _list.Items.Add(label, shouldMute);
        }
    }

    private void CommitFromUi()
    {
        _settings.EndpointOverrides.Clear();
        for (var i = 0; i < _endpoints.Count; i++)
        {
            var name = _endpoints[i];
            var checkedNow = _list.GetItemChecked(i);
            var defaultDecision = !DiscordSessionController.IsHearingPathEndpoint(name);
            if (checkedNow != defaultDecision)
            {
                _settings.EndpointOverrides[name] = checkedNow;
            }
        }
    }
}
