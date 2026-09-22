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
        private static readonly Color BrandColor = Color.FromArgb(38, 70, 122);
        private static readonly Color HelpColor = Color.FromArgb(90, 95, 105);

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
            ShowInTaskbar = false;
            ClientSize = new Size(520, 400);
            BackColor = Color.White;
            Font = new Font("Segoe UI", 9f);

            Controls.Add(BuildHeader(importCount));

            int y = 96;
            _simplify = AddOption(
                ref y,
                "Optimizar geometría",
                "Une los segmentos alineados y reconstruye los arcos en vez de dejarlos troceados " +
                "en muchas líneas rectas. De las mallas dibuja sólo el contorno. Genera bastantes " +
                "menos elementos.",
                true);

            _text = AddOption(
                ref y,
                "Recrear los textos del DWG",
                "Extrae las cadenas reales y las crea como texto de Revit, ajustado al tamaño que " +
                "corresponde a la escala de la vista.",
                true);

            _skipTextLines = AddOption(
                ref y,
                "No duplicar el texto recreado",
                "Omite las líneas de las capas cuyo texto ya se recreó, para que no queden " +
                "dibujadas debajo del texto nuevo.",
                true);

            _hatches = AddOption(
                ref y,
                "Convertir sombreados (hatch) a regiones rellenas",
                "Crea los sombreados macizos como regiones rellenas nativas, con el color de su " +
                "capa en el DWG. Si lo dejas desmarcado, se dibujan como líneas igual que el resto.",
                false);

            var separator = new Panel
            {
                BackColor = Color.FromArgb(225, 228, 233),
                Location = new Point(0, ClientSize.Height - 58),
                Size = new Size(ClientSize.Width, 1),
            };

            var ok = new Button
            {
                Text = "Explotar",
                DialogResult = DialogResult.OK,
                Location = new Point(ClientSize.Width - 212, ClientSize.Height - 44),
                Size = new Size(96, 30),
                BackColor = BrandColor,
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
            };
            ok.FlatAppearance.BorderSize = 0;

            var cancel = new Button
            {
                Text = "Cancelar",
                DialogResult = DialogResult.Cancel,
                Location = new Point(ClientSize.Width - 108, ClientSize.Height - 44),
                Size = new Size(96, 30),
                FlatStyle = FlatStyle.System,
            };

            Controls.Add(separator);
            Controls.Add(ok);
            Controls.Add(cancel);

            AcceptButton = ok;
            CancelButton = cancel;

            _text.CheckedChanged += (s, e) => _skipTextLines.Enabled = _text.Checked;
        }

        private static Panel BuildHeader(int importCount)
        {
            var header = new Panel
            {
                BackColor = BrandColor,
                Location = new Point(0, 0),
                Size = new Size(520, 76),
            };

            header.Controls.Add(new Label
            {
                Text = "EXPLOTAR DWG",
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 14f, FontStyle.Bold),
                Location = new Point(20, 14),
                Size = new Size(400, 28),
                BackColor = Color.Transparent,
            });

            header.Controls.Add(new Label
            {
                Text = importCount == 1
                    ? "1 DWG en la vista activa se convertirá a geometría nativa de Revit."
                    : $"{importCount} DWG en la vista activa se convertirán a geometría nativa de Revit.",
                ForeColor = Color.FromArgb(205, 215, 232),
                Location = new Point(22, 44),
                Size = new Size(480, 20),
                BackColor = Color.Transparent,
            });

            return header;
        }

        private CheckBox AddOption(ref int y, string title, string help, bool isChecked)
        {
            var box = new CheckBox
            {
                Text = title,
                Checked = isChecked,
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                Location = new Point(22, y),
                Size = new Size(470, 22),
            };

            var helpLabel = new Label
            {
                Text = help,
                ForeColor = HelpColor,
                Location = new Point(41, y + 21),
                Size = new Size(455, 40),
            };

            Controls.Add(box);
            Controls.Add(helpLabel);

            y += 74;
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
