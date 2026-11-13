using System;
using System.IO;
using System.Drawing;
using System.Diagnostics;
using System.Windows.Forms;

namespace Poe2ItemCompanion
{
    public class LogViewerForm : Form
    {
        private TextBox _logTextBox;
        private Button _refreshBtn;
        private Button _copyBtn;
        private Button _clearBtn;
        private Button _openFolderBtn;
        private Button _openExternalBtn;
        private Label _pathLabel;

        public LogViewerForm()
        {
            InitializeComponent();
            LoadLogContent();
        }

        private void InitializeComponent()
        {
            Text = "Path of Exile Companion - Diagnostic Log Viewer";
            Size = new Size(800, 560);
            MinimumSize = new Size(600, 400);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(24, 24, 27);
            ForeColor = Color.White;
            Font = new Font("Segoe UI", 9f);
            Icon = SystemIcons.Shield;

            // Top Header Bar
            var topPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 44,
                BackColor = Color.FromArgb(32, 32, 36),
                Padding = new Padding(10, 8, 10, 8)
            };

            _refreshBtn = new Button
            {
                Text = "Refresh",
                Location = new Point(10, 8),
                Width = 85,
                Height = 28,
                BackColor = Color.FromArgb(45, 50, 60),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            _refreshBtn.Click += (s, e) => LoadLogContent();

            _copyBtn = new Button
            {
                Text = "Copy all",
                Location = new Point(102, 8),
                Width = 85,
                Height = 28,
                BackColor = Color.FromArgb(45, 50, 60),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            _copyBtn.Click += (s, e) =>
            {
                if (!string.IsNullOrEmpty(_logTextBox.Text))
                {
                    Clipboard.SetText(_logTextBox.Text);
                    MessageBox.Show("Log contents copied to clipboard.", "Copied", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            };

            _clearBtn = new Button
            {
                Text = "Clear log",
                Location = new Point(194, 8),
                Width = 90,
                Height = 28,
                BackColor = Color.FromArgb(60, 40, 40),
                ForeColor = Color.FromArgb(255, 180, 180),
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            _clearBtn.Click += (s, e) =>
            {
                if (MessageBox.Show("Are you sure you want to clear the log file?", "Confirm Clear", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                {
                    try
                    {
                        string path = Logger.LogFilePath;
                        if (File.Exists(path))
                        {
                            File.WriteAllText(path, string.Empty);
                        }
                        LoadLogContent();
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show("Failed to clear log: " + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            };

            _openFolderBtn = new Button
            {
                Text = "Open logs folder",
                Location = new Point(291, 8),
                Width = 135,
                Height = 28,
                BackColor = Color.FromArgb(45, 50, 60),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            _openFolderBtn.Click += (s, e) =>
            {
                try
                {
                    string dir = Path.GetDirectoryName(Logger.LogFilePath);
                    if (Directory.Exists(dir))
                    {
                        Process.Start("explorer.exe", dir);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Failed to open folder: " + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            };

            _openExternalBtn = new Button
            {
                Text = "Classic Notepad",
                Location = new Point(433, 8),
                Width = 115,
                Height = 28,
                BackColor = Color.FromArgb(45, 50, 60),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            _openExternalBtn.Click += (s, e) =>
            {
                try
                {
                    string path = Logger.LogFilePath;
                    string classicNotepad = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "notepad.exe");
                    if (File.Exists(classicNotepad))
                    {
                        Process.Start(classicNotepad, "\"" + path + "\"");
                    }
                    else
                    {
                        Process.Start("notepad.exe", "\"" + path + "\"");
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Could not launch external notepad: " + ex.Message, "Notice", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            };

            topPanel.Controls.Add(_refreshBtn);
            topPanel.Controls.Add(_copyBtn);
            topPanel.Controls.Add(_clearBtn);
            topPanel.Controls.Add(_openFolderBtn);
            topPanel.Controls.Add(_openExternalBtn);

            // Bottom Status / Path Bar
            var bottomPanel = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 28,
                BackColor = Color.FromArgb(20, 20, 22),
                Padding = new Padding(8, 4, 8, 4)
            };

            _pathLabel = new Label
            {
                Text = "Log File: " + Logger.LogFilePath,
                Dock = DockStyle.Fill,
                AutoEllipsis = true,
                ForeColor = Color.FromArgb(140, 140, 150),
                Font = new Font("Segoe UI", 8.5f)
            };
            bottomPanel.Controls.Add(_pathLabel);

            // Center Text Box
            _logTextBox = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Both,
                BackColor = Color.FromArgb(16, 16, 18),
                ForeColor = Color.FromArgb(220, 220, 230),
                Font = new Font("Consolas", 9.5f),
                WordWrap = false
            };

            Controls.Add(_logTextBox);
            Controls.Add(topPanel);
            Controls.Add(bottomPanel);
            UiTheme.Apply(this);
            _clearBtn.ForeColor = Color.FromArgb(255, 180, 180);
            _clearBtn.FlatAppearance.BorderColor = Color.FromArgb(172, 92, 92);
        }

        public void LoadLogContent()
        {
            try
            {
                string path = Logger.LogFilePath;
                if (File.Exists(path))
                {
                    string text = File.ReadAllText(path);
                    _logTextBox.Text = text;
                    _logTextBox.SelectionStart = _logTextBox.Text.Length;
                    _logTextBox.ScrollToCaret();
                }
                else
                {
                    _logTextBox.Text = "(Log file is empty)";
                }
            }
            catch (Exception ex)
            {
                _logTextBox.Text = "Error reading log file: " + ex.Message;
            }
        }
    }
}
