using ILGPU;
using ILGPU.Runtime;
using ILGPU.Runtime.Cuda;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NonParkerSquareFinder
{
    // Runs most of the work for each m on a graphics card. From the factors of c it splits the one large
    // prime into two squares, multiplies out the x values, and then finds each m where the sum of two of
    // its x values is another of its x values.
    //
    // The card only keeps a key for each x, which is the x with enough low bits dropped to fit in 64
    // bits. If x + y = z then the key of z is the key of x plus the key of y, or one more, so that is
    // what it looks for. That can pick out an m that does not really qualify but can never miss one that
    // does, and the few it picks out get the full check on the CPU.
    internal class GpuChecker : IDisposable
    {
        private readonly Context context;
        private readonly Accelerator accelerator;

        private readonly Action<Index1D, ArrayView<ulong>, ArrayView<long>, ArrayView<long>, ArrayView<long>, ArrayView<long>> split;
        private readonly Action<Index1D, BatchViews, KeyViews, ArrayView<int>, ArrayView<int>, int> build;
        private readonly Action<Index1D, ArrayView<int>, ArrayView<int>, ArrayView<int>> setBounds;
        private readonly Action<Index1D, ArrayView<ulong>, ArrayView<int>, ArrayView<int>, ArrayView<ulong>> sort;
        private readonly Action<Index1D, ArrayView<ulong>, ArrayView<int>, ArrayView<int>, ArrayView<int>, ArrayView<int>> check;

        // The two squares that each sieved prime splits into, and what Split needs to pick a base
        private readonly MemoryBuffer1D<int, Stride1D.Dense> primeAs;
        private readonly MemoryBuffer1D<int, Stride1D.Dense> primeBs;
        private readonly MemoryBuffer1D<long, Stride1D.Dense> smallPrimes;
        private readonly MemoryBuffer1D<long, Stride1D.Dense> nonResidues;

        // A copy of the batch
        private MemoryBuffer1D<int, Stride1D.Dense> groupStarts;
        private MemoryBuffer1D<ulong, Stride1D.Dense> plains;
        private MemoryBuffer1D<int, Stride1D.Dense> factorStarts;
        private MemoryBuffer1D<int, Stride1D.Dense> factors;
        private MemoryBuffer1D<int, Stride1D.Dense> largeIndexes;
        private MemoryBuffer1D<ulong, Stride1D.Dense> largePrimes;
        private MemoryBuffer1D<long, Stride1D.Dense> largeAs;
        private MemoryBuffer1D<long, Stride1D.Dense> largeBs;

        // One entry for each key
        private MemoryBuffer1D<ulong, Stride1D.Dense> keys;
        private MemoryBuffer1D<ulong, Stride1D.Dense> sorted;
        private MemoryBuffer1D<long, Stride1D.Dense> res;
        private MemoryBuffer1D<long, Stride1D.Dense> ims;
        private MemoryBuffer1D<int, Stride1D.Dense> starts;
        private MemoryBuffer1D<int, Stride1D.Dense> ends;
        private MemoryBuffer1D<int, Stride1D.Dense> flags;
        private readonly MemoryBuffer1D<int, Stride1D.Dense> flagCount;

        public string Name => accelerator.Name;

        // How many low bits of an x are dropped to make its key. It leaves every key under 2^63, so two
        // of them can be added
        public int Shift { get; }

        private GpuChecker(Context context, UInt128 totalSearchSpace, (ulong P, long A, long B)[] primes)
        {
            this.context = context;

            accelerator = context.CreateCudaAccelerator(0);
            split = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<ulong>, ArrayView<long>, ArrayView<long>, ArrayView<long>, ArrayView<long>>(Split);
            build = accelerator.LoadAutoGroupedStreamKernel<Index1D, BatchViews, KeyViews, ArrayView<int>, ArrayView<int>, int>(Build);
            setBounds = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>, ArrayView<int>, ArrayView<int>>(SetBounds);
            sort = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<ulong>, ArrayView<int>, ArrayView<int>, ArrayView<ulong>>(Sort);
            check = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<ulong>, ArrayView<int>, ArrayView<int>, ArrayView<int>, ArrayView<int>>(Check);

            primeAs = accelerator.Allocate1D(primes.Select(prime => (int)prime.A).ToArray());
            primeBs = accelerator.Allocate1D(primes.Select(prime => (int)prime.B).ToArray());
            flagCount = accelerator.Allocate1D<int>(1);

            // A large prime p is 1 more than a multiple of 4, so a small prime q is a square mod p
            // exactly when p is a square mod q. That lets Split find a base that is not a square mod p
            // from a list of which remainders are not squares for each small prime.
            var bases = new long[] { 3, 5, 7, 11, 13, 17, 19, 23, 29, 31, 37, 41, 43, 47, 53, 59, 61 };

            smallPrimes = accelerator.Allocate1D(bases);
            nonResidues = accelerator.Allocate1D(bases.Select(q =>
            {
                var squares = Enumerable.Range(1, (int)q - 1).Select(n => (long)n * n % q).ToHashSet();

                return Enumerable.Range(1, (int)q - 1).Where(n => !squares.Contains(n)).Sum(n => 1L << n);
            }).ToArray());

            Shift = Math.Max(0, 128 - (int)UInt128.LeadingZeroCount(totalSearchSpace) - 63);
        }

        // Returns null when there is no NVIDIA card to use
        public static GpuChecker Create(UInt128 totalSearchSpace, (ulong P, long A, long B)[] primes)
        {
            var context = Context.Create(builder => builder.Cuda().Optimize(OptimizationLevel.O2));

            if (context.GetCudaDevices().Count == 0)
            {
                context.Dispose();

                return null;
            }

            return new GpuChecker(context, totalSearchSpace, primes);
        }

        // Returns the groups in the batch that need the full check
        public List<int> Check(GpuBatch batch)
        {
            // The card does one batch at a time, and each thread has to claim it before using it
            lock (accelerator)
            {
                using var binding = accelerator.BindScoped();

                BuildKeys(batch);

                return FindFlagged(batch.GroupStarts, batch.GroupCount, batch.KeyCount);
            }
        }

        // The keys the card works out for a batch, and which groups it left for the CPU, so that they
        // can be compared with the CPU's own
        internal ulong[] BuiltKeys(GpuBatch batch, List<int> leftForCpu)
        {
            lock (accelerator)
            {
                using var binding = accelerator.BindScoped();

                BuildKeys(batch);
                accelerator.Synchronize();
                AddFlagged(batch.GroupStarts, batch.GroupCount, batch.KeyCount, leftForCpu);

                return keys.View.SubView(0, batch.KeyCount).GetAsArray1D();
            }
        }

        // Runs only the search for sums, on keys that are already worked out
        internal List<int> CheckKeys(ulong[] batchKeys, int keyCount, int[] batchGroupStarts, int groupCount)
        {
            lock (accelerator)
            {
                using var binding = accelerator.BindScoped();

                ReserveKeys(keyCount);
                Reserve(ref groupStarts, groupCount + 1);
                Upload(keys, batchKeys, keyCount);
                Upload(groupStarts, batchGroupStarts, groupCount + 1);
                flagCount.MemSetToZero();

                return FindFlagged(batchGroupStarts, groupCount, keyCount);
            }
        }

        public void Dispose()
        {
            foreach (var buffer in new MemoryBuffer[] { primeAs, primeBs, smallPrimes, nonResidues, groupStarts, plains, factorStarts, factors,
                largeIndexes, largePrimes, largeAs, largeBs, keys, sorted, res, ims, starts, ends, flags, flagCount })
            {
                buffer?.Dispose();
            }

            accelerator.Dispose();
            context.Dispose();
        }

        private void BuildKeys(GpuBatch batch)
        {
            ReserveKeys(batch.KeyCount);
            Reserve(ref groupStarts, batch.GroupCount + 1);
            Reserve(ref factorStarts, batch.GroupCount + 1);
            Reserve(ref plains, batch.GroupCount);
            Reserve(ref largeIndexes, batch.GroupCount);
            Reserve(ref factors, batch.FactorCount);
            Reserve(ref largePrimes, batch.LargeCount);
            Reserve(ref largeAs, batch.LargeCount);
            Reserve(ref largeBs, batch.LargeCount);

            Upload(groupStarts, batch.GroupStarts, batch.GroupCount + 1);
            Upload(factorStarts, batch.FactorStarts, batch.GroupCount + 1);
            Upload(plains, batch.Plains, batch.GroupCount);
            Upload(largeIndexes, batch.LargeIndexes, batch.GroupCount);
            Upload(factors, batch.Factors, batch.FactorCount);
            Upload(largePrimes, batch.LargePrimes, batch.LargeCount);
            flagCount.MemSetToZero();

            if (batch.LargeCount > 0)
            {
                split(batch.LargeCount, largePrimes.View, smallPrimes.View, nonResidues.View, largeAs.View, largeBs.View);
            }

            build(
                batch.GroupCount,
                new BatchViews
                {
                    GroupStarts = groupStarts.View,
                    Plains = plains.View,
                    FactorStarts = factorStarts.View,
                    Factors = factors.View,
                    LargeIndexes = largeIndexes.View,
                    LargePrimes = largePrimes.View,
                    LargeAs = largeAs.View,
                    LargeBs = largeBs.View,
                },
                new KeyViews { Keys = keys.View, Res = res.View, Ims = ims.View, Flags = flags.View, FlagCount = flagCount.View },
                primeAs.View,
                primeBs.View,
                Shift);
        }

        private List<int> FindFlagged(int[] batchGroupStarts, int groupCount, int keyCount)
        {
            var flagged = new List<int>();

            setBounds(groupCount, groupStarts.View, starts.View, ends.View);
            sort(keyCount, keys.View, starts.View, ends.View, sorted.View);
            check(keyCount, sorted.View, starts.View, ends.View, flags.View, flagCount.View);
            accelerator.Synchronize();
            AddFlagged(batchGroupStarts, groupCount, keyCount, flagged);

            return flagged;
        }

        private void AddFlagged(int[] batchGroupStarts, int groupCount, int keyCount, List<int> flagged)
        {
            if (flagCount.GetAsArray1D()[0] == 0)
            {
                return;
            }

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

        private void ReserveKeys(int keyCount)
        {
            var oldFlags = flags;

            Reserve(ref keys, keyCount);
            Reserve(ref sorted, keyCount);
            Reserve(ref res, keyCount);
            Reserve(ref ims, keyCount);
            Reserve(ref starts, keyCount);
            Reserve(ref ends, keyCount);
            Reserve(ref flags, keyCount);

            if (flags != oldFlags)
            {
                flags.MemSetToZero();
            }
        }

        private void Reserve<T>(ref MemoryBuffer1D<T, Stride1D.Dense> buffer, int count)
            where T : unmanaged
        {
            if (buffer == null || buffer.Length < count)
            {
                buffer?.Dispose();
                buffer = accelerator.Allocate1D<T>(Math.Max(16, count + count / 2));
            }
        }

        private static void Upload<T>(MemoryBuffer1D<T, Stride1D.Dense> buffer, T[] data, int count)
            where T : unmanaged
        {
            if (count > 0)
            {
                buffer.View.SubView(0, count).BaseView.CopyFromCPU(new ReadOnlySpan<T>(data, 0, count));
            }
        }

        // Finds the a > b > 0 with a * a + b * b = p for one large prime, the same way TwoSquares does
        private static void Split(Index1D index, ArrayView<ulong> primes, ArrayView<long> smallPrimes, ArrayView<long> nonResidues, ArrayView<long> largeAs, ArrayView<long> largeBs)
        {
            var p = primes[index];

            // 2 is not a square mod p when p is 5 more than a multiple of 8
            var n = (p & 7) == 5 ? 2UL : 0UL;

            for (var i = 0; n == 0 && i < smallPrimes.Length; i++)
            {
                var q = (ulong)smallPrimes[i];

                if (((nonResidues[i] >> (int)(p % q)) & 1) != 0)
                {
                    n = q;
                }
            }

            // Build sees that 0 and 0 are not the answer and leaves the group for the CPU
            largeAs[index] = 0;
            largeBs[index] = 0;

            if (n == 0)
            {
                return;
            }

            var inverse = p;

            for (var i = 0; i < 5; i++)
            {
                inverse *= 2UL - p * inverse;
            }

            var negativeInverse = 0UL - inverse;
            var one = (0UL - p) % p;
            var montgomeryN = 0UL;

            for (var i = 0UL; i < n; i++)
            {
                montgomeryN += one;

                if (montgomeryN >= p)
                {
                    montgomeryN -= p;
                }
            }

            var exponent = (p - 1) / 4;
            var bit = 62;

            while ((exponent >> bit) == 0)
            {
                bit--;
            }

            var root = one;

            for (; bit >= 0; bit--)
            {
                root = MontgomeryMultiply(root, root, p, negativeInverse);

                if (((exponent >> bit) & 1) != 0)
                {
                    root = MontgomeryMultiply(root, montgomeryN, p, negativeInverse);
                }
            }

            var previous = p;
            var current = MontgomeryMultiply(root, 1, p, negativeInverse);

            while (current > uint.MaxValue || current * current > p)
            {
                var next = previous % current;

                previous = current;
                current = next;
            }

            largeAs[index] = (long)current;
            largeBs[index] = (long)(previous % current);
        }

        // a * b / 2^64 mod p, which needs p to be odd and under 2^63
        private static ulong MontgomeryMultiply(ulong a, ulong b, ulong p, ulong negativeInverse)
        {
            var low = a * b;
            var result = MultiplyHigh(a, b) + MultiplyHigh(low * negativeInverse, p) + (low != 0 ? 1UL : 0UL);

            return result >= p ? result - p : result;
        }

        // The top 64 bits of a * b, which the card has to put together from 32 bit pieces
        private static ulong MultiplyHigh(ulong a, ulong b)
        {
            ulong aLow = (uint)a;
            ulong bLow = (uint)b;
            var aHigh = a >> 32;
            var bHigh = b >> 32;
            var lowHigh = aLow * bHigh;
            var highLow = aHigh * bLow;
            var middle = (aLow * bLow >> 32) + (uint)lowHigh + (uint)highLow;

            return aHigh * bHigh + (lowHigh >> 32) + (highLow >> 32) + (middle >> 32);
        }

        // Multiplies out the x values of one group from its factors, the same way Searcher.Build does,
        // and writes their keys
        private static void Build(Index1D group, BatchViews batch, KeyViews keys, ArrayView<int> primeAs, ArrayView<int> primeBs, int shift)
        {
            var start = batch.GroupStarts[group];
            var firstFactor = batch.FactorStarts[group];
            var smallCount = batch.FactorStarts[group + 1] - firstFactor;
            var large = batch.LargeIndexes[group];
            var factorCount = large < 0 ? smallCount : smallCount + 1;
            var largeA = 0L;
            var largeB = 0L;

            if (large >= 0)
            {
                largeA = batch.LargeAs[large];
                largeB = batch.LargeBs[large];

                if ((ulong)(largeA * largeA + largeB * largeB) != batch.LargePrimes[large])
                {
                    keys.Flags[start] = 1;
                    Atomic.Add(ref keys.FlagCount[0], 1);

                    return;
                }
            }

            // The combinations so far are kept in the group's own stretch of Res and Ims
            keys.Res[start] = (long)batch.Plains[group];
            keys.Ims[start] = 0;

            var combinationCount = 1;
            var next = start;

            for (var factor = 0; factor < factorCount; factor++)
            {
                var a = largeA;
                var b = largeB;
                var power = 1;

                if (factor < smallCount)
                {
                    var packed = batch.Factors[firstFactor + factor];

                    a = primeAs[packed & 0xFFFFFF];
                    b = primeBs[packed & 0xFFFFFF];
                    power = packed >> 24;
                }

                var last = factor == factorCount - 1;

                // The last factor only needs the first half of its choices, as in Searcher.Build
                for (var choice = last ? power : 2 * power; choice >= 0; choice--)
                {
                    // (a + bi) to the power of choice, times (a - bi) for the rest of 2 * power
                    var re = 1L;
                    var im = 0L;

                    for (var i = 0; i < 2 * power; i++)
                    {
                        var sign = i < choice ? 1 : -1;
                        var newRe = re * a - sign * im * b;

                        im = im * a + sign * re * b;
                        re = newRe;
                    }

                    if (last)
                    {
                        var stop = choice < power ? combinationCount : combinationCount / 2;

                        for (var i = 0; i < stop; i++)
                        {
                            var u = keys.Res[start + i] * re - keys.Ims[start + i] * im;
                            var v = keys.Res[start + i] * im + keys.Ims[start + i] * re;

                            keys.Keys[next++] = Key(u, v, shift);
                        }
                    }
                    else
                    {
                        for (var i = 0; i < combinationCount; i++)
                        {
                            var u = keys.Res[start + i] * re - keys.Ims[start + i] * im;
                            var v = keys.Res[start + i] * im + keys.Ims[start + i] * re;

                            keys.Res[start + choice * combinationCount + i] = u;
                            keys.Ims[start + choice * combinationCount + i] = v;
                        }
                    }
                }

                combinationCount *= 2 * power + 1;
            }
        }

        // The key of x = 2 * u * v
        private static ulong Key(long u, long v, int shift)
        {
            var absU = (ulong)(u < 0 ? -u : u);
            var absV = (ulong)(v < 0 ? -v : v);
            var low = absU * absV;
            var high = MultiplyHigh(absU, absV) << 1 | low >> 63;

            low <<= 1;

            return shift == 0 ? low : high << (64 - shift) | low >> shift;
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
            var largest = sorted[end - 1];
            var sumIndex = i + 1;

            // No sum past the largest key can be in the group
            for (var j = i + 1; j < end && sorted[j] + y <= largest; j++)
            {
                var sum = sorted[j] + y;

                // The sum only grows as x does, so the place to look for it never moves backwards
                while (sorted[sumIndex] < sum)
                {
                    sumIndex++;
                }

                if (sorted[sumIndex] - sum <= 1)
                {
                    flags[starts[i]] = 1;
                    Atomic.Add(ref flagCount[0], 1);

                    break;
                }
            }
        }

        private struct BatchViews
        {
            public ArrayView<int> GroupStarts;
            public ArrayView<ulong> Plains;
            public ArrayView<int> FactorStarts;
            public ArrayView<int> Factors;
            public ArrayView<int> LargeIndexes;
            public ArrayView<ulong> LargePrimes;
            public ArrayView<long> LargeAs;
            public ArrayView<long> LargeBs;
        }

        private struct KeyViews
        {
            public ArrayView<ulong> Keys;
            public ArrayView<long> Res;
            public ArrayView<long> Ims;
            public ArrayView<int> Flags;
            public ArrayView<int> FlagCount;
        }
    }
}
