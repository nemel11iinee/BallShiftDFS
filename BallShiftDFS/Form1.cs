using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace BallShift
{
    public class Form1 : Form
    {
        private readonly Random _rnd = new Random();

        private BallGridControl startGrid;
        private BallGridControl goalGrid;
        private BallGridControl animGrid;

        private Button btnRandom;
        private Button btnSolve;
        private Button btnPlay;
        private Button btnStop;
        private NumericUpDown numMaxDepth;

        private Label lblIterations;
        private Label lblMaxOpen;
        private Label lblOpenEnd;
        private Label lblMaxTotal;
        private Label lblElapsed;
        private Label lblStepInfo;
        private Label lblStatus;

        private const int StepIntervalMs = 750; // период шагов анимации пути
        private const int MoveDurationMs = 500;  // длительность плавного сдвига внутри шага

        private readonly Timer animTimer;
        private List<State> _solutionPath;
        private int _animIndex;

        private static readonly Color BgDark = Color.FromArgb(30, 39, 50);
        private static readonly Color PanelDark = Color.FromArgb(44, 62, 80);
        private static readonly Color Accent = Color.FromArgb(52, 152, 219);
        private static readonly Color AccentGreen = Color.FromArgb(46, 204, 113);
        private static readonly Color AccentRed = Color.FromArgb(231, 76, 60);
        private static readonly Color TextLight = Color.FromArgb(236, 240, 241);

        public Form1()
        {
            animTimer = new Timer { Interval = StepIntervalMs };
            animTimer.Tick += AnimTimer_Tick;

            InitializeUi();
            RandomizeAll();
        }

        // ==================== Построение интерфейса ====================

        private void InitializeUi()
        {
            Text = "Лаб. работа №1 — «Двигаем шарики» (Поиск в глубину, вариант 14)";
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(980, 680);
            Size = new Size(1080, 720);
            BackColor = BgDark;
            Font = new Font("Segoe UI", 9F);

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                BackColor = BgDark,
                Padding = new Padding(16)
            };
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 72));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 28));
            Controls.Add(root);

            var boards = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 1,
                BackColor = BgDark
            };
            boards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.3F));
            boards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.3F));
            boards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.4F));
            root.Controls.Add(boards, 0, 0);

            boards.Controls.Add(BuildStartPanel(), 0, 0);
            boards.Controls.Add(BuildGoalPanel(), 1, 0);
            boards.Controls.Add(BuildAnimPanel(), 2, 0);

            root.Controls.Add(BuildStatsPanel(), 0, 1);
        }

        private GroupBox WrapGroup(string title, Control content)
        {
            var group = new GroupBox
            {
                Text = title,
                Dock = DockStyle.Fill,
                ForeColor = TextLight,
                BackColor = PanelDark,
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
                Margin = new Padding(8),
                Padding = new Padding(10)
            };
            content.Dock = DockStyle.Fill;
            content.BackColor = PanelDark;
            content.Font = new Font("Segoe UI", 9F, FontStyle.Regular);
            group.Controls.Add(content);
            return group;
        }

        private Control BuildStartPanel()
        {
            var layout = new TableLayoutPanel { ColumnCount = 1, RowCount = 3, Dock = DockStyle.Fill };
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 78));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));

            startGrid = new BallGridControl { Editable = true, Dock = DockStyle.Fill, Margin = new Padding(4) };
            layout.Controls.Add(startGrid, 0, 0);

            layout.Controls.Add(MakeHint("Кликайте по шарику — цвет меняется по кругу"), 0, 1);

            btnRandom = MakeButton("Случайная генерация", Accent);
            btnRandom.Click += (s, e) => RandomizeAll();
            layout.Controls.Add(btnRandom, 0, 2);

            return WrapGroup("Начальное состояние", layout);
        }

        private Control BuildGoalPanel()
        {
            var layout = new TableLayoutPanel { ColumnCount = 1, RowCount = 3, Dock = DockStyle.Fill };
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 78));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));

            goalGrid = new BallGridControl { Editable = true, Dock = DockStyle.Fill, Margin = new Padding(4) };
            layout.Controls.Add(goalGrid, 0, 0);

            layout.Controls.Add(MakeHint("Целевая расстановка шариков"), 0, 1);

            return WrapGroup("Целевое состояние", layout);
        }

        private Control BuildAnimPanel()
        {
            var layout = new TableLayoutPanel { ColumnCount = 1, RowCount = 3, Dock = DockStyle.Fill };
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 78));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));

            animGrid = new BallGridControl { Editable = false, Dock = DockStyle.Fill, Margin = new Padding(4) };
            layout.Controls.Add(animGrid, 0, 0);

            lblStepInfo = MakeHint("Шаг: —");
            layout.Controls.Add(lblStepInfo, 0, 1);

            var animButtons = new TableLayoutPanel { ColumnCount = 2, RowCount = 1, Dock = DockStyle.Fill, BackColor = PanelDark };
            animButtons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            animButtons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));

            btnPlay = MakeButton("▶ Анимация пути", AccentGreen);
            btnPlay.Click += BtnPlay_Click;
            btnStop = MakeButton("⏹ Стоп", AccentRed);
            btnStop.Click += (s, e) => StopAnimation();

            animButtons.Controls.Add(btnPlay, 0, 0);
            animButtons.Controls.Add(btnStop, 1, 0);
            layout.Controls.Add(animButtons, 0, 2);

            return WrapGroup("Путь решения (анимация)", layout);
        }

        private Control BuildStatsPanel()
        {
            var outer = new TableLayoutPanel { ColumnCount = 2, RowCount = 1, Dock = DockStyle.Fill, BackColor = BgDark };
            outer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 62));
            outer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38));

            var statsGrid = new TableLayoutPanel { ColumnCount = 2, RowCount = 5, Dock = DockStyle.Fill };
            for (int i = 0; i < 5; i++) statsGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 20));

            lblIterations = MakeStatValue("0");
            lblMaxOpen = MakeStatValue("0");
            lblOpenEnd = MakeStatValue("0");
            lblMaxTotal = MakeStatValue("0");
            lblElapsed = MakeStatValue("0 мс");

            AddStatRow(statsGrid, 0, "Итераций алгоритма:", lblIterations);
            AddStatRow(statsGrid, 1, "Макс. размер списка O:", lblMaxOpen);
            AddStatRow(statsGrid, 2, "Размер списка O на конец поиска:", lblOpenEnd);
            AddStatRow(statsGrid, 3, "Макс. |O| + |C| за весь поиск:", lblMaxTotal);
            AddStatRow(statsGrid, 4, "Время поиска:", lblElapsed);

            outer.Controls.Add(WrapGroup("Статистика поиска", statsGrid), 0, 0);

            var runLayout = new TableLayoutPanel { ColumnCount = 1, RowCount = 3, Dock = DockStyle.Fill };
            runLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            runLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            runLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            var depthLayout = new TableLayoutPanel { ColumnCount = 2, RowCount = 1, Dock = DockStyle.Fill, BackColor = PanelDark };
            depthLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 62));
            depthLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38));

            var lblDepth = new Label
            {
                Text = "Глубина поиска (расширяется авто):",
                ForeColor = TextLight,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft,
                Dock = DockStyle.Fill
            };
            numMaxDepth = new NumericUpDown { Minimum = 1, Maximum = 200, Value = 25, Dock = DockStyle.Fill };
            depthLayout.Controls.Add(lblDepth, 0, 0);
            depthLayout.Controls.Add(numMaxDepth, 1, 0);
            runLayout.Controls.Add(depthLayout, 0, 0);

            btnSolve = MakeButton("Запустить поиск в глубину (DFS)", AccentGreen);
            btnSolve.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
            btnSolve.Click += BtnSolve_Click;
            runLayout.Controls.Add(btnSolve, 0, 1);

            lblStatus = new Label
            {
                Text = "Готово к работе.",
                ForeColor = Color.Silver,
                BackColor = Color.Transparent,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.TopLeft
            };
            runLayout.Controls.Add(lblStatus, 0, 2);

            outer.Controls.Add(WrapGroup("Запуск алгоритма", runLayout), 1, 0);

            return outer;
        }

        private void AddStatRow(TableLayoutPanel grid, int row, string caption, Label valueLabel)
        {
            var cap = new Label
            {
                Text = caption,
                ForeColor = TextLight,
                BackColor = Color.Transparent,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft
            };
            grid.Controls.Add(cap, 0, row);
            grid.Controls.Add(valueLabel, 1, row);
        }

        private Label MakeStatValue(string text) => new Label
        {
            Text = text,
            ForeColor = Accent,
            BackColor = Color.Transparent,
            Font = new Font("Segoe UI", 10F, FontStyle.Bold),
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleRight
        };

        private Label MakeHint(string text) => new Label
        {
            Text = text,
            ForeColor = Color.Silver,
            BackColor = Color.Transparent,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Segoe UI", 8F, FontStyle.Italic)
        };

        private Button MakeButton(string text, Color back)
        {
            var b = new Button
            {
                Text = text,
                Dock = DockStyle.Fill,
                FlatStyle = FlatStyle.Flat,
                BackColor = back,
                ForeColor = Color.White,
                Margin = new Padding(4),
                Cursor = Cursors.Hand
            };
            b.FlatAppearance.BorderSize = 0;
            return b;
        }

        // ==================== Логика ====================

        private void RandomizeAll()
        {
            StopAnimation();
            startGrid.Randomize(_rnd);

            // Целевое состояние получаем, применяя к начальному случайную серию ходов —
            // это гарантирует, что решение существует и алгоритм сможет его найти.
            var s = State.FromGrid(startGrid.Grid);
            int steps = _rnd.Next(5, 13);
            for (int i = 0; i < steps; i++)
            {
                var children = s.GenerateChildren();
                s = children[_rnd.Next(children.Count)];
            }
            goalGrid.Grid = s.ToGrid();

            ClearStats();
            lblStatus.Text = "Сгенерированы случайные начальное и целевое состояния.";
        }

        private void ClearStats()
        {
            lblIterations.Text = "0";
            lblMaxOpen.Text = "0";
            lblOpenEnd.Text = "0";
            lblMaxTotal.Text = "0";
            lblElapsed.Text = "0 мс";
            lblStepInfo.Text = "Шаг: —";
            animGrid.Grid = new int[4, 4];
            _solutionPath = null;
        }

        private async void BtnSolve_Click(object sender, EventArgs e)
        {
            StopAnimation();

            var start = State.FromGrid(startGrid.Grid);
            var goal = State.FromGrid(goalGrid.Grid);

            if (!start.ColorCounts().SequenceEqual(goal.ColorCounts()))
            {
                MessageBox.Show(this,
                    "Количество шариков каждого цвета в начальном и целевом состоянии должно совпадать " +
                    "(ходы только переставляют шарики местами, но не меняют их количество).",
                    "Недостижимое состояние", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            int maxDepth = (int)numMaxDepth.Value;

            // Поиск может занимать заметное время, поэтому выполняем его в фоновом потоке
            // (Task.Run), чтобы форма не "зависала" и оставалась отзывчивой. Progress<T>
            // сам возвращает вызовы в поток интерфейса, так что лейблы обновлять безопасно.
            SetSearchInProgress(true);
            lblStatus.Text = "Идёт поиск...";
            var progress = new Progress<SearchStats>(UpdateStatsLabels);

            (List<State> path, SearchStats stats) result;
            try
            {
                result = await Task.Run(() => DfsSolver.Solve(start, goal, maxDepth, progress));
            }
            finally
            {
                SetSearchInProgress(false);
            }

            UpdateStatsLabels(result.stats);

            if (result.path == null)
            {
                _solutionPath = null;
                lblStatus.Text = "Решение не найдено: целевое состояние недостижимо.";
                MessageBox.Show(this,
                    "Путь к целевому состоянию не найден даже при максимальной глубине поиска.",
                    "Решение не найдено", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (result.stats.LimitExtended)
                numMaxDepth.Value = Math.Min(numMaxDepth.Maximum, Math.Max(numMaxDepth.Minimum, result.stats.DepthLimit));

            _solutionPath = result.path;
            lblStatus.Text = $"Решение найдено! Длина пути: {result.path.Count - 1} ход(ов)."
                + (result.stats.LimitExtended ? $" Глубина автоматически увеличена с {maxDepth} до {result.stats.DepthLimit}." : "")
                + " Нажмите «Анимация пути».";
            _animIndex = 0;
            animGrid.Grid = result.path[0].ToGrid();
            lblStepInfo.Text = $"Шаг 0 из {result.path.Count - 1} (начальное состояние)";
        }

        private void UpdateStatsLabels(SearchStats stats)
        {
            lblIterations.Text = stats.Iterations.ToString("N0");
            lblMaxOpen.Text = stats.MaxOpenSize.ToString("N0");
            lblOpenEnd.Text = stats.OpenSizeAtEnd.ToString("N0");
            lblMaxTotal.Text = stats.MaxTotalSize.ToString("N0");
            lblElapsed.Text = $"{stats.Elapsed.TotalMilliseconds:0} мс";
        }

        private void SetSearchInProgress(bool inProgress)
        {
            btnSolve.Enabled = !inProgress;
            btnRandom.Enabled = !inProgress;
            startGrid.Editable = !inProgress;
            goalGrid.Editable = !inProgress;
            numMaxDepth.Enabled = !inProgress;
            btnSolve.Text = inProgress ? "Идёт поиск..." : "Запустить поиск в глубину (DFS)";
        }

        private void BtnPlay_Click(object sender, EventArgs e)
        {
            if (_solutionPath == null)
            {
                MessageBox.Show(this, "Сначала выполните поиск решения.", "Нет решения",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            _animIndex = 0;
            animTimer.Start();
            btnPlay.Enabled = false;
            AnimTimer_Tick(null, EventArgs.Empty); // сразу показываем начальное состояние
        }

        private void AnimTimer_Tick(object sender, EventArgs e)
        {
            if (_solutionPath == null || _animIndex >= _solutionPath.Count)
            {
                StopAnimation();
                return;
            }

            var st = _solutionPath[_animIndex];

            int hRow = (st.Move == MoveKind.RowLeft || st.Move == MoveKind.RowRight) ? st.MoveIndex : -1;
            int hCol = (st.Move == MoveKind.ColUp || st.Move == MoveKind.ColDown) ? st.MoveIndex : -1;

            if (_animIndex == 0)
            {
                animGrid.Grid = st.ToGrid();
            }
            else
            {
                // Плавно двигаем шарики выбранной строки/столбца к следующему состоянию пути.
                animGrid.FinishMove();
                animGrid.HighlightRowCol(hRow, hCol);
                animGrid.AnimateMove(st.ToGrid(), st.Move, st.MoveIndex, MoveDurationMs);
            }

            lblStepInfo.Text = _animIndex == 0
                ? $"Шаг 0 из {_solutionPath.Count - 1} (начальное состояние)"
                : $"Шаг {_animIndex} из {_solutionPath.Count - 1}: {st.DescribeMove()}";

            _animIndex++;
        }

        private void StopAnimation()
        {
            animTimer.Stop();
            animGrid.FinishMove();
            btnPlay.Enabled = true;
        }
    }
}