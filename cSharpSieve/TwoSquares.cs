using System;
using System.Numerics;

namespace NonParkerSquareFinder
{
    internal static class TwoSquares
    {
        // Finds the a > b > 0 with a * a + b * b = p, for a prime p that is 1 more than a multiple of 4
        public static (long A, long B) Split(ulong p)
        {
            // Euclid's algorithm on p and a square root of -1 passes through a and then b
            var previous = p;
            var current = RootOfMinusOne(p);

            while ((UInt128)current * current > p)
            {
                (previous, current) = (current, previous % current);
            }

            return ((long)current, (long)(previous % current));
        }

        // n to the power (p - 1) / 4 is a square root of -1 whenever n is not a square mod p, and half of
        // all n are not, so this tries n = 2, 3, 4... until one works
        private static ulong RootOfMinusOne(ulong p)
        {
            // Everything is kept in Montgomery form, where a is stored as a * 2^64 mod p. That lets
            // Multiply reduce mod p without dividing a 128 bit number.
            var inverse = p;

            for (var i = 0; i < 5; i++)
            {
                inverse *= 2UL - p * inverse;
            }

            var negativeInverse = 0UL - inverse;
            var one = (ulong.MaxValue - p + 1) % p;
            var minusOne = p - one;
            var exponent = (p - 1) / 4;
            var n = one;

            while (true)
            {
                n += one;

                if (n >= p)
                {
                    n -= p;
                }

                var root = one;

                for (var bit = 63 - BitOperations.LeadingZeroCount(exponent); bit >= 0; bit--)
                {
                    root = Multiply(root, root, p, negativeInverse);

                    if ((exponent >> bit & 1) != 0)
                    {
                        root = Multiply(root, n, p, negativeInverse);
                    }
                }

                if (Multiply(root, root, p, negativeInverse) == minusOne)
                {
                    return Multiply(root, 1, p, negativeInverse);
                }
            }
        }

        // a * b / 2^64 mod p, which needs p to be odd and under 2^63
        private static ulong Multiply(ulong a, ulong b, ulong p, ulong negativeInverse)
        {
            var high = Math.BigMul(a, b, out var low);

            // Adding this multiple of p clears the low 64 bits, so dividing by 2^64 just drops them
            var cleared = Math.BigMul(low * negativeInverse, p, out _);
            var result = high + cleared + (low != 0 ? 1UL : 0);

            return result >= p ? result - p : result;
        }
    }
}
