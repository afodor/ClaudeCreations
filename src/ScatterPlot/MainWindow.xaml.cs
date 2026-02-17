using System.ComponentModel;
using System.Data;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Microsoft.Win32;
using ScottPlot;

namespace ScatterPlotExplorer;

public partial class MainWindow : Window
{
    // ---------- data state ----------
    private DataTable? _data;
    private string[]? _columns;
    private string? _filePath;
    private char _fileDelimiter = '\t';
    private bool _dataModified; // true after rows deleted

    // plot positions (after mapping text→numeric, possibly log-transformed)
    private double[]? _plotX;
    private double[]? _plotY;
    private string[]? _xCategories;
    private string[]? _yCategories;

    // selection
    private HashSet<int> _selectedRows = new();
    private bool _isSelecting;
    private ScottPlot.Pixel _selectStart;
    private System.Windows.Point _dragStart;

    // hover
    private int _hoveredRow = -1;

    // highlight from DataGrid clicks
    private HashSet<int> _gridHighlightedRows = new();

    // highlight from legend hover
    private HashSet<int> _legendHighlightedRows = new();

    // category → row indices (built during UpdatePlot)
    private Dictionary<string, HashSet<int>> _colorCategoryRows = new();
    private Dictionary<string, HashSet<int>> _shapeCategoryRows = new();

    // drag mode for axis zoom
    private enum DragMode { None, BoxSelect, AxisXPan, AxisYPan, RightDragPan }
    private DragMode _dragMode;

    // full data extent (for clamping zoom-out)
    private double _dataXMin, _dataXMax, _dataYMin, _dataYMax;
    private System.Windows.Point _panStart;
    private AxisLimits _panStartLimits;

    // column filters
    private Dictionary<string, (double min, double max)> _numericFilters = new();
    private Dictionary<string, HashSet<string>> _categoricalFilters = new();
    private HashSet<int>? _filteredRows; // null = all visible

    // regression model
    private double[]? _regressionCoeffs;
    private string[]? _regressionPredictors;
    private string? _regressionResponse;
    private double _regressionRSquared;
    private double[]? _regressionSE;
    private double[]? _regressionPValues;
    private int _regressionN;
    private bool _regressionLogResponse;
    private bool _regressionLogPredictors;
    private int _regressionOrder = 1;
    private ScottPlot.Color _regLineColor = new ScottPlot.Color(200, 30, 30);
    private float _regLineWidth = 2;
    private bool _regLineDashed = false;

    // identity line (y = x)
    private bool _showIdentityLine;
    private ScottPlot.Color _identityLineColor = new ScottPlot.Color(140, 140, 140);
    private float _identityLineWidth = 2;
    private bool _identityLineDashed = true;

    // Cartesian axes (x=0, y=0 lines)
    private bool _showCartesianAxes;

    // label font properties
    private string _labelFontFamily = "Segoe UI";
    private float _labelFontSize = 10;
    private ScottPlot.Color _labelFontColor = ScottPlot.Colors.Black;

    // categorical colour overrides (category value → hex like "#FF7F0E")
    private Dictionary<string, string> _categoryColorOverrides = new();

    // per-point colour overrides (row index → colour)
    private Dictionary<int, ScottPlot.Color> _pointColorOverrides = new();
    // saved preset index for restoring after "Colour selected" override
    private int _savedPresetIndex = 0;

    // journal
    private string? _journalPath;

    // multi-window support
    private static readonly List<MainWindow> _allWindows = new();
    private bool _isLinkedChild;

    // suppress re-entrant updates
    private bool _updating;

    // preset colours
    private static readonly (string Name, string Hex)[] PresetColors =
    [
        ("Blue",      "#1F77B4"),
        ("Orange",    "#FF7F0E"),
        ("Green",     "#2CA02C"),
        ("Red",       "#D62728"),
        ("Purple",    "#9467BD"),
        ("Brown",     "#8C564B"),
        ("Pink",      "#E377C2"),
        ("Grey",      "#7F7F7F"),
        ("Yellow",    "#BCBD22"),
        ("Cyan",      "#17BECF"),
        ("Black",     "#000000"),
    ];

    // marker shapes for shape-by-column
    private static readonly (string Name, MarkerShape Shape)[] Shapes =
    [
        ("Circle",        MarkerShape.FilledCircle),
        ("Square",        MarkerShape.FilledSquare),
        ("Diamond",       MarkerShape.FilledDiamond),
        ("Triangle Up",   MarkerShape.FilledTriangleUp),
        ("Triangle Down", MarkerShape.FilledTriangleDown),
        ("Star",          MarkerShape.Asterisk),
        ("Cross",         MarkerShape.Cross),
        ("Eks",           MarkerShape.Eks),
    ];

    // Category10 colours for legend
    private static readonly string[] Cat10Hex =
    [
        "#1F77B4", "#FF7F0E", "#2CA02C", "#D62728", "#9467BD",
        "#8C564B", "#E377C2", "#7F7F7F", "#BCBD22", "#17BECF",
    ];

    // recognised missing-data tokens
    private static readonly HashSet<string> MissingValues = new(StringComparer.OrdinalIgnoreCase)
    { "", "NA", "N/A", "NaN", ".", "null", "-", "?" };

    // recent files
    private static readonly string RecentFilesPath = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ScatterPlotExplorer", "recent.txt");
    private List<string> _recentFiles = new();

    public MainWindow()
    {
        InitializeComponent();
        try
        {
            string icoPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app.ico");
            if (File.Exists(icoPath))
                Icon = new System.Windows.Media.Imaging.BitmapImage(new Uri(icoPath));
        }
        catch { }
        PopulateStaticCombos();
        SetupSelectionInput();
        LoadRecentFiles();
        BuildRecentMenu();
        KeyDown += Window_KeyDown;
        _allWindows.Add(this);
        Closed += (s, e) => _allWindows.Remove(this);
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.A && Keyboard.Modifiers == ModifierKeys.Control
            && _data != null && _plotX != null && _plotY != null)
        {
            _selectedRows.Clear();
            for (int i = 0; i < _plotX.Length; i++)
            {
                if (IsRowVisible(i) && !double.IsNaN(_plotX[i]) && !double.IsNaN(_plotY[i]))
                    _selectedRows.Add(i);
            }
            _gridHighlightedRows.Clear();
            UpdateSelectionDisplay();
            UpdatePlot(true);
            BroadcastSelection();
            e.Handled = true;
        }
        else if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control
            && _data != null && _selectedRows.Count > 0)
        {
            CopySelectedToClipboard();
            e.Handled = true;
        }
        else if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control && _data != null)
        {
            txtSearch.Focus();
            txtSearch.SelectAll();
            e.Handled = true;
        }
        else if (e.Key == Key.Delete && _data != null && _selectedRows.Count > 0)
        {
            var result = MessageBox.Show(
                $"Delete {_selectedRows.Count} selected point{(_selectedRows.Count == 1 ? "" : "s")} from in-memory data?",
                "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes) return;

            // Remove selected rows from in-memory data (not the file)
            _dataModified = true;
            foreach (int idx in _selectedRows.OrderByDescending(i => i))
            {
                if (idx < _data.Rows.Count)
                    _data.Rows.RemoveAt(idx);
            }
            _selectedRows.Clear();
            _gridHighlightedRows.Clear();
            _legendHighlightedRows.Clear();
            _pointColorOverrides.Clear(); // row indices shift after delete
            _hoveredRow = -1;
            ClearRegressionModel();
            BuildFilterPanel();
            UpdatePlot();
            UpdateSelectionDisplay();
            BroadcastDataChanged();
            e.Handled = true;
        }
    }

    // ====================================================================
    //  Init
    // ====================================================================
    private void PopulateStaticCombos()
    {
        foreach (var (name, _) in PresetColors)
            cboPresetColor.Items.Add(name);
        cboPresetColor.SelectedIndex = 0;

        foreach (var (name, _) in Shapes)
            cboShape.Items.Add(name);
        cboShape.SelectedIndex = 0;
    }

    // ====================================================================
    //  File loading
    // ====================================================================
    private void BtnOpen_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Filter = "Text files|*.csv;*.tsv;*.txt;*.dat|All files|*.*"
        };
        if (dlg.ShowDialog() == true)
            LoadFile(dlg.FileName);
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
            LoadFile(files[0]);
    }

    private void LoadFile(string path)
    {
        try
        {
            string[] lines;
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var sr = new StreamReader(fs))
                lines = sr.ReadToEnd().Split(["\r\n", "\n"], StringSplitOptions.None);

            if (lines.Length < 2) { MessageBox.Show("File has fewer than 2 lines."); return; }

            char delim = DetectDelimiter(lines);
            var dt = ParseToDataTable(lines, delim);
            if (dt.Columns.Count == 0) { MessageBox.Show("No columns detected."); return; }

            _data = dt;
            _columns = dt.Columns.Cast<DataColumn>().Select(c => c.ColumnName).ToArray();
            _filePath = path;
            _fileDelimiter = delim;
            _dataModified = false;

            txtFileName.Text = path;
            PopulateColumnCombos();
            BuildFilterPanel();
            panelControls.IsEnabled = true;
            btnClose.IsEnabled = true;
            menuClose.IsEnabled = true;
            menuCopyRCode.IsEnabled = true;
            menuJournalPublishNew.IsEnabled = true;
            menuJournalAppend.IsEnabled = true;
            AddToRecentFiles(path);
            _selectedRows.Clear();
            _gridHighlightedRows.Clear();
            _legendHighlightedRows.Clear();
            _categoryColorOverrides.Clear();
            _pointColorOverrides.Clear();
            statsPanel.Children.Clear();
            statsPanel.Children.Add(new TextBlock { Text = "(no model fitted)", FontSize = 11, Foreground = Brushes.Gray });
            AddIdentityLineControls();
            UpdatePlot();
            UpdateSelectionDisplay();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error loading file:\n{ex.Message}");
        }
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        _data = null;
        _columns = null;
        _filePath = null;
        _plotX = null;
        _plotY = null;
        _xCategories = null;
        _yCategories = null;
        _selectedRows.Clear();
        _gridHighlightedRows.Clear();
        _legendHighlightedRows.Clear();
        _hoveredRow = -1;
        _colorCategoryRows.Clear();
        _shapeCategoryRows.Clear();
        _numericFilters.Clear();
        _categoricalFilters.Clear();
        _filteredRows = null;
        filterPanel.Children.Clear();
        _regressionCoeffs = null;
        _regressionPredictors = null;
        _regressionResponse = null;
        _showIdentityLine = false;
        _categoryColorOverrides.Clear();
        _pointColorOverrides.Clear();
        statsPanel.Children.Clear();
        statsPanel.Children.Add(new TextBlock { Text = "(no model fitted)", FontSize = 11, Foreground = Brushes.Gray });
        AddIdentityLineControls();

        _updating = true;
        cboX.Items.Clear();
        cboY.Items.Clear();
        cboColor.Items.Clear();
        cboShapeCol.Items.Clear();
        cboSizeCol.Items.Clear();
        txtXMin.Text = ""; txtXMax.Text = "";
        txtYMin.Text = ""; txtYMax.Text = "";
        _updating = false;

        panelControls.IsEnabled = false;
        btnClose.IsEnabled = false;
        menuClose.IsEnabled = false;
        menuCopyRCode.IsEnabled = false;
        menuJournalPublishNew.IsEnabled = false;
        menuJournalAppend.IsEnabled = false;
        txtFileName.Text = "Drag & drop a delimited text file, or click Open";
        legendPanel.Children.Clear();
        dgSelected.ItemsSource = null;
        txtSelCount.Text = "";
        btnCopy.IsEnabled = false;
        HideTooltip();

        wpfPlot.Plot.Clear();
        wpfPlot.Refresh();
    }

    private void MenuExit_Click(object sender, RoutedEventArgs e) => Close();

    // ====================================================================
    //  Recent files
    // ====================================================================
    private void LoadRecentFiles()
    {
        _recentFiles.Clear();
        try
        {
            if (File.Exists(RecentFilesPath))
            {
                var lines = File.ReadAllLines(RecentFilesPath);
                _recentFiles = lines.Where(l => !string.IsNullOrWhiteSpace(l)).Take(10).ToList();
            }
        }
        catch { }
    }

    private void SaveRecentFiles()
    {
        try
        {
            var dir = System.IO.Path.GetDirectoryName(RecentFilesPath)!;
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            File.WriteAllLines(RecentFilesPath, _recentFiles);
        }
        catch { }
    }

    private void AddToRecentFiles(string path)
    {
        _recentFiles.RemoveAll(f => string.Equals(f, path, StringComparison.OrdinalIgnoreCase));
        _recentFiles.Insert(0, path);
        if (_recentFiles.Count > 10) _recentFiles.RemoveRange(10, _recentFiles.Count - 10);
        SaveRecentFiles();
        BuildRecentMenu();
    }

    private void BuildRecentMenu()
    {
        menuRecent.Items.Clear();
        if (_recentFiles.Count == 0)
        {
            menuRecent.Items.Add(new MenuItem { Header = "(none)", IsEnabled = false });
            return;
        }
        foreach (var path in _recentFiles)
        {
            var item = new MenuItem { Header = path, Tag = path };
            item.Click += RecentFile_Click;
            menuRecent.Items.Add(item);
        }
    }

    private void RecentFile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && mi.Tag is string path)
        {
            if (File.Exists(path))
                LoadFile(path);
            else
            {
                MessageBox.Show($"File not found:\n{path}");
                _recentFiles.Remove(path);
                SaveRecentFiles();
                BuildRecentMenu();
            }
        }
    }

    private static char DetectDelimiter(string[] lines)
    {
        char[] candidates = ['\t', ',', ';', ' '];
        int bestCount = 0;
        char best = ',';
        foreach (char d in candidates)
        {
            int first = lines[0].Split(d).Length;
            if (first < 2) continue;
            bool consistent = true;
            for (int i = 1; i < Math.Min(lines.Length, 6); i++)
                if (lines[i].Split(d).Length != first) { consistent = false; break; }
            if (consistent && first > bestCount) { bestCount = first; best = d; }
        }
        return best;
    }

    private static DataTable ParseToDataTable(string[] lines, char delim)
    {
        var dt = new DataTable();
        var headers = lines[0].Split(delim);
        foreach (var h in headers)
            dt.Columns.Add(h.Trim());

        for (int i = 1; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i])) continue;
            var vals = lines[i].Split(delim);
            var row = dt.NewRow();
            for (int c = 0; c < Math.Min(vals.Length, dt.Columns.Count); c++)
            {
                string val = vals[c].Trim();
                row[c] = MissingValues.Contains(val) ? "NA" : val;
            }
            dt.Rows.Add(row);
        }

        var colList = dt.Columns.Cast<DataColumn>().ToList();
        foreach (DataColumn col in colList)
        {
            bool allNumeric = true;
            bool hasAnyValue = false;
            foreach (DataRow row in dt.Rows)
            {
                string s = row[col].ToString() ?? "";
                if (s == "NA" || s == "") continue; // skip missing
                hasAnyValue = true;
                if (!double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out _))
                { allNumeric = false; break; }
            }
            if (allNumeric && hasAnyValue)
                col.ExtendedProperties["IsNumeric"] = true;
        }
        return dt;
    }

    private bool IsNumericColumn(string colName)
    {
        if (_data == null) return false;
        var col = _data.Columns[colName];
        return col?.ExtendedProperties.ContainsKey("IsNumeric") == true;
    }

    private void PopulateColumnCombos()
    {
        _updating = true;
        cboX.Items.Clear();
        cboY.Items.Clear();
        cboColor.Items.Clear();
        cboShapeCol.Items.Clear();
        cboSizeCol.Items.Clear();

        cboColor.Items.Add("(none)");
        cboShapeCol.Items.Add("(none)");
        cboLabelCol.Items.Clear();
        cboLabelCol.Items.Add("(none)");
        cboSizeCol.Items.Add("(none)");

        foreach (var c in _columns!)
        {
            cboX.Items.Add(c);
            cboY.Items.Add(c);
            cboColor.Items.Add(c);
            cboShapeCol.Items.Add(c);
            cboLabelCol.Items.Add(c);
            cboSizeCol.Items.Add(c);
        }

        // Default: first numeric column for X, second numeric for Y
        int firstNum = -1, secondNum = -1;
        for (int i = 0; i < _columns.Length; i++)
        {
            if (IsNumericColumn(_columns[i]))
            {
                if (firstNum < 0) firstNum = i;
                else if (secondNum < 0) { secondNum = i; break; }
            }
        }
        cboX.SelectedIndex = firstNum >= 0 ? firstNum : 0;
        cboY.SelectedIndex = secondNum >= 0 ? secondNum : (firstNum >= 0 ? firstNum : (_columns.Length >= 2 ? 1 : 0));
        cboColor.SelectedIndex = 0;
        cboShapeCol.SelectedIndex = 0;
        cboLabelCol.SelectedIndex = 0;
        cboSizeCol.SelectedIndex = 0;
        _updating = false;

        UpdateAxisRangeBoxes();
    }

    // ====================================================================
    //  Plot rendering
    // ====================================================================
    private void UpdatePlot(bool preserveView = false)
    {
        if (_data == null || _columns == null || cboX.SelectedItem == null || cboY.SelectedItem == null)
            return;

        // Save current view if we need to preserve it (e.g. during hover/selection)
        AxisLimits? savedLimits = preserveView ? wpfPlot.Plot.Axes.GetLimits() : null;

        var plot = wpfPlot.Plot;
        plot.Clear();

        string xCol = cboX.SelectedItem.ToString()!;
        string yCol = cboY.SelectedItem.ToString()!;
        bool xNumeric = IsNumericColumn(xCol);
        bool yNumeric = IsNumericColumn(yCol);

        int n = _data.Rows.Count;
        _plotX = new double[n];
        _plotY = new double[n];
        _xCategories = null;
        _yCategories = null;

        var rng = new Random(42);

        // --- X values ---
        if (xNumeric)
        {
            for (int i = 0; i < n; i++)
            {
                string s = _data.Rows[i][xCol].ToString()!;
                _plotX[i] = double.TryParse(s, NumberStyles.Any,
                    CultureInfo.InvariantCulture, out double v) ? v : double.NaN;
            }
        }
        else
        {
            var cats = _data.Rows.Cast<DataRow>().Select(r => r[xCol].ToString()!).Distinct().ToArray();
            _xCategories = cats;
            var map = new Dictionary<string, int>();
            for (int c = 0; c < cats.Length; c++) map[cats[c]] = c + 1;
            for (int i = 0; i < n; i++)
                _plotX[i] = map[_data.Rows[i][xCol].ToString()!] + (rng.NextDouble() - 0.5) * 0.3;
        }

        // --- Y values ---
        if (yNumeric)
        {
            for (int i = 0; i < n; i++)
            {
                string s = _data.Rows[i][yCol].ToString()!;
                _plotY[i] = double.TryParse(s, NumberStyles.Any,
                    CultureInfo.InvariantCulture, out double v) ? v : double.NaN;
            }
        }
        else
        {
            var cats = _data.Rows.Cast<DataRow>().Select(r => r[yCol].ToString()!).Distinct().ToArray();
            _yCategories = cats;
            var map = new Dictionary<string, int>();
            for (int c = 0; c < cats.Length; c++) map[cats[c]] = c + 1;
            for (int i = 0; i < n; i++)
                _plotY[i] = map[_data.Rows[i][yCol].ToString()!] + (rng.NextDouble() - 0.5) * 0.3;
        }

        // --- Log10 transform ---
        bool logX = chkLogX.IsChecked == true && xNumeric;
        bool logY = chkLogY.IsChecked == true && yNumeric;
        if (logX)
            for (int i = 0; i < n; i++)
                _plotX[i] = _plotX[i] > 0 ? Math.Log10(_plotX[i]) : double.NaN;
        if (logY)
            for (int i = 0; i < n; i++)
                _plotY[i] = _plotY[i] > 0 ? Math.Log10(_plotY[i]) : double.NaN;

        // --- Compute full data extent (for zoom clamping) ---
        {
            var validX = _plotX.Where(v => !double.IsNaN(v)).ToArray();
            var validY = _plotY.Where(v => !double.IsNaN(v)).ToArray();
            if (validX.Length > 0)
            {
                double pad = (validX.Max() - validX.Min()) * 0.05;
                if (pad == 0) pad = 1;
                _dataXMin = validX.Min() - pad;
                _dataXMax = validX.Max() + pad;
            }
            if (validY.Length > 0)
            {
                double pad = (validY.Max() - validY.Min()) * 0.05;
                if (pad == 0) pad = 1;
                _dataYMin = validY.Min() - pad;
                _dataYMax = validY.Max() + pad;
            }
        }

        // --- Per-point properties ---
        string? colorCol = cboColor.SelectedItem?.ToString();
        bool useColorCol = colorCol != null && colorCol != "(none)" && _data.Columns.Contains(colorCol);

        string? shapeCol = cboShapeCol.SelectedItem?.ToString();
        bool useShapeCol = shapeCol != null && shapeCol != "(none)" && _data.Columns.Contains(shapeCol);

        string? sizeCol = cboSizeCol.SelectedItem?.ToString();
        bool useSizeCol = sizeCol != null && sizeCol != "(none)" && _data.Columns.Contains(sizeCol);

        float baseSize = (float)sliderSize.Value;
        var baseShape = Shapes[Math.Max(0, cboShape.SelectedIndex)].Shape;
        ScottPlot.Color baseColor = GetSelectedColor();

        // Build per-point colour + category row maps
        var palette = new ScottPlot.Palettes.Category10();
        string[] colorCategories = [];
        ScottPlot.Color[] pointColors = new ScottPlot.Color[n];
        var catColorMap = new Dictionary<string, ScottPlot.Color>();
        _colorCategoryRows.Clear();

        if (useColorCol && !IsNumericColumn(colorCol!))
        {
            colorCategories = _data.Rows.Cast<DataRow>().Select(r => r[colorCol!].ToString()!).Distinct().ToArray();

            // Build colour map: overrides first, then palette skipping override colours
            var usedPaletteHexes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var cat in colorCategories)
            {
                if (_categoryColorOverrides.TryGetValue(cat, out string? hex))
                    usedPaletteHexes.Add(hex);
            }
            int palIdx = 0;
            foreach (var cat in colorCategories)
            {
                _colorCategoryRows[cat] = new HashSet<int>();
                if (_categoryColorOverrides.TryGetValue(cat, out string? hex))
                {
                    catColorMap[cat] = ParseHexColor(hex);
                }
                else
                {
                    // Skip palette indices whose hex matches an override colour
                    while (palIdx < 1000)
                    {
                        var pc = palette.GetColor(palIdx);
                        string pcHex = $"#{pc.Red:X2}{pc.Green:X2}{pc.Blue:X2}";
                        palIdx++;
                        if (!usedPaletteHexes.Contains(pcHex))
                        {
                            catColorMap[cat] = pc;
                            break;
                        }
                    }
                    if (!catColorMap.ContainsKey(cat))
                        catColorMap[cat] = palette.GetColor(0);
                }
            }
            for (int i = 0; i < n; i++)
            {
                string catVal = _data.Rows[i][colorCol!].ToString()!;
                pointColors[i] = catColorMap[catVal];
                _colorCategoryRows[catVal].Add(i);
            }
        }
        else if (useColorCol && IsNumericColumn(colorCol!))
        {
            bool logColor = chkLogColor.IsChecked == true;
            double cmin = double.MaxValue, cmax = double.MinValue;
            double[] cvals = new double[n];
            for (int i = 0; i < n; i++)
            {
                string s = _data.Rows[i][colorCol!].ToString()!;
                if (double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out double v))
                {
                    if (logColor) v = v > 0 ? Math.Log10(v) : double.NaN;
                    cvals[i] = v;
                    if (!double.IsNaN(v)) { if (v < cmin) cmin = v; if (v > cmax) cmax = v; }
                }
                else cvals[i] = double.NaN;
            }
            bool reverseColor = chkReverseColor.IsChecked == true;
            double crange = cmax - cmin; if (crange == 0) crange = 1;
            for (int i = 0; i < n; i++)
            {
                if (double.IsNaN(cvals[i]))
                { pointColors[i] = new ScottPlot.Color(180, 180, 180); continue; }
                double t = (cvals[i] - cmin) / crange;
                if (reverseColor) t = 1 - t;
                byte r = (byte)(255 * t);
                byte g = (byte)(60 * (1 - Math.Abs(2 * t - 1)));
                byte b = (byte)(255 * (1 - t));
                pointColors[i] = new ScottPlot.Color(r, g, b);
            }
        }
        else
        {
            for (int i = 0; i < n; i++) pointColors[i] = baseColor;
        }

        // Apply per-point colour overrides (from "Colour selected")
        if (_pointColorOverrides.Count > 0)
        {
            foreach (var (i, c) in _pointColorOverrides)
                if (i >= 0 && i < n) pointColors[i] = c;
        }

        // Build per-point shape + category row maps
        string[] shapeCategories = [];
        MarkerShape[] pointShapes = new MarkerShape[n];
        _shapeCategoryRows.Clear();

        if (useShapeCol)
        {
            shapeCategories = _data.Rows.Cast<DataRow>().Select(r => r[shapeCol!].ToString()!).Distinct().ToArray();
            var smap = new Dictionary<string, int>();
            for (int i = 0; i < shapeCategories.Length; i++)
            {
                smap[shapeCategories[i]] = i;
                _shapeCategoryRows[shapeCategories[i]] = new HashSet<int>();
            }
            for (int i = 0; i < n; i++)
            {
                string catVal = _data.Rows[i][shapeCol!].ToString()!;
                pointShapes[i] = Shapes[smap[catVal] % Shapes.Length].Shape;
                _shapeCategoryRows[catVal].Add(i);
            }
        }
        else
        {
            for (int i = 0; i < n; i++) pointShapes[i] = baseShape;
        }

        // Build per-point size
        float[] pointSizes = new float[n];
        if (useSizeCol && IsNumericColumn(sizeCol!))
        {
            bool logSize = chkLogSize.IsChecked == true;
            double smin = double.MaxValue, smax = double.MinValue;
            double[] svals = new double[n];
            for (int i = 0; i < n; i++)
            {
                string s = _data.Rows[i][sizeCol!].ToString()!;
                if (double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out double v))
                {
                    if (logSize) v = v > 0 ? Math.Log10(v) : double.NaN;
                    svals[i] = v;
                    if (!double.IsNaN(v)) { if (v < smin) smin = v; if (v > smax) smax = v; }
                }
                else svals[i] = double.NaN;
            }
            bool reverseSize = chkReverseSize.IsChecked == true;
            double srange = smax - smin; if (srange == 0) srange = 1;
            for (int i = 0; i < n; i++)
            {
                if (double.IsNaN(svals[i])) { pointSizes[i] = baseSize; continue; }
                double t = (svals[i] - smin) / srange;
                if (reverseSize) t = 1 - t;
                pointSizes[i] = 3f + (float)(t * 25);
            }
        }
        else if (useSizeCol)
        {
            // Categorical size: assign distinct sizes per category
            var sizeCats = _data.Rows.Cast<DataRow>().Select(r => r[sizeCol!].ToString()!).Distinct().ToArray();
            var sizeMap = new Dictionary<string, float>();
            for (int i = 0; i < sizeCats.Length; i++)
                sizeMap[sizeCats[i]] = 4f + (float)i / Math.Max(1, sizeCats.Length - 1) * 24f;
            for (int i = 0; i < n; i++)
                pointSizes[i] = sizeMap[_data.Rows[i][sizeCol!].ToString()!];
        }
        else
        {
            for (int i = 0; i < n; i++) pointSizes[i] = baseSize;
        }

        // --- Render points grouped by (color, shape, size) or individually ---
        bool needsPerPoint = useSizeCol || (useColorCol && IsNumericColumn(colorCol!)) || _pointColorOverrides.Count > 0; // per-point when size, numeric colour, or point overrides vary
        if (needsPerPoint)
        {
            for (int i = 0; i < n; i++)
            {
                if (!IsRowVisible(i)) continue;
                if (double.IsNaN(_plotX[i]) || double.IsNaN(_plotY[i])) continue;
                var sp = plot.Add.Scatter(new double[] { _plotX[i] }, new double[] { _plotY[i] });
                sp.LineWidth = 0;
                sp.MarkerSize = pointSizes[i];
                sp.MarkerShape = pointShapes[i];
                sp.Color = pointColors[i];
            }
        }
        else
        {
            // Group by (color index, shape index) for efficiency
            var groups = new Dictionary<(int ci, int si), (List<double> xs, List<double> ys)>();
            for (int i = 0; i < n; i++)
            {
                if (!IsRowVisible(i)) continue;
                if (double.IsNaN(_plotX[i]) || double.IsNaN(_plotY[i])) continue;
                int ci = useColorCol ? Array.IndexOf(colorCategories.Length > 0 ? colorCategories : [baseColor.ToString()], _data.Rows[i][colorCol ?? ""].ToString()) : 0;
                if (ci < 0) ci = 0;
                int si = useShapeCol ? Array.IndexOf(shapeCategories, _data.Rows[i][shapeCol ?? ""].ToString()) : 0;
                if (si < 0) si = 0;
                var key = (ci, si);
                if (!groups.ContainsKey(key))
                    groups[key] = (new List<double>(), new List<double>());
                groups[key].xs.Add(_plotX[i]);
                groups[key].ys.Add(_plotY[i]);
            }
            foreach (var ((ci, si), (xs, ys)) in groups)
            {
                var sp = plot.Add.Scatter(xs.ToArray(), ys.ToArray());
                sp.LineWidth = 0;
                sp.MarkerSize = baseSize;
                sp.MarkerShape = useShapeCol && si < Shapes.Length ? Shapes[si % Shapes.Length].Shape : baseShape;
                sp.Color = useColorCol ? (ci < colorCategories.Length && catColorMap.ContainsKey(colorCategories[ci]) ? catColorMap[colorCategories[ci]] : ci < colorCategories.Length ? palette.GetColor(ci) : baseColor) : baseColor;
            }
        }

        // --- Draw point labels (selected points only) ---
        string? labelCol = cboLabelCol.SelectedItem?.ToString();
        bool useLabelCol = labelCol != null && labelCol != "(none)" && _data.Columns.Contains(labelCol);
        if (useLabelCol && _selectedRows.Count > 0)
        {
            for (int i = 0; i < n; i++)
            {
                if (!_selectedRows.Contains(i)) continue;
                if (!IsRowVisible(i)) continue;
                if (double.IsNaN(_plotX[i]) || double.IsNaN(_plotY[i])) continue;
                string label = _data.Rows[i][labelCol!]?.ToString() ?? "";
                if (string.IsNullOrEmpty(label) || label == "NA") continue;
                var txt = plot.Add.Text(label, _plotX[i], _plotY[i]);
                txt.LabelFontSize = _labelFontSize;
                txt.LabelFontColor = _labelFontColor;
                txt.LabelFontName = _labelFontFamily;
                txt.LabelOffsetX = pointSizes[i] / 2 + 3;
                txt.LabelOffsetY = -pointSizes[i] / 2;
                txt.LabelBorderWidth = 0;
                txt.LabelBackgroundColor = ScottPlot.Colors.Transparent;
            }
        }

        // --- Draw regression line/curve (simple only, when axes match) ---
        if (_regressionCoeffs != null && _regressionPredictors?.Length == 1 && _regressionResponse != null)
        {
            string curXCol = cboX.SelectedItem.ToString()!;
            string curYCol = cboY.SelectedItem.ToString()!;
            if (_regressionPredictors[0] == curXCol && _regressionResponse == curYCol
                && xNumeric && yNumeric)
            {
                var rawXVals = new List<double>();
                for (int i = 0; i < n; i++)
                {
                    if (double.TryParse(_data.Rows[i][curXCol].ToString(), NumberStyles.Any,
                        CultureInfo.InvariantCulture, out double v))
                        rawXVals.Add(v);
                }
                if (rawXVals.Count >= 2)
                {
                    double rxMin = rawXVals.Min(), rxMax = rawXVals.Max();
                    var lx = new List<double>();
                    var ly = new List<double>();
                    for (int pi = 0; pi < 200; pi++)
                    {
                        double rx = rxMin + (rxMax - rxMin) * pi / 199.0;
                        double tx = _regressionLogPredictors ? (rx > 0 ? Math.Log10(rx) : double.NaN) : rx;
                        if (double.IsNaN(tx)) continue;
                        // Evaluate polynomial: b0 + b1*tx + b2*tx² + ...
                        double ty = _regressionCoeffs[0];
                        double txPow = 1.0;
                        for (int ci = 1; ci < _regressionCoeffs.Length; ci++)
                        {
                            txPow *= tx;
                            ty += _regressionCoeffs[ci] * txPow;
                        }
                        double ry = _regressionLogResponse ? Math.Pow(10, ty) : ty;
                        double px = logX ? (rx > 0 ? Math.Log10(rx) : double.NaN) : rx;
                        double py = logY ? (ry > 0 ? Math.Log10(ry) : double.NaN) : ry;
                        if (!double.IsNaN(px) && !double.IsNaN(py)) { lx.Add(px); ly.Add(py); }
                    }
                    if (lx.Count >= 2)
                    {
                        var line = plot.Add.Scatter(lx.ToArray(), ly.ToArray());
                        line.MarkerSize = 0;
                        line.LineWidth = _regLineWidth;
                        line.Color = _regLineColor;
                        line.LinePattern = _regLineDashed ? ScottPlot.LinePattern.Dashed : ScottPlot.LinePattern.Solid;
                    }
                }
            }
        }

        // --- Draw identity line (y = x) ---
        if (_showIdentityLine && xNumeric && yNumeric && _plotX != null && _plotY != null)
        {
            // _dataXMin/Max and _dataYMin/Max are already in plot space (log-transformed + padded)
            // Identity line: y = x in plot coordinates
            double idLo = Math.Max(_dataXMin, _dataYMin);
            double idHi = Math.Min(_dataXMax, _dataYMax);
            if (idLo < idHi)
            {
                var idLine = plot.Add.Scatter(new[] { idLo, idHi }, new[] { idLo, idHi });
                idLine.MarkerSize = 0;
                idLine.LineWidth = _identityLineWidth;
                idLine.Color = _identityLineColor;
                idLine.LinePattern = _identityLineDashed ? ScottPlot.LinePattern.Dashed : ScottPlot.LinePattern.Solid;
            }
        }

        // --- Draw Cartesian axes (x=0, y=0 lines) ---
        if (_showCartesianAxes && xNumeric && yNumeric)
        {
            var axisColor = new ScottPlot.Color(100, 100, 100);
            // y=0 horizontal line (if 0 is within Y data range)
            if (_dataYMin <= 0 && _dataYMax >= 0)
            {
                double y0 = logY ? 0 : 0; // log10(1)=0, so y=0 in log space means original value 1
                if (!logY || (_dataYMin <= 0 && _dataYMax >= 0))
                {
                    var hLine = plot.Add.Scatter(new[] { _dataXMin, _dataXMax }, new[] { y0, y0 });
                    hLine.MarkerSize = 0;
                    hLine.LineWidth = 1;
                    hLine.Color = axisColor;
                    hLine.LinePattern = ScottPlot.LinePattern.DenselyDashed;
                }
            }
            // x=0 vertical line (if 0 is within X data range)
            if (_dataXMin <= 0 && _dataXMax >= 0)
            {
                double x0 = 0;
                var vLine = plot.Add.Scatter(new[] { x0, x0 }, new[] { _dataYMin, _dataYMax });
                vLine.MarkerSize = 0;
                vLine.LineWidth = 1;
                vLine.Color = axisColor;
                vLine.LinePattern = ScottPlot.LinePattern.DenselyDashed;
            }
        }

        // --- Highlight selected points (from box-select) ---
        DrawHighlightRings(_selectedRows, ScottPlot.Colors.Red, baseSize + 6);

        // --- Highlight DataGrid-clicked points ---
        // Points that are both selected AND highlighted from the DataGrid get a bright extra ring
        if (_gridHighlightedRows.Count > 0 && _selectedRows.Count > 0)
        {
            var both = new HashSet<int>(_gridHighlightedRows);
            both.IntersectWith(_selectedRows);
            if (both.Count > 0)
            {
                DrawHighlightRings(both, ScottPlot.Colors.Orange, baseSize + 12);
                DrawHighlightRings(both, ScottPlot.Colors.Yellow, baseSize + 8);
            }
            var gridOnly = new HashSet<int>(_gridHighlightedRows);
            gridOnly.ExceptWith(_selectedRows);
            DrawHighlightRings(gridOnly, ScottPlot.Colors.Orange, baseSize + 8);
        }
        else
            DrawHighlightRings(_gridHighlightedRows, ScottPlot.Colors.Orange, baseSize + 8);

        // --- Highlight legend-hovered points ---
        DrawHighlightRings(_legendHighlightedRows, ScottPlot.Colors.Cyan, baseSize + 8);

        // --- Highlight hovered point ---
        if (_hoveredRow >= 0 && _hoveredRow < n && _plotX != null &&
            !double.IsNaN(_plotX[_hoveredRow]) && !double.IsNaN(_plotY[_hoveredRow]))
        {
            var h = plot.Add.Scatter(
                new double[] { _plotX[_hoveredRow] }, new double[] { _plotY[_hoveredRow] });
            h.LineWidth = 0;
            h.MarkerSize = baseSize + 10;
            h.MarkerShape = MarkerShape.OpenCircle;
            h.Color = ScottPlot.Colors.Magenta;
        }

        // --- Axis labels & ticks ---
        plot.Axes.Bottom.Label.Text = logX ? $"{xCol} (log10)" : xCol;
        plot.Axes.Left.Label.Text = logY ? $"{yCol} (log10)" : yCol;

        if (_xCategories != null)
        {
            var ticks = _xCategories.Select((c, i) => new ScottPlot.Tick(i + 1, c)).ToList();
            plot.Axes.Bottom.TickGenerator = new ScottPlot.TickGenerators.NumericManual(ticks.ToArray());
            plot.Axes.SetLimitsX(0.3, _xCategories.Length + 0.7);
        }
        else if (logX)
            plot.Axes.Bottom.TickGenerator = new ScottPlot.TickGenerators.NumericManual(GenerateLog10Ticks(_plotX).ToArray());
        else
            plot.Axes.Bottom.TickGenerator = new ScottPlot.TickGenerators.NumericAutomatic();

        if (_yCategories != null)
        {
            var ticks = _yCategories.Select((c, i) => new ScottPlot.Tick(i + 1, c)).ToList();
            plot.Axes.Left.TickGenerator = new ScottPlot.TickGenerators.NumericManual(ticks.ToArray());
            plot.Axes.SetLimitsY(0.3, _yCategories.Length + 0.7);
        }
        else if (logY)
            plot.Axes.Left.TickGenerator = new ScottPlot.TickGenerators.NumericManual(GenerateLog10Ticks(_plotY).ToArray());
        else
            plot.Axes.Left.TickGenerator = new ScottPlot.TickGenerators.NumericAutomatic();

        if (savedLimits.HasValue)
            plot.Axes.SetLimits(savedLimits.Value);
        else
            ApplyAxisRange();

        // No plot legend — we build our own in the sidebar
        plot.HideLegend();
        wpfPlot.Refresh();

        // Build sidebar legend
        BuildLegend(colorCategories, shapeCategories, useColorCol, useShapeCol);
    }

    private void DrawHighlightRings(HashSet<int> rows, ScottPlot.Color color, float size)
    {
        if (rows.Count == 0 || _plotX == null || _plotY == null) return;
        var xs = new List<double>();
        var ys = new List<double>();
        foreach (int i in rows)
        {
            if (i < 0 || i >= _plotX.Length) continue;
            if (double.IsNaN(_plotX[i]) || double.IsNaN(_plotY[i])) continue;
            xs.Add(_plotX[i]);
            ys.Add(_plotY[i]);
        }
        if (xs.Count == 0) return;
        var sp = wpfPlot.Plot.Add.Scatter(xs.ToArray(), ys.ToArray());
        sp.LineWidth = 0;
        sp.MarkerSize = size;
        sp.MarkerShape = MarkerShape.OpenCircle;
        sp.Color = color;
    }

    // ====================================================================
    //  Legend in sidebar (interactive)
    // ====================================================================
    private void BuildLegend(string[] colorCats, string[] shapeCats, bool useColor, bool useShape)
    {
        legendPanel.Children.Clear();

        if (useColor && colorCats.Length > 0)
        {
            // Rebuild same colour map as UpdatePlot (overrides + skip logic)
            var palette = new ScottPlot.Palettes.Category10();
            var usedHexes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var cat in colorCats)
                if (_categoryColorOverrides.TryGetValue(cat, out string? h)) usedHexes.Add(h);
            int palIdx = 0;
            var legendColors = new Dictionary<string, ScottPlot.Color>();
            foreach (var cat in colorCats)
            {
                if (_categoryColorOverrides.TryGetValue(cat, out string? hex))
                {
                    legendColors[cat] = ParseHexColor(hex);
                }
                else
                {
                    while (palIdx < 1000)
                    {
                        var pc = palette.GetColor(palIdx);
                        string pcHex = $"#{pc.Red:X2}{pc.Green:X2}{pc.Blue:X2}";
                        palIdx++;
                        if (!usedHexes.Contains(pcHex)) { legendColors[cat] = pc; break; }
                    }
                    if (!legendColors.ContainsKey(cat)) legendColors[cat] = palette.GetColor(0);
                }
            }

            for (int i = 0; i < colorCats.Length; i++)
            {
                string catValue = colorCats[i];
                var sp = new StackPanel
                {
                    Orientation = System.Windows.Controls.Orientation.Horizontal,
                    Margin = new Thickness(0, 1, 0, 1),
                    Cursor = Cursors.Hand,
                    Background = Brushes.Transparent,
                    Tag = $"color:{catValue}"
                };
                var c = legendColors[catValue];
                var swatch = new Ellipse
                {
                    Width = 12, Height = 12,
                    Fill = new SolidColorBrush(System.Windows.Media.Color.FromRgb(c.Red, c.Green, c.Blue)),
                    Margin = new Thickness(0, 0, 6, 0),
                    VerticalAlignment = System.Windows.VerticalAlignment.Center
                };
                sp.Children.Add(swatch);
                sp.Children.Add(new TextBlock
                {
                    Text = catValue, FontSize = 11,
                    VerticalAlignment = System.Windows.VerticalAlignment.Center
                });

                sp.MouseEnter += LegendItem_MouseEnter;
                sp.MouseLeave += LegendItem_MouseLeave;
                sp.MouseLeftButtonUp += LegendItem_Click;

                legendPanel.Children.Add(sp);
            }
        }

        if (useShape && shapeCats.Length > 0)
        {
            if (legendPanel.Children.Count > 0)
                legendPanel.Children.Add(new Separator { Margin = new Thickness(0, 4, 0, 4) });
            for (int i = 0; i < shapeCats.Length; i++)
            {
                string catValue = shapeCats[i];
                var sp = new StackPanel
                {
                    Orientation = System.Windows.Controls.Orientation.Horizontal,
                    Margin = new Thickness(0, 1, 0, 1),
                    Cursor = Cursors.Hand,
                    Background = Brushes.Transparent,
                    Tag = $"shape:{catValue}"
                };
                sp.Children.Add(new TextBlock
                {
                    Text = Shapes[i % Shapes.Length].Name,
                    FontSize = 10, Width = 60, Foreground = Brushes.Gray,
                    VerticalAlignment = System.Windows.VerticalAlignment.Center
                });
                sp.Children.Add(new TextBlock
                {
                    Text = catValue, FontSize = 11,
                    VerticalAlignment = System.Windows.VerticalAlignment.Center
                });

                sp.MouseEnter += LegendItem_MouseEnter;
                sp.MouseLeave += LegendItem_MouseLeave;
                sp.MouseLeftButtonUp += LegendItem_Click;

                legendPanel.Children.Add(sp);
            }
        }

        if (legendPanel.Children.Count == 0)
            legendPanel.Children.Add(new TextBlock { Text = "(no grouping)", FontSize = 11, Foreground = Brushes.Gray });
    }

    private HashSet<int> GetRowsForLegendItem(string? tag)
    {
        if (tag == null) return new HashSet<int>();
        if (tag.StartsWith("color:"))
        {
            string cat = tag.Substring(6);
            return _colorCategoryRows.TryGetValue(cat, out var rows) ? rows : new HashSet<int>();
        }
        if (tag.StartsWith("shape:"))
        {
            string cat = tag.Substring(6);
            return _shapeCategoryRows.TryGetValue(cat, out var rows) ? rows : new HashSet<int>();
        }
        return new HashSet<int>();
    }

    private void LegendItem_MouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is StackPanel sp)
        {
            sp.Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(40, 0, 180, 255));
            _legendHighlightedRows = GetRowsForLegendItem(sp.Tag?.ToString());
            UpdatePlot(true);
        }
    }

    private void LegendItem_MouseLeave(object sender, MouseEventArgs e)
    {
        if (sender is StackPanel sp)
        {
            sp.Background = Brushes.Transparent;
            _legendHighlightedRows.Clear();
            UpdatePlot(true);
        }
    }

    private void LegendItem_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is StackPanel sp)
        {
            var rows = GetRowsForLegendItem(sp.Tag?.ToString());
            _selectedRows = new HashSet<int>(rows);
            _gridHighlightedRows.Clear();
            _legendHighlightedRows.Clear();
            UpdateSelectionDisplay();
            UpdatePlot(true);
            BroadcastSelection();
        }
    }

    // ====================================================================
    //  Column filters
    // ====================================================================
    private void BuildFilterPanel()
    {
        filterPanel.Children.Clear();
        _numericFilters.Clear();
        _categoricalFilters.Clear();
        _filteredRows = null;

        if (_data == null || _columns == null) return;

        foreach (var col in _columns)
        {
            if (IsNumericColumn(col))
                BuildNumericFilter(col);
            else
                BuildCategoricalFilter(col);
        }
    }

    private void BuildNumericFilter(string col)
    {
        var vals = NumericValues(col);
        if (vals.Length == 0) return;

        double dataMin = vals.Min(), dataMax = vals.Max();
        _numericFilters[col] = (dataMin, dataMax);

        var expander = new Expander
        {
            Header = col,
            FontSize = 11,
            IsExpanded = false,
            Margin = new Thickness(0, 0, 0, 2)
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition());
        grid.RowDefinitions.Add(new RowDefinition());

        var lblMin = new TextBlock { Text = "Min", FontSize = 10, Foreground = Brushes.Gray };
        var lblMax = new TextBlock { Text = "Max", FontSize = 10, Foreground = Brushes.Gray, Margin = new Thickness(4, 0, 0, 0) };
        Grid.SetColumn(lblMin, 0); Grid.SetRow(lblMin, 0);
        Grid.SetColumn(lblMax, 1); Grid.SetRow(lblMax, 0);

        var txtMin = new TextBox
        {
            Text = dataMin.ToString("G6"),
            FontSize = 10,
            Margin = new Thickness(0, 0, 2, 0),
            Tag = $"fmin:{col}"
        };
        var txtMax = new TextBox
        {
            Text = dataMax.ToString("G6"),
            FontSize = 10,
            Margin = new Thickness(2, 0, 0, 0),
            Tag = $"fmax:{col}"
        };
        Grid.SetColumn(txtMin, 0); Grid.SetRow(txtMin, 1);
        Grid.SetColumn(txtMax, 1); Grid.SetRow(txtMax, 1);

        txtMin.LostFocus += FilterNumeric_Changed;
        txtMax.LostFocus += FilterNumeric_Changed;

        grid.Children.Add(lblMin);
        grid.Children.Add(lblMax);
        grid.Children.Add(txtMin);
        grid.Children.Add(txtMax);

        // Right-click context menu for numeric filters
        var ctx = new ContextMenu();
        var colorBy = new MenuItem { Header = "Colour by this column", Tag = $"fctx:color:{col}" };
        colorBy.Click += FilterContext_Click;
        var sizeBy = new MenuItem { Header = "Size by this column", Tag = $"fctx:size:{col}" };
        sizeBy.Click += FilterContext_Click;
        var labelBy = new MenuItem { Header = "Label by this column", Tag = $"fctx:label:{col}" };
        labelBy.Click += FilterContext_Click;
        ctx.Items.Add(colorBy);
        ctx.Items.Add(sizeBy);
        ctx.Items.Add(labelBy);
        grid.ContextMenu = ctx;

        expander.Content = grid;
        filterPanel.Children.Add(expander);
    }

    private void BuildCategoricalFilter(string col)
    {
        var categories = _data!.Rows.Cast<DataRow>()
            .Select(r => r[col].ToString()!)
            .Distinct()
            .OrderBy(s => s)
            .ToArray();

        if (categories.Length == 0) return;

        _categoricalFilters[col] = new HashSet<string>(categories);

        var expander = new Expander
        {
            Header = col,
            FontSize = 11,
            IsExpanded = false,
            Margin = new Thickness(0, 0, 0, 2)
        };

        var scrollViewer = new ScrollViewer
        {
            MaxHeight = 120,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };

        var stack = new StackPanel { Tag = $"fcat:{col}" };

        foreach (var cat in categories)
        {
            var cb = new CheckBox
            {
                Content = cat,
                IsChecked = true,
                FontSize = 10,
                Tag = $"fcat:{col}:{cat}",
                Margin = new Thickness(0, 1, 0, 1)
            };
            cb.Checked += FilterCategorical_Changed;
            cb.Unchecked += FilterCategorical_Changed;

            // Per-checkbox context menu for colour assignment
            var cbCtx = new ContextMenu();
            var assignColor = new MenuItem { Header = "Assign colour...", Tag = cat };
            assignColor.Click += (s, ev) =>
            {
                string catVal = (string)((MenuItem)s!).Tag;
                ShowCategoryColorPicker(catVal);
            };
            var clearColor = new MenuItem { Header = "Clear colour", Tag = cat };
            clearColor.Click += (s, ev) =>
            {
                string catVal = (string)((MenuItem)s!).Tag;
                _categoryColorOverrides.Remove(catVal);
                UpdatePlot(true);
            };
            cbCtx.Items.Add(assignColor);
            cbCtx.Items.Add(clearColor);
            cb.ContextMenu = cbCtx;

            stack.Children.Add(cb);
        }

        // Right-click context menu
        var ctx = new ContextMenu();
        var selectAll = new MenuItem { Header = "Select All", Tag = $"fctx:all:{col}" };
        selectAll.Click += FilterContext_Click;
        var selectNone = new MenuItem { Header = "Select None", Tag = $"fctx:none:{col}" };
        selectNone.Click += FilterContext_Click;
        var colorBy = new MenuItem { Header = "Colour by this column", Tag = $"fctx:color:{col}" };
        colorBy.Click += FilterContext_Click;
        var sizeBy = new MenuItem { Header = "Size by this column", Tag = $"fctx:size:{col}" };
        sizeBy.Click += FilterContext_Click;
        var labelBy2 = new MenuItem { Header = "Label by this column", Tag = $"fctx:label:{col}" };
        labelBy2.Click += FilterContext_Click;
        ctx.Items.Add(selectAll);
        ctx.Items.Add(selectNone);
        ctx.Items.Add(new Separator());
        ctx.Items.Add(colorBy);
        ctx.Items.Add(sizeBy);
        ctx.Items.Add(labelBy2);
        stack.ContextMenu = ctx;

        scrollViewer.Content = stack;
        expander.Content = scrollViewer;
        filterPanel.Children.Add(expander);
    }

    private void ShowCategoryColorPicker(string categoryValue)
    {
        // Colour options: Cat10 palette + Black + White
        var colors = new (string Name, string Hex)[]
        {
            ("Blue",      "#1F77B4"), ("Orange",    "#FF7F0E"), ("Green",     "#2CA02C"),
            ("Red",       "#D62728"), ("Purple",    "#9467BD"), ("Brown",     "#8C564B"),
            ("Pink",      "#E377C2"), ("Grey",      "#7F7F7F"), ("Yellow",    "#BCBD22"),
            ("Cyan",      "#17BECF"), ("Black",     "#000000"), ("White",     "#FFFFFF"),
        };

        var popup = new Window
        {
            Title = $"Assign colour to \"{categoryValue}\"",
            Width = 250, Height = 220,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            ResizeMode = ResizeMode.NoResize
        };

        var wrap = new System.Windows.Controls.WrapPanel { Margin = new Thickness(8) };
        foreach (var (name, hex) in colors)
        {
            var parsed = ParseHexColor(hex);
            var btn = new Button
            {
                Width = 40, Height = 30, Margin = new Thickness(2),
                Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(parsed.Red, parsed.Green, parsed.Blue)),
                ToolTip = name, Tag = hex,
                BorderBrush = hex == "#FFFFFF" ? Brushes.Gray : Brushes.Transparent,
                BorderThickness = new Thickness(1)
            };
            btn.Click += (s, ev) =>
            {
                _categoryColorOverrides[categoryValue] = (string)((Button)s!).Tag;
                popup.Close();
                UpdatePlot(true);
            };
            wrap.Children.Add(btn);
        }

        popup.Content = wrap;
        popup.ShowDialog();
    }

    private void FilterNumeric_Changed(object sender, RoutedEventArgs e)
    {
        if (_updating || sender is not TextBox tb) return;
        string? tag = tb.Tag?.ToString();
        if (tag == null) return;

        bool isMin = tag.StartsWith("fmin:");
        string col = tag.Substring(5);

        if (!_numericFilters.ContainsKey(col)) return;
        var (curMin, curMax) = _numericFilters[col];

        if (double.TryParse(tb.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out double val))
        {
            if (isMin) _numericFilters[col] = (val, curMax);
            else _numericFilters[col] = (curMin, val);
        }

        RecomputeFilteredRows();
        UpdatePlot();
        UpdateSelectionDisplay();
        BroadcastFilterChange();
    }

    private void FilterCategorical_Changed(object sender, RoutedEventArgs e)
    {
        if (_updating || sender is not CheckBox cb) return;
        string? tag = cb.Tag?.ToString();
        if (tag == null || !tag.StartsWith("fcat:")) return;

        // tag = "fcat:ColName:CatValue"
        var parts = tag.Split(':', 3);
        if (parts.Length < 3) return;
        string col = parts[1];
        string catVal = parts[2];

        if (!_categoricalFilters.ContainsKey(col)) return;

        if (cb.IsChecked == true)
            _categoricalFilters[col].Add(catVal);
        else
            _categoricalFilters[col].Remove(catVal);

        RecomputeFilteredRows();
        UpdatePlot();
        UpdateSelectionDisplay();
        BroadcastFilterChange();
    }

    private void FilterContext_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem mi) return;
        string? tag = mi.Tag?.ToString();
        if (tag == null) return;

        var parts = tag.Split(':', 3);
        if (parts.Length < 3) return;
        string action = parts[1];
        string col = parts[2];

        if (action == "color")
        {
            // Set colour-by to this column
            for (int i = 0; i < cboColor.Items.Count; i++)
            {
                if (cboColor.Items[i].ToString() == col)
                {
                    cboColor.SelectedIndex = i;
                    break;
                }
            }
            return;
        }

        if (action == "size")
        {
            // Set size-by to this column
            for (int i = 0; i < cboSizeCol.Items.Count; i++)
            {
                if (cboSizeCol.Items[i].ToString() == col)
                {
                    cboSizeCol.SelectedIndex = i;
                    break;
                }
            }
            return;
        }

        if (action == "label")
        {
            for (int i = 0; i < cboLabelCol.Items.Count; i++)
            {
                if (cboLabelCol.Items[i].ToString() == col)
                {
                    cboLabelCol.SelectedIndex = i;
                    break;
                }
            }
            return;
        }

        if (!_categoricalFilters.ContainsKey(col)) return;

        bool selectAll = action == "all";

        // Find the StackPanel with checkboxes for this column
        foreach (var child in filterPanel.Children)
        {
            if (child is Expander exp && exp.Content is ScrollViewer sv && sv.Content is StackPanel sp)
            {
                if (sp.Tag?.ToString() == $"fcat:{col}")
                {
                    _updating = true;
                    foreach (var item in sp.Children)
                    {
                        if (item is CheckBox cb)
                        {
                            cb.IsChecked = selectAll;
                        }
                    }
                    _updating = false;

                    if (selectAll)
                    {
                        var allCats = _data!.Rows.Cast<DataRow>()
                            .Select(r => r[col].ToString()!)
                            .Distinct();
                        _categoricalFilters[col] = new HashSet<string>(allCats);
                    }
                    else
                    {
                        _categoricalFilters[col].Clear();
                    }
                    break;
                }
            }
        }

        RecomputeFilteredRows();
        UpdatePlot();
        UpdateSelectionDisplay();
        BroadcastFilterChange();
    }

    private void RecomputeFilteredRows()
    {
        if (_data == null) { _filteredRows = null; return; }

        int n = _data.Rows.Count;
        var filtered = new HashSet<int>();

        for (int i = 0; i < n; i++)
        {
            bool pass = true;
            var row = _data.Rows[i];

            foreach (var (col, (fmin, fmax)) in _numericFilters)
            {
                if (double.TryParse(row[col].ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out double v))
                {
                    if (v < fmin || v > fmax) { pass = false; break; }
                }
            }

            if (pass)
            {
                foreach (var (col, allowed) in _categoricalFilters)
                {
                    string val = row[col].ToString()!;
                    if (!allowed.Contains(val)) { pass = false; break; }
                }
            }

            if (pass) filtered.Add(i);
        }

        _filteredRows = filtered.Count == n ? null : filtered;
    }

    private bool IsRowVisible(int i) => _filteredRows == null || _filteredRows.Contains(i);

    // ====================================================================
    //  Axis helpers
    // ====================================================================
    private void ApplyAxisRange()
    {
        var plot = wpfPlot.Plot;
        double xmin = 0, xmax = 0, ymin = 0, ymax = 0;
        bool hasXRange = double.TryParse(txtXMin.Text, out xmin) &&
                         double.TryParse(txtXMax.Text, out xmax) && xmin < xmax;
        bool hasYRange = double.TryParse(txtYMin.Text, out ymin) &&
                         double.TryParse(txtYMax.Text, out ymax) && ymin < ymax;

        // Compute default limits for each axis
        double defXMin = _xCategories != null ? 0.3 : _dataXMin;
        double defXMax = _xCategories != null ? _xCategories.Length + 0.7 : _dataXMax;
        double defYMin = _yCategories != null ? 0.3 : _dataYMin;
        double defYMax = _yCategories != null ? _yCategories.Length + 0.7 : _dataYMax;

        plot.Axes.SetLimits(
            hasXRange ? xmin : defXMin,
            hasXRange ? xmax : defXMax,
            hasYRange ? ymin : defYMin,
            hasYRange ? ymax : defYMax);

        // Apply axis inversion by swapping limits
        if (chkInvertX.IsChecked == true || chkInvertY.IsChecked == true)
        {
            var lim = plot.Axes.GetLimits();
            plot.Axes.SetLimits(
                chkInvertX.IsChecked == true ? lim.Right : lim.Left,
                chkInvertX.IsChecked == true ? lim.Left : lim.Right,
                chkInvertY.IsChecked == true ? lim.Top : lim.Bottom,
                chkInvertY.IsChecked == true ? lim.Bottom : lim.Top);
        }
    }

    private void UpdateAxisRangeBoxes()
    {
        if (_data == null || cboX.SelectedItem == null || cboY.SelectedItem == null) return;
        string xCol = cboX.SelectedItem.ToString()!;
        string yCol = cboY.SelectedItem.ToString()!;

        if (chkLogX.IsChecked == true)
        { txtXMin.Text = ""; txtXMax.Text = ""; }
        else if (IsNumericColumn(xCol) && _data.Rows.Count > 0)
        {
            var vals = NumericValues(xCol);
            if (vals.Length > 0) { txtXMin.Text = vals.Min().ToString("G6"); txtXMax.Text = vals.Max().ToString("G6"); }
        }
        else { txtXMin.Text = ""; txtXMax.Text = ""; }

        if (chkLogY.IsChecked == true)
        { txtYMin.Text = ""; txtYMax.Text = ""; }
        else if (IsNumericColumn(yCol) && _data.Rows.Count > 0)
        {
            var vals = NumericValues(yCol);
            if (vals.Length > 0) { txtYMin.Text = vals.Min().ToString("G6"); txtYMax.Text = vals.Max().ToString("G6"); }
        }
        else { txtYMin.Text = ""; txtYMax.Text = ""; }
    }

    private double[] NumericValues(string col) =>
        _data!.Rows.Cast<DataRow>()
            .Select(r => double.TryParse(r[col].ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out double v) ? v : double.NaN)
            .Where(v => !double.IsNaN(v)).ToArray();

    private ScottPlot.Color GetSelectedColor()
    {
        int idx = Math.Max(0, cboPresetColor.SelectedIndex);
        string hex = PresetColors[idx].Hex;
        return new ScottPlot.Color(
            Convert.ToByte(hex.Substring(1, 2), 16),
            Convert.ToByte(hex.Substring(3, 2), 16),
            Convert.ToByte(hex.Substring(5, 2), 16));
    }

    private static List<ScottPlot.Tick> GenerateLog10Ticks(double[]? logValues)
    {
        var ticks = new List<ScottPlot.Tick>();
        if (logValues == null || logValues.Length == 0) return ticks;
        var valid = logValues.Where(v => !double.IsNaN(v) && !double.IsInfinity(v)).ToArray();
        if (valid.Length == 0) return ticks;

        double lo = valid.Min(), hi = valid.Max();
        int minExp = (int)Math.Floor(lo) - 1;
        int maxExp = (int)Math.Ceiling(hi) + 1;

        for (int exp = minExp; exp <= maxExp; exp++)
        {
            double realVal = Math.Pow(10, exp);
            ticks.Add(new ScottPlot.Tick(exp, realVal >= 1 ? realVal.ToString("G0") : realVal.ToString("G4")));
        }
        if (maxExp - minExp <= 6)
        {
            foreach (int sub in new[] { 2, 5 })
                for (int exp = minExp; exp <= maxExp; exp++)
                {
                    double realVal = sub * Math.Pow(10, exp);
                    double logPos = Math.Log10(realVal);
                    if (logPos >= lo - 0.5 && logPos <= hi + 0.5)
                        ticks.Add(new ScottPlot.Tick(logPos, realVal >= 1 ? realVal.ToString("G0") : realVal.ToString("G4")));
                }
        }
        ticks.Sort((a, b) => a.Position.CompareTo(b.Position));
        return ticks;
    }

    // ====================================================================
    //  Data area boundary detection for axis zoom
    // ====================================================================
    /// <summary>Convert a WPF device-independent position to a ScottPlot pixel (accounts for DPI scaling).</summary>
    private ScottPlot.Pixel WpfToPlotPixel(System.Windows.Point pos)
    {
        float s = wpfPlot.DisplayScale;
        return new ScottPlot.Pixel((float)pos.X * s, (float)pos.Y * s);
    }

    /// <summary>Convert a ScottPlot pixel back to WPF device-independent coordinates.</summary>
    private System.Windows.Point PlotPixelToWpf(ScottPlot.Pixel px)
    {
        float s = wpfPlot.DisplayScale;
        return new System.Windows.Point(px.X / s, px.Y / s);
    }

    private (double left, double right, double top, double bottom) GetDataAreaPixelBounds()
    {
        // Returns bounds in WPF DIP space so comparisons with GetPosition() work
        var limits = wpfPlot.Plot.Axes.GetLimits();
        var bottomLeft = PlotPixelToWpf(wpfPlot.Plot.GetPixel(new Coordinates(limits.Left, limits.Bottom)));
        var topRight = PlotPixelToWpf(wpfPlot.Plot.GetPixel(new Coordinates(limits.Right, limits.Top)));
        return (bottomLeft.X, topRight.X, topRight.Y, bottomLeft.Y);
    }

    // ====================================================================
    //  Hover tooltip + nearest point
    // ====================================================================
    private int FindNearestPoint(System.Windows.Point pos)
    {
        if (_plotX == null || _plotY == null) return -1;
        var plotPx = WpfToPlotPixel(pos);
        int nearest = -1;
        double bestDist = double.MaxValue;
        for (int i = 0; i < _plotX.Length; i++)
        {
            if (!IsRowVisible(i)) continue;
            if (double.IsNaN(_plotX[i]) || double.IsNaN(_plotY[i])) continue;
            var px = wpfPlot.Plot.GetPixel(new Coordinates(_plotX[i], _plotY[i]));
            double dx = px.X - plotPx.X;
            double dy = px.Y - plotPx.Y;
            double dist = dx * dx + dy * dy;
            if (dist < bestDist) { bestDist = dist; nearest = i; }
        }
        float scaledThreshold = 20 * wpfPlot.DisplayScale;
        return nearest >= 0 && bestDist < scaledThreshold * scaledThreshold ? nearest : -1;
    }

    private void HandleHover(MouseEventArgs e)
    {
        if (_plotX == null || _plotY == null || _data == null) { HideTooltip(); return; }

        var pos = e.GetPosition(wpfPlot);
        var plotPx = WpfToPlotPixel(pos);
        var coord = wpfPlot.Plot.GetCoordinates(plotPx);

        // Find nearest point (in pixel space for accuracy)
        int nearest = -1;
        double bestDist = double.MaxValue;
        for (int i = 0; i < _plotX.Length; i++)
        {
            if (double.IsNaN(_plotX[i]) || double.IsNaN(_plotY[i])) continue;
            var px = wpfPlot.Plot.GetPixel(new Coordinates(_plotX[i], _plotY[i]));
            double dx = px.X - plotPx.X;
            double dy = px.Y - plotPx.Y;
            double dist = dx * dx + dy * dy;
            if (dist < bestDist) { bestDist = dist; nearest = i; }
        }

        float hoverThreshold = 20 * wpfPlot.DisplayScale;
        if (nearest >= 0 && bestDist < hoverThreshold * hoverThreshold)
        {
            if (_hoveredRow != nearest)
            {
                _hoveredRow = nearest;
                ShowTooltip(nearest, pos);
                HighlightDataGridRow(nearest);
                UpdatePlot(true);
            }
        }
        else if (_hoveredRow >= 0)
        {
            _hoveredRow = -1;
            HideTooltip();
            UpdatePlot(true);
        }
    }

    private void ShowTooltip(int rowIdx, System.Windows.Point pos)
    {
        if (_data == null || _columns == null) return;
        var row = _data.Rows[rowIdx];
        var sb = new StringBuilder();
        foreach (var col in _columns)
            sb.AppendLine($"{col}: {row[col]}");
        hoverTooltipText.Text = sb.ToString().TrimEnd();
        hoverTooltip.Visibility = Visibility.Visible;
        hoverTooltip.Margin = new Thickness(pos.X + 15, pos.Y + 15, 0, 0);
    }

    private void HideTooltip()
    {
        hoverTooltip.Visibility = Visibility.Collapsed;
    }

    private void HighlightDataGridRow(int rowIdx)
    {
        if (dgSelected.ItemsSource is DataView dv)
        {
            for (int i = 0; i < dv.Count; i++)
            {
                if (RowMatches(dv[i].Row, _data!.Rows[rowIdx]))
                {
                    _updating = true;
                    dgSelected.SelectedIndex = i;
                    dgSelected.ScrollIntoView(dgSelected.Items[i]);
                    _updating = false;
                    break;
                }
            }
        }
    }

    private bool RowMatches(DataRow a, DataRow b)
    {
        for (int c = 0; c < a.Table.Columns.Count; c++)
            if (a[c].ToString() != b[c].ToString()) return false;
        return true;
    }

    // ====================================================================
    //  Box selection + axis zoom via left-drag
    // ====================================================================
    private void SetupSelectionInput()
    {
        // Disable all ScottPlot built-in interactions — we handle everything
        wpfPlot.UserInputProcessor.IsEnabled = false;

        wpfPlot.MouseLeftButtonDown += Plot_MouseDown;
        wpfPlot.MouseRightButtonDown += Plot_RightMouseDown;
        wpfPlot.MouseMove += Plot_MouseMove;
        wpfPlot.MouseLeftButtonUp += Plot_MouseUp;
        wpfPlot.MouseRightButtonUp += Plot_RightMouseUp;
        wpfPlot.MouseWheel += Plot_MouseWheel;
        wpfPlot.Cursor = Cursors.Cross;
    }

    private void Plot_MouseDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(wpfPlot);
        _selectStart = WpfToPlotPixel(_dragStart);
        _isSelecting = true;
        wpfPlot.CaptureMouse();

        // Determine drag mode based on where the click lands
        try
        {
            var bounds = GetDataAreaPixelBounds();
            if (_dragStart.Y > bounds.bottom + 5)
                _dragMode = DragMode.AxisXPan;
            else if (_dragStart.X < bounds.left - 5)
                _dragMode = DragMode.AxisYPan;
            else
                _dragMode = DragMode.BoxSelect;
        }
        catch
        {
            _dragMode = DragMode.BoxSelect;
        }

        // For axis pan, save start state and show green highlight
        if (_dragMode == DragMode.AxisXPan || _dragMode == DragMode.AxisYPan)
        {
            _panStart = _dragStart;
            _panStartLimits = wpfPlot.Plot.Axes.GetLimits();
            selectionRect.Visibility = Visibility.Visible;
            selectionRect.Stroke = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x22, 0xAA, 0x44));
            selectionRect.Fill = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x33, 0x22, 0xAA, 0x44));
            try
            {
                var bounds = GetDataAreaPixelBounds();
                if (_dragMode == DragMode.AxisXPan)
                {
                    Canvas.SetLeft(selectionRect, bounds.left);
                    Canvas.SetTop(selectionRect, bounds.bottom);
                    selectionRect.Width = bounds.right - bounds.left;
                    selectionRect.Height = wpfPlot.ActualHeight - bounds.bottom;
                }
                else
                {
                    Canvas.SetLeft(selectionRect, 0);
                    Canvas.SetTop(selectionRect, bounds.top);
                    selectionRect.Width = bounds.left;
                    selectionRect.Height = bounds.bottom - bounds.top;
                }
            }
            catch { selectionRect.Visibility = Visibility.Collapsed; }
        }
        else
        {
            selectionRect.Visibility = Visibility.Visible;
            Canvas.SetLeft(selectionRect, _dragStart.X);
            Canvas.SetTop(selectionRect, _dragStart.Y);
            selectionRect.Width = 0;
            selectionRect.Height = 0;
            selectionRect.Stroke = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x22, 0x77, 0xDD));
            selectionRect.Fill = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x22, 0x1F, 0x77, 0xB4));
        }
    }

    private void Plot_MouseMove(object sender, MouseEventArgs e)
    {
        if (_dragMode == DragMode.RightDragPan)
        {
            HandleRightDragPan(e);
            return;
        }

        if (_dragMode == DragMode.AxisXPan || _dragMode == DragMode.AxisYPan)
        {
            HandleAxisPan(e);
            return;
        }

        if (_isSelecting)
        {
            var pos = e.GetPosition(wpfPlot);
            Canvas.SetLeft(selectionRect, Math.Min(pos.X, _dragStart.X));
            Canvas.SetTop(selectionRect, Math.Min(pos.Y, _dragStart.Y));
            selectionRect.Width = Math.Abs(pos.X - _dragStart.X);
            selectionRect.Height = Math.Abs(pos.Y - _dragStart.Y);
        }
        else
        {
            HandleHover(e);
        }
    }

    private void Plot_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isSelecting) return;
        _isSelecting = false;
        wpfPlot.ReleaseMouseCapture();
        selectionRect.Visibility = Visibility.Collapsed;

        var pos = e.GetPosition(wpfPlot);
        var selectEnd = WpfToPlotPixel(pos);

        // Too small = click, not drag
        bool tooSmall = Math.Abs(selectEnd.X - _selectStart.X) < 3 &&
                        Math.Abs(selectEnd.Y - _selectStart.Y) < 3;

        if (_dragMode == DragMode.AxisXPan || _dragMode == DragMode.AxisYPan)
        {
            selectionRect.Visibility = Visibility.Collapsed;
            _dragMode = DragMode.None;
            return;
        }

        // Normal box select
        var cs = wpfPlot.Plot.GetCoordinates(_selectStart);
        var ce = wpfPlot.Plot.GetCoordinates(selectEnd);
        double dataXMin = Math.Min(cs.X, ce.X);
        double dataXMax = Math.Max(cs.X, ce.X);
        double dataYMin = Math.Min(cs.Y, ce.Y);
        double dataYMax = Math.Max(cs.Y, ce.Y);

        if (_plotX == null || _plotY == null) return;

        bool shiftHeld = Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift);

        if (tooSmall)
        {
            if (shiftHeld)
            {
                // Shift+click: toggle nearest point
                int nearest = FindNearestPoint(pos);
                if (nearest >= 0)
                {
                    if (!_selectedRows.Remove(nearest))
                        _selectedRows.Add(nearest);
                }
            }
            else
            {
                _selectedRows.Clear();
                int nearest = FindNearestPoint(pos);
                if (nearest >= 0)
                    _selectedRows.Add(nearest);
            }
        }
        else
        {
            // Find points in the box
            var boxed = new HashSet<int>();
            for (int i = 0; i < _plotX.Length; i++)
            {
                if (!IsRowVisible(i)) continue;
                if (double.IsNaN(_plotX[i]) || double.IsNaN(_plotY[i])) continue;
                if (_plotX[i] >= dataXMin && _plotX[i] <= dataXMax &&
                    _plotY[i] >= dataYMin && _plotY[i] <= dataYMax)
                    boxed.Add(i);
            }

            if (shiftHeld)
            {
                // Toggle: remove if already selected, add if not
                foreach (int i in boxed)
                {
                    if (!_selectedRows.Remove(i))
                        _selectedRows.Add(i);
                }
            }
            else
            {
                _selectedRows = boxed;
            }
        }

        _gridHighlightedRows.Clear();
        _dragMode = DragMode.None;
        UpdateSelectionDisplay();
        UpdatePlot(true);
        BroadcastSelection();
    }

    // ====================================================================
    //  Right-drag pan (pure translation, no scale change)
    // ====================================================================
    private void Plot_RightMouseDown(object sender, MouseButtonEventArgs e)
    {
        _panStart = e.GetPosition(wpfPlot);
        _panStartLimits = wpfPlot.Plot.Axes.GetLimits();
        _dragMode = DragMode.RightDragPan;
        wpfPlot.CaptureMouse();
        e.Handled = true;
    }

    private void Plot_RightMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_dragMode == DragMode.RightDragPan)
        {
            var pos = e.GetPosition(wpfPlot);
            bool wasDrag = Math.Abs(pos.X - _panStart.X) > 3 || Math.Abs(pos.Y - _panStart.Y) > 3;
            _dragMode = DragMode.None;
            wpfPlot.ReleaseMouseCapture();

            if (!wasDrag && _data != null)
            {
                var cm = new ContextMenu();
                var fitItem = new MenuItem { Header = "Fit Linear Model..." };
                fitItem.Click += (s, args) => ShowFitModelDialog();
                cm.Items.Add(fitItem);
                if (_regressionCoeffs != null)
                {
                    var clearItem = new MenuItem { Header = "Clear Model" };
                    clearItem.Click += (s, args) => ClearRegressionModel();
                    cm.Items.Add(clearItem);
                }
                cm.Items.Add(new Separator());
                var zoomOriginal = new MenuItem { Header = "Zoom to original data" };
                zoomOriginal.Click += (s, args) => { UpdateAxisRangeBoxes(); UpdatePlot(); };
                cm.Items.Add(zoomOriginal);
                var showAll = new MenuItem { Header = "Show all" };
                showAll.Click += (s, args) =>
                {
                    BuildFilterPanel();
                    UpdateAxisRangeBoxes();
                    UpdatePlot();
                    BroadcastFilterChange();
                };
                cm.Items.Add(showAll);
                cm.Items.Add(new Separator());
                var cartesian = new MenuItem
                {
                    Header = "Draw Cartesian axes",
                    IsCheckable = true,
                    IsChecked = _showCartesianAxes
                };
                cartesian.Click += (s, args) =>
                {
                    _showCartesianAxes = !_showCartesianAxes;
                    UpdatePlot(true);
                };
                cm.Items.Add(cartesian);
                if (_selectedRows.Count > 0)
                {
                    cm.Items.Add(new Separator());
                    var copyItem = new MenuItem { Header = $"Copy selected ({_selectedRows.Count})" };
                    copyItem.Click += (s2, args2) => CopySelectedToClipboard();
                    cm.Items.Add(copyItem);
                }
                cm.PlacementTarget = wpfPlot;
                cm.IsOpen = true;
            }

            e.Handled = true;
        }
    }

    private void HandleRightDragPan(MouseEventArgs e)
    {
        var pos = e.GetPosition(wpfPlot);
        // Convert pixel delta to data delta
        var startCoord = wpfPlot.Plot.GetCoordinates(WpfToPlotPixel(_panStart));
        var currentCoord = wpfPlot.Plot.GetCoordinates(WpfToPlotPixel(pos));
        double dx = startCoord.X - currentCoord.X;
        double dy = startCoord.Y - currentCoord.Y;

        double newLeft = _panStartLimits.Left + dx;
        double newRight = _panStartLimits.Right + dx;
        double newBottom = _panStartLimits.Bottom + dy;
        double newTop = _panStartLimits.Top + dy;

        // Clamp pan so we can't scroll past the data extent
        double viewWidth = newRight - newLeft;
        double viewHeight = newTop - newBottom;

        if (newLeft < _dataXMin) { newLeft = _dataXMin; newRight = newLeft + viewWidth; }
        if (newRight > _dataXMax) { newRight = _dataXMax; newLeft = newRight - viewWidth; }
        if (newBottom < _dataYMin) { newBottom = _dataYMin; newTop = newBottom + viewHeight; }
        if (newTop > _dataYMax) { newTop = _dataYMax; newBottom = newTop - viewHeight; }

        wpfPlot.Plot.Axes.SetLimits(newLeft, newRight, newBottom, newTop);
        wpfPlot.Refresh();
    }

    private void HandleAxisPan(MouseEventArgs e)
    {
        var pos = e.GetPosition(wpfPlot);
        var startCoord = wpfPlot.Plot.GetCoordinates(WpfToPlotPixel(_panStart));
        var currentCoord = wpfPlot.Plot.GetCoordinates(WpfToPlotPixel(pos));

        var limits = _panStartLimits;

        if (_dragMode == DragMode.AxisXPan)
        {
            double dx = startCoord.X - currentCoord.X;
            double newLeft = limits.Left + dx;
            double newRight = limits.Right + dx;
            double viewWidth = newRight - newLeft;

            if (newLeft < _dataXMin) { newLeft = _dataXMin; newRight = newLeft + viewWidth; }
            if (newRight > _dataXMax) { newRight = _dataXMax; newLeft = newRight - viewWidth; }

            wpfPlot.Plot.Axes.SetLimits(newLeft, newRight, limits.Bottom, limits.Top);
        }
        else // AxisYPan
        {
            double dy = startCoord.Y - currentCoord.Y;
            double newBottom = limits.Bottom + dy;
            double newTop = limits.Top + dy;
            double viewHeight = newTop - newBottom;

            if (newBottom < _dataYMin) { newBottom = _dataYMin; newTop = newBottom + viewHeight; }
            if (newTop > _dataYMax) { newTop = _dataYMax; newBottom = newTop - viewHeight; }

            wpfPlot.Plot.Axes.SetLimits(limits.Left, limits.Right, newBottom, newTop);
        }
        wpfPlot.Refresh();
    }

    // ====================================================================
    //  Mouse wheel zoom (clamped to data range)
    // ====================================================================
    private void Plot_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_plotX == null || _plotY == null) return;

        var pos = e.GetPosition(wpfPlot);
        var coord = wpfPlot.Plot.GetCoordinates(WpfToPlotPixel(pos));
        var limits = wpfPlot.Plot.Axes.GetLimits();

        double factor = e.Delta > 0 ? 0.85 : 1.0 / 0.85; // zoom in or out

        // If zooming out, check if we're already at full data extent
        if (e.Delta < 0)
        {
            double dataWidth = _dataXMax - _dataXMin;
            double dataHeight = _dataYMax - _dataYMin;
            double viewWidth = limits.Right - limits.Left;
            double viewHeight = limits.Top - limits.Bottom;

            // Already showing full data range? Don't zoom out further
            if (viewWidth >= dataWidth && viewHeight >= dataHeight)
            {
                e.Handled = true;
                return;
            }
        }

        // Zoom centered on mouse position
        double newLeft = coord.X - (coord.X - limits.Left) * factor;
        double newRight = coord.X + (limits.Right - coord.X) * factor;
        double newBottom = coord.Y - (coord.Y - limits.Bottom) * factor;
        double newTop = coord.Y + (limits.Top - coord.Y) * factor;

        // Clamp to data extent when zooming out
        if (newLeft < _dataXMin) newLeft = _dataXMin;
        if (newRight > _dataXMax) newRight = _dataXMax;
        if (newBottom < _dataYMin) newBottom = _dataYMin;
        if (newTop > _dataYMax) newTop = _dataYMax;

        wpfPlot.Plot.Axes.SetLimits(newLeft, newRight, newBottom, newTop);
        wpfPlot.Refresh();
        e.Handled = true;
    }

    private void BtnClearSelection_Click(object sender, RoutedEventArgs e)
    {
        _selectedRows.Clear();
        _gridHighlightedRows.Clear();
        _legendHighlightedRows.Clear();
        UpdateSelectionDisplay();
        UpdatePlot(true);
        BroadcastSelection();
    }

    private void BtnResetZoom_Click(object sender, RoutedEventArgs e)
    {
        UpdateAxisRangeBoxes();
        UpdatePlot();
    }

    // ====================================================================
    //  Search / Query
    // ====================================================================
    private void BtnSearch_Click(object sender, RoutedEventArgs e) => ExecuteSearch(addToSelection: false);

    private void TxtSearch_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            bool shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
            ExecuteSearch(addToSelection: shift);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            txtSearch.Clear();
            txtSearchResult.Text = "";
            e.Handled = true;
        }
    }

    private void ExecuteSearch(bool addToSelection)
    {
        if (_data == null || _columns == null) return;
        string query = txtSearch.Text.Trim();
        if (string.IsNullOrEmpty(query))
        {
            txtSearchResult.Text = "";
            return;
        }

        var parsed = ParseQuery(query);
        if (parsed.error != null)
        {
            txtSearchResult.Foreground = System.Windows.Media.Brushes.Red;
            txtSearchResult.Text = parsed.error;
            return;
        }

        var matches = new HashSet<int>();
        for (int i = 0; i < _data.Rows.Count; i++)
        {
            if (!IsRowVisible(i)) continue;
            var row = _data.Rows[i];

            if (parsed.column == null)
            {
                // Simple text search: match any column
                foreach (string col in _columns)
                {
                    string val = row[col]?.ToString() ?? "";
                    if (val.Contains(query, StringComparison.OrdinalIgnoreCase))
                    {
                        matches.Add(i);
                        break;
                    }
                }
            }
            else
            {
                string cellVal = row[parsed.column]?.ToString() ?? "";
                bool match = EvaluateOperator(cellVal, parsed.op!, parsed.value!);
                if (match) matches.Add(i);
            }
        }

        if (addToSelection)
        {
            foreach (int idx in matches)
                _selectedRows.Add(idx);
        }
        else
        {
            _selectedRows = matches;
        }

        _gridHighlightedRows.Clear();
        UpdateSelectionDisplay();
        UpdatePlot(true);
        BroadcastSelection();

        txtSearchResult.Foreground = matches.Count > 0
            ? System.Windows.Media.Brushes.Gray
            : System.Windows.Media.Brushes.Gray;
        txtSearchResult.Text = matches.Count > 0
            ? $"{matches.Count} rows selected"
            : "No matches";
    }

    private static bool EvaluateOperator(string cellVal, string op, string queryVal)
    {
        double cellNum = 0, queryNum = 0;
        bool bothNumeric = double.TryParse(cellVal, NumberStyles.Float, CultureInfo.InvariantCulture, out cellNum)
                        && double.TryParse(queryVal, NumberStyles.Float, CultureInfo.InvariantCulture, out queryNum);

        switch (op)
        {
            case "=":
                return bothNumeric
                    ? cellNum == queryNum
                    : cellVal.Equals(queryVal, StringComparison.OrdinalIgnoreCase);
            case "!=":
                return bothNumeric
                    ? cellNum != queryNum
                    : !cellVal.Equals(queryVal, StringComparison.OrdinalIgnoreCase);
            case ">":
                return bothNumeric && cellNum > queryNum;
            case "<":
                return bothNumeric && cellNum < queryNum;
            case ">=":
                return bothNumeric && cellNum >= queryNum;
            case "<=":
                return bothNumeric && cellNum <= queryNum;
            case "contains":
                return cellVal.Contains(queryVal, StringComparison.OrdinalIgnoreCase);
            default:
                return false;
        }
    }

    private (string? column, string? op, string? value, string? error) ParseQuery(string query)
    {
        // Try to parse as: column operator value
        // Check multi-char operators first to avoid partial matches
        string[] operators = [">=", "<=", "!=", "contains", ">", "<", "="];

        foreach (string op in operators)
        {
            int idx;
            if (op == "contains")
            {
                // Look for " contains " (with spaces)
                idx = query.IndexOf(" contains ", StringComparison.OrdinalIgnoreCase);
                if (idx < 0) continue;
                string colPart = query[..idx].Trim();
                string valPart = query[(idx + 10)..].Trim();
                valPart = Unquote(valPart);
                string? resolved = ResolveColumnName(colPart);
                if (resolved == null)
                    return (null, null, null, $"Unknown column: {colPart}");
                if (string.IsNullOrEmpty(valPart))
                    return (null, null, null, "Missing value after 'contains'");
                return (resolved, "contains", valPart, null);
            }
            else
            {
                idx = query.IndexOf(op, StringComparison.Ordinal);
                if (idx <= 0) continue;
                string colPart = query[..idx].Trim();
                string valPart = query[(idx + op.Length)..].Trim();
                valPart = Unquote(valPart);
                if (string.IsNullOrEmpty(colPart)) continue;
                string? resolved = ResolveColumnName(colPart);
                if (resolved == null)
                {
                    // This operator position might be inside the value — try other operators
                    continue;
                }
                if (string.IsNullOrEmpty(valPart))
                    return (null, null, null, $"Missing value after '{op}'");
                return (resolved, op, valPart, null);
            }
        }

        // No operator found — treat as simple text search
        return (null, null, null, null);
    }

    private static string Unquote(string s)
    {
        if (s.Length >= 2 && ((s[0] == '"' && s[^1] == '"') || (s[0] == '\'' && s[^1] == '\'')))
            return s[1..^1];
        return s;
    }

    private string? ResolveColumnName(string input)
    {
        if (_columns == null) return null;
        // Exact match (case-insensitive)
        foreach (string col in _columns)
            if (col.Equals(input, StringComparison.OrdinalIgnoreCase)) return col;
        // Unambiguous prefix match
        var prefixMatches = _columns.Where(c => c.StartsWith(input, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (prefixMatches.Length == 1) return prefixMatches[0];
        return null;
    }

    private void UpdateSelectionDisplay()
    {
        if (_data == null) { dgSelected.ItemsSource = null; return; }

        // Save horizontal scroll position before replacing ItemsSource
        double hOffset = 0;
        var sv = GetDataGridScrollViewer(dgSelected);
        if (sv != null) hOffset = sv.HorizontalOffset;

        if (_selectedRows.Count == 0)
        {
            // Show all data when nothing is selected
            dgSelected.ItemsSource = _data.DefaultView;
            txtSelCount.Text = $"({_data.Rows.Count} rows total)";
            btnCopy.IsEnabled = false;
            menuCopySelected.IsEnabled = false;
        }
        else
        {
            var view = _data.Clone();
            foreach (int idx in _selectedRows.OrderBy(i => i))
                if (idx < _data.Rows.Count)
                    view.ImportRow(_data.Rows[idx]);
            dgSelected.ItemsSource = view.DefaultView;
            txtSelCount.Text = $"({_selectedRows.Count} selected)";
            btnCopy.IsEnabled = true;
            menuCopySelected.IsEnabled = true;
        }

        // Restore horizontal scroll position after layout updates
        if (sv != null && hOffset > 0)
            dgSelected.Dispatcher.BeginInvoke(() => sv.ScrollToHorizontalOffset(hOffset),
                System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private static ScrollViewer? GetDataGridScrollViewer(DependencyObject d)
    {
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(d); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(d, i);
            if (child is ScrollViewer sv) return sv;
            var result = GetDataGridScrollViewer(child);
            if (result != null) return result;
        }
        return null;
    }

    // ====================================================================
    //  DataGrid → plot highlighting
    // ====================================================================
    private void DgSelected_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updating || _data == null || _plotX == null) return;

        _gridHighlightedRows.Clear();
        foreach (var item in dgSelected.SelectedItems)
        {
            if (item is DataRowView drv)
            {
                // Find matching row index in _data
                for (int i = 0; i < _data.Rows.Count; i++)
                {
                    if (RowMatches(drv.Row, _data.Rows[i]))
                    {
                        _gridHighlightedRows.Add(i);
                        break;
                    }
                }
            }
        }
        UpdatePlot(true);
    }

    private void DgSelected_AutoGeneratingColumn(object sender, DataGridAutoGeneratingColumnEventArgs e)
    {
        var style = new Style(typeof(DataGridColumnHeader));
        style.Setters.Add(new EventSetter(FrameworkElement.ContextMenuOpeningEvent,
            new ContextMenuEventHandler(ColumnHeader_ContextMenuOpening)));
        style.Setters.Add(new Setter(FrameworkElement.ContextMenuProperty, new ContextMenu()));
        e.Column.HeaderStyle = style;
    }

    private void ColumnHeader_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (sender is not DataGridColumnHeader header) return;
        string colName = header.Content?.ToString() ?? "";
        bool hasSelection = _selectedRows.Count > 0;

        var menu = new ContextMenu();

        var copyName = new MenuItem { Header = "Copy column name" };
        copyName.Click += (_, _) => Clipboard.SetText(colName);
        menu.Items.Add(copyName);

        var copyAll = new MenuItem { Header = "Copy column data" };
        copyAll.Click += (_, _) => CopyColumnData(colName, false);
        menu.Items.Add(copyAll);

        var copySel = new MenuItem { Header = "Copy column data (selected rows)", IsEnabled = hasSelection };
        copySel.Click += (_, _) => CopyColumnData(colName, true);
        menu.Items.Add(copySel);

        var copyRows = new MenuItem { Header = "Copy all selected rows", IsEnabled = hasSelection };
        copyRows.Click += (_, _) => CopySelectedToClipboard();
        menu.Items.Add(copyRows);

        header.ContextMenu = menu;
    }

    private void CopyColumnData(string colName, bool selectedOnly)
    {
        if (_data == null) return;
        var sb = new StringBuilder();
        sb.AppendLine(colName);
        if (selectedOnly)
        {
            foreach (int idx in _selectedRows.OrderBy(i => i))
                if (idx < _data.Rows.Count)
                    sb.AppendLine(_data.Rows[idx][colName]?.ToString() ?? "");
        }
        else
        {
            if (dgSelected.ItemsSource is DataView dv)
                foreach (DataRowView drv in dv)
                    sb.AppendLine(drv[colName]?.ToString() ?? "");
        }
        Clipboard.SetText(sb.ToString());
    }

    private void DgSelected_Sorting(object sender, DataGridSortingEventArgs e)
    {
        string colName = e.Column.Header?.ToString() ?? "";
        if (_data == null || !IsNumericColumn(colName)) return;

        e.Handled = true;

        var direction = (e.Column.SortDirection == ListSortDirection.Ascending)
            ? ListSortDirection.Descending
            : ListSortDirection.Ascending;
        e.Column.SortDirection = direction;

        foreach (var col in dgSelected.Columns)
            if (col != e.Column) col.SortDirection = null;

        if (dgSelected.ItemsSource is DataView dv && dv.Table != null)
        {
            var rows = dv.Table.Rows.Cast<DataRow>().ToList();
            rows.Sort((a, b) =>
            {
                bool ap = double.TryParse(a[colName]?.ToString() ?? "", NumberStyles.Any,
                    CultureInfo.InvariantCulture, out double da);
                bool bp = double.TryParse(b[colName]?.ToString() ?? "", NumberStyles.Any,
                    CultureInfo.InvariantCulture, out double db);
                if (!ap && !bp) return 0;
                if (!ap) return 1;
                if (!bp) return -1;
                int cmp = da.CompareTo(db);
                return direction == ListSortDirection.Descending ? -cmp : cmp;
            });

            var sorted = dv.Table.Clone();
            foreach (var row in rows) sorted.ImportRow(row);

            double hOffset = 0;
            var sv = GetDataGridScrollViewer(dgSelected);
            if (sv != null) hOffset = sv.HorizontalOffset;

            _updating = true;
            dgSelected.ItemsSource = sorted.DefaultView;
            _updating = false;

            if (sv != null && hOffset > 0)
                dgSelected.Dispatcher.BeginInvoke(() =>
                {
                    var sv2 = GetDataGridScrollViewer(dgSelected);
                    sv2?.ScrollToHorizontalOffset(hOffset);
                }, System.Windows.Threading.DispatcherPriority.Loaded);
        }
    }

    // ====================================================================
    //  Clipboard
    // ====================================================================
    private void BtnCopy_Click(object sender, RoutedEventArgs e) => CopySelectedToClipboard();
    private void MenuCopySelected_Click(object sender, RoutedEventArgs e) => CopySelectedToClipboard();

    private void CopySelectedToClipboard()
    {
        if (_data == null || _selectedRows.Count == 0) return;
        var sb = new StringBuilder();
        sb.AppendLine(string.Join("\t", _columns!));
        foreach (int idx in _selectedRows.OrderBy(i => i))
        {
            if (idx >= _data.Rows.Count) continue;
            var row = _data.Rows[idx];
            sb.AppendLine(string.Join("\t", _columns!.Select(c => row[c].ToString())));
        }
        Clipboard.SetText(sb.ToString());
        txtSelCount.Text = $"({_selectedRows.Count} rows — copied!)";
    }

    // ====================================================================
    //  Copy R Code
    // ====================================================================
    private void MenuCopyRCode_Click(object sender, RoutedEventArgs e)
    {
        if (_data == null || _columns == null || _filePath == null) return;
        string code = GenerateRCode();
        Clipboard.SetText(code);
        MessageBox.Show("R code copied to clipboard.", "Copy R Code", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private string GenerateRCode()
    {
        var sb = new StringBuilder();
        sb.AppendLine("library(ggplot2)");
        sb.AppendLine();

        // --- Read data ---
        if (_dataModified)
        {
            // Data was modified (rows deleted) — embed current data inline
            sb.AppendLine("# Data modified in-app (rows deleted); embedding current data");
            sb.AppendLine("data <- read.delim(text = paste(");
            sb.AppendLine($"  \"{string.Join("\\t", _columns!.Select(c => REscape(c)))}\",");
            int rowCount = _data!.Rows.Count;
            for (int ri = 0; ri < rowCount; ri++)
            {
                var row = _data.Rows[ri];
                string vals = string.Join("\\t", _columns!.Select(c =>
                {
                    string v = row[c]?.ToString() ?? "NA";
                    return REscape(v);
                }));
                string comma = ri < rowCount - 1 ? "," : "";
                sb.AppendLine($"  \"{vals}\"{comma}");
            }
            sb.AppendLine("  , sep = \"\\n\"), sep = \"\\t\", stringsAsFactors = FALSE, na.strings = c(\"NA\", \"N/A\", \"NaN\", \".\", \"null\", \"-\", \"?\"))");
        }
        else
        {
            string rPath = _filePath!.Replace("\\", "/");
            string sepR = _fileDelimiter switch
            {
                '\t' => "\\t",
                ',' => ",",
                ';' => ";",
                ' ' => " ",
                _ => "\\t"
            };
            sb.AppendLine($"data <- read.delim(\"{rPath}\", sep = \"{sepR}\", stringsAsFactors = FALSE, na.strings = c(\"NA\", \"N/A\", \"NaN\", \".\", \"null\", \"-\", \"?\"))");
        }
        sb.AppendLine();

        // --- Filters ---
        bool hasFilters = false;
        foreach (var (col, (fmin, fmax)) in _numericFilters)
        {
            // Check if the filter is actually narrower than the data range
            var vals = NumericValues(col);
            if (vals.Length == 0) continue;
            double dataMin = vals.Min(), dataMax = vals.Max();
            if (fmin > dataMin)
            {
                sb.AppendLine($"data <- data[is.na(data${RName(col)}) | data${RName(col)} >= {fmin.ToString("G", CultureInfo.InvariantCulture)}, ]");
                hasFilters = true;
            }
            if (fmax < dataMax)
            {
                sb.AppendLine($"data <- data[is.na(data${RName(col)}) | data${RName(col)} <= {fmax.ToString("G", CultureInfo.InvariantCulture)}, ]");
                hasFilters = true;
            }
        }
        foreach (var (col, allowed) in _categoricalFilters)
        {
            var allCats = _data!.Rows.Cast<DataRow>().Select(r => r[col].ToString()!).Distinct().ToArray();
            if (allowed.Count < allCats.Length)
            {
                var quoted = allowed.Select(v => $"\"{REscape(v)}\"");
                sb.AppendLine($"data <- data[data${RName(col)} %in% c({string.Join(", ", quoted)}), ]");
                hasFilters = true;
            }
        }
        if (hasFilters) sb.AppendLine();

        // --- Aesthetics ---
        string xCol = cboX.SelectedItem?.ToString() ?? _columns![0];
        string yCol = cboY.SelectedItem?.ToString() ?? (_columns!.Length > 1 ? _columns[1] : _columns[0]);

        string? colorCol = cboColor.SelectedItem?.ToString();
        bool useColorCol = colorCol != null && colorCol != "(none)" && _data!.Columns.Contains(colorCol);

        string? shapeCol = cboShapeCol.SelectedItem?.ToString();
        bool useShapeCol = shapeCol != null && shapeCol != "(none)" && _data!.Columns.Contains(shapeCol);

        string? sizeCol = cboSizeCol.SelectedItem?.ToString();
        bool useSizeCol = sizeCol != null && sizeCol != "(none)" && _data!.Columns.Contains(sizeCol);

        // Ensure categorical columns used as color/shape are factors
        if (useColorCol && !IsNumericColumn(colorCol!))
            sb.AppendLine($"data${RName(colorCol!)} <- as.factor(data${RName(colorCol!)})");
        if (useShapeCol && !IsNumericColumn(shapeCol!))
            sb.AppendLine($"data${RName(shapeCol!)} <- as.factor(data${RName(shapeCol!)})");

        // Build aes() string
        var aes = new List<string>
        {
            $"x = `{xCol}`",
            $"y = `{yCol}`"
        };
        if (useColorCol) aes.Add($"colour = `{colorCol}`");
        if (useShapeCol) aes.Add($"shape = `{shapeCol}`");
        if (useSizeCol) aes.Add($"size = `{sizeCol}`");

        sb.AppendLine();
        sb.AppendLine($"p <- ggplot(data, aes({string.Join(", ", aes)})) +");

        // --- geom_point ---
        var pointArgs = new List<string>();
        if (!useColorCol)
        {
            int idx = Math.Max(0, cboPresetColor.SelectedIndex);
            pointArgs.Add($"colour = \"{PresetColors[idx].Hex}\"");
        }
        if (!useShapeCol)
        {
            int shapeIdx = Math.Max(0, cboShape.SelectedIndex);
            int rShape = shapeIdx switch
            {
                0 => 16, // FilledCircle
                1 => 15, // FilledSquare
                2 => 18, // FilledDiamond
                3 => 17, // FilledTriangleUp
                4 => 25, // FilledTriangleDown
                5 => 8,  // Asterisk
                6 => 3,  // Cross (+)
                7 => 4,  // Eks (X)
                _ => 16
            };
            pointArgs.Add($"shape = {rShape}");
        }
        if (!useSizeCol)
            pointArgs.Add($"size = {(sliderSize.Value / 3.0).ToString("F1", CultureInfo.InvariantCulture)}");

        string pointArgsStr = pointArgs.Count > 0 ? $", {string.Join(", ", pointArgs)}" : "";
        sb.AppendLine($"  geom_point({pointArgsStr.TrimStart(',', ' ')}) +");

        // --- Colour scale ---
        if (useColorCol && IsNumericColumn(colorCol!))
        {
            string cLow = chkReverseColor.IsChecked == true ? "red" : "blue";
            string cHigh = chkReverseColor.IsChecked == true ? "blue" : "red";
            string cTrans = chkLogColor.IsChecked == true ? ", trans = \"log10\"" : "";
            sb.AppendLine($"  scale_colour_gradient(low = \"{cLow}\", high = \"{cHigh}\"{cTrans}) +");
        }
        else if (useColorCol && _categoryColorOverrides.Count > 0)
        {
            // Apply custom colour overrides
            var cats = _data!.Rows.Cast<DataRow>().Select(r => r[colorCol!].ToString()!).Distinct().ToArray();
            var colorValues = new List<string>();
            var palette = new ScottPlot.Palettes.Category10();
            int palIdx = 0;
            foreach (var cat in cats)
            {
                if (_categoryColorOverrides.TryGetValue(cat, out string? hex))
                    colorValues.Add($"\"{REscape(cat)}\" = \"{hex}\"");
                else
                {
                    var pc = palette.GetColor(palIdx);
                    colorValues.Add($"\"{REscape(cat)}\" = \"#{pc.Red:X2}{pc.Green:X2}{pc.Blue:X2}\"");
                    palIdx++;
                }
            }
            sb.AppendLine($"  scale_colour_manual(values = c({string.Join(", ", colorValues)})) +");
        }

        // --- Size scale ---
        if (useSizeCol && (chkLogSize.IsChecked == true || chkReverseSize.IsChecked == true))
        {
            var sizeArgs = new List<string>();
            if (chkLogSize.IsChecked == true) sizeArgs.Add("trans = \"log10\"");
            if (chkReverseSize.IsChecked == true) sizeArgs.Add("range = c(6, 1)");
            sb.AppendLine($"  scale_size_continuous({string.Join(", ", sizeArgs)}) +");
        }

        // --- Log scales ---
        bool logX = chkLogX.IsChecked == true;
        bool logY = chkLogY.IsChecked == true;
        if (logX) sb.AppendLine("  scale_x_log10() +");
        if (logY) sb.AppendLine("  scale_y_log10() +");

        // --- Identity line ---
        if (_showIdentityLine)
        {
            string idCol = $"\"#{_identityLineColor.Red:X2}{_identityLineColor.Green:X2}{_identityLineColor.Blue:X2}\"";
            string idLty = _identityLineDashed ? "\"dashed\"" : "\"solid\"";
            sb.AppendLine($"  geom_abline(intercept = 0, slope = 1, colour = {idCol}, linewidth = {(_identityLineWidth / 2.0).ToString("F1", CultureInfo.InvariantCulture)}, linetype = {idLty}) +");
        }

        // --- Regression ---
        if (_regressionCoeffs != null && _regressionPredictors?.Length == 1 && _regressionResponse != null)
        {
            string regCol = $"\"#{_regLineColor.Red:X2}{_regLineColor.Green:X2}{_regLineColor.Blue:X2}\"";
            string regLty = _regLineDashed ? "\"dashed\"" : "\"solid\"";
            string regLw = (_regLineWidth / 2.0).ToString("F1", CultureInfo.InvariantCulture);
            string rPred = RName(_regressionPredictors[0]);
            string rResp = RName(_regressionResponse);

            if (_regressionPredictors[0] == xCol && _regressionResponse == yCol
                && !_regressionLogResponse && !_regressionLogPredictors)
            {
                if (_regressionOrder == 1)
                {
                    sb.AppendLine($"  geom_smooth(method = \"lm\", formula = y ~ x, se = FALSE, colour = {regCol}, linewidth = {regLw}, linetype = {regLty}) +");
                }
                else
                {
                    sb.AppendLine($"  geom_smooth(method = \"lm\", formula = y ~ poly(x, {_regressionOrder}, raw = TRUE), se = FALSE, colour = {regCol}, linewidth = {regLw}, linetype = {regLty}) +");
                }
            }
            else if (_regressionPredictors[0] == xCol && _regressionResponse == yCol)
            {
                // Log transforms applied at regression level — draw from stored coefficients
                string predExpr = _regressionLogPredictors ? "log10(x)" : "x";
                var terms = new StringBuilder($"{_regressionCoeffs[0].ToString("G6", CultureInfo.InvariantCulture)}");
                for (int i = 1; i < _regressionCoeffs.Length; i++)
                {
                    string coeff = _regressionCoeffs[i].ToString("G6", CultureInfo.InvariantCulture);
                    string power = i > 1 ? $"^{i}" : "";
                    terms.Append($" + {coeff} * {predExpr}{power}");
                }
                string formula = terms.ToString();
                if (_regressionLogResponse)
                    sb.AppendLine($"  stat_function(fun = function(x) 10^({formula}), colour = {regCol}, linewidth = {regLw}, linetype = {regLty}) +");
                else
                    sb.AppendLine($"  stat_function(fun = function(x) {formula}, colour = {regCol}, linewidth = {regLw}, linetype = {regLty}) +");
            }
        }

        // --- Point labels ---
        string? labelCol = cboLabelCol.SelectedItem?.ToString();
        bool useLabelCol = labelCol != null && labelCol != "(none)" && _data!.Columns.Contains(labelCol);
        if (useLabelCol)
        {
            string labelHex = $"#{_labelFontColor.Red:X2}{_labelFontColor.Green:X2}{_labelFontColor.Blue:X2}";
            double labelSizePt = _labelFontSize / 2.83; // px to pt approximation
            sb.AppendLine($"  geom_text(aes(label = `{labelCol}`), size = {labelSizePt.ToString("F1", CultureInfo.InvariantCulture)}, colour = \"{labelHex}\", hjust = 0, nudge_x = 0.02, check_overlap = TRUE) +");
        }

        // --- Axis labels ---
        sb.AppendLine($"  labs(x = \"{REscape(xCol)}\", y = \"{REscape(yCol)}\") +");

        // --- Axis limits (from current view) ---
        var limits = wpfPlot.Plot.Axes.GetLimits();
        if (logX)
        {
            double xLo = Math.Pow(10, limits.Left);
            double xHi = Math.Pow(10, limits.Right);
            sb.AppendLine($"  coord_cartesian(xlim = c({xLo.ToString("G6", CultureInfo.InvariantCulture)}, {xHi.ToString("G6", CultureInfo.InvariantCulture)}),");
        }
        else
        {
            sb.AppendLine($"  coord_cartesian(xlim = c({limits.Left.ToString("G6", CultureInfo.InvariantCulture)}, {limits.Right.ToString("G6", CultureInfo.InvariantCulture)}),");
        }
        if (logY)
        {
            double yLo = Math.Pow(10, limits.Bottom);
            double yHi = Math.Pow(10, limits.Top);
            sb.AppendLine($"                  ylim = c({yLo.ToString("G6", CultureInfo.InvariantCulture)}, {yHi.ToString("G6", CultureInfo.InvariantCulture)})) +");
        }
        else
        {
            sb.AppendLine($"                  ylim = c({limits.Bottom.ToString("G6", CultureInfo.InvariantCulture)}, {limits.Top.ToString("G6", CultureInfo.InvariantCulture)})) +");
        }

        // --- Inverted axes ---
        bool invertX = chkInvertX.IsChecked == true;
        bool invertY = chkInvertY.IsChecked == true;

        // --- Theme ---
        sb.AppendLine("  theme_minimal()");

        // Axis direction (append after theme)
        if (invertX || invertY)
        {
            sb.AppendLine();
            sb.Append("p <- p +");
            if (invertX) sb.Append(" scale_x_reverse()");
            if (invertX && invertY) sb.Append(" +");
            if (invertY) sb.Append(" scale_y_reverse()");
            sb.AppendLine();
        }

        sb.AppendLine();
        sb.AppendLine("print(p)");

        // --- Standalone lm() model ---
        if (_regressionCoeffs != null && _regressionPredictors != null && _regressionResponse != null)
        {
            sb.AppendLine();
            string rResp2 = RName(_regressionResponse);
            if (_regressionPredictors.Length == 1)
            {
                string rPred2 = RName(_regressionPredictors[0]);
                string lmResp = _regressionLogResponse ? $"log10(data${rResp2})" : $"data${rResp2}";
                string lmPred = _regressionLogPredictors ? $"log10(data${rPred2})" : $"data${rPred2}";
                if (_regressionOrder == 1)
                    sb.AppendLine($"model <- lm({lmResp} ~ {lmPred})");
                else
                    sb.AppendLine($"model <- lm({lmResp} ~ poly({lmPred}, {_regressionOrder}, raw = TRUE))");
            }
            else
            {
                // Multivariate
                string lmResp = _regressionLogResponse ? $"log10({RName(_regressionResponse)})" : RName(_regressionResponse);
                var lmPreds = _regressionPredictors.Select(pr =>
                    _regressionLogPredictors ? $"log10({RName(pr)})" : RName(pr));
                sb.AppendLine($"model <- lm({lmResp} ~ {string.Join(" + ", lmPreds)}, data = data)");
            }
            sb.AppendLine("summary(model)");
        }

        return sb.ToString();
    }

    /// <summary>Wrap column name for R: backtick names with spaces/special chars.</summary>
    private static string RName(string col) => col.Contains(' ') || col.Contains('.') || col.Contains('-') ? $"`{col}`" : col;

    /// <summary>Escape for R strings (backslash and double quote).</summary>
    private static string REscape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");

    // ====================================================================
    //  Control event handlers
    // ====================================================================
    private void Axis_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_updating) return;
        UpdateAxisRangeBoxes();
        // Preserve selection — row indices stay valid since data hasn't changed
        UpdatePlot();
        UpdateSelectionDisplay();
    }

    private void Appearance_Changed(object sender, RoutedEventArgs e)
    {
        if (_updating) return;
        UpdatePlot(true);
    }

    private void Appearance_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_updating) return;
        // When a colour-by column is chosen, deselect single colour preset
        if (sender == cboColor && cboColor.SelectedIndex > 0)
        {
            _updating = true;
            cboPresetColor.SelectedIndex = -1;
            _updating = false;
        }
        UpdatePlot(true);
    }

    private void InvertAxis_Changed(object sender, RoutedEventArgs e)
    {
        if (_updating) return;
        UpdatePlot(); // no preserveView — need ApplyAxisRange to run the inversion
    }

    private void LogScale_Changed(object sender, RoutedEventArgs e)
    {
        if (_updating) return;
        UpdateAxisRangeBoxes();
        UpdatePlot();
    }

    private void PresetColor_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_updating || cboPresetColor.SelectedIndex < 0) return;
        int newIdx = cboPresetColor.SelectedIndex;
        string hex = PresetColors[newIdx].Hex;

        // Apply colour to selected points if "Colour selected" is checked
        if (chkColorSelected.IsChecked == true && _selectedRows.Count > 0)
        {
            var color = ParseHexColor(hex);
            foreach (int i in _selectedRows)
                _pointColorOverrides[i] = color;
            // Restore the preset to what it was so the base colour doesn't change
            _updating = true;
            cboPresetColor.SelectedIndex = _savedPresetIndex;
            string savedHex = PresetColors[_savedPresetIndex].Hex;
            rectColor.Fill = new SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(savedHex));
            _updating = false;
            UpdatePlot(true);
            return;
        }

        // Normal path: update swatch and remember this as the base
        _savedPresetIndex = newIdx;
        rectColor.Fill = new SolidColorBrush(
            (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex));
        // Reset "Colour by column" so single colour takes effect
        if (cboColor.SelectedIndex > 0)
            cboColor.SelectedIndex = 0; // triggers Appearance_Changed → UpdatePlot
        else
            UpdatePlot(true);
    }

    private void RectColor_Click(object sender, MouseButtonEventArgs e)
    {
        cboPresetColor.SelectedIndex = (cboPresetColor.SelectedIndex + 1) % PresetColors.Length;
    }

    private void BtnLabelFont_Click(object sender, RoutedEventArgs e)
    {
        // Save originals for cancel
        string origFamily = _labelFontFamily;
        float origSize = _labelFontSize;
        ScottPlot.Color origColor = _labelFontColor;

        var dlg = new Window
        {
            Title = "Label Font Properties",
            Width = 300, Height = 240,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            ResizeMode = ResizeMode.NoResize
        };

        var stack = new StackPanel { Margin = new Thickness(12) };

        // Font family
        stack.Children.Add(new TextBlock { Text = "Font family", FontSize = 11, Foreground = System.Windows.Media.Brushes.Gray });
        var cboFont = new ComboBox { FontSize = 12, Margin = new Thickness(0, 0, 0, 8) };
        foreach (var ff in System.Windows.Media.Fonts.SystemFontFamilies.OrderBy(f => f.Source))
            cboFont.Items.Add(ff.Source);
        cboFont.SelectedItem = _labelFontFamily;
        if (cboFont.SelectedItem == null) cboFont.SelectedIndex = 0;
        cboFont.SelectionChanged += (s, ev) =>
        {
            _labelFontFamily = cboFont.SelectedItem?.ToString() ?? "Segoe UI";
            UpdatePlot(true);
        };
        stack.Children.Add(cboFont);

        // Font size
        stack.Children.Add(new TextBlock { Text = "Font size", FontSize = 11, Foreground = System.Windows.Media.Brushes.Gray });
        var spSize = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        var sliderFontSize = new Slider { Minimum = 6, Maximum = 30, Value = _labelFontSize, Width = 180, TickFrequency = 1, IsSnapToTickEnabled = true };
        var txtFontSize = new TextBlock { VerticalAlignment = System.Windows.VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };
        txtFontSize.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("Value") { Source = sliderFontSize, StringFormat = "{0:0}" });
        sliderFontSize.ValueChanged += (s, ev) =>
        {
            _labelFontSize = (float)sliderFontSize.Value;
            UpdatePlot(true);
        };
        spSize.Children.Add(sliderFontSize);
        spSize.Children.Add(txtFontSize);
        stack.Children.Add(spSize);

        // Font colour
        stack.Children.Add(new TextBlock { Text = "Font colour", FontSize = 11, Foreground = System.Windows.Media.Brushes.Gray });
        var colorPanel = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
        var colorOptions = new (string Name, string Hex)[]
        {
            ("Black", "#000000"), ("Dark Grey", "#404040"), ("Grey", "#808080"),
            ("Red", "#D62728"), ("Blue", "#1F77B4"), ("Green", "#2CA02C"),
            ("Orange", "#FF7F0E"), ("Purple", "#9467BD"), ("Brown", "#8C564B"),
            ("Pink", "#E377C2"), ("Cyan", "#17BECF"), ("White", "#FFFFFF"),
        };
        System.Windows.Shapes.Rectangle? selectedSwatch = null;
        string currentHex = $"#{_labelFontColor.Red:X2}{_labelFontColor.Green:X2}{_labelFontColor.Blue:X2}";
        foreach (var (name, hex) in colorOptions)
        {
            var swatch = new System.Windows.Shapes.Rectangle
            {
                Width = 22, Height = 22, Margin = new Thickness(2),
                Fill = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex)),
                Stroke = hex.Equals(currentHex, StringComparison.OrdinalIgnoreCase)
                    ? System.Windows.Media.Brushes.Blue : System.Windows.Media.Brushes.LightGray,
                StrokeThickness = hex.Equals(currentHex, StringComparison.OrdinalIgnoreCase) ? 2.5 : 1,
                RadiusX = 3, RadiusY = 3,
                Cursor = Cursors.Hand,
                Tag = hex,
                ToolTip = name
            };
            if (hex.Equals(currentHex, StringComparison.OrdinalIgnoreCase))
                selectedSwatch = swatch;
            swatch.MouseLeftButtonUp += (s, ev) =>
            {
                if (selectedSwatch != null)
                {
                    selectedSwatch.Stroke = System.Windows.Media.Brushes.LightGray;
                    selectedSwatch.StrokeThickness = 1;
                }
                var r = (System.Windows.Shapes.Rectangle)s!;
                r.Stroke = System.Windows.Media.Brushes.Blue;
                r.StrokeThickness = 2.5;
                selectedSwatch = r;
                _labelFontColor = ParseHexColor((string)r.Tag);
                UpdatePlot(true);
            };
            colorPanel.Children.Add(swatch);
        }
        stack.Children.Add(colorPanel);

        // Close button
        var btnPanel = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Right };
        var btnClose = new Button { Content = "Close", Padding = new Thickness(16, 4, 16, 4), IsDefault = true };
        btnClose.Click += (s, ev) => dlg.Close();
        btnPanel.Children.Add(btnClose);
        stack.Children.Add(btnPanel);

        dlg.Content = stack;
        dlg.ShowDialog();
    }

    private void AxisRange_Changed(object sender, RoutedEventArgs e)
    {
        if (_updating) return;
        UpdatePlot();
    }

    // ====================================================================
    //  Linear Regression
    // ====================================================================
    private void BtnFitModel_Click(object sender, RoutedEventArgs e) => ShowFitModelDialog();

    private void ShowFitModelDialog()
    {
        if (_data == null || _columns == null) return;

        var numericCols = _columns.Where(c => IsNumericColumn(c)).ToArray();
        if (numericCols.Length < 2)
        {
            MessageBox.Show("Need at least 2 numeric columns for regression.");
            return;
        }

        var dialog = new Window
        {
            Title = "Fit Linear Model",
            Width = 360, Height = 520,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            ResizeMode = ResizeMode.NoResize
        };

        var mainStack = new StackPanel { Margin = new Thickness(15) };

        // Response variable
        mainStack.Children.Add(new TextBlock { Text = "Response (Y):", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
        var cboResponse = new ComboBox { Margin = new Thickness(0, 0, 0, 10) };
        foreach (var c in numericCols) cboResponse.Items.Add(c);
        string currentY = cboY.SelectedItem?.ToString() ?? "";
        cboResponse.SelectedItem = numericCols.Contains(currentY) ? currentY : numericCols[0];
        mainStack.Children.Add(cboResponse);

        // Model type
        mainStack.Children.Add(new TextBlock { Text = "Model type:", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
        var radioSimple = new RadioButton { Content = "Simple (one predictor)", IsChecked = true, Margin = new Thickness(0, 0, 0, 2) };
        var radioMulti = new RadioButton { Content = "Multivariate", Margin = new Thickness(0, 0, 0, 10) };
        mainStack.Children.Add(radioSimple);
        mainStack.Children.Add(radioMulti);

        // Simple predictor
        var simplePanel = new StackPanel();
        simplePanel.Children.Add(new TextBlock { Text = "Predictor (X):", Margin = new Thickness(0, 0, 0, 4) });
        var cboPredictor = new ComboBox { Margin = new Thickness(0, 0, 0, 10) };
        foreach (var c in numericCols) cboPredictor.Items.Add(c);
        string currentX = cboX.SelectedItem?.ToString() ?? "";
        cboPredictor.SelectedItem = numericCols.Contains(currentX) ? currentX : numericCols[0];
        simplePanel.Children.Add(cboPredictor);

        // Polynomial order
        var orderPanel = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
        orderPanel.Children.Add(new TextBlock { Text = "Polynomial order:", VerticalAlignment = System.Windows.VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
        var txtOrder = new TextBox { Text = "1", Width = 40, VerticalContentAlignment = System.Windows.VerticalAlignment.Center };
        orderPanel.Children.Add(txtOrder);
        simplePanel.Children.Add(orderPanel);

        mainStack.Children.Add(simplePanel);

        // Multi predictors
        var multiPanel = new StackPanel { Visibility = Visibility.Collapsed };
        multiPanel.Children.Add(new TextBlock { Text = "Predictors:", Margin = new Thickness(0, 0, 0, 4) });
        var predictorScroll = new ScrollViewer { MaxHeight = 150, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var predictorStack = new StackPanel();
        var predictorCheckboxes = new List<CheckBox>();
        foreach (var c in numericCols)
        {
            var cb = new CheckBox
            {
                Content = c, FontSize = 12,
                IsChecked = c != (cboResponse.SelectedItem?.ToString() ?? ""),
                IsEnabled = c != (cboResponse.SelectedItem?.ToString() ?? ""),
                Margin = new Thickness(0, 1, 0, 1)
            };
            predictorCheckboxes.Add(cb);
            predictorStack.Children.Add(cb);
        }
        predictorScroll.Content = predictorStack;
        multiPanel.Children.Add(predictorScroll);
        mainStack.Children.Add(multiPanel);

        // Log transform options — pre-check from current axis log state
        mainStack.Children.Add(new TextBlock { Text = "Transform:", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
        var chkLogResponse = new CheckBox { Content = "Log\u2081\u2080 response (Y)", Margin = new Thickness(0, 0, 0, 2), IsChecked = chkLogY.IsChecked == true };
        var chkLogPredictors = new CheckBox { Content = "Log\u2081\u2080 predictor(s)", Margin = new Thickness(0, 0, 0, 10), IsChecked = chkLogX.IsChecked == true };
        mainStack.Children.Add(chkLogResponse);
        mainStack.Children.Add(chkLogPredictors);

        // Toggle visibility
        radioSimple.Checked += (s, ev) => { simplePanel.Visibility = Visibility.Visible; multiPanel.Visibility = Visibility.Collapsed; };
        radioMulti.Checked += (s, ev) => { simplePanel.Visibility = Visibility.Collapsed; multiPanel.Visibility = Visibility.Visible; };

        // Update predictor checkboxes when response changes
        cboResponse.SelectionChanged += (s, ev) =>
        {
            string resp = cboResponse.SelectedItem?.ToString() ?? "";
            foreach (var cb in predictorCheckboxes)
            {
                string colName = cb.Content.ToString()!;
                cb.IsEnabled = colName != resp;
                if (colName == resp) cb.IsChecked = false;
            }
        };

        // Data scope selector
        int nSelected = _selectedRows.Count;
        int nVisible = _filteredRows?.Count ?? _data.Rows.Count;
        int nAll = _data.Rows.Count;
        mainStack.Children.Add(new TextBlock { Text = "Data:", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
        var cboDataScope = new ComboBox { Margin = new Thickness(0, 0, 0, 10) };
        cboDataScope.Items.Add(new ComboBoxItem { Content = $"Selected points ({nSelected})", Tag = "selected", IsEnabled = nSelected > 0 });
        cboDataScope.Items.Add(new ComboBoxItem { Content = $"All visible points ({nVisible})", Tag = "visible" });
        cboDataScope.Items.Add(new ComboBoxItem { Content = $"All data ({nAll})", Tag = "all" });
        cboDataScope.SelectedIndex = nSelected > 0 ? 0 : 1;
        mainStack.Children.Add(cboDataScope);

        // Buttons
        var buttonPanel = new StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right
        };
        var btnOK = new Button { Content = "OK", Width = 80, Padding = new Thickness(0, 4, 0, 4), IsDefault = true };
        var btnCancel = new Button { Content = "Cancel", Width = 80, Padding = new Thickness(0, 4, 0, 4), Margin = new Thickness(8, 0, 0, 0), IsCancel = true };
        buttonPanel.Children.Add(btnOK);
        buttonPanel.Children.Add(btnCancel);
        mainStack.Children.Add(buttonPanel);

        btnOK.Click += (s, ev) => { dialog.DialogResult = true; };
        btnCancel.Click += (s, ev) => { dialog.DialogResult = false; };

        dialog.Content = mainStack;

        if (dialog.ShowDialog() != true) return;

        // Gather settings
        string responseCol = cboResponse.SelectedItem.ToString()!;
        bool isSimple = radioSimple.IsChecked == true;

        // Get row indices based on data scope selection
        string dataScope = (cboDataScope.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "visible";
        int[] rowIndices;
        if (dataScope == "selected")
            rowIndices = _selectedRows.OrderBy(i => i).ToArray();
        else if (dataScope == "all")
            rowIndices = Enumerable.Range(0, _data.Rows.Count).ToArray();
        else // "visible"
            rowIndices = Enumerable.Range(0, _data.Rows.Count).Where(i => IsRowVisible(i)).ToArray();

        bool logY = chkLogResponse.IsChecked == true;
        bool logPreds = chkLogPredictors.IsChecked == true;

        if (isSimple)
        {
            string predictorCol = cboPredictor.SelectedItem.ToString()!;
            int polyOrder = 1;
            if (int.TryParse(txtOrder.Text.Trim(), out int parsed) && parsed >= 1 && parsed <= 10)
                polyOrder = parsed;
            ComputeAndShowRegression(new[] { predictorCol }, responseCol, rowIndices, logY, logPreds, polyOrder);
        }
        else
        {
            var selectedPredictors = predictorCheckboxes
                .Where(cb => cb.IsChecked == true && cb.IsEnabled)
                .Select(cb => cb.Content.ToString()!)
                .ToArray();

            if (selectedPredictors.Length == 0)
            {
                MessageBox.Show("Select at least one predictor.");
                return;
            }

            ComputeAndShowRegression(selectedPredictors, responseCol, rowIndices, logY, logPreds);
        }
    }

    private void ComputeAndShowRegression(string[] predictors, string response, int[] rowIndices,
        bool logResponse = false, bool logPredictors = false, int polyOrder = 1)
    {
        // For polynomial: single predictor expanded to x, x², x³, ...
        int p = predictors.Length == 1 ? polyOrder : predictors.Length;
        int pp = p + 1; // +1 for intercept

        // Pre-filter rows with missing data or non-positive values for log
        rowIndices = rowIndices.Where(ri =>
        {
            var r = _data!.Rows[ri];
            double yv = ParseDouble(r[response].ToString()!);
            if (double.IsNaN(yv)) return false;
            if (logResponse && yv <= 0) return false;
            foreach (var pred in predictors)
            {
                double xv = ParseDouble(r[pred].ToString()!);
                if (double.IsNaN(xv)) return false;
                if (logPredictors && xv <= 0) return false;
            }
            return true;
        }).ToArray();

        int n = rowIndices.Length;
        if (n <= pp)
        {
            MessageBox.Show($"Need more than {pp} observations for {pp} parameters (have {n}).");
            return;
        }

        // Build design matrix X (n x pp) and response vector y
        var X = new double[n, pp];
        var y = new double[n];
        bool isPoly = predictors.Length == 1 && polyOrder > 1;

        // For polynomial regression, center and scale the predictor to avoid ill-conditioning
        double polyMean = 0, polyStd = 1;
        if (isPoly)
        {
            var rawVals = new double[n];
            for (int i = 0; i < n; i++)
            {
                double v = ParseDouble(_data!.Rows[rowIndices[i]][predictors[0]].ToString()!);
                rawVals[i] = logPredictors ? Math.Log10(v) : v;
            }
            polyMean = rawVals.Average();
            double sumSq = rawVals.Sum(v => (v - polyMean) * (v - polyMean));
            polyStd = n > 1 ? Math.Sqrt(sumSq / (n - 1)) : 1;
            if (polyStd < 1e-15) polyStd = 1;
        }

        for (int i = 0; i < n; i++)
        {
            var row = _data!.Rows[rowIndices[i]];
            X[i, 0] = 1.0;
            if (isPoly)
            {
                double v = ParseDouble(row[predictors[0]].ToString()!);
                double xv = logPredictors ? Math.Log10(v) : v;
                double xs = (xv - polyMean) / polyStd; // centered and scaled
                for (int j = 1; j <= polyOrder; j++)
                    X[i, j] = Math.Pow(xs, j);
            }
            else
            {
                for (int j = 0; j < predictors.Length; j++)
                {
                    double v = ParseDouble(row[predictors[j]].ToString()!);
                    X[i, j + 1] = logPredictors ? Math.Log10(v) : v;
                }
            }
            double yv = ParseDouble(row[response].ToString()!);
            y[i] = logResponse ? Math.Log10(yv) : yv;
        }

        // X'X
        var XtX = new double[pp, pp];
        for (int i = 0; i < pp; i++)
            for (int j = i; j < pp; j++)
            {
                double s = 0;
                for (int k = 0; k < n; k++) s += X[k, i] * X[k, j];
                XtX[i, j] = XtX[j, i] = s;
            }

        // X'y
        var Xty = new double[pp];
        for (int i = 0; i < pp; i++)
        {
            double s = 0;
            for (int k = 0; k < n; k++) s += X[k, i] * y[k];
            Xty[i] = s;
        }

        // Invert X'X
        var inv = InvertMatrix(XtX, pp);
        if (inv == null)
        {
            MessageBox.Show("Singular matrix — check for collinear predictors.");
            return;
        }

        // beta = (X'X)^-1 * X'y
        var beta = new double[pp];
        for (int i = 0; i < pp; i++)
        {
            double s = 0;
            for (int j = 0; j < pp; j++) s += inv[i, j] * Xty[j];
            beta[i] = s;
        }

        // R-squared and residual variance
        double yMean = y.Average();
        double SSres = 0, SStot = 0;
        for (int i = 0; i < n; i++)
        {
            double yhat = 0;
            for (int j = 0; j < pp; j++) yhat += X[i, j] * beta[j];
            SSres += (y[i] - yhat) * (y[i] - yhat);
            SStot += (y[i] - yMean) * (y[i] - yMean);
        }
        double rSq = SStot > 0 ? 1.0 - SSres / SStot : 0;
        int df = n - pp;
        double sigma2 = df > 0 ? SSres / df : 0;

        // F-statistic for overall model
        double SSreg = SStot - SSres;
        int dfReg = p; // number of predictors (not including intercept)
        int dfRes = df; // n - pp
        double fStat = (dfReg > 0 && dfRes > 0 && SSres > 0)
            ? (SSreg / dfReg) / (SSres / dfRes) : 0;
        double pModel = (dfReg > 0 && dfRes > 0 && fStat > 0)
            ? FDistPValue(fStat, dfReg, dfRes) : 1;

        // Standard errors, t-statistics, p-values
        var se = new double[pp];
        var tStat = new double[pp];
        var pVal = new double[pp];
        for (int i = 0; i < pp; i++)
        {
            se[i] = Math.Sqrt(Math.Max(0, sigma2 * inv[i, i]));
            tStat[i] = se[i] > 1e-15 ? beta[i] / se[i] : 0;
            pVal[i] = df > 0 ? TDistPValue(Math.Abs(tStat[i]), df) : 1;
        }

        // Non-parametric correlations (simple linear regression only, not polynomial)
        double? spearmanRho = null, spearmanP = null, kendallTau = null, kendallP = null;
        if (predictors.Length == 1 && polyOrder == 1)
        {
            var xVals = new double[n];
            for (int i = 0; i < n; i++) xVals[i] = X[i, 1]; // predictor column
            (spearmanRho, spearmanP) = ComputeSpearmanRho(xVals, y);
            (kendallTau, kendallP) = ComputeKendallTau(xVals, y);
        }

        // Convert scaled polynomial coefficients back to raw x-space for line drawing
        // Scaled model: y = c0 + c1*s + c2*s² + ... where s = (x - mean) / std
        // We need raw coefficients: y = a0 + a1*x + a2*x² + ...
        double[] rawBeta = beta;
        if (isPoly)
        {
            rawBeta = new double[pp];
            // Expand (x - mean)^k / std^k using binomial theorem
            // coeff of x^j in sum_{k=0..order} beta[k] * ((x-mean)/std)^k
            for (int k = 0; k <= polyOrder; k++)
            {
                double bk = beta[k]; // coefficient for s^k
                // s^k = ((x - mean)/std)^k = sum_{j=0..k} C(k,j) * x^j * (-mean)^(k-j) / std^k
                for (int j = 0; j <= k; j++)
                {
                    double binom = BinomialCoeff(k, j);
                    double term = bk * binom * Math.Pow(-polyMean, k - j) / Math.Pow(polyStd, k);
                    rawBeta[j] += term;
                }
            }
        }

        // Store results
        _regressionCoeffs = rawBeta;
        _regressionPredictors = predictors;
        _regressionResponse = response;
        _regressionRSquared = rSq;
        _regressionSE = se;
        _regressionPValues = pVal;
        _regressionN = n;
        _regressionLogResponse = logResponse;
        _regressionLogPredictors = logPredictors;
        _regressionOrder = polyOrder;

        // Build display labels for predictors (polynomial terms get x², x³, etc.)
        string[] displayPredictors;
        if (isPoly)
        {
            string baseName = logPredictors ? $"log\u2081\u2080({predictors[0]})" : predictors[0];
            displayPredictors = new string[polyOrder];
            for (int i = 0; i < polyOrder; i++)
                displayPredictors[i] = i == 0 ? baseName : baseName + SuperscriptDigits(i + 1);
        }
        else
        {
            displayPredictors = predictors.Select(pr => logPredictors ? $"log\u2081\u2080({pr})" : pr).ToArray();
        }

        UpdateStatisticsPanel(displayPredictors, response, beta, se, tStat, pVal, rSq, n, df,
            logResponse, logPredictors, fStat, dfReg, dfRes, pModel,
            spearmanRho, spearmanP, kendallTau, kendallP);
        UpdatePlot(true);
    }

    private void UpdateStatisticsPanel(string[] predictors, string response,
        double[] beta, double[] se, double[] tStat, double[] pVal,
        double rSq, int n, int df, bool logResponse = false, bool logPredictors = false,
        double fStat = 0, int dfReg = 0, int dfRes = 0, double pModel = 1,
        double? spearmanRho = null, double? spearmanP = null,
        double? kendallTau = null, double? kendallP = null)
    {
        statsPanel.Children.Clear();

        string yLabel = logResponse ? $"log\u2081\u2080({response})" : response;
        // predictors array already contains formatted display labels (with log prefix / superscripts)
        string xLabels = string.Join(" + ", predictors);
        string modelDesc = $"{yLabel} ~ {xLabels}";

        statsPanel.Children.Add(new TextBlock
        {
            Text = modelDesc, FontWeight = FontWeights.SemiBold, FontSize = 11,
            TextWrapping = TextWrapping.Wrap
        });
        statsPanel.Children.Add(new TextBlock
        {
            Text = $"N = {n},  R\u00B2 = {rSq:F4},  df = {df}",
            FontSize = 10, Foreground = Brushes.Gray, Margin = new Thickness(0, 2, 0, 0)
        });

        // F-statistic line
        string pModelStr = pModel.ToString("G4");
        var fBlock = new TextBlock
        {
            Text = $"F({dfReg}, {dfRes}) = {fStat:F2},  p = {pModelStr}",
            FontSize = 10, Margin = new Thickness(0, 0, 0, 6)
        };
        fBlock.Foreground = pModel < 0.05
            ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 120, 0))
            : Brushes.Gray;
        statsPanel.Children.Add(fBlock);

        // Header
        statsPanel.Children.Add(new TextBlock
        {
            Text = string.Format("{0,-12} {1,10} {2,9} {3,7} {4,9}", "", "Estimate", "SE", "t", "p"),
            FontFamily = new FontFamily("Consolas"), FontSize = 10, Foreground = Brushes.Gray
        });

        // Intercept row
        AddCoeffRow("(Intercept)", beta[0], se[0], tStat[0], pVal[0]);

        // Predictor rows
        for (int i = 0; i < predictors.Length; i++)
            AddCoeffRow(predictors[i], beta[i + 1], se[i + 1], tStat[i + 1], pVal[i + 1]);

        // Non-parametric correlations (simple regression only)
        if (spearmanRho.HasValue && kendallTau.HasValue)
        {
            statsPanel.Children.Add(new TextBlock
            {
                Text = "\u2014 Non-parametric \u2014",
                FontSize = 10, Foreground = Brushes.Gray,
                Margin = new Thickness(0, 8, 0, 2)
            });

            string spPStr = spearmanP!.Value.ToString("G4");
            var spBlock = new TextBlock
            {
                Text = $"Spearman \u03C1 = {spearmanRho.Value:F4},  p = {spPStr}",
                FontFamily = new FontFamily("Consolas"), FontSize = 10
            };
            if (spearmanP.Value < 0.05)
                spBlock.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 120, 0));
            statsPanel.Children.Add(spBlock);

            string ktPStr = kendallP!.Value.ToString("G4");
            var ktBlock = new TextBlock
            {
                Text = $"Kendall \u03C4  = {kendallTau.Value:F4},  p = {ktPStr}",
                FontFamily = new FontFamily("Consolas"), FontSize = 10
            };
            if (kendallP.Value < 0.05)
                ktBlock.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 120, 0));
            statsPanel.Children.Add(ktBlock);
        }

        // --- Regression line style controls ---
        statsPanel.Children.Add(new TextBlock
        {
            Text = "\u2014 Line style \u2014",
            FontSize = 10, Foreground = Brushes.Gray,
            Margin = new Thickness(0, 8, 0, 2)
        });

        // Colour picker row
        var colorRow = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 0) };
        colorRow.Children.Add(new TextBlock { Text = "Colour:", FontSize = 10, VerticalAlignment = System.Windows.VerticalAlignment.Center, Width = 44 });
        var cboLineColor = new ComboBox { FontSize = 10, Width = 90 };
        var lineColors = new (string Name, byte R, byte G, byte B)[]
        {
            ("Red", 200, 30, 30), ("Blue", 30, 30, 200), ("Black", 0, 0, 0),
            ("Green", 0, 140, 0), ("Orange", 220, 120, 0), ("Purple", 130, 0, 180),
            ("Grey", 120, 120, 120)
        };
        int selColorIdx = 0;
        for (int ci = 0; ci < lineColors.Length; ci++)
        {
            var lc = lineColors[ci];
            var swatch = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
            swatch.Children.Add(new Border
            {
                Width = 12, Height = 12, Margin = new Thickness(0, 0, 4, 0),
                Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(lc.R, lc.G, lc.B))
            });
            swatch.Children.Add(new TextBlock { Text = lc.Name, VerticalAlignment = System.Windows.VerticalAlignment.Center });
            cboLineColor.Items.Add(new ComboBoxItem { Content = swatch, Tag = lc });
            if (_regLineColor.Red == lc.R && _regLineColor.Green == lc.G && _regLineColor.Blue == lc.B)
                selColorIdx = ci;
        }
        cboLineColor.SelectedIndex = selColorIdx;
        cboLineColor.SelectionChanged += (s, ev) =>
        {
            if (cboLineColor.SelectedItem is ComboBoxItem item && item.Tag is (string, byte r, byte g, byte b))
            {
                _regLineColor = new ScottPlot.Color(r, g, b);
                UpdatePlot(true);
            }
        };
        colorRow.Children.Add(cboLineColor);
        statsPanel.Children.Add(colorRow);

        // Width picker row
        var widthRow = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 0) };
        widthRow.Children.Add(new TextBlock { Text = "Width:", FontSize = 10, VerticalAlignment = System.Windows.VerticalAlignment.Center, Width = 44 });
        var cboLineWidth = new ComboBox { FontSize = 10, Width = 60 };
        float[] widths = [1, 1.5f, 2, 3, 4, 5];
        int selWidthIdx = 2; // default 2
        for (int wi = 0; wi < widths.Length; wi++)
        {
            cboLineWidth.Items.Add(new ComboBoxItem { Content = widths[wi].ToString("0.#") });
            if (Math.Abs(widths[wi] - _regLineWidth) < 0.01f) selWidthIdx = wi;
        }
        cboLineWidth.SelectedIndex = selWidthIdx;
        cboLineWidth.SelectionChanged += (s, ev) =>
        {
            if (cboLineWidth.SelectedIndex >= 0 && cboLineWidth.SelectedIndex < widths.Length)
            {
                _regLineWidth = widths[cboLineWidth.SelectedIndex];
                UpdatePlot(true);
            }
        };
        widthRow.Children.Add(cboLineWidth);
        statsPanel.Children.Add(widthRow);

        // Dash style row
        var dashRow = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 0) };
        dashRow.Children.Add(new TextBlock { Text = "Style:", FontSize = 10, VerticalAlignment = System.Windows.VerticalAlignment.Center, Width = 44 });
        var cboLineDash = new ComboBox { FontSize = 10, Width = 80 };
        cboLineDash.Items.Add(new ComboBoxItem { Content = "Solid" });
        cboLineDash.Items.Add(new ComboBoxItem { Content = "Dashed" });
        cboLineDash.SelectedIndex = _regLineDashed ? 1 : 0;
        cboLineDash.SelectionChanged += (s, ev) =>
        {
            _regLineDashed = cboLineDash.SelectedIndex == 1;
            UpdatePlot(true);
        };
        dashRow.Children.Add(cboLineDash);
        statsPanel.Children.Add(dashRow);

        AddIdentityLineControls();

        // Clear button
        var btnClear = new Button
        {
            Content = "Clear Model", Padding = new Thickness(8, 3, 8, 3),
            Margin = new Thickness(0, 8, 0, 0)
        };
        btnClear.Click += (s, e) => ClearRegressionModel();
        statsPanel.Children.Add(btnClear);
    }

    private void AddCoeffRow(string name, double est, double se, double t, double p)
    {
        string pStr = p.ToString("G4");
        string truncName = name.Length > 12 ? name.Substring(0, 11) + "\u2026" : name;
        var row = new TextBlock
        {
            Text = string.Format("{0,-12} {1,10:G5} {2,9:G4} {3,7:F2} {4,9}", truncName, est, se, t, pStr),
            FontFamily = new FontFamily("Consolas"), FontSize = 10
        };
        if (p < 0.05)
            row.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 120, 0));
        statsPanel.Children.Add(row);
    }

    // ====================================================================
    //  Multi-window support
    // ====================================================================
    private void MenuNewWindow_Click(object sender, RoutedEventArgs e)
    {
        if (_data == null || _columns == null) return;
        var w = new MainWindow();
        w._data = _data;
        w._columns = _columns;
        w._isLinkedChild = true;
        w.txtFileName.Text = txtFileName.Text + " (linked)";
        w.PopulateColumnCombos();

        // Share filter state by reference — child has no filter UI
        w._numericFilters = _numericFilters;
        w._categoricalFilters = _categoricalFilters;
        w._filteredRows = _filteredRows;
        w.filterSeparator.Visibility = Visibility.Collapsed;
        w.filterHeader.Visibility = Visibility.Collapsed;
        w.filterPanel.Visibility = Visibility.Collapsed;

        w.panelControls.IsEnabled = true;
        w.btnClose.IsEnabled = true;
        w.menuClose.IsEnabled = true;
        w.UpdatePlot();
        w.UpdateSelectionDisplay();
        w.Show();
    }

    private void BroadcastSelection()
    {
        foreach (var w in _allWindows)
        {
            if (w != this && w._data != null && w._data == _data)
            {
                w._selectedRows = new HashSet<int>(_selectedRows);
                w._gridHighlightedRows.Clear();
                w._updating = true;
                w.UpdateSelectionDisplay();
                w._updating = false;
                w.UpdatePlot(true);
            }
        }
    }

    private void BroadcastDataChanged()
    {
        foreach (var w in _allWindows)
        {
            if (w != this && w._data != null && w._data == _data)
            {
                w._selectedRows.Clear();
                w._gridHighlightedRows.Clear();
                w._legendHighlightedRows.Clear();
                w._hoveredRow = -1;
                if (w._isLinkedChild)
                {
                    // Child shares filter dictionaries from parent — just reassign
                    w._numericFilters = _numericFilters;
                    w._categoricalFilters = _categoricalFilters;
                    w._filteredRows = _filteredRows;
                }
                else
                {
                    w.BuildFilterPanel();
                }
                w.UpdatePlot();
                w.UpdateSelectionDisplay();
            }
        }
    }

    private void BroadcastFilterChange()
    {
        foreach (var w in _allWindows)
        {
            if (w != this && w._data != null && w._data == _data)
            {
                w._numericFilters = _numericFilters;
                w._categoricalFilters = _categoricalFilters;
                w._filteredRows = _filteredRows;
                w.UpdatePlot();
                w._updating = true;
                w.UpdateSelectionDisplay();
                w._updating = false;
            }
        }
    }

    private void AddIdentityLineControls()
    {
        statsPanel.Children.Add(new TextBlock
        {
            Text = "— Identity line —",
            FontSize = 10, Foreground = Brushes.Gray,
            Margin = new Thickness(0, 8, 0, 2)
        });

        var chkIdentity = new CheckBox
        {
            Content = "Show identity line (y = x)",
            IsChecked = _showIdentityLine,
            FontSize = 10, Margin = new Thickness(0, 2, 0, 0)
        };

        var idStylePanel = new StackPanel { Visibility = _showIdentityLine ? Visibility.Visible : Visibility.Collapsed };

        // Colour row
        var idColorRow = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 0) };
        idColorRow.Children.Add(new TextBlock { Text = "Colour:", FontSize = 10, VerticalAlignment = System.Windows.VerticalAlignment.Center, Width = 44 });
        var cboIdColor = new ComboBox { FontSize = 10, Width = 90 };
        var idLineColors = new (string Name, byte R, byte G, byte B)[]
        {
            ("Grey", 140, 140, 140), ("Black", 0, 0, 0), ("Red", 200, 30, 30),
            ("Blue", 30, 30, 200), ("Green", 0, 140, 0), ("Orange", 220, 120, 0),
            ("Purple", 130, 0, 180)
        };
        int selIdColorIdx = 0;
        for (int ci = 0; ci < idLineColors.Length; ci++)
        {
            var lc = idLineColors[ci];
            var swatch = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
            swatch.Children.Add(new Border
            {
                Width = 12, Height = 12, Margin = new Thickness(0, 0, 4, 0),
                Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(lc.R, lc.G, lc.B))
            });
            swatch.Children.Add(new TextBlock { Text = lc.Name, VerticalAlignment = System.Windows.VerticalAlignment.Center });
            cboIdColor.Items.Add(new ComboBoxItem { Content = swatch, Tag = lc });
            if (_identityLineColor.Red == lc.R && _identityLineColor.Green == lc.G && _identityLineColor.Blue == lc.B)
                selIdColorIdx = ci;
        }
        cboIdColor.SelectedIndex = selIdColorIdx;
        cboIdColor.SelectionChanged += (s, ev) =>
        {
            if (cboIdColor.SelectedItem is ComboBoxItem item && item.Tag is (string, byte r, byte g, byte b))
            {
                _identityLineColor = new ScottPlot.Color(r, g, b);
                UpdatePlot(true);
            }
        };
        idColorRow.Children.Add(cboIdColor);
        idStylePanel.Children.Add(idColorRow);

        // Width row
        var idWidthRow = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 0) };
        idWidthRow.Children.Add(new TextBlock { Text = "Width:", FontSize = 10, VerticalAlignment = System.Windows.VerticalAlignment.Center, Width = 44 });
        var cboIdWidth = new ComboBox { FontSize = 10, Width = 60 };
        float[] idWidths = [1, 1.5f, 2, 3, 4, 5];
        int selIdWidthIdx = 2;
        for (int wi = 0; wi < idWidths.Length; wi++)
        {
            cboIdWidth.Items.Add(new ComboBoxItem { Content = idWidths[wi].ToString("0.#") });
            if (Math.Abs(idWidths[wi] - _identityLineWidth) < 0.01f) selIdWidthIdx = wi;
        }
        cboIdWidth.SelectedIndex = selIdWidthIdx;
        cboIdWidth.SelectionChanged += (s, ev) =>
        {
            if (cboIdWidth.SelectedIndex >= 0 && cboIdWidth.SelectedIndex < idWidths.Length)
            {
                _identityLineWidth = idWidths[cboIdWidth.SelectedIndex];
                UpdatePlot(true);
            }
        };
        idWidthRow.Children.Add(cboIdWidth);
        idStylePanel.Children.Add(idWidthRow);

        // Style row
        var idDashRow = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 0) };
        idDashRow.Children.Add(new TextBlock { Text = "Style:", FontSize = 10, VerticalAlignment = System.Windows.VerticalAlignment.Center, Width = 44 });
        var cboIdDash = new ComboBox { FontSize = 10, Width = 80 };
        cboIdDash.Items.Add(new ComboBoxItem { Content = "Solid" });
        cboIdDash.Items.Add(new ComboBoxItem { Content = "Dashed" });
        cboIdDash.SelectedIndex = _identityLineDashed ? 1 : 0;
        cboIdDash.SelectionChanged += (s, ev) =>
        {
            _identityLineDashed = cboIdDash.SelectedIndex == 1;
            UpdatePlot(true);
        };
        idDashRow.Children.Add(cboIdDash);
        idStylePanel.Children.Add(idDashRow);

        chkIdentity.Checked += (s, ev) => { _showIdentityLine = true; idStylePanel.Visibility = Visibility.Visible; UpdatePlot(true); };
        chkIdentity.Unchecked += (s, ev) => { _showIdentityLine = false; idStylePanel.Visibility = Visibility.Collapsed; UpdatePlot(true); };

        statsPanel.Children.Add(chkIdentity);
        statsPanel.Children.Add(idStylePanel);
    }

    private void ClearRegressionModel()
    {
        _regressionCoeffs = null;
        _regressionPredictors = null;
        _regressionResponse = null;
        statsPanel.Children.Clear();
        statsPanel.Children.Add(new TextBlock { Text = "(no model fitted)", FontSize = 11, Foreground = Brushes.Gray });
        AddIdentityLineControls();
        UpdatePlot(true);
    }

    private static ScottPlot.Color ParseHexColor(string hex)
    {
        hex = hex.TrimStart('#');
        byte r = byte.Parse(hex.Substring(0, 2), NumberStyles.HexNumber);
        byte g = byte.Parse(hex.Substring(2, 2), NumberStyles.HexNumber);
        byte b = byte.Parse(hex.Substring(4, 2), NumberStyles.HexNumber);
        return new ScottPlot.Color(r, g, b);
    }

    // ====================================================================
    //  Session Journal
    // ====================================================================

    private void MenuJournalPublishNew_Click(object sender, RoutedEventArgs e)
    {
        if (_data == null || _columns == null) return;
        var dlg = new SaveFileDialog
        {
            Title = "Create new journal",
            Filter = "HTML files|*.html",
            FileName = $"journal_{DateTime.Now:yyyyMMdd}.html",
            InitialDirectory = _filePath != null ? System.IO.Path.GetDirectoryName(_filePath) : ""
        };
        if (dlg.ShowDialog() != true) return;
        _journalPath = dlg.FileName;
        // Delete existing so it gets created fresh
        if (File.Exists(_journalPath))
            File.Delete(_journalPath);
        PublishToJournal();
    }

    private void MenuJournalAppend_Click(object sender, RoutedEventArgs e)
    {
        if (_data == null || _columns == null) return;
        if (_journalPath == null || !File.Exists(_journalPath))
        {
            var dlg = new OpenFileDialog
            {
                Title = "Select journal to append to",
                Filter = "HTML files|*.html",
                InitialDirectory = _filePath != null ? System.IO.Path.GetDirectoryName(_filePath) : ""
            };
            if (dlg.ShowDialog() != true) return;
            _journalPath = dlg.FileName;
        }
        PublishToJournal();
    }

    private void PublishToJournal()
    {
        if (_data == null || _columns == null || _journalPath == null) return;

        // Show annotation dialog
        var dialog = new Window
        {
            Title = "Publish View to Journal",
            Width = 420, Height = 260,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            ResizeMode = ResizeMode.NoResize
        };
        var stack = new StackPanel { Margin = new Thickness(15) };
        stack.Children.Add(new TextBlock { Text = "Add notes about this view:", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) });
        var txtAnnotation = new TextBox
        {
            AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
            Height = 120, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            FontSize = 12
        };
        stack.Children.Add(txtAnnotation);
        var btnPanel = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
        var btnOk = new Button { Content = "Publish", Padding = new Thickness(16, 4, 16, 4), IsDefault = true };
        var btnCancel = new Button { Content = "Cancel", Padding = new Thickness(16, 4, 16, 4), Margin = new Thickness(8, 0, 0, 0), IsCancel = true };
        btnOk.Click += (s, ev) => dialog.DialogResult = true;
        btnPanel.Children.Add(btnOk);
        btnPanel.Children.Add(btnCancel);
        stack.Children.Add(btnPanel);
        dialog.Content = stack;

        if (dialog.ShowDialog() != true) return;

        string annotation = txtAnnotation.Text.Trim();

        try
        {
            // Render plot to base64 PNG
            string tempPng = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"spe_journal_{Guid.NewGuid():N}.png");
            wpfPlot.Plot.SavePng(tempPng, 1000, 700);
            byte[] pngBytes = File.ReadAllBytes(tempPng);
            string base64Img = Convert.ToBase64String(pngBytes);
            try { File.Delete(tempPng); } catch { }

            // Build view state JSON
            string stateJson = BuildViewStateJson();

            // Build summary line
            string summary = BuildEntrySummary();

            // Build the HTML entry
            string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            string entryId = $"entry-{DateTime.Now:yyyyMMddHHmmss}";
            var entrySb = new StringBuilder();
            entrySb.AppendLine($"<div class=\"entry\" id=\"{entryId}\">");
            entrySb.AppendLine($"  <h2>{System.Net.WebUtility.HtmlEncode(timestamp)}</h2>");
            entrySb.AppendLine($"  <p class=\"summary\">{System.Net.WebUtility.HtmlEncode(summary)}</p>");
            entrySb.AppendLine($"  <img src=\"data:image/png;base64,{base64Img}\" alt=\"Plot snapshot\"/>");
            if (!string.IsNullOrEmpty(annotation))
            {
                entrySb.AppendLine($"  <div class=\"annotation\">");
                foreach (var line in annotation.Split('\n'))
                    entrySb.AppendLine($"    <p>{System.Net.WebUtility.HtmlEncode(line.TrimEnd('\r'))}</p>");
                entrySb.AppendLine($"  </div>");
            }
            entrySb.AppendLine($"  <details><summary>View state (JSON)</summary>");
            entrySb.AppendLine($"  <script class=\"view-state\" type=\"application/json\">");
            entrySb.AppendLine(stateJson);
            entrySb.AppendLine($"  </script>");
            entrySb.AppendLine($"  <pre class=\"json-display\">{System.Net.WebUtility.HtmlEncode(stateJson)}</pre>");
            entrySb.AppendLine($"  </details>");
            entrySb.AppendLine($"</div>");

            // Write or append to journal file
            if (!File.Exists(_journalPath))
            {
                // Create new journal with HTML header
                var fileSb = new StringBuilder();
                fileSb.AppendLine("<!DOCTYPE html>");
                fileSb.AppendLine("<html lang=\"en\">");
                fileSb.AppendLine("<head>");
                fileSb.AppendLine("<meta charset=\"UTF-8\">");
                fileSb.AppendLine("<title>Scatter Plot Explorer — Session Journal</title>");
                fileSb.AppendLine("<style>");
                fileSb.AppendLine("body { font-family: 'Segoe UI', Calibri, Arial, sans-serif; max-width: 1100px; margin: 0 auto; padding: 20px; background: #fafafa; color: #333; }");
                fileSb.AppendLine("h1 { border-bottom: 2px solid #1F77B4; padding-bottom: 8px; color: #1F77B4; }");
                fileSb.AppendLine(".entry { background: #fff; border: 1px solid #ddd; border-radius: 6px; padding: 20px; margin-bottom: 24px; box-shadow: 0 1px 3px rgba(0,0,0,0.06); }");
                fileSb.AppendLine(".entry h2 { margin-top: 0; font-size: 1.1em; color: #555; }");
                fileSb.AppendLine(".entry img { max-width: 100%; border: 1px solid #eee; border-radius: 4px; margin: 10px 0; }");
                fileSb.AppendLine(".annotation { background: #fffde7; border-left: 3px solid #f9a825; padding: 10px 14px; margin: 10px 0; border-radius: 0 4px 4px 0; }");
                fileSb.AppendLine(".annotation p { margin: 4px 0; }");
                fileSb.AppendLine(".summary { font-size: 0.9em; color: #777; margin: 2px 0 10px 0; }");
                fileSb.AppendLine("details { margin-top: 10px; }");
                fileSb.AppendLine("details summary { cursor: pointer; color: #1F77B4; font-size: 0.9em; }");
                fileSb.AppendLine(".json-display { background: #f5f5f5; padding: 10px; border-radius: 4px; font-size: 0.8em; overflow-x: auto; max-height: 300px; overflow-y: auto; }");
                fileSb.AppendLine("@media print { .entry { break-inside: avoid; } details { display: none; } }");
                fileSb.AppendLine("</style>");
                fileSb.AppendLine("</head>");
                fileSb.AppendLine("<body>");
                fileSb.AppendLine("<h1>Scatter Plot Explorer — Session Journal</h1>");
                fileSb.Append(entrySb);
                fileSb.AppendLine("</body>");
                fileSb.AppendLine("</html>");
                File.WriteAllText(_journalPath, fileSb.ToString(), Encoding.UTF8);
            }
            else
            {
                // Append before closing </body></html>
                string existing = File.ReadAllText(_journalPath, Encoding.UTF8);
                int bodyClose = existing.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
                if (bodyClose >= 0)
                {
                    string updated = existing.Substring(0, bodyClose) + entrySb.ToString() + existing.Substring(bodyClose);
                    File.WriteAllText(_journalPath, updated, Encoding.UTF8);
                }
                else
                {
                    // Fallback: just append
                    File.AppendAllText(_journalPath, entrySb.ToString(), Encoding.UTF8);
                }
            }

            MessageBox.Show($"View published to journal.\n{_journalPath}", "Journal", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error publishing to journal:\n{ex.Message}", "Journal Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private string BuildViewStateJson()
    {
        var state = new Dictionary<string, object?>();
        state["timestamp"] = DateTime.Now.ToString("o");
        state["filePath"] = _filePath;
        state["delimiter"] = _fileDelimiter.ToString();
        state["xColumn"] = cboX.SelectedItem?.ToString();
        state["yColumn"] = cboY.SelectedItem?.ToString();

        string? colorCol = cboColor.SelectedItem?.ToString();
        state["colorColumn"] = colorCol == "(none)" ? null : colorCol;
        string? shapeCol = cboShapeCol.SelectedItem?.ToString();
        state["shapeColumn"] = shapeCol == "(none)" ? null : shapeCol;
        string? sizeCol = cboSizeCol.SelectedItem?.ToString();
        state["sizeColumn"] = sizeCol == "(none)" ? null : sizeCol;
        string? labelCol = cboLabelCol.SelectedItem?.ToString();
        state["labelColumn"] = labelCol == "(none)" ? null : labelCol;

        state["logX"] = chkLogX.IsChecked == true;
        state["logY"] = chkLogY.IsChecked == true;
        state["invertX"] = chkInvertX.IsChecked == true;
        state["invertY"] = chkInvertY.IsChecked == true;
        state["logColor"] = chkLogColor.IsChecked == true;
        state["reverseColor"] = chkReverseColor.IsChecked == true;
        state["logSize"] = chkLogSize.IsChecked == true;
        state["reverseSize"] = chkReverseSize.IsChecked == true;

        state["presetColorIndex"] = cboPresetColor.SelectedIndex;
        state["shapeIndex"] = cboShape.SelectedIndex;
        state["sliderSize"] = sliderSize.Value;

        state["xMin"] = txtXMin.Text;
        state["xMax"] = txtXMax.Text;
        state["yMin"] = txtYMin.Text;
        state["yMax"] = txtYMax.Text;

        // Numeric filters — only those narrower than defaults
        var numFilters = new Dictionary<string, object>();
        foreach (var (col, (fmin, fmax)) in _numericFilters)
            numFilters[col] = new { min = fmin, max = fmax };
        state["numericFilters"] = numFilters;

        // Categorical filters
        var catFilters = new Dictionary<string, object>();
        foreach (var (col, allowed) in _categoricalFilters)
            catFilters[col] = allowed.ToList();
        state["categoricalFilters"] = catFilters;

        // Selected rows
        state["selectedRows"] = _selectedRows.OrderBy(i => i).ToList();

        // Identity line
        state["showIdentityLine"] = _showIdentityLine;
        state["identityLineColor"] = $"#{_identityLineColor.Red:X2}{_identityLineColor.Green:X2}{_identityLineColor.Blue:X2}";
        state["identityLineWidth"] = _identityLineWidth;
        state["identityLineDashed"] = _identityLineDashed;

        // Cartesian axes
        state["showCartesianAxes"] = _showCartesianAxes;

        // Regression
        if (_regressionCoeffs != null)
        {
            state["regressionCoeffs"] = _regressionCoeffs.ToList();
            state["regressionPredictors"] = _regressionPredictors?.ToList();
            state["regressionResponse"] = _regressionResponse;
            state["regressionOrder"] = _regressionOrder;
            state["regressionLogResponse"] = _regressionLogResponse;
            state["regressionLogPredictors"] = _regressionLogPredictors;
            state["regLineColor"] = $"#{_regLineColor.Red:X2}{_regLineColor.Green:X2}{_regLineColor.Blue:X2}";
            state["regLineWidth"] = _regLineWidth;
            state["regLineDashed"] = _regLineDashed;
        }

        // Category colour overrides
        if (_categoryColorOverrides.Count > 0)
            state["categoryColorOverrides"] = new Dictionary<string, string>(_categoryColorOverrides);

        // Label font
        state["labelFontFamily"] = _labelFontFamily;
        state["labelFontSize"] = _labelFontSize;
        state["labelFontColor"] = $"#{_labelFontColor.Red:X2}{_labelFontColor.Green:X2}{_labelFontColor.Blue:X2}";

        var options = new JsonSerializerOptions { WriteIndented = true };
        return JsonSerializer.Serialize(state, options);
    }

    private string BuildEntrySummary()
    {
        var parts = new List<string>();
        string? x = cboX.SelectedItem?.ToString();
        string? y = cboY.SelectedItem?.ToString();
        if (x != null && y != null) parts.Add($"X={x}, Y={y}");

        string? colourCol = cboColor.SelectedItem?.ToString();
        if (colourCol != null && colourCol != "(none)") parts.Add($"Colour={colourCol}");

        int nVisible = 0;
        if (_plotX != null)
            for (int i = 0; i < _plotX.Length; i++)
                if (IsRowVisible(i) && !double.IsNaN(_plotX[i]) && !double.IsNaN(_plotY![i]))
                    nVisible++;
        parts.Add($"N={nVisible}");

        if (_selectedRows.Count > 0) parts.Add($"{_selectedRows.Count} selected");
        if (_regressionCoeffs != null) parts.Add($"Regression (order {_regressionOrder})");
        if (chkLogX.IsChecked == true) parts.Add("log₁₀ X");
        if (chkLogY.IsChecked == true) parts.Add("log₁₀ Y");

        return string.Join(" · ", parts);
    }

    private void MenuJournalOpen_Click(object sender, RoutedEventArgs e)
    {
        string? pathToOpen = _journalPath;

        if (pathToOpen == null || !File.Exists(pathToOpen))
        {
            var dlg = new OpenFileDialog
            {
                Title = "Open journal file",
                Filter = "HTML files|*.html|All files|*.*",
                InitialDirectory = _filePath != null ? System.IO.Path.GetDirectoryName(_filePath) : ""
            };
            if (dlg.ShowDialog() != true) return;
            pathToOpen = dlg.FileName;
            _journalPath = pathToOpen;
        }

        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo(pathToOpen) { UseShellExecute = true };
            System.Diagnostics.Process.Start(psi);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not open journal:\n{ex.Message}");
        }
    }

    private void MenuJournalLoad_Click(object sender, RoutedEventArgs e)
    {
        // Pick a journal file if none set
        string? journalFile = _journalPath;
        if (journalFile == null || !File.Exists(journalFile))
        {
            var dlg = new OpenFileDialog
            {
                Title = "Open journal file",
                Filter = "HTML files|*.html|All files|*.*",
                InitialDirectory = _filePath != null ? System.IO.Path.GetDirectoryName(_filePath) : ""
            };
            if (dlg.ShowDialog() != true) return;
            journalFile = dlg.FileName;
            _journalPath = journalFile;
        }

        // Parse entries from journal HTML
        string html = File.ReadAllText(journalFile, Encoding.UTF8);
        var entries = ParseJournalEntries(html);
        if (entries.Count == 0)
        {
            MessageBox.Show("No entries found in journal file.", "Load View", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // Show picker dialog
        var dialog = new Window
        {
            Title = "Load View from Journal",
            Width = 500, Height = 400,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            ResizeMode = ResizeMode.CanResize
        };
        var stack = new StackPanel { Margin = new Thickness(15) };
        stack.Children.Add(new TextBlock { Text = "Select an entry to restore:", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) });
        var lb = new ListBox { Height = 260 };
        foreach (var (timestamp, preview, json) in entries)
        {
            string display = $"{timestamp}";
            if (!string.IsNullOrEmpty(preview)) display += $"  —  {preview}";
            lb.Items.Add(new ListBoxItem { Content = display, Tag = json });
        }
        lb.SelectedIndex = entries.Count - 1; // default to most recent
        stack.Children.Add(lb);
        var btnPanel = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
        var btnOk = new Button { Content = "Load", Padding = new Thickness(16, 4, 16, 4), IsDefault = true };
        var btnCancel = new Button { Content = "Cancel", Padding = new Thickness(16, 4, 16, 4), Margin = new Thickness(8, 0, 0, 0), IsCancel = true };
        btnOk.Click += (s, ev) => { if (lb.SelectedItem != null) dialog.DialogResult = true; };
        lb.MouseDoubleClick += (s, ev) => { if (lb.SelectedItem != null) dialog.DialogResult = true; };
        btnPanel.Children.Add(btnOk);
        btnPanel.Children.Add(btnCancel);
        stack.Children.Add(btnPanel);
        dialog.Content = stack;

        if (dialog.ShowDialog() != true) return;

        string selectedJson = ((ListBoxItem)lb.SelectedItem!).Tag?.ToString() ?? "";
        if (string.IsNullOrEmpty(selectedJson))
        {
            MessageBox.Show("No view state found for this entry.");
            return;
        }

        try
        {
            ApplyViewState(selectedJson);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error loading view state:\n{ex.Message}", "Load Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private class JournalEntry
    {
        public string Timestamp { get; set; } = "";
        public string Summary { get; set; } = "";
        public string Annotation { get; set; } = "";
        public string Base64Image { get; set; } = "";  // without data:image/png;base64, prefix
        public string Json { get; set; } = "";
        public bool Deleted { get; set; }
    }

    private List<JournalEntry> ParseJournalEntriesFull(string html)
    {
        var entries = new List<JournalEntry>();
        string scriptOpen = "<script class=\"view-state\" type=\"application/json\">";
        string scriptClose = "</script>";

        int pos = 0;
        while (true)
        {
            int entryStart = html.IndexOf("<div class=\"entry\"", pos, StringComparison.OrdinalIgnoreCase);
            if (entryStart < 0) break;

            int nextEntry = html.IndexOf("<div class=\"entry\"", entryStart + 10, StringComparison.OrdinalIgnoreCase);
            int entryEnd = nextEntry > 0 ? nextEntry : html.Length;
            string entryHtml = html.Substring(entryStart, entryEnd - entryStart);

            var entry = new JournalEntry();

            // Timestamp from <h2>
            int h2s = entryHtml.IndexOf("<h2>", StringComparison.OrdinalIgnoreCase);
            int h2e = entryHtml.IndexOf("</h2>", h2s >= 0 ? h2s : 0, StringComparison.OrdinalIgnoreCase);
            if (h2s >= 0 && h2e > h2s)
                entry.Timestamp = System.Net.WebUtility.HtmlDecode(entryHtml.Substring(h2s + 4, h2e - h2s - 4)).Trim();

            // Summary from <p class="summary">
            int sumS = entryHtml.IndexOf("<p class=\"summary\">", StringComparison.OrdinalIgnoreCase);
            if (sumS >= 0)
            {
                sumS += "<p class=\"summary\">".Length;
                int sumE = entryHtml.IndexOf("</p>", sumS, StringComparison.OrdinalIgnoreCase);
                if (sumE > sumS)
                    entry.Summary = System.Net.WebUtility.HtmlDecode(entryHtml.Substring(sumS, sumE - sumS)).Trim();
            }

            // Base64 image from <img src="data:image/png;base64,...">
            int imgS = entryHtml.IndexOf("data:image/png;base64,", StringComparison.OrdinalIgnoreCase);
            if (imgS >= 0)
            {
                imgS += "data:image/png;base64,".Length;
                int imgE = entryHtml.IndexOf("\"", imgS, StringComparison.Ordinal);
                if (imgE > imgS)
                    entry.Base64Image = entryHtml.Substring(imgS, imgE - imgS);
            }

            // Annotation from <div class="annotation"> — collect all <p> tags
            int annS = entryHtml.IndexOf("<div class=\"annotation\">", StringComparison.OrdinalIgnoreCase);
            if (annS >= 0)
            {
                int annE = entryHtml.IndexOf("</div>", annS, StringComparison.OrdinalIgnoreCase);
                if (annE > annS)
                {
                    string annBlock = entryHtml.Substring(annS, annE - annS);
                    var lines = new List<string>();
                    int pPos = 0;
                    while (true)
                    {
                        int pS = annBlock.IndexOf("<p>", pPos, StringComparison.OrdinalIgnoreCase);
                        if (pS < 0) break;
                        pS += 3;
                        int pE = annBlock.IndexOf("</p>", pS, StringComparison.OrdinalIgnoreCase);
                        if (pE < 0) break;
                        lines.Add(System.Net.WebUtility.HtmlDecode(annBlock.Substring(pS, pE - pS)));
                        pPos = pE + 4;
                    }
                    entry.Annotation = string.Join("\n", lines);
                }
            }

            // JSON from <script class="view-state">
            int scrS = entryHtml.IndexOf(scriptOpen, StringComparison.OrdinalIgnoreCase);
            int scrE = entryHtml.IndexOf(scriptClose, scrS >= 0 ? scrS : 0, StringComparison.OrdinalIgnoreCase);
            if (scrS >= 0 && scrE > scrS)
                entry.Json = entryHtml.Substring(scrS + scriptOpen.Length, scrE - scrS - scriptOpen.Length).Trim();

            entries.Add(entry);
            pos = entryEnd;
        }

        return entries;
    }

    /// <summary>Backwards-compat wrapper used by Load view dialog.</summary>
    private List<(string timestamp, string preview, string json)> ParseJournalEntries(string html)
    {
        var full = ParseJournalEntriesFull(html);
        return full.Select(e =>
        {
            string preview = e.Annotation;
            if (preview.Length > 80) preview = preview.Substring(0, 77) + "...";
            if (preview.Contains('\n')) preview = preview.Substring(0, preview.IndexOf('\n'));
            return (e.Timestamp, preview, e.Json);
        }).Where(x => !string.IsNullOrEmpty(x.Json)).ToList();
    }

    private void ApplyViewState(string json)
    {
        var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // Check if we need to load a file first
        string? filePath = root.TryGetProperty("filePath", out var fp) ? fp.GetString() : null;
        if (filePath != null && _filePath != filePath)
        {
            if (File.Exists(filePath))
                LoadFile(filePath);
            else
            {
                MessageBox.Show($"Data file not found:\n{filePath}\n\nOpen the file first, then try loading the view again.",
                    "File Not Found", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
        }

        if (_data == null || _columns == null) return;

        _updating = true;

        // Axis columns
        if (root.TryGetProperty("xColumn", out var xc) && xc.ValueKind == JsonValueKind.String)
            SetComboByText(cboX, xc.GetString()!);
        if (root.TryGetProperty("yColumn", out var yc) && yc.ValueKind == JsonValueKind.String)
            SetComboByText(cboY, yc.GetString()!);

        // Appearance columns
        if (root.TryGetProperty("colorColumn", out var cc))
            SetComboByText(cboColor, cc.ValueKind == JsonValueKind.Null ? "(none)" : cc.GetString()!);
        if (root.TryGetProperty("shapeColumn", out var sc))
            SetComboByText(cboShapeCol, sc.ValueKind == JsonValueKind.Null ? "(none)" : sc.GetString()!);
        if (root.TryGetProperty("sizeColumn", out var szc))
            SetComboByText(cboSizeCol, szc.ValueKind == JsonValueKind.Null ? "(none)" : szc.GetString()!);
        if (root.TryGetProperty("labelColumn", out var lc))
            SetComboByText(cboLabelCol, lc.ValueKind == JsonValueKind.Null ? "(none)" : lc.GetString()!);

        // Checkboxes
        if (root.TryGetProperty("logX", out var lx)) chkLogX.IsChecked = lx.GetBoolean();
        if (root.TryGetProperty("logY", out var ly)) chkLogY.IsChecked = ly.GetBoolean();
        if (root.TryGetProperty("invertX", out var ix)) chkInvertX.IsChecked = ix.GetBoolean();
        if (root.TryGetProperty("invertY", out var iy)) chkInvertY.IsChecked = iy.GetBoolean();
        if (root.TryGetProperty("logColor", out var lcol)) chkLogColor.IsChecked = lcol.GetBoolean();
        if (root.TryGetProperty("reverseColor", out var rc)) chkReverseColor.IsChecked = rc.GetBoolean();
        if (root.TryGetProperty("logSize", out var lsz)) chkLogSize.IsChecked = lsz.GetBoolean();
        if (root.TryGetProperty("reverseSize", out var rsz)) chkReverseSize.IsChecked = rsz.GetBoolean();

        // Preset colour and shape
        if (root.TryGetProperty("presetColorIndex", out var pci)) cboPresetColor.SelectedIndex = Math.Clamp(pci.GetInt32(), 0, cboPresetColor.Items.Count - 1);
        if (root.TryGetProperty("shapeIndex", out var si)) cboShape.SelectedIndex = Math.Clamp(si.GetInt32(), 0, cboShape.Items.Count - 1);
        if (root.TryGetProperty("sliderSize", out var ss)) sliderSize.Value = ss.GetDouble();

        // Axis ranges
        if (root.TryGetProperty("xMin", out var xmin)) txtXMin.Text = xmin.GetString() ?? "";
        if (root.TryGetProperty("xMax", out var xmax)) txtXMax.Text = xmax.GetString() ?? "";
        if (root.TryGetProperty("yMin", out var ymin)) txtYMin.Text = ymin.GetString() ?? "";
        if (root.TryGetProperty("yMax", out var ymax)) txtYMax.Text = ymax.GetString() ?? "";

        // Category colour overrides
        _categoryColorOverrides.Clear();
        if (root.TryGetProperty("categoryColorOverrides", out var cco) && cco.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in cco.EnumerateObject())
                _categoryColorOverrides[prop.Name] = prop.Value.GetString()!;
        }

        // Numeric filters
        _numericFilters.Clear();
        if (root.TryGetProperty("numericFilters", out var nf) && nf.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in nf.EnumerateObject())
            {
                if (prop.Value.TryGetProperty("min", out var minv) && prop.Value.TryGetProperty("max", out var maxv))
                    _numericFilters[prop.Name] = (minv.GetDouble(), maxv.GetDouble());
            }
        }

        // Categorical filters
        _categoricalFilters.Clear();
        if (root.TryGetProperty("categoricalFilters", out var cf) && cf.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in cf.EnumerateObject())
            {
                if (prop.Value.ValueKind == JsonValueKind.Array)
                {
                    var set = new HashSet<string>();
                    foreach (var item in prop.Value.EnumerateArray())
                        if (item.ValueKind == JsonValueKind.String) set.Add(item.GetString()!);
                    _categoricalFilters[prop.Name] = set;
                }
            }
        }

        // Identity line
        if (root.TryGetProperty("showIdentityLine", out var sil)) _showIdentityLine = sil.GetBoolean();
        if (root.TryGetProperty("identityLineColor", out var ilc) && ilc.ValueKind == JsonValueKind.String)
            _identityLineColor = ParseHexColor(ilc.GetString()!);
        if (root.TryGetProperty("identityLineWidth", out var ilw)) _identityLineWidth = (float)ilw.GetDouble();
        if (root.TryGetProperty("identityLineDashed", out var ild)) _identityLineDashed = ild.GetBoolean();

        // Cartesian axes
        if (root.TryGetProperty("showCartesianAxes", out var sca)) _showCartesianAxes = sca.GetBoolean();

        // Label font
        if (root.TryGetProperty("labelFontFamily", out var lff) && lff.ValueKind == JsonValueKind.String)
            _labelFontFamily = lff.GetString()!;
        if (root.TryGetProperty("labelFontSize", out var lfs)) _labelFontSize = (float)lfs.GetDouble();
        if (root.TryGetProperty("labelFontColor", out var lfcol) && lfcol.ValueKind == JsonValueKind.String)
            _labelFontColor = ParseHexColor(lfcol.GetString()!);

        // Regression
        _regressionCoeffs = null;
        _regressionPredictors = null;
        _regressionResponse = null;
        if (root.TryGetProperty("regressionCoeffs", out var rcoeffs) && rcoeffs.ValueKind == JsonValueKind.Array)
        {
            _regressionCoeffs = rcoeffs.EnumerateArray().Select(v => v.GetDouble()).ToArray();
            if (root.TryGetProperty("regressionPredictors", out var rpred) && rpred.ValueKind == JsonValueKind.Array)
                _regressionPredictors = rpred.EnumerateArray().Select(v => v.GetString()!).ToArray();
            if (root.TryGetProperty("regressionResponse", out var rresp) && rresp.ValueKind == JsonValueKind.String)
                _regressionResponse = rresp.GetString();
            if (root.TryGetProperty("regressionOrder", out var rord)) _regressionOrder = rord.GetInt32();
            if (root.TryGetProperty("regressionLogResponse", out var rlr)) _regressionLogResponse = rlr.GetBoolean();
            if (root.TryGetProperty("regressionLogPredictors", out var rlp)) _regressionLogPredictors = rlp.GetBoolean();
            if (root.TryGetProperty("regLineColor", out var rlc) && rlc.ValueKind == JsonValueKind.String)
                _regLineColor = ParseHexColor(rlc.GetString()!);
            if (root.TryGetProperty("regLineWidth", out var rlw)) _regLineWidth = (float)rlw.GetDouble();
            if (root.TryGetProperty("regLineDashed", out var rld)) _regLineDashed = rld.GetBoolean();
        }

        _updating = false;

        // Rebuild filters UI to reflect loaded state, then recompute
        BuildFilterPanel();
        RecomputeFilteredRows();

        // Restore selection
        _selectedRows.Clear();
        if (root.TryGetProperty("selectedRows", out var sr) && sr.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in sr.EnumerateArray())
                if (item.TryGetInt32(out int idx) && idx >= 0 && idx < (_data?.Rows.Count ?? 0))
                    _selectedRows.Add(idx);
        }

        UpdatePlot();
        UpdateSelectionDisplay();

        // Rebuild statistics panel
        statsPanel.Children.Clear();
        if (_regressionCoeffs != null && _regressionPredictors != null && _regressionResponse != null)
        {
            string yLabel = _regressionLogResponse ? $"log\u2081\u2080({_regressionResponse})" : _regressionResponse;
            var dispPreds = new List<string>();
            foreach (var p in _regressionPredictors)
            {
                string lbl = _regressionLogPredictors ? $"log\u2081\u2080({p})" : p;
                dispPreds.Add(lbl);
            }
            if (_regressionPredictors.Length == 1 && _regressionOrder > 1)
            {
                dispPreds.Clear();
                string baseName = _regressionLogPredictors ? $"log\u2081\u2080({_regressionPredictors[0]})" : _regressionPredictors[0];
                for (int o = 1; o <= _regressionOrder; o++)
                    dispPreds.Add(o == 1 ? baseName : baseName + SuperscriptDigits(o));
            }
            string modelDesc = $"{yLabel} ~ {string.Join(" + ", dispPreds)}";
            statsPanel.Children.Add(new TextBlock { Text = modelDesc, FontWeight = FontWeights.SemiBold, FontSize = 11, TextWrapping = TextWrapping.Wrap });
            statsPanel.Children.Add(new TextBlock { Text = "(restored from journal — refit for full stats)", FontSize = 10, Foreground = Brushes.Gray, Margin = new Thickness(0, 2, 0, 6) });

            var btnClear = new Button { Content = "Clear Model", Padding = new Thickness(8, 3, 8, 3), HorizontalAlignment = System.Windows.HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 0) };
            btnClear.Click += (s2, e2) => ClearRegressionModel();
            statsPanel.Children.Add(btnClear);
        }
        else
            statsPanel.Children.Add(new TextBlock { Text = "(no model fitted)", FontSize = 11, Foreground = Brushes.Gray });
        AddIdentityLineControls();
    }

    private void MenuJournalEdit_Click(object sender, RoutedEventArgs e)
    {
        // Pick a journal file if none set
        string? journalFile = _journalPath;
        if (journalFile == null || !File.Exists(journalFile))
        {
            var dlg = new OpenFileDialog
            {
                Title = "Open journal file to edit",
                Filter = "HTML files|*.html|All files|*.*",
                InitialDirectory = _filePath != null ? System.IO.Path.GetDirectoryName(_filePath) : ""
            };
            if (dlg.ShowDialog() != true) return;
            journalFile = dlg.FileName;
            _journalPath = journalFile;
        }

        string html = File.ReadAllText(journalFile, Encoding.UTF8);
        var entries = ParseJournalEntriesFull(html);
        if (entries.Count == 0)
        {
            MessageBox.Show("No entries found in journal file.", "Edit Journal", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // Build editor window
        var win = new Window
        {
            Title = $"Edit Journal — {System.IO.Path.GetFileName(journalFile)}",
            Width = 820, Height = 700,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this
        };

        var rootDock = new DockPanel();

        // Top toolbar
        var toolbar = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Margin = new Thickness(10, 8, 10, 8) };
        var btnSave = new Button { Content = "Save", Padding = new Thickness(14, 4, 14, 4), FontWeight = FontWeights.SemiBold };
        var btnAddNote = new Button { Content = "Add note (no image)", Padding = new Thickness(14, 4, 14, 4), Margin = new Thickness(10, 0, 0, 0) };
        var statusText = new TextBlock { Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = System.Windows.VerticalAlignment.Center, Foreground = Brushes.Gray };
        toolbar.Children.Add(btnSave);
        toolbar.Children.Add(btnAddNote);
        toolbar.Children.Add(statusText);
        DockPanel.SetDock(toolbar, Dock.Top);
        rootDock.Children.Add(toolbar);

        // Scrollable entries panel
        var scrollViewer = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(10, 0, 10, 10) };
        var entriesPanel = new StackPanel();

        // Track TextBox → entry mapping for save
        var entryControls = new List<(JournalEntry entry, TextBox annotationBox)>();

        foreach (var entry in entries)
        {
            var card = new Border
            {
                BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xDD, 0xDD, 0xDD)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(12),
                Margin = new Thickness(0, 0, 0, 12),
                Background = Brushes.White
            };
            var cardStack = new StackPanel();

            // Header row: timestamp + summary + delete button
            var headerRow = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
            var btnDelete = new Button { Content = "Delete", Padding = new Thickness(8, 2, 8, 2), Foreground = Brushes.Red, FontSize = 11 };
            DockPanel.SetDock(btnDelete, Dock.Right);
            headerRow.Children.Add(btnDelete);
            var headerText = new TextBlock
            {
                FontWeight = FontWeights.SemiBold, FontSize = 13,
                Text = entry.Timestamp,
                VerticalAlignment = System.Windows.VerticalAlignment.Center
            };
            headerRow.Children.Add(headerText);
            cardStack.Children.Add(headerRow);

            if (!string.IsNullOrEmpty(entry.Summary))
            {
                cardStack.Children.Add(new TextBlock
                {
                    Text = entry.Summary, FontSize = 11, Foreground = Brushes.Gray,
                    Margin = new Thickness(0, 0, 0, 6)
                });
            }

            // Thumbnail image
            if (!string.IsNullOrEmpty(entry.Base64Image))
            {
                try
                {
                    var bitmapImg = new System.Windows.Media.Imaging.BitmapImage();
                    bitmapImg.BeginInit();
                    bitmapImg.StreamSource = new MemoryStream(Convert.FromBase64String(entry.Base64Image));
                    bitmapImg.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                    bitmapImg.EndInit();
                    bitmapImg.Freeze();
                    var img = new System.Windows.Controls.Image
                    {
                        Source = bitmapImg,
                        MaxHeight = 300,
                        HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
                        Margin = new Thickness(0, 0, 0, 6)
                    };
                    cardStack.Children.Add(img);
                }
                catch { }
            }

            // Editable annotation
            cardStack.Children.Add(new TextBlock { Text = "Annotation:", FontSize = 11, Foreground = Brushes.Gray, Margin = new Thickness(0, 0, 0, 2) });
            var txtAnn = new TextBox
            {
                Text = entry.Annotation,
                AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
                MinHeight = 50, MaxHeight = 200,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                FontSize = 12,
                Padding = new Thickness(4)
            };
            cardStack.Children.Add(txtAnn);

            card.Child = cardStack;
            entriesPanel.Children.Add(card);
            entryControls.Add((entry, txtAnn));

            // Wire delete button — capture references
            var capturedCard = card;
            var capturedEntry = entry;
            btnDelete.Click += (s, ev) =>
            {
                var result = MessageBox.Show($"Delete entry from {capturedEntry.Timestamp}?",
                    "Delete Entry", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (result == MessageBoxResult.Yes)
                {
                    capturedEntry.Deleted = true;
                    capturedCard.Visibility = Visibility.Collapsed;
                    statusText.Text = "Entry marked for deletion — click Save to apply.";
                    statusText.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xD6, 0x27, 0x28));
                }
            };
        }

        scrollViewer.Content = entriesPanel;
        rootDock.Children.Add(scrollViewer);
        win.Content = rootDock;

        // Add note button
        btnAddNote.Click += (s, ev) =>
        {
            var newEntry = new JournalEntry
            {
                Timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                Summary = "Text note"
            };
            entries.Add(newEntry);

            var card = new Border
            {
                BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xDD, 0xDD, 0xDD)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(12),
                Margin = new Thickness(0, 0, 0, 12),
                Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFF, 0xFD, 0xE7))
            };
            var cardStack = new StackPanel();
            var headerRow = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
            var btnDel = new Button { Content = "Delete", Padding = new Thickness(8, 2, 8, 2), Foreground = Brushes.Red, FontSize = 11 };
            DockPanel.SetDock(btnDel, Dock.Right);
            headerRow.Children.Add(btnDel);
            headerRow.Children.Add(new TextBlock { Text = newEntry.Timestamp, FontWeight = FontWeights.SemiBold, FontSize = 13, VerticalAlignment = System.Windows.VerticalAlignment.Center });
            cardStack.Children.Add(headerRow);
            cardStack.Children.Add(new TextBlock { Text = "Annotation:", FontSize = 11, Foreground = Brushes.Gray, Margin = new Thickness(0, 0, 0, 2) });
            var txtAnn = new TextBox
            {
                AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
                MinHeight = 50, MaxHeight = 200,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                FontSize = 12, Padding = new Thickness(4)
            };
            cardStack.Children.Add(txtAnn);
            card.Child = cardStack;
            entriesPanel.Children.Add(card);
            entryControls.Add((newEntry, txtAnn));

            var capturedCard = card;
            var capturedEntry = newEntry;
            btnDel.Click += (s2, ev2) =>
            {
                capturedEntry.Deleted = true;
                capturedCard.Visibility = Visibility.Collapsed;
            };

            txtAnn.Focus();
            scrollViewer.ScrollToBottom();
            statusText.Text = "Note added — click Save when done.";
            statusText.Foreground = Brushes.Gray;
        };

        // Save button
        btnSave.Click += (s, ev) =>
        {
            try
            {
                // Update annotations from TextBoxes
                foreach (var (entry, box) in entryControls)
                    entry.Annotation = box.Text.Trim();

                // Rebuild HTML
                var sb = new StringBuilder();
                sb.AppendLine("<!DOCTYPE html>");
                sb.AppendLine("<html lang=\"en\">");
                sb.AppendLine("<head>");
                sb.AppendLine("<meta charset=\"UTF-8\">");
                sb.AppendLine("<title>Scatter Plot Explorer \u2014 Session Journal</title>");
                sb.AppendLine("<style>");
                sb.AppendLine("body { font-family: 'Segoe UI', Calibri, Arial, sans-serif; max-width: 1100px; margin: 0 auto; padding: 20px; background: #fafafa; color: #333; }");
                sb.AppendLine("h1 { border-bottom: 2px solid #1F77B4; padding-bottom: 8px; color: #1F77B4; }");
                sb.AppendLine(".entry { background: #fff; border: 1px solid #ddd; border-radius: 6px; padding: 20px; margin-bottom: 24px; box-shadow: 0 1px 3px rgba(0,0,0,0.06); }");
                sb.AppendLine(".entry h2 { margin-top: 0; font-size: 1.1em; color: #555; }");
                sb.AppendLine(".entry img { max-width: 100%; border: 1px solid #eee; border-radius: 4px; margin: 10px 0; }");
                sb.AppendLine(".annotation { background: #fffde7; border-left: 3px solid #f9a825; padding: 10px 14px; margin: 10px 0; border-radius: 0 4px 4px 0; }");
                sb.AppendLine(".annotation p { margin: 4px 0; }");
                sb.AppendLine(".summary { font-size: 0.9em; color: #777; margin: 2px 0 10px 0; }");
                sb.AppendLine("details { margin-top: 10px; }");
                sb.AppendLine("details summary { cursor: pointer; color: #1F77B4; font-size: 0.9em; }");
                sb.AppendLine(".json-display { background: #f5f5f5; padding: 10px; border-radius: 4px; font-size: 0.8em; overflow-x: auto; max-height: 300px; overflow-y: auto; }");
                sb.AppendLine("@media print { .entry { break-inside: avoid; } details { display: none; } }");
                sb.AppendLine("</style>");
                sb.AppendLine("</head>");
                sb.AppendLine("<body>");
                sb.AppendLine("<h1>Scatter Plot Explorer \u2014 Session Journal</h1>");

                foreach (var entry in entries)
                {
                    if (entry.Deleted) continue;
                    string entryId = $"entry-{entry.Timestamp.Replace(" ", "").Replace(":", "").Replace("-", "")}";
                    sb.AppendLine($"<div class=\"entry\" id=\"{entryId}\">");
                    sb.AppendLine($"  <h2>{System.Net.WebUtility.HtmlEncode(entry.Timestamp)}</h2>");
                    if (!string.IsNullOrEmpty(entry.Summary))
                        sb.AppendLine($"  <p class=\"summary\">{System.Net.WebUtility.HtmlEncode(entry.Summary)}</p>");
                    if (!string.IsNullOrEmpty(entry.Base64Image))
                        sb.AppendLine($"  <img src=\"data:image/png;base64,{entry.Base64Image}\" alt=\"Plot snapshot\"/>");
                    if (!string.IsNullOrEmpty(entry.Annotation))
                    {
                        sb.AppendLine("  <div class=\"annotation\">");
                        foreach (var line in entry.Annotation.Split('\n'))
                            sb.AppendLine($"    <p>{System.Net.WebUtility.HtmlEncode(line.TrimEnd('\r'))}</p>");
                        sb.AppendLine("  </div>");
                    }
                    if (!string.IsNullOrEmpty(entry.Json))
                    {
                        sb.AppendLine("  <details><summary>View state (JSON)</summary>");
                        sb.AppendLine("  <script class=\"view-state\" type=\"application/json\">");
                        sb.AppendLine(entry.Json);
                        sb.AppendLine("  </script>");
                        sb.AppendLine($"  <pre class=\"json-display\">{System.Net.WebUtility.HtmlEncode(entry.Json)}</pre>");
                        sb.AppendLine("  </details>");
                    }
                    sb.AppendLine("</div>");
                }

                sb.AppendLine("</body>");
                sb.AppendLine("</html>");

                File.WriteAllText(journalFile!, sb.ToString(), Encoding.UTF8);
                statusText.Text = $"Saved {entries.Count(en => !en.Deleted)} entries.";
                statusText.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x2C, 0xA0, 0x2C));
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving journal:\n{ex.Message}", "Save Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        };

        win.Show();
    }

    private static void SetComboByText(ComboBox combo, string text)
    {
        for (int i = 0; i < combo.Items.Count; i++)
        {
            if (combo.Items[i].ToString() == text)
            {
                combo.SelectedIndex = i;
                return;
            }
        }
    }

    // ====================================================================
    //  Statistical helpers (no external packages)
    // ====================================================================
    private static double ParseDouble(string s) =>
        s == "NA" ? double.NaN :
        double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out double v) ? v : 0;

    private static string SuperscriptDigits(int n)
    {
        var supers = "⁰¹²³⁴⁵⁶⁷⁸⁹";
        return string.Concat(n.ToString().Select(c => supers[c - '0']));
    }

    private static double BinomialCoeff(int n, int k)
    {
        if (k < 0 || k > n) return 0;
        if (k == 0 || k == n) return 1;
        double result = 1;
        for (int i = 0; i < Math.Min(k, n - k); i++)
            result = result * (n - i) / (i + 1);
        return result;
    }

    private static double[,]? InvertMatrix(double[,] matrix, int size)
    {
        var aug = new double[size, 2 * size];
        for (int i = 0; i < size; i++)
            for (int j = 0; j < size; j++)
            {
                aug[i, j] = matrix[i, j];
                aug[i, j + size] = i == j ? 1.0 : 0.0;
            }

        for (int col = 0; col < size; col++)
        {
            int maxRow = col;
            for (int row = col + 1; row < size; row++)
                if (Math.Abs(aug[row, col]) > Math.Abs(aug[maxRow, col]))
                    maxRow = row;

            if (Math.Abs(aug[maxRow, col]) < 1e-12) return null;

            if (maxRow != col)
                for (int j = 0; j < 2 * size; j++)
                    (aug[col, j], aug[maxRow, j]) = (aug[maxRow, j], aug[col, j]);

            double pivot = aug[col, col];
            for (int j = 0; j < 2 * size; j++) aug[col, j] /= pivot;

            for (int row = 0; row < size; row++)
            {
                if (row == col) continue;
                double f = aug[row, col];
                for (int j = 0; j < 2 * size; j++) aug[row, j] -= f * aug[col, j];
            }
        }

        var result = new double[size, size];
        for (int i = 0; i < size; i++)
            for (int j = 0; j < size; j++)
                result[i, j] = aug[i, j + size];
        return result;
    }

    /// <summary>P-value from the F-distribution (upper tail).</summary>
    private static double FDistPValue(double f, int df1, int df2)
    {
        if (f <= 0 || df1 <= 0 || df2 <= 0) return 1;
        double x = df2 / (df2 + df1 * f);
        return RegIncBeta(df2 / 2.0, df1 / 2.0, x);
    }

    /// <summary>Compute ranks with average ranking for ties.</summary>
    private static double[] ComputeRanks(double[] values)
    {
        int n = values.Length;
        var indexed = new (double val, int idx)[n];
        for (int i = 0; i < n; i++) indexed[i] = (values[i], i);
        Array.Sort(indexed, (a, b) => a.val.CompareTo(b.val));
        var ranks = new double[n];
        int i2 = 0;
        while (i2 < n)
        {
            int j = i2;
            while (j < n - 1 && indexed[j + 1].val == indexed[i2].val) j++;
            double avgRank = (i2 + j) / 2.0 + 1.0; // 1-based average rank
            for (int k = i2; k <= j; k++) ranks[indexed[k].idx] = avgRank;
            i2 = j + 1;
        }
        return ranks;
    }

    /// <summary>Spearman's rank correlation coefficient and p-value.</summary>
    private static (double rho, double pValue) ComputeSpearmanRho(double[] x, double[] y)
    {
        int n = x.Length;
        if (n < 3) return (0, 1);
        var rx = ComputeRanks(x);
        var ry = ComputeRanks(y);
        // Pearson r on ranks
        double mx = 0, my = 0;
        for (int i = 0; i < n; i++) { mx += rx[i]; my += ry[i]; }
        mx /= n; my /= n;
        double num = 0, dx = 0, dy = 0;
        for (int i = 0; i < n; i++)
        {
            double a = rx[i] - mx, b = ry[i] - my;
            num += a * b; dx += a * a; dy += b * b;
        }
        double rho = (dx > 0 && dy > 0) ? num / Math.Sqrt(dx * dy) : 0;
        // p-value via t-approximation
        double t = rho * Math.Sqrt((n - 2) / (1.0 - rho * rho));
        double p = (n > 2 && Math.Abs(rho) < 1) ? TDistPValue(Math.Abs(t), n - 2) : (Math.Abs(rho) >= 1 ? 0 : 1);
        return (rho, p);
    }

    /// <summary>Kendall's tau-b and p-value (normal approximation).</summary>
    private static (double tau, double pValue) ComputeKendallTau(double[] x, double[] y)
    {
        int n = x.Length;
        if (n < 2) return (0, 1);
        long concordant = 0, discordant = 0, tiesX = 0, tiesY = 0;
        for (int i = 0; i < n - 1; i++)
            for (int j = i + 1; j < n; j++)
            {
                double dx = x[i] - x[j], dy = y[i] - y[j];
                if (dx == 0 && dy == 0) { tiesX++; tiesY++; }
                else if (dx == 0) tiesX++;
                else if (dy == 0) tiesY++;
                else if (Math.Sign(dx) == Math.Sign(dy)) concordant++;
                else discordant++;
            }
        long n0 = (long)n * (n - 1) / 2;
        double denom = Math.Sqrt((double)(n0 - tiesX) * (n0 - tiesY));
        double tau = denom > 0 ? (concordant - discordant) / denom : 0;
        // Normal approximation for p-value
        double v0 = (double)n * (n - 1) * (2 * n + 5);
        // Tie corrections would need group counts; use simplified variance
        double variance = v0 / 18.0;
        double z = variance > 0 ? (concordant - discordant) / Math.Sqrt(variance) : 0;
        double p = NormalCdfComplement(Math.Abs(z));
        return (tau, p);
    }

    /// <summary>Two-tailed p-value from standard normal (complementary error function).</summary>
    private static double NormalCdfComplement(double z)
    {
        // Two-tailed: p = 2 * (1 - Phi(|z|)) using erfc approximation
        double t = 1.0 / (1.0 + 0.3275911 * Math.Abs(z));
        double poly = t * (0.254829592 + t * (-0.284496736 + t * (1.421413741 + t * (-1.453152027 + t * 1.061405429))));
        double oneMinusPhi = 0.5 * poly * Math.Exp(-z * z / 2.0);  // upper tail
        return 2.0 * oneMinusPhi; // two-tailed
    }

    /// <summary>Two-tailed p-value from Student's t-distribution.</summary>
    private static double TDistPValue(double t, int df)
    {
        double x = df / (df + t * t);
        return RegIncBeta(df / 2.0, 0.5, x);
    }

    /// <summary>Regularized incomplete beta function I_x(a,b).</summary>
    private static double RegIncBeta(double a, double b, double x)
    {
        if (x <= 0) return 0;
        if (x >= 1) return 1;
        double lnBeta = LogGamma(a) + LogGamma(b) - LogGamma(a + b);
        double front = Math.Exp(a * Math.Log(x) + b * Math.Log(1 - x) - lnBeta);
        if (x < (a + 1) / (a + b + 2))
            return front * BetaCF(a, b, x) / a;
        else
            return 1.0 - front * BetaCF(b, a, 1 - x) / b;
    }

    private static double BetaCF(double a, double b, double x)
    {
        double qab = a + b, qap = a + 1, qam = a - 1;
        double c = 1, d = 1 - qab * x / qap;
        if (Math.Abs(d) < 1e-30) d = 1e-30;
        d = 1 / d;
        double h = d;
        for (int m = 1; m <= 200; m++)
        {
            int m2 = 2 * m;
            double aa = m * (b - m) * x / ((qam + m2) * (a + m2));
            d = 1 + aa * d; if (Math.Abs(d) < 1e-30) d = 1e-30;
            c = 1 + aa / c; if (Math.Abs(c) < 1e-30) c = 1e-30;
            d = 1 / d; h *= d * c;
            aa = -(a + m) * (qab + m) * x / ((a + m2) * (qap + m2));
            d = 1 + aa * d; if (Math.Abs(d) < 1e-30) d = 1e-30;
            c = 1 + aa / c; if (Math.Abs(c) < 1e-30) c = 1e-30;
            d = 1 / d;
            double del = d * c;
            h *= del;
            if (Math.Abs(del - 1) < 1e-10) break;
        }
        return h;
    }

    /// <summary>Log-gamma via Lanczos approximation.</summary>
    private static double LogGamma(double x)
    {
        double[] c = { 76.18009172947146, -86.50532032941677, 24.01409824083091,
                       -1.231739572450155, 0.1208650973866179e-2, -0.5395239384953e-5 };
        double y = x, tmp = x + 5.5;
        tmp -= (x + 0.5) * Math.Log(tmp);
        double ser = 1.000000000190015;
        for (int j = 0; j < 6; j++) ser += c[j] / ++y;
        return -tmp + Math.Log(2.5066282746310005 * ser / x);
    }
}
