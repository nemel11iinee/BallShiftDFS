using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace BallShift
{
    /// <summary>Статистика одного запуска поиска.</summary>
    public class SearchStats
    {
        public int Iterations;        // сколько раз алгоритм извлекал узел из O
        public int MaxOpenSize;       // максимальный размер списка O за весь процесс
        public int OpenSizeAtEnd;     // размер списка O на момент завершения поиска
        public int MaxTotalSize;      // максимум |O| + |C| за весь процесс поиска
        public bool Found;
        public bool Aborted;          // true, если поиск остановлен из-за превышения лимита состояний
        public TimeSpan Elapsed;

        public SearchStats Clone() => (SearchStats)MemberwiseClone();
    }

    /// <summary>
    /// Поиск в глубину в пространстве состояний.
    /// Список O (открытые узлы, ожидающие раскрытия) реализован как стек — это и даёт
    /// поведение "в глубину". Список C (закрытые, уже раскрытые узлы) — хэш-множество.
    ///
    /// Отдельно ведётся множество "visited" — все состояния, которые когда-либо были
    /// обнаружены (то есть уже лежат в O либо уже перенесены в C). Состояние помечается
    /// посещённым СРАЗУ при добавлении в O, а не только при извлечении из него — иначе
    /// одно и то же состояние может добавляться в O многократно через разные пути.
    /// </summary>
    public static class DfsSolver
    {
        // Раз в сколько итераций сообщать промежуточный прогресс наверх (чтобы форма могла
        // обновлять статистику на экране, пока поиск идёт в фоновом потоке).
        private const int ProgressReportEvery = 20000;

        // Защитный предел на суммарное число когда-либо обнаруженных состояний. Полное
        // пространство состояний этой головоломки (4x4, по 4 шарика 4 цветов) — 63 063 000
        // различных раскладок, и часть из них поиск в глубину без учёта кратчайшего пути
        // вполне может успеть обойти. Лимит защищает от исчерпания памяти: при его
        // достижении поиск корректно останавливается вместо аварийного падения программы.
        private const int DefaultNodeBudget = 3_000_000;

        public static (List<State> path, SearchStats stats) Solve(
            State start, State goal, int maxDepth,
            IProgress<SearchStats> progress = null, int nodeBudget = DefaultNodeBudget)
        {
            var stats = new SearchStats();
            var sw = Stopwatch.StartNew();

            var open = new Stack<State>();      // список O
            var closed = new HashSet<State>();  // список C
            var visited = new HashSet<State>(); // все когда-либо обнаруженные состояния (O ∪ C)

            visited.Add(start);
            open.Push(start);

            while (open.Count > 0)
            {
                if (visited.Count > nodeBudget)
                {
                    stats.Aborted = true;
                    break;
                }

                stats.Iterations++;
                if (open.Count > stats.MaxOpenSize) stats.MaxOpenSize = open.Count;

                int total = open.Count + closed.Count;
                if (total > stats.MaxTotalSize) stats.MaxTotalSize = total;

                var current = open.Pop();

                if (current.Equals(goal))
                {
                    stats.Found = true;
                    stats.OpenSizeAtEnd = open.Count;
                    sw.Stop();
                    stats.Elapsed = sw.Elapsed;
                    return (ReconstructPath(current), stats);
                }

                closed.Add(current);

                if (current.Depth < maxDepth)
                {
                    foreach (var child in current.GenerateChildren())
                    {
                        if (!visited.Contains(child))
                        {
                            visited.Add(child);
                            open.Push(child);
                        }
                    }
                }

                if (progress != null && stats.Iterations % ProgressReportEvery == 0)
                {
                    stats.OpenSizeAtEnd = open.Count;
                    stats.Elapsed = sw.Elapsed;
                    progress.Report(stats.Clone());
                }
            }

            stats.OpenSizeAtEnd = open.Count;
            sw.Stop();
            stats.Elapsed = sw.Elapsed;
            return (null, stats); // решение не найдено (в пределах maxDepth либо из-за лимита узлов)
        }

        private static List<State> ReconstructPath(State end)
        {
            var path = new List<State>();
            var cur = end;
            while (cur != null)
            {
                path.Add(cur);
                cur = cur.Parent;
            }
            path.Reverse();
            return path;
        }
    }
}