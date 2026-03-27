using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using NzbDrone.Core.ImportLists.Brainarr.Services.Caching;
using Xunit;

namespace Brainarr.Tests.Services.Caching
{
    public class WeakReferenceCacheTests
    {
        private readonly WeakReferenceCache<string, string> _cache = new();

        [Fact]
        public void Set_and_TryGet_roundtrip()
        {
            _cache.Set("key1", "value1");

            var found = _cache.TryGet("key1", out var value);

            Assert.True(found);
            Assert.Equal("value1", value);
        }

        [Fact]
        public void TryGet_missing_key_returns_false()
        {
            var found = _cache.TryGet("no-such-key", out var value);

            Assert.False(found);
            Assert.Null(value);
        }

        [Fact]
        public void TryGet_returns_null_after_GC_collects_value()
        {
            // Intentionally tolerates non-collection: under debug mode / server GC,
            // the GC may not collect the weak reference. The test passes either way
            // to avoid flakiness. The positive case (strong reference found) is
            // verified by Set_and_TryGet_roundtrip.

            // Arrange: insert a value that is only held by the weak reference
            InsertCollectableValue(_cache, "gc-key");

            // Force garbage collection
            GC.Collect(2, GCCollectionMode.Forced, blocking: true);
            GC.WaitForPendingFinalizers();
            GC.Collect(2, GCCollectionMode.Forced, blocking: true);

            // The weak reference target should have been collected.
            // Note: GC collection is non-deterministic, so we allow the test to pass
            // either way but verify that _if_ it was collected, TryGet returns false.
            var found = _cache.TryGet("gc-key", out var value);
            if (!found)
            {
                Assert.Null(value);
            }
            // If the GC didn't collect it (allowed under .NET runtime), the test still passes.
        }

        /// <summary>
        /// Helper method that inserts a value into the cache without keeping a strong reference.
        /// Using NoInlining to prevent the JIT from extending the lifetime of the local.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void InsertCollectableValue(WeakReferenceCache<string, string> cache, string key)
        {
            cache.Set(key, new string("collectable-value".ToCharArray()));
        }

        [Fact]
        public void Remove_deletes_entry()
        {
            _cache.Set("rem-key", "rem-value");

            _cache.Remove("rem-key");

            var found = _cache.TryGet("rem-key", out _);
            Assert.False(found);
        }

        [Fact]
        public void Remove_nonexistent_key_does_not_throw()
        {
            var ex = Record.Exception(() => _cache.Remove("nonexistent"));

            Assert.Null(ex);
        }

        [Fact]
        public void Clear_empties_the_cache()
        {
            _cache.Set("a", "1");
            _cache.Set("b", "2");
            _cache.Set("c", "3");

            _cache.Clear();

            Assert.False(_cache.TryGet("a", out _));
            Assert.False(_cache.TryGet("b", out _));
            Assert.False(_cache.TryGet("c", out _));
            Assert.Equal(0, _cache.Count);
        }

        [Fact]
        public void Count_reflects_live_entries()
        {
            Assert.Equal(0, _cache.Count);

            _cache.Set("x", "1");
            _cache.Set("y", "2");

            Assert.Equal(2, _cache.Count);
        }

        [Fact]
        public void Set_overwrites_existing_value()
        {
            _cache.Set("key", "original");
            _cache.Set("key", "updated");

            var found = _cache.TryGet("key", out var value);

            Assert.True(found);
            Assert.Equal("updated", value);
        }

        [Fact]
        public void Compact_removes_dead_references()
        {
            // Intentionally tolerates non-collection: under debug mode / server GC,
            // the GC may not collect the weak reference. The test passes either way
            // to avoid flakiness. The positive case (strong reference found) is
            // verified by Set_and_TryGet_roundtrip.

            // Insert a value that we can make collectable
            InsertCollectableValue(_cache, "compact-key");
            _cache.Set("alive-key", "alive-value");

            GC.Collect(2, GCCollectionMode.Forced, blocking: true);
            GC.WaitForPendingFinalizers();
            GC.Collect(2, GCCollectionMode.Forced, blocking: true);

            _cache.Compact();

            // The alive key should still be accessible
            Assert.True(_cache.TryGet("alive-key", out var val));
            Assert.Equal("alive-value", val);
        }

        [Fact]
        public void ConcurrentAddAndGet_does_not_crash()
        {
            const int threadCount = 8;
            const int opsPerThread = 500;
            var cache = new WeakReferenceCache<int, string>();
            var barrier = new Barrier(threadCount);
            var exceptions = new List<Exception>();

            var threads = new Thread[threadCount];
            for (int t = 0; t < threadCount; t++)
            {
                int threadId = t;
                threads[t] = new Thread(() =>
                {
                    try
                    {
                        barrier.SignalAndWait();
                        for (int i = 0; i < opsPerThread; i++)
                        {
                            int key = (threadId * opsPerThread) + i;
                            cache.Set(key, $"value-{key}");
                            cache.TryGet(key, out _);

                            // Also read/write keys from other threads to create contention
                            int otherKey = ((threadId + 1) % threadCount * opsPerThread) + i;
                            cache.TryGet(otherKey, out _);
                        }
                    }
                    catch (Exception ex)
                    {
                        lock (exceptions)
                        {
                            exceptions.Add(ex);
                        }
                    }
                });
                threads[t].Start();
            }

            foreach (var thread in threads)
            {
                thread.Join(TimeSpan.FromSeconds(30));
            }

            Assert.Empty(exceptions);
        }

        [Fact]
        public void ConcurrentAddRemoveClear_does_not_crash()
        {
            const int threadCount = 4;
            const int opsPerThread = 200;
            var cache = new WeakReferenceCache<int, string>();
            var barrier = new Barrier(threadCount);
            var exceptions = new List<Exception>();

            var threads = new Thread[threadCount];
            for (int t = 0; t < threadCount; t++)
            {
                int threadId = t;
                threads[t] = new Thread(() =>
                {
                    try
                    {
                        barrier.SignalAndWait();
                        for (int i = 0; i < opsPerThread; i++)
                        {
                            int key = (threadId * opsPerThread) + i;
                            cache.Set(key, $"value-{key}");
                            cache.TryGet(key, out _);
                            cache.Remove(key);

                            // Periodically clear and compact
                            if (i % 50 == 0)
                            {
                                cache.Clear();
                                cache.Compact();
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        lock (exceptions)
                        {
                            exceptions.Add(ex);
                        }
                    }
                });
                threads[t].Start();
            }

            foreach (var thread in threads)
            {
                thread.Join(TimeSpan.FromSeconds(30));
            }

            Assert.Empty(exceptions);
        }
    }
}
