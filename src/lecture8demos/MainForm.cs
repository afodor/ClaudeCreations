namespace Lecture08Tools;

enum ToolMode { HyperGeometric, FisherExact, Poisson, MeanVariance }

partial class MainForm : Form
{
    private ToolMode currentTool = ToolMode.HyperGeometric;

    // Coordinate mapping
    private double xMin, xMax, yMin, yMax;
    private RectangleF plotArea; // pixel rect for plot area within canvas

    // Mean-Variance data (persists between paints)
    private double[]? mvMeans, mvVariances;
    private double mvDispersion;

    // Fisher data (persists between paints)
    private double[]? fisherProbs;
    private int fisherMinA, fisherMaxA, fisherObsA;
    private double fisherPValue;
    private bool[]? fisherExtreme;

    // Animation
    private System.Windows.Forms.Timer? animTimer;
    private bool animating;

    private readonly Random rng = new();

    public MainForm()
    {
        InitializeComponent();
        SwitchTool(ToolMode.HyperGeometric);
    }

    // ════════════════════════════════════════════════════════════
    //  TOOL SWITCHING
    // ════════════════════════════════════════════════════════════

    private void SwitchTool(ToolMode mode)
    {
        currentTool = mode;

        panelHyper.Visible = mode == ToolMode.HyperGeometric;
        panelFisher.Visible = mode == ToolMode.FisherExact;
        panelPoisson.Visible = mode == ToolMode.Poisson;
        panelMeanVar.Visible = mode == ToolMode.MeanVariance;

        menuHypergeometric.Checked = mode == ToolMode.HyperGeometric;
        menuFisher.Checked = mode == ToolMode.FisherExact;
        menuPoisson.Checked = mode == ToolMode.Poisson;
        menuMeanVar.Checked = mode == ToolMode.MeanVariance;

        string[] names = { "Hypergeometric vs Binomial", "Fisher's Exact Test",
                           "Poisson Approximation", "Mean-Variance Explorer" };
        statusLabel.Text = names[(int)mode];

        if (mode == ToolMode.Poisson)
            UpdatePoissonLabels();

        canvas.Invalidate();
    }

    // ════════════════════════════════════════════════════════════
    //  COORDINATE MAPPING
    // ════════════════════════════════════════════════════════════

    private const float MarginLeft = 60, MarginRight = 20, MarginTop = 40, MarginBottom = 50;

    private void SetupPlotArea(int canvasW, int canvasH)
    {
        plotArea = new RectangleF(
            MarginLeft, MarginTop,
            canvasW - MarginLeft - MarginRight,
            canvasH - MarginTop - MarginBottom);
    }

    private PointF DataToCanvas(double x, double y)
    {
        float px = plotArea.Left + (float)((x - xMin) / (xMax - xMin)) * plotArea.Width;
        float py = plotArea.Bottom - (float)((y - yMin) / (yMax - yMin)) * plotArea.Height;
        return new PointF(px, py);
    }

    private float DataToCanvasX(double x)
        => plotArea.Left + (float)((x - xMin) / (xMax - xMin)) * plotArea.Width;

    private float DataToCanvasY(double y)
        => plotArea.Bottom - (float)((y - yMin) / (yMax - yMin)) * plotArea.Height;

    // ════════════════════════════════════════════════════════════
    //  SHARED DRAWING HELPERS
    // ════════════════════════════════════════════════════════════

    private void DrawAxes(Graphics g, string xLabel, string yLabel, bool integerXTicks = true)
    {
        using var pen = new Pen(Color.Black, 1.5f);
        using var font = new Font("Segoe UI", 8f);
        using var brush = new SolidBrush(Color.Black);
        using var sf = new StringFormat { Alignment = StringAlignment.Center };

        // Axes
        g.DrawLine(pen, plotArea.Left, plotArea.Bottom, plotArea.Right, plotArea.Bottom);
        g.DrawLine(pen, plotArea.Left, plotArea.Top, plotArea.Left, plotArea.Bottom);

        // X ticks
        int nxTicks = Math.Min((int)(xMax - xMin) + 1, 25);
        double xStep = NiceStep(xMin, xMax, nxTicks);
        for (double v = Math.Ceiling(xMin / xStep) * xStep; v <= xMax; v += xStep)
        {
            float px = DataToCanvasX(v);
            g.DrawLine(Pens.Gray, px, plotArea.Bottom, px, plotArea.Bottom + 4);
            string lbl = integerXTicks ? ((int)Math.Round(v)).ToString() : v.ToString("G4");
            g.DrawString(lbl, font, brush, px, plotArea.Bottom + 5, sf);
        }

        // Y ticks
        double yStep = NiceStep(yMin, yMax, 8);
        for (double v = Math.Ceiling(yMin / yStep) * yStep; v <= yMax * 1.001; v += yStep)
        {
            float py = DataToCanvasY(v);
            g.DrawLine(Pens.LightGray, plotArea.Left, py, plotArea.Right, py);
            g.DrawLine(Pens.Gray, plotArea.Left - 4, py, plotArea.Left, py);
            using var sfr = new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center };
            g.DrawString(v.ToString("G3"), font, brush, plotArea.Left - 6, py, sfr);
        }

        // Axis labels
        using var labelFont = new Font("Segoe UI", 9f);
        g.DrawString(xLabel, labelFont, brush,
            plotArea.Left + plotArea.Width / 2, plotArea.Bottom + 30, sf);

        using var sfVert = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        var state = g.Save();
        g.TranslateTransform(14, plotArea.Top + plotArea.Height / 2);
        g.RotateTransform(-90);
        g.DrawString(yLabel, labelFont, brush, 0, 0, sfVert);
        g.Restore(state);
    }

    private void DrawTitle(Graphics g, string title)
    {
        using var font = new Font("Segoe UI Semibold", 11f);
        using var sf = new StringFormat { Alignment = StringAlignment.Center };
        g.DrawString(title, font, Brushes.Black,
            plotArea.Left + plotArea.Width / 2, 8, sf);
    }

    private static double NiceStep(double min, double max, int targetTicks)
    {
        double range = max - min;
        if (range <= 0) return 1;
        double rough = range / Math.Max(targetTicks, 1);
        double mag = Math.Pow(10, Math.Floor(Math.Log10(rough)));
        double norm = rough / mag;
        double nice = norm < 1.5 ? 1 : norm < 3 ? 2 : norm < 7 ? 5 : 10;
        return nice * mag;
    }

    // ════════════════════════════════════════════════════════════
    //  CANVAS PAINT DISPATCHER
    // ════════════════════════════════════════════════════════════

    private void Canvas_Paint(object? sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        SetupPlotArea(canvas.Width, canvas.Height);

        switch (currentTool)
        {
            case ToolMode.HyperGeometric: PaintHypergeometric(g); break;
            case ToolMode.FisherExact: PaintFisher(g); break;
            case ToolMode.Poisson: PaintPoisson(g); break;
            case ToolMode.MeanVariance: PaintMeanVariance(g); break;
        }
    }

    // ════════════════════════════════════════════════════════════
    //  TOOL 1: HYPERGEOMETRIC vs BINOMIAL
    // ════════════════════════════════════════════════════════════

    private void ClampHyperParams()
    {
        int N = (int)nudHyperN.Value;
        nudHyperK.Maximum = N;
        nudHyperDraw.Maximum = N;
        if (nudHyperK.Value > N) nudHyperK.Value = N;
        if (nudHyperDraw.Value > N) nudHyperDraw.Value = N;
    }

    private void PaintHypergeometric(Graphics g)
    {
        int N = (int)nudHyperN.Value;
        int K = (int)nudHyperK.Value;
        int n = (int)nudHyperDraw.Value;
        double p = (double)K / N;
        bool showBinom = chkBinomialApprox.Checked;

        // Compute PMFs
        var hyperPmf = new double[n + 1];
        var binomPmf = new double[n + 1];
        double maxP = 0;

        for (int k = 0; k <= n; k++)
        {
            hyperPmf[k] = HypergeometricPMF(k, N, K, n);
            if (showBinom) binomPmf[k] = BinomialPMF(k, n, p);
            double m = Math.Max(hyperPmf[k], showBinom ? binomPmf[k] : 0);
            if (m > maxP) maxP = m;
        }

        // Setup coordinates
        xMin = -0.5; xMax = n + 0.5;
        yMin = 0; yMax = maxP * 1.15;
        if (yMax <= 0) yMax = 1;

        DrawAxes(g, "k (successes)", "P(X = k)");
        DrawTitle(g, $"Hypergeometric(N={N}, K={K}, n={n}) vs Binomial(n={n}, p={p:F3})");

        float barW = Math.Max(plotArea.Width / (n + 1) * 0.35f, 2);
        float baseY = DataToCanvasY(0);

        for (int k = 0; k <= n; k++)
        {
            float cx = DataToCanvasX(k);

            // Hypergeometric: solid blue bars
            if (hyperPmf[k] > 0)
            {
                float top = DataToCanvasY(hyperPmf[k]);
                float left = showBinom ? cx - barW - 1 : cx - barW / 2;
                g.FillRectangle(Brushes.SteelBlue, left, top, barW, baseY - top);
                g.DrawRectangle(Pens.DarkBlue, left, top, barW, baseY - top);
            }

            // Binomial: red outlined bars
            if (showBinom && binomPmf[k] > 0)
            {
                float top = DataToCanvasY(binomPmf[k]);
                float left = cx + 1;
                using var pen = new Pen(Color.Crimson, 1.5f);
                using var br = new SolidBrush(Color.FromArgb(60, 220, 20, 60));
                g.FillRectangle(br, left, top, barW, baseY - top);
                g.DrawRectangle(pen, left, top, barW, baseY - top);
            }
        }

        // Legend
        DrawLegend(g, showBinom);

        // Info label
        double hyperMean = (double)n * K / N;
        double hyperVar = (double)n * K * (N - K) * (N - n) / (N * N * ((double)N - 1));
        double binomMean = n * p;
        double binomVar = n * p * (1 - p);

        lblHyperInfo.Text = $"p = K/N = {p:F4}\n\n" +
            $"Hyper E[X] = {hyperMean:F3}\n" +
            $"Hyper Var  = {hyperVar:F3}\n\n" +
            $"Binom E[X] = {binomMean:F3}\n" +
            $"Binom Var  = {binomVar:F3}";
    }

    private void DrawLegend(Graphics g, bool showBinom)
    {
        float lx = plotArea.Right - 170, ly = plotArea.Top + 5;
        using var font = new Font("Segoe UI", 8.5f);
        using var bg = new SolidBrush(Color.FromArgb(230, 255, 255, 255));
        float lh = showBinom ? 44 : 24;
        g.FillRectangle(bg, lx, ly, 165, lh);
        g.DrawRectangle(Pens.Gray, lx, ly, 165, lh);

        g.FillRectangle(Brushes.SteelBlue, lx + 5, ly + 6, 14, 10);
        g.DrawString("Hypergeometric", font, Brushes.Black, lx + 24, ly + 3);

        if (showBinom)
        {
            using var br = new SolidBrush(Color.FromArgb(60, 220, 20, 60));
            g.FillRectangle(br, lx + 5, ly + 26, 14, 10);
            g.DrawRectangle(Pens.Crimson, lx + 5, ly + 26, 14, 10);
            g.DrawString("Binomial approx", font, Brushes.Black, lx + 24, ly + 23);
        }
    }

    // ════════════════════════════════════════════════════════════
    //  TOOL 2: FISHER'S EXACT TEST
    // ════════════════════════════════════════════════════════════

    private void BtnFisherCompute_Click(object? sender, EventArgs e)
    {
        int a = (int)nudFisherA.Value;
        int b = (int)nudFisherB.Value;
        int c = (int)nudFisherC.Value;
        int d = (int)nudFisherD.Value;

        int R1 = a + b, R2 = c + d;
        int C1 = a + c, C2 = b + d;
        int N = R1 + R2;

        // Range of possible a values given fixed margins
        int aMin = Math.Max(0, C1 - R2);
        int aMax = Math.Min(R1, C1);

        fisherMinA = aMin;
        fisherMaxA = aMax;
        fisherObsA = a;

        // Compute probability of each table
        int count = aMax - aMin + 1;
        fisherProbs = new double[count];
        fisherExtreme = new bool[count];

        for (int i = 0; i < count; i++)
        {
            int ai = aMin + i;
            int bi = R1 - ai;
            int ci = C1 - ai;
            int di = R2 - ci;
            fisherProbs[i] = Math.Exp(
                LogChoose(R1, ai) + LogChoose(R2, ci) - LogChoose(N, C1));
        }

        double pObs = fisherProbs[a - aMin];

        // Determine p-value based on alternative
        fisherPValue = 0;
        if (rbFisherTwoSided.Checked)
        {
            for (int i = 0; i < count; i++)
            {
                if (fisherProbs[i] <= pObs + 1e-12)
                {
                    fisherPValue += fisherProbs[i];
                    fisherExtreme[i] = true;
                }
            }
        }
        else if (rbFisherLess.Checked)
        {
            for (int i = 0; i < count; i++)
            {
                int ai = aMin + i;
                if (ai <= a)
                {
                    fisherPValue += fisherProbs[i];
                    fisherExtreme[i] = true;
                }
            }
        }
        else // Greater
        {
            for (int i = 0; i < count; i++)
            {
                int ai = aMin + i;
                if (ai >= a)
                {
                    fisherPValue += fisherProbs[i];
                    fisherExtreme[i] = true;
                }
            }
        }

        if (fisherPValue > 1) fisherPValue = 1;

        double oddsRatio = (a * d == 0 || b * c == 0)
            ? double.PositiveInfinity
            : (double)(a * d) / (b * c);

        string interp = fisherPValue < 0.001 ? "Very strong evidence against H₀"
            : fisherPValue < 0.01 ? "Strong evidence against H₀"
            : fisherPValue < 0.05 ? "Moderate evidence against H₀"
            : "Insufficient evidence against H₀";

        lblFisherInfo.Text =
            $"Odds ratio = {oddsRatio:F3}\n" +
            $"p-value = {fisherPValue:F6}\n\n" +
            $"{interp}";

        canvas.Invalidate();
    }

    private void PaintFisher(Graphics g)
    {
        if (fisherProbs == null || fisherExtreme == null)
        {
            using var font = new Font("Segoe UI", 12f);
            g.DrawString("Click 'Compute' to run Fisher's Exact Test",
                font, Brushes.Gray, plotArea.Left + 40, plotArea.Top + 40);
            return;
        }

        int count = fisherProbs.Length;
        double maxP = 0;
        for (int i = 0; i < count; i++)
            if (fisherProbs[i] > maxP) maxP = fisherProbs[i];

        xMin = fisherMinA - 0.5;
        xMax = fisherMaxA + 0.5;
        yMin = 0;
        yMax = maxP * 1.15;
        if (yMax <= 0) yMax = 1;

        DrawAxes(g, $"a (cell value, {txtFisherRow1.Text} × {txtFisherCol1.Text})", "P(table | H₀)");
        DrawTitle(g, $"Fisher's Exact Test: p = {fisherPValue:F4}");

        float barW = Math.Max(plotArea.Width / (count + 1) * 0.7f, 4);
        float baseY = DataToCanvasY(0);

        for (int i = 0; i < count; i++)
        {
            int ai = fisherMinA + i;
            float cx = DataToCanvasX(ai);
            float top = DataToCanvasY(fisherProbs[i]);

            Brush br;
            Pen pen;
            if (ai == fisherObsA)
            {
                br = Brushes.Crimson;
                pen = Pens.DarkRed;
            }
            else if (fisherExtreme[i])
            {
                br = Brushes.Orange;
                pen = Pens.DarkOrange;
            }
            else
            {
                br = Brushes.LightGray;
                pen = Pens.Gray;
            }

            g.FillRectangle(br, cx - barW / 2, top, barW, baseY - top);
            g.DrawRectangle(pen, cx - barW / 2, top, barW, baseY - top);
        }

        // Alpha line
        using var dashPen = new Pen(Color.Green, 1.5f) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash };
        float alphaY = DataToCanvasY(0.05);
        if (alphaY > plotArea.Top && alphaY < plotArea.Bottom)
        {
            g.DrawLine(dashPen, plotArea.Left, alphaY, plotArea.Right, alphaY);
            using var sfont = new Font("Segoe UI", 8f);
            g.DrawString("α = 0.05", sfont, Brushes.Green, plotArea.Right - 60, alphaY - 16);
        }

        // Legend
        float lx = plotArea.Right - 160, ly = plotArea.Top + 5;
        using var lfont = new Font("Segoe UI", 8.5f);
        using var lbg = new SolidBrush(Color.FromArgb(230, 255, 255, 255));
        g.FillRectangle(lbg, lx, ly, 155, 64);
        g.DrawRectangle(Pens.Gray, lx, ly, 155, 64);
        g.FillRectangle(Brushes.Crimson, lx + 5, ly + 6, 14, 10);
        g.DrawString("Observed table", lfont, Brushes.Black, lx + 24, ly + 3);
        g.FillRectangle(Brushes.Orange, lx + 5, ly + 26, 14, 10);
        g.DrawString("Extreme tables", lfont, Brushes.Black, lx + 24, ly + 23);
        g.FillRectangle(Brushes.LightGray, lx + 5, ly + 46, 14, 10);
        g.DrawString("Other tables", lfont, Brushes.Black, lx + 24, ly + 43);
    }

    // ════════════════════════════════════════════════════════════
    //  TOOL 3: POISSON APPROXIMATION
    // ════════════════════════════════════════════════════════════

    private void UpdatePoissonLabels()
    {
        double lambda = trkLambda.Value / 10.0;
        int n = trkPoissonN.Value;
        double p = lambda / n;
        if (p > 1) p = 1;
        lblLambda.Text = $"λ = {lambda:F1}";
        lblPoissonN.Text = $"n = {n}, p = {p:F4}";
    }

    private void BtnAnimate_Click(object? sender, EventArgs e)
    {
        if (animating)
        {
            StopAnimation();
            return;
        }

        animating = true;
        btnAnimate.Text = "Stop";
        animTimer = new System.Windows.Forms.Timer { Interval = 50 };
        animTimer.Tick += (s2, e2) =>
        {
            int cur = trkPoissonN.Value;
            int step = Math.Max(1, cur / 20);
            int next = cur + step;
            if (next > trkPoissonN.Maximum)
            {
                StopAnimation();
                return;
            }
            trkPoissonN.Value = next;
        };
        animTimer.Start();
    }

    private void StopAnimation()
    {
        animating = false;
        btnAnimate.Text = "Animate n → ∞";
        animTimer?.Stop();
        animTimer?.Dispose();
        animTimer = null;
    }

    private void PaintPoisson(Graphics g)
    {
        double lambda = trkLambda.Value / 10.0;
        int n = trkPoissonN.Value;
        double p = lambda / n;
        if (p > 1) p = 1;

        bool showPois = chkShowPoisson.Checked;
        bool showBin = chkShowBinomial.Checked;

        // Auto-range x
        int kMax = Math.Max((int)(lambda + 4 * Math.Sqrt(lambda)) + 1, 10);
        kMax = Math.Min(kMax, n); // can't exceed n for binomial

        var poisPmf = new double[kMax + 1];
        var binPmf = new double[kMax + 1];
        double maxProb = 0;

        for (int k = 0; k <= kMax; k++)
        {
            if (showPois) poisPmf[k] = PoissonPMF(k, lambda);
            if (showBin) binPmf[k] = BinomialPMF(k, n, p);
            double m = Math.Max(showPois ? poisPmf[k] : 0, showBin ? binPmf[k] : 0);
            if (m > maxProb) maxProb = m;
        }

        xMin = -0.5; xMax = kMax + 0.5;
        yMin = 0; yMax = maxProb * 1.15;
        if (yMax <= 0) yMax = 1;

        DrawAxes(g, "k", "P(X = k)");
        DrawTitle(g, $"Poisson(λ={lambda:F1}) vs Binomial(n={n}, p={p:F4})");

        float barW = Math.Max(plotArea.Width / (kMax + 2) * 0.35f, 2);
        float baseY = DataToCanvasY(0);

        for (int k = 0; k <= kMax; k++)
        {
            float cx = DataToCanvasX(k);

            if (showPois && poisPmf[k] > 0)
            {
                float top = DataToCanvasY(poisPmf[k]);
                float left = (showPois && showBin) ? cx - barW - 1 : cx - barW / 2;
                g.FillRectangle(Brushes.SteelBlue, left, top, barW, baseY - top);
                g.DrawRectangle(Pens.DarkBlue, left, top, barW, baseY - top);
            }

            if (showBin && binPmf[k] > 0)
            {
                float top = DataToCanvasY(binPmf[k]);
                float left = (showPois && showBin) ? cx + 1 : cx - barW / 2;
                using var pen = new Pen(Color.Crimson, 1.5f);
                using var br = new SolidBrush(Color.FromArgb(60, 220, 20, 60));
                g.FillRectangle(br, left, top, barW, baseY - top);
                g.DrawRectangle(pen, left, top, barW, baseY - top);
            }
        }

        // Legend
        float lx = plotArea.Right - 150, ly = plotArea.Top + 5;
        using var font = new Font("Segoe UI", 8.5f);
        using var lbg = new SolidBrush(Color.FromArgb(230, 255, 255, 255));
        int entries = (showPois ? 1 : 0) + (showBin ? 1 : 0);
        float lh = entries * 20 + 4;
        g.FillRectangle(lbg, lx, ly, 145, lh);
        g.DrawRectangle(Pens.Gray, lx, ly, 145, lh);
        int row = 0;
        if (showPois)
        {
            g.FillRectangle(Brushes.SteelBlue, lx + 5, ly + 6 + row * 20, 14, 10);
            g.DrawString("Poisson", font, Brushes.Black, lx + 24, ly + 3 + row * 20);
            row++;
        }
        if (showBin)
        {
            using var br = new SolidBrush(Color.FromArgb(60, 220, 20, 60));
            g.FillRectangle(br, lx + 5, ly + 6 + row * 20, 14, 10);
            g.DrawRectangle(Pens.Crimson, lx + 5, ly + 6 + row * 20, 14, 10);
            g.DrawString("Binomial", font, Brushes.Black, lx + 24, ly + 3 + row * 20);
        }

        // KL divergence info
        if (showPois && showBin)
        {
            double kl = 0;
            for (int k = 0; k <= kMax; k++)
            {
                if (poisPmf[k] > 1e-15 && binPmf[k] > 1e-15)
                    kl += poisPmf[k] * Math.Log(poisPmf[k] / binPmf[k]);
            }
            lblPoissonInfo.Text = $"KL(Poisson || Binomial)\n= {kl:E3}";
        }
        else
        {
            lblPoissonInfo.Text = "";
        }
    }

    // ════════════════════════════════════════════════════════════
    //  TOOL 4: MEAN-VARIANCE EXPLORER
    // ════════════════════════════════════════════════════════════

    private void BtnSimulate_Click(object? sender, EventArgs e)
    {
        int nGenes = (int)nudGenes.Value;
        int nSamples = (int)nudSamples.Value;
        bool isNB = cmbDistribution.SelectedIndex == 1;
        double r = (double)nudDispersion.Value;
        double meanMin = (double)nudMeanMin.Value;
        double meanMax = (double)nudMeanMax.Value;
        if (meanMin > meanMax) { var t = meanMin; meanMin = meanMax; meanMax = t; }

        mvMeans = new double[nGenes];
        mvVariances = new double[nGenes];
        mvDispersion = r;

        for (int gene = 0; gene < nGenes; gene++)
        {
            // Random mean for this gene (log-uniform)
            double logMu = Math.Log(meanMin) + rng.NextDouble() * (Math.Log(meanMax) - Math.Log(meanMin));
            double mu = Math.Exp(logMu);

            double sum = 0, sumSq = 0;
            for (int s = 0; s < nSamples; s++)
            {
                int count;
                if (isNB)
                    count = NextNegBinomial(r, mu);
                else
                    count = NextPoisson(mu);
                sum += count;
                sumSq += (double)count * count;
            }

            double mean = sum / nSamples;
            double variance = (sumSq - sum * sum / nSamples) / (nSamples - 1);
            mvMeans[gene] = mean;
            mvVariances[gene] = variance;
        }

        lblMeanVarInfo.Text = $"Simulated {nGenes} genes\n× {nSamples} samples\n" +
            $"Distribution: {(isNB ? $"NegBin(r={r})" : "Poisson")}";

        canvas.Invalidate();
    }

    private void PaintMeanVariance(Graphics g)
    {
        if (mvMeans == null || mvVariances == null)
        {
            using var font = new Font("Segoe UI", 12f);
            g.DrawString("Click 'Simulate' to generate data",
                font, Brushes.Gray, plotArea.Left + 40, plotArea.Top + 40);
            return;
        }

        bool isNB = cmbDistribution.SelectedIndex == 1;

        // Find range (log scale)
        double minM = double.MaxValue, maxM = double.MinValue;
        double minV = double.MaxValue, maxV = double.MinValue;
        for (int i = 0; i < mvMeans.Length; i++)
        {
            if (mvMeans[i] > 0 && mvVariances[i] > 0)
            {
                double lm = Math.Log10(mvMeans[i]);
                double lv = Math.Log10(mvVariances[i]);
                if (lm < minM) minM = lm;
                if (lm > maxM) maxM = lm;
                if (lv < minV) minV = lv;
                if (lv > maxV) maxV = lv;
            }
        }

        double pad = 0.2;
        xMin = Math.Floor(Math.Min(minM, minV) - pad);
        xMax = Math.Ceiling(Math.Max(maxM, maxV) + pad);
        yMin = xMin; // same scale for both axes
        yMax = xMax;

        DrawAxesLogLog(g, "log₁₀(mean)", "log₁₀(variance)");
        DrawTitle(g, $"Mean vs Variance: {(isNB ? "Negative Binomial" : "Poisson")}");

        // Scatter points
        using var dotBrush = new SolidBrush(Color.FromArgb(120, 70, 130, 180));
        for (int i = 0; i < mvMeans.Length; i++)
        {
            if (mvMeans[i] > 0 && mvVariances[i] > 0)
            {
                var pt = DataToCanvas(Math.Log10(mvMeans[i]), Math.Log10(mvVariances[i]));
                g.FillEllipse(dotBrush, pt.X - 2.5f, pt.Y - 2.5f, 5, 5);
            }
        }

        // y = x line (Poisson: variance = mean → log(var) = log(mean))
        using var redPen = new Pen(Color.Red, 2f) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash };
        var p1 = DataToCanvas(xMin, xMin);
        var p2 = DataToCanvas(xMax, xMax);
        g.DrawLine(redPen, p1, p2);

        // Label the line
        using var lfont = new Font("Segoe UI", 8.5f);
        var lbl1Pt = DataToCanvas(xMax - 0.6, xMax - 0.3);
        g.DrawString("Var = Mean", lfont, Brushes.Red, lbl1Pt.X, lbl1Pt.Y);

        // NegBin expected curve: Var = μ + μ²/r → log10(Var) = log10(μ + μ²/r)
        if (isNB && mvDispersion > 0)
        {
            using var bluePen = new Pen(Color.Blue, 2f);
            var pts = new List<PointF>();
            for (double lm = xMin; lm <= xMax; lm += 0.02)
            {
                double mu = Math.Pow(10, lm);
                double v = mu + mu * mu / mvDispersion;
                double lv = Math.Log10(v);
                if (lv >= yMin && lv <= yMax)
                    pts.Add(DataToCanvas(lm, lv));
            }
            if (pts.Count > 1)
                g.DrawLines(bluePen, pts.ToArray());

            var lbl2Pt = DataToCanvas(xMax - 1.2, Math.Log10(Math.Pow(10, xMax - 1.2) + Math.Pow(10, 2 * (xMax - 1.2)) / mvDispersion) + 0.15);
            g.DrawString($"Var = μ + μ²/{mvDispersion:G3}", lfont, Brushes.Blue, lbl2Pt.X, lbl2Pt.Y);
        }
    }

    private void DrawAxesLogLog(Graphics g, string xLabel, string yLabel)
    {
        using var pen = new Pen(Color.Black, 1.5f);
        using var font = new Font("Segoe UI", 8f);
        using var brush = new SolidBrush(Color.Black);
        using var sf = new StringFormat { Alignment = StringAlignment.Center };

        g.DrawLine(pen, plotArea.Left, plotArea.Bottom, plotArea.Right, plotArea.Bottom);
        g.DrawLine(pen, plotArea.Left, plotArea.Top, plotArea.Left, plotArea.Bottom);

        // Integer log ticks
        for (int v = (int)Math.Ceiling(xMin); v <= (int)Math.Floor(xMax); v++)
        {
            float px = DataToCanvasX(v);
            g.DrawLine(Pens.LightGray, px, plotArea.Top, px, plotArea.Bottom);
            g.DrawLine(Pens.Gray, px, plotArea.Bottom, px, plotArea.Bottom + 4);
            g.DrawString(v.ToString(), font, brush, px, plotArea.Bottom + 5, sf);
        }

        for (int v = (int)Math.Ceiling(yMin); v <= (int)Math.Floor(yMax); v++)
        {
            float py = DataToCanvasY(v);
            g.DrawLine(Pens.LightGray, plotArea.Left, py, plotArea.Right, py);
            g.DrawLine(Pens.Gray, plotArea.Left - 4, py, plotArea.Left, py);
            using var sfr = new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center };
            g.DrawString(v.ToString(), font, brush, plotArea.Left - 6, py, sfr);
        }

        using var labelFont = new Font("Segoe UI", 9f);
        g.DrawString(xLabel, labelFont, brush,
            plotArea.Left + plotArea.Width / 2, plotArea.Bottom + 30, sf);

        using var sfVert = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        var state = g.Save();
        g.TranslateTransform(14, plotArea.Top + plotArea.Height / 2);
        g.RotateTransform(-90);
        g.DrawString(yLabel, labelFont, brush, 0, 0, sfVert);
        g.Restore(state);
    }

    // ════════════════════════════════════════════════════════════
    //  MATH FUNCTIONS
    // ════════════════════════════════════════════════════════════

    private static double LogGamma(double x)
    {
        // Lanczos approximation (g=7, n=9)
        double[] coef = {
            0.99999999999980993,
            676.5203681218851,
            -1259.1392167224028,
            771.32342877765313,
            -176.61502916214059,
            12.507343278686905,
            -0.13857109526572012,
            9.9843695780195716e-6,
            1.5056327351493116e-7
        };

        if (x < 0.5)
        {
            return Math.Log(Math.PI / Math.Sin(Math.PI * x)) - LogGamma(1 - x);
        }

        x -= 1;
        double a = coef[0];
        double t = x + 7.5;
        for (int i = 1; i < 9; i++)
            a += coef[i] / (x + i);

        return 0.5 * Math.Log(2 * Math.PI) + (x + 0.5) * Math.Log(t) - t + Math.Log(a);
    }

    private static double LogFactorial(double n)
        => LogGamma(n + 1);

    private static double LogChoose(double n, double k)
    {
        if (k < 0 || k > n) return double.NegativeInfinity;
        return LogFactorial(n) - LogFactorial(k) - LogFactorial(n - k);
    }

    private static double HypergeometricPMF(int k, int N, int K, int n)
    {
        if (k < Math.Max(0, n + K - N) || k > Math.Min(K, n))
            return 0;
        double logP = LogChoose(K, k) + LogChoose(N - K, n - k) - LogChoose(N, n);
        return Math.Exp(logP);
    }

    private static double BinomialPMF(int k, int n, double p)
    {
        if (k < 0 || k > n || p < 0 || p > 1) return 0;
        if (p == 0) return k == 0 ? 1 : 0;
        if (p == 1) return k == n ? 1 : 0;
        double logP = LogChoose(n, k) + k * Math.Log(p) + (n - k) * Math.Log(1 - p);
        return Math.Exp(logP);
    }

    private static double PoissonPMF(int k, double lambda)
    {
        if (k < 0 || lambda <= 0) return 0;
        double logP = k * Math.Log(lambda) - lambda - LogFactorial(k);
        return Math.Exp(logP);
    }

    // ── Random number generators ────────────────────────────────

    private double NextGamma(double shape)
    {
        // Marsaglia-Tsang method for shape >= 1
        if (shape < 1)
        {
            double u = rng.NextDouble();
            return NextGamma(shape + 1) * Math.Pow(u, 1.0 / shape);
        }

        double d = shape - 1.0 / 3.0;
        double c = 1.0 / Math.Sqrt(9.0 * d);
        while (true)
        {
            double x, v;
            do
            {
                x = NextNormal();
                v = 1 + c * x;
            } while (v <= 0);

            v = v * v * v;
            double u = rng.NextDouble();
            if (u < 1 - 0.0331 * x * x * x * x)
                return d * v;
            if (Math.Log(u) < 0.5 * x * x + d * (1 - v + Math.Log(v)))
                return d * v;
        }
    }

    private double NextNormal()
    {
        // Box-Muller
        double u1 = 1.0 - rng.NextDouble();
        double u2 = rng.NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
    }

    private int NextPoisson(double lambda)
    {
        if (lambda < 30)
        {
            // Knuth
            double L = Math.Exp(-lambda);
            int k = 0;
            double p = 1;
            do
            {
                k++;
                p *= rng.NextDouble();
            } while (p > L);
            return k - 1;
        }
        else
        {
            // Normal approximation rounded
            double x = lambda + Math.Sqrt(lambda) * NextNormal();
            return Math.Max(0, (int)Math.Round(x));
        }
    }

    private int NextNegBinomial(double r, double mu)
    {
        // Gamma-Poisson mixture
        double scale = mu / r;
        double lambda = NextGamma(r) * scale;
        return NextPoisson(lambda);
    }
}
