namespace DrawAPrior;

public partial class MainForm : Form
{
    private const int GridSize = 200;
    private const double Epsilon = 1e-6;

    // Grid values: π_i for i = 0..GridSize-1
    private readonly double[] piGrid = new double[GridSize];

    // Prior and posterior distributions
    private double[] originalPrior = new double[GridSize];
    private double[] posterior = new double[GridSize];

    // Drawing state
    private bool isDrawingMode;
    private bool isMouseDown;
    private readonly List<PointF> drawnPoints = new();

    // Raw drawn density indexed by grid cell — used during drawing
    private double[] drawnDensity = new double[GridSize];
    private bool[] drawnMask = new bool[GridSize];

    // Flip state
    private int totalFlips;
    private int totalHeads;
    private int totalTails;
    private bool hasPrior;

    // RNG
    private readonly Random rng = new();

    // Algorithm state
    private bool useMetropolis;
    private bool showRectangles;
    private double metropolisStepSize = 0.1;
    private double metropolisInitialGuess = 0.5;
    private int metropolisSamples = 10000;
    private double[] metropolisPosterior = new double[GridSize];

    // Persistent Metropolis chain state (for "show each iteration" mode)
    private bool showMetropolisIterations;
    private bool chainInitialized;
    private double chainPosition;
    private double chainLogP;
    private int chainTotalSteps;
    private readonly List<double> chainTrail = new();

    // Canvas margins for the plot area
    private const int MarginLeft = 55;
    private const int MarginRight = 20;
    private const int MarginTop = 20;
    private const int MarginBottom = 40;

    public MainForm()
    {
        InitializeComponent();
        InitializeGrid();
        SetDoubleBuffered(canvas);
        LoadAppIcon();
    }

    private void LoadAppIcon()
    {
        string icoPath = Path.Combine(AppContext.BaseDirectory, "app.ico");
        if (!File.Exists(icoPath))
            icoPath = Path.Combine(Path.GetDirectoryName(Application.ExecutablePath)!, "..", "..", "..", "app.ico");
        if (File.Exists(icoPath))
            Icon = new Icon(icoPath);
    }

    private static void SetDoubleBuffered(Control control)
    {
        typeof(Control)
            .GetProperty("DoubleBuffered", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .SetValue(control, true);
    }

    private void InitializeGrid()
    {
        for (int i = 0; i < GridSize; i++)
        {
            // Center of each grid cell: avoid exact 0 and 1
            piGrid[i] = (i + 0.5) / GridSize;
        }
    }

    // ──────────────────────────────────────────────
    //  Coordinate mapping between canvas and plot
    // ──────────────────────────────────────────────

    private RectangleF PlotArea
    {
        get
        {
            float w = canvas.Width - MarginLeft - MarginRight;
            float h = canvas.Height - MarginTop - MarginBottom;
            return new RectangleF(MarginLeft, MarginTop, Math.Max(w, 1), Math.Max(h, 1));
        }
    }

    /// <summary>Map a canvas pixel to (piValue, density) in plot coordinates.</summary>
    private (double pi, double density) CanvasToPlot(float cx, float cy)
    {
        var area = PlotArea;
        double pi = (cx - area.X) / area.Width;           // 0..1
        double density = 1.0 - (cy - area.Y) / area.Height; // 0..1 (bottom=0, top=1)
        return (Math.Clamp(pi, 0, 1), Math.Clamp(density, 0, 1));
    }

    /// <summary>Map a (gridIndex, densityValue) to canvas pixel coordinates.
    /// densityValue is already in 0..maxDensity; we pass maxDensity to scale.</summary>
    private PointF PlotToCanvas(int gridIndex, double densityValue, double maxDensity)
    {
        var area = PlotArea;
        float x = area.X + (float)(piGrid[gridIndex] * area.Width);
        float y = area.Y + area.Height - (float)(densityValue / Math.Max(maxDensity, 1e-12) * area.Height);
        return new PointF(x, y);
    }

    // ──────────────────────────────────────────────
    //  Button handlers
    // ──────────────────────────────────────────────

    private void BtnDrawPrior_Click(object? sender, EventArgs e)
    {
        if (isDrawingMode)
        {
            // Finish drawing
            FinishDrawing();
            return;
        }

        // Enter drawing mode
        isDrawingMode = true;
        btnDrawPrior.Text = "Done Drawing";
        hasPrior = false;
        totalFlips = 0;
        totalHeads = 0;
        totalTails = 0;
        Array.Fill(drawnDensity, 0.0);
        Array.Fill(drawnMask, false);
        drawnPoints.Clear();
        Array.Fill(originalPrior, 0.0);
        Array.Fill(posterior, 0.0);
        Array.Fill(metropolisPosterior, 0.0);
        ResetChain();
        UpdateFlipLabel();
        canvas.Invalidate();
    }

    private void BtnReset_Click(object? sender, EventArgs e)
    {
        StopAutoFlip();
        isDrawingMode = false;
        btnDrawPrior.Text = "Draw Prior";
        hasPrior = false;
        totalFlips = 0;
        totalHeads = 0;
        totalTails = 0;
        Array.Fill(originalPrior, 0.0);
        Array.Fill(posterior, 0.0);
        Array.Fill(metropolisPosterior, 0.0);
        ResetChain();
        Array.Fill(drawnDensity, 0.0);
        Array.Fill(drawnMask, false);
        drawnPoints.Clear();
        UpdateFlipLabel();
        canvas.Invalidate();
    }

    private void BtnClearData_Click(object? sender, EventArgs e)
    {
        StopAutoFlip();
        totalFlips = 0;
        totalHeads = 0;
        totalTails = 0;
        Array.Copy(originalPrior, posterior, GridSize);
        Array.Fill(metropolisPosterior, 0.0);
        ResetChain();
        UpdateFlipLabel();
        canvas.Invalidate();
    }

    private void TrackTrueP_Scroll(object? sender, EventArgs e)
    {
        lblTruePValue.Text = (trackTrueP.Value / 100.0).ToString("F2");
    }

    private void TrackSpeed_Scroll(object? sender, EventArgs e)
    {
        // Map slider 1..10: left=slow (1=1000ms), right=fast (10=10ms)
        double t = 1.0 - (trackSpeed.Value - 1) / 9.0; // 1..0
        int interval = (int)(10 * Math.Pow(100, t));     // 10..1000
        autoFlipTimer.Interval = Math.Clamp(interval, 10, 1000);
    }

    private void BtnAutoFlip_Click(object? sender, EventArgs e)
    {
        if (autoFlipTimer.Enabled)
        {
            StopAutoFlip();
        }
        else
        {
            // Auto-finish drawing if user clicks Auto while still drawing
            if (isDrawingMode)
                FinishDrawing();

            if (hasPrior)
            {
                autoFlipTimer.Start();
                btnAutoFlip.Text = "Auto \u25A0"; // ■ stop symbol
            }
        }
    }

    private void StopAutoFlip()
    {
        autoFlipTimer.Stop();
        btnAutoFlip.Text = "Auto \u25B6"; // ▶ play symbol
    }

    private void AutoFlipTimer_Tick(object? sender, EventArgs e)
    {
        DoFlips(1);
    }

    // ──────────────────────────────────────────────
    //  Algorithm menu handlers
    // ──────────────────────────────────────────────

    private void MnuGridApprox_Click(object? sender, EventArgs e)
    {
        if (!useMetropolis) return; // already selected
        useMetropolis = false;
        mnuGridApprox.Checked = true;
        mnuMetropolis.Checked = false;
        UpdateMetropolisMenuEnabled();
        lblAlgorithm.Text = "[Grid Approximation]";
        canvas.Invalidate();
    }

    private void MnuMetropolis_Click(object? sender, EventArgs e)
    {
        if (useMetropolis) return; // already selected
        useMetropolis = true;
        mnuGridApprox.Checked = false;
        mnuMetropolis.Checked = true;
        UpdateMetropolisMenuEnabled();
        lblAlgorithm.Text = "[Metropolis]";
        ResetChain();
        if (totalFlips > 0 && hasPrior && !showMetropolisIterations)
            UpdatePosteriorMetropolis();
        canvas.Invalidate();
    }

    private void MnuShowRectangles_Click(object? sender, EventArgs e)
    {
        showRectangles = mnuShowRectangles.Checked;
        canvas.Invalidate();
    }

    private void MnuShowIterations_Click(object? sender, EventArgs e)
    {
        showMetropolisIterations = mnuShowIterations.Checked;
        if (showMetropolisIterations)
        {
            // Reset chain so it starts fresh
            ResetChain();
            // Rebuild histogram incrementally from scratch if we have flips
            if (totalFlips > 0 && hasPrior)
            {
                Array.Fill(metropolisPosterior, 0.0);
                canvas.Invalidate();
            }
        }
        else
        {
            // Switching off: recompute full batch Metropolis if we have data
            if (useMetropolis && totalFlips > 0 && hasPrior)
                UpdatePosteriorMetropolis();
            canvas.Invalidate();
        }
    }

    private void MnuInitialGuess_Click(object? sender, EventArgs e)
    {
        using var dlg = new PriorParameterDialog("Metropolis Initial Guess",
            new[] { "Starting \u03C0" }, new[] { metropolisInitialGuess });
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        double val = dlg.Results![0];
        if (val <= 0 || val >= 1) { MessageBox.Show("Initial guess must be between 0 and 1 (exclusive)."); return; }
        metropolisInitialGuess = val;
        mnuInitialGuess.Text = $"    Initial Guess: {val:F2}";
        ResetChain();
        if (useMetropolis && totalFlips > 0 && hasPrior)
        {
            if (!showMetropolisIterations)
                UpdatePosteriorMetropolis();
            else
                Array.Fill(metropolisPosterior, 0.0);
            canvas.Invalidate();
        }
    }

    private void ResetChain()
    {
        chainInitialized = false;
        chainPosition = metropolisInitialGuess;
        chainLogP = double.NegativeInfinity;
        chainTotalSteps = 0;
        chainTrail.Clear();
    }

    private void UpdateMetropolisMenuEnabled()
    {
        // Show Rectangles only meaningful in grid approx mode
        mnuShowRectangles.Enabled = !useMetropolis;

        // Show Iterations and Initial Guess only meaningful in Metropolis mode
        mnuShowIterations.Enabled = useMetropolis;
        mnuInitialGuess.Enabled = useMetropolis;

        // Step size and samples only meaningful in Metropolis mode
        // (samples not relevant in show-iterations mode, but keep enabled for batch)
        mnuStepSize0001.Enabled = useMetropolis;
        mnuStepSize001.Enabled = useMetropolis;
        mnuStepSize005.Enabled = useMetropolis;
        mnuStepSize01.Enabled = useMetropolis;
        mnuStepSize02.Enabled = useMetropolis;
        mnuSamples5000.Enabled = useMetropolis;
        mnuSamples10000.Enabled = useMetropolis;
        mnuSamples50000.Enabled = useMetropolis;
    }

    private void SetStepSize(double size, ToolStripMenuItem selected)
    {
        metropolisStepSize = size;
        mnuStepSize0001.Checked = false;
        mnuStepSize001.Checked = false;
        mnuStepSize005.Checked = false;
        mnuStepSize01.Checked = false;
        mnuStepSize02.Checked = false;
        selected.Checked = true;
        ResetChain();
        if (useMetropolis && totalFlips > 0 && hasPrior)
        {
            if (!showMetropolisIterations)
                UpdatePosteriorMetropolis();
            else
                Array.Fill(metropolisPosterior, 0.0);
            canvas.Invalidate();
        }
    }

    private void SetSamples(int count, ToolStripMenuItem selected)
    {
        metropolisSamples = count;
        mnuSamples5000.Checked = false;
        mnuSamples10000.Checked = false;
        mnuSamples50000.Checked = false;
        selected.Checked = true;
        if (useMetropolis && totalFlips > 0 && hasPrior)
        {
            UpdatePosteriorMetropolis();
            canvas.Invalidate();
        }
    }

    // ──────────────────────────────────────────────
    //  Prior menu handlers
    // ──────────────────────────────────────────────

    /// <summary>Sets the prior from a function f(π), normalizes, and resets flip state.</summary>
    private void SetPriorFromFunction(Func<double, double> f)
    {
        StopAutoFlip();
        isDrawingMode = false;
        btnDrawPrior.Text = "Draw Prior";

        for (int i = 0; i < GridSize; i++)
            originalPrior[i] = Math.Max(f(piGrid[i]), Epsilon);

        NormalizeDistribution(originalPrior);
        Array.Copy(originalPrior, posterior, GridSize);
        Array.Fill(metropolisPosterior, 0.0);
        ResetChain();

        totalFlips = 0;
        totalHeads = 0;
        totalTails = 0;
        hasPrior = true;

        drawnPoints.Clear();
        Array.Fill(drawnDensity, 0.0);
        Array.Fill(drawnMask, false);

        UpdateFlipLabel();
        canvas.Invalidate();
    }

    private void MnuPriorUniform_Click(object? sender, EventArgs e)
    {
        SetPriorFromFunction(_ => 1.0);
    }

    private void MnuPriorNormal_Click(object? sender, EventArgs e)
    {
        using var dlg = new PriorParameterDialog("Normal Prior",
            new[] { "Mean", "Std Dev" }, new[] { 0.5, 0.1 });
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        double mean = dlg.Results![0];
        double sd = dlg.Results[1];
        if (sd <= 0) { MessageBox.Show("Std Dev must be > 0."); return; }
        SetPriorFromFunction(x =>
            Math.Exp(-0.5 * Math.Pow((x - mean) / sd, 2)));
    }

    private void MnuPriorBeta_Click(object? sender, EventArgs e)
    {
        using var dlg = new PriorParameterDialog("Beta Prior",
            new[] { "Alpha", "Beta" }, new[] { 10.0, 10.0 });
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        double a = dlg.Results![0];
        double b = dlg.Results[1];
        if (a <= 0 || b <= 0) { MessageBox.Show("Alpha and Beta must be > 0."); return; }
        // Beta density kernel: x^(a-1) * (1-x)^(b-1), computed in log space
        SetPriorFromFunction(x =>
            Math.Exp((a - 1) * Math.Log(x) + (b - 1) * Math.Log(1.0 - x)));
    }

    private void MnuPriorExponential_Click(object? sender, EventArgs e)
    {
        using var dlg = new PriorParameterDialog("Exponential Prior",
            new[] { "Rate" }, new[] { 5.0 });
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        double rate = dlg.Results![0];
        if (rate <= 0) { MessageBox.Show("Rate must be > 0."); return; }
        SetPriorFromFunction(x => Math.Exp(-rate * x));
    }

    private void MnuPriorEnergyTrap_Click(object? sender, EventArgs e)
    {
        using var dlg = new PriorParameterDialog("Energy Trap Prior",
            new[] { "Peak 1 (location)", "Peak 2 (location)", "Peak 3 (location)", "Width (SD)" },
            new[] { 0.2, 0.5, 0.8, 0.02 });
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        double p1 = dlg.Results![0];
        double p2 = dlg.Results[1];
        double p3 = dlg.Results[2];
        double w  = dlg.Results[3];
        if (w <= 0) { MessageBox.Show("Width must be > 0."); return; }
        // Three narrow Gaussians + low baseline
        SetPriorFromFunction(x =>
        {
            double Gauss(double center) =>
                Math.Exp(-0.5 * Math.Pow((x - center) / w, 2));
            return 0.01 + Gauss(p1) + 0.6 * Gauss(p2) + Gauss(p3);
        });
    }

    // ──────────────────────────────────────────────
    //  Drawing on canvas
    // ──────────────────────────────────────────────

    private void Canvas_MouseDown(object? sender, MouseEventArgs e)
    {
        if (!isDrawingMode || e.Button != MouseButtons.Left) return;
        isMouseDown = true;
        RecordDrawPoint(e.Location);
        canvas.Invalidate();
    }

    private void Canvas_MouseMove(object? sender, MouseEventArgs e)
    {
        if (!isMouseDown || !isDrawingMode) return;
        RecordDrawPoint(e.Location);
        canvas.Invalidate();
    }

    private void Canvas_MouseUp(object? sender, MouseEventArgs e)
    {
        isMouseDown = false;
    }

    private void RecordDrawPoint(Point location)
    {
        var (pi, density) = CanvasToPlot(location.X, location.Y);
        drawnPoints.Add(new PointF((float)pi, (float)density));

        // Map to grid cell
        int idx = (int)(pi * GridSize);
        idx = Math.Clamp(idx, 0, GridSize - 1);
        drawnDensity[idx] = density;
        drawnMask[idx] = true;
    }

    private void FinishDrawing()
    {
        isDrawingMode = false;
        btnDrawPrior.Text = "Draw Prior";

        // Interpolate gaps in drawnDensity
        InterpolateDrawnPrior();

        // Copy to originalPrior and normalize
        Array.Copy(drawnDensity, originalPrior, GridSize);
        NormalizeDistribution(originalPrior);
        Array.Copy(originalPrior, posterior, GridSize);

        hasPrior = true;
        canvas.Invalidate();
    }

    private void InterpolateDrawnPrior()
    {
        // Find first and last drawn indices
        int first = -1, last = -1;
        for (int i = 0; i < GridSize; i++)
        {
            if (drawnMask[i])
            {
                if (first == -1) first = i;
                last = i;
            }
        }

        if (first == -1)
        {
            // Nothing drawn — use uniform
            Array.Fill(drawnDensity, 1.0);
            return;
        }

        // Fill before first drawn point with first value
        for (int i = 0; i < first; i++)
            drawnDensity[i] = Epsilon;

        // Fill after last drawn point with last value
        for (int i = last + 1; i < GridSize; i++)
            drawnDensity[i] = Epsilon;

        // Linear interpolation between drawn points
        int prev = first;
        for (int i = first + 1; i <= last; i++)
        {
            if (drawnMask[i])
            {
                // Interpolate between prev and i
                if (i - prev > 1)
                {
                    double v0 = drawnDensity[prev];
                    double v1 = drawnDensity[i];
                    for (int j = prev + 1; j < i; j++)
                    {
                        double t = (double)(j - prev) / (i - prev);
                        drawnDensity[j] = v0 + t * (v1 - v0);
                    }
                }
                prev = i;
            }
        }

        // Ensure minimum epsilon everywhere
        for (int i = 0; i < GridSize; i++)
        {
            if (drawnDensity[i] < Epsilon)
                drawnDensity[i] = Epsilon;
        }
    }

    // ──────────────────────────────────────────────
    //  Flipping & Bayesian update
    // ──────────────────────────────────────────────

    private void DoFlips(int count)
    {
        // Auto-finish drawing if user clicks a flip button while still drawing
        if (isDrawingMode)
            FinishDrawing();

        if (!hasPrior) return;

        double trueP = trackTrueP.Value / 100.0;
        for (int i = 0; i < count; i++)
        {
            totalFlips++;
            if (rng.NextDouble() < trueP)
                totalHeads++;
            else
                totalTails++;
        }

        // Always compute grid approximation (fast, needed as reference in Metropolis mode)
        UpdatePosterior();

        if (useMetropolis)
        {
            if (showMetropolisIterations)
                StepChain(count);
            else
                UpdatePosteriorMetropolis();
        }

        UpdateFlipLabel();
        canvas.Invalidate();
    }

    private void UpdatePosterior()
    {
        // Log-space computation for numerical stability
        double[] logPost = new double[GridSize];
        for (int i = 0; i < GridSize; i++)
        {
            double logPrior = Math.Log(originalPrior[i]);
            double logLik = totalHeads * Math.Log(piGrid[i])
                          + totalTails * Math.Log(1.0 - piGrid[i]);
            logPost[i] = logPrior + logLik;
        }

        // Shift by max for numerical stability
        double maxLog = double.NegativeInfinity;
        for (int i = 0; i < GridSize; i++)
        {
            if (logPost[i] > maxLog) maxLog = logPost[i];
        }

        double sum = 0;
        for (int i = 0; i < GridSize; i++)
        {
            posterior[i] = Math.Exp(logPost[i] - maxLog);
            sum += posterior[i];
        }

        // Normalize
        if (sum > 0)
        {
            for (int i = 0; i < GridSize; i++)
                posterior[i] /= sum;
        }
    }

    // ──────────────────────────────────────────────
    //  Metropolis-Hastings sampler
    // ──────────────────────────────────────────────

    /// <summary>Log-posterior (unnormalized) at a given π, interpolating the prior from the grid.</summary>
    private double LogPosteriorAt(double pi)
    {
        if (pi <= 0 || pi >= 1) return double.NegativeInfinity;

        double gridPos = pi * GridSize - 0.5;
        int lo = (int)Math.Floor(gridPos);
        int hi = lo + 1;
        lo = Math.Clamp(lo, 0, GridSize - 1);
        hi = Math.Clamp(hi, 0, GridSize - 1);
        double t = gridPos - Math.Floor(gridPos);
        double priorVal = originalPrior[lo] * (1 - t) + originalPrior[hi] * t;

        if (priorVal <= 0) return double.NegativeInfinity;

        return Math.Log(priorVal)
             + totalHeads * Math.Log(pi)
             + totalTails * Math.Log(1.0 - pi);
    }

    private double NextNormal()
    {
        double u1 = 1.0 - rng.NextDouble();
        double u2 = rng.NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
    }

    private static double ReflectIntoBounds(double v)
    {
        while (v < 0 || v > 1)
        {
            if (v < 0) v = -v;
            if (v > 1) v = 2.0 - v;
        }
        return v;
    }

    private void UpdatePosteriorMetropolis()
    {
        Array.Fill(metropolisPosterior, 0.0);

        int totalSamples = metropolisSamples;
        int burnIn = totalSamples / 5; // discard first 20%

        double piCurrent = metropolisInitialGuess;
        double logPCurrent = LogPosteriorAt(piCurrent);

        for (int s = 0; s < totalSamples; s++)
        {
            double piNew = ReflectIntoBounds(piCurrent + metropolisStepSize * NextNormal());

            double logPNew = LogPosteriorAt(piNew);
            double logAlpha = logPNew - logPCurrent;

            if (Math.Log(rng.NextDouble()) < logAlpha)
            {
                piCurrent = piNew;
                logPCurrent = logPNew;
            }

            if (s >= burnIn)
            {
                int bin = (int)(piCurrent * GridSize);
                bin = Math.Clamp(bin, 0, GridSize - 1);
                metropolisPosterior[bin]++;
            }
        }

        NormalizeDistribution(metropolisPosterior);
    }

    /// <summary>Run N incremental Metropolis steps, adding each to the histogram and trail.</summary>
    private void StepChain(int steps)
    {
        if (!chainInitialized)
        {
            chainPosition = metropolisInitialGuess;
            chainLogP = LogPosteriorAt(chainPosition);
            chainInitialized = true;
        }
        else
        {
            // Recompute log-posterior at current position (data changed since last step)
            chainLogP = LogPosteriorAt(chainPosition);
        }

        for (int s = 0; s < steps; s++)
        {
            double piNew = ReflectIntoBounds(chainPosition + metropolisStepSize * NextNormal());
            double logPNew = LogPosteriorAt(piNew);
            double logAlpha = logPNew - chainLogP;

            if (Math.Log(rng.NextDouble()) < logAlpha)
            {
                chainPosition = piNew;
                chainLogP = logPNew;
            }

            chainTotalSteps++;
            chainTrail.Add(chainPosition);

            // Bin every step (no burn-in — the point is to watch it explore)
            int bin = (int)(chainPosition * GridSize);
            bin = Math.Clamp(bin, 0, GridSize - 1);
            metropolisPosterior[bin]++;
        }
    }

    private void UpdateFlipLabel()
    {
        lblFlips.Text = $"Flips: {totalFlips}   H: {totalHeads}   T: {totalTails}";
    }

    private static void NormalizeDistribution(double[] dist)
    {
        double sum = 0;
        for (int i = 0; i < dist.Length; i++) sum += dist[i];
        if (sum > 0)
        {
            for (int i = 0; i < dist.Length; i++) dist[i] /= sum;
        }
    }

    // ──────────────────────────────────────────────
    //  Canvas painting
    // ──────────────────────────────────────────────

    private void Canvas_Resize(object? sender, EventArgs e)
    {
        canvas.Invalidate();
    }

    private void Canvas_Paint(object? sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        var area = PlotArea;

        DrawAxes(g, area);

        if (isDrawingMode)
        {
            DrawLiveSketch(g, area);
            return;
        }

        if (!hasPrior) return;

        if (useMetropolis)
        {
            // --- Metropolis mode ---
            // For show-iterations, normalize a copy for display so the raw counts keep accumulating
            double[] displayHist = metropolisPosterior;
            if (showMetropolisIterations && chainTotalSteps > 0)
            {
                displayHist = new double[GridSize];
                Array.Copy(metropolisPosterior, displayHist, GridSize);
                NormalizeDistribution(displayHist);
            }

            double maxDensity = 0;
            for (int i = 0; i < GridSize; i++)
            {
                if (originalPrior[i] > maxDensity) maxDensity = originalPrior[i];
                if (displayHist[i] > maxDensity) maxDensity = displayHist[i];
                if (posterior[i] > maxDensity) maxDensity = posterior[i];
            }
            if (maxDensity < 1e-12) maxDensity = 1e-12;

            // Draw prior as blue filled area
            DrawFilledDistribution(g, area, originalPrior, maxDensity,
                Color.FromArgb(60, 30, 100, 220), Color.FromArgb(180, 30, 100, 220));

            // Draw Metropolis posterior as orange histogram bars
            if (totalFlips > 0)
            {
                DrawHistogramBars(g, area, displayHist, maxDensity,
                    Color.FromArgb(100, 230, 120, 20), Color.FromArgb(200, 230, 120, 20));

                // Draw exact grid posterior as red line
                DrawDistributionLine(g, area, posterior, maxDensity,
                    Color.FromArgb(220, 200, 30, 30), 2.5f);
            }

            // Draw walker trail and current position
            if (showMetropolisIterations && chainTrail.Count > 0)
                DrawChainWalker(g, area);
        }
        else
        {
            // --- Grid Approximation mode ---
            double maxDensity = 0;
            for (int i = 0; i < GridSize; i++)
            {
                if (originalPrior[i] > maxDensity) maxDensity = originalPrior[i];
                if (posterior[i] > maxDensity) maxDensity = posterior[i];
            }
            if (maxDensity < 1e-12) maxDensity = 1e-12;

            if (showRectangles)
            {
                // Draw prior as discrete blue rectangles
                DrawGridRectangles(g, area, originalPrior, maxDensity,
                    Color.FromArgb(60, 30, 100, 220), Color.FromArgb(160, 30, 100, 220));

                // Draw posterior as discrete red rectangles
                if (totalFlips > 0)
                {
                    DrawGridRectangles(g, area, posterior, maxDensity,
                        Color.FromArgb(60, 200, 30, 30), Color.FromArgb(200, 200, 30, 30));
                }
            }
            else
            {
                // Draw prior as blue filled area
                DrawFilledDistribution(g, area, originalPrior, maxDensity,
                    Color.FromArgb(60, 30, 100, 220), Color.FromArgb(180, 30, 100, 220));

                // Draw posterior as red line
                if (totalFlips > 0)
                {
                    DrawDistributionLine(g, area, posterior, maxDensity,
                        Color.FromArgb(220, 200, 30, 30), 2.5f);
                }
            }
        }

        // Draw true p(head) as green dashed vertical
        DrawTruePLine(g, area);
    }

    private void DrawAxes(Graphics g, RectangleF area)
    {
        using var axisPen = new Pen(Color.FromArgb(100, 100, 100), 1f);
        using var labelFont = new Font("Segoe UI", 8.5f);
        using var titleFont = new Font("Segoe UI", 9f, FontStyle.Bold);
        using var brush = new SolidBrush(Color.FromArgb(80, 80, 80));

        // Draw axes lines
        g.DrawLine(axisPen, area.Left, area.Bottom, area.Right, area.Bottom); // X axis
        g.DrawLine(axisPen, area.Left, area.Top, area.Left, area.Bottom);     // Y axis

        // X axis labels
        for (int tick = 0; tick <= 10; tick++)
        {
            float x = area.Left + tick / 10f * area.Width;
            g.DrawLine(axisPen, x, area.Bottom, x, area.Bottom + 4);
            string label = (tick / 10.0).ToString("F1");
            var sz = g.MeasureString(label, labelFont);
            g.DrawString(label, labelFont, brush, x - sz.Width / 2, area.Bottom + 5);
        }

        // X axis title
        {
            string title = "\u03C0 (probability of heads)";
            var sz = g.MeasureString(title, titleFont);
            g.DrawString(title, titleFont, brush, area.Left + area.Width / 2 - sz.Width / 2, area.Bottom + 20);
        }

        // Y axis label
        {
            string title = "Density";
            var sz = g.MeasureString(title, titleFont);
            var state = g.Save();
            g.TranslateTransform(area.Left - 40, area.Top + area.Height / 2 + sz.Width / 2);
            g.RotateTransform(-90);
            g.DrawString(title, titleFont, brush, 0, 0);
            g.Restore(state);
        }

        // Light grid lines
        using var gridPen = new Pen(Color.FromArgb(35, 150, 150, 150), 1f);
        gridPen.DashStyle = System.Drawing.Drawing2D.DashStyle.Dot;
        for (int tick = 1; tick <= 9; tick++)
        {
            float x = area.Left + tick / 10f * area.Width;
            g.DrawLine(gridPen, x, area.Top, x, area.Bottom);
        }
        for (int tick = 1; tick <= 4; tick++)
        {
            float y = area.Top + tick / 5f * area.Height;
            g.DrawLine(gridPen, area.Left, y, area.Right, y);
        }
    }

    private void DrawFilledDistribution(Graphics g, RectangleF area,
        double[] dist, double maxDensity, Color fillColor, Color lineColor)
    {
        var points = new PointF[GridSize + 2];
        for (int i = 0; i < GridSize; i++)
        {
            points[i + 1] = PlotToCanvas(i, dist[i], maxDensity);
        }
        // Close the polygon along the bottom
        points[0] = new PointF(PlotToCanvas(0, 0, maxDensity).X, area.Bottom);
        points[GridSize + 1] = new PointF(PlotToCanvas(GridSize - 1, 0, maxDensity).X, area.Bottom);

        using var fillBrush = new SolidBrush(fillColor);
        g.FillPolygon(fillBrush, points);

        // Draw the outline (skip the two bottom-closing points)
        using var linePen = new Pen(lineColor, 1.8f);
        var linePoints = new PointF[GridSize];
        Array.Copy(points, 1, linePoints, 0, GridSize);
        g.DrawLines(linePen, linePoints);
    }

    private void DrawDistributionLine(Graphics g, RectangleF area,
        double[] dist, double maxDensity, Color color, float width)
    {
        var points = new PointF[GridSize];
        for (int i = 0; i < GridSize; i++)
        {
            points[i] = PlotToCanvas(i, dist[i], maxDensity);
        }
        using var pen = new Pen(color, width);
        g.DrawLines(pen, points);
    }

    private void DrawDashedDistributionLine(Graphics g, RectangleF area,
        double[] dist, double maxDensity, Color color, float width)
    {
        var points = new PointF[GridSize];
        for (int i = 0; i < GridSize; i++)
        {
            points[i] = PlotToCanvas(i, dist[i], maxDensity);
        }
        using var pen = new Pen(color, width);
        pen.DashStyle = System.Drawing.Drawing2D.DashStyle.Dash;
        g.DrawLines(pen, points);
    }

    private void DrawGridRectangles(Graphics g, RectangleF area,
        double[] dist, double maxDensity, Color fillColor, Color outlineColor)
    {
        float cellWidth = area.Width / GridSize;

        using var fillBrush = new SolidBrush(fillColor);
        using var outlinePen = new Pen(outlineColor, 0.8f);

        for (int i = 0; i < GridSize; i++)
        {
            float x = area.Left + (float)(piGrid[i] * area.Width) - cellWidth / 2f;
            float height = (float)(dist[i] / Math.Max(maxDensity, 1e-12) * area.Height);
            float y = area.Bottom - height;

            if (height < 0.5f) continue; // skip negligible bars

            var rect = new RectangleF(x, y, cellWidth, height);
            g.FillRectangle(fillBrush, rect);
            g.DrawRectangle(outlinePen, rect.X, rect.Y, rect.Width, rect.Height);
        }
    }

    private void DrawHistogramBars(Graphics g, RectangleF area,
        double[] dist, double maxDensity, Color fillColor, Color outlineColor)
    {
        float cellWidth = area.Width / GridSize;

        using var fillBrush = new SolidBrush(fillColor);
        using var outlinePen = new Pen(outlineColor, 0.8f);

        for (int i = 0; i < GridSize; i++)
        {
            float x = area.Left + (float)(piGrid[i] * area.Width) - cellWidth / 2f;
            float height = (float)(dist[i] / Math.Max(maxDensity, 1e-12) * area.Height);
            float y = area.Bottom - height;

            if (height < 0.5f) continue;

            var rect = new RectangleF(x, y, cellWidth, height);
            g.FillRectangle(fillBrush, rect);
            g.DrawRectangle(outlinePen, rect.X, rect.Y, rect.Width, rect.Height);
        }
    }

    private void DrawChainWalker(Graphics g, RectangleF area)
    {
        int count = chainTrail.Count;
        int trailLen = Math.Min(count, 80); // show last 80 positions
        int start = count - trailLen;

        // Draw trail as connected dots, fading from transparent to solid
        for (int i = start; i < count - 1; i++)
        {
            float t = (float)(i - start) / trailLen; // 0..1
            int alpha = (int)(40 + 160 * t);
            float x0 = area.Left + (float)(chainTrail[i] * area.Width);
            float x1 = area.Left + (float)(chainTrail[i + 1] * area.Width);
            float y = area.Bottom + 14; // just below the x axis

            using var pen = new Pen(Color.FromArgb(alpha, 220, 60, 20), 1.2f);
            g.DrawLine(pen, x0, y, x1, y);

            // Small dot at each position
            float dotSize = 2f + 2f * t;
            using var brush = new SolidBrush(Color.FromArgb(alpha, 220, 60, 20));
            g.FillEllipse(brush, x0 - dotSize / 2, y - dotSize / 2, dotSize, dotSize);
        }

        // Current position: large dot + vertical line into the plot
        float cx = area.Left + (float)(chainPosition * area.Width);
        using var currentBrush = new SolidBrush(Color.FromArgb(240, 220, 40, 20));
        g.FillEllipse(currentBrush, cx - 5, area.Bottom + 9, 10, 10);

        using var vPen = new Pen(Color.FromArgb(100, 220, 40, 20), 1.5f);
        vPen.DashStyle = System.Drawing.Drawing2D.DashStyle.Dot;
        g.DrawLine(vPen, cx, area.Top, cx, area.Bottom);

        // Label with step count
        using var font = new Font("Segoe UI", 7.5f);
        using var lblBrush = new SolidBrush(Color.FromArgb(180, 60, 20));
        string label = $"step {chainTotalSteps}";
        var sz = g.MeasureString(label, font);
        float lx = Math.Clamp(cx - sz.Width / 2, area.Left, area.Right - sz.Width);
        g.DrawString(label, font, lblBrush, lx, area.Bottom + 24);
    }

    private void DrawTruePLine(Graphics g, RectangleF area)
    {
        double trueP = trackTrueP.Value / 100.0;
        float x = area.Left + (float)(trueP * area.Width);
        using var pen = new Pen(Color.FromArgb(180, 0, 160, 0), 2f);
        pen.DashStyle = System.Drawing.Drawing2D.DashStyle.Dash;
        g.DrawLine(pen, x, area.Top, x, area.Bottom);

        // Label
        using var font = new Font("Segoe UI", 8f);
        using var brush = new SolidBrush(Color.FromArgb(0, 130, 0));
        string label = $"true p = {trueP:F2}";
        var sz = g.MeasureString(label, font);
        float lx = Math.Clamp(x - sz.Width / 2, area.Left, area.Right - sz.Width);
        g.DrawString(label, font, brush, lx, area.Top - 2);
    }

    private void DrawLiveSketch(Graphics g, RectangleF area)
    {
        // Draw points being sketched in real-time
        if (drawnPoints.Count < 2) return;

        using var pen = new Pen(Color.FromArgb(200, 30, 100, 220), 2.5f);
        for (int i = 1; i < drawnPoints.Count; i++)
        {
            var p0 = drawnPoints[i - 1];
            var p1 = drawnPoints[i];

            // Only connect consecutive points (no large gaps from separate strokes)
            float dx = Math.Abs(p1.X - p0.X);
            if (dx > 0.15f) continue; // skip if gap is too large

            float x0 = area.Left + p0.X * area.Width;
            float y0 = area.Top + (1f - p0.Y) * area.Height;
            float x1 = area.Left + p1.X * area.Width;
            float y1 = area.Top + (1f - p1.Y) * area.Height;
            g.DrawLine(pen, x0, y0, x1, y1);
        }

        // Instruction text
        using var font = new Font("Segoe UI", 11f, FontStyle.Italic);
        using var brush = new SolidBrush(Color.FromArgb(120, 120, 120));
        g.DrawString("Draw your prior distribution, then click \"Done Drawing\"",
            font, brush, area.Left + 10, area.Top + 10);
    }
}
