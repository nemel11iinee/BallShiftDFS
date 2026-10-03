using System;
using System.Drawing;
using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace BallShift
{
    /// <summary>
    /// Визуальный виджет поля 4x4. Используется трижды на форме: для начального состояния,
    /// целевого состояния (оба — редактируемые кликом) и для отображения текущего шага анимации
    /// найденного пути (нередактируемый).
    /// </summary>
    public class BallGridControl : UserControl
    {
        public const int Cells = 4;

        public static readonly Color[] Palette =
        {
            Color.FromArgb(231, 76, 60),   // красный
            Color.FromArgb(46, 204, 113),  // зелёный
            Color.FromArgb(52, 152, 219),  // синий
            Color.FromArgb(241, 196, 15)   // жёлтый
        };

        private int[,] _grid = new int[Cells, Cells];
        private int _highlightRow = -1;
        private int _highlightCol = -1;

        // --- состояние плавной анимации одного хода ---
        private Timer _moveTimer;
        private readonly Stopwatch _moveClock = new Stopwatch();
        private int[,] _moveTarget;
        private MoveKind _moveKind;
        private int _moveIndex;
        private int _moveDurationMs;
        private float _moveProgress; // 0..1 после сглаживания

        private bool IsMoving => _moveTarget != null;

        public bool Editable { get; set; } = false;
        public event EventHandler GridChanged;

        public BallGridControl()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint
                    | ControlStyles.UserPaint
                    | ControlStyles.OptimizedDoubleBuffer
                    | ControlStyles.ResizeRedraw, true);
            BackColor = Color.FromArgb(44, 62, 80);
            Size = new Size(220, 220);
            MouseClick += BallGridControl_MouseClick;
        }

        public int[,] Grid
        {
            get => _grid;
            set { CancelMove(); _grid = value; _highlightRow = -1; _highlightCol = -1; Invalidate(); }
        }

        /// <summary>
        /// Плавно сдвигает строку/столбец на одну клетку: шарики едут, а тот, что выходит за край,
        /// появляется с противоположной стороны. По окончании поле принимает значение newGrid.
        /// Если предыдущий ход ещё не завершился, он мгновенно доигрывается.
        /// </summary>
        public void AnimateMove(int[,] newGrid, MoveKind move, int index, int durationMs)
        {
            FinishMove();
            _moveTarget = newGrid;
            _moveKind = move;
            _moveIndex = index;
            _moveDurationMs = Math.Max(1, durationMs);
            _moveProgress = 0f;
            if (_moveTimer == null)
            {
                _moveTimer = new Timer { Interval = 15 };
                _moveTimer.Tick += MoveTimer_Tick;
            }
            _moveClock.Restart();
            _moveTimer.Start();
        }

        /// <summary>Мгновенно доигрывает текущий ход (если он идёт) — поле принимает конечное состояние.</summary>
        public void FinishMove()
        {
            if (!IsMoving) return;
            _moveTimer.Stop();
            _grid = _moveTarget;
            _moveTarget = null;
            Invalidate();
        }

        private void CancelMove()
        {
            if (!IsMoving) return;
            _moveTimer.Stop();
            _moveTarget = null;
        }

        private void MoveTimer_Tick(object sender, EventArgs e)
        {
            float t = (float)_moveClock.ElapsedMilliseconds / _moveDurationMs;
            if (t >= 1f) { FinishMove(); return; }
            _moveProgress = t * t * (3f - 2f * t); // плавный разгон и торможение
            Invalidate();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _moveTimer != null) _moveTimer.Dispose();
            base.Dispose(disposing);
        }

        public void Randomize(Random rnd)
        {
            CancelMove();
            // Ровно по 4 шарика каждого из 4 цветов (16 клеток = 4 цвета x 4 шарика),
            // порядок на поле — случайная перестановка (Fisher-Yates).
            var values = new System.Collections.Generic.List<int>(Cells * Cells);
            for (int color = 0; color < 4; color++)
                for (int k = 0; k < Cells; k++)
                    values.Add(color);

            for (int i = values.Count - 1; i > 0; i--)
            {
                int j = rnd.Next(i + 1);
                int temp = values[i];
                values[i] = values[j];
                values[j] = temp;
            }

            int idx = 0;
            for (int i = 0; i < Cells; i++)
                for (int j = 0; j < Cells; j++)
                    _grid[i, j] = values[idx++];

            _highlightRow = -1;
            _highlightCol = -1;
            Invalidate();
        }

        /// <summary>Подсвечивает строку и/или столбец (для анимации). -1 — не подсвечивать.</summary>
        public void HighlightRowCol(int row, int col)
        {
            _highlightRow = row;
            _highlightCol = col;
            Invalidate();
        }

        private void BallGridControl_MouseClick(object sender, MouseEventArgs e)
        {
            if (!Editable) return;
            FinishMove();

            float cw = Width / (float)Cells;
            float ch = Height / (float)Cells;
            int col = (int)(e.X / cw);
            int row = (int)(e.Y / ch);
            if (row < 0 || row >= Cells || col < 0 || col >= Cells) return;

            _grid[row, col] = (_grid[row, col] + 1) % 4; // клик = следующий цвет по кругу
            Invalidate();
            GridChanged?.Invoke(this, EventArgs.Empty);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            using (var bgBrush = new SolidBrush(BackColor))
                g.FillRectangle(bgBrush, ClientRectangle);

            float cw = Width / (float)Cells;
            float ch = Height / (float)Cells;

            // Фон клеток — статичный, шарики рисуются поверх (движущаяся линия — со смещением).
            for (int i = 0; i < Cells; i++)
            {
                for (int j = 0; j < Cells; j++)
                {
                    var cellRect = new RectangleF(j * cw, i * ch, cw, ch);
                    bool highlighted = (i == _highlightRow) || (j == _highlightCol);

                    var innerRect = RectangleF.Inflate(cellRect, -3, -3);
                    using (var cellBg = new SolidBrush(highlighted
                        ? Color.FromArgb(90, 255, 255, 255)
                        : Color.FromArgb(25, 255, 255, 255)))
                    using (var path = RoundedRect(innerRect, 8))
                        g.FillPath(cellBg, path);
                }
            }

            bool rowMove = IsMoving && (_moveKind == MoveKind.RowLeft || _moveKind == MoveKind.RowRight);
            bool colMove = IsMoving && (_moveKind == MoveKind.ColUp || _moveKind == MoveKind.ColDown);
            float sign = (_moveKind == MoveKind.RowLeft || _moveKind == MoveKind.ColUp) ? -1f : 1f;

            for (int i = 0; i < Cells; i++)
            {
                for (int j = 0; j < Cells; j++)
                {
                    float dx = 0, dy = 0;
                    bool moving = false;
                    if (rowMove && i == _moveIndex) { dx = sign * _moveProgress * cw; moving = true; }
                    if (colMove && j == _moveIndex) { dy = sign * _moveProgress * ch; moving = true; }

                    var cellRect = new RectangleF(j * cw + dx, i * ch + dy, cw, ch);
                    DrawBall(g, cellRect, _grid[i, j]);

                    if (moving)
                    {
                        // Копия шарика с противоположной стороны линии: создаёт эффект "зацикливания".
                        float wrapX = rowMove ? -sign * Cells * cw : 0;
                        float wrapY = colMove ? -sign * Cells * ch : 0;
                        DrawBall(g, new RectangleF(cellRect.X + wrapX, cellRect.Y + wrapY, cw, ch), _grid[i, j]);
                    }
                }
            }

            using (var borderPen = new Pen(Color.FromArgb(52, 73, 94), 2))
                g.DrawRectangle(borderPen, 0, 0, Width - 1, Height - 1);
        }

        private static void DrawBall(Graphics g, RectangleF cellRect, int colorIdx)
        {
            var ballRect = RectangleF.Inflate(cellRect, -cellRect.Width * 0.18f, -cellRect.Height * 0.18f);

            using (var shadow = new SolidBrush(Color.FromArgb(70, 0, 0, 0)))
                g.FillEllipse(shadow, ballRect.X + 2, ballRect.Y + 3, ballRect.Width, ballRect.Height);

            using (var brush = new LinearGradientBrush(
                ballRect,
                ControlPaint.Light(Palette[colorIdx], 0.35f),
                ControlPaint.Dark(Palette[colorIdx], 0.1f),
                LinearGradientMode.ForwardDiagonal))
                g.FillEllipse(brush, ballRect);

            using (var pen = new Pen(Color.FromArgb(130, 255, 255, 255), 1.5f))
                g.DrawEllipse(pen, ballRect);
        }

        private static GraphicsPath RoundedRect(RectangleF r, float radius)
        {
            var path = new GraphicsPath();
            float d = radius * 2;
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}