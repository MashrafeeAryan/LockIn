using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace LockIn
{
    internal sealed class DerivativeChallenge
    {
        public const int QuestionCount = 5;
        private readonly Random random;
        private readonly HashSet<Tuple<int, int>> asked = new HashSet<Tuple<int, int>>();

        public int Coefficient { get; private set; }
        public int Power { get; private set; }
        public int CorrectAnswers { get; private set; }
        public bool Complete { get { return CorrectAnswers == QuestionCount; } }

        public DerivativeChallenge(Random random)
        {
            this.random = random;
            NextQuestion();
        }

        public bool Answer(int coefficient, int power)
        {
            if (Complete || coefficient != Coefficient * Power || power != Power - 1)
                return false;

            CorrectAnswers++;
            if (!Complete) NextQuestion();
            return true;
        }

        private void NextQuestion()
        {
            do
            {
                Coefficient = random.Next(2, 10);
                if (random.Next(2) == 0) Coefficient = -Coefficient;
                Power = random.Next(2, 7);
            }
            while (!asked.Add(Tuple.Create(Coefficient, Power)));
        }
    }

    internal sealed class DerivativeDialog : Form
    {
        private readonly DerivativeChallenge challenge;
        private readonly Action solved;
        private readonly Label progress;
        private readonly Label question;
        private readonly TextBox coefficient;
        private readonly TextBox power;
        private readonly Label feedback;

        public DerivativeDialog(DerivativeChallenge challenge, Action solved)
        {
            this.challenge = challenge;
            this.solved = solved;
            SuspendLayout();
            Text = "End session early";
            AutoScaleDimensions = new SizeF(96, 96);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(356, 269);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            MaximizeBox = false;
            MinimizeBox = false;
            BackColor = Theme.Background;
            ForeColor = Theme.Text;
            Font = Theme.Font(10);

            progress = new Label
            {
                Bounds = new Rectangle(22, 16, 312, 26),
                Font = Theme.Font(10, FontStyle.Bold),
                ForeColor = Theme.Accent
            };
            question = new Label
            {
                Bounds = new Rectangle(22, 44, 312, 62),
                Font = Theme.Font(22, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleCenter
            };
            Label coefficientLabel = new Label { Text = "Coefficient", Bounds = new Rectangle(22, 114, 145, 24) };
            Label powerLabel = new Label { Text = "Power of x", Bounds = new Rectangle(189, 114, 145, 24) };
            coefficient = new TextBox
            {
                Location = new Point(22, 141),
                Width = 145,
                MaxLength = 5,
                AccessibleName = "Coefficient"
            };
            power = new TextBox
            {
                Location = new Point(189, 141),
                Width = 145,
                MaxLength = 3,
                AccessibleName = "Power of x"
            };
            feedback = new Label
            {
                Bounds = new Rectangle(22, 177, 312, 24),
                ForeColor = Theme.Accent
            };
            FlatButton back = new FlatButton
            {
                Text = "Back to study",
                Bounds = new Rectangle(22, 211, 145, 40)
            };
            FlatButton check = new FlatButton
            {
                Text = "Check",
                Primary = true,
                Bounds = new Rectangle(189, 211, 145, 40)
            };
            back.Click += delegate { Close(); };
            check.Click += delegate { CheckAnswer(); };
            Controls.AddRange(new Control[] { progress, question, coefficientLabel, powerLabel, coefficient, power, feedback, back, check });
            AcceptButton = check;
            CancelButton = back;
            ShowQuestion();
            ResumeLayout(false);
            PerformLayout();
        }

        private void ShowQuestion()
        {
            progress.Text = "Derivative " + (challenge.CorrectAnswers + 1) + " of " + DerivativeChallenge.QuestionCount;
            question.Text = "d/dx (" + challenge.Coefficient + "x" + "²³⁴⁵⁶"[challenge.Power - 2] + ")";
            coefficient.Clear();
            power.Clear();
            feedback.Text = "";
            ActiveControl = coefficient;
        }

        private void CheckAnswer()
        {
            int coefficientValue;
            int powerValue;
            if (!int.TryParse(coefficient.Text, out coefficientValue) || !int.TryParse(power.Text, out powerValue))
            {
                feedback.Text = "Enter a coefficient and a power.";
                return;
            }
            if (!challenge.Answer(coefficientValue, powerValue))
            {
                feedback.Text = "Try again.";
                return;
            }

            if (challenge.Complete) solved();
            else ShowQuestion();
        }
    }
}
