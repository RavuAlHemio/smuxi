using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Smuxi.Frontend.Http
{
    public class CircleBuffer<T> : ICollection<T>
    {
        protected class Enumerator : IEnumerator<T>
        {
            protected CircleBuffer<T> Buffer;
            protected int CurrentIndex;

            public Enumerator(CircleBuffer<T> buffer)
            {
                Buffer = buffer;
                CurrentIndex = -1;
            }

            public void Dispose()
            {
            }

            public bool MoveNext()
            {
                ++CurrentIndex;
                return (CurrentIndex < Buffer.ElementCount);
            }

            public void Reset()
            {
                CurrentIndex = -1;
            }

            public T Current
            {
                get
                {
                    if (CurrentIndex < 0 || CurrentIndex >= Buffer.ElementCount) {
                        throw new IndexOutOfRangeException();
                    }

                    if (Buffer.ElementCount < Buffer.BackingArray.Length) {
                        return Buffer.BackingArray[CurrentIndex];
                    }

                    return Buffer.BackingArray[(Buffer.FirstIndexIfFull + CurrentIndex) % Buffer.ElementCount];
                }
            }

            object IEnumerator.Current => Current;
        }

        protected T[] BackingArray;
        protected int ElementCount;
        protected int FirstIndexIfFull;

        public CircleBuffer(int capacity)
        {
            BackingArray = new T[capacity];
            ElementCount = 0;
            FirstIndexIfFull = 0;
        }

        public IEnumerator<T> GetEnumerator()
        {
            return new Enumerator(this);
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public void Add(T item)
        {
            if (ElementCount < BackingArray.Length) {
                // just append
                BackingArray[ElementCount++] = item;
                return;
            }

            BackingArray[FirstIndexIfFull] = item;
            FirstIndexIfFull = (FirstIndexIfFull + 1)%BackingArray.Length;
        }

        public void AddRange(IEnumerable<T> items)
        {
            foreach (T item in items) {
                Add(item);
            }
        }

        public void Clear()
        {
            ElementCount = 0;
            for (int i = 0; i < BackingArray.Length; ++i) {
                BackingArray[i] = default(T);
            }
        }

        public bool Contains(T item)
        {
            return this.Any(element => Comparer<T>.Default.Compare(element, item) == 0);
        }

        public void CopyTo(T[] array, int arrayIndex)
        {
            if (ElementCount > array.Length - arrayIndex) {
                throw new ArgumentException($"not enough space in {nameof(array)}", nameof(array));
            }

            if (ElementCount < BackingArray.Length) {
                Array.Copy(BackingArray, 0, array, arrayIndex, ElementCount);
                return;
            }

            Array.Copy(BackingArray, FirstIndexIfFull, array, arrayIndex, BackingArray.Length - FirstIndexIfFull);
            Array.Copy(BackingArray, 0, array, arrayIndex + BackingArray.Length - FirstIndexIfFull, FirstIndexIfFull);
        }

        public bool Remove(T item)
        {
            throw new NotImplementedException();
        }

        public int Count => ElementCount;
        public bool IsReadOnly => false;
    }
}