using System;
using System.Collections.Generic;
using System.Text;

namespace BallShift
{
    /// <summary>Тип хода, которым было получено состояние (нужен только для отображения человеку).</summary>
    public enum MoveKind : byte
    {
        None,
        RowLeft,
        RowRight,
        ColUp,
        ColDown
    }

    /// <summary>
    /// Состояние задачи "Двигаем шарики": поле 4x4, в каждой клетке шарик одного из 4 цветов (0..3).
    ///
    /// Поле хранится не как int[,]-массив, а упаковано в одно 32-битное число: под каждую
    /// клетку отведено ровно 2 бита (2 бита x 16 клеток = 32 бита — значение клетки 0..3
    /// как раз укладывается в 2 бита). Благодаря этому состояние — это одно число, а не
    /// объект с отдельным массивом внутри: не нужна лишняя аллокация на каждое состояние,
    /// а сравнение состояний (Equals/GetHashCode) — это сравнение двух чисел, а не обход
    /// 16 клеток. Это важно, т.к. в процессе поиска создаются и сравниваются миллионы
    /// состояний, и раньше именно это (плюс строка с описанием хода на каждое состояние)
    /// приводило к нехватке памяти при глубоком поиске.
    /// </summary>
    public class State : IEquatable<State>
    {
        public const int Size = 4;

        private readonly uint _packed;

        /// <summary>Упакованное представление поля (2 бита на клетку).</summary>
        public uint Packed => _packed;

        public State Parent { get; }
        public MoveKind Move { get; }
        public int MoveIndex { get; } // номер строки/столбца, к которому относится Move (-1, если хода не было)
        public int Depth { get; }

        private State(uint packed, State parent, MoveKind move, int moveIndex)
        {
            _packed = packed;
            Parent = parent;
            Move = move;
            MoveIndex = moveIndex;
            Depth = parent == null ? 0 : parent.Depth + 1;
        }

        /// <summary>Строит состояние (без родителя — как начальное) из обычного 4x4 массива.</summary>
        public static State FromGrid(int[,] grid)
        {
            uint packed = 0;
            for (int r = 0; r < Size; r++)
                for (int c = 0; c < Size; c++)
                    packed = SetCell(packed, r * Size + c, grid[r, c]);
            return new State(packed, null, MoveKind.None, -1);
        }

        /// <summary>Разворачивает упакованное состояние обратно в обычный 4x4 массив — для отрисовки.</summary>
        public int[,] ToGrid()
        {
            var grid = new int[Size, Size];
            for (int r = 0; r < Size; r++)
                for (int c = 0; c < Size; c++)
                    grid[r, c] = GetCell(_packed, r * Size + c);
            return grid;
        }

        private static int GetCell(uint packed, int idx) => (int)((packed >> (idx * 2)) & 0x3);

        private static uint SetCell(uint packed, int idx, int value)
        {
            int shift = idx * 2;
            uint mask = ~(0x3u << shift);
            return (packed & mask) | ((uint)value << shift);
        }

        /// <summary>
        /// Раскрытие состояния: применяем все правила переходов (сдвиг каждой строки
        /// влево/вправо и каждого столбца вверх/вниз) и получаем список дочерних состояний.
        /// </summary>
        public List<State> GenerateChildren()
        {
            var children = new List<State>(16);
            for (int row = 0; row < Size; row++)
            {
                children.Add(ShiftRow(row, left: true));
                children.Add(ShiftRow(row, left: false));
            }
            for (int col = 0; col < Size; col++)
            {
                children.Add(ShiftColumn(col, up: true));
                children.Add(ShiftColumn(col, up: false));
            }
            return children;
        }

        /// <summary>Применяет ход к состоянию и возвращает дочернее состояние (родитель — текущее).</summary>
        public State Apply(MoveKind move, int index)
        {
            switch (move)
            {
                case MoveKind.RowLeft: return ShiftRow(index, left: true);
                case MoveKind.RowRight: return ShiftRow(index, left: false);
                case MoveKind.ColUp: return ShiftColumn(index, up: true);
                case MoveKind.ColDown: return ShiftColumn(index, up: false);
                default: throw new ArgumentException("Недопустимый ход", nameof(move));
            }
        }

        private State ShiftRow(int row, bool left)
        {
            int i0 = row * Size, i1 = i0 + 1, i2 = i0 + 2, i3 = i0 + 3;
            int c0 = GetCell(_packed, i0), c1 = GetCell(_packed, i1), c2 = GetCell(_packed, i2), c3 = GetCell(_packed, i3);

            uint g = _packed;
            if (left)
            {
                g = SetCell(g, i0, c1); g = SetCell(g, i1, c2);
                g = SetCell(g, i2, c3); g = SetCell(g, i3, c0);
            }
            else
            {
                g = SetCell(g, i0, c3); g = SetCell(g, i1, c0);
                g = SetCell(g, i2, c1); g = SetCell(g, i3, c2);
            }
            return new State(g, this, left ? MoveKind.RowLeft : MoveKind.RowRight, row);
        }

        private State ShiftColumn(int col, bool up)
        {
            int i0 = col, i1 = Size + col, i2 = 2 * Size + col, i3 = 3 * Size + col;
            int c0 = GetCell(_packed, i0), c1 = GetCell(_packed, i1), c2 = GetCell(_packed, i2), c3 = GetCell(_packed, i3);

            uint g = _packed;
            if (up)
            {
                g = SetCell(g, i0, c1); g = SetCell(g, i1, c2);
                g = SetCell(g, i2, c3); g = SetCell(g, i3, c0);
            }
            else
            {
                g = SetCell(g, i0, c3); g = SetCell(g, i1, c0);
                g = SetCell(g, i2, c1); g = SetCell(g, i3, c2);
            }
            return new State(g, this, up ? MoveKind.ColUp : MoveKind.ColDown, col);
        }

        /// <summary>
        /// Человекочитаемое описание хода. Строка формируется только тогда, когда реально
        /// нужна (один конкретный шаг анимации найденного пути) — а не для каждого из
        /// миллионов состояний, перебираемых во время самого поиска.
        /// </summary>
        public string DescribeMove()
        {
            switch (Move)
            {
                case MoveKind.RowLeft: return $"Строка {MoveIndex + 1}: сдвиг влево";
                case MoveKind.RowRight: return $"Строка {MoveIndex + 1}: сдвиг вправо";
                case MoveKind.ColUp: return $"Столбец {MoveIndex + 1}: сдвиг вверх";
                case MoveKind.ColDown: return $"Столбец {MoveIndex + 1}: сдвиг вниз";
                default: return "Начальное состояние";
            }
        }

        /// <summary>
        /// Количество шариков каждого цвета. Ходы — это перестановки клеток, поэтому это
        /// количество является инвариантом: если оно отличается у начального и целевого
        /// состояний, целевое состояние принципиально недостижимо.
        /// </summary>
        public int[] ColorCounts()
        {
            var counts = new int[4];
            for (int idx = 0; idx < Size * Size; idx++)
                counts[GetCell(_packed, idx)]++;
            return counts;
        }

        public string ToFileLine()
        {
            var sb = new StringBuilder();
            for (int idx = 0; idx < Size * Size; idx++)
            {
                sb.Append(GetCell(_packed, idx));
                if (idx != Size * Size - 1) sb.Append(',');
            }
            return sb.ToString();
        }

        public bool Equals(State other) => other != null && _packed == other._packed;

        public override bool Equals(object obj) => Equals(obj as State);

        public override int GetHashCode() => unchecked((int)_packed);
    }
}