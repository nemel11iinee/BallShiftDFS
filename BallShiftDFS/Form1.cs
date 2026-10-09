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
        private Button btnCompare;
        private NumericUpDown numMaxDepth;
        private ComboBox cmbAlgorithm;
        private ComboBox cmbOpenList;
        private Label lblGenerated;
        private Label lblBranching;

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
            Text = "Лаб. работы №1 и №3 — «Двигаем шарики» (поиск в глубину и A*)";
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(1000, 740);
            Size = new Size(1100, 800);
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
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 64));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 36));
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

            var statsGrid = new TableLayoutPanel { ColumnCount = 2, RowCount = 7, Dock = DockStyle.Fill };
            for (int i = 0; i < 7; i++) statsGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 100f / 7));

            lblIterations = MakeStatValue("0");
            lblMaxOpen = MakeStatValue("0");
            lblOpenEnd = MakeStatValue("0");
            lblMaxTotal = MakeStatValue("0");
            lblElapsed = MakeStatValue("0 мс");
            lblGenerated = MakeStatValue("—");
            lblBranching = MakeStatValue("—");

            AddStatRow(statsGrid, 0, "Итераций алгоритма:", lblIterations);
            AddStatRow(statsGrid, 1, "Макс. размер списка O:", lblMaxOpen);
            AddStatRow(statsGrid, 2, "Размер списка O на конец поиска:", lblOpenEnd);
            AddStatRow(statsGrid, 3, "Макс. |O| + |C| за весь поиск:", lblMaxTotal);
            AddStatRow(statsGrid, 4, "Время поиска:", lblElapsed);
            AddStatRow(statsGrid, 5, "Сгенерировано узлов (A*):", lblGenerated);
            AddStatRow(statsGrid, 6, "Эфф. коэффициент ветвления b*:", lblBranching);

            outer.Controls.Add(WrapGroup("Статистика поиска", statsGrid), 0, 0);

            var runLayout = new TableLayoutPanel { ColumnCount = 2, RowCount = 5, Dock = DockStyle.Fill };
            runLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
            runLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58));
            for (int i = 0; i < 3; i++) runLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            runLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            runLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            runLayout.Controls.Add(MakeCaption("Алгоритм:"), 0, 0);
            cmbAlgorithm = MakeCombo(
                "Поиск в глубину (ЛР №1)",
                "A*, h1: клетки / 4",
                "A*, h2: покрытие линиями",
                "A*, h = 0 (слепой)");
            cmbAlgorithm.SelectedIndex = 2; // по умолчанию — A* с сильной эвристикой
            cmbAlgorithm.SelectedIndexChanged += (s, e) => UpdateAlgorithmControls();
            runLayout.Controls.Add(cmbAlgorithm, 1, 0);

            runLayout.Controls.Add(MakeCaption("Список O (для A*):"), 0, 1);
            cmbOpenList = MakeCombo(
                OpenListFactory.Describe(OpenListKind.BinaryHeap),
                OpenListFactory.Describe(OpenListKind.SortedArray),
                OpenListFactory.Describe(OpenListKind.UnsortedList),
                OpenListFactory.Describe(OpenListKind.Buckets));
            cmbOpenList.SelectedIndex = 0;
            runLayout.Controls.Add(cmbOpenList, 1, 1);

            runLayout.Controls.Add(MakeCaption("Глубина DFS (расширяется авто):"), 0, 2);
            numMaxDepth = new NumericUpDown { Minimum = 1, Maximum = 200, Value = 25, Dock = DockStyle.Fill };
            runLayout.Controls.Add(numMaxDepth, 1, 2);

            btnSolve = MakeButton("Запустить поиск", AccentGreen);
            btnSolve.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
            btnSolve.Click += BtnSolve_Click;
            btnCompare = MakeButton("Сравнить алгоритмы", Accent);
            btnCompare.Click += (s, e) =>
            {
                using (var dlg = new ComparisonForm()) dlg.ShowDialog(this);
            };
            var buttons = new TableLayoutPanel { ColumnCount = 2, RowCount = 1, Dock = DockStyle.Fill, BackColor = PanelDark };
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            buttons.Controls.Add(btnSolve, 0, 0);
            buttons.Controls.Add(btnCompare, 1, 0);
            runLayout.Controls.Add(buttons, 0, 3);
            runLayout.SetColumnSpan(buttons, 2);

            lblStatus = new Label
            {
                Text = "Готово к работе.",
                ForeColor = Color.Silver,
                BackColor = Color.Transparent,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.TopLeft
            };
            runLayout.Controls.Add(lblStatus, 0, 4);
            runLayout.SetColumnSpan(lblStatus, 2);

            UpdateAlgorithmControls();

            outer.Controls.Add(WrapGroup("Запуск алгоритма", runLayout), 1, 0);

            return outer;
        }

        private Label MakeCaption(string text) => new Label
        {
            Text = text,
            ForeColor = TextLight,
            BackColor = Color.Transparent,
            TextAlign = ContentAlignment.MiddleLeft,
            Dock = DockStyle.Fill
        };

        private ComboBox MakeCombo(params string[] items)
        {
            var cmb = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
            cmb.Items.AddRange(items);
            return cmb;
        }

        /// <summary>Индексы cmbAlgorithm: 0 — DFS, 1 — A* h1, 2 — A* h2, 3 — A* с h = 0.</summary>
        private void UpdateAlgorithmControls()
        {
            bool dfs = cmbAlgorithm.SelectedIndex == 0;
            numMaxDepth.Enabled = dfs;
            cmbOpenList.Enabled = !dfs;
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
            lblGenerated.Text = "—";
            lblBranching.Text = "—";
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
            int algorithm = cmbAlgorithm.SelectedIndex;
            var openKind = (OpenListKind)Math.Max(0, cmbOpenList.SelectedIndex);
            HeuristicKind heuristic = algorithm == 1 ? HeuristicKind.MisplacedCells
                : algorithm == 2 ? HeuristicKind.LineCover : HeuristicKind.Zero;

            // Поиск может занимать заметное время, поэтому выполняем его в фоновом потоке
            // (Task.Run), чтобы форма не "зависала" и оставалась отзывчивой. Progress<T>
            // сам возвращает вызовы в поток интерфейса, так что лейблы обновлять безопасно.
            SetSearchInProgress(true);
            lblStatus.Text = "Идёт поиск...";
            var progress = new Progress<SearchStats>(UpdateStatsLabels);

            (List<State> path, SearchStats stats) result;
            try
            {
                var search = algorithm == 0
                    ? Task.Run(() => DfsSolver.Solve(start, goal, maxDepth, progress))
                    : Task.Run(() => AStarSolver.Solve(start, goal, heuristic, openKind, progress));
                result = await search;
            }
            finally
            {
                SetSearchInProgress(false);
            }

            UpdateStatsLabels(result.stats);

            if (result.path == null)
            {
                _solutionPath = null;
                if (result.stats.Aborted && algorithm != 0)
                {
                    lblStatus.Text = "Поиск прерван: превышен лимит числа узлов.";
                    MessageBox.Show(this,
                        "Число сгенерированных узлов превысило безопасный предел по памяти. " +
                        "Выберите более сильную эвристику (h2) или более простую задачу.",
                        "Поиск прерван", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                lblStatus.Text = "Решение не найдено: целевое состояние недостижимо.";
                MessageBox.Show(this,
                    "Путь к целевому состоянию не найден даже при максимальной глубине поиска.",
                    "Решение не найдено", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (algorithm == 0 && result.stats.LimitExtended)
                numMaxDepth.Value = Math.Min(numMaxDepth.Maximum, Math.Max(numMaxDepth.Minimum, result.stats.DepthLimit));

            _solutionPath = result.path;
            lblStatus.Text = $"Решение найдено! Длина пути: {result.path.Count - 1} ход(ов)."
                + (algorithm != 0 ? " Путь оптимален: эвристика допустима." : "")
                + (algorithm == 0 && result.stats.LimitExtended ? $" Глубина автоматически увеличена с {maxDepth} до {result.stats.DepthLimit}." : "")
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

            if (stats.Generated > 0)
            {
                lblGenerated.Text = stats.Generated.ToString("N0");
                double b = BranchingFactor.Effective(stats.Generated, stats.PathLength);
                lblBranching.Text = stats.Found && !double.IsNaN(b) ? b.ToString("0.00") : "—";
            }
            else
            {
                lblGenerated.Text = "—";
                lblBranching.Text = "—";
            }
        }

        private void SetSearchInProgress(bool inProgress)
        {
            btnSolve.Enabled = !inProgress;
            btnRandom.Enabled = !inProgress;
            startGrid.Editable = !inProgress;
            goalGrid.Editable = !inProgress;
            btnCompare.Enabled = !inProgress;
            cmbAlgorithm.Enabled = !inProgress;
            if (inProgress)
            {
                numMaxDepth.Enabled = false;
                cmbOpenList.Enabled = false;
            }
            else
            {
                UpdateAlgorithmControls();
            }
            btnSolve.Text = inProgress ? "Идёт поиск..." : "Запустить поиск";
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