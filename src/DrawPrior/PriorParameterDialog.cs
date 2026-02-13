namespace DrawAPrior;

/// <summary>
/// Simple dialog that shows labeled numeric fields and returns the values.
/// </summary>
public sealed class PriorParameterDialog : Form
{
    private readonly TextBox[] textBoxes;
    private readonly string[] paramNames;

    public double[]? Results { get; private set; }

    public PriorParameterDialog(string title, string[] names, double[] defaults)
    {
        paramNames = names;
        Text = title;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;

        int rowHeight = 32;
        int topPad = 12;
        int labelWidth = 120;
        int boxWidth = 80;
        int formWidth = labelWidth + boxWidth + 40;

        textBoxes = new TextBox[names.Length];
        for (int i = 0; i < names.Length; i++)
        {
            var lbl = new Label
            {
                Text = names[i] + ":",
                AutoSize = true,
                Location = new Point(12, topPad + i * rowHeight + 4),
            };
            Controls.Add(lbl);

            var tb = new TextBox
            {
                Text = defaults[i].ToString("G"),
                Location = new Point(labelWidth + 16, topPad + i * rowHeight),
                Width = boxWidth,
            };
            Controls.Add(tb);
            textBoxes[i] = tb;
        }

        int btnY = topPad + names.Length * rowHeight + 8;

        var btnOk = new Button
        {
            Text = "OK",
            DialogResult = DialogResult.OK,
            Location = new Point(formWidth / 2 - 90, btnY),
            Size = new Size(75, 28),
        };
        btnOk.Click += BtnOk_Click;
        Controls.Add(btnOk);

        var btnCancel = new Button
        {
            Text = "Cancel",
            DialogResult = DialogResult.Cancel,
            Location = new Point(formWidth / 2 + 10, btnY),
            Size = new Size(75, 28),
        };
        Controls.Add(btnCancel);

        AcceptButton = btnOk;
        CancelButton = btnCancel;
        ClientSize = new Size(formWidth, btnY + 40);
    }

    private void BtnOk_Click(object? sender, EventArgs e)
    {
        var results = new double[textBoxes.Length];
        for (int i = 0; i < textBoxes.Length; i++)
        {
            if (!double.TryParse(textBoxes[i].Text, out double val))
            {
                MessageBox.Show($"Invalid value for {paramNames[i]}.",
                    "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                DialogResult = DialogResult.None;
                return;
            }
            results[i] = val;
        }
        Results = results;
    }
}
