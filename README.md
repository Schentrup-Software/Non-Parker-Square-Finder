# Non-Parker Square Finder

A program to search for a magic square of squares, informally known as a "Non-Parker Square" (http://theparkersquare.com/). This app is .NET 5 console app and can be run using Visual Studio.

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
1. Calculate every square between 0 and the max search space
1. Calculate each and every pairing of squares between 0 and the upper limit of the search space
1. Calculate the difference between each pairing
    1. The smaller number of the pair will be your m
    1. The larger will be m + (x or y)
    1. The difference will be your (x or y)
1. Subtract the (x or y) value from m
1. Check the difference to see if it is a square
1. If it is, save the value of m and (x or y) as a pair
1. This creates a list of pairings of possible m and (x or y) values
1. Then group together all the m values
1. Pair every combination of (x or y) values for each m
1. Check each combination of x, y, and m to see if they work

## Results

Unfortunately, the search came up empty. The space search was every square smaller than 922,335,506,689. The [results folder](./Results) contains 2 files. The mfile.txt is every square between 0 and 922,335,506,689. The xyfile.txt is every pair of numbers where: 

1. one is a square
2. if you add them together or subtract the higher from the lower, the result is a square
3. the bigger number is smaller than 922,335,506,689

## Notes for future explorers using this program

This program can be run with larger numbers. It just requires you to change the `totalSearchSpace` const at the top of the program file. That number can be increased all the way up to 9,223,372,036,854,775,807 (size of C# long) with enough ram and CPU. 922,335,506,689 has 960,382 perfect squares between it and 0. `long.MaxValue` has about 1,000 times that many.

Running the program in VS (which is not a great benchmark super well but should get us an order of magnitude close enough) took about 4.5 hours. Doing this on the full set then will require about 4500 hours (187 days) since it is not parallelized. More work will have to be done to make it parallelizable before it is reasonable to run it on the full set.
