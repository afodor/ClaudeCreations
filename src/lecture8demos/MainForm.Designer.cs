namespace Lecture08Tools;

partial class MainForm
{
    private System.ComponentModel.IContainer components = null;

    protected override void Dispose(bool disposing)
    {
        if (disposing && (components != null))
            components.Dispose();
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();

        // ── Main layout ──────────────────────────────────────────
        menuStrip = new MenuStrip();
        toolMenu = new ToolStripMenuItem("&Tool");
        menuHypergeometric = new ToolStripMenuItem("Hypergeometric vs Binomial");
        menuFisher = new ToolStripMenuItem("Fisher's Exact Test");
        menuPoisson = new ToolStripMenuItem("Poisson Approximation");
        menuMeanVar = new ToolStripMenuItem("Mean-Variance Explorer");

        menuHypergeometric.Click += (s, e) => SwitchTool(ToolMode.HyperGeometric);
        menuFisher.Click += (s, e) => SwitchTool(ToolMode.FisherExact);
        menuPoisson.Click += (s, e) => SwitchTool(ToolMode.Poisson);
        menuMeanVar.Click += (s, e) => SwitchTool(ToolMode.MeanVariance);

        toolMenu.DropDownItems.AddRange(new ToolStripItem[] {
            menuHypergeometric, menuFisher, menuPoisson, menuMeanVar
        });
        menuStrip.Items.Add(toolMenu);

        leftPanel = new Panel
        {
            Dock = DockStyle.Left,
            Width = 230,
            Padding = new Padding(6),
            BackColor = SystemColors.ControlLight
        };

        canvas = new PictureBox
        {
            Dock = DockStyle.Fill,
            BackColor = Color.White
        };
        canvas.Paint += Canvas_Paint;
        canvas.Resize += (s, e) => canvas.Invalidate();
        canvas.MouseWheel += Canvas_MouseWheel_Zoom;
        canvas.MouseDown += Canvas_MouseDown_Zoom;
        canvas.MouseMove += Canvas_MouseMove_Zoom;
        canvas.MouseUp += Canvas_MouseUp_Zoom;
        canvas.MouseEnter += (s, e) => canvas.Focus();

        statusBar = new StatusStrip();
        statusLabel = new ToolStripStatusLabel("Ready");
        statusBar.Items.Add(statusLabel);

        // ── Tool 1: Hypergeometric ──────────────────────────────
        panelHyper = new Panel { Dock = DockStyle.Fill, AutoScroll = true };

        var yH = 6;
        panelHyper.Controls.Add(MakeLabel("Population N:", 6, yH));
        yH += 20;
        nudHyperN = MakeNud(6, yH, 2, 10000, 50); yH += 30;

        panelHyper.Controls.Add(MakeLabel("Success states K:", 6, yH));
        yH += 20;
        nudHyperK = MakeNud(6, yH, 0, 10000, 15); yH += 30;

        panelHyper.Controls.Add(MakeLabel("Draws n:", 6, yH));
        yH += 20;
        nudHyperDraw = MakeNud(6, yH, 0, 10000, 10); yH += 30;

        chkBinomialApprox = new CheckBox
        {
            Text = "Show Binomial approx",
            Location = new Point(6, yH),
            Width = 200,
            Checked = true
        };
        chkBinomialApprox.CheckedChanged += (s, e) => canvas.Invalidate();
        panelHyper.Controls.Add(chkBinomialApprox); yH += 30;

        panelHyper.Controls.Add(MakeLabel("X Range:", 6, yH));
        yH += 20;

        nudXRangeMin = new NumericUpDown
        {
            Location = new Point(6, yH),
            Width = 70,
            Minimum = 0,
            Maximum = 10000,
            Value = 0
        };
        nudXRangeMin.ValueChanged += NudXRange_ValueChanged;
        panelHyper.Controls.Add(nudXRangeMin);

        panelHyper.Controls.Add(new Label
        {
            Text = "to",
            Location = new Point(80, yH + 3),
            AutoSize = true
        });

        nudXRangeMax = new NumericUpDown
        {
            Location = new Point(98, yH),
            Width = 70,
            Minimum = 0,
            Maximum = 10000,
            Value = 10
        };
        nudXRangeMax.ValueChanged += NudXRange_ValueChanged;
        panelHyper.Controls.Add(nudXRangeMax);
        yH += 28;

        btnResetZoom = new Button
        {
            Text = "Reset Zoom",
            Location = new Point(6, yH),
            Width = 162
        };
        btnResetZoom.Click += (s, e) => ResetHyperZoom();
        panelHyper.Controls.Add(btnResetZoom);
        yH += 30;

        lblHyperInfo = new Label
        {
            Location = new Point(6, yH),
            Size = new Size(210, 120),
            Font = new Font("Consolas", 8.5f)
        };
        panelHyper.Controls.Add(lblHyperInfo);

        nudHyperN.ValueChanged += (s, e) => { ClampHyperParams(); ResetHyperZoom(); };
        nudHyperK.ValueChanged += (s, e) => { ClampHyperParams(); canvas.Invalidate(); };
        nudHyperDraw.ValueChanged += (s, e) => { ClampHyperParams(); ResetHyperZoom(); };

        // ── Tool 2: Fisher's Exact Test ─────────────────────────
        panelFisher = new Panel { Dock = DockStyle.Fill, AutoScroll = true };

        var yF = 6;
        // Column labels
        panelFisher.Controls.Add(MakeLabel("", 6, yF)); // spacer for row label column
        txtFisherCol1 = new TextBox { Text = "Success", Location = new Point(70, yF), Width = 65 };
        txtFisherCol2 = new TextBox { Text = "Failure", Location = new Point(140, yF), Width = 65 };
        panelFisher.Controls.Add(txtFisherCol1);
        panelFisher.Controls.Add(txtFisherCol2);
        yF += 28;

        // Row 1
        txtFisherRow1 = new TextBox { Text = "Group 1", Location = new Point(6, yF), Width = 60 };
        nudFisherA = MakeNud(70, yF, 0, 100000, 8, 65);
        nudFisherB = MakeNud(140, yF, 0, 100000, 2, 65);
        panelFisher.Controls.Add(txtFisherRow1);
        yF += 28;

        // Row 2
        txtFisherRow2 = new TextBox { Text = "Group 2", Location = new Point(6, yF), Width = 60 };
        nudFisherC = MakeNud(70, yF, 0, 100000, 3, 65);
        nudFisherD = MakeNud(140, yF, 0, 100000, 7, 65);
        panelFisher.Controls.Add(txtFisherRow2);
        yF += 34;

        // Alternative hypothesis
        panelFisher.Controls.Add(MakeLabel("Alternative:", 6, yF));
        yF += 20;
        rbFisherTwoSided = new RadioButton { Text = "Two-sided", Location = new Point(6, yF), Width = 100, Checked = true };
        panelFisher.Controls.Add(rbFisherTwoSided); yF += 22;
        rbFisherLess = new RadioButton { Text = "Less", Location = new Point(6, yF), Width = 100 };
        panelFisher.Controls.Add(rbFisherLess); yF += 22;
        rbFisherGreater = new RadioButton { Text = "Greater", Location = new Point(6, yF), Width = 100 };
        panelFisher.Controls.Add(rbFisherGreater); yF += 30;

        btnFisherCompute = new Button { Text = "Compute", Location = new Point(6, yF), Width = 200 };
        btnFisherCompute.Click += BtnFisherCompute_Click;
        panelFisher.Controls.Add(btnFisherCompute); yF += 32;

        lblFisherInfo = new Label
        {
            Location = new Point(6, yF),
            Size = new Size(210, 140),
            Font = new Font("Consolas", 8.5f)
        };
        panelFisher.Controls.Add(lblFisherInfo);

        // ── Tool 3: Poisson Approximation ───────────────────────
        panelPoisson = new Panel { Dock = DockStyle.Fill, AutoScroll = true };

        var yP = 6;
        panelPoisson.Controls.Add(MakeLabel("λ (lambda):", 6, yP));
        yP += 20;
        trkLambda = new TrackBar
        {
            Location = new Point(6, yP),
            Width = 210,
            Minimum = 1,
            Maximum = 500,
            Value = 50,
            TickFrequency = 50,
            SmallChange = 1,
            LargeChange = 10
        };
        panelPoisson.Controls.Add(trkLambda); yP += 50;
        lblLambda = new Label { Location = new Point(6, yP), Size = new Size(210, 18), Text = "λ = 5.0" };
        panelPoisson.Controls.Add(lblLambda); yP += 24;

        panelPoisson.Controls.Add(MakeLabel("n (binomial trials):", 6, yP));
        yP += 20;
        trkPoissonN = new TrackBar
        {
            Location = new Point(6, yP),
            Width = 210,
            Minimum = 1,
            Maximum = 1000,
            Value = 20,
            TickFrequency = 100,
            SmallChange = 1,
            LargeChange = 10
        };
        panelPoisson.Controls.Add(trkPoissonN); yP += 50;
        lblPoissonN = new Label { Location = new Point(6, yP), Size = new Size(210, 18), Text = "n = 20, p = 0.2500" };
        panelPoisson.Controls.Add(lblPoissonN); yP += 24;

        chkShowPoisson = new CheckBox { Text = "Show Poisson", Location = new Point(6, yP), Width = 200, Checked = true };
        chkShowPoisson.CheckedChanged += (s, e) => canvas.Invalidate();
        panelPoisson.Controls.Add(chkShowPoisson); yP += 24;

        chkShowBinomial = new CheckBox { Text = "Show Binomial", Location = new Point(6, yP), Width = 200, Checked = true };
        chkShowBinomial.CheckedChanged += (s, e) => canvas.Invalidate();
        panelPoisson.Controls.Add(chkShowBinomial); yP += 30;

        btnAnimate = new Button { Text = "Animate n → ∞", Location = new Point(6, yP), Width = 200 };
        btnAnimate.Click += BtnAnimate_Click;
        panelPoisson.Controls.Add(btnAnimate); yP += 32;

        lblPoissonInfo = new Label
        {
            Location = new Point(6, yP),
            Size = new Size(210, 80),
            Font = new Font("Consolas", 8.5f)
        };
        panelPoisson.Controls.Add(lblPoissonInfo);

        trkLambda.ValueChanged += (s, e) => { UpdatePoissonLabels(); canvas.Invalidate(); };
        trkPoissonN.ValueChanged += (s, e) => { UpdatePoissonLabels(); canvas.Invalidate(); };

        // ── Tool 4: Mean-Variance Explorer ──────────────────────
        panelMeanVar = new Panel { Dock = DockStyle.Fill, AutoScroll = true };

        var yM = 6;
        panelMeanVar.Controls.Add(MakeLabel("Genes:", 6, yM));
        yM += 20;
        nudGenes = MakeNud(6, yM, 100, 10000, 1000); yM += 30;

        panelMeanVar.Controls.Add(MakeLabel("Samples:", 6, yM));
        yM += 20;
        nudSamples = MakeNud(6, yM, 2, 100, 20); yM += 30;

        panelMeanVar.Controls.Add(MakeLabel("Distribution:", 6, yM));
        yM += 20;
        cmbDistribution = new ComboBox
        {
            Location = new Point(6, yM),
            Width = 210,
            DropDownStyle = ComboBoxStyle.DropDownList
        };
        cmbDistribution.Items.AddRange(new[] { "Poisson", "Negative Binomial" });
        cmbDistribution.SelectedIndex = 0;
        cmbDistribution.SelectedIndexChanged += (s, e) =>
        {
            nudDispersion.Enabled = cmbDistribution.SelectedIndex == 1;
            canvas.Invalidate();
        };
        panelMeanVar.Controls.Add(cmbDistribution); yM += 30;

        panelMeanVar.Controls.Add(MakeLabel("Dispersion (r):", 6, yM));
        yM += 20;
        nudDispersion = MakeNudDecimal(6, yM, 0.1m, 1000m, 10m, 1);
        nudDispersion.Enabled = false;
        yM += 30;

        panelMeanVar.Controls.Add(MakeLabel("Mean range:", 6, yM));
        yM += 20;
        nudMeanMin = MakeNud(6, yM, 1, 100000, 1, 100);
        panelMeanVar.Controls.Add(MakeLabel("to", 110, yM + 3));
        nudMeanMax = MakeNud(130, yM, 1, 100000, 500, 80);
        yM += 30;

        btnSimulate = new Button { Text = "Simulate", Location = new Point(6, yM), Width = 210 };
        btnSimulate.Click += BtnSimulate_Click;
        panelMeanVar.Controls.Add(btnSimulate); yM += 32;

        lblMeanVarInfo = new Label
        {
            Location = new Point(6, yM),
            Size = new Size(210, 100),
            Font = new Font("Consolas", 8.5f)
        };
        panelMeanVar.Controls.Add(lblMeanVarInfo);

        // ── Add sub-panels to left panel ────────────────────────
        leftPanel.Controls.Add(panelHyper);
        leftPanel.Controls.Add(panelFisher);
        leftPanel.Controls.Add(panelPoisson);
        leftPanel.Controls.Add(panelMeanVar);

        // ── Assemble form ───────────────────────────────────────
        SuspendLayout();
        MainMenuStrip = menuStrip;
        Controls.Add(canvas);
        Controls.Add(leftPanel);
        Controls.Add(statusBar);
        Controls.Add(menuStrip);

        Text = "Lecture 08 — Statistics Toolkit";
        ClientSize = new Size(1100, 700);
        StartPosition = FormStartPosition.CenterScreen;
        ResumeLayout(false);
        PerformLayout();
    }

    // ── Helper: create Label ────────────────────────────────────
    private Label MakeLabel(string text, int x, int y)
    {
        return new Label
        {
            Text = text,
            Location = new Point(x, y),
            AutoSize = true,
            Font = new Font("Segoe UI", 9f)
        };
    }

    // ── Helper: create NumericUpDown ────────────────────────────
    private NumericUpDown MakeNud(int x, int y, int min, int max, int val, int width = 210)
    {
        var nud = new NumericUpDown
        {
            Location = new Point(x, y),
            Width = width,
            Minimum = min,
            Maximum = max,
            Value = Math.Clamp(val, min, max)
        };
        panelBeingBuilt().Controls.Add(nud);
        return nud;
    }

    // ── Helper: create decimal NumericUpDown ─────────────────────
    private NumericUpDown MakeNudDecimal(int x, int y, decimal min, decimal max, decimal val, int decimals)
    {
        var nud = new NumericUpDown
        {
            Location = new Point(x, y),
            Width = 210,
            Minimum = min,
            Maximum = max,
            Value = val,
            DecimalPlaces = decimals,
            Increment = decimals == 1 ? 0.1m : 1m
        };
        panelBeingBuilt().Controls.Add(nud);
        return nud;
    }

    private Panel panelBeingBuilt()
    {
        // Determine from call stack context — we use a simpler approach:
        // The last panel added via Controls.Add captures it.
        // Actually, we'll just return the right panel based on which nud fields are null.
        if (nudHyperN == null! || nudHyperDraw == null!) return panelHyper;
        if (nudFisherD == null!) return panelFisher;
        if (nudGenes == null! || nudSamples == null! || nudDispersion == null! || nudMeanMax == null!) return panelMeanVar;
        return panelMeanVar; // fallback
    }

    // ── Field declarations ──────────────────────────────────────
    private MenuStrip menuStrip;
    private ToolStripMenuItem toolMenu;
    private ToolStripMenuItem menuHypergeometric, menuFisher, menuPoisson, menuMeanVar;

    private Panel leftPanel;
    private PictureBox canvas;
    private StatusStrip statusBar;
    private ToolStripStatusLabel statusLabel;

    // Tool 1: Hypergeometric
    private Panel panelHyper;
    private NumericUpDown nudHyperN, nudHyperK, nudHyperDraw;
    private CheckBox chkBinomialApprox;
    private NumericUpDown nudXRangeMin, nudXRangeMax;
    private Button btnResetZoom;
    private Label lblHyperInfo;

    // Tool 2: Fisher
    private Panel panelFisher;
    private TextBox txtFisherRow1, txtFisherRow2, txtFisherCol1, txtFisherCol2;
    private NumericUpDown nudFisherA, nudFisherB, nudFisherC, nudFisherD;
    private RadioButton rbFisherTwoSided, rbFisherLess, rbFisherGreater;
    private Button btnFisherCompute;
    private Label lblFisherInfo;

    // Tool 3: Poisson
    private Panel panelPoisson;
    private TrackBar trkLambda, trkPoissonN;
    private Label lblLambda, lblPoissonN, lblPoissonInfo;
    private CheckBox chkShowPoisson, chkShowBinomial;
    private Button btnAnimate;

    // Tool 4: Mean-Variance
    private Panel panelMeanVar;
    private NumericUpDown nudGenes, nudSamples, nudDispersion, nudMeanMin, nudMeanMax;
    private ComboBox cmbDistribution;
    private Button btnSimulate;
    private Label lblMeanVarInfo;
}
