using System;
using System.Collections.Generic;
using System.Linq;

namespace NonParkerSquareFinder
{
    // With m = c * c, both m - x and m + x are squares exactly when c * c = u * u + v * v and x = 2 * u * v,
    // since then m - x = (u - v)^2 and m + x = (u + v)^2. So the x values for an m come straight from the
    // Pythagorean triples with hypotenuse c, and nothing has to be tested for being a square.
    //
    // Every triple is k times a primitive one. To find the ones that land in a range of c without holding
    // them all in memory, the primitive triples with a small c are kept in a table and multiplied by the
    // large k, and the few small k left over generate the primitive triples they need directly.
    internal class Searcher
    {
        private readonly (ulong C, ulong X)[] smallTriples;
        private readonly ulong directScales;

        // The x values found for each c in the segment, as linked lists running through nexts
        private readonly int[] heads;
        private int[] nexts;
        private ulong[] xs;
        private int count;

        private ulong[] group = new ulong[16];
        private readonly List<(ulong M, ulong X, ulong Y)> found = new();

        public long PairCount { get; private set; }

        // smallTriples has to hold every primitive triple that still fits in the search space when
        // multiplied by more than directScales
        public Searcher((ulong C, ulong X)[] smallTriples, ulong directScales, int segmentSize)
        {
            this.smallTriples = smallTriples;
            this.directScales = directScales;

            heads = new int[segmentSize];
            nexts = new int[segmentSize * 4];
            xs = new ulong[segmentSize * 4];
        }

        // Every primitive triple with c up to maxC, smallest c first
        public static (ulong C, ulong X)[] SmallTriples(ulong maxC)
        {
            return PrimitiveTriples(5, maxC).OrderBy(triple => triple.C).ToArray();
        }

        // Checks every m = c * c for c from firstC to lastC, which can cover at most one segment
        public List<(ulong M, ulong X, ulong Y)> Search(ulong firstC, ulong lastC)
        {
            found.Clear();
            Fill(firstC, lastC);

            for (var c = firstC; c <= lastC; c++)
            {
                var size = Collect((int)(c - firstC));

                // x - y, y, x and x + y are four different values
                if (size >= 4)
                {
                    Array.Sort(group, 0, size);
                    FindSquares(c * c, group, size, found);
                }
            }

            PairCount += count;

            return found;
        }

        internal void Fill(ulong firstC, ulong lastC)
        {
            Array.Fill(heads, -1);
            count = 0;

            foreach (var (c, x) in smallTriples)
            {
                if (c * (directScales + 1) > lastC)
                {
                    break;
                }

                for (var k = Math.Max(directScales + 1, (firstC + c - 1) / c); k * c <= lastC; k++)
                {
                    Add(k * c - firstC, x * k * k);
                }
            }

            for (ulong k = 1; k <= directScales; k++)
            {
                foreach (var (c, x) in PrimitiveTriples((firstC + k - 1) / k, lastC / k))
                {
                    Add(k * c - firstC, x * k * k);
                }
            }
        }

        // Copies the x values for one c into group and returns how many there are
        internal int Collect(int offset)
        {
            var size = 0;

            for (var i = heads[offset]; i >= 0; i = nexts[i])
            {
                if (size == group.Length)
                {
                    Array.Resize(ref group, size * 2);
                }

                group[size++] = xs[i];
            }

            return size;
        }

        internal ReadOnlySpan<ulong> Group => group;

        // sortedXs holds every x where m - x and m + x are squares, so x and y complete a magic square
        // when x + y and x - y are in there too
        internal static void FindSquares(ulong m, ulong[] sortedXs, int size, List<(ulong M, ulong X, ulong Y)> found)
        {
            for (var i = 0; i < size - 1; i++)
            {
                var y = sortedXs[i];
                var sumIndex = i + 2;

                for (var j = i + 1; j < size; j++)
                {
                    var x = sortedXs[j];

                    // x + y only grows as x does, so the place to look for it never moves backwards
                    while (sumIndex < size && sortedXs[sumIndex] < x + y)
                    {
                        sumIndex++;
                    }

                    if (sumIndex == size)
                    {
                        break;
                    }

                    if (sortedXs[sumIndex] == x + y && Array.BinarySearch(sortedXs, 0, j, x - y) >= 0)
                    {
                        found.Add((m, x, y));
                    }
                }
            }
        }

        // Every primitive triple with c from minC to maxC, as c and its x
        internal static IEnumerable<(ulong C, ulong X)> PrimitiveTriples(ulong minC, ulong maxC)
        {
            for (var p = Math.Max(2, Sqrt(minC / 2)); p * p < maxC; p++)
            {
                var pp = p * p;
                var minQ = pp < minC ? CeilingSqrt(minC - pp) : 1;
                var maxQ = Math.Min(p - 1, Sqrt(maxC - pp));

                // The triple is only primitive when one of p and q is even and they share no factor
                if ((p + minQ) % 2 == 0)
                {
                    minQ++;
                }

                for (var q = minQ; q <= maxQ; q += 2)
                {
                    if (Gcd(p, q) == 1)
                    {
                        var qq = q * q;

                        yield return (pp + qq, 2 * (pp - qq) * (2 * p * q));
                    }
                }
            }
        }

        public static ulong Sqrt(ulong n)
        {
            var root = (ulong)Math.Sqrt(n);

            // Math.Sqrt works on a double, which can't hold a large n exactly
            while (root * root > n)
            {
                root--;
            }

            while ((root + 1) * (root + 1) <= n)
            {
                root++;
            }

            return root;
        }

        private static ulong CeilingSqrt(ulong n)
        {
            var root = Sqrt(n);

            return root * root < n ? root + 1 : root;
        }

        private static ulong Gcd(ulong a, ulong b)
        {
            while (b != 0)
            {
                (a, b) = (b, a % b);
            }

            return a;
        }

        private void Add(ulong offset, ulong x)
        {
            if (count == xs.Length)
            {
                Array.Resize(ref xs, count * 2);
                Array.Resize(ref nexts, count * 2);
            }

            xs[count] = x;
            nexts[count] = heads[offset];
            heads[offset] = count++;
        }
    }
}
