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

The sieve version also hands the pair checking to an NVIDIA graphics card when the machine has one. It talks to the card through the ILGPU package, which the build downloads, and the card needs nothing installed beyond its normal driver. It gets the x values cut down to 64 bits and looks for any m where the sum of two of them is another one, or one more than another one to allow for the bits that were cut off. That can pick out an m that does not really work, but it can't miss one that does, and anything it picks out gets the full check on the CPU. Without a card the CPU does all of it.

## Results

Unfortunately, the search came up empty. The space searched was every m up to 922,337,203,685,477,580,700 (100 times the size of C# long). That is 30,370,004,999 squares, with 110,040,967,697 pairs of m and x where m - x and m + x are both squares. None of them had a second value y that completed the square. Both versions were run over the whole space and agree on those counts.

The [results folder](./cSharpSimple/Results) contains 2 files from an earlier version of the search, which stopped at 922,335,506,689. Both current versions reproduce every pair in them but do not write files themselves. The mfile.txt is every square between 0 and 922,335,506,689. The xyfile.txt is every pair of numbers where: 

1. one is a square
2. if you add them together or subtract the higher from the lower, the result is a square
3. the bigger number is smaller than 922,335,506,689

## Notes for future explorers using this program

The size of the search is `totalSearchSpace` at the top of each program file. It is a `UInt128`. The simple version can be raised as far as `Int128.MaxValue`, about 1.7 x 10^38, and the sieve version as far as `long.MaxValue` squared, about 8.5 x 10^37. In practice the time the search takes becomes the limit long before either.

These are the times on an Intel i9-13900K held to 12 threads, with and without a GeForce RTX 4090:

| Search limit on m | Simple | Sieve, CPU only | Sieve with the card |
|---|---|---|---|
| 9,223,372,036,854,775,807 | 1 min 24 sec | 57 sec | 26 sec |
| 100 times that (the current limit) | 19 min 3 sec | 12 min 30 sec | 5 min 0 sec |

The gap between simple and sieve widens as the limit goes up. On a sample of blocks near a limit 10,000 times higher again, the sieve version without the card was about 3.7 times faster.

With the card, the CPU's share of the work is what sets the speed. The card was only about 40% busy during these runs, so more CPU threads should make it faster still. Both versions use every core by default. To use fewer, set the `DOTNET_PROCESSOR_COUNT` environment variable before running.

For comparison, the earlier version tested every pair of squares and took about 4.5 hours to reach 922,335,506,689, a limit a billion times smaller.
