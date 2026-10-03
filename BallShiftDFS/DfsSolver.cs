using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace BallShift
{
    /// <summary>Статистика одного запуска поиска.</summary>
    public class SearchStats
    {
        public int Iterations;        // сколько раз алгоритм извлекал узел из O (раскрыл узел)
        public int MaxOpenSize;       // максимальный размер списка O за весь процесс
        public int OpenSizeAtEnd;     // размер списка O на момент завершения поиска
        public int MaxTotalSize;      // максимум |O| + |C| за весь процесс поиска
        public bool Found;
        public bool Aborted;          // true, если поиск остановлен досрочно (достигнут абсолютный предел глубины)
        public int DepthLimit;        // текущий (на момент завершения — последний) предел глубины
        public bool LimitExtended;    // true, если пришлось выйти за глубину, заданную пользователем
        public TimeSpan Elapsed;

        public SearchStats Clone() => (SearchStats)MemberwiseClone();
    }

    /// <summary>
    /// Поиск в глубину в пространстве состояний с итеративным углублением.
    ///
    /// Раньше поиск был обычным DFS с фиксированным ограничением глубины и общим множеством
    /// "уже виденных" состояний. Такой поиск неполон: состояние, впервые встреченное на большой
    /// глубине, запрещало дойти до него по короткому пути, поэтому решение часто не находилось,
    /// а множество состояний разрасталось до миллионов и поиск прерывался по памяти.
    ///
    /// Теперь выполняется серия поисков в глубину с растущим пределом глубины (L = нижняя оценка,
    /// L+1, L+2, ...) до тех пор, пока решение не найдётся — если заданной пользователем глубины
    /// не хватает, предел автоматически увеличивается. Это даёт полноту поиска и кратчайший путь.
    ///
    /// Ускорение:
    ///  * состояние — одно 32-битное число (2 бита на клетку), ходы — битовые операции без аллокаций;
    ///  * отсечение по оценке: один ход меняет не более 4 клеток, поэтому до цели не меньше
    ///    ceil(число_несовпадающих_клеток / 4) ходов (если g + оценка > предела — ветка отсекается);
    ///  * список C — хэш-таблица ограниченного размера "состояние -> наименьшая глубина, на которой
    ///    оно встречалось"; повторный заход на ту же или большую глубину отсекается. Если таблица
    ///    заполнилась, новые состояния просто не запоминаются (корректность не страдает, память
    ///    ограничена);
    ///  * не делаем ход, обратный предыдущему.
    /// Список O — стек ещё не раскрытых потомков вдоль текущей ветки.
    /// </summary>
    public static class DfsSolver
    {
        private const int ProgressReportEvery = 20000;
        private const int AbsoluteMaxDepth = 200;  // страховка от бесконечного цикла для недостижимой цели
        private const int TableBits = 23;          // 8M ячеек (~40 МБ)
        private const int ProbeLength = 8;

        public static (List<State> path, SearchStats stats) Solve(
            State start, State goal, int maxDepth, IProgress<SearchStats> progress = null)
        {
            var search = new Search(start.Packed, goal.Packed, maxDepth, progress);
            var moves = search.Run();

            List<State> path = null;
            if (moves != null)
            {
                path = new List<State>(moves.Count + 1) { start };
                var cur = start;
                foreach (int m in moves)
                {
                    cur = cur.Apply((MoveKind)((m >> 2) + 1), m & 3);
                    path.Add(cur);
                }
            }
            return (path, search.Stats);
        }

        private sealed class Search
        {
            private readonly uint _start, _goal;
            private readonly int _softDepth;
            private readonly IProgress<SearchStats> _progress;
            private readonly Stopwatch _sw = Stopwatch.StartNew();
            public readonly SearchStats Stats = new SearchStats();

            private readonly uint[] _keys = new uint[1 << TableBits];
            private readonly byte[] _depths = new byte[1 << TableBits]; // глубина + 1, 0 = пустая ячейка
            private int _closedCount;

            private readonly uint[][] _kidStates = new uint[AbsoluteMaxDepth + 1][];
            private readonly byte[][] _kidMoves = new byte[AbsoluteMaxDepth + 1][];
            private readonly int[] _path = new int[AbsoluteMaxDepth + 1];
            private int _open;

            public Search(uint start, uint goal, int softDepth, IProgress<SearchStats> progress)
            {
                _start = start; _goal = goal; _softDepth = softDepth; _progress = progress;
                for (int i = 0; i < _kidStates.Length; i++)
                {
                    _kidStates[i] = new uint[16];
                    _kidMoves[i] = new byte[16];
                }
            }

            public List<int> Run()
            {
                int limit = Estimate(_start);
                while (limit <= AbsoluteMaxDepth)
                {
                    Stats.DepthLimit = limit;
                    Stats.LimitExtended = limit > _softDepth;
                    Array.Clear(_depths, 0, _depths.Length);
                    _closedCount = 0;
                    _open = 0;

                    if (Dfs(_start, 0, limit, -1))
                    {
                        Stats.Found = true;
                        Finish();
                        var result = new List<int>(limit);
                        for (int i = 0; i < _foundLength; i++) result.Add(_path[i]);
                        return result;
                    }
                    Report();
                    limit++;
                }
                Stats.Aborted = true;
                Finish();
                return null;
            }

            private int _foundLength;

            private bool Dfs(uint state, int depth, int limit, int lastMove)
            {
                Stats.Iterations++;
                if (state == _goal) { _foundLength = depth; return true; }

                // Узел извлечён из O и раскрывается: его потомки попадают в O.
                var states = _kidStates[depth];
                var moves = _kidMoves[depth];
                int n = 0;
                for (int m = 0; m < 16; m++)
                {
                    if (lastMove >= 0 && IsInverse(m, lastMove)) continue;
                    uint child = Apply(state, m);
                    if (depth + 1 + Estimate(child) > limit) continue;
                    if (Seen(child, depth + 1)) continue;
                    states[n] = child; moves[n] = (byte)m; n++;
                }
                _open += n;
                if (_open > Stats.MaxOpenSize) Stats.MaxOpenSize = _open;
                int total = _open + _closedCount;
                if (total > Stats.MaxTotalSize) Stats.MaxTotalSize = total;

                for (int i = n - 1; i >= 0; i--)
                {
                    _open--;
                    _path[depth] = moves[i];
                    if (Dfs(states[i], depth + 1, limit, moves[i])) return true;
                    if (Stats.Iterations % ProgressReportEvery == 0) Report();
                }
                return false;
            }

            private void Report()
            {
                if (_progress == null) return;
                var s = Stats.Clone();
                s.OpenSizeAtEnd = _open;
                s.Elapsed = _sw.Elapsed;
                _progress.Report(s);
            }

            private void Finish()
            {
                Stats.OpenSizeAtEnd = _open;
                _sw.Stop();
                Stats.Elapsed = _sw.Elapsed;
            }

            /// <summary>Возвращает true, если состояние уже встречалось на такой же или меньшей глубине.</summary>
            private bool Seen(uint state, int depth)
            {
                uint mask = (1u << TableBits) - 1;
                uint idx = (state * 2654435761u) >> (32 - TableBits);
                byte d = (byte)(depth + 1);
                for (int p = 0; p < ProbeLength; p++)
                {
                    uint j = (idx + (uint)p) & mask;
                    if (_depths[j] == 0)
                    {
                        _keys[j] = state; _depths[j] = d; _closedCount++;
                        return false;
                    }
                    if (_keys[j] == state)
                    {
                        if (_depths[j] <= d) return true;
                        _depths[j] = d;
                        return false;
                    }
                }
                return false; // таблица переполнена в этой области — просто не запоминаем
            }

            private int Estimate(uint state)
            {
                uint x = state ^ _goal;
                x = (x | (x >> 1)) & 0x55555555u;
                int bits = PopCount(x);
                return (bits + 3) / 4;
            }

            private static int PopCount(uint v)
            {
                v = v - ((v >> 1) & 0x55555555u);
                v = (v & 0x33333333u) + ((v >> 2) & 0x33333333u);
                return (int)((((v + (v >> 4)) & 0x0F0F0F0Fu) * 0x01010101u) >> 24);
            }

            private static bool IsInverse(int a, int b)
            {
                // ходы 0/1 (строка влево/вправо) и 2/3 (столбец вверх/вниз) взаимно обратны при том же индексе
                return (a & 3) == (b & 3) && ((a >> 2) ^ 1) == (b >> 2);
            }
        }

        /// <summary>
        /// Применяет ход к упакованному состоянию. Номер хода m: m >> 2 — вид (0 строка влево,
        /// 1 строка вправо, 2 столбец вверх, 3 столбец вниз), m &amp; 3 — номер строки/столбца.
        /// </summary>
        internal static uint Apply(uint p, int m)
        {
            int kind = m >> 2, i = m & 3;
            if (kind < 2)
            {
                int s = 8 * i;
                uint x = (p >> s) & 0xFFu;
                x = kind == 0 ? ((x >> 2) | (x << 6)) & 0xFFu : ((x << 2) | (x >> 6)) & 0xFFu;
                return (p & ~(0xFFu << s)) | (x << s);
            }
            else
            {
                int s = 2 * i;
                uint x = (p >> s) & 0x03030303u;
                x = kind == 2 ? (x >> 8) | (x << 24) : (x << 8) | (x >> 24);
                return (p & ~(0x03030303u << s)) | (x << s);
            }
        }
    }
}
