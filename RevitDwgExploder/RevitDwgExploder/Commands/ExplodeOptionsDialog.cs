using System;
using System.Drawing;
using System.Windows.Forms;

namespace RevitDwgExploder.Commands
{
    internal class ExplodeOptions
    {
        public bool SimplifyGeometry = true;
        public bool RecreateText = true;
        public bool SkipRecreatedTextLines = true;
        public bool ConvertHatches = false;
    }

    /// <summary>
    /// Diálogo de opciones previo a explotar. Se muestra antes de abrir
    /// ninguna transacción.
    /// </summary>
    internal class ExplodeOptionsDialog : Form
    {
        private readonly CheckBox _simplify;
        private readonly CheckBox _text;
        private readonly CheckBox _skipTextLines;
        private readonly CheckBox _hatches;

        public ExplodeOptions Options { get; private set; }

        public ExplodeOptionsDialog(int importCount)
        {
            Text = "EMASY — Explotar DWG";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(460, 310);
            Font = new Font("Segoe UI", 9f);

            var header = new Label
            {
                Text = importCount == 1
                    ? "Se explotará 1 DWG de la vista activa."
                    : $"Se explotarán {importCount} DWG de la vista activa.",
                Location = new Point(16, 16),
                Size = new Size(430, 20),
                Font = new Font("Segoe UI", 10f, FontStyle.Bold),
            };

            _simplify = NewCheck(
                "Optimizar geometría (recomendado)",
                "Une los segmentos alineados y reconstruye los arcos en vez de dejarlos\n" +
                "troceados en muchas líneas rectas. Genera bastantes menos elementos.",
                48, true);

            _text = NewCheck(
                "Recrear los textos del DWG como texto de Revit",
                "Extrae las cadenas reales y las crea como TextNote, ajustadas a la\n" +
                "escala de la vista.",
                112, true);

            _skipTextLines = NewCheck(
                "No duplicar el texto recreado",
                "Omite las líneas de las capas cuyo texto ya se recreó, para que no\n" +
                "queden dibujadas debajo del texto nuevo.",
                176, true);

            _hatches = NewCheck(
                "Convertir sombreados (hatch) a regiones rellenas de Revit",
                "Los sombreados macizos del DWG se crean como Filled Region nativas.\n" +
                "Si lo dejas desmarcado, se dibujan como líneas igual que el resto.",
                228, false);

            var ok = new Button
            {
                Text = "Explotar",
                DialogResult = DialogResult.OK,
                Location = new Point(258, 268),
                Size = new Size(90, 28),
            };

            var cancel = new Button
            {
                Text = "Cancelar",
                DialogResult = DialogResult.Cancel,
                Location = new Point(356, 268),
                Size = new Size(90, 28),
            };

            Controls.Add(header);
            Controls.Add(_simplify);
            Controls.Add(_text);
            Controls.Add(_skipTextLines);
            Controls.Add(_hatches);
            Controls.Add(ok);
            Controls.Add(cancel);

            AcceptButton = ok;
            CancelButton = cancel;

            _text.CheckedChanged += (s, e) => _skipTextLines.Enabled = _text.Checked;
        }

        private static CheckBox NewCheck(string title, string help, int top, bool isChecked)
        {
            var box = new CheckBox
            {
                Text = title + Environment.NewLine + help,
                Checked = isChecked,
                Location = new Point(20, top),
                Size = new Size(420, 56),
                AutoSize = false,
            };
            return box;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (DialogResult == DialogResult.OK)
            {
                Options = new ExplodeOptions
                {
                    SimplifyGeometry = _simplify.Checked,
                    RecreateText = _text.Checked,
                    SkipRecreatedTextLines = _text.Checked && _skipTextLines.Checked,
                    ConvertHatches = _hatches.Checked,
                };
            }

            base.OnFormClosing(e);
        }
    }
}
