using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;

namespace WinCleaner
{
    public class CircularButton : Button
    {
        private bool _isHovered = false;
        private bool _isPressed = false;

        public CircularButton()
        {
            this.FlatStyle = FlatStyle.Flat;
            this.FlatAppearance.BorderSize = 0;
            this.BackColor = Color.FromArgb(40, 167, 69); // Vibrant emerald green
            this.ForeColor = Color.White;
            this.Cursor = Cursors.Hand;
            this.Font = new Font("Segoe UI", 14F, FontStyle.Bold);
            this.SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            _isHovered = true;
            this.Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _isHovered = false;
            _isPressed = false;
            this.Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs mevent)
        {
            base.OnMouseDown(mevent);
            if (mevent.Button == MouseButtons.Left)
            {
                _isPressed = true;
                this.Invalidate();
            }
        }

        protected override void OnMouseUp(MouseEventArgs mevent)
        {
            base.OnMouseUp(mevent);
            _isPressed = false;
            this.Invalidate();
        }

        protected override void OnPaint(PaintEventArgs pevent)
        {
            Graphics g = pevent.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            int diameter = Math.Min(this.Width, this.Height) - 6;
            int x = (this.Width - diameter) / 2;
            int y = (this.Height - diameter) / 2;

            using (GraphicsPath path = new GraphicsPath())
            {
                path.AddEllipse(x, y, diameter, diameter);
                this.Region = new Region(path);

                Color fillColor = Color.FromArgb(40, 167, 69);
                if (_isPressed)
                    fillColor = Color.FromArgb(28, 116, 48);
                else if (_isHovered)
                    fillColor = Color.FromArgb(48, 185, 78);

                using (SolidBrush brush = new SolidBrush(fillColor))
                {
                    g.FillEllipse(brush, x, y, diameter, diameter);
                }

                // Border ring
                Color borderColor = _isHovered ? Color.FromArgb(72, 210, 105) : Color.FromArgb(30, 126, 52);
                using (Pen pen = new Pen(borderColor, 3.5f))
                {
                    g.DrawEllipse(pen, x + 1, y + 1, diameter - 2, diameter - 2);
                }

                // Inner soft glow ring
                using (Pen innerPen = new Pen(Color.FromArgb(70, 255, 255, 255), 1.5f))
                {
                    g.DrawEllipse(innerPen, x + 4, y + 4, diameter - 8, diameter - 8);
                }

                // Draw Text
                using (StringFormat sf = new StringFormat
                {
                    Alignment = StringAlignment.Center,
                    LineAlignment = StringAlignment.Center
                })
                {
                    using (SolidBrush textBrush = new SolidBrush(this.ForeColor))
                    {
                        RectangleF textRect = new RectangleF(x, y, diameter, diameter);
                        g.DrawString(this.Text, this.Font, textBrush, textRect, sf);
                    }
                }
            }
        }
    }

    public class SandboxFolderForm : Form
    {
        private TextBox _txtPath;
        private Button _btnBrowse;
        private Button _btnDesktopPreset;
        private Button _btnDefaultPreset;
        private Label _lblValidation;
        private Button _btnSave;
        private Button _btnCancel;

        public string SelectedPath { get; private set; }

        public SandboxFolderForm()
        {
            InitializeComponent();
            _txtPath.Text = CleanerEngine.GetSandboxDirectory();
            _txtPath.SelectAll();
            ValidateCurrentPath();
        }

        private void InitializeComponent()
        {
            this.Text = "Select Sandbox Folder - WinCleaner";
            this.Size = new Size(580, 270);
            this.MinimumSize = new Size(520, 270);
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.ShowInTaskbar = false;
            this.Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
            this.BackColor = Color.FromArgb(248, 249, 250);

            Panel container = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(18, 14, 18, 14)
            };

            Label lblPrompt = new Label
            {
                Text = "Choose or paste any folder path on your computer to use as the Sandbox:",
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
                ForeColor = Color.FromArgb(33, 37, 41),
                Location = new Point(18, 14),
                AutoSize = true
            };

            Label lblSubtitle = new Label
            {
                Text = "Items in this folder can be cleaned or replenished with disposable mock data.",
                Font = new Font("Segoe UI", 8.5F),
                ForeColor = Color.FromArgb(108, 117, 125),
                Location = new Point(18, 36),
                AutoSize = true
            };

            _txtPath = new TextBox
            {
                Location = new Point(18, 62),
                Size = new Size(420, 26),
                Font = new Font("Segoe UI", 9.5F)
            };
            _txtPath.TextChanged += (s, e) => ValidateCurrentPath();

            _btnBrowse = new Button
            {
                Text = "📁 Browse...",
                Location = new Point(446, 60),
                Size = new Size(100, 28),
                FlatStyle = FlatStyle.System,
                Cursor = Cursors.Hand
            };
            _btnBrowse.Click += BtnBrowse_Click;

            FlowLayoutPanel presetsPanel = new FlowLayoutPanel
            {
                Location = new Point(18, 98),
                Size = new Size(528, 30),
                FlowDirection = FlowDirection.LeftToRight,
                Margin = new Padding(0)
            };

            Label lblQuick = new Label
            {
                Text = "Quick Set:",
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                ForeColor = Color.FromArgb(70, 70, 70),
                AutoSize = true,
                Padding = new Padding(0, 4, 6, 0)
            };

            _btnDesktopPreset = new Button
            {
                Text = "🖥️ Desktop\\Sandbox",
                Size = new Size(150, 25),
                FlatStyle = FlatStyle.System,
                Cursor = Cursors.Hand
            };
            _btnDesktopPreset.Click += (s, e) =>
            {
                string dt = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                _txtPath.Text = Path.Combine(dt, "WinCleaner_Sandbox");
            };

            _btnDefaultPreset = new Button
            {
                Text = "🔄 Reset to Default",
                Size = new Size(130, 25),
                FlatStyle = FlatStyle.System,
                Cursor = Cursors.Hand
            };
            _btnDefaultPreset.Click += (s, e) =>
            {
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                _txtPath.Text = Path.Combine(appData, "WinCleaner", "Sandbox");
            };

            presetsPanel.Controls.Add(lblQuick);
            presetsPanel.Controls.Add(_btnDesktopPreset);
            presetsPanel.Controls.Add(_btnDefaultPreset);

            _lblValidation = new Label
            {
                Location = new Point(18, 134),
                Size = new Size(528, 36),
                Font = new Font("Segoe UI", 8.5F),
                ForeColor = Color.FromArgb(40, 167, 69)
            };

            Panel buttonBar = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 36
            };

            _btnSave = new Button
            {
                Text = "✓ Save & Use Folder",
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                Size = new Size(150, 32),
                BackColor = Color.FromArgb(40, 167, 69),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand,
                Dock = DockStyle.Right
            };
            _btnSave.FlatAppearance.BorderColor = Color.FromArgb(30, 126, 52);
            _btnSave.Click += BtnSave_Click;

            _btnCancel = new Button
            {
                Text = "Cancel",
                Size = new Size(85, 32),
                FlatStyle = FlatStyle.System,
                Cursor = Cursors.Hand,
                Dock = DockStyle.Right,
                Margin = new Padding(0, 0, 10, 0)
            };
            _btnCancel.Click += (s, e) => { this.DialogResult = DialogResult.Cancel; this.Close(); };

            Panel space = new Panel { Dock = DockStyle.Right, Width = 10 };
            buttonBar.Controls.Add(_btnCancel);
            buttonBar.Controls.Add(space);
            buttonBar.Controls.Add(_btnSave);

            container.Controls.Add(lblPrompt);
            container.Controls.Add(lblSubtitle);
            container.Controls.Add(_txtPath);
            container.Controls.Add(_btnBrowse);
            container.Controls.Add(presetsPanel);
            container.Controls.Add(_lblValidation);
            container.Controls.Add(buttonBar);

            this.Controls.Add(container);
            this.AcceptButton = _btnSave;
            this.CancelButton = _btnCancel;
        }

        private bool ValidateCurrentPath()
        {
            string path = _txtPath.Text.Trim();
            if (string.IsNullOrEmpty(path))
            {
                _lblValidation.ForeColor = Color.FromArgb(200, 35, 51);
                _lblValidation.Text = "⚠️ Please enter or browse to a valid folder path.";
                _btnSave.Enabled = false;
                return false;
            }

            string reason;
            if (CleanerEngine.IsProtectedPath(path, out reason))
            {
                _lblValidation.ForeColor = Color.FromArgb(200, 35, 51);
                _lblValidation.Text = "⚠️ " + reason;
                _btnSave.Enabled = false;
                return false;
            }

            if (Directory.Exists(path))
            {
                _lblValidation.ForeColor = Color.FromArgb(40, 167, 69);
                _lblValidation.Text = "✓ Directory exists and is ready for use.";
            }
            else
            {
                _lblValidation.ForeColor = Color.FromArgb(0, 123, 255);
                _lblValidation.Text = "ℹ️ Directory does not exist yet. It will be created automatically upon saving.";
            }

            _btnSave.Enabled = true;
            return true;
        }

        private void BtnBrowse_Click(object sender, EventArgs e)
        {
            using (FolderBrowserDialog fbd = new FolderBrowserDialog())
            {
                fbd.Description = "Select a safe sandbox folder:";
                string current = _txtPath.Text.Trim();
                if (!string.IsNullOrEmpty(current) && Directory.Exists(current))
                {
                    fbd.SelectedPath = current;
                }
                else
                {
                    fbd.SelectedPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                }
                fbd.ShowNewFolderButton = true;

                if (fbd.ShowDialog(this) == DialogResult.OK)
                {
                    _txtPath.Text = fbd.SelectedPath;
                }
            }
        }

        private void BtnSave_Click(object sender, EventArgs e)
        {
            if (!ValidateCurrentPath()) return;

            string path = _txtPath.Text.Trim();
            try
            {
                if (!Directory.Exists(path))
                {
                    Directory.CreateDirectory(path);
                }
                SelectedPath = path;
                CleanerEngine.SetSandboxDirectory(path);
                this.DialogResult = DialogResult.OK;
                this.Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not access or create directory:\n\n" + ex.Message,
                    "Directory Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }

    public class MainForm : Form
    {
        private readonly CleanerEngine _engine;

        // Top Toolbar
        private Panel _topBar;
        private Label _lblAppTitle;
        private Label _lblActiveFolderBadge;
        private ToolTip _toolTip;
        private Button _btnSelectFolder;
        private Button _btnPopulate;
        private Button _btnOpenFolder;

        // Center: Circular Clean Button Area
        private Panel _centerPanel;
        private CircularButton _btnClean;
        private System.Windows.Forms.Timer _animTimer;
        private int _animFrame = 0;

        // Results Card
        private Panel _resultCard;
        private Label _lblLastDeleted;
        private Label _lblSessionStats;

        // History View
        private Panel _historyContainer;
        private ListView _lvDeletedHistory;
        private Button _btnClearHistory;

        // Session Tracking
        private long _totalBytesCleaned = 0;
        private int _totalItemsCleaned = 0;
        private bool _isCleaning = false;

        public MainForm()
        {
            _engine = new CleanerEngine();
            InitializeComponent();
            UpdateStats();
        }

        private static void EnableDoubleBuffer(Control control)
        {
            try
            {
                var prop = typeof(Control).GetProperty("DoubleBuffered",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (prop != null)
                {
                    prop.SetValue(control, true, null);
                }
            }
            catch { }
        }

        private void InitializeComponent()
        {
            this.Text = "WinCleaner";
            this.Size = new Size(660, 580);
            this.MinimumSize = new Size(560, 500);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
            this.BackColor = Color.FromArgb(246, 248, 250);

            _toolTip = new ToolTip();

            TableLayoutPanel mainLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 4,
                Padding = new Padding(16, 10, 16, 12)
            };
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));  // Top bar
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 185)); // Big Circular Green Button
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));  // Result stats card
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));  // History list

            // ================= 1. TOP BAR =================
            _topBar = new Panel
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(0)
            };

            _lblAppTitle = new Label
            {
                Text = "🧼 WinCleaner",
                Font = new Font("Segoe UI", 11F, FontStyle.Bold),
                ForeColor = Color.FromArgb(33, 37, 41),
                Location = new Point(0, 6),
                AutoSize = true
            };

            _lblActiveFolderBadge = new Label
            {
                Text = "📁 Sandbox: " + Path.GetFileName(CleanerEngine.GetSandboxDirectory()),
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                ForeColor = Color.FromArgb(40, 110, 55),
                BackColor = Color.FromArgb(235, 247, 238),
                BorderStyle = BorderStyle.FixedSingle,
                Padding = new Padding(6, 3, 6, 3),
                Location = new Point(130, 5),
                AutoSize = true,
                Cursor = Cursors.Hand
            };
            _lblActiveFolderBadge.Click += (s, e) => OpenSandboxFolderDialog();
            _toolTip.SetToolTip(_lblActiveFolderBadge, "Active Sandbox Folder:\n" + CleanerEngine.GetSandboxDirectory() + "\n\nClick to change folder.");

            FlowLayoutPanel topToolbar = new FlowLayoutPanel
            {
                Dock = DockStyle.Right,
                FlowDirection = FlowDirection.RightToLeft,
                AutoSize = true,
                Margin = new Padding(0)
            };

            _btnOpenFolder = new Button
            {
                Text = "📂 Open",
                Size = new Size(72, 26),
                FlatStyle = FlatStyle.System,
                Cursor = Cursors.Hand
            };
            _btnOpenFolder.Click += BtnOpenFolder_Click;
            _toolTip.SetToolTip(_btnOpenFolder, "Open active sandbox folder in Windows Explorer");

            _btnPopulate = new Button
            {
                Text = "➕ Mock Data",
                Size = new Size(100, 26),
                FlatStyle = FlatStyle.System,
                Cursor = Cursors.Hand
            };
            _btnPopulate.Click += BtnPopulate_Click;
            _toolTip.SetToolTip(_btnPopulate, "Generate 6 dummy files and 4 dummy folders in the sandbox");

            _btnSelectFolder = new Button
            {
                Text = "📁 Choose Folder...",
                Size = new Size(130, 26),
                FlatStyle = FlatStyle.System,
                Cursor = Cursors.Hand
            };
            _btnSelectFolder.Click += (s, e) => OpenSandboxFolderDialog();
            _toolTip.SetToolTip(_btnSelectFolder, "Open the folder selection window to pick or paste any sandbox directory");

            topToolbar.Controls.Add(_btnOpenFolder);
            topToolbar.Controls.Add(_btnPopulate);
            topToolbar.Controls.Add(_btnSelectFolder);

            _topBar.Controls.Add(_lblAppTitle);
            _topBar.Controls.Add(_lblActiveFolderBadge);
            _topBar.Controls.Add(topToolbar);

            // ================= 2. CIRCULAR GREEN CLEAN BUTTON =================
            _centerPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                Margin = new Padding(0, 6, 0, 0)
            };

            _btnClean = new CircularButton
            {
                Size = new Size(150, 150),
                Text = "🧼\nCLEAN"
            };
            _btnClean.Click += BtnClean_Click;
            _toolTip.SetToolTip(_btnClean, "Click to clean an item from the sandbox folder");

            _centerPanel.Resize += (s, e) =>
            {
                _btnClean.Location = new Point(
                    (_centerPanel.Width - _btnClean.Width) / 2,
                    (_centerPanel.Height - _btnClean.Height) / 2
                );
            };
            _centerPanel.Controls.Add(_btnClean);

            // Cleaning Animation Timer
            _animTimer = new System.Windows.Forms.Timer();
            _animTimer.Interval = 90;
            _animTimer.Tick += AnimTimer_Tick;

            // ================= 3. RESULT STATS CARD =================
            _resultCard = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(235, 247, 238),
                BorderStyle = BorderStyle.FixedSingle,
                Margin = new Padding(0, 6, 0, 0),
                Padding = new Padding(12, 5, 12, 5)
            };

            _lblLastDeleted = new Label
            {
                Text = "Ready to clean. Click the green button to start.",
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
                ForeColor = Color.FromArgb(21, 87, 36),
                Dock = DockStyle.Top,
                AutoSize = true
            };

            _lblSessionStats = new Label
            {
                Text = "Sandbox items: 0 | Total cleaned: 0 B",
                Font = new Font("Segoe UI", 8.5F),
                ForeColor = Color.FromArgb(40, 110, 55),
                Dock = DockStyle.Bottom,
                AutoSize = true
            };

            _resultCard.Controls.Add(_lblLastDeleted);
            _resultCard.Controls.Add(_lblSessionStats);

            // ================= 4. DELETED ITEMS VIEWER / HISTORY =================
            _historyContainer = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                Margin = new Padding(0, 6, 0, 0),
                Padding = new Padding(6)
            };

            Panel historyHeader = new Panel
            {
                Dock = DockStyle.Top,
                Height = 26
            };

            Label lblHistoryTitle = new Label
            {
                Text = "📋 Deleted Items & Sizes:",
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                ForeColor = Color.FromArgb(60, 60, 60),
                Dock = DockStyle.Left,
                AutoSize = true,
                Padding = new Padding(0, 4, 0, 0)
            };

            _btnClearHistory = new Button
            {
                Text = "Clear List",
                Size = new Size(75, 22),
                Dock = DockStyle.Right,
                FlatStyle = FlatStyle.System,
                Cursor = Cursors.Hand
            };
            _btnClearHistory.Click += (s, e) => _lvDeletedHistory.Items.Clear();

            historyHeader.Controls.Add(lblHistoryTitle);
            historyHeader.Controls.Add(_btnClearHistory);

            _lvDeletedHistory = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                HeaderStyle = ColumnHeaderStyle.Nonclickable,
                Font = new Font("Segoe UI", 9F)
            };
            _lvDeletedHistory.Columns.Add("Deleted Item", 220);
            _lvDeletedHistory.Columns.Add("Type", 75);
            _lvDeletedHistory.Columns.Add("Size Freed", 95);
            _lvDeletedHistory.Columns.Add("Time", 75);
            _lvDeletedHistory.Columns.Add("Original Path", 150);
            EnableDoubleBuffer(_lvDeletedHistory);

            _lvDeletedHistory.Resize += (s, e) =>
            {
                int fixedWidths = 220 + 75 + 95 + 75; // 465
                int remaining = _lvDeletedHistory.ClientSize.Width - fixedWidths;
                if (remaining > 80)
                {
                    _lvDeletedHistory.Columns[4].Width = remaining - 4;
                }
            };

            _historyContainer.Controls.Add(_lvDeletedHistory);
            _historyContainer.Controls.Add(historyHeader);

            // Assemble main layout
            mainLayout.Controls.Add(_topBar, 0, 0);
            mainLayout.Controls.Add(_centerPanel, 0, 1);
            mainLayout.Controls.Add(_resultCard, 0, 2);
            mainLayout.Controls.Add(_historyContainer, 0, 3);

            this.Controls.Add(mainLayout);
        }

        private void OpenSandboxFolderDialog()
        {
            using (SandboxFolderForm dlg = new SandboxFolderForm())
            {
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    UpdateStats();
                    _lblLastDeleted.Text = "📁 Sandbox folder set to: " + Path.GetFileName(dlg.SelectedPath);
                    MessageBox.Show(this, "Sandbox folder successfully changed to:\n\n" + dlg.SelectedPath,
                        "Sandbox Folder Changed", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
        }

        private void BtnClean_Click(object sender, EventArgs e)
        {
            if (_isCleaning) return;
            _isCleaning = true;
            _animFrame = 0;
            _animTimer.Start();
        }

        private void AnimTimer_Tick(object sender, EventArgs e)
        {
            _animFrame++;
            switch (_animFrame)
            {
                case 1:
                    _btnClean.Text = "🧹\nSWEEP";
                    _btnClean.BackColor = Color.FromArgb(48, 185, 78);
                    break;
                case 2:
                    _btnClean.Text = "✨\nPOLISH";
                    _btnClean.BackColor = Color.FromArgb(56, 198, 88);
                    break;
                case 3:
                    _btnClean.Text = "🧼\nSCRUB";
                    _btnClean.BackColor = Color.FromArgb(40, 167, 69);
                    break;
                case 4:
                    _btnClean.Text = "💫\nSHINE";
                    _btnClean.BackColor = Color.FromArgb(33, 136, 56);
                    break;
                case 5:
                    _btnClean.Text = "✨\nDONE";
                    break;
                default:
                    _animTimer.Stop();
                    ExecuteCleaning();
                    _btnClean.Text = "🧼\nCLEAN";
                    _btnClean.BackColor = Color.FromArgb(40, 167, 69);
                    _isCleaning = false;
                    break;
            }
        }

        private void ExecuteCleaning()
        {
            bool isDir;
            string deletedName;
            long deletedBytes;
            int remaining = 0;
            // Always target both files and folders randomly by default
            string victimPath = _engine.DeleteRandomSandboxItem(RouletteTargetMode.All, out isDir, out deletedName, out deletedBytes, out remaining);

            if (!string.IsNullOrEmpty(victimPath))
            {
                _totalBytesCleaned += deletedBytes;
                _totalItemsCleaned++;

                // Add to history ListView
                ListViewItem lvi = new ListViewItem(deletedName);
                lvi.SubItems.Add(isDir ? "📁 Folder" : "📄 File");
                lvi.SubItems.Add(CleanerEngine.FormatBytes(deletedBytes));
                lvi.SubItems.Add(DateTime.Now.ToString("HH:mm:ss"));
                lvi.SubItems.Add(victimPath);
                if (isDir)
                {
                    lvi.ForeColor = Color.FromArgb(160, 60, 0);
                }
                _lvDeletedHistory.Items.Insert(0, lvi);

                _lblLastDeleted.Text = string.Format("✅ Deleted: {0} ({1})",
                    deletedName, CleanerEngine.FormatBytes(deletedBytes));
                _lblSessionStats.Text = string.Format("Remaining items: {0}  |  Session freed: {1} ({2} items cleaned)",
                    remaining, CleanerEngine.FormatBytes(_totalBytesCleaned), _totalItemsCleaned);

                System.Media.SystemSounds.Asterisk.Play();
            }
            else
            {
                List<SandboxEntry> current = _engine.GetSandboxEntries();
                if (current.Count == 0)
                {
                    DialogResult gen = MessageBox.Show(this,
                        "The sandbox folder is completely empty!\n\nWould you like to generate fresh mock files and folders now?",
                        "Sandbox Empty",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Question);

                    if (gen == DialogResult.Yes)
                    {
                        _engine.GenerateSandboxDummyData(6, 4);
                        UpdateStats();
                        _lblLastDeleted.Text = "✨ Generated 6 mock files and 4 mock folders in sandbox.";
                    }
                }
            }
        }

        private void BtnPopulate_Click(object sender, EventArgs e)
        {
            _engine.GenerateSandboxDummyData(6, 4);
            UpdateStats();
            _lblLastDeleted.Text = "✨ Generated 6 mock files and 4 mock folders in sandbox.";
        }

        private void BtnOpenFolder_Click(object sender, EventArgs e)
        {
            string dir = CleanerEngine.GetSandboxDirectory();
            if (Directory.Exists(dir))
            {
                Process.Start("explorer.exe", dir);
            }
        }

        private void UpdateStats()
        {
            string dir = CleanerEngine.GetSandboxDirectory();
            _lblActiveFolderBadge.Text = "📁 Sandbox: " + Path.GetFileName(dir);
            _toolTip.SetToolTip(_lblActiveFolderBadge, "Active Sandbox Folder:\n" + dir + "\n\nClick to change folder.");

            List<SandboxEntry> entries = _engine.GetSandboxEntries();
            long totalSize = 0;
            foreach (var ent in entries) totalSize += ent.Size;

            _lblSessionStats.Text = string.Format("Remaining items: {0} ({1})  |  Session freed: {2} ({3} items cleaned)",
                entries.Count, CleanerEngine.FormatBytes(totalSize),
                CleanerEngine.FormatBytes(_totalBytesCleaned), _totalItemsCleaned);
        }
    }
}
