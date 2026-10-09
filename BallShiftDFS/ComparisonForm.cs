using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace BallShift
{
    /// <summary>
    /// Окно сравнения алгоритмов (ЛР №3). На наборе задач разной сложности запускаются:
    /// поиск в глубину из ЛР №1 и A* с эвристиками h = 0 (слепой), h1 и h2; затем на одной задаче
    /// сравниваются разные реализации списка O. Показывается статистика поиска и эффективный
    /// коэффициент ветвления b*.
    /// </summary>
    public class ComparisonForm : Form
    {
        // Лимит узлов для A* с h = 0 в сравнении: иначе на сложных задачах слепой поиск
        // занимает слишком много памяти и времени.
        private const int BlindNodeBudget = 3_000_000;

        private readonly DataGridView _grid;
        private readonly Label _status;

        public ComparisonForm()
        {
            Text = "Сравнение алгоритмов и реализаций списка O";
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(1000, 640);
            MinimumSize = new Size(800, 400);
            BackColor = Color.FromArgb(30, 39, 50);
            Font = new Font("Segoe UI", 9F);

            _status = new Label
            {
                Dock = DockStyle.Bottom,
                Height = 44,
                ForeColor = Color.Silver,
                BackColor = Color.Transparent,
                Padding = new Padding(8, 4, 8, 4),
                Text = "Выполняется сравнение..."
            };

            _grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                RowHeadersVisible = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                BackgroundColor = Color.FromArgb(44, 62, 80),
                BorderStyle = BorderStyle.None,
                EnableHeadersVisualStyles = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect
            };
            _grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(52, 152, 219);
            _grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.White;
            _grid.DefaultCellStyle.BackColor = Color.FromArgb(44, 62, 80);
            _grid.DefaultCellStyle.ForeColor = Color.FromArgb(236, 240, 241);
            _grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(52, 73, 94);
            _grid.DefaultCellStyle.SelectionForeColor = Color.White;

            AddColumn("Задача / структура O", 24);
            AddColumn("Алгоритм", 18);
            AddColumn("Длина пути", 9);
            AddColumn("Раскрыто узлов", 12);
            AddColumn("Сгенерировано", 12);
            AddColumn("Макс. |O|", 10);
            AddColumn("Время, мс", 9);
            AddColumn("b*", 6);

            Controls.Add(_grid);
            Controls.Add(_status);

            Shown += async (s, e) => await RunComparisonAsync();
        }

        private void AddColumn(string title, float weight)
        {
            _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = title, FillWeight = weight });
        }

        private async Task RunComparisonAsync()
        {
            var progress = new Progress<string[]>(row =>
            {
                int idx = _grid.Rows.Add(row);
                _grid.FirstDisplayedScrollingRowIndex = idx;
            });
            try
            {
                await Task.Run(() => RunAll(progress));
                _status.Text = "Готово. b* — эффективный коэффициент ветвления (чем ближе к 1, тем лучше эвристика). " +
                               "DFS из ЛР №1 — итеративное углубление с отсечением по нижней оценке.";
            }
            catch (Exception ex)
            {
                _status.Text = "Ошибка сравнения: " + ex.Message;
            }
        }

        private static void RunAll(IProgress<string[]> report)
        {
            // Набор задач разной сложности: цель фиксирована, начальное состояние получено серией
            // случайных ходов из цели (фиксированное зерно — результаты воспроизводимы).
            var rnd = new Random(2024);
            var goal = RandomBoard(rnd);
            int[] scrambleLengths = { 3, 5, 8, 60 };
            var problems = new List<State>();
            foreach (int k in scrambleLengths)
            {
                uint p = goal.Packed;
                for (int i = 0; i < k; i++) p = DfsSolver.Apply(p, rnd.Next(16));
                problems.Add(State.FromPacked(p));
            }

            for (int i = 0; i < problems.Count; i++)
            {
                string name = $"Задача {i + 1} (перемешивание: {scrambleLengths[i]} ходов)";
                var start = problems[i];

                var dfs = DfsSolver.Solve(start, goal, 25);
                report.Report(Row(name, "DFS (ЛР №1)", dfs.stats, dfs.path != null, false));

                foreach (var h in new[] { HeuristicKind.Zero, HeuristicKind.MisplacedCells, HeuristicKind.LineCover })
                {
                    int budget = h == HeuristicKind.Zero ? BlindNodeBudget : AStarSolver.DefaultNodeBudget;
                    var r = AStarSolver.Solve(start, goal, h, OpenListKind.BinaryHeap, null, budget);
                    report.Report(Row(name, "A*, " + Heuristics.Describe(h), r.stats, r.path != null, true));
                }
            }

            // Сравнение реализаций списка O на одной задаче (A*, h2).
            var hard = problems[2];
            report.Report(new[] { "— Реализации списка O (задача 3, A*, h2) —", "", "", "", "", "", "", "" });
            foreach (OpenListKind kind in Enum.GetValues(typeof(OpenListKind)))
            {
                var r = AStarSolver.Solve(hard, goal, HeuristicKind.LineCover, kind);
                report.Report(Row(OpenListFactory.Describe(kind), "A*, h2", r.stats, r.path != null, true));
            }
        }

        private static State RandomBoard(Random rnd)
        {
            var cells = new List<int>();
            for (int color = 0; color < 4; color++)
                for (int k = 0; k < 4; k++) cells.Add(color);
            for (int i = cells.Count - 1; i > 0; i--)
            {
                int j = rnd.Next(i + 1);
                int t = cells[i]; cells[i] = cells[j]; cells[j] = t;
            }
            uint packed = 0;
            for (int i = 0; i < cells.Count; i++) packed |= (uint)cells[i] << (2 * i);
            return State.FromPacked(packed);
        }

        private static string[] Row(string problem, string algorithm, SearchStats s, bool solved, bool isAStar)
        {
            string length = solved ? s.PathLength.ToString() : (s.Aborted ? "лимит" : "—");
            string branching = "—";
            if (solved && isAStar)
            {
                double b = BranchingFactor.Effective(s.Generated, s.PathLength);
                if (!double.IsNaN(b)) branching = b.ToString("0.00");
            }
            return new[]
            {
                problem,
                algorithm,
                length,
                s.Iterations.ToString("N0"),
                isAStar ? s.Generated.ToString("N0") : "—",
                s.MaxOpenSize.ToString("N0"),
                s.Elapsed.TotalMilliseconds.ToString("0"),
                branching
            };
        }
    }
}
