using ILGPU;
using ILGPU.Runtime;
using ILGPU.Runtime.Cuda;
using System;
using System.Collections.Generic;

namespace NonParkerSquareFinder
{
    // Runs the first half of the pair check on a graphics card: finding each m where the sum of two of
    // its x values is another of its x values.
    //
    // The card works on keys, which are the x values with enough low bits dropped to fit in 64 bits. If
    // x + y = z then the key of z is the key of x plus the key of y, or one more, so that is what it looks
    // for. That can pick out an m that does not really qualify but can never miss one that does, and the
    // few it picks out get the full check on the CPU.
    internal class GpuChecker : IDisposable
    {
        private readonly Context context;
        private readonly Accelerator accelerator;

        private readonly Action<Index1D, ArrayView<int>, ArrayView<int>, ArrayView<int>> setBounds;
        private readonly Action<Index1D, ArrayView<ulong>, ArrayView<int>, ArrayView<int>, ArrayView<ulong>> sort;
        private readonly Action<Index1D, ArrayView<ulong>, ArrayView<int>, ArrayView<int>, ArrayView<int>, ArrayView<int>> check;

        // One entry for each key, apart from groupStarts which has one for each group
        private MemoryBuffer1D<ulong, Stride1D.Dense> keys;
        private MemoryBuffer1D<ulong, Stride1D.Dense> sorted;
        private MemoryBuffer1D<int, Stride1D.Dense> starts;
        private MemoryBuffer1D<int, Stride1D.Dense> ends;
        private MemoryBuffer1D<int, Stride1D.Dense> flags;
        private MemoryBuffer1D<int, Stride1D.Dense> groupStarts;
        private readonly MemoryBuffer1D<int, Stride1D.Dense> flagCount;

        public string Name => accelerator.Name;

        // How many low bits of an x are dropped to make its key. It leaves every key under 2^63, so two
        // of them can be added
        public int Shift { get; }

        private GpuChecker(Context context, UInt128 totalSearchSpace)
        {
            this.context = context;

            accelerator = context.CreateCudaAccelerator(0);
            setBounds = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>, ArrayView<int>, ArrayView<int>>(SetBounds);
            sort = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<ulong>, ArrayView<int>, ArrayView<int>, ArrayView<ulong>>(Sort);
            check = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<ulong>, ArrayView<int>, ArrayView<int>, ArrayView<int>, ArrayView<int>>(Check);
            flagCount = accelerator.Allocate1D<int>(1);

            Shift = Math.Max(0, 128 - (int)UInt128.LeadingZeroCount(totalSearchSpace) - 63);
        }

        // Returns null when there is no NVIDIA card to use
        public static GpuChecker Create(UInt128 totalSearchSpace)
        {
            var context = Context.Create(builder => builder.Cuda().Optimize(OptimizationLevel.O2));

            if (context.GetCudaDevices().Count == 0)
            {
                context.Dispose();

                return null;
            }

            return new GpuChecker(context, totalSearchSpace);
        }

        // Takes the keys of many m at once, as one group of keys for each m, and returns the groups
        // that need the full check
        public List<int> Check(ulong[] batchKeys, int keyCount, int[] batchGroupStarts, int groupCount)
        {
            var flagged = new List<int>();

            // The card does one batch at a time, and each thread has to claim it before using it
            lock (accelerator)
            {
                using var binding = accelerator.BindScoped();

                Reserve(keyCount, groupCount + 1);

                keys.View.SubView(0, keyCount).BaseView.CopyFromCPU(new ReadOnlySpan<ulong>(batchKeys, 0, keyCount));
                groupStarts.View.SubView(0, groupCount + 1).BaseView.CopyFromCPU(new ReadOnlySpan<int>(batchGroupStarts, 0, groupCount + 1));
                flagCount.MemSetToZero();

                setBounds(groupCount, groupStarts.View, starts.View, ends.View);
                sort(keyCount, keys.View, starts.View, ends.View, sorted.View);
                check(keyCount, sorted.View, starts.View, ends.View, flags.View, flagCount.View);
                accelerator.Synchronize();

                if (flagCount.GetAsArray1D()[0] > 0)
                {
                    var batchFlags = flags.View.SubView(0, keyCount).GetAsArray1D();

                    for (var group = 0; group < groupCount; group++)
                    {
                        if (batchFlags[batchGroupStarts[group]] != 0)
                        {
                            flagged.Add(group);
                        }
                    }

                    flags.MemSetToZero();
                }
            }

            return flagged;
        }

        public void Dispose()
        {
            keys?.Dispose();
            sorted?.Dispose();
            starts?.Dispose();
            ends?.Dispose();
            flags?.Dispose();
            groupStarts?.Dispose();
            flagCount.Dispose();
            accelerator.Dispose();
            context.Dispose();
        }

        private void Reserve(int keyCount, int groupStartCount)
        {
            if (keys == null || keys.Length < keyCount)
            {
                keys?.Dispose();
                sorted?.Dispose();
                starts?.Dispose();
                ends?.Dispose();
                flags?.Dispose();

                var capacity = keyCount + keyCount / 2;

                keys = accelerator.Allocate1D<ulong>(capacity);
                sorted = accelerator.Allocate1D<ulong>(capacity);
                starts = accelerator.Allocate1D<int>(capacity);
                ends = accelerator.Allocate1D<int>(capacity);
                flags = accelerator.Allocate1D<int>(capacity);
                flags.MemSetToZero();
            }

            if (groupStarts == null || groupStarts.Length < groupStartCount)
            {
                groupStarts?.Dispose();
                groupStarts = accelerator.Allocate1D<int>(groupStartCount + groupStartCount / 2);
            }
        }

        // Tells every key where its group starts and ends
        private static void SetBounds(Index1D group, ArrayView<int> groupStarts, ArrayView<int> starts, ArrayView<int> ends)
        {
            var start = groupStarts[group];
            var end = groupStarts[group + 1];

            for (var i = start; i < end; i++)
            {
                starts[i] = start;
                ends[i] = end;
            }
        }

        // Puts one key in its sorted place in its group, which is past every key that is smaller
        private static void Sort(Index1D index, ArrayView<ulong> keys, ArrayView<int> starts, ArrayView<int> ends, ArrayView<ulong> sorted)
        {
            int i = index;
            var key = keys[i];
            var end = ends[i];
            var place = starts[i];

            for (var j = starts[i]; j < end; j++)
            {
                var other = keys[j];

                // Equal keys keep the order they came in so that no two get the same place
                if (other < key || (other == key && j < i))
                {
                    place++;
                }
            }

            sorted[place] = key;
        }

        // Takes one key as y and tries every later key in its group as x
        private static void Check(Index1D index, ArrayView<ulong> sorted, ArrayView<int> starts, ArrayView<int> ends, ArrayView<int> flags, ArrayView<int> flagCount)
        {
            int i = index;
            var y = sorted[i];
            var end = ends[i];
            var sumIndex = i + 1;

            for (var j = i + 1; j < end; j++)
            {
                var sum = sorted[j] + y;

                // The sum only grows as x does, so the place to look for it never moves backwards
                while (sumIndex < end && sorted[sumIndex] < sum)
                {
                    sumIndex++;
                }

                if (sumIndex == end)
                {
                    break;
                }

                if (sorted[sumIndex] - sum <= 1)
                {
                    flags[starts[i]] = 1;
                    Atomic.Add(ref flagCount[0], 1);

                    break;
                }
            }
        }
    }
}
