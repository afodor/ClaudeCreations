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
        UpdateFlipLabel();
        canvas.Invalidate();
    }

    private void TrackTrueP_Scroll(object? sender, EventArgs e)
    {
        lblTruePValue.Text = (trackTrueP.Value / 100.0).ToString("F2");
    }

    private void BtnAutoFlip_Click(object? sender, EventArgs e)
    {
        if (autoFlipTimer.Enabled)
        {
            StopAutoFlip();
        }
        else if (hasPrior)
        {
            autoFlipTimer.Start();
            btnAutoFlip.Text = "Auto \u25A0"; // ■ stop symbol
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

        UpdatePosterior();
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

        // Determine max density for scaling
        double maxDensity = 0;
        for (int i = 0; i < GridSize; i++)
        {
            if (originalPrior[i] > maxDensity) maxDensity = originalPrior[i];
            if (posterior[i] > maxDensity) maxDensity = posterior[i];
        }
        if (maxDensity < 1e-12) maxDensity = 1e-12;

        // Draw prior as blue filled area
        DrawFilledDistribution(g, area, originalPrior, maxDensity,
            Color.FromArgb(60, 30, 100, 220), Color.FromArgb(180, 30, 100, 220));

        // Draw posterior as red line
        if (totalFlips > 0)
        {
            DrawDistributionLine(g, area, posterior, maxDensity,
                Color.FromArgb(220, 200, 30, 30), 2.5f);
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
