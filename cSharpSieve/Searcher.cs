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
        // Every odd prime up to the square root of the largest c. A and B are 0 unless P = A * A + B * B
        private readonly (ulong P, long A, long B)[] primes;

        // The product of the odd primes found in each c of the segment, so what is left over can be worked out
        private readonly ulong[] products;

        // The primes in each c that split, once for each time they divide it, as linked lists through nexts
        private readonly int[] heads;
        private int[] nexts;
        private int[] splitPrimes;
        private int splitCount;

        // The factors of the c being worked on that split: a + bi and how many times it divides c
        private readonly (long A, long B, int Power)[] factors = new (long A, long B, int Power)[64];
        private int factorCount;
        private ulong factorProduct;
        private ulong largePrime;

        private readonly (long Re, long Im)[] powers = new (long Re, long Im)[128];
        private readonly (long Re, long Im)[] choices = new (long Re, long Im)[128];
        private (long Re, long Im)[] combinations = new (long Re, long Im)[16];

        private UInt128[] group = new UInt128[16];
        private readonly List<(UInt128 M, UInt128 X, UInt128 Y)> found = new();

        public long PairCount { get; private set; }

        public Searcher((ulong P, long A, long B)[] primes, int segmentSize)
        {
            this.primes = primes;

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
                    Build(c, size);
                    Array.Sort(group, 0, size);
                    FindSquares((UInt128)c * c, group, size, found);
                }

                PairCount += size;
            }

            return found;
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
                    factors[factorCount++] = (prime.A, prime.B, 1);
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

        // Lists the x values of the c last given to Factor in group
        internal void Build(ulong c, int size)
        {
            if (largePrime != 0)
            {
                var (a, b) = TwoSquares.Split(largePrime);

                factors[factorCount++] = (a, b, 1);
                factorProduct *= largePrime;
            }

            if (group.Length < size)
            {
                Array.Resize(ref group, size * 2);
            }

            if (combinations.Length < size)
            {
                Array.Resize(ref combinations, size * 2);
            }

            // The part of c with none of these primes in it can't be split up, so it multiplies everything
            combinations[0] = ((long)(c / factorProduct), 0);

            var combinationCount = 1;
            var groupSize = 0;

            for (var factor = 0; factor < factorCount; factor++)
            {
                var (a, b, power) = factors[factor];

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

                if (factor < factorCount - 1)
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
