using System;
using System.Collections.Generic;
using System.Numerics;

namespace NonParkerSquareFinder
{
    // With m = c * c, both m - x and m + x are squares exactly when c * c = u * u + v * v and x = 2 * u * v,
    // since then m - x = (u - v)^2 and m + x = (u + v)^2. So the x values for an m come from the ways of
    // writing c * c as a sum of two squares.
    //
    // Those ways are decided by the prime factors of c that are 1 more than a multiple of 4. Each such
    // prime is a * a + b * b in exactly one way, which splits it into (a + bi)(a - bi), and every u + vi
    // with u * u + v * v = c * c is a product of those pieces. So each segment of c values is factored
    // with a sieve, and the x values of a c are multiplied together from its factors.
    internal class Searcher
    {
        private const int BatchSize = 1 << 22;

        // A few huge groups hold up the card for as long as it takes to get through the biggest one, so
        // they wait in a batch of their own, which fills slowly and has many of them to work on at once
        private const int SmallGroupSize = 64;

        // Every odd prime up to the square root of the largest c. A and B are 0 unless P = A * A + B * B
        private readonly (ulong P, long A, long B)[] primes;

        // The product of the odd primes found in each c of the segment, so what is left over can be worked out
        private readonly ulong[] products;

        // The primes in each c that split, once for each time they divide it, as linked lists through nexts
        private readonly int[] heads;
        private int[] nexts;
        private int[] splitPrimes;
        private int splitCount;

        // The sieved factors of the c being worked on that split: the place of each in primes and how
        // many times it divides c
        private readonly (int Prime, int Power)[] factors = new (int Prime, int Power)[64];
        private int factorCount;
        private ulong factorProduct;
        private ulong largePrime;

        private readonly (long Re, long Im)[] powers = new (long Re, long Im)[128];
        private readonly (long Re, long Im)[] choices = new (long Re, long Im)[128];
        private (long Re, long Im)[] combinations = new (long Re, long Im)[16];

        private UInt128[] group = new UInt128[16];
        private readonly List<(UInt128 M, UInt128 X, UInt128 Y)> found = new();

        // Null when there is no graphics card, which leaves the whole pair check to FindSquares
        private readonly GpuChecker gpu;

        // The m waiting to go to the graphics card
        private readonly GpuBatch smallGroups = new();
        private readonly GpuBatch largeGroups = new();

        private Searcher fullChecker;

        public long PairCount { get; private set; }

        internal long RecheckCount { get; private set; }

        public Searcher((ulong P, long A, long B)[] primes, int segmentSize, GpuChecker gpu)
        {
            this.primes = primes;
            this.gpu = gpu;

            products = new ulong[segmentSize];
            heads = new int[segmentSize];
            nexts = new int[segmentSize * 2];
            splitPrimes = new int[segmentSize * 2];
        }

        // Every odd prime that can be the smaller factor of a c up to maxC
        public static (ulong P, long A, long B)[] Primes(ulong maxC)
        {
            var limit = (long)Sqrt(maxC);
            var composite = new bool[limit + 1];
            var primes = new List<(ulong P, long A, long B)>();

            for (var p = 3L; p <= limit; p += 2)
            {
                if (composite[p])
                {
                    continue;
                }

                var (a, b) = p % 4 == 1 ? TwoSquares.Split((ulong)p) : (0, 0);
                primes.Add(((ulong)p, a, b));

                for (var multiple = p * p; multiple <= limit; multiple += 2 * p)
                {
                    composite[multiple] = true;
                }
            }

            return primes.ToArray();
        }

        // Checks every m = c * c for c from firstC to lastC, which can cover at most one segment
        public List<(UInt128 M, UInt128 X, UInt128 Y)> Search(ulong firstC, ulong lastC)
        {
            found.Clear();
            Sieve(firstC, lastC);

            for (var c = firstC; c <= lastC; c++)
            {
                var size = Factor(c, (int)(c - firstC));

                // x - y, y, x and x + y are four different values
                if (size >= 4)
                {
                    if (gpu == null)
                    {
                        Build(c, size);
                        Array.Sort(group, 0, size);
                        FindSquares((UInt128)c * c, group, size, found);
                    }
                    else
                    {
                        var batch = size <= SmallGroupSize ? smallGroups : largeGroups;

                        batch.Add(c, size, Plain(c), factors.AsSpan(0, factorCount), largePrime);
                    }
                }

                PairCount += size;
            }

            if (smallGroups.KeyCount >= BatchSize)
            {
                CheckBatch(smallGroups);
            }

            if (largeGroups.KeyCount >= BatchSize)
            {
                CheckBatch(largeGroups);
            }

            return found;
        }

        // Checks whatever is still waiting for the graphics card after the last segment
        public List<(UInt128 M, UInt128 X, UInt128 Y)> Finish()
        {
            found.Clear();
            CheckBatch(smallGroups);
            CheckBatch(largeGroups);

            return found;
        }

        // Has the card work out the x values for the c at the top of the search, where the numbers are
        // biggest, and says whether they all match the ones worked out here
        public static bool CardAgrees((ulong P, long A, long B)[] primes, GpuChecker gpu, ulong maxC)
        {
            const int count = 1 << 16;

            var firstC = maxC < count ? 0 : maxC - count + 1;
            var searcher = new Searcher(primes, count, null);
            var batch = new GpuBatch();
            var expected = new List<ulong>();
            var leftForCpu = new List<int>();

            searcher.Sieve(firstC, maxC);

            for (var c = firstC; c <= maxC; c++)
            {
                var size = searcher.Factor(c, (int)(c - firstC));

                if (size >= 4)
                {
                    batch.Add(c, size, searcher.Plain(c), searcher.factors.AsSpan(0, searcher.factorCount), searcher.largePrime);
                    searcher.Build(c, size);

                    for (var i = 0; i < size; i++)
                    {
                        expected.Add((ulong)(searcher.group[i] >> gpu.Shift));
                    }
                }
            }

            if (batch.GroupCount == 0)
            {
                return true;
            }

            var keys = gpu.BuiltKeys(batch, leftForCpu);

            for (var group = 0; group < batch.GroupCount; group++)
            {
                var start = batch.GroupStarts[group];
                var size = batch.GroupStarts[group + 1] - start;

                if (leftForCpu.Contains(group))
                {
                    continue;
                }

                // The two lists come out in different orders
                Array.Sort(keys, start, size);
                expected.Sort(start, size, null);

                for (var i = start; i < start + size; i++)
                {
                    if (keys[i] != expected[i])
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        internal void Sieve(ulong firstC, ulong lastC)
        {
            var size = (int)(lastC - firstC + 1);

            Array.Fill(products, 1UL, 0, size);
            Array.Fill(heads, -1, 0, size);
            splitCount = 0;

            for (var index = 0; index < primes.Length; index++)
            {
                var p = primes[index].P;
                var splits = primes[index].A != 0;

                if (p * p > lastC)
                {
                    break;
                }

                // Going through the multiples of p, then of p * p and so on, visits a c once for each
                // time p divides it
                for (var power = p; ; power *= p)
                {
                    for (var c = Math.Max(power, (firstC + power - 1) / power * power); c <= lastC; c += power)
                    {
                        var offset = (int)(c - firstC);

                        products[offset] *= p;

                        if (splits)
                        {
                            Add(offset, index);
                        }
                    }

                    if (power > lastC / p)
                    {
                        break;
                    }
                }
            }
        }

        // Gathers the factors of one c in the segment and returns how many x values it has
        internal int Factor(ulong c, int offset)
        {
            factorCount = 0;
            factorProduct = 1;

            var count = 1L;
            var lastPrime = -1;

            for (var i = heads[offset]; i >= 0; i = nexts[i])
            {
                var prime = primes[splitPrimes[i]];

                factorProduct *= prime.P;

                // The entries for one prime were added one after the other
                if (splitPrimes[i] == lastPrime)
                {
                    count = count / (2 * factors[factorCount - 1].Power + 1) * (2 * factors[factorCount - 1].Power + 3);
                    factors[factorCount - 1].Power++;
                }
                else
                {
                    count *= 3;
                    factors[factorCount++] = (splitPrimes[i], 1);
                    lastPrime = splitPrimes[i];
                }
            }

            // Once the sieved primes and the twos are divided out of c, at most one prime is left
            largePrime = (c >> BitOperations.TrailingZeroCount(c)) / products[offset];

            if (largePrime % 4 == 1 && largePrime > 1)
            {
                count *= 3;
            }
            else
            {
                largePrime = 0;
            }

            // Each x comes from two of the ways of writing c * c as a sum of two squares, apart from
            // c * c + 0 which gives none
            return (int)((count - 1) / 2);
        }

        // The part of the c last given to Factor with no prime that splits in it. It can't be split up,
        // so it multiplies everything
        internal ulong Plain(ulong c)
        {
            return c / (largePrime == 0 ? factorProduct : factorProduct * largePrime);
        }

        // Lists the x values of the c last given to Factor in group
        internal void Build(ulong c, int size)
        {
            // The large prime goes after the sieved ones
            var allFactors = largePrime == 0 ? factorCount : factorCount + 1;

            if (group.Length < size)
            {
                Array.Resize(ref group, size * 2);
            }

            if (combinations.Length < size)
            {
                Array.Resize(ref combinations, size * 2);
            }

            combinations[0] = ((long)Plain(c), 0);

            var combinationCount = 1;
            var groupSize = 0;

            for (var factor = 0; factor < allFactors; factor++)
            {
                var (a, b) = factor < factorCount ? (primes[factors[factor].Prime].A, primes[factors[factor].Prime].B) : TwoSquares.Split(largePrime);
                var power = factor < factorCount ? factors[factor].Power : 1;

                // A prime dividing c a number of times divides c * c twice as many times, and each of
                // those can go to a + bi or to a - bi
                powers[0] = (1, 0);

                for (var i = 1; i <= 2 * power; i++)
                {
                    powers[i] = Multiply(powers[i - 1], (a, b));
                }

                for (var i = 0; i <= 2 * power; i++)
                {
                    choices[i] = Multiply(powers[i], (powers[2 * power - i].Re, -powers[2 * power - i].Im));
                }

                if (factor < allFactors - 1)
                {
                    for (var choice = 2 * power; choice >= 0; choice--)
                    {
                        for (var i = 0; i < combinationCount; i++)
                        {
                            combinations[choice * combinationCount + i] = Multiply(combinations[i], choices[choice]);
                        }
                    }

                    combinationCount *= 2 * power + 1;
                }
                else
                {
                    // The second half of the full list would be the first half with every a + bi and
                    // a - bi swapped, which gives the same x values again, so the last factor stops halfway
                    for (var choice = 0; choice <= power; choice++)
                    {
                        var stop = choice < power ? combinationCount : combinationCount / 2;

                        for (var i = 0; i < stop; i++)
                        {
                            var (u, v) = Multiply(combinations[i], choices[choice]);

                            group[groupSize++] = 2 * (UInt128)(ulong)Math.Abs(u) * (ulong)Math.Abs(v);
                        }
                    }
                }
            }
        }

        internal ReadOnlySpan<UInt128> Group => group;

        // sortedXs holds every x where m - x and m + x are squares, so x and y complete a magic square
        // when x + y and x - y are in there too
        internal static void FindSquares(UInt128 m, UInt128[] sortedXs, int size, List<(UInt128 M, UInt128 X, UInt128 Y)> found)
        {
            for (var i = 0; i < size - 1; i++)
            {
                var y = sortedXs[i];
                var sumIndex = i + 2;

                for (var j = i + 1; j < size; j++)
                {
                    var x = sortedXs[j];
                    var sum = x + y;

                    // The sum only grows as x does, so the place to look for it never moves backwards
                    while (sumIndex < size && sortedXs[sumIndex] < sum)
                    {
                        sumIndex++;
                    }

                    if (sumIndex == size)
                    {
                        break;
                    }

                    if (sortedXs[sumIndex] == sum && Array.BinarySearch(sortedXs, 0, j, x - y) >= 0)
                    {
                        found.Add((m, x, y));
                    }
                }
            }
        }

        public static ulong Sqrt(UInt128 n)
        {
            var root = (ulong)Math.Sqrt((double)n);

            // Math.Sqrt works on a double, which can't hold a large n exactly
            while ((UInt128)root * root > n)
            {
                root--;
            }

            while ((UInt128)(root + 1) * (root + 1) <= n)
            {
                root++;
            }

            return root;
        }

        private static (long Re, long Im) Multiply((long Re, long Im) a, (long Re, long Im) b)
        {
            return (a.Re * b.Re - a.Im * b.Im, a.Re * b.Im + a.Im * b.Re);
        }

        // Sends a batch to the graphics card and gives each c it picks out the full check
        private void CheckBatch(GpuBatch batch)
        {
            if (batch.GroupCount == 0)
            {
                return;
            }

            foreach (var index in gpu.Check(batch))
            {
                fullChecker ??= new Searcher(primes, 1, null);
                found.AddRange(fullChecker.Search(batch.Cs[index], batch.Cs[index]));
                RecheckCount++;
            }

            batch.Clear();
        }

        private void Add(int offset, int prime)
        {
            if (splitCount == splitPrimes.Length)
            {
                Array.Resize(ref splitPrimes, splitCount * 2);
                Array.Resize(ref nexts, splitCount * 2);
            }

            splitPrimes[splitCount] = prime;
            nexts[splitCount] = heads[offset];
            heads[offset] = splitCount++;
        }
    }
}
