# Non-Parker Square Finder

A program to search for a magic square of squares, informally known as a "Non-Parker Square" (http://theparkersquare.com/). There are two versions, both .NET 8 console apps. [cSharpSieve](./cSharpSieve) is the faster one and uses an NVIDIA graphics card if there is one, and [cSharpSimple](./cSharpSimple) is shorter and easier to follow. Run either with `dotnet run -c Release` from its folder, or from Visual Studio.

## Methodology

We are using the following formula to search for the square:

```
m+x        m-(x+y)       m+y
m-(x-y)    m             m+(x-y)
m-y        m+(x+y)       m-x
```

[source](http://ken.duisenberg.com/potw/archive/arch00/000504sol.html)

It is trivial to see that any choice of unique m, x, and y where m > (x + y) will get you a magic square.

From this, we can figure out that m must be a perfect square.

The app works like this:

1. Pick an upper limit on the search space
1. Go through every m up to the limit. Since m is a square, that means every c where m = c²
1. For each m, list every x where m - x and m + x are both squares
    1. This happens exactly when c² = u² + v² and x = 2uv, because then m - x = (u - v)² and m + x = (u + v)²
    1. So the x values come from the Pythagorean triples with hypotenuse c
    1. Those triples can be generated directly, so no x has to be tested to see if it works
1. In a magic square of squares, x, y, x + y and x - y all have to be in that list for the same m
1. So for each m, check every pair in its list to see if their sum and their difference are in the list too

The two versions only differ in how they find the triples for a block of c values:

- The simple version keeps a table of small triples and goes through it for every block to see which multiples of them land in the block. The table has to grow with the search limit, and most of it misses any one block, so this gets slower the further the search goes.
- The sieve version works out the prime factors of every c in the block with a sieve. A c only has triples if some of its prime factors are 1 more than a multiple of 4. Each of those primes is a sum of two squares in exactly one way, and the triples for c are built by multiplying those pieces together. It also skips any c with fewer than four x values, since those can't make a square.

The sieve version also hands most of the work to an NVIDIA graphics card when the machine has one. It talks to the card through the ILGPU package, which the build downloads, and the card needs nothing installed beyond its normal driver. The CPU still runs the sieve and gathers the factors of each c. The card then does three things:

- It splits the one prime factor too large for the sieve into its two squares.
- It multiplies out the x values for each m, keeping each one cut down to 64 bits.
- It looks for any m where the sum of two of those is another one, or one more than another one to allow for the bits that were cut off.

That last step can pick out an m that does not really work, but it can't miss one that does, and anything it picks out gets the full check on the CPU. When the program starts, it has the card work out the x values for the largest numbers in the search and compares them with the CPU's. If they differ, or there is no card, the CPU does all of it.

## Results

Unfortunately, the search came up empty. The space searched was every m up to 92,233,720,368,547,758,070,000 (10,000 times the size of C# long). That is 303,700,049,997 squares, with 1,211,705,983,486 pairs of m and x where m - x and m + x are both squares. None of them had a second value y that completed the square.

Both versions were run up to 922,337,203,685,477,580,700 and agree on the counts to there. The rest was run with the sieve version and a graphics card. Its count of pairs matches a separate count made by adding up lattice points under circles, which shares no code or method with the search.

### How this compares with other searches

Christian Boyer keeps a record of searches for this square at [multimagie.com](http://www.multimagie.com/English/SquaresOfSquaresSearch.htm). Two of the results there are worth setting beside this one:

- Lee Morgenstern found no solution with a step value up to 2.4 x 10^21, counting every progression of odd squares (2014). The step value is the x or y above. The smaller of the two is less than half of m, so that covers every m below about 4.8 x 10^21.
- Duncan Buell searched for a "magic hourglass", which is 7 of the 9 squares, with m up to 2.5 x 10^25 (1999). His search assumed that the three entries in each line through the centre share no common factor, which [a later paper](https://members.loria.fr/PZimmermann/papers/squares.pdf) points out a solution does not have to satisfy.

This search checks every m up to its limit with no such condition. Up to about 4.8 x 10^21 it agrees with Morgenstern's result. From there to 9.2 x 10^22 it covers cases that, going by that page, had only been reached by searches with extra conditions. The page may not list everything that has been done.

The [results folder](./cSharpSimple/Results) contains 2 files from an earlier version of the search, which stopped at 922,335,506,689. Both current versions reproduce every pair in them but do not write files themselves. The mfile.txt is every square between 0 and 922,335,506,689. The xyfile.txt is every pair of numbers where: 

1. one is a square
2. if you add them together or subtract the higher from the lower, the result is a square
3. the bigger number is smaller than 922,335,506,689

## Notes for future explorers using this program

The size of the search is `totalSearchSpace` at the top of each program file. It is a `UInt128`. The sieve version is set to 10,000 times `long.MaxValue`, and the simple version is still set to 100 times. The simple version can be raised as far as `Int128.MaxValue`, about 1.7 x 10^38, and the sieve version as far as `long.MaxValue` squared, about 8.5 x 10^37. In practice the time the search takes becomes the limit long before either.

These are the times on an Intel i9-13900K held to 12 threads, with and without a GeForce RTX 4090:

| Search limit on m | Simple | Sieve, CPU only | Sieve with the card |
|---|---|---|---|
| 9,223,372,036,854,775,807 | 1 min 24 sec | 57 sec | 9 sec |
| 100 times that | 19 min 3 sec | 12 min 30 sec | 1 min 48 sec |
| 10,000 times that (the sieve version's limit) | not run | not run | 27 min 22 sec |

The gap between simple and sieve widens as the limit goes up. On a sample of blocks near 9.2 x 10^24, the sieve version without the card was about 3.7 times faster than the simple version.

Going further gets slower in two ways. Each 100 times in the limit is 10 times as many squares, and each square takes longer because it has more x values. Timing sample stretches with the card suggests about 6 hours to reach 9.2 x 10^24 and about 3 days to reach 9.2 x 10^26 on this machine. Past about 9 x 10^33 the card drops out, because of a size limit in how the factors are sent to it, and the CPU takes over.

With the card, the CPU and the card are close to balanced. The card was between half and three quarters busy during these runs, so more CPU threads should make it a little faster before the card becomes the limit. Both versions use every core by default. To use fewer, set the `DOTNET_PROCESSOR_COUNT` environment variable before running.

For comparison, the earlier version tested every pair of squares and took about 4.5 hours to reach 922,335,506,689, a limit a billion times smaller.
