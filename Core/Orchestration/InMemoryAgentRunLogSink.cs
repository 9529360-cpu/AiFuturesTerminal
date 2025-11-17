using System;
using System.Collections.Generic;
using System.Threading;
using AiFuturesTerminal.Core.Orchestration;

namespace AiFuturesTerminal.Core.Orchestration
{
    public sealed class InMemoryAgentRunLogSink : IAgentRunLogSink
    {
        private readonly AgentRunLog[] _buffer;
        private int _index = 0;
        private int _count = 0;
        private readonly ReaderWriterLockSlim _lock = new ReaderWriterLockSlim();

        public InMemoryAgentRunLogSink(int capacity = 500)
        {
            if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            _buffer = new AgentRunLog[capacity];
        }

        public void Append(AgentRunLog log)
        {
            if (log == null) return;
            _lock.EnterWriteLock();
            try
            {
                _buffer[_index] = log;
                _index = (_index + 1) % _buffer.Length;
                if (_count < _buffer.Length) _count++;
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }

        public IReadOnlyList<AgentRunLog> Snapshot(int maxCount)
        {
            if (maxCount <= 0) return Array.Empty<AgentRunLog>();
            _lock.EnterReadLock();
            try
            {
                var take = Math.Min(maxCount, _count);
                var result = new List<AgentRunLog>(take);
                for (int i = 0; i < take; i++)
                {
                    int idx = (_index - 1 - i + _buffer.Length) % _buffer.Length;
                    var item = _buffer[idx];
                    if (item != null) result.Add(item);
                }
                return result;
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }
    }
}
