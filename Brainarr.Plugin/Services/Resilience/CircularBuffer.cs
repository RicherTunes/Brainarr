using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Resilience
{
    /// <summary>
    /// A thread-safe circular buffer implementation for maintaining a sliding window of items.
    /// Used by the circuit breaker to track recent operation results.
    /// </summary>
    public class CircularBuffer<T> : IEnumerable<T>
    {
        private readonly T[] _buffer;
        private readonly object _lock = new object();
        private int _head;
        private int _tail;
        private int _count;
        
        public CircularBuffer(int capacity)
        {
            if (capacity <= 0)
                throw new ArgumentException("Capacity must be greater than zero", nameof(capacity));
            
            _buffer = new T[capacity];
            _head = 0;
            _tail = 0;
            _count = 0;
        }
        
        public int Capacity => _buffer.Length;
        public int Count => _count;
        public bool IsEmpty => _count == 0;
        public bool IsFull => _count == _buffer.Length;
        
        public void Add(T item)
        {
            lock (_lock)
            {
                _buffer[_tail] = item;
                _tail = (_tail + 1) % _buffer.Length;
                
                if (_count < _buffer.Length)
                {
                    _count++;
                }
                else
                {
                    // Buffer is full, move head forward (overwrite oldest)
                    _head = (_head + 1) % _buffer.Length;
                }
            }
        }
        
        public void Clear()
        {
            lock (_lock)
            {
                Array.Clear(_buffer, 0, _buffer.Length);
                _head = 0;
                _tail = 0;
                _count = 0;
            }
        }
        
        public List<T> ToList()
        {
            lock (_lock)
            {
                var result = new List<T>(_count);
                
                if (_count == 0)
                    return result;
                
                if (_head < _tail)
                {
                    // Continuous segment
                    for (int i = _head; i < _tail; i++)
                    {
                        result.Add(_buffer[i]);
                    }
                }
                else
                {
                    // Wrapped around
                    for (int i = _head; i < _buffer.Length; i++)
                    {
                        result.Add(_buffer[i]);
                    }
                    for (int i = 0; i < _tail; i++)
                    {
                        result.Add(_buffer[i]);
                    }
                }
                
                return result;
            }
        }
        
        public int CountWhere(Func<T, bool> predicate)
        {
            lock (_lock)
            {
                return ToList().Count(predicate);
            }
        }
        
        public IEnumerator<T> GetEnumerator()
        {
            return ToList().GetEnumerator();
        }
        
        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}