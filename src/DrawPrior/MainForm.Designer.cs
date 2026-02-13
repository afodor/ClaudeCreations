namespace DrawAPrior;

partial class MainForm
{
    private System.ComponentModel.IContainer components = null;

    protected override void Dispose(bool disposing)
    {
        if (disposing && (components != null))
        {
            components.Dispose();
        }
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();

        // --- Top toolbar ---
        toolbarPanel = new Panel();
        btnDrawPrior = new Button();
        btnReset = new Button();
        btnClearData = new Button();
        lblFlips = new Label();

        // --- Canvas ---
        canvas = new PictureBox();

        // --- Bottom controls ---
        bottomPanel = new Panel();
        lblTrueP = new Label();
        trackTrueP = new TrackBar();
        lblTruePValue = new Label();
        btnFlip1 = new Button();
        btnFlip10 = new Button();
        btnFlip100 = new Button();
        btnAutoFlip = new Button();

        autoFlipTimer = new System.Windows.Forms.Timer(components);

        SuspendLayout();
        toolbarPanel.SuspendLayout();
        bottomPanel.SuspendLayout();
        ((System.ComponentModel.ISupportInitialize)canvas).BeginInit();
        ((System.ComponentModel.ISupportInitialize)trackTrueP).BeginInit();

        // ---- toolbarPanel ----
        toolbarPanel.Dock = DockStyle.Top;
        toolbarPanel.Height = 44;
        toolbarPanel.Padding = new Padding(8, 6, 8, 6);
        toolbarPanel.Controls.Add(btnDrawPrior);
        toolbarPanel.Controls.Add(btnReset);
        toolbarPanel.Controls.Add(btnClearData);
        toolbarPanel.Controls.Add(lblFlips);

        btnDrawPrior.Text = "Draw Prior";
        btnDrawPrior.AutoSize = true;
        btnDrawPrior.Location = new Point(8, 8);
        btnDrawPrior.Click += BtnDrawPrior_Click;

        btnReset.Text = "Reset";
        btnReset.AutoSize = true;
        btnReset.Location = new Point(110, 8);
        btnReset.Click += BtnReset_Click;

        btnClearData.Text = "Clear Data";
        btnClearData.AutoSize = true;
        btnClearData.Location = new Point(180, 8);
        btnClearData.Click += BtnClearData_Click;

        lblFlips.Text = "Flips: 0   H: 0   T: 0";
        lblFlips.AutoSize = true;
        lblFlips.Location = new Point(290, 12);
        lblFlips.Font = new Font("Segoe UI", 10F, FontStyle.Bold);

        // ---- canvas ----
        canvas.Dock = DockStyle.Fill;
        canvas.BackColor = Color.White;
        canvas.BorderStyle = BorderStyle.FixedSingle;
        canvas.MouseDown += Canvas_MouseDown;
        canvas.MouseMove += Canvas_MouseMove;
        canvas.MouseUp += Canvas_MouseUp;
        canvas.Paint += Canvas_Paint;
        canvas.Resize += Canvas_Resize;

        // ---- bottomPanel (single compact row) ----
        bottomPanel.Dock = DockStyle.Bottom;
        bottomPanel.Height = 40;
        bottomPanel.Padding = new Padding(4, 2, 4, 2);
        bottomPanel.Controls.Add(lblTrueP);
        bottomPanel.Controls.Add(trackTrueP);
        bottomPanel.Controls.Add(lblTruePValue);
        bottomPanel.Controls.Add(btnFlip1);
        bottomPanel.Controls.Add(btnFlip10);
        bottomPanel.Controls.Add(btnFlip100);
        bottomPanel.Controls.Add(btnAutoFlip);

        lblTrueP.Text = "p(head):";
        lblTrueP.AutoSize = true;
        lblTrueP.Location = new Point(6, 10);

        trackTrueP.Minimum = 0;
        trackTrueP.Maximum = 100;
        trackTrueP.Value = 50;
        trackTrueP.TickFrequency = 10;
        trackTrueP.SmallChange = 1;
        trackTrueP.LargeChange = 5;
        trackTrueP.Location = new Point(70, 2);
        trackTrueP.Size = new Size(180, 30);
        trackTrueP.Scroll += TrackTrueP_Scroll;

        lblTruePValue.Text = "0.50";
        lblTruePValue.AutoSize = true;
        lblTruePValue.Location = new Point(255, 10);
        lblTruePValue.Font = new Font("Segoe UI", 9F, FontStyle.Bold);

        btnFlip1.Text = "+1";
        btnFlip1.Size = new Size(40, 28);
        btnFlip1.Location = new Point(310, 5);
        btnFlip1.Click += (s, e) => DoFlips(1);

        btnFlip10.Text = "+10";
        btnFlip10.Size = new Size(45, 28);
        btnFlip10.Location = new Point(354, 5);
        btnFlip10.Click += (s, e) => DoFlips(10);

        btnFlip100.Text = "+100";
        btnFlip100.Size = new Size(50, 28);
        btnFlip100.Location = new Point(403, 5);
        btnFlip100.Click += (s, e) => DoFlips(100);

        btnAutoFlip.Text = "Auto \u25B6";
        btnAutoFlip.Size = new Size(60, 28);
        btnAutoFlip.Location = new Point(460, 5);
        btnAutoFlip.Click += BtnAutoFlip_Click;

        // ---- autoFlipTimer ----
        autoFlipTimer.Interval = 100;
        autoFlipTimer.Tick += AutoFlipTimer_Tick;

        // ---- MainForm ----
        Text = "DrawAPrior — Bayesian Coin-Flip Tool";
        ClientSize = new Size(720, 480);
        MinimumSize = new Size(540, 320);
        Controls.Add(canvas);
        Controls.Add(bottomPanel);
        Controls.Add(toolbarPanel);

        toolbarPanel.ResumeLayout(false);
        toolbarPanel.PerformLayout();
        bottomPanel.ResumeLayout(false);
        bottomPanel.PerformLayout();
        ((System.ComponentModel.ISupportInitialize)canvas).EndInit();
        ((System.ComponentModel.ISupportInitialize)trackTrueP).EndInit();
        ResumeLayout(false);
    }

    private Panel toolbarPanel;
    private Button btnDrawPrior;
    private Button btnReset;
    private Button btnClearData;
    private Label lblFlips;

    private PictureBox canvas;

    private Panel bottomPanel;
    private Label lblTrueP;
    private TrackBar trackTrueP;
    private Label lblTruePValue;
    private Button btnFlip1;
    private Button btnFlip10;
    private Button btnFlip100;
    private Button btnAutoFlip;

    private System.Windows.Forms.Timer autoFlipTimer;
}
