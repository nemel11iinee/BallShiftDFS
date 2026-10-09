using System;
using System.Collections.Generic;

namespace BallShift
{
    /// <summary>
    /// Запись списка O: индекс узла в хранилище поиска и его приоритет.
    /// Приоритет — f(n) = g(n) + h(n); при равных f раньше извлекается узел с большим g
    /// (более глубокий, т.е. ближе к цели) — это сокращает число раскрытых узлов.
    /// </summary>
    public struct OpenEntry
    {
        public int F;
        public int G;
        public int Node;

        public OpenEntry(int f, int g, int node) { F = f; G = g; Node = node; }

        /// <summary>true, если запись a должна быть извлечена раньше записи b.</summary>
        public static bool HasHigherPriority(OpenEntry a, OpenEntry b)
            => a.F < b.F || (a.F == b.F && a.G > b.G);
    }

    /// <summary>
    /// Абстрактный тип данных "очередь с приоритетом" — то, что требуется от списка O в алгоритмах
    /// эвристического поиска (A*, жадный поиск и др.). Набор операций:
    ///   Insert     — добавить узел вместе с его приоритетом f;
    ///   ExtractMin — извлечь (и удалить) узел с наименьшим f;
    ///   Count/IsEmpty — размер и проверка на пустоту.
    /// Обновление приоритета уже лежащего в O узла (нашли более короткий путь) реализуется
    /// добавлением новой записи; устаревшую запись вызывающий код пропускает при извлечении
    /// ("ленивое удаление"). Конкретная структура данных определяет лишь стоимость операций.
    /// </summary>
    public interface IOpenList
    {
        string Name { get; }
        int Count { get; }
        void Insert(OpenEntry entry);
        OpenEntry ExtractMin();
    }

    public enum OpenListKind
    {
        BinaryHeap,
        SortedArray,
        UnsortedList,
        Buckets
    }

    public static class OpenListFactory
    {
        public static IOpenList Create(OpenListKind kind)
        {
            switch (kind)
            {
                case OpenListKind.BinaryHeap: return new BinaryHeapOpenList();
                case OpenListKind.SortedArray: return new SortedArrayOpenList();
                case OpenListKind.UnsortedList: return new UnsortedOpenList();
                case OpenListKind.Buckets: return new BucketOpenList();
                default: throw new ArgumentOutOfRangeException(nameof(kind));
            }
        }

        public static string Describe(OpenListKind kind)
        {
            switch (kind)
            {
                case OpenListKind.BinaryHeap: return "Двоичная куча";
                case OpenListKind.SortedArray: return "Упорядоченный массив";
                case OpenListKind.UnsortedList: return "Неупорядоченный список";
                case OpenListKind.Buckets: return "Корзины по значению f";
                default: return kind.ToString();
            }
        }
    }

    /// <summary>
    /// Двоичная куча (min-heap) на массиве: Insert и ExtractMin за O(log n). Основная реализация.
    /// </summary>
    public sealed class BinaryHeapOpenList : IOpenList
    {
        private OpenEntry[] _items = new OpenEntry[1024];
        private int _count;

        public string Name => OpenListFactory.Describe(OpenListKind.BinaryHeap);
        public int Count => _count;

        public void Insert(OpenEntry entry)
        {
            if (_count == _items.Length) Array.Resize(ref _items, _count * 2);
            int i = _count++;
            while (i > 0)
            {
                int parent = (i - 1) / 2;
                if (!OpenEntry.HasHigherPriority(entry, _items[parent])) break;
                _items[i] = _items[parent];
                i = parent;
            }
            _items[i] = entry;
        }

        public OpenEntry ExtractMin()
        {
            if (_count == 0) throw new InvalidOperationException("Список O пуст.");
            var result = _items[0];
            var last = _items[--_count];
            int i = 0;
            while (true)
            {
                int child = 2 * i + 1;
                if (child >= _count) break;
                if (child + 1 < _count && OpenEntry.HasHigherPriority(_items[child + 1], _items[child])) child++;
                if (!OpenEntry.HasHigherPriority(_items[child], last)) break;
                _items[i] = _items[child];
                i = child;
            }
            if (_count > 0) _items[i] = last;
            return result;
        }
    }

    /// <summary>
    /// Массив, всегда упорядоченный по убыванию приоритета (минимум — в конце).
    /// Позиция вставки ищется двоичным поиском за O(log n), но сам сдвиг элементов — O(n);
    /// ExtractMin — O(1).
    /// </summary>
    public sealed class SortedArrayOpenList : IOpenList
    {
        private readonly List<OpenEntry> _items = new List<OpenEntry>();

        public string Name => OpenListFactory.Describe(OpenListKind.SortedArray);
        public int Count => _items.Count;

        public void Insert(OpenEntry entry)
        {
            // Элементы лежат от "худшего" приоритета к "лучшему"; ищем первую позицию,
            // на которой лежит запись с приоритетом выше, чем у вставляемой.
            int lo = 0, hi = _items.Count;
            while (lo < hi)
            {
                int mid = (lo + hi) / 2;
                if (OpenEntry.HasHigherPriority(_items[mid], entry)) hi = mid;
                else lo = mid + 1;
            }
            _items.Insert(lo, entry);
        }

        public OpenEntry ExtractMin()
        {
            if (_items.Count == 0) throw new InvalidOperationException("Список O пуст.");
            var result = _items[_items.Count - 1];
            _items.RemoveAt(_items.Count - 1);
            return result;
        }
    }

    /// <summary>
    /// Неупорядоченный список: Insert — O(1), ExtractMin — полный просмотр списка за O(n).
    /// Самая простая реализация; в работе используется для демонстрации разницы в стоимости.
    /// </summary>
    public sealed class UnsortedOpenList : IOpenList
    {
        private readonly List<OpenEntry> _items = new List<OpenEntry>();

        public string Name => OpenListFactory.Describe(OpenListKind.UnsortedList);
        public int Count => _items.Count;

        public void Insert(OpenEntry entry) => _items.Add(entry);

        public OpenEntry ExtractMin()
        {
            if (_items.Count == 0) throw new InvalidOperationException("Список O пуст.");
            int best = 0;
            for (int i = 1; i < _items.Count; i++)
                if (OpenEntry.HasHigherPriority(_items[i], _items[best])) best = i;

            var result = _items[best];
            _items[best] = _items[_items.Count - 1]; // порядок в списке не важен
            _items.RemoveAt(_items.Count - 1);
            return result;
        }
    }

    /// <summary>
    /// Корзины по значению f (bucket queue): f в этой задаче — небольшое целое, поэтому для каждого
    /// значения f заводится свой стек, а указатель на минимальную непустую корзину движется вперёд.
    /// Insert — O(1), ExtractMin — O(1) амортизированно. Внутри корзины извлекается последняя
    /// добавленная запись (LIFO) — как правило, самая глубокая.
    /// </summary>
    public sealed class BucketOpenList : IOpenList
    {
        private readonly List<Stack<OpenEntry>> _buckets = new List<Stack<OpenEntry>>();
        private int _min;
        private int _count;

        public string Name => OpenListFactory.Describe(OpenListKind.Buckets);
        public int Count => _count;

        public void Insert(OpenEntry entry)
        {
            while (_buckets.Count <= entry.F) _buckets.Add(new Stack<OpenEntry>());
            _buckets[entry.F].Push(entry);
            if (_count == 0 || entry.F < _min) _min = entry.F;
            _count++;
        }

        public OpenEntry ExtractMin()
        {
            if (_count == 0) throw new InvalidOperationException("Список O пуст.");
            while (_buckets[_min].Count == 0) _min++;
            _count--;
            return _buckets[_min].Pop();
        }
    }
}
