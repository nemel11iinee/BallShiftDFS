using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace BallShift
{
    /// <summary>
    /// Алгоритм A*: список O — очередь с приоритетом f(n) = g(n) + h(n) (см. <see cref="IOpenList"/>),
    /// список C — хэш-таблица "состояние -> узел" (она же служит множеством уже обнаруженных состояний).
    ///
    /// Раскрытие узла отличается от поиска в глубину:
    ///  * для каждого потомка считается g = g(родителя) + 1 и f = g + h;
    ///  * если состояние ещё не встречалось — создаётся узел и запись попадает в O;
    ///  * если встречалось, но найден более короткий путь (меньше g) — путь и g узла обновляются, в O
    ///    добавляется новая запись (старая запись остаётся в O и пропускается при извлечении);
    ///  * проверка цели выполняется при ИЗВЛЕЧЕНИИ узла из O (а не при генерации) — именно тогда
    ///    A* с допустимой эвристикой гарантирует оптимальность найденного пути.
    ///
    /// Узлы хранятся в параллельных массивах (состояние, g, индекс родителя, ход), а не в объектах —
    /// это экономит память: поиск создаёт сотни тысяч узлов.
    /// </summary>
    public static class AStarSolver
    {
        private const int ProgressReportEvery = 20000;
        public const int DefaultNodeBudget = 6_000_000;

        public static (List<State> path, SearchStats stats) Solve(
            State start, State goal, HeuristicKind heuristic, OpenListKind openKind,
            IProgress<SearchStats> progress = null, int nodeBudget = DefaultNodeBudget)
        {
            var stats = new SearchStats();
            var sw = Stopwatch.StartNew();
            uint goalState = goal.Packed;

            IOpenList open = OpenListFactory.Create(openKind);
            var index = new Dictionary<uint, int>();   // состояние -> номер узла (C и обнаруженные состояния)

            var store = new NodeStore();
            int closed = 0;                              // число раскрытых узлов |C|

            uint startState = start.Packed;
            int root = store.Add(startState, 0, -1, 0);
            index[startState] = root;
            open.Insert(new OpenEntry(Heuristics.Estimate(heuristic, startState, goalState), 0, root));

            int foundNode = -1;
            while (open.Count > 0)
            {
                if (stats.Generated > nodeBudget)
                {
                    stats.Aborted = true;
                    break;
                }

                if (open.Count > stats.MaxOpenSize) stats.MaxOpenSize = open.Count;
                int total = open.Count + closed;
                if (total > stats.MaxTotalSize) stats.MaxTotalSize = total;

                var entry = open.ExtractMin();
                if (entry.G != store.G[entry.Node]) continue; // устаревшая запись: к узлу нашли путь короче

                stats.Iterations++;
                int node = entry.Node;
                uint state = store.States[node];

                if (state == goalState)
                {
                    foundNode = node;
                    break;
                }

                closed++;
                int g = store.G[node] + 1;
                for (int m = 0; m < 16; m++)
                {
                    uint child = DfsSolver.Apply(state, m);
                    int childNode;
                    if (index.TryGetValue(child, out childNode))
                    {
                        if (store.G[childNode] <= g) continue;      // известный путь не хуже
                        store.G[childNode] = g;                // нашли более короткий путь
                        store.Parent[childNode] = node;
                        store.Move[childNode] = (byte)m;
                    }
                    else
                    {
                        childNode = store.Add(child, g, node, m);
                        index[child] = childNode;
                        stats.Generated++;
                    }
                    open.Insert(new OpenEntry(g + Heuristics.Estimate(heuristic, child, goalState), g, childNode));
                }

                if (progress != null && stats.Iterations % ProgressReportEvery == 0)
                {
                    var snapshot = stats.Clone();
                    snapshot.OpenSizeAtEnd = open.Count;
                    snapshot.Elapsed = sw.Elapsed;
                    progress.Report(snapshot);
                }
            }

            stats.OpenSizeAtEnd = open.Count;
            sw.Stop();
            stats.Elapsed = sw.Elapsed;

            if (foundNode < 0) return (null, stats);

            stats.Found = true;
            var moveList = new List<int>();
            for (int n = foundNode; store.Parent[n] >= 0; n = store.Parent[n]) moveList.Add(store.Move[n]);
            moveList.Reverse();
            stats.PathLength = moveList.Count;

            var path = new List<State>(moveList.Count + 1) { start };
            var cur = start;
            foreach (int m in moveList)
            {
                cur = cur.Apply((MoveKind)((m >> 2) + 1), m & 3);
                path.Add(cur);
            }
            return (path, stats);
        }

        /// <summary>Хранилище узлов поиска в параллельных массивах (экономнее, чем объект на узел).</summary>
        private sealed class NodeStore
        {
            public uint[] States = new uint[1 << 12];
            public int[] G = new int[1 << 12];
            public int[] Parent = new int[1 << 12];
            public byte[] Move = new byte[1 << 12];
            private int _count;

            public int Add(uint state, int g, int parent, int move)
            {
                if (_count == States.Length)
                {
                    int size = _count * 2;
                    Array.Resize(ref States, size);
                    Array.Resize(ref G, size);
                    Array.Resize(ref Parent, size);
                    Array.Resize(ref Move, size);
                }
                States[_count] = state; G[_count] = g; Parent[_count] = parent; Move[_count] = (byte)move;
                return _count++;
            }
        }
    }
}
