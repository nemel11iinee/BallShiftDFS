using System;

namespace BallShift
{
    public enum HeuristicKind
    {
        /// <summary>h(n) = 0: A* вырождается в поиск равных цен (слепой поиск).</summary>
        Zero,
        /// <summary>h1: число несовпадающих с целью клеток, делённое на 4 с округлением вверх.</summary>
        MisplacedCells,
        /// <summary>h2: наименьшее число строк и столбцов, покрывающих все несовпадающие клетки.</summary>
        LineCover
    }

    /// <summary>
    /// Эвристические оценки h(n) — нижние границы числа ходов до цели для головоломки "Двигаем шарики".
    ///
    /// Ослабленная задача: ход сдвигает ровно одну строку или один столбец, т.е. затрагивает
    /// только 4 клетки одной линии. Поэтому
    ///  * h1: за один ход можно исправить не более 4 клеток, значит нужно не меньше
    ///    ceil(M / 4) ходов, где M — число клеток, цвет которых не совпадает с целевым;
    ///  * h2: каждая несовпадающая клетка должна быть затронута хотя бы одним ходом, т.е. её строка
    ///    или её столбец должны быть сдвинуты. Значит, число ходов не меньше размера минимального
    ///    набора линий (строк/столбцов), покрывающего все несовпадающие клетки. Это минимальное
    ///    вершинное покрытие двудольного графа (строки — столбцы, ребро = несовпадающая клетка);
    ///    по теореме Кёнига его размер равен размеру максимального паросочетания.
    /// Обе оценки допустимы (не завышают) и согласованы; h2 >= h1 для любого состояния, т.е. h2
    /// доминирует над h1 (каждая линия покрывает не более 4 клеток, поэтому покрытие >= ceil(M/4)).
    /// </summary>
    public static class Heuristics
    {
        public static string Describe(HeuristicKind kind)
        {
            switch (kind)
            {
                case HeuristicKind.Zero: return "h = 0 (слепой)";
                case HeuristicKind.MisplacedCells: return "h1: клетки / 4";
                case HeuristicKind.LineCover: return "h2: покрытие линиями";
                default: return kind.ToString();
            }
        }

        public static int Estimate(HeuristicKind kind, uint state, uint goal)
        {
            switch (kind)
            {
                case HeuristicKind.MisplacedCells: return MisplacedCells(state, goal);
                case HeuristicKind.LineCover: return LineCover(state, goal);
                default: return 0;
            }
        }

        /// <summary>Битовая маска несовпадающих клеток: младший бит каждой пары битов клетки.</summary>
        private static uint MismatchMask(uint state, uint goal)
        {
            uint x = state ^ goal;
            return (x | (x >> 1)) & 0x55555555u;
        }

        public static int MisplacedCells(uint state, uint goal)
        {
            return (PopCount(MismatchMask(state, goal)) + 3) / 4;
        }

        public static int LineCover(uint state, uint goal)
        {
            uint mask = MismatchMask(state, goal);
            if (mask == 0) return 0;

            // adj[r] — битовая маска столбцов, в которых клетка строки r не совпадает с целью.
            var adj = new int[4];
            for (int r = 0; r < 4; r++)
                for (int c = 0; c < 4; c++)
                    if (((mask >> ((r * 4 + c) * 2)) & 1u) != 0) adj[r] |= 1 << c;

            // Максимальное паросочетание (алгоритм Куна) на двудольном графе 4 + 4 вершины.
            var matchOfCol = new int[] { -1, -1, -1, -1 };
            int matching = 0;
            for (int r = 0; r < 4; r++)
            {
                int visited = 0;
                if (TryMatch(r, adj, matchOfCol, ref visited)) matching++;
            }

            // Страховка: покрытие линиями не может быть меньше h1 (по построению так и есть).
            return Math.Max(matching, MisplacedCells(state, goal));
        }

        private static bool TryMatch(int row, int[] adj, int[] matchOfCol, ref int visited)
        {
            for (int c = 0; c < 4; c++)
            {
                if ((adj[row] >> c & 1) == 0 || (visited >> c & 1) != 0) continue;
                visited |= 1 << c;
                if (matchOfCol[c] < 0 || TryMatch(matchOfCol[c], adj, matchOfCol, ref visited))
                {
                    matchOfCol[c] = row;
                    return true;
                }
            }
            return false;
        }

        private static int PopCount(uint v)
        {
            v = v - ((v >> 1) & 0x55555555u);
            v = (v & 0x33333333u) + ((v >> 2) & 0x33333333u);
            return (int)((((v + (v >> 4)) & 0x0F0F0F0Fu) * 0x01010101u) >> 24);
        }
    }

    /// <summary>Эффективный коэффициент ветвления b*: решение уравнения N + 1 = 1 + b + b^2 + ... + b^d.</summary>
    public static class BranchingFactor
    {
        /// <param name="generated">N — число сгенерированных узлов (без корня)</param>
        /// <param name="depth">d — глубина найденного решения</param>
        public static double Effective(long generated, int depth)
        {
            if (depth <= 0 || generated <= 0) return double.NaN;

            double lo = 1.0, hi = Math.Max(2.0, generated);
            for (int i = 0; i < 100; i++)
            {
                double mid = (lo + hi) / 2;
                if (TotalNodes(mid, depth) < generated + 1) lo = mid; else hi = mid;
            }
            return (lo + hi) / 2;
        }

        private static double TotalNodes(double b, int d)
        {
            double sum = 0, term = 1;
            for (int i = 0; i <= d; i++) { sum += term; term *= b; }
            return sum;
        }
    }
}
