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

        // --- MenuStrip ---
        menuStrip = new MenuStrip();
        mnuAlgorithm = new ToolStripMenuItem();
        mnuGridApprox = new ToolStripMenuItem();
        mnuShowRectangles = new ToolStripMenuItem();
        mnuMetropolis = new ToolStripMenuItem();
        mnuStepSize0001 = new ToolStripMenuItem();
        mnuStepSize001 = new ToolStripMenuItem();
        mnuStepSize005 = new ToolStripMenuItem();
        mnuStepSize01 = new ToolStripMenuItem();
        mnuStepSize02 = new ToolStripMenuItem();
        mnuSamples5000 = new ToolStripMenuItem();
        mnuSamples10000 = new ToolStripMenuItem();
        mnuSamples50000 = new ToolStripMenuItem();
        mnuShowIterations = new ToolStripMenuItem();
        mnuInitialGuess = new ToolStripMenuItem();

        mnuPrior = new ToolStripMenuItem();
        mnuPriorUniform = new ToolStripMenuItem();
        mnuPriorNormal = new ToolStripMenuItem();
        mnuPriorBeta = new ToolStripMenuItem();
        mnuPriorExponential = new ToolStripMenuItem();
        mnuPriorEnergyTrap = new ToolStripMenuItem();

        // --- Top toolbar ---
        toolbarPanel = new Panel();
        btnDrawPrior = new Button();
        btnReset = new Button();
        btnClearData = new Button();
        lblFlips = new Label();
        lblAlgorithm = new Label();

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
        lblSpeed = new Label();
        trackSpeed = new TrackBar();

        autoFlipTimer = new System.Windows.Forms.Timer(components);

        SuspendLayout();
        menuStrip.SuspendLayout();
        toolbarPanel.SuspendLayout();
        bottomPanel.SuspendLayout();
        ((System.ComponentModel.ISupportInitialize)canvas).BeginInit();
        ((System.ComponentModel.ISupportInitialize)trackTrueP).BeginInit();
        ((System.ComponentModel.ISupportInitialize)trackSpeed).BeginInit();

        // ---- menuStrip ----
        menuStrip.Items.Add(mnuPrior);
        menuStrip.Items.Add(mnuAlgorithm);
        menuStrip.Dock = DockStyle.Top;

        // ---- Prior menu ----
        mnuPrior.Text = "&Prior";
        mnuPrior.DropDownItems.AddRange(new ToolStripItem[]
        {
            mnuPriorUniform,
            mnuPriorNormal,
            mnuPriorBeta,
            mnuPriorExponential,
            new ToolStripSeparator(),
            mnuPriorEnergyTrap,
        });

        mnuPriorUniform.Text = "Uniform";
        mnuPriorUniform.Click += MnuPriorUniform_Click;

        mnuPriorNormal.Text = "Normal...";
        mnuPriorNormal.Click += MnuPriorNormal_Click;

        mnuPriorBeta.Text = "Beta...";
        mnuPriorBeta.Click += MnuPriorBeta_Click;

        mnuPriorExponential.Text = "Exponential...";
        mnuPriorExponential.Click += MnuPriorExponential_Click;

        mnuPriorEnergyTrap.Text = "Energy Trap...";
        mnuPriorEnergyTrap.Click += MnuPriorEnergyTrap_Click;

        // ---- Algorithm menu ----
        mnuAlgorithm.Text = "&Algorithm";
        mnuAlgorithm.DropDownItems.AddRange(new ToolStripItem[]
        {
            mnuGridApprox,
            mnuShowRectangles,
            new ToolStripSeparator(),
            mnuMetropolis,
            mnuShowIterations,
            mnuInitialGuess,
            new ToolStripSeparator(),
            new ToolStripMenuItem("Step Size") { Enabled = false },
            mnuStepSize0001,
            mnuStepSize001,
            mnuStepSize005,
            mnuStepSize01,
            mnuStepSize02,
            new ToolStripSeparator(),
            new ToolStripMenuItem("Samples") { Enabled = false },
            mnuSamples5000,
            mnuSamples10000,
            mnuSamples50000,
        });

        mnuGridApprox.Text = "Grid Approximation";
        mnuGridApprox.Checked = true;
        mnuGridApprox.Click += MnuGridApprox_Click;

        mnuShowRectangles.Text = "    Show Rectangles";
        mnuShowRectangles.CheckOnClick = true;
        mnuShowRectangles.Click += MnuShowRectangles_Click;

        mnuMetropolis.Text = "Metropolis";
        mnuMetropolis.Click += MnuMetropolis_Click;

        mnuShowIterations.Text = "    Show Each Iteration";
        mnuShowIterations.CheckOnClick = true;
        mnuShowIterations.Click += MnuShowIterations_Click;

        mnuInitialGuess.Text = "    Initial Guess: 0.50";
        mnuInitialGuess.Click += MnuInitialGuess_Click;

        mnuStepSize0001.Text = "    0.0001";
        mnuStepSize0001.Click += (s, e) => SetStepSize(0.0001, mnuStepSize0001);
        mnuStepSize001.Text = "    0.001";
        mnuStepSize001.Click += (s, e) => SetStepSize(0.001, mnuStepSize001);
        mnuStepSize005.Text = "    0.05";
        mnuStepSize005.Click += (s, e) => SetStepSize(0.05, mnuStepSize005);
        mnuStepSize01.Text = "    0.1";
        mnuStepSize01.Checked = true;
        mnuStepSize01.Click += (s, e) => SetStepSize(0.1, mnuStepSize01);
        mnuStepSize02.Text = "    0.2";
        mnuStepSize02.Click += (s, e) => SetStepSize(0.2, mnuStepSize02);

        mnuSamples5000.Text = "    5,000";
        mnuSamples5000.Click += (s, e) => SetSamples(5000, mnuSamples5000);
        mnuSamples10000.Text = "    10,000";
        mnuSamples10000.Checked = true;
        mnuSamples10000.Click += (s, e) => SetSamples(10000, mnuSamples10000);
        mnuSamples50000.Text = "    50,000";
        mnuSamples50000.Click += (s, e) => SetSamples(50000, mnuSamples50000);

        UpdateMetropolisMenuEnabled();

        // ---- toolbarPanel ----
        toolbarPanel.Dock = DockStyle.Top;
        toolbarPanel.Height = 44;
        toolbarPanel.Padding = new Padding(8, 6, 8, 6);
        toolbarPanel.Controls.Add(btnDrawPrior);
        toolbarPanel.Controls.Add(btnReset);
        toolbarPanel.Controls.Add(btnClearData);
        toolbarPanel.Controls.Add(lblFlips);
        toolbarPanel.Controls.Add(lblAlgorithm);

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

        lblAlgorithm.Text = "[Grid Approximation]";
        lblAlgorithm.AutoSize = true;
        lblAlgorithm.Location = new Point(520, 12);
        lblAlgorithm.Font = new Font("Segoe UI", 9F, FontStyle.Italic);
        lblAlgorithm.ForeColor = Color.FromArgb(100, 100, 100);

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
        bottomPanel.Controls.Add(lblSpeed);
        bottomPanel.Controls.Add(trackSpeed);

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

        lblSpeed.Text = "Speed:";
        lblSpeed.AutoSize = true;
        lblSpeed.Location = new Point(530, 10);

        trackSpeed.Minimum = 1;    // fastest: 10ms
        trackSpeed.Maximum = 10;   // slowest: 1000ms
        trackSpeed.Value = 5;      // default: ~100ms (mapped logarithmically)
        trackSpeed.TickFrequency = 1;
        trackSpeed.SmallChange = 1;
        trackSpeed.LargeChange = 2;
        trackSpeed.Location = new Point(580, 2);
        trackSpeed.Size = new Size(120, 30);
        trackSpeed.Scroll += TrackSpeed_Scroll;

        // ---- autoFlipTimer ----
        autoFlipTimer.Interval = 100;
        autoFlipTimer.Tick += AutoFlipTimer_Tick;

        // ---- MainForm ----
        Text = "DrawAPrior \u2014 Bayesian Coin-Flip Tool";
        ClientSize = new Size(720, 480);
        MinimumSize = new Size(540, 320);
        MainMenuStrip = menuStrip;
        Controls.Add(canvas);
        Controls.Add(bottomPanel);
        Controls.Add(toolbarPanel);
        Controls.Add(menuStrip);

        menuStrip.ResumeLayout(false);
        menuStrip.PerformLayout();
        toolbarPanel.ResumeLayout(false);
        toolbarPanel.PerformLayout();
        bottomPanel.ResumeLayout(false);
        bottomPanel.PerformLayout();
        ((System.ComponentModel.ISupportInitialize)canvas).EndInit();
        ((System.ComponentModel.ISupportInitialize)trackTrueP).EndInit();
        ((System.ComponentModel.ISupportInitialize)trackSpeed).EndInit();
        ResumeLayout(false);
        PerformLayout();
    }

    private MenuStrip menuStrip;
    private ToolStripMenuItem mnuAlgorithm;
    private ToolStripMenuItem mnuGridApprox;
    private ToolStripMenuItem mnuShowRectangles;
    private ToolStripMenuItem mnuMetropolis;
    private ToolStripMenuItem mnuStepSize0001;
    private ToolStripMenuItem mnuStepSize001;
    private ToolStripMenuItem mnuStepSize005;
    private ToolStripMenuItem mnuStepSize01;
    private ToolStripMenuItem mnuStepSize02;
    private ToolStripMenuItem mnuSamples5000;
    private ToolStripMenuItem mnuSamples10000;
    private ToolStripMenuItem mnuSamples50000;
    private ToolStripMenuItem mnuShowIterations;
    private ToolStripMenuItem mnuInitialGuess;

    private ToolStripMenuItem mnuPrior;
    private ToolStripMenuItem mnuPriorUniform;
    private ToolStripMenuItem mnuPriorNormal;
    private ToolStripMenuItem mnuPriorBeta;
    private ToolStripMenuItem mnuPriorExponential;
    private ToolStripMenuItem mnuPriorEnergyTrap;

    private Panel toolbarPanel;
    private Button btnDrawPrior;
    private Button btnReset;
    private Button btnClearData;
    private Label lblFlips;
    private Label lblAlgorithm;

    private PictureBox canvas;

    private Panel bottomPanel;
    private Label lblTrueP;
    private TrackBar trackTrueP;
    private Label lblTruePValue;
    private Button btnFlip1;
    private Button btnFlip10;
    private Button btnFlip100;
    private Button btnAutoFlip;
    private Label lblSpeed;
    private TrackBar trackSpeed;

    private System.Windows.Forms.Timer autoFlipTimer;
}
