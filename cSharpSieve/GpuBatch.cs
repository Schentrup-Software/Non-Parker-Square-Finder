using System;

namespace NonParkerSquareFinder
{
    // The m waiting to go to the graphics card. Each one is a group: the factors of its c that the card
    // needs to work out its x values, and the range of keys those x values will fill.
    internal class GpuBatch
    {
        public int GroupCount { get; private set; }
        public int KeyCount { get; private set; }
        public int FactorCount { get; private set; }
        public int LargeCount { get; private set; }

        public ulong[] Cs { get; private set; } = new ulong[1024];

        // The part of each c that has no prime that splits in it
        public ulong[] Plains { get; private set; } = new ulong[1024];

        // Where each group's keys and factors start, with one more entry to mark where the last group ends
        public int[] GroupStarts { get; private set; } = new int[1025];
        public int[] FactorStarts { get; private set; } = new int[1025];

        // A sieved prime that splits, as its place in the list of primes, with how many times it divides
        // c in the top 8 bits
        public int[] Factors { get; private set; } = new int[1024];

        // The one prime in c too big for the sieve, as a place in LargePrimes, or -1 when there is none
        public int[] LargeIndexes { get; private set; } = new int[1024];
        public ulong[] LargePrimes { get; private set; } = new ulong[1024];

        public void Add(ulong c, int size, ulong plain, ReadOnlySpan<(int Prime, int Power)> factors, ulong largePrime)
        {
            if (GroupCount + 1 == GroupStarts.Length)
            {
                Cs = Grow(Cs, GroupCount * 2);
                Plains = Grow(Plains, GroupCount * 2);
                LargeIndexes = Grow(LargeIndexes, GroupCount * 2);
                GroupStarts = Grow(GroupStarts, GroupCount * 2 + 1);
                FactorStarts = Grow(FactorStarts, GroupCount * 2 + 1);
            }

            if (FactorCount + factors.Length > Factors.Length)
            {
                Factors = Grow(Factors, (FactorCount + factors.Length) * 2);
            }

            if (LargeCount == LargePrimes.Length)
            {
                LargePrimes = Grow(LargePrimes, LargeCount * 2);
            }

            Cs[GroupCount] = c;
            Plains[GroupCount] = plain;
            LargeIndexes[GroupCount] = -1;

            foreach (var (prime, power) in factors)
            {
                Factors[FactorCount++] = prime | power << 24;
            }

            if (largePrime != 0)
            {
                LargeIndexes[GroupCount] = LargeCount;
                LargePrimes[LargeCount++] = largePrime;
            }

            GroupCount++;
            KeyCount += size;
            GroupStarts[GroupCount] = KeyCount;
            FactorStarts[GroupCount] = FactorCount;
        }

        public void Clear()
        {
            GroupCount = 0;
            KeyCount = 0;
            FactorCount = 0;
            LargeCount = 0;
        }

        private static T[] Grow<T>(T[] array, int length)
        {
            Array.Resize(ref array, length);

            return array;
        }
    }
}
