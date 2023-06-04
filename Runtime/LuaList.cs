using System;
using System.Collections;
using System.Collections.Generic;

namespace pytocs.runtime
{
    public class LuaList<T> : IEnumerable<T>
    {
        public readonly List<T> innerList;

        public LuaList()
        {
            innerList = new List<T>();
        }

        public LuaList(IEnumerable<T> collection)
        {
            innerList = new List<T>(collection);
        }

        public int Count => innerList.Count;

        public T this[int index]
        {
            get => innerList[--index];
            set => innerList[--index] = value;
        }

        public void Add(T item)
        {
            innerList.Add(item);
        }

        public void Clear()
        {
            innerList.Clear();
        }

        public bool Contains(T item)
        {
            return innerList.Contains(item);
        }

        public int FindIndex(Predicate<T> match)
        {
            var innerIndex = innerList.FindIndex(match);
            if (innerIndex < 0) return innerIndex;
            return innerIndex + 1;
        }

        public IEnumerator<T> GetEnumerator()
        {
            return new Enumerator(innerList);
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }


        public struct Enumerator : IEnumerator<T>
        {
            private readonly List<T?> _list;
            private int _index;
            private T? _current;

            internal Enumerator(List<T?> list)
            {
                _list = list;
                _index = 0;
                _current = default(T);
            }

            public void Dispose()
            {
            }

            public bool MoveNext()
            {
                if ((uint) _index >= (uint) _list.Count)
                    return MoveNextRare();

                _current = _list[_index];
                ++_index;
                return true;
            }

            private bool MoveNextRare()
            {
                _index = _list.Count + 1;
                _current = default(T);
                return false;
            }

            /// <summary>获取枚举数当前位置的元素。</summary>
            public T? Current => _current;

            object? IEnumerator.Current => Current;

            void IEnumerator.Reset()
            {
                _index = 0;
                _current = default(T);
            }
        }
    }
}