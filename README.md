# Non-Parker Square Finder

A program to search for a magic square of squares, informally known as a "Non-Parker Square" (http://theparkersquare.com/). This app is a .NET 8 console app. Run it with `dotnet run -c Release` from the [cSharp folder](./cSharp), or from Visual Studio.

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

## Results

Unfortunately, the search came up empty. The space searched was every m up to 922,337,203,685,477,580,700 (100 times the size of C# long). That is 30,370,004,999 squares, with 110,040,967,697 pairs of m and x where m - x and m + x are both squares. None of them had a second value y that completed the square.

The [results folder](./cSharp/Results) contains 2 files from an earlier version of the search, which stopped at 922,335,506,689. The current version reproduces every pair in them but does not write files itself. The mfile.txt is every square between 0 and 922,335,506,689. The xyfile.txt is every pair of numbers where: 

1. one is a square
2. if you add them together or subtract the higher from the lower, the result is a square
3. the bigger number is smaller than 922,335,506,689

## Notes for future explorers using this program

The size of the search is `totalSearchSpace` at the top of the program file. It is a `UInt128`, so it can be raised as far as `Int128.MaxValue`, which is about 1.7 x 10^38. Past that the program can no longer add two x values together, because the sum can be nearly twice m. In practice the time the search takes becomes the limit long before that.

On an Intel i9-13900K held to 12 threads, searching up to 9,223,372,036,854,775,807 takes 1 minute 24 seconds, and the current limit of 100 times that takes 19 minutes. So raising the limit 100 times made the search take about 14 times as long. It uses every core by default. To use fewer, set the `DOTNET_PROCESSOR_COUNT` environment variable before running it.

For comparison, the earlier version tested every pair of squares and took about 4.5 hours to reach 922,335,506,689, a limit a billion times smaller.
